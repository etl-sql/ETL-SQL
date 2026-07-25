using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Quality;

/// <summary>
/// Parses the <c>@expect</c> mini-DSL into <see cref="ColumnRule"/>s and assembles
/// <c>@expect</c>/<c>@fail</c> (and numbered <c>@expect_N</c>/<c>@fail_N</c>) metadata pairs into
/// <see cref="ColumnRuleBinding"/>s. A real tokenizer, not <c>string.Split(',')</c>: rules combine
/// with top-level commas, while commas inside <c>MATCHES</c> regexes (groups/classes/braces or
/// <c>\,</c>), <c>IN (…)</c> lists, and <c>EXPR</c> function calls stay literal. Strips the outer
/// quotes the tag layer preserves (a doubled same-kind quote is a literal quote — SQL style).
/// All failures throw <see cref="ColumnRuleParseException"/> — malformed rules are hard errors,
/// never silently ignored (design decision 5).
/// </summary>
public static partial class ColumnRuleParser
{
    /// <summary>Parses one <c>@expect</c> tag value (quotes still attached) into its rules.</summary>
    public static IReadOnlyList<ColumnRule> Parse(string rawExpectValue)
    {
        var value = Unquote(rawExpectValue);
        if (string.IsNullOrWhiteSpace(value))
            throw new ColumnRuleParseException("@expect value is empty.");

        var rules = new List<ColumnRule>();
        foreach (var segment in SplitTopLevel(value))
        {
            var text = segment.Trim();
            if (text.Length == 0)
                throw new ColumnRuleParseException($"@expect '{value}' contains an empty rule segment.");
            rules.Add(ParseRule(text));
        }
        return rules;
    }

    /// <summary>
    /// Resolves a column's metadata dictionary into ordered rule/action bindings: <c>expect</c>
    /// pairs with <c>fail</c>, <c>expect_N</c> with <c>fail_N</c>; a missing action defaults to
    /// <see cref="FailAction.Warn"/>; a <c>fail</c> key without its <c>expect</c>, or an unknown
    /// action value, is a hard error. Returns an empty list when the column carries no rules.
    /// </summary>
    public static IReadOnlyList<ColumnRuleBinding> ParseBindings(IReadOnlyDictionary<string, string> metadata)
    {
        var expectKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in metadata)
        {
            var match = RuleKeyRegex().Match(key);
            if (!match.Success) continue;
            var suffix = match.Groups[2].Value; // "" or "_<n>"
            if (match.Groups[1].Value.Equals("expect", StringComparison.OrdinalIgnoreCase))
                expectKeys[suffix] = value;
            else
                failKeys[suffix] = value;
        }

        foreach (var suffix in failKeys.Keys)
        {
            if (!expectKeys.ContainsKey(suffix))
                throw new ColumnRuleParseException(
                    $"@fail{suffix} has no matching @expect{suffix} rule on the same column.");
        }

