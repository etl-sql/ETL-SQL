using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Quality;

/// <summary>
/// Parses <c>EXPECT &lt;rule&gt; [ON FAILURE &lt;action&gt;]</c> clauses off a select column,
/// straight from the shared token stream.
/// <para>
/// Rules are grammar, not comment tags: a rule decides which rows leave the statement, so it must
/// not be something a formatter or comment stripper can silently remove. Parsing from tokens is
/// what makes every rule element carry a real source position, lets bounds and predicates go
/// through the engine's own expression parser, and removes the outer quoting the tag layer forced.
/// </para>
/// <para>
/// Grammar: <c>rules := or ; or := and (OR and)* ; and := primary (AND primary)* ;
/// primary := '(' or ')' | atom</c>. <c>BETWEEN</c> and <c>LENGTH BETWEEN</c> consume their own
/// <c>AND</c> while parsing bounds, so the rule-level <c>AND</c> never sees it — the textual
/// look-ahead the string parser needed for that case is gone.
/// </para>
/// <para>
/// A top-level comma does <b>not</b> combine rules any more: in a select list the comma belongs to
/// the column list, and one character cannot mean both. Combine rules with <c>AND</c>.
/// </para>
/// </summary>
public sealed class ColumnExpectClauseParser
{
    private readonly IParser _parser;

    public ColumnExpectClauseParser(IParser parser) => _parser = parser;

    /// <summary>True when an <c>EXPECT</c> clause starts at the parser's current token.</summary>
    public bool AtClause => _parser.Current.Type == TokenType.EXPECT;

    /// <summary>
    /// Parses every consecutive <c>EXPECT</c> clause at the cursor, or returns null when there is
    /// none. Clauses are returned in written order.
    /// </summary>
    public IReadOnlyList<ColumnExpectClause>? ParseClauses()
    {
        if (!AtClause) return null;

        var clauses = new List<ColumnExpectClause>();
        while (AtClause)
            clauses.Add(ParseClause());
        return clauses;
    }

    private ColumnExpectClause ParseClause()
    {
        var expectToken = _parser.Consume(TokenType.EXPECT, "Expected EXPECT");
        if (IsWord(_parser.Current, "SCHEMA"))
            throw Syntax(_parser.Current,
                "EXPECT SCHEMA is a statement, not a column rule. Write it on its own line, "
                + "or use a column rule such as 'EXPECT NOT NULL'.");

        var startToken = _parser.Current;
        var rule = ParseOr();
        var text = Slice(startToken, _parser.Previous);
        var rules = Flatten(rule);

        var action = FailAction.Warn;
        var explicitAction = false;
        if (_parser.Current.Type == TokenType.ON && IsWord(_parser.Peek, "FAILURE"))
        {
            _parser.Advance(); // ON
            _parser.Advance(); // FAILURE
            action = ParseAction();
            explicitAction = true;
        }

        return new ColumnExpectClause(rules, action, explicitAction, text)
        {
            Line = expectToken.Line,
            Column = expectToken.Column,
            EndLine = _parser.LastTokenEndLine,
            EndColumn = _parser.LastTokenEndColumn
        };
    }

    /// <summary>
    /// Reads the action word after a column-level <c>ON FAILURE</c>. Routing (<c>TO</c>,
    /// <c>WITH</c>) belongs to the statement-level clause and is rejected here with a message that
    /// says where it goes — declaring a target per column would let two columns disagree about
    /// where the same run's rows land.
    /// </summary>
    private FailAction ParseAction()
    {
        var token = _parser.Current;
        FailAction action;
        if (token.Type == TokenType.THROW || IsWord(token, "THROW")) action = FailAction.Throw;
        else if (IsWord(token, "WARN")) action = FailAction.Warn;
        else if (IsWord(token, "QUARANTINE")) action = FailAction.Quarantine;
        else if (IsWord(token, "NOTIFY"))
            throw Syntax(token,
                "NOTIFY is a job-level action. A column rule acts on the failing row "
                + "(THROW, WARN, QUARANTINE); use ASSERT JOB … ON FAILURE NOTIFY to alert a channel.");
        else
            throw Syntax(token,
                $"'{token.Value}' is not a column failure action. Expected THROW, WARN, or QUARANTINE.");

        _parser.Advance();

        if (_parser.Current.Type == TokenType.TO)
            throw Syntax(_parser.Current,
                "A column's ON FAILURE names the action only. The target belongs to the statement's "
                + "trailing clause: ON FAILURE QUARANTINE TO <table>.");
        if (_parser.Current.Type == TokenType.WITH)
            throw Syntax(_parser.Current,
                "A column's ON FAILURE takes no options. RETENTION and HANDLING belong to the "
                + "statement's trailing ON FAILURE clause.");

        return action;
    }

