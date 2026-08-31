using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Quality;

/// <summary>
/// The <b>read-side</b> rule parser: turns the <c>expect</c>/<c>fail</c> stewardship tags that
/// <see cref="ColumnExpectProjection"/> publishes onto lineage back into
/// <see cref="ColumnRuleBinding"/>s, for the catalog, the Portal, and
/// <c>SHOW DATA QUALITY RULES</c>.
/// <para>
/// This is not the authoring path — scripts declare rules with an <c>EXPECT</c> clause, parsed from
/// the token stream by <see cref="ColumnExpectClauseParser"/>. What arrives here is the engine's own
/// projection, so this parser only has to accept what that emits: rules combined with <c>AND</c>
/// (a top-level <c>AND</c> unrolls, matching the clause parser), a quoted <c>MATCHES</c> pattern,
/// and the historical comma-combined form. All failures throw
/// <see cref="ColumnRuleParseException"/>; read-side callers skip the entry rather than fail a
/// listing over one unreadable tag.
/// </para>
/// </summary>
public static partial class ColumnRuleParser
{
    /// <summary>Parses one projected <c>expect</c> tag value (quotes optional) into its rules.</summary>
    public static IReadOnlyList<ColumnRule> Parse(string rawExpectValue)
    {
        var value = Unquote(rawExpectValue);
        if (string.IsNullOrWhiteSpace(value))
            throw new ColumnRuleParseException("Rule value is empty.");

        var rules = new List<ColumnRule>();
        foreach (var segment in SplitTopLevel(value))
        {
            var text = segment.Trim();
            if (text.Length == 0)
                throw new ColumnRuleParseException($"'{value}' contains an empty rule segment.");

            // A top-level AND unrolls into independent rules, matching the clause parser, so a
            // steward reading the catalog sees the same rules the engine evaluates and reports
            // rather than one merged line.
            var rule = ParseRule(text);
            if (rule is AndRule conjunction) rules.AddRange(conjunction.Operands);
            else rules.Add(rule);
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
                    $"fail{suffix} has no matching expect{suffix} rule on the same column.");
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

    /// <summary>Parses one projected <c>fail</c> tag value (quotes optional) into its action.</summary>
    public static FailAction ParseAction(string rawFailValue, string suffix = "")
    {
        var value = Unquote(rawFailValue).Trim();
        return value.ToUpperInvariant() switch
        {
            "THROW" => FailAction.Throw,
            "WARN" => FailAction.Warn,
            "QUARANTINE" => FailAction.Quarantine,
            _ => throw new ColumnRuleParseException(
                $"fail{suffix} action '{value}' is not recognized. Allowed actions: THROW, WARN, QUARANTINE.")
        };
    }

    /// <summary>True when the metadata dictionary carries any projected rule keys.</summary>
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

    /// <summary>
    /// Strips one pair of single quotes from a MATCHES pattern, unescaping doubled quotes inside.
    /// An unquoted pattern is returned as-is, so rules written before quoting was required still
    /// read back.
    /// </summary>
    private static string UnquotePattern(string pattern)
    {
        if (pattern.Length >= 2 && pattern[0] == '\'' && pattern[^1] == '\'')
            return pattern[1..^1].Replace("''", "'");
        return pattern;
    }

    private static ColumnRule ParseRule(string text)
    {
        return ParseOrExpression(text.Trim());
    }

    private static ColumnRule ParseOrExpression(string text)
    {
        var branches = SplitTopLevelOr(text);
        if (branches.Count > 1)
        {
            var operands = new List<ColumnRule>();
            foreach (var branch in branches)
            {
                if (string.IsNullOrWhiteSpace(branch))
                    throw new ColumnRuleParseException($"Rule '{text}' contains an empty OR operand.");
                operands.Add(ParseAndExpression(branch));
            }
            return new OrRule(operands) { Text = text };
        }
        return ParseAndExpression(text);
    }

    private static ColumnRule ParseAndExpression(string text)
    {
        var branches = SplitTopLevelAnd(text);
        if (branches.Count > 1)
        {
            var operands = new List<ColumnRule>();
            foreach (var branch in branches)
            {
                if (string.IsNullOrWhiteSpace(branch))
                    throw new ColumnRuleParseException($"Rule '{text}' contains an empty AND operand.");
                operands.Add(ParsePrimaryRule(branch));
            }
            return new AndRule(operands) { Text = text };
        }
        return ParsePrimaryRule(text);
    }

    private static ColumnRule ParsePrimaryRule(string text)
    {
        var trimmed = text.Trim();
        if (IsEnclosedInMatchingParens(trimmed))
        {
            var inner = trimmed[1..^1].Trim();
            if (string.IsNullOrWhiteSpace(inner))
                throw new ColumnRuleParseException($"Empty parenthesized rule in '{text}'.");
            return ParseOrExpression(inner);
        }

        return ParseAtomicRule(trimmed);
    }

    private static bool IsEnclosedInMatchingParens(string text)
    {
        if (text.Length < 2 || text[0] != '(' || text[^1] != ')')
            return false;
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    if (i + 1 < text.Length && text[i + 1] == quote) i++;
                    else quote = '\0';
                }
                continue;
            }
            if (c is '\'' or '"') quote = c;
            else if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0 && i < text.Length - 1)
                    return false;
            }
        }
        return depth == 0;
    }

    private static List<string> SplitTopLevelOr(string text)
    {
        var list = new List<string>();
        int depth = 0;
        char quote = '\0';
        int last = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    if (i + 1 < text.Length && text[i + 1] == quote) i++;
                    else quote = '\0';
                }
                continue;
            }

            switch (c)
            {
                case '\\' when i + 1 < text.Length:
                    i++;
                    continue;
                case '\'' or '"':
                    quote = c;
                    continue;
                case '(' or '[' or '{':
                    depth++;
                    continue;
                case ')' or ']' or '}':
                    depth--;
                    continue;
            }

            if (depth == 0)
            {
                if (i + 2 <= text.Length && text.AsSpan(i, 2).Equals("OR", StringComparison.OrdinalIgnoreCase))
                {
                    bool prevBoundary = i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] == ')';
                    bool nextBoundary = i + 2 == text.Length || char.IsWhiteSpace(text[i + 2]) || text[i + 2] == '(';
                    if (prevBoundary && nextBoundary)
                    {
                        list.Add(text[last..i].Trim());
                        i += 1;
                        last = i + 1;
                    }
                }
            }
        }
        list.Add(text[last..].Trim());
        return list;
    }

    private static List<string> SplitTopLevelAnd(string text)
    {
        var list = new List<string>();
        int depth = 0;
        char quote = '\0';
        int last = 0;
        bool betweenExpectsAnd = StartsWithBetween(text.TrimStart());

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    if (i + 1 < text.Length && text[i + 1] == quote) i++;
                    else quote = '\0';
                }
                continue;
            }

            switch (c)
            {
                case '\\' when i + 1 < text.Length:
                    i++;
                    continue;
                case '\'' or '"':
                    quote = c;
                    continue;
                case '(' or '[' or '{':
                    depth++;
                    continue;
                case ')' or ']' or '}':
                    depth--;
                    continue;
            }

            if (depth == 0)
            {
                if (i + 3 <= text.Length && text.AsSpan(i, 3).Equals("AND", StringComparison.OrdinalIgnoreCase))
                {
                    bool prevBoundary = i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] == ')';
                    bool nextBoundary = i + 3 == text.Length || char.IsWhiteSpace(text[i + 3]) || text[i + 3] == '(';
                    if (prevBoundary && nextBoundary)
                    {
                        if (betweenExpectsAnd)
                        {
                            betweenExpectsAnd = false;
                        }
                        else
                        {
                            list.Add(text[last..i].Trim());
                            i += 2;
                            last = i + 1;
                            betweenExpectsAnd = StartsWithBetween(text[last..].TrimStart());
                        }
                    }
                }
            }
        }
        list.Add(text[last..].Trim());
        return list;
    }

    private static bool StartsWithBetween(string s)
    {
        return s.StartsWith("BETWEEN ", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("LENGTH BETWEEN ", StringComparison.OrdinalIgnoreCase);
    }

    private static ColumnRule ParseAtomicRule(string text)
    {
        var upper = text.ToUpperInvariant();

        if (Regex.IsMatch(upper, @"^NOT\s+NULL$"))
            return new NotNullRule { Text = text };

        if (Regex.IsMatch(upper, @"^NOT\s+BLANK$"))
            return new NotBlankRule { Text = text };

        if (upper.StartsWith("LENGTH", StringComparison.Ordinal))
            return ParseLengthRule(text);

        if (upper.StartsWith("CASTABLE", StringComparison.Ordinal))
            return ParseCastableRule(text);

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

        // NOT MATCHES / NOT IN reuse the positive forms' parsing with the verdict inverted, so the
        // two directions cannot drift in what they accept. NOT NULL and NOT BLANK are matched
        // above; EXISTS IN never reaches here because NOT is required immediately before IN.
        var negated = Regex.Match(text, @"^NOT\s+(?<body>MATCHES\b.*|IN\s*\(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (negated.Success)
            return ParsePatternOrListRule(negated.Groups["body"].Value.Trim(), text, negated: true);

        if (upper.StartsWith("MATCHES", StringComparison.Ordinal))
            return ParsePatternOrListRule(text, text, negated: false);

        var existsWith = Regex.Match(
            text,
            @"^EXISTS\s+WITH\s*\((?<src>[^)]+)\)\s+IN\s+(?<table>[A-Za-z_#][\w.#]*)\s*\((?<keys>[^)]+)\)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (existsWith.Success)
        {
            var sourceColumns = ParseColumnList(existsWith.Groups["src"].Value, "EXISTS WITH", text);
            var keyColumns = ParseColumnList(existsWith.Groups["keys"].Value, "EXISTS WITH reference", text);
            if (sourceColumns.Count != keyColumns.Count)
                throw new ColumnRuleParseException(
                    $"EXISTS WITH '{text}' probes {sourceColumns.Count} column(s) against " +
                    $"{keyColumns.Count} reference column(s); the tuples must have the same arity.");
            return new ExistsInRule(existsWith.Groups["table"].Value, keyColumns, sourceColumns) { Text = text };
        }

        var existsIn = Regex.Match(text, @"^EXISTS\s+IN\s+(?<table>[A-Za-z_#][\w.#]*)\s*\(\s*(?<col>[A-Za-z_]\w*)\s*\)$",
            RegexOptions.IgnoreCase);
        if (existsIn.Success)
            return new ExistsInRule(existsIn.Groups["table"].Value, existsIn.Groups["col"].Value) { Text = text };
        if (upper.StartsWith("EXISTS", StringComparison.Ordinal))
            throw new ColumnRuleParseException(
                $"EXISTS expects the form 'EXISTS IN table(KeyColumn)' or " +
                $"'EXISTS WITH (col, …) IN table(KeyColumn, …)', got '{text}'.");

        if (Regex.IsMatch(text, @"^IN\s*\(", RegexOptions.IgnoreCase))
            return ParsePatternOrListRule(text, text, negated: false);

        if (upper.StartsWith("BETWEEN", StringComparison.Ordinal))
        {
            var body = text["BETWEEN".Length..];
            var separator = FindTopLevelAnd(body);
            if (separator < 0)
                throw new ColumnRuleParseException(
                    $"BETWEEN expects the form 'BETWEEN <lower> AND <upper>', got '{text}'.");

            var lowerBound = body[..separator].Trim();
            var upperBound = body[(separator + 3)..].Trim();
            if (lowerBound.Length == 0 || upperBound.Length == 0)
                throw new ColumnRuleParseException($"BETWEEN rule '{text}' is missing a bound.");

            return new BetweenRule(
                ParseSqlExpression(lowerBound, "BETWEEN lower bound"),
                ParseSqlExpression(upperBound, "BETWEEN upper bound"))
            { Text = text };
        }

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
            $"Unknown rule '{text}'. Supported: NOT NULL, NOT BLANK, UNIQUE, " +
            "UNIQUE WITH (cols), UNIQUE_FIRST/UNIQUE_LAST BY <key>, MATCHES <regex>, IN (<list>), " +
            "EXISTS IN table(col), EXISTS WITH (cols) IN table(cols), LENGTH BETWEEN <min> AND <max>, " +
            "LENGTH <compare> <n>, CASTABLE AS <type>, NOT IN (<list>), NOT MATCHES <regex>, " +
            "BETWEEN <lower> AND <upper>, EXPR <predicate>, and numeric >= <= > < = compares.");
    }

    /// <summary>
    /// Lowers every <c>LENGTH</c> form onto one inclusive range. <c>&gt;</c> and <c>&lt;</c> shift
    /// the bound by one rather than getting their own runtime predicate, which is only sound
    /// because a character count is an integer.
    /// </summary>
    private static ColumnRule ParseLengthRule(string text)
    {
        var between = Regex.Match(
            text, @"^LENGTH\s+BETWEEN\s+(?<min>-?\d+)\s+AND\s+(?<max>-?\d+)$", RegexOptions.IgnoreCase);
        if (between.Success)
        {
            var min = ParseLengthBound(between.Groups["min"].Value, text);
            var max = ParseLengthBound(between.Groups["max"].Value, text);
            if (min > max)
                throw new ColumnRuleParseException(
                    $"LENGTH rule '{text}' has a minimum above its maximum, so no value can satisfy it.");
            return new LengthRule(min, max) { Text = text };
        }

        var comparison = Regex.Match(
            text, @"^LENGTH\s*(?<op>>=|<=|=|>|<)\s*(?<value>-?\d+)$", RegexOptions.IgnoreCase);
        if (comparison.Success)
        {
            var bound = ParseLengthBound(comparison.Groups["value"].Value, text);
            return comparison.Groups["op"].Value switch
            {
                ">=" => new LengthRule(bound, null) { Text = text },
                ">" => new LengthRule(bound + 1, null) { Text = text },
                "<=" => new LengthRule(0, bound) { Text = text },
                "<" when bound == 0 => throw new ColumnRuleParseException(
                    $"LENGTH rule '{text}' can never be satisfied — no value is shorter than zero characters."),
                "<" => new LengthRule(0, bound - 1) { Text = text },
                _ => new LengthRule(bound, bound) { Text = text }
            };
        }

        throw new ColumnRuleParseException(
            $"LENGTH expects the form 'LENGTH BETWEEN <min> AND <max>' or a comparison such as " +
            $"'LENGTH >= 5', got '{text}'.");
    }

    /// <summary>
    /// Index of the <c>AND</c> separating a BETWEEN rule's two bounds, or -1. Only an <c>AND</c>
    /// outside parentheses and quotes separates them — <c>DATEADD(DAY, -30, @RunDate)</c> may
    /// itself contain one, and a naive first-match split would cut the bound in half.
    /// </summary>
    private static int FindTopLevelAnd(string value)
    {
        var depth = 0;
        var quote = '\0';

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    if (i + 1 < value.Length && value[i + 1] == quote) i++;
                    else quote = '\0';
                }
                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    continue;
                case '(' or '[':
                    depth++;
                    continue;
                case ')' or ']':
                    depth--;
                    continue;
            }

            if (depth != 0 || i == 0) continue;
            if (!char.IsWhiteSpace(value[i - 1])) continue;
            if (i + 3 > value.Length) continue;
            if (!value.AsSpan(i, 3).Equals("AND", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 3 < value.Length && !char.IsWhiteSpace(value[i + 3])) continue;
            return i;
        }
        return -1;
    }

    /// <summary>
    /// Parses <c>MATCHES &lt;regex&gt;</c> or <c>IN (&lt;list&gt;)</c> from <paramref name="body"/>,
    /// which is the rule with any leading <c>NOT</c> already removed. One parser serves both
    /// directions so the negative form cannot accept something the positive form rejects.
    /// <paramref name="text"/> is the rule as written, kept for diagnostics.
    /// </summary>
    private static ColumnRule ParsePatternOrListRule(string body, string text, bool negated)
    {
        if (body.StartsWith("MATCHES", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = body["MATCHES".Length..].Trim();
            if (pattern.Length == 0)
                throw new ColumnRuleParseException("MATCHES requires a regex pattern.");
            // In the clause grammar the pattern is a string literal, and that is the form projected
            // onto lineage. Unwrap it here so a rule read back off a lineage tag compiles to the
            // same regex the statement enforced — a quoted pattern that stayed quoted would match
            // nothing and quietly disagree with the engine.
            pattern = UnquotePattern(pattern);
            var rule = new MatchesRule(pattern, negated) { Text = text };
            rule.Compile(caseSensitive: true); // validate now: syntax + NonBacktracking support
            return rule;
        }

        var inList = Regex.Match(body, @"^IN\s*(?<list>\(.+\))$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (inList.Success)
            return new InListRule(ParseInList(inList.Groups["list"].Value), negated) { Text = text };

        throw new ColumnRuleParseException(
            $"'{text}' is not a valid MATCHES or IN rule. Expected 'MATCHES <regex>' or 'IN (<list>)', "
            + "optionally prefixed with NOT.");
    }

    /// <summary>
    /// Parses <c>CASTABLE AS &lt;type&gt;</c>, optionally with a declared width. The type name is
    /// checked against the engine's converter registry here rather than at runtime: an unregistered
    /// type makes the shared cast return the value unchanged, so the rule would pass every row —
    /// the failure mode a validity check must not have.
    /// </summary>
    private static ColumnRule ParseCastableRule(string text)
    {
        var match = Regex.Match(
            text,
            @"^CASTABLE\s+AS\s+(?<type>[A-Za-z_]\w*)\s*(\(\s*(?<precision>\d+)\s*(,\s*(?<scale>\d+)\s*)?\))?$",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new ColumnRuleParseException(
                $"CASTABLE expects the form 'CASTABLE AS <type>', optionally with a width such as " +
                $"'CASTABLE AS DECIMAL(18,2)', got '{text}'.");

        var baseType = match.Groups["type"].Value.ToUpperInvariant();
        if (!TypeConverter.IsRegistered(baseType))
            throw new ColumnRuleParseException(
                $"CASTABLE AS '{baseType}' names a type this engine has no conversion for, so the " +
                "rule would accept every value. Use a type CAST accepts.");

        int? precision = match.Groups["precision"].Success
            ? int.Parse(match.Groups["precision"].Value, CultureInfo.InvariantCulture)
            : null;
        int? scale = match.Groups["scale"].Success
            ? int.Parse(match.Groups["scale"].Value, CultureInfo.InvariantCulture)
            : null;

        if (precision is 0)
            throw new ColumnRuleParseException(
                $"CASTABLE rule '{text}' declares a width of zero, which no value can satisfy.");
        if (precision is { } p && scale is { } s && s > p)
            throw new ColumnRuleParseException(
                $"CASTABLE rule '{text}' declares more decimal places than total digits.");

        // Rebuilt from the captured groups rather than sliced out of the rule text, so the width
        // reaches the converter in the canonical form it parses — the forms it interprets itself,
        // such as DATETIME(3) truncating to a precision, keep behaving as they do in a CAST.
        var declaredType = precision switch
        {
            { } width when scale is { } places => $"{baseType}({width},{places})",
            { } width => $"{baseType}({width})",
            _ => baseType
        };
        return new CastableRule(declaredType, baseType, precision, scale) { Text = text };
    }

    private static int ParseLengthBound(string raw, string text)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bound) || bound < 0)
            throw new ColumnRuleParseException(
                $"LENGTH rule '{text}' requires whole, non-negative character counts.");
        return bound;
    }

    /// <summary>
    /// Parses a comma-separated parenthesized column list into identifiers. Rejects empty entries
    /// and anything that is not a bare identifier — a rule that silently accepted an expression
    /// here would build its key set from something the reference-table read cannot reproduce.
    /// </summary>
    private static List<string> ParseColumnList(string raw, string ruleName, string text)
    {
        var columns = raw.Split(',', StringSplitOptions.TrimEntries).ToList();
        if (columns.Count == 0 || columns.Any(c => !Regex.IsMatch(c, @"^[A-Za-z_]\w*$")))
            throw new ColumnRuleParseException(
                $"{ruleName} expects a parenthesized list of column names, got '{text}'.");
        return columns;
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
