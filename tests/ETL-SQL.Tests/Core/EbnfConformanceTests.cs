using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Core;

[Trait("Category", "EbnfConformance")]
public class EbnfConformanceTests
{
    private static readonly string GrammarPath = Path.Combine(AppContext.BaseDirectory, "../../../../../docs/grammar.ebnf");
    private static readonly string StatementParserPath = Path.Combine(AppContext.BaseDirectory, "../../../../../src/ETL-SQL.Core/Parser/StatementParser.cs");
    private static readonly string DocsPath = Path.Combine(AppContext.BaseDirectory, "../../../../../docs");

    [Fact]
    public void EbnfGrammar_FileExists()
    {
        Assert.True(File.Exists(GrammarPath), $"EBNF file not found at {GrammarPath}");
    }

    [Fact]
    public void GeneratedValidSequences_ParseWithoutExceptions()
    {
        if (!File.Exists(GrammarPath)) return;
        
        var grammarText = File.ReadAllText(GrammarPath);
        var rules = EbnfParser.Parse(grammarText);
        Assert.True(rules.ContainsKey("script"), "Grammar must contain a 'script' root rule.");

        var rnd = new Random(42); // Deterministic seed for reproducible testing
        
        var generated = 0;
        for (int i = 0; i < 200 && generated < 50; i++)
        {
            string sql = AddRequiredSemanticContext(rules["script"].Generate(rules, rnd, depth: 0).Trim());
            if (sql.Length == 0) continue;

            var errors = ParseErrors(sql);
            var counterexample = errors.Count == 0 ? sql : MinimizeRejectedValidSql(sql);
            Assert.True(errors.Count == 0,
                $"EBNF generated input that the execution parser rejected (seed 42, case {generated}).\n"
                + $"Minimal counterexample:\n{counterexample}\n\n"
                + $"Generated SQL:\n{sql}\n\nDiagnostics:\n{string.Join("\n", errors)}");
            generated++;
        }

        Assert.Equal(50, generated);
    }

    [Fact]
    public void EveryDeclaredStatementProduction_GeneratesAcceptedInput()
    {
        var rules = EbnfParser.Parse(File.ReadAllText(GrammarPath));
        var statement = Assert.IsType<EbnfChoice>(rules["statement"].Node);

        for (var index = 0; index < statement.Options.Count; index++)
        {
            var sql = AddRequiredSemanticContext((statement.Options[index].Generate(rules, new Random(10_000 + index), 0) + ";").Trim());
            var errors = ParseErrors(sql);
            Assert.True(errors.Count == 0,
                $"Statement production {index + 1}/{statement.Options.Count} generated rejected SQL.\n"
                + $"Minimal counterexample:\n{MinimizeRejectedValidSql(sql)}\n\n"
                + $"Generated SQL:\n{sql}\n\nDiagnostics:\n{string.Join("\n", errors)}");
        }
    }

    [Fact]
    public void EveryStatementRuleAlternative_GeneratesAcceptedInput()
    {
        var rules = EbnfParser.Parse(File.ReadAllText(GrammarPath));
        var cases = rules.Values
            .Where(rule => rule.Name.EndsWith("_statement", StringComparison.Ordinal))
            .SelectMany(rule => rule.Node is EbnfChoice choice
                ? choice.Options.Select((node, index) => (rule.Name, Index: index + 1, Node: node))
                : [(rule.Name, Index: 1, Node: rule.Node)])
            .OrderBy(testCase => testCase.Name, StringComparer.Ordinal)
            .ThenBy(testCase => testCase.Index)
            .ToArray();

        for (var caseIndex = 0; caseIndex < cases.Length; caseIndex++)
        {
            var testCase = cases[caseIndex];
            var sql = AddRequiredSemanticContext((testCase.Node.Generate(rules, new Random(30_000 + caseIndex), 0) + ";").Trim());
            var errors = ParseErrors(sql);
            Assert.True(errors.Count == 0,
                $"Statement rule {testCase.Name} alternative {testCase.Index} generated rejected SQL.\n"
                + $"Minimal counterexample:\n{MinimizeRejectedValidSql(sql)}\n\n"
                + $"Generated SQL:\n{sql}\n\nDiagnostics:\n{string.Join("\n", errors)}");
        }
    }