    /// <summary>
    /// AND/OR compose into a single rule tree; a clause's <see cref="ColumnExpectClause.Rules"/> is
    /// therefore one rule unless the tree is a top-level AND, which is unrolled so each conjunct
    /// reports its own failures — the same shape the comma-separated tag form produced.
    /// </summary>
    private static IReadOnlyList<ColumnRule> Flatten(ColumnRule rule) =>
        rule is AndRule and_ ? and_.Operands : new[] { rule };

    private ColumnRule ParseOr()
    {
        var start = _parser.Current;
        var left = ParseAnd();
        if (_parser.Current.Type != TokenType.OR) return left;

        var operands = new List<ColumnRule> { left };
        while (_parser.Match(TokenType.OR))
            operands.Add(ParseAnd());
        return new OrRule(operands) { Text = Slice(start, _parser.Previous) };
    }

    private ColumnRule ParseAnd()
    {
        var start = _parser.Current;
        var left = ParsePrimary();
        if (_parser.Current.Type != TokenType.AND) return left;

        var operands = new List<ColumnRule> { left };
        while (_parser.Match(TokenType.AND))
            operands.Add(ParsePrimary());
        return new AndRule(operands) { Text = Slice(start, _parser.Previous) };
    }

    private ColumnRule ParsePrimary()
    {
        if (_parser.Current.Type == TokenType.LPAREN)
        {
            var open = _parser.Advance();
            var inner = ParseOr();
            _parser.Consume(TokenType.RPAREN, "Expected ')' to close a grouped rule");
            return CloneWithText(inner, Slice(open, _parser.Previous));
        }
        return ParseAtom();
    }

    private ColumnRule ParseAtom()
    {
        var start = _parser.Current;
        var rule = ParseAtomBody(start);
        return CloneWithText(rule, Slice(start, _parser.Previous));
    }

    private ColumnRule ParseAtomBody(Token start)
    {
        // NOT NULL / NOT BLANK / NOT MATCHES / NOT IN. The negated pattern and list forms reuse the
        // positive parsing with the verdict inverted, so the two directions cannot drift.
        if (start.Type == TokenType.NOT)
        {
            _parser.Advance();
            if (_parser.Current.Type == TokenType.NULL) { _parser.Advance(); return new NotNullRule() { Text = string.Empty }; }
            if (IsWord(_parser.Current, "BLANK")) { _parser.Advance(); return new NotBlankRule() { Text = string.Empty }; }
            if (IsWord(_parser.Current, "MATCHES")) return ParseMatches(negated: true);
            if (_parser.Current.Type == TokenType.IN) return ParseInList(negated: true);
            throw Syntax(_parser.Current,
                $"NOT expects NULL, BLANK, MATCHES, or IN, got '{_parser.Current.Value}'.");
        }

        if (IsWord(start, "UNIQUE")) return ParseUnique();
        if (IsWord(start, "UNIQUE_FIRST") || IsWord(start, "UNIQUE_LAST")) return ParseUniqueOrdered();
        if (IsWord(start, "MATCHES")) return ParseMatches(negated: false);
        if (start.Type == TokenType.IN) return ParseInList(negated: false);
        if (IsWord(start, "EXISTS")) return ParseExists();
        if (IsWord(start, "LENGTH")) return ParseLength();
        if (IsWord(start, "CASTABLE")) return ParseCastable();
        if (IsWord(start, "EXPR")) return ParseExpr();
        if (start.Type == TokenType.BETWEEN) return ParseBetween();
        if (TryCompareOp(start, out var op)) return ParseComparison(op);

        throw Syntax(start,
            $"Unknown rule '{start.Value}'. Supported: NOT NULL, NOT BLANK, UNIQUE, "
            + "UNIQUE WITH (cols), UNIQUE_FIRST/UNIQUE_LAST BY <key>, MATCHES '<regex>', IN (<list>), "
            + "EXISTS IN table(col), EXISTS WITH (cols) IN table(cols), LENGTH BETWEEN <min> AND <max>, "
            + "LENGTH <compare> <n>, CASTABLE AS <type>, NOT IN (<list>), NOT MATCHES '<regex>', "
            + "BETWEEN <lower> AND <upper>, EXPR <predicate>, and numeric >= <= > < = compares.");
    }

    private ColumnRule ParseUnique()
    {
        _parser.Advance(); // UNIQUE
        if (_parser.Current.Type != TokenType.WITH)
            return new UniqueRule(UniqueMode.All, null, null) { Text = string.Empty };

        _parser.Advance(); // WITH
        var columns = ParseColumnList("UNIQUE WITH");
        return new UniqueRule(UniqueMode.All, null, columns) { Text = string.Empty };
    }

