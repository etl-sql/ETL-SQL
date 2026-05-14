using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Validates that BULK INSERT numeric options (BATCHSIZE, MAXERRORS) are positive integers.
    /// </summary>
    public class BulkInsertOptionsRule : ILintRule
    {
        public string Name => "BulkInsertOptions";
        public string Description => "Ensures BATCHSIZE and MAXERRORS in BULK INSERT are valid non-negative integers.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            foreach (var statement in script.Statements)
                AnalyzeStatement(statement, results);
            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void AnalyzeStatement(Statement statement, List<LintResult> results)
        {
            if (statement is BulkInsertStatement bulk)
            {
                ValidateIntOption(bulk, "BATCHSIZE", minValue: 1, results);
                ValidateIntOption(bulk, "MAXERRORS", minValue: 0, results);
            }

            if (statement is BlockStatement block)
                foreach (var s in block.Statements) AnalyzeStatement(s, results);
            else if (statement is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody, results);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, results);
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results);
            }
            else if (statement is WhileStatement w) AnalyzeStatement(w.Body, results);
            else if (statement is ForStatement f) AnalyzeStatement(f.Body, results);
            else if (statement is ForeachStatement fe) AnalyzeStatement(fe.Body, results);
        }

        private static void ValidateIntOption(BulkInsertStatement stmt, string key, int minValue, List<LintResult> results)
        {
            if (!stmt.Options.TryGetValue(key, out var expr)) return;
            if (expr is not LiteralExpression literal) return;

            var raw = literal.Value?.ToString() ?? "";
            if (!int.TryParse(raw, out var val) || val < minValue)
            {
                results.Add(new LintResult
                {
                    RuleName = "BulkInsertOptions",
                    Severity = LintSeverity.Error,
                    Message = $"BULK INSERT option {key} must be an integer ≥ {minValue}. Got: '{raw}'.",
                    LineNumber = stmt.Line,
                    ColumnNumber = stmt.Column
                });
            }
        }
    }
}
