using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Reporting.Authoring;

/// <summary>A typed Studio filter applied to a dataset query or visual source.</summary>
public sealed record DesignerQueryFilter(
    string Id,
    string Column,
    string Kind,
    IReadOnlyList<string>? Values = null,
    string? Minimum = null,
    string? Maximum = null,
    string? ParameterName = null,
    string? ParameterOperator = null,
    string? AllValue = null);

/// <summary>
/// Builds parser-valid filtered SELECT sources. Studio-owned predicates carry a small marker so a
/// later edit can replace them without disturbing a hand-authored WHERE clause.
/// </summary>
public sealed partial class DesignerQueryFilterService
{
    private const string MarkerPrefix = "/* ETL-SQL-STUDIO-FILTER ";

    public string Apply(string source, IReadOnlyList<DesignerQueryFilter>? filters, bool asVisualSource = true)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("A dataset query or visual source is required.", nameof(source));

        var trimmed = source.Trim();
        var wrapped = HasOuterParentheses(trimmed);
        var query = wrapped ? trimmed[1..^1].Trim() : trimmed;
        if (!StartsWithQuery(query))
        {
            if (!SourceReferencePattern().IsMatch(query))
                throw new ArgumentException("The visual source is not a valid table or dataset reference.", nameof(source));
            query = $"SELECT * FROM {query}";
            wrapped = asVisualSource;
        }

        var desired = filters ?? [];
        var desiredIds = desired.Select(filter => filter.Id).ToHashSet(StringComparer.Ordinal);
        var retainedParameters = ReadManagedFilters(query)
            .Where(filter => filter.Kind.Equals("parameter", StringComparison.OrdinalIgnoreCase)
                && !desiredIds.Contains(filter.Id))
            .ToList();
        query = RemoveManagedPredicates(query);
        foreach (var filter in retainedParameters.Concat(desired))
        {
            var predicate = BuildPredicate(filter);
            if (predicate is null) continue;
            query = AppendManagedPredicate(query, filter, predicate);
        }