    private ColumnRule ParseUniqueOrdered()
    {
        var word = _parser.Advance();
        var mode = word.Value.EndsWith("FIRST", StringComparison.OrdinalIgnoreCase)
            ? UniqueMode.First
            : UniqueMode.Last;
        if (_parser.Current.Type != TokenType.BY)
            throw Syntax(_parser.Current,
                $"{word.Value.ToUpperInvariant()} requires an explicit BY <order-key> — source order "
                + "is not stable, so \"first\" is otherwise non-deterministic.");
        _parser.Advance(); // BY
        var orderKey = ParseRuleExpression("UNIQUE BY order key");
        return new UniqueRule(mode, orderKey, null) { Text = string.Empty };
    }

    /// <summary>
    /// <c>MATCHES '&lt;regex&gt;'</c>. The pattern is a string literal because a bare regex cannot
    /// be lexed — <c>@</c>, quotes, and operators would all tokenize. The pattern is compiled here
    /// so an invalid or ReDoS-prone one fails at parse time rather than mid-stream.
    /// </summary>
    private ColumnRule ParseMatches(bool negated)
    {
        _parser.Advance(); // MATCHES
        if (_parser.Current.Type != TokenType.STRING_LITERAL)
            throw Syntax(_parser.Current,
                "MATCHES takes a quoted pattern, e.g. MATCHES '^[A-Z]{2}-\\d+$'. "
                + "A bare regex cannot be tokenized.");
        var pattern = _parser.Advance().Value;
        var rule = new MatchesRule(pattern, negated) { Text = pattern };
        try
        {
            rule.Compile(caseSensitive: true); // validate now: syntax + NonBacktracking support
        }
        catch (ColumnRuleParseException ex)
        {
            throw Syntax(_parser.Previous, ex.Message);
        }
        return rule;
    }

    private ColumnRule ParseInList(bool negated)
    {
        _parser.Advance(); // IN
        _parser.Consume(TokenType.LPAREN, "Expected '(' after IN");

        var values = new List<object?>();
        while (true)
        {
            var negative = false;
            if (_parser.Current.Type is TokenType.MINUS or TokenType.PLUS)
            {
                negative = _parser.Current.Type == TokenType.MINUS;
                _parser.Advance();
            }

            var token = _parser.Current;
            if (token.Type == TokenType.STRING_LITERAL && !negative)
            {
                values.Add(token.Value);
            }
            else if (token.Type == TokenType.NUMBER)
            {
                var number = decimal.Parse(token.Value, CultureInfo.InvariantCulture);
                values.Add(negative ? -number : number);
            }
            else
            {
                throw Syntax(token,
                    "IN list supports string and numeric literals only "
                    + "(NULL is meaningless here — non-NOT NULL rules skip NULL values).");
            }
            _parser.Advance();

            if (_parser.Match(TokenType.COMMA)) continue;
            break;
        }
        _parser.Consume(TokenType.RPAREN, "Expected ')' to close the IN list");

        if (values.Count == 0)
            throw Syntax(_parser.Previous, "IN list must contain at least one literal.");
        return new InListRule(values, negated) { Text = string.Empty };
    }

    private ColumnRule ParseExists()
    {
        _parser.Advance(); // EXISTS

        if (_parser.Current.Type == TokenType.WITH)
        {
            _parser.Advance();
            var sourceColumns = ParseColumnList("EXISTS WITH");
            if (_parser.Current.Type != TokenType.IN)
                throw Syntax(_parser.Current, "EXISTS WITH (cols) expects IN <table>(cols).");
            _parser.Advance();
            var table = ParseQualifiedName("EXISTS WITH reference table");
            var keyColumns = ParseColumnList("EXISTS WITH reference");
            if (sourceColumns.Count != keyColumns.Count)
                throw Syntax(_parser.Previous,
                    $"EXISTS WITH probes {sourceColumns.Count} column(s) against {keyColumns.Count} "
                    + "reference column(s); the tuples must have the same arity.");
            return new ExistsInRule(table, keyColumns, sourceColumns) { Text = string.Empty };
        }

        if (_parser.Current.Type != TokenType.IN)
            throw Syntax(_parser.Current,
                "EXISTS expects the form 'EXISTS IN table(KeyColumn)' or "
                + "'EXISTS WITH (col, …) IN table(KeyColumn, …)'.");
        _parser.Advance();
        var reference = ParseQualifiedName("EXISTS IN reference table");
        var columns = ParseColumnList("EXISTS IN reference");
        if (columns.Count != 1)
            throw Syntax(_parser.Previous,
                "EXISTS IN takes one key column. Use EXISTS WITH (cols) IN table(cols) for a tuple.");
        return new ExistsInRule(reference, columns[0]) { Text = string.Empty };
    }

