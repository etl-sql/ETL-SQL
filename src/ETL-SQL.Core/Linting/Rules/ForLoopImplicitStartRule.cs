using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Provides an informational lint message when a FOR loop start value is omitted,
    /// explicitly stating that it defaults to 1.
    /// </summary>
    public class ForLoopImplicitStartRule : ILintRule
    {
        public string Name => "ForLoopImplicitStart";
        public string Description => "Informs that a FOR loop without an explicit start value defaults to 1.";

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
            if (statement is ForStatement forStmt)
            {
                if (forStmt.IsStartImplicit)
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Info,
                        Message = "No start value provided; FOR loop defaults to 1.",
                        LineNumber = forStmt.Line,
                        ColumnNumber = forStmt.Column
                    });
                }
                AnalyzeStatement(forStmt.Body, results);
            }
            else if (statement is ParallelForStatement pForStmt)
            {
                if (pForStmt.IsStartImplicit)
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Info,
                        Message = "No start value provided; PARALLEL FOR loop defaults to 1.",
                        LineNumber = pForStmt.Line,
                        ColumnNumber = pForStmt.Column
                    });
                }
                AnalyzeStatement(pForStmt.Body, results);
            }
            else if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) AnalyzeStatement(s, results);
            }
            else if (statement is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody, results);
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, results);
                }
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results);
            }
            else if (statement is WhileStatement whileStmt)
            {
                AnalyzeStatement(whileStmt.Body, results);
            }
            else if (statement is TryCatchStatement tryCatch)
            {
                AnalyzeStatement(tryCatch.TryBody, results);
                AnalyzeStatement(tryCatch.CatchBody, results);
            }
        }
    }
}
