using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Services;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Flags explicit disabling of spill security features (encryption or compression).
/// These features protect intermediate query results from exposure on disk.
/// </summary>
public class SpillSecurityRule : ILintRule
{
    public string Name => "SpillSecurity";
    public string Description => "Warns when disk-spill encryption or compression is explicitly disabled.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        foreach (var statement in script.Statements)
        {
            AnalyzeStatement(statement, results);
        }
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void AnalyzeStatement(Statement statement, List<LintResult> results, int depth = 0)
    {
        if (depth > 50)
        {
            throw new SecurityException("Script nesting level exceeds the maximum allowed security depth (50). Refactor the script to use fewer nested blocks.");
        }

        if (statement is SetSpillOptionStatement spill)
        {
            if (!spill.Enabled)
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Security Warning: Disabling {spill.Option} can expose intermediate data to the host's disk in plain text. Ensure this script is running in an approved safe zone.",
                    LineNumber = spill.Line,
                    ColumnNumber = spill.Column
                });
            }
        }

        // Recurse into blocks/containers
        if (statement is BlockStatement block)
        {
            foreach (var s in block.Statements) AnalyzeStatement(s, results, depth + 1);
        }
        else if (statement is IfStatement ifStmt)
        {
            AnalyzeStatement(ifStmt.IfBody, results, depth + 1);
            if (ifStmt.ElseIfClauses != null)
                foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, results, depth + 1);
            if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results, depth + 1);
        }
        else if (statement is WhileStatement whileStmt)
        {
            AnalyzeStatement(whileStmt.Body, results, depth + 1);
        }
        else if (statement is ForStatement forStmt)
        {
            AnalyzeStatement(forStmt.Body, results, depth + 1);
        }
        else if (statement is ForeachStatement foreachStmt)
        {
            AnalyzeStatement(foreachStmt.Body, results, depth + 1);
        }
        else if (statement is TryCatchStatement tryCatch)
        {
            AnalyzeStatement(tryCatch.TryBody, results, depth + 1);
            AnalyzeStatement(tryCatch.CatchBody, results, depth + 1);
        }
    }
}