        var bindings = new List<ColumnRuleBinding>();
        foreach (var suffix in expectKeys.Keys.OrderBy(SuffixOrder))
        {
            var rules = Parse(expectKeys[suffix]);
            bool explicitAction = failKeys.TryGetValue(suffix, out var rawAction);
            var action = explicitAction ? ParseAction(rawAction!, suffix) : FailAction.Warn;
            bindings.Add(new ColumnRuleBinding($"expect{suffix}", rules, action, explicitAction));
        }
        return bindings;
    }

    /// <summary>Parses one <c>@fail</c> tag value (quotes still attached) into its action.</summary>
    public static FailAction ParseAction(string rawFailValue, string suffix = "")
    {
        var value = Unquote(rawFailValue).Trim();
        return value.ToUpperInvariant() switch
        {
            "THROW" => FailAction.Throw,
            "WARN" => FailAction.Warn,
            "QUARANTINE" => FailAction.Quarantine,
            _ => throw new ColumnRuleParseException(
                $"@fail{suffix} action '{value}' is not recognized. Allowed actions: THROW, WARN, QUARANTINE.")
        };
    }

    /// <summary>True when the metadata dictionary carries any <c>@expect</c>/<c>@fail</c> rule keys.</summary>
    public static bool HasRuleTags(IReadOnlyDictionary<string, string>? metadata) =>
        metadata != null && metadata.Keys.Any(k => RuleKeyRegex().IsMatch(k));

    /// <summary>
    /// True for <c>expect</c>/<c>fail</c> and their numbered variants. These are <b>enforcement
    /// directives bound to the statement that declares them</b>, not descriptive metadata about the
    /// data, so — unlike <c>@pii</c>, <c>@owner</c>, or <c>@d</c> — they must never be inherited by
    /// downstream columns. A rule that re-fired on every later read of a loaded table would
    /// re-validate (and re-quarantine) rows that were already validated at load.
    /// </summary>
    public static bool IsRuleTagKey(string key) => RuleKeyRegex().IsMatch(key);

    /// <summary>
    /// Strips one pair of matching outer quotes (<c>'…'</c> or <c>"…"</c>) preserved by the tag
    /// layer, unescaping doubled same-kind quotes inside. Unquoted values are returned trimmed.
    /// </summary>
    public static string Unquote(string raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length >= 2 && (value[0] == '\'' || value[0] == '"') && value[^1] == value[0])
        {
            var quote = value[0];
            var inner = value[1..^1];
            return inner.Replace(new string(quote, 2), quote.ToString());
        }
        return value;
    }

    private static ColumnRule ParseRule(string text)
    {
        var upper = text.ToUpperInvariant();

        if (Regex.IsMatch(upper, @"^NOT\s+NULL$"))
            return new NotNullRule { Text = text };

        if (upper == "UNIQUE")
            return new UniqueRule(UniqueMode.All, null, null) { Text = text };

        var uniqueWith = Regex.Match(text, @"^UNIQUE\s+WITH\s*\((?<cols>.+)\)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (uniqueWith.Success)
        {
            var columns = uniqueWith.Groups["cols"].Value
                .Split(',', StringSplitOptions.TrimEntries)
                .ToList();
            if (columns.Count == 0 || columns.Any(c => !Regex.IsMatch(c, @"^[A-Za-z_]\w*$")))
                throw new ColumnRuleParseException(
                    $"UNIQUE WITH expects a parenthesized list of column names, got '{text}'.");
            return new UniqueRule(UniqueMode.All, null, columns) { Text = text };
        }

        var uniqueOrdered = Regex.Match(text, @"^UNIQUE_(?<mode>FIRST|LAST)(\s+BY\s+(?<key>.+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (uniqueOrdered.Success)
        {
            var mode = uniqueOrdered.Groups["mode"].Value.Equals("FIRST", StringComparison.OrdinalIgnoreCase)
                ? UniqueMode.First
                : UniqueMode.Last;
            if (!uniqueOrdered.Groups["key"].Success || string.IsNullOrWhiteSpace(uniqueOrdered.Groups["key"].Value))
                throw new ColumnRuleParseException(
                    $"UNIQUE_{uniqueOrdered.Groups["mode"].Value.ToUpperInvariant()} requires an explicit " +
                    "BY <order-key> — source order is not stable, so \"first\" is otherwise non-deterministic.");
            var orderKey = ParseSqlExpression(uniqueOrdered.Groups["key"].Value, "UNIQUE BY order key");
            return new UniqueRule(mode, orderKey, null) { Text = text };
        }

        if (upper.StartsWith("MATCHES", StringComparison.Ordinal))
        {
            var pattern = text["MATCHES".Length..].Trim();
            if (pattern.Length == 0)
                throw new ColumnRuleParseException("MATCHES requires a regex pattern.");
            var rule = new MatchesRule(pattern) { Text = text };
            rule.Compile(caseSensitive: true); // validate now: syntax + NonBacktracking support
            return rule;
        }

        var existsIn = Regex.Match(text, @"^EXISTS\s+IN\s+(?<table>[A-Za-z_#][\w.#]*)\s*\(\s*(?<col>[A-Za-z_]\w*)\s*\)$",
            RegexOptions.IgnoreCase);
        if (existsIn.Success)
            return new ExistsInRule(existsIn.Groups["table"].Value, existsIn.Groups["col"].Value) { Text = text };
        if (upper.StartsWith("EXISTS", StringComparison.Ordinal))
            throw new ColumnRuleParseException(
                $"EXISTS IN expects the form 'EXISTS IN table(KeyColumn)', got '{text}'.");

        var inList = Regex.Match(text, @"^IN\s*(?<list>\(.+\))$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (inList.Success)
            return new InListRule(ParseInList(inList.Groups["list"].Value)) { Text = text };

        if (upper.StartsWith("EXPR", StringComparison.Ordinal))
        {
            var predicateText = text["EXPR".Length..].Trim();
            if (predicateText.Length == 0)
                throw new ColumnRuleParseException("EXPR requires a boolean predicate.");
            return new ExprRule(ParseSqlExpression(predicateText, "EXPR predicate")) { Text = text };
        }

        var comparison = Regex.Match(text, @"^(?<op>>=|<=|=|>|<)\s*(?<value>.+)$", RegexOptions.Singleline);
        if (comparison.Success)
        {
            if (!decimal.TryParse(comparison.Groups["value"].Value.Trim(), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var bound))
                throw new ColumnRuleParseException(
                    $"Comparison rule '{text}' requires a numeric bound (compares are decimal at runtime).");
            var op = comparison.Groups["op"].Value switch
            {
                ">=" => CompareOp.GreaterOrEqual,
                "<=" => CompareOp.LessOrEqual,
                ">" => CompareOp.Greater,
                "<" => CompareOp.Less,
                _ => CompareOp.Equal
            };
            return new ComparisonRule(op, bound) { Text = text };
        }

        throw new ColumnRuleParseException(
            $"Unknown @expect rule '{text}'. Supported: NOT NULL, UNIQUE, UNIQUE WITH (cols), " +
            "UNIQUE_FIRST/UNIQUE_LAST BY <key>, MATCHES <regex>, IN (<list>), EXISTS IN table(col), " +
            "EXPR <predicate>, and numeric >= <= > < = compares.");
    }

    /// <summary>
    /// Splits combined rules at top-level commas. A comma is literal when it sits inside
    /// parentheses/brackets/braces, inside a quoted string (<c>''</c> doubling respected), or —
    /// outside quotes — immediately after a backslash (regex escape).
    /// </summary>
    private static IEnumerable<string> SplitTopLevel(string value)
    {
        var segment = new StringBuilder();
        int depth = 0;
        char quote = '\0';

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (quote != '\0')
            {
                segment.Append(c);
                if (c == quote)
                {
                    if (i + 1 < value.Length && value[i + 1] == quote) { segment.Append(value[++i]); continue; }
                    quote = '\0';
                }
                continue;
            }

            switch (c)
            {
                case '\\' when i + 1 < value.Length:
                    segment.Append(c).Append(value[++i]);
                    continue;
                case '\'' or '"':
                    quote = c;
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return segment.ToString();
                    segment.Clear();
                    continue;
            }
            segment.Append(c);
        }
        yield return segment.ToString();
    }

    private static List<object?> ParseInList(string parenGroup)
    {
        var tokens = new Lexer(parenGroup).Tokenize();
        int i = 0;
        if (tokens.Count == 0 || tokens[i].Type != TokenType.LPAREN)
            throw new ColumnRuleParseException($"IN list '{parenGroup}' must be parenthesized.");
        i++;

        var values = new List<object?>();
        while (true)
        {
            bool negative = false;
            if (tokens[i].Type is TokenType.MINUS or TokenType.PLUS)
            {
                negative = tokens[i].Type == TokenType.MINUS;
                i++;
            }

            switch (tokens[i].Type)
            {
                case TokenType.STRING_LITERAL when !negative:
                    values.Add(tokens[i].Value);
                    break;
                case TokenType.NUMBER:
                    var number = decimal.Parse(tokens[i].Value, CultureInfo.InvariantCulture);
                    values.Add(negative ? -number : number);
                    break;
                default:
                    throw new ColumnRuleParseException(
                        $"IN list '{parenGroup}' supports string and numeric literals only " +
                        "(NULL is meaningless here — non-NOT NULL rules skip NULL values).");
            }
            i++;

            if (tokens[i].Type == TokenType.COMMA) { i++; continue; }
            if (tokens[i].Type == TokenType.RPAREN) break;
            throw new ColumnRuleParseException($"IN list '{parenGroup}' is malformed near '{tokens[i].Value}'.");
        }

        if (values.Count == 0)
            throw new ColumnRuleParseException("IN list must contain at least one literal.");
        if (tokens[i + 1].Type != TokenType.EOF)
            throw new ColumnRuleParseException($"IN list '{parenGroup}' has trailing content.");
        return values;
    }

    private static Expression ParseSqlExpression(string text, string context)
    {
        try
        {
            var parser = new Parser.Parser(new Lexer(text).Tokenize(), text);
            var expression = parser.ParseExpression();
            if (parser.Current.Type != TokenType.EOF)
                throw new ColumnRuleParseException(
                    $"{context} '{text}' has trailing content near '{parser.Current.Value}'.");
            return expression;
        }
        catch (SyntaxException ex)
        {
            throw new ColumnRuleParseException($"{context} '{text}' is not a valid expression: {ex.Message}");
        }
    }

    private static int SuffixOrder(string suffix) =>
        suffix.Length == 0 ? -1 : int.Parse(suffix[1..], CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(expect|fail)(_[0-9]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RuleKeyRegex();
}
