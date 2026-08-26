using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ETL_SQL.Reporting.HtmlVisual;

/// <summary>
/// Evaluates HTML visual templates with typed, HTML-escaped substitutions and conditional blocks.
/// All output is HTML-encoded by default — there is no raw escape hatch.
/// </summary>
public sealed class HtmlTemplateEvaluator
{
    public const int MaxConditionalDepth = 4;

    private static readonly Regex SubstitutionPattern = new(
        @"\{\{(?<body>[^}]+)\}\}",
        RegexOptions.Compiled);

    private static readonly Regex ConditionalOpenPattern = new(
        @"^(?<field>\S+)\s+(?<op>IS\s+NOT\s+NULL|IS\s+NULL|[!=<>]{1,2})\s*(?<value>'[^']*'|\d+(?:\.\d+)?)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FieldFormatPattern = new(
        @"^(?<name>@?\w+)(?:\s+FORMAT\s+'(?<fmt>[^']*)')?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Evaluates a template against a single row of data and a set of parameters.
    /// </summary>
    /// <param name="template">The HTML template string.</param>
    /// <param name="row">Column name → value mapping for the current row. Null for source-free visuals.</param>
    /// <param name="parameters">Parameter name (with @) → value mapping.</param>
    /// <param name="formatValue">Optional formatter callback. Receives (rawValue, formatSpec) → formatted string.</param>
    /// <returns>HTML-encoded output string.</returns>
    public string Evaluate(
        string template,
        IReadOnlyDictionary<string, object?>? row,
        IReadOnlyDictionary<string, object?>? parameters,
        Func<object?, string, string>? formatValue = null)
    {
        var result = EvaluateBlock(template, row, parameters, formatValue, depth: 0);
        return result;
    }

    /// <summary>
    /// Evaluates a template for REPEATER mode — one evaluation per row.
    /// </summary>
    public string EvaluateRepeater(
        string template,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyDictionary<string, object?>? parameters,
        int maxRows,
        Func<object?, string, string>? formatValue = null)
    {
        var sb = new StringBuilder();
        var count = Math.Min(rows.Count, maxRows);
        for (var i = 0; i < count; i++)
            sb.Append(Evaluate(template, rows[i], parameters, formatValue));
        return sb.ToString();
    }

    /// <summary>
    /// Evaluates a fallback template — same substitution syntax but no HTML encoding,
    /// no conditionals, no micro-chart helpers. Plain text output.
    /// </summary>
    public string EvaluateFallback(
        string fallbackTemplate,
        IReadOnlyDictionary<string, object?>? row,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        return SubstitutionPattern.Replace(fallbackTemplate, match =>
        {
            var body = match.Groups["body"].Value.Trim();
            var parsed = FieldFormatPattern.Match(body);
            if (!parsed.Success) return match.Value;

            var name = parsed.Groups["name"].Value;
            var value = ResolveValue(name, row, parameters);
            return value?.ToString() ?? "";
        });
    }

    private string EvaluateBlock(
        string template,
        IReadOnlyDictionary<string, object?>? row,
        IReadOnlyDictionary<string, object?>? parameters,
        Func<object?, string, string>? formatValue,
        int depth)
    {
        if (depth > MaxConditionalDepth)
            throw new HtmlTemplateException($"Conditional nesting exceeds maximum depth of {MaxConditionalDepth}.");

        var sb = new StringBuilder();
        var pos = 0;

        while (pos < template.Length)
        {
            var openIdx = template.IndexOf("{{", pos, StringComparison.Ordinal);
            if (openIdx < 0)
            {
                sb.Append(template, pos, template.Length - pos);
                break;
            }

            sb.Append(template, pos, openIdx - pos);

            var closeIdx = template.IndexOf("}}", openIdx + 2, StringComparison.Ordinal);
            if (closeIdx < 0)
            {
                sb.Append(template, openIdx, template.Length - openIdx);
                break;
            }

            var body = template.Substring(openIdx + 2, closeIdx - openIdx - 2).Trim();

            if (body.StartsWith("#IF ", StringComparison.OrdinalIgnoreCase))
            {
                var endTag = "{{/IF}}";
                var (innerContent, endPos) = ExtractConditionalBlock(template, closeIdx + 2, endTag);
                pos = endPos;

                if (EvaluateCondition(body, row, parameters))
                    sb.Append(EvaluateBlock(innerContent, row, parameters, formatValue, depth + 1));
            }
            else if (body.Equals("/IF", StringComparison.OrdinalIgnoreCase))
            {
                pos = closeIdx + 2;
            }
            else
            {
                var parsed = FieldFormatPattern.Match(body);
                if (parsed.Success)
                {
                    var name = parsed.Groups["name"].Value;
                    var fmt = parsed.Groups["fmt"].Success ? parsed.Groups["fmt"].Value : null;
                    var value = ResolveValue(name, row, parameters);
                    string text;
                    if (fmt != null && formatValue != null)
                        text = formatValue(value, fmt);
                    else
                        text = value?.ToString() ?? "";
                    sb.Append(HtmlEncode(text));
                }
                else
                {
                    sb.Append(HtmlEncode(body));
                }
                pos = closeIdx + 2;
            }
        }

        return sb.ToString();
    }

    private static (string content, int endPos) ExtractConditionalBlock(string template, int startPos, string endTag)
    {
        var depth = 1;
        var pos = startPos;

        while (pos < template.Length && depth > 0)
        {
            var nextOpen = template.IndexOf("{{#IF ", pos, StringComparison.OrdinalIgnoreCase);
            var nextClose = template.IndexOf(endTag, pos, StringComparison.OrdinalIgnoreCase);

            if (nextClose < 0)
                throw new HtmlTemplateException("Unclosed {{#IF}} block — missing {{/IF}}.");

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                pos = nextOpen + 6;
            }
            else
            {
                depth--;
                if (depth == 0)
                {
                    var content = template.Substring(startPos, nextClose - startPos);
                    return (content, nextClose + endTag.Length);
                }
                pos = nextClose + endTag.Length;
            }
        }

        throw new HtmlTemplateException("Unclosed {{#IF}} block — missing {{/IF}}.");
    }