    /// <summary>
    /// Every <c>LENGTH</c> form lowers onto one inclusive range; <c>&gt;</c>/<c>&lt;</c> shift the
    /// bound by one, which is only sound because a character count is an integer. A range no value
    /// can satisfy is an error rather than a rule that quarantines the whole table.
    /// </summary>
    private ColumnRule ParseLength()
    {
        _parser.Advance(); // LENGTH

        if (_parser.Current.Type == TokenType.BETWEEN)
        {
            _parser.Advance();
            var min = ParseWholeNumber("LENGTH BETWEEN minimum");
            _parser.Consume(TokenType.AND, "Expected AND between the LENGTH BETWEEN bounds");
            var max = ParseWholeNumber("LENGTH BETWEEN maximum");
            if (min > max)
                throw Syntax(_parser.Previous,
                    "LENGTH rule has a minimum above its maximum, so no value can satisfy it.");
            return new LengthRule(min, max) { Text = string.Empty };
        }

        if (!TryCompareOp(_parser.Current, out var op))
            throw Syntax(_parser.Current,
                "LENGTH expects 'LENGTH BETWEEN <min> AND <max>' or a comparison such as 'LENGTH >= 5'.");
        var opToken = _parser.Advance();
        var bound = ParseWholeNumber("LENGTH bound");

        return op switch
        {
            CompareOp.GreaterOrEqual => new LengthRule(bound, null) { Text = string.Empty },
            CompareOp.Greater => new LengthRule(bound + 1, null) { Text = string.Empty },
            CompareOp.LessOrEqual => new LengthRule(0, bound) { Text = string.Empty },
            CompareOp.Less when bound == 0 => throw Syntax(opToken,
                "LENGTH rule can never be satisfied — no value is shorter than zero characters."),
            CompareOp.Less => new LengthRule(0, bound - 1) { Text = string.Empty },
            _ => new LengthRule(bound, bound) { Text = string.Empty }
        };
    }

    /// <summary>
    /// <c>CASTABLE AS &lt;type&gt;[(p[,s])]</c>. The type is checked against the converter registry
    /// at parse time: an unregistered type makes the shared cast return the value unchanged, so the
    /// rule would accept every row — the failure mode a validity check must not have.
    /// </summary>
    private ColumnRule ParseCastable()
    {
        _parser.Advance(); // CASTABLE
        _parser.Consume(TokenType.AS, "Expected AS after CASTABLE");

        var typeToken = _parser.Current;
        if (!_parser.IsIdentifier(typeToken) && !_parser.IsDataType(typeToken.Type))
            throw Syntax(typeToken, $"CASTABLE AS expects a type name, got '{typeToken.Value}'.");
        _parser.Advance();
        var baseType = typeToken.Value.ToUpperInvariant();

        int? precision = null, scale = null;
        if (_parser.Match(TokenType.LPAREN))
        {
            precision = ParseWholeNumber("CASTABLE width");
            if (_parser.Match(TokenType.COMMA)) scale = ParseWholeNumber("CASTABLE scale");
            _parser.Consume(TokenType.RPAREN, "Expected ')' after the CASTABLE width");
        }

        if (!TypeConverter.IsRegistered(baseType))
            throw Syntax(typeToken,
                $"CASTABLE AS '{baseType}' names a type this engine has no conversion for, so the "
                + "rule would accept every value. Use a type CAST accepts.");
        if (precision is 0)
            throw Syntax(typeToken, "CASTABLE rule declares a width of zero, which no value can satisfy.");
        if (precision is { } p && scale is { } s && s > p)
            throw Syntax(typeToken, "CASTABLE rule declares more decimal places than total digits.");

        // Rebuilt from the parsed parts rather than sliced, so the width reaches the converter in
        // the canonical form it parses — forms it interprets itself, such as DATETIME(3) truncating
        // to a precision, keep behaving as they do in a CAST.
        var declaredType = precision switch
        {
            { } width when scale is { } places => $"{baseType}({width},{places})",
            { } width => $"{baseType}({width})",
            _ => baseType
        };
        return new CastableRule(declaredType, baseType, precision, scale) { Text = string.Empty };
    }

    private ColumnRule ParseExpr()
    {
        _parser.Advance(); // EXPR
        var predicate = ParseRuleExpression("EXPR predicate");
        return new ExprRule(predicate) { Text = string.Empty };
    }

