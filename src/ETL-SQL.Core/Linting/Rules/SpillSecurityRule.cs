using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
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

        private void AnalyzeStatement(Statement statement, List<LintResult> results)
        {
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
                foreach (var s in block.Statements) AnalyzeStatement(s, results);
            }
            else if (statement is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody, results);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, results);
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results);
            }
            else if (statement is WhileStatement whileStmt)
            {
                AnalyzeStatement(whileStmt.Body, results);
            }
            else if (statement is ForStatement forStmt)
            {
                AnalyzeStatement(forStmt.Body, results);
            }
            else if (statement is ForeachStatement foreachStmt)
            {
                AnalyzeStatement(foreachStmt.Body, results);
            }
            else if (statement is TryCatchStatement tryCatch)
            {
                AnalyzeStatement(tryCatch.TryBody, results);
                AnalyzeStatement(tryCatch.CatchBody, results);
            }
        }
    }
}
