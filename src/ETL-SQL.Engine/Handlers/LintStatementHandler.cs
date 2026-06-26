using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the LINT statement, performing static analysis on script files to identify potential issues and best practice violations.
/// </summary>
public class LintStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;
    public Type SupportedStatementType => typeof(LintStatement);

    public LintStatementHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Executes the LINT statement, running all registered static analysis rules and returning the results as a table.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var lintStmt = (LintStatement)statement;
        string? sql = null;
        string? lintDocumentUri = null;

        if (lintStmt.ScriptPath != null)
        {
            var fullPath = Path.IsPathRooted(lintStmt.ScriptPath)
                ? lintStmt.ScriptPath
                : Path.Combine(Directory.GetCurrentDirectory(), lintStmt.ScriptPath);

            if (!File.Exists(fullPath))
            {
                _logger.Error("LINT target file not found: {Path}", null, fullPath);
                throw new ExecutionException($"Script file not found: {fullPath}", null, lintStmt.Line, lintStmt.Column);
            }

            _logger.Info("Linting script file: {Path}", fullPath);
            sql = await File.ReadAllTextAsync(fullPath);
            lintDocumentUri = fullPath;
        }
        else
        {
            throw new ExecutionException(
                "LINT without a file path is not yet supported. Use LINT 'path.sql';",
                null, lintStmt.Line, lintStmt.Column);
        }

        var lexer = new Lexer(sql);
        var tokens = lexer.Tokenize();
        var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
        var script = parser.Parse();

        var linter = LinterFactory.CreateWithAllRules();

        var results = await linter.AnalyzeAsync(script, new DefaultLintContext { DocumentUri = lintDocumentUri ?? string.Empty });

        var table = new DataTable();
        table.AddColumn("Severity");
        table.AddColumn("Rule");
        table.AddColumn("Line");
        table.AddColumn("Message");

        foreach (var res in results.OrderBy(r => r.LineNumber))
        {
            var row = new Row();
            row["Severity"] = res.Severity.ToString();
            row["Rule"] = res.RuleName;
            row["Line"] = res.LineNumber;
            row["Message"] = res.Message;
            await table.AddRowAsync(row);
        }

        _logger.Info("Linting completed for {Path}. Findings: {Count}", lintStmt.ScriptPath, results.Count);
        context.LastResult = table;
    }
}