    private ColumnRule ParseBetween()
    {
        _parser.Advance(); // BETWEEN
        var lower = _parser.ParseExpressionTerm();
        _parser.Consume(TokenType.AND, "Expected AND between the BETWEEN bounds");
        var upper = _parser.ParseExpressionTerm();
        return new BetweenRule(lower, upper) { Text = string.Empty };
    }

    private ColumnRule ParseComparison(CompareOp op)
    {
        _parser.Advance(); // operator
        var negative = false;
        if (_parser.Current.Type is TokenType.MINUS or TokenType.PLUS)
        {
            negative = _parser.Current.Type == TokenType.MINUS;
            _parser.Advance();
        }
        if (_parser.Current.Type != TokenType.NUMBER)
            throw Syntax(_parser.Current,
                "A comparison rule requires a numeric bound (compares are decimal at runtime). "
                + "For a non-numeric or computed bound use EXPR or BETWEEN.");
        var value = decimal.Parse(_parser.Advance().Value, CultureInfo.InvariantCulture);
        return new ComparisonRule(op, negative ? -value : value) { Text = string.Empty };
    }

    /// <summary>
    /// Parses a rule's embedded expression at comparison precedence, so a following rule-level
    /// <c>AND</c> is not swallowed into the expression. Parenthesize to use AND/OR inside one.
    /// </summary>
    private Expression ParseRuleExpression(string context)
    {
        try
        {
            return _parser.ParseExpressionNoLogical();
        }
        catch (SyntaxException ex)
        {
            throw new SyntaxException($"{context} is not a valid expression: {ex.Message}", ex.Line, ex.Column);
        }
    }

    private List<string> ParseColumnList(string ruleName)
    {
        _parser.Consume(TokenType.LPAREN, $"Expected '(' after {ruleName}");
        var columns = new List<string>();
        do
        {
            var token = _parser.Current;
            if (!_parser.IsIdentifier(token))
                throw Syntax(token, $"{ruleName} expects a list of column names, got '{token.Value}'.");
            columns.Add(_parser.Advance().Value);
        }
        while (_parser.Match(TokenType.COMMA));
        _parser.Consume(TokenType.RPAREN, $"Expected ')' after the {ruleName} column list");
        if (columns.Count == 0)
            throw Syntax(_parser.Previous, $"{ruleName} expects at least one column name.");
        return columns;
    }

    private string ParseQualifiedName(string context)
    {
        var token = _parser.Current;
        if (!_parser.IsIdentifier(token))
            throw Syntax(token, $"{context} expects a table name, got '{token.Value}'.");
        var name = new StringBuilder(_parser.Advance().Value);
        while (_parser.Current.Type == TokenType.DOT)
        {
            _parser.Advance();
            name.Append('.').Append(_parser.ConsumeIdentifier($"Expected a name after '.' in the {context}").Value);
        }
        return name.ToString();
    }

    private int ParseWholeNumber(string context)
    {
        var token = _parser.Current;
        if (token.Type != TokenType.NUMBER ||
            !int.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value < 0)
            throw Syntax(token, $"{context} requires a whole, non-negative number, got '{token.Value}'.");
        _parser.Advance();
        return value;
    }

    private static bool TryCompareOp(Token token, out CompareOp op)
    {
        switch (token.Type)
        {
            case TokenType.GREATER_EQUALS: op = CompareOp.GreaterOrEqual; return true;
            case TokenType.LESS_EQUALS: op = CompareOp.LessOrEqual; return true;
            case TokenType.GREATER_THAN: op = CompareOp.Greater; return true;
            case TokenType.LESS_THAN: op = CompareOp.Less; return true;
            case TokenType.EQUALS: op = CompareOp.Equal; return true;
            default: op = CompareOp.Equal; return false;
        }
    }

    /// <summary>
    /// Each atom is built with an empty <see cref="ColumnRule.Text"/>; <see cref="ParseAtom"/>
    /// fills it in from the source once the atom's extent is known, so no atom has to re-derive
    /// its own text.
    /// </summary>
    private static ColumnRule CloneWithText(ColumnRule rule, string text) => rule with { Text = text };

    /// <summary>
    /// The text spanned by a token range, so a rule reports itself as written — this is what
    /// <c>__dq_rule</c> and every diagnostic quote back.
    /// </summary>
    private string Slice(Token start, Token end) => _parser.SliceSource(start, end);

    private static bool IsWord(Token token, string word) =>
        token.Value.Equals(word, StringComparison.OrdinalIgnoreCase);

    private static SyntaxException Syntax(Token token, string message) =>
        new(message, token.Line, token.Column);
}
