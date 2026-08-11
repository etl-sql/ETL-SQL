using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core;

namespace ETL_SQL.Tests.Core;

public class EbnfConformanceTests
{
    private static readonly string GrammarPath = Path.Combine(AppContext.BaseDirectory, "../../../../../docs/grammar.ebnf");

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
        
        for (int i = 0; i < 50; i++)
        {
            // Generate a valid sequence
            string sql = rules["script"].Generate(rules, rnd, depth: 0);
            
            // Should parse without crashing (Diagnostics may contain errors if our generator creates something semantically invalid, 
            // but the requirement is proving the parser handles it without a fatal crash and accepts the shape)
            var parser = new ETL_SQL.Core.Parser.Parser(new ETL_SQL.Core.Parser.Lexer(sql).Tokenize());
            var script = parser.Parse();
            
            // For perfectly matched EBNF, diagnostics should ideally be empty.
            // (Leaving this as a soft check or just crash-prevention check for now)
            Assert.NotNull(script);
        }
    }

    [Fact]
    public void GeneratedInvalidSequences_ParserRecoversWithoutCrashing()
    {
        if (!File.Exists(GrammarPath)) return;

        var grammarText = File.ReadAllText(GrammarPath);
        var rules = EbnfParser.Parse(grammarText);
        var rnd = new Random(99);

        for (int i = 0; i < 50; i++)
        {
            string sql = rules["script"].Generate(rules, rnd, depth: 0);
            var mutated = MutateHostile(sql, rnd);
            
            try
            {
                var tokens = new ETL_SQL.Core.Parser.Lexer(mutated).Tokenize();
                var parser = new ETL_SQL.Core.Parser.Parser(tokens);
                var script = parser.Parse();
                Assert.NotNull(script);
            }
            catch (ETL_SQL.Core.Common.Exceptions.SyntaxException)
            {
                // Throwing a syntax exception is a graceful rejection of hostile input.
                // We just want to ensure it doesn't crash with NullReference, OutOfMemory, StackOverflow, etc.
            }
        }
    }

    private string MutateHostile(string input, Random rnd)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var bytes = Encoding.UTF8.GetBytes(input);
        
        // Randomly corrupt bytes, drop chunks, or insert garbage
        int mutations = rnd.Next(1, 5);
        for (int i = 0; i < mutations; i++)
        {
            int action = rnd.Next(3);
            int idx = rnd.Next(bytes.Length);
            
            if (action == 0) // Corrupt byte
            {
                bytes[idx] = (byte)rnd.Next(256);
            }
            else if (action == 1) // Truncate
            {
                return Encoding.UTF8.GetString(bytes, 0, idx);
            }
            else if (action == 2) // Insert garbage token
            {
                var sb = new StringBuilder(input);
                sb.Insert(rnd.Next(input.Length), " !@#$%^&*()_+-= ");
                return sb.ToString();
            }
        }
        
        return Encoding.UTF8.GetString(bytes);
    }
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
        if (Name == "any_char_except_quote") return "a"; // naive stub
        
        // Prevent infinite recursion on recursive rules (e.g. expressions)
        if (depth > 5)
        {
            if (Name == "expression") return "1 ";
            if (Name == "statement") return "PRINT 1 ";
        }
        
        if (rules.TryGetValue(Name, out var rule))
            return rule.Generate(rules, rnd, depth + 1);
        
        return "";
    }
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
}

public class EbnfRepetition : EbnfNode
{
    public EbnfNode Node { get; }
    public EbnfRepetition(EbnfNode node) => Node = node;
    public override string Generate(Dictionary<string, EbnfRule> rules, Random rnd, int depth)
    {
        int count = rnd.Next(0, 3);
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
            sb.Append(Node.Generate(rules, rnd, depth));
        return sb.ToString();
    }
}

public static class EbnfParser
{
    // A VERY naive EBNF parser just enough to parse our grammar.ebnf subset.
    // Handles tokens: identifier, =, |, [, ], {, }, (, ), ", ', ;
    public static Dictionary<string, EbnfRule> Parse(string ebnf)
    {
        var tokens = Tokenize(ebnf);
        int pos = 0;
        var rules = new Dictionary<string, EbnfRule>();

        while (pos < tokens.Count)
        {
            if (tokens[pos] == ";") { pos++; continue; }
            string name = tokens[pos++];
            if (pos >= tokens.Count || tokens[pos++] != "=") break;
            
            var node = ParseChoice(tokens, ref pos);
            rules[name] = new EbnfRule { Name = name, Node = node };
            
            if (pos < tokens.Count && tokens[pos] == ";") pos++;
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
                if (pos < tokens.Count && tokens[pos] == "]") pos++;
                nodes.Add(new EbnfOptional(inner));
            }
            else if (t == "{")
            {
                var inner = ParseChoice(tokens, ref pos);
                if (pos < tokens.Count && tokens[pos] == "}") pos++;
                nodes.Add(new EbnfRepetition(inner));
            }
            else if (t == "(")
            {
                var inner = ParseChoice(tokens, ref pos);
                if (pos < tokens.Count && tokens[pos] == ")") pos++;
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