        ValidateQuery(query);
        return wrapped ? $"({query})" : query;
    }

    public string BuildCategoricalOptionSource(string source, string column)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("A slicer option source is required.", nameof(source));

        var trimmed = source.Trim();
        var unwrapped = HasOuterParentheses(trimmed) ? trimmed[1..^1].Trim() : trimmed;
        var identifier = QuoteIdentifier(column);
        string query;
        if (StartsWithQuery(unwrapped))
        {
            var clean = RemoveManagedPredicates(unwrapped).TrimEnd().TrimEnd(';');
            query = $"SELECT DISTINCT {identifier} FROM ({clean}) AS studio_options ORDER BY {identifier}";
        }
        else
        {
            if (!SourceReferencePattern().IsMatch(unwrapped))
                throw new ArgumentException("The slicer option source is not a valid table or dataset reference.", nameof(source));
            query = $"SELECT DISTINCT {identifier} FROM {unwrapped} ORDER BY {identifier}";
        }

        ValidateQuery(query);
        return $"({query})";
    }

    private static string? BuildPredicate(DesignerQueryFilter filter)
    {
        var column = QuoteIdentifier(filter.Column);
        return filter.Kind.Trim().ToLowerInvariant() switch
        {
            "categorical" => BuildCategorical(column, filter.Values),
            "number" => BuildRange(column, filter.Minimum, filter.Maximum, isDate: false),
            "date" => BuildRange(column, filter.Minimum, filter.Maximum, isDate: true),
            "parameter" => BuildParameter(column, filter),
            _ => throw new ArgumentException($"Unsupported Studio filter kind '{filter.Kind}'.")
        };
    }

    private static string? BuildCategorical(string column, IReadOnlyList<string>? values)
    {
        var selected = (values ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (selected.Count == 0) return null;
        var literals = selected.Select(SqlStringLiteral).ToList();
        return literals.Count == 1
            ? $"{column} = {literals[0]}"
            : $"{column} IN ({string.Join(", ", literals)})";
    }

    private static string? BuildRange(string column, string? minimum, string? maximum, bool isDate)
    {
        var min = NormalizeBound(minimum, isDate);
        var max = NormalizeBound(maximum, isDate);
        if (min is null && max is null) return null;
        if (min is not null && max is not null) return $"{column} BETWEEN {min} AND {max}";
        return min is not null ? $"{column} >= {min}" : $"{column} <= {max}";
    }

    private static string BuildParameter(string column, DesignerQueryFilter filter)
    {
        var parameter = NormalizeParameter(filter.ParameterName);
        return (filter.ParameterOperator ?? "equals").Trim().ToLowerInvariant() switch
        {
            "equals" when !string.IsNullOrWhiteSpace(filter.AllValue) =>
                $"({parameter} = {SqlStringLiteral(filter.AllValue)} OR {column} = {parameter})",
            "equals" => $"{column} = {parameter}",
            "minimum" => $"{column} >= {parameter}",
            "maximum" => $"{column} <= {parameter}",
            _ => throw new ArgumentException($"Unsupported parameter filter operator '{filter.ParameterOperator}'.")
        };
    }

    private static string? NormalizeBound(string? value, bool isDate)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (isDate)
        {
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new ArgumentException($"'{value}' is not an ISO date (yyyy-MM-dd).");
            return SqlStringLiteral(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            throw new ArgumentException($"'{value}' is not a valid numeric filter bound.");
        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static string AppendManagedPredicate(string query, DesignerQueryFilter filter, string predicate)
    {
        var insertAt = FindFirstTopLevelClause(query, ["GROUP", "HAVING", "WINDOW", "QUALIFY", "ORDER", "LIMIT", "OFFSET", "USING", "FOR"]);
        if (insertAt < 0) insertAt = query.TrimEnd().TrimEnd(';').Length;
        var head = query[..insertAt].TrimEnd();
        var tail = query[insertAt..].TrimStart();
        var conjunction = FindFirstTopLevelClause(head, ["WHERE"]) >= 0 ? string.Empty : " WHERE 1 = 1";
        var marker = EncodeMarker(filter);
        var result = $"{head}{conjunction} {marker} AND ({predicate})";
        return tail.Length == 0 ? result : $"{result} {tail}";
    }

    private static string RemoveManagedPredicates(string query)
    {
        var result = query;
        var searchFrom = 0;
        while (true)
        {
            var markerStart = result.IndexOf(MarkerPrefix, searchFrom, StringComparison.Ordinal);
            if (markerStart < 0) return result;
            var markerEnd = result.IndexOf("*/", markerStart + MarkerPrefix.Length, StringComparison.Ordinal);
            if (markerEnd < 0) throw new ArgumentException("A Studio filter marker is incomplete.", nameof(query));
            var cursor = SkipWhitespace(result, markerEnd + 2);
            if (!IsWordAt(result, cursor, "AND"))
                throw new ArgumentException("A Studio filter marker is not followed by its predicate.", nameof(query));
            cursor = SkipWhitespace(result, cursor + 3);
            if (cursor >= result.Length || result[cursor] != '(')
                throw new ArgumentException("A Studio filter predicate must be parenthesized.", nameof(query));
            var close = FindMatchingParenthesis(result, cursor);
            if (close < 0) throw new ArgumentException("A Studio filter predicate is incomplete.", nameof(query));
            result = result.Remove(markerStart, close + 1 - markerStart);
            searchFrom = markerStart;
        }
    }

    private static IReadOnlyList<DesignerQueryFilter> ReadManagedFilters(string query)
    {
        var filters = new List<DesignerQueryFilter>();
        var searchFrom = 0;
        while (true)
        {
            var markerStart = query.IndexOf(MarkerPrefix, searchFrom, StringComparison.Ordinal);
            if (markerStart < 0) return filters;
            var payloadStart = markerStart + MarkerPrefix.Length;
            var markerEnd = query.IndexOf(" */", payloadStart, StringComparison.Ordinal);
            if (markerEnd < 0) throw new ArgumentException("A Studio filter marker is incomplete.", nameof(query));
            var payload = query[payloadStart..markerEnd].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            try
            {
                var filter = JsonSerializer.Deserialize<DesignerQueryFilter>(Convert.FromBase64String(payload));
                if (filter is not null) filters.Add(filter);
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                throw new ArgumentException("A Studio filter marker is invalid.", nameof(query), ex);
            }
            searchFrom = markerEnd + 3;
        }
    }

    private static string EncodeMarker(DesignerQueryFilter filter)
    {
        var json = JsonSerializer.Serialize(filter);
        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{MarkerPrefix}{value} */";
    }

    private static void ValidateQuery(string query)
    {
        var script = new CoreParser(new Lexer(query).Tokenize(), query).Parse();
        if (script.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || script.Statements.Count != 1
            || script.Statements[0] is not SelectStatement)
            throw new ArgumentException("The filtered source is not a valid ETL-SQL SELECT query.", nameof(query));
    }

    private static bool StartsWithQuery(string value) =>
        Regex.IsMatch(value, @"^(?:SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string QuoteIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A filter column is required.");
        return string.Join(".", value.Split('.').Select(part => SimpleIdentifierPattern().IsMatch(part)
            ? part
            : $"[{part.Replace("]", "]]", StringComparison.Ordinal)}]"));
    }

    private static string NormalizeParameter(string? value)
    {
        var parameter = value?.Trim() ?? string.Empty;
        if (!parameter.StartsWith('@')) parameter = "@" + parameter;
        if (!ParameterPattern().IsMatch(parameter)) throw new ArgumentException("The parameter name is invalid.");
        return parameter;
    }

    private static string SqlStringLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static bool HasOuterParentheses(string text)
    {
        if (text.Length < 2 || text[0] != '(') return false;
        return FindMatchingParenthesis(text, 0) == text.Length - 1;
    }

    private static int FindFirstTopLevelClause(string text, IReadOnlyList<string> keywords)
    {
        var depth = 0;
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (inLineComment) { if (current is '\r' or '\n') inLineComment = false; continue; }
            if (inBlockComment) { if (current == '*' && next == '/') { inBlockComment = false; index++; } continue; }
            if (!inString && current == '-' && next == '-') { inLineComment = true; index++; continue; }
            if (!inString && current == '/' && next == '*') { inBlockComment = true; index++; continue; }
            if (current == '\'')
            {
                if (inString && next == '\'') { index++; continue; }
                inString = !inString;
                continue;
            }
            if (inString) continue;
            if (current == '(') { depth++; continue; }
            if (current == ')') { depth--; continue; }
            if (depth == 0 && keywords.Any(keyword => IsWordAt(text, index, keyword))) return index;
        }
        return -1;
    }

    private static int FindMatchingParenthesis(string text, int open)
    {
        var depth = 0;
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;
        for (var index = open; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (inLineComment) { if (current is '\r' or '\n') inLineComment = false; continue; }
            if (inBlockComment) { if (current == '*' && next == '/') { inBlockComment = false; index++; } continue; }
            if (!inString && current == '-' && next == '-') { inLineComment = true; index++; continue; }
            if (!inString && current == '/' && next == '*') { inBlockComment = true; index++; continue; }
            if (current == '\'')
            {
                if (inString && next == '\'') { index++; continue; }
                inString = !inString;
                continue;
            }
            if (inString) continue;
            if (current == '(') depth++;
            else if (current == ')' && --depth == 0) return index;
        }
        return -1;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static bool IsWordAt(string text, int index, string word) =>
        index >= 0 && index + word.Length <= text.Length
        && text.AsSpan(index, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase)
        && (index == 0 || !IsWordCharacter(text[index - 1]))
        && (index + word.Length == text.Length || !IsWordCharacter(text[index + word.Length]));

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    [GeneratedRegex(@"^[#&]?[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceReferencePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleIdentifierPattern();

    [GeneratedRegex(@"^@[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterPattern();
}
