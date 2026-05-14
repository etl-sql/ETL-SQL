using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Warns when a flat-file connection or BULK INSERT specifies a DELIMITER that is the same
    /// as the ROW_DELIMITER. Matching delimiters make the file unparseable at read time.
    /// </summary>
    public class FlatFileDelimiterConflictRule : ILintRule
    {
        public string Name => "FlatFileDelimiterConflict";
        public string Description => "Ensures DELIMITER and ROW_DELIMITER are distinct for flat-file connections.";

        private static readonly HashSet<string> FlatFileTypes = new(StringComparer.OrdinalIgnoreCase)
            { "FLATFILE", "CSV", "TSV", "PSV", "FIXED" };

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            foreach (var statement in script.Statements)
                AnalyzeStatement(statement, results);
            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void AnalyzeStatement(Statement statement, List<LintResult> results)
        {
            if (statement is CreateConnectionStatement conn
                && conn.ConnectionType != null
                && FlatFileTypes.Contains(conn.ConnectionType)
                && conn.Options != null)
            {
                CheckDelimiters(conn.Options, statement, results);
            }
            else if (statement is BulkInsertStatement bulk)
            {
                CheckDelimiters(bulk.Options, statement, results);
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

        private static void CheckDelimiters(Dictionary<string, Expression> options, Statement stmt, List<LintResult> results)
        {
            var delim = GetLiteralValue(options, "DELIMITER") ?? GetLiteralValue(options, "FIELDTERMINATOR");
            var rowDelim = GetLiteralValue(options, "ROW_DELIMITER") ?? GetLiteralValue(options, "ROWTERMINATOR");
            if (delim == null || rowDelim == null) return;

            if (string.Equals(delim, rowDelim, StringComparison.Ordinal))
            {
                results.Add(new LintResult
                {
                    RuleName = "FlatFileDelimiterConflict",
                    Severity = LintSeverity.Error,
                    Message = $"DELIMITER and ROW_DELIMITER are both '{delim}'. They must be distinct or the file will be unparseable.",
                    LineNumber = stmt.Line,
                    ColumnNumber = stmt.Column
                });
            }
        }

        private static string? GetLiteralValue(Dictionary<string, Expression> options, string key)
        {
            if (!options.TryGetValue(key, out var expr)) return null;
            if (expr is LiteralExpression lit) return lit.Value?.ToString();
            if (expr is IdentifierExpression id) return id.Name;
            return null;
        }
    }
}