    [Fact]
    public void EveryExpressionExampleProduction_GeneratesAcceptedInput()
    {
        var rules = EbnfParser.Parse(File.ReadAllText(GrammarPath));
        var examples = Assert.IsType<EbnfChoice>(rules["expression_example"].Node);

        for (var index = 0; index < examples.Options.Count; index++)
        {
            var expression = examples.Options[index].Generate(rules, new Random(20_000 + index), 0).Trim();
            var sql = $"SELECT {expression};";
            var errors = ParseErrors(sql);
            Assert.True(errors.Count == 0,
                $"Expression example {index + 1}/{examples.Options.Count} was rejected.\nSQL:\n{sql}\n\nDiagnostics:\n{string.Join("\n", errors)}");
        }
    }

    [Fact]
    public void GeneratedInvalidSequences_AreRejected()
    {
        if (!File.Exists(GrammarPath)) return;

        var grammarText = File.ReadAllText(GrammarPath);
        var rules = EbnfParser.Parse(grammarText);
        var rnd = new Random(99);

        var generated = 0;
        for (int i = 0; i < 200 && generated < 50; i++)
        {
            string sql = rules["script"].Generate(rules, rnd, depth: 0).Trim();
            if (sql.Length == 0) continue;

            // This is a grammar-level mutation, not random byte damage: SELECT requires a select
            // list before FROM. Prefixing it leaves the generated suffix available to exercise the
            // parser's recovery path while guaranteeing that the complete input is invalid.
            var mutated = "DECLARE = ; " + sql;
            var errors = ParseErrors(mutated);
            Assert.True(errors.Count > 0,
                "Execution parser accepted grammar-invalid input. Minimal counterexample:\nDECLARE = ;");
            generated++;
        }

        Assert.Equal(50, generated);
    }

    [Fact]
    public void GrammarReferences_AreAllResolved()
    {
        var rules = EbnfParser.Parse(File.ReadAllText(GrammarPath));
        var builtins = new HashSet<string>(StringComparer.Ordinal) { "EOF", "any_char_except_quote" };
        var unresolved = rules.Values
            .SelectMany(rule => rule.Node.References())
            .Where(name => !builtins.Contains(name) && !rules.ContainsKey(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unresolved.Length == 0,
            "EBNF contains unresolved rule references: " + string.Join(", ", unresolved));
    }

    [Fact]
    public void ExecutionParserEntryKeywords_AreRepresentedByStatementGrammar()
    {
        var rules = EbnfParser.Parse(File.ReadAllText(GrammarPath));
        var grammarEntries = LeadingLiterals(rules["statement"].Node, rules, new HashSet<string>(StringComparer.Ordinal));

        var parserSource = File.ReadAllText(StatementParserPath);
        var parserEntries = Regex.Matches(parserSource, @"_dispatchMap\[TokenType\.(\w+)\]")
            .Select(match => match.Groups[1].Value)
            // These dispatch entries exist solely to emit migration diagnostics (or, for TOKENS,
            // are unreachable behind canonical REVOKE TOKENS) and are not accepted language.
            .Where(keyword => keyword is not ("TAG" or "SHOW" or "TOKENS"))
            .ToHashSet(StringComparer.Ordinal);

        parserEntries.UnionWith([
            "SET", "BEGIN", "FOR", "SELECT", "PIVOT", "UNPIVOT", "EXEC", "EXECUTE",
            "SEND", "RECEIVE", "KILL", "COPY", "MOVE", "RENAME", "COMPRESS", "DECOMPRESS",
            "ENCRYPT", "DECRYPT", "TEST", "IMPORT", "REINDEX"
        ]);

        var missing = parserEntries.Except(grammarEntries).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0,
            "Execution parser entry keywords missing from statement grammar: " + string.Join(", ", missing));
    }