    private static bool EvaluateCondition(
        string ifBody,
        IReadOnlyDictionary<string, object?>? row,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var match = ConditionalOpenPattern.Match(ifBody.Substring(4).Trim());
        if (!match.Success)
            throw new HtmlTemplateException($"Invalid conditional syntax: {{{{{ifBody}}}}}");

        var fieldName = match.Groups["field"].Value;
        var op = match.Groups["op"].Value.Trim().ToUpperInvariant();
        var literalStr = match.Groups["value"].Success ? match.Groups["value"].Value : null;

        var fieldValue = ResolveValue(fieldName, row, parameters);

        if (op == "IS NULL") return fieldValue == null;
        if (op == "IS NOT NULL") return fieldValue != null;

        if (fieldValue == null) return false;

        object? literal = null;
        if (literalStr != null)
        {
            if (literalStr.StartsWith("'") && literalStr.EndsWith("'"))
                literal = literalStr.Substring(1, literalStr.Length - 2);
            else if (decimal.TryParse(literalStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                literal = num;
        }

        var cmp = CompareValues(fieldValue, literal);

        return op switch
        {
            "=" or "==" => cmp == 0,
            "!=" or "<>" => cmp != 0,
            "<" => cmp < 0,
            ">" => cmp > 0,
            "<=" => cmp <= 0,
            ">=" => cmp >= 0,
            _ => throw new HtmlTemplateException($"Unsupported operator '{op}' in conditional.")
        };
    }

    private static int CompareValues(object? left, object? right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        if (left is decimal ld && right is decimal rd) return ld.CompareTo(rd);
        if (left is IConvertible && right is IConvertible)
        {
            try
            {
                var leftDec = Convert.ToDecimal(left, CultureInfo.InvariantCulture);
                var rightDec = Convert.ToDecimal(right, CultureInfo.InvariantCulture);
                return leftDec.CompareTo(rightDec);
            }
            catch
            {
                // Fall through to string comparison
            }
        }

        return string.Compare(left.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static object? ResolveValue(
        string name,
        IReadOnlyDictionary<string, object?>? row,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        if (name.StartsWith("@"))
        {
            if (parameters != null && parameters.TryGetValue(name, out var pval))
                return pval;
            if (parameters != null && parameters.TryGetValue(name.Substring(1), out pval))
                return pval;
            return null;
        }

        if (row != null)
        {
            if (row.TryGetValue(name, out var val))
                return val;
            foreach (var kvp in row)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }

        return null;
    }

    internal static string HtmlEncode(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#x27;"); break;
                case '/': sb.Append("&#x2F;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

public sealed class HtmlTemplateException : Exception
{
    public HtmlTemplateException(string message) : base(message) { }
}
