using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Linting;
/// <summary>
/// Orchestrates the linting process by executing multiple <see cref="ILintRule"/> instances.
/// </summary>
public class Linter
{
    private readonly List<ILintRule> _rules = new();
    private readonly ILogger? _logger;

    public Linter(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Adds a new linting rule to the linter.</summary>
    public void AddRule(ILintRule rule)
    {
        _rules.Add(rule);
    }

    /// <summary>Checks if a rule of the specified type exists.</summary>
    public bool HasRuleOfType(Type type) => _rules.Any(r => r.GetType() == type);

    /// <summary>Analyzes the script against all registered rules.</summary>
    public async Task<List<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var logger = context.Logger ?? _logger;

        // 1. Discovery Pass (populate in-script metadata)
        var overlay = new ScriptMetadataOverlay(context.Metadata);
        DiscoverScriptMetadata(script, overlay);

        // 2. Wrap context with overlay
        if (context is DefaultLintContext defaultContext)
        {
            defaultContext.Metadata = overlay;
        }

        // 3. Execute Rules
        var rules = _rules.ToArray();
        var ruleTasks = rules.Select(rule => AnalyzeRuleAsync(rule, script, context, logger));
        var ruleResults = await Task.WhenAll(ruleTasks);
        return ruleResults.SelectMany(r => r).ToList();
    }

    private async Task<IReadOnlyCollection<LintResult>> AnalyzeRuleAsync(ILintRule rule, Script script, ILintContext context, ILogger? logger)
    {
        try
        {
            var results = await rule.AnalyzeAsync(script, context);
            return results as IReadOnlyCollection<LintResult> ?? results.ToList();
        }
        catch (Exception ex)
        {
            logger?.Error("Lint rule {RuleName} failed.", ex, rule.Name);
            return Array.Empty<LintResult>();
        }
    }

    private void DiscoverScriptMetadata(Script script, ScriptMetadataOverlay overlay)
    {
        foreach (var statement in script.Statements)
        {
            DiscoverFromStatement(statement, overlay);
        }
    }

    private void DiscoverFromStatement(Statement statement, ScriptMetadataOverlay overlay)
    {
        DiscoverCtes(statement, overlay);

        switch (statement)
        {
            case CreateConnectionStatement conn: DiscoverCreateConnection(conn, overlay); break;
            case CreateTableStatement create: DiscoverCreateTable(create, overlay); break;
            case ExecutePushdownStatement pushdown: DiscoverPushdown(pushdown, overlay); break;
            case BlockStatement block: DiscoverBlock(block, overlay); break;
            case IfStatement ifStmt: DiscoverIf(ifStmt, overlay); break;
            case WhileStatement whileStmt: DiscoverFromStatement(whileStmt.Body, overlay); break;
            case ForStatement forStmt: DiscoverFromStatement(forStmt.Body, overlay); break;
            case ForeachStatement foreachStmt: DiscoverFromStatement(foreachStmt.Body, overlay); break;
            case TryCatchStatement tryCatch: DiscoverTryCatch(tryCatch, overlay); break;
            case CreateProcedureStatement proc: DiscoverFromStatement(proc.Body, overlay); break;
            case CreateFunctionStatement func: DiscoverFromStatement(func.Body, overlay); break;
            case ParallelStatement parallel: DiscoverFromStatement(parallel.Body, overlay); break;
            case ParallelForStatement parallelFor: DiscoverFromStatement(parallelFor.Body, overlay); break;
        }
    }

    private static void DiscoverCtes(Statement statement, ScriptMetadataOverlay overlay)
    {
        if (statement.Ctes == null) return;
        foreach (var cte in statement.Ctes)
        {
            overlay.RegisterTable("DEFAULT", cte.Name);
            if (cte.ColumnNames != null)
                foreach (var col in cte.ColumnNames) overlay.RegisterColumn("DEFAULT", cte.Name, col);
        }
    }

    private static void DiscoverCreateConnection(CreateConnectionStatement conn, ScriptMetadataOverlay overlay)
        => overlay.RegisterConnection(conn.name, conn.type ?? "UNKNOWN");

    private void DiscoverCreateTable(CreateTableStatement create, ScriptMetadataOverlay overlay)
    {
        string connName = create.TargetTable.ConnectionName ?? "DEFAULT";
        string tableName = NormalizeName(create.TargetTable.TableName);
        overlay.RegisterTable(connName, tableName);
        foreach (var col in create.Columns)
            overlay.RegisterColumn(connName, tableName, col.ColumnName);
    }

    private void DiscoverPushdown(ExecutePushdownStatement pushdown, ScriptMetadataOverlay overlay)
    {
        string connName = pushdown.ConnectionName is IdentifierExpression id ? id.Name : "DEFAULT";
        DiscoverFromNativeBlock(pushdown.SqlText, connName, overlay);
    }

    private void DiscoverBlock(BlockStatement block, ScriptMetadataOverlay overlay)
    {
        foreach (var s in block.Statements) DiscoverFromStatement(s, overlay);
    }

    private void DiscoverIf(IfStatement ifStmt, ScriptMetadataOverlay overlay)
    {
        DiscoverFromStatement(ifStmt.IfBody, overlay);
        if (ifStmt.ElseIfClauses != null)
            foreach (var ei in ifStmt.ElseIfClauses) DiscoverFromStatement(ei.Body, overlay);
        if (ifStmt.ElseBody != null) DiscoverFromStatement(ifStmt.ElseBody, overlay);
    }

    private void DiscoverTryCatch(TryCatchStatement tryCatch, ScriptMetadataOverlay overlay)
    {
        DiscoverFromStatement(tryCatch.TryBody, overlay);
        DiscoverFromStatement(tryCatch.CatchBody, overlay);
    }

    private void DiscoverFromNativeBlock(string sql, string connectionName, ScriptMetadataOverlay overlay)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;

        // 1. Try Parsing as ETL-SQL
        try
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
            while (parser.Current.Type != TokenType.EOF)
            {
                var stmt = parser.ParseStatement();
                if (stmt is CreateTableStatement create)
                {
                    string tableName = NormalizeName(create.TargetTable.TableName);
                    overlay.RegisterTable(connectionName, tableName);
                    foreach (var col in create.Columns) overlay.RegisterColumn(connectionName, tableName, col.ColumnName);
                }
            }
            return; // Success
        }
        catch { /* Fallback to Regex */ }

        // 2. Regex Fallback for "Big Stuff" discovery
        // CREATE TABLE [dbo.]TableName
        var createMatches = Regex.Matches(sql, @"CREATE\s+TABLE\s+(?:[\w\[\]""]+\.)?([\w\[\]""]+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        foreach (Match match in createMatches)
        {
            string tableName = NormalizeName(match.Groups[1].Value);
            overlay.RegisterTable(connectionName, tableName);
        }
    }

    private string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // Strip brackets, quotes, and common schema prefixes like dbo.
        string clean = name.Trim('[', ']', '"');
        if (clean.Contains('.')) clean = clean.Split('.').Last();
        return clean;
    }
}