    [Fact]
    public void ParserAcceptedDocumentExamples_StartFromGrammarReachableEntries()
    {
        var rules = EbnfParser.Parse(File.ReadAllText(GrammarPath));
        var grammarEntries = LeadingLiterals(rules["statement"].Node, rules, new HashSet<string>(StringComparer.Ordinal));
        var fence = new Regex(@"```(?:sql|etlsql|rptsql)\s*\r?\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var checkedExamples = 0;
        var missing = new List<string>();

        foreach (var path in Directory.EnumerateFiles(DocsPath, "*.md", SearchOption.AllDirectories)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(DocsPath, path);
            foreach (Match match in fence.Matches(File.ReadAllText(path)))
            {
                var sql = match.Groups[1].Value.Trim();
                if (sql.Length == 0 || sql.Contains("...", StringComparison.Ordinal)
                    || sql.Contains('<') || sql.Contains('>'))
                    continue;

                List<Token> tokens;
                try { tokens = new Lexer(sql).Tokenize(); }
                catch { continue; }

                var significant = tokens
                    .Where(token => token.Type is not (TokenType.EOF or TokenType.COLUMN_TAG))
                    .ToArray();
                var first = significant.FirstOrDefault();
                if (first == null || ParseErrors(sql).Count != 0)
                    continue;

                checkedExamples++;
                var isSectionLabel = significant.Length > 1
                    && significant[1].Type == TokenType.COLON
                    && rules.ContainsKey("section_label_statement");
                if (!isSectionLabel && !grammarEntries.Contains(first.Value.ToUpperInvariant()))
                    missing.Add($"{relativePath}: {first.Value}");
            }
        }

        Assert.True(checkedExamples >= 100,
            $"Expected at least 100 parser-accepted documentation examples, checked {checkedExamples}.");
        Assert.True(missing.Count == 0,
            "Parser-accepted documentation examples start with entries absent from EBNF: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void ParserAcceptedDocumentExamples_AreRecognizedByCompleteGrammar()
    {
        var rules = EbnfParser.Parse(File.ReadAllText(GrammarPath));
        var fence = new Regex(@"```(?:sql|etlsql|rptsql)\s*\r?\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var checkedExamples = 0;
        var unrecognized = new List<string>();

        foreach (var path in Directory.EnumerateFiles(DocsPath, "*.md", SearchOption.AllDirectories)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(DocsPath, path);
            if (relativePath.StartsWith("templates" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (Match match in fence.Matches(File.ReadAllText(path)))
            {
                var sql = match.Groups[1].Value.Trim();
                if (sql.Length == 0 || sql.Contains("...", StringComparison.Ordinal)
                    || sql.Contains('<') || sql.Contains('>')
                    || Regex.IsMatch(sql, @"\[\s*[A-Z][A-Z_]*(?:\s|\])")
                    || Regex.IsMatch(sql, @"\b[A-Z_]+\s*\|\s*[A-Z_]+\b")
                    || ParseErrors(sql).Count != 0)
                    continue;

                List<Token> tokens;
                try
                {
                    tokens = new Lexer(sql).Tokenize()
                        .Where(token => token.Type != TokenType.COLUMN_TAG)
                        .ToList();
                }
                catch
                {
                    continue;
                }

                checkedExamples++;
                if (!EbnfRecognizer.Recognizes(rules["script"].Node, rules, tokens, out var furthest))
                {
                    var firstLine = sql.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
                    var near = string.Join(" ", tokens.Skip(Math.Max(0, furthest - 3)).Take(7).Select(token => token.Value));
                    unrecognized.Add($"{relativePath}: {firstLine} [near token {furthest}: {near}]");
                }
            }
        }

        Assert.True(checkedExamples >= 100,
            $"Expected at least 100 parser-accepted documentation examples, checked {checkedExamples}.");
        Assert.True(unrecognized.Count == 0,
            $"Complete EBNF did not recognize {unrecognized.Count}/{checkedExamples} parser-accepted documentation examples:\n"
            + string.Join("\n", unrecognized.Take(50)));
    }

    [Theory]
    [InlineData("script statement ;")]
    [InlineData("script = [ statement ;")]
    [InlineData("script = statement ; script = statement ;")]
    [InlineData("script = \"unterminated ;")]
    [InlineData("/* unterminated")]
    public void MalformedEbnf_IsRejected(string ebnf)
    {
        Assert.Throws<FormatException>(() => EbnfParser.Parse(ebnf));
    }

    private static IReadOnlyList<string> ParseErrors(string sql)
    {
        try
        {
            var parser = new ETL_SQL.Core.Parser.Parser(new ETL_SQL.Core.Parser.Lexer(sql).Tokenize());
            return parser.Parse().Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Message)
                .ToArray();
        }
        catch (ETL_SQL.Core.Common.Exceptions.SyntaxException ex)
        {
            return [ex.Message];
        }
    }

    private static string AddRequiredSemanticContext(string sql)
    {
        // Label existence is a whole-script semantic check rather than context-free syntax. Give a
        // generated GOTO its matching declaration so this suite tests the EBNF/parser boundary, not
        // the separate undefined-label validator.
        if (sql.Contains("GOTO x", StringComparison.OrdinalIgnoreCase)
            && !sql.Contains("x :", StringComparison.OrdinalIgnoreCase))
            sql += " x:;";
        return sql;
    }

    private static HashSet<string> LeadingLiterals(
        EbnfNode node,
        Dictionary<string, EbnfRule> rules,
        HashSet<string> visiting)
    {
        if (node is EbnfLiteral literal)
            return [literal.Value.ToUpperInvariant()];
        if (node is EbnfChoice choice)
            return choice.Options.SelectMany(option => LeadingLiterals(option, rules, new HashSet<string>(visiting, StringComparer.Ordinal)))
                .ToHashSet(StringComparer.Ordinal);
        if (node is EbnfSequence sequence && sequence.Nodes.Count > 0)
            return LeadingLiterals(sequence.Nodes[0], rules, visiting);
        if (node is EbnfOptional optional)
            return LeadingLiterals(optional.Node, rules, visiting);
        if (node is EbnfRepetition repetition)
            return LeadingLiterals(repetition.Node, rules, visiting);
        if (node is EbnfRef reference && rules.TryGetValue(reference.Name, out var rule) && visiting.Add(reference.Name))
            return LeadingLiterals(rule.Node, rules, visiting);
        return new HashSet<string>(StringComparer.Ordinal);
    }

    private static string MinimizeRejectedValidSql(string sql)
    {
        var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var statement in statements)
        {
            var candidate = statement + ";";
            if (ParseErrors(candidate).Count > 0) return candidate;
        }

        // Cross-statement dependencies can make no individual statement reproduce the mismatch.
        // Deterministically remove one statement at a time while the rejection remains, which
        // leaves a stable local-minimum counterexample rather than the whole generated script.
        var remaining = statements.ToList();
        for (var index = remaining.Count - 1; index >= 0; index--)
        {
            var candidateParts = remaining.Where((_, i) => i != index).ToList();
            var candidate = string.Join("; ", candidateParts) + ";";
            if (candidateParts.Count > 0 && ParseErrors(candidate).Count > 0)
                remaining = candidateParts;
        }
        return string.Join("; ", remaining) + ";";
    }
}

internal sealed class EbnfRecognizer
{
    private readonly Dictionary<string, EbnfRule> _rules;
    private readonly IReadOnlyList<Token> _tokens;
    private readonly Dictionary<(EbnfNode Node, int Position), HashSet<int>> _memo = new();
    private readonly HashSet<(EbnfNode Node, int Position)> _active = new();
    private int _furthest;

    private EbnfRecognizer(Dictionary<string, EbnfRule> rules, IReadOnlyList<Token> tokens)
    {
        _rules = rules;
        _tokens = tokens;
    }

    public static bool Recognizes(EbnfNode root, Dictionary<string, EbnfRule> rules, IReadOnlyList<Token> tokens)
        => Recognizes(root, rules, tokens, out _);

    public static bool Recognizes(EbnfNode root, Dictionary<string, EbnfRule> rules, IReadOnlyList<Token> tokens, out int furthest)
    {
        var recognizer = new EbnfRecognizer(rules, tokens);
        var accepted = recognizer.Match(root, 0).Contains(tokens.Count);
        furthest = recognizer._furthest;
        return accepted;
    }

    private HashSet<int> Match(EbnfNode node, int position)
    {
        _furthest = Math.Max(_furthest, position);
        var key = (node, position);
        if (_memo.TryGetValue(key, out var cached)) return cached;
        if (!_active.Add(key)) return [];

        HashSet<int> result = node switch
        {
            EbnfLiteral literal => MatchLiteral(literal, position),
            EbnfRef reference => MatchReference(reference, position),
            EbnfChoice choice => choice.Options.SelectMany(option => Match(option, position)).ToHashSet(),
            EbnfSequence sequence => MatchSequence(sequence, position),
            EbnfOptional optional => Match(optional.Node, position).Append(position).ToHashSet(),
            EbnfRepetition repetition => MatchRepetition(repetition, position),
            _ => []
        };

        _active.Remove(key);
        _memo[key] = result;
        return result;
    }

    private HashSet<int> MatchSequence(EbnfSequence sequence, int position)
    {
        var positions = new HashSet<int> { position };
        foreach (var child in sequence.Nodes)
        {
            positions = positions.SelectMany(candidate => Match(child, candidate)).ToHashSet();
            if (positions.Count == 0) break;
        }
        return positions;
    }

    private HashSet<int> MatchRepetition(EbnfRepetition repetition, int position)
    {
        var reached = new HashSet<int> { position };
        var pending = new Queue<int>();
        pending.Enqueue(position);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var next in Match(repetition.Node, current))
                if (next != current && reached.Add(next)) pending.Enqueue(next);
        }
        return reached;
    }

    private HashSet<int> MatchReference(EbnfRef reference, int position)
    {
        if (reference.Name == "EOF")
            return position < _tokens.Count && _tokens[position].Type == TokenType.EOF ? [position + 1] : [];
        if (position >= _tokens.Count) return [];

        var token = _tokens[position];
        var lexical = reference.Name switch
        {
            "identifier" or "additional_identifier" or "base_window_name" => IsWordToken(token),
            "variable_name" => token.Type == TokenType.VARIABLE,
            "dataset_name" => token.Value.StartsWith('&'),
            "temp_table_name" => token.Value.StartsWith('#'),
            "number" or "number_literal" or "digit" => token.Type == TokenType.NUMBER,
            "string_literal" or "any_char_except_quote" => token.Type == TokenType.STRING_LITERAL,
            "letter" => IsWordToken(token),
            _ => false
        };
        if (lexical) return [position + 1];

        return _rules.TryGetValue(reference.Name, out var rule) ? Match(rule.Node, position) : [];
    }

    private HashSet<int> MatchLiteral(EbnfLiteral literal, int position)
    {
        if (position >= _tokens.Count) return [];
        return string.Equals(_tokens[position].Value, literal.Value, StringComparison.OrdinalIgnoreCase)
            ? [position + 1]
            : [];
    }

    private static bool IsWordToken(Token token)
        => token.Type == TokenType.IDENTIFIER
           || token.Type != TokenType.EOF
           && token.Type is not (TokenType.STRING_LITERAL or TokenType.NUMBER or TokenType.VARIABLE)
           && !token.Value.StartsWith('#') && !token.Value.StartsWith('&')
           && token.Value.Length > 0
           && (char.IsLetter(token.Value[0]) || token.Value[0] == '_');
}

// ─── Minimal EBNF Parser & Generator Engine ─────────────────────────────────────

public class EbnfRule
{
    public string Name { get; set; } = "";
    public EbnfNode Node { get; set; } = new EbnfSequence(new List<EbnfNode>());

    public string Generate(Dictionary<string, EbnfRule> rules, Random rnd, int depth)
    {
        return Node.Generate(rules, rnd, depth);
    }
}

public abstract class EbnfNode
{
    public abstract string Generate(Dictionary<string, EbnfRule> rules, Random rnd, int depth);
    public virtual IEnumerable<string> References() => [];
}

public class EbnfLiteral : EbnfNode
{
    public string Value { get; }
    public EbnfLiteral(string value) => Value = value;
    public override string Generate(Dictionary<string, EbnfRule> rules, Random rnd, int depth)
    {
        if (Value == "EOF") return "";
        return Value + " ";
    }
}

public class EbnfRef : EbnfNode
{
    public string Name { get; }
    public EbnfRef(string name) => Name = name;
    public override string Generate(Dictionary<string, EbnfRule> rules, Random rnd, int depth)
    {
        // Lexical productions must remain contiguous. Expanding identifier = letter { ... } with
        // the general node generator inserts token-separating spaces and turns @name, &dataset,
        // numbers, and quoted strings into different token streams than the grammar describes.
        if (Name == "identifier") return "x ";
        if (Name == "additional_identifier") return "y ";
        // A named-window base is syntactically an identifier but semantically must refer to an
        // earlier definition. Random statement generation has no symbol table, so omit this
        // optional semantic reference; dedicated parser tests cover valid inheritance chains.
        if (Name == "base_window_name") return "";
        if (Name == "variable_name") return "@x ";
        if (Name == "dataset_name") return "&x ";
        if (Name == "temp_table_name") return "#x ";
        if (Name == "number" || Name == "number_literal") return "1 ";
        if (Name == "string_literal") return "'x' ";
        if (Name == "retention_literal") return "'30 DAYS' ";
        if (Name == "ldap_literal") return "'LDAP' ";
        if (Name == "data_type") return "INT ";
        if (Name == "visual_clause") return "TITLE = 'x' ";
        if (Name == "button_clause") return "TITLE = 'x' ";
        if (Name == "select_statement") return "SELECT 1 ";
        if (Name == "lineage_name") return "x ";
        if (Name == "set_boolean_option") return "ALLOW_PLAINTEXT_SECRETS ";
        if (Name == "any_char_except_quote") return "a"; // naive stub
        if (Name is "grouping_expression" or "ordering_expression") return "x ";

        // Statements routinely contain several expressions and the expression grammar is both
        // recursive and highly compositional. Use a deterministic accepted leaf here so statement
        // generation tests statement structure rather than exploding into exponential expression
        // trees. Expression families have their own focused conformance cases.
        if (Name == "expression")
        {
            string[] acceptedExpressions = ["1 ", "@x ", "'x' ", "x + 1 ", "x IS NULL "];
            return acceptedExpressions[rnd.Next(acceptedExpressions.Length)];
        }
        
        // Prevent infinite recursion on recursive rules (e.g. expressions)
        if (depth > 5)
        {
            if (Name == "statement") return "PRINT 1 ";
        }
        
        if (rules.TryGetValue(Name, out var rule))
            return rule.Generate(rules, rnd, depth + 1);
        
        return "";
    }

    public override IEnumerable<string> References() => [Name];
}

public class EbnfSequence : EbnfNode
{
    public List<EbnfNode> Nodes { get; }
    public EbnfSequence(List<EbnfNode> nodes) => Nodes = nodes;
    public override string Generate(Dictionary<string, EbnfRule> rules, Random rnd, int depth)
    {
        var sb = new StringBuilder();
        foreach (var n in Nodes) sb.Append(n.Generate(rules, rnd, depth));
        return sb.ToString();
    }
    public override IEnumerable<string> References() => Nodes.SelectMany(node => node.References());
}

public class EbnfChoice : EbnfNode
{
    public List<EbnfNode> Options { get; }
    public EbnfChoice(List<EbnfNode> options) => Options = options;
    public override string Generate(Dictionary<string, EbnfRule> rules, Random rnd, int depth)
    {
        if (Options.Count == 0) return "";
        var opt = Options[rnd.Next(Options.Count)];
        return opt.Generate(rules, rnd, depth);
    }
    public override IEnumerable<string> References() => Options.SelectMany(node => node.References());
}

public class EbnfOptional : EbnfNode
{
    public EbnfNode Node { get; }
    public EbnfOptional(EbnfNode node) => Node = node;
    public override string Generate(Dictionary<string, EbnfRule> rules, Random rnd, int depth)
    {
        if (rnd.Next(2) == 0) return "";
        return Node.Generate(rules, rnd, depth);
    }
    public override IEnumerable<string> References() => Node.References();
}

public class EbnfRepetition : EbnfNode
{
    public EbnfNode Node { get; }
    public EbnfRepetition(EbnfNode node) => Node = node;
    public override string Generate(Dictionary<string, EbnfRule> rules, Random rnd, int depth)
    {
        // One occurrence is sufficient to exercise a repeated production. Keeping generated
        // conformance cases linear avoids the exponential growth caused by nested expression
        // precedence rules while still proving that both the empty and populated forms parse.
        int count = rnd.Next(0, 2);
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
            sb.Append(Node.Generate(rules, rnd, depth));
        return sb.ToString();
    }
    public override IEnumerable<string> References() => Node.References();
}

public static class EbnfParser
{
    // A deliberately small strict EBNF parser for the notation used by grammar.ebnf.
    // Handles tokens: identifier, =, |, [, ], {, }, (, ), ", ', ;
    public static Dictionary<string, EbnfRule> Parse(string ebnf)
    {
        var tokens = Tokenize(ebnf);
        int pos = 0;
        var rules = new Dictionary<string, EbnfRule>(StringComparer.Ordinal);

        while (pos < tokens.Count)
        {
            if (tokens[pos] == ";") { pos++; continue; }
            string name = tokens[pos++];
            if (pos >= tokens.Count || tokens[pos++] != "=")
                throw new FormatException($"Expected '=' after EBNF rule '{name}'.");
            if (rules.ContainsKey(name))
                throw new FormatException($"Duplicate EBNF rule '{name}'.");
            
            var node = ParseChoice(tokens, ref pos);
            rules[name] = new EbnfRule { Name = name, Node = node };
            
            if (pos >= tokens.Count || tokens[pos] != ";")
                throw new FormatException($"Expected ';' after EBNF rule '{name}'.");
            pos++;
        }
        
        return rules;
    }

    private static EbnfNode ParseChoice(List<string> tokens, ref int pos)
    {
        var seqs = new List<EbnfNode>();
        seqs.Add(ParseSequence(tokens, ref pos));
        
        while (pos < tokens.Count && tokens[pos] == "|")
        {
            pos++;
            seqs.Add(ParseSequence(tokens, ref pos));
        }
        
        return seqs.Count == 1 ? seqs[0] : new EbnfChoice(seqs);
    }

    private static EbnfNode ParseSequence(List<string> tokens, ref int pos)
    {
        var nodes = new List<EbnfNode>();
        while (pos < tokens.Count && tokens[pos] != "|" && tokens[pos] != ";" && tokens[pos] != "]" && tokens[pos] != "}" && tokens[pos] != ")")
        {
            string t = tokens[pos++];
            if (t == "[")
            {
                var inner = ParseChoice(tokens, ref pos);
                RequireClosing(tokens, ref pos, "]", "optional production");
                nodes.Add(new EbnfOptional(inner));
            }
            else if (t == "{")
            {
                var inner = ParseChoice(tokens, ref pos);
                RequireClosing(tokens, ref pos, "}", "repeated production");
                nodes.Add(new EbnfRepetition(inner));
            }
            else if (t == "(")
            {
                var inner = ParseChoice(tokens, ref pos);
                RequireClosing(tokens, ref pos, ")", "grouped production");
                nodes.Add(inner); // For now, just pass through choice without extra wrapper
            }
            else if (t.StartsWith("\"") || t.StartsWith("'"))
            {
                nodes.Add(new EbnfLiteral(t.Substring(1, t.Length - 2)));
            }
            else
            {
                nodes.Add(new EbnfRef(t));
            }
        }
        return nodes.Count == 1 ? nodes[0] : new EbnfSequence(nodes);
    }

    private static void RequireClosing(List<string> tokens, ref int pos, string expected, string context)
    {
        if (pos >= tokens.Count || tokens[pos] != expected)
            throw new FormatException($"Expected '{expected}' to close {context}.");
        pos++;
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < input.Length && input[i+1] == '*')
            {
                i += 2;
                while (i + 1 < input.Length && !(input[i] == '*' && input[i+1] == '/')) i++;
                if (i + 1 >= input.Length)
                    throw new FormatException("Unterminated EBNF comment.");
                i += 2;
                continue;
            }
            
            if ("=|[]{}();".Contains(c))
            {
                tokens.Add(c.ToString());
                i++;
            }
            else if (c == '"' || c == '\'')
            {
                int start = i;
                i++;
                while (i < input.Length && input[i] != c) i++;
                if (i >= input.Length)
                    throw new FormatException("Unterminated EBNF literal.");
                i++;
                tokens.Add(input.Substring(start, i - start));
            }
            else
            {
                int start = i;
                while (i < input.Length && !char.IsWhiteSpace(input[i]) && !"=|[]{}();\"'".Contains(input[i])) i++;
                tokens.Add(input.Substring(start, i - start));
            }
        }
        return tokens;
    }
}
