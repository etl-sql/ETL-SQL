using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Detects CREATE CONNECTION statements that specify both Windows authentication
    /// (TRUSTED_CONNECTION=TRUE) and SQL authentication (USER_ID / PASSWORD).
    /// These are mutually exclusive modes and combining them is always a misconfiguration.
    /// </summary>
    public class ConnectionAuthConflictRule : ILintRule
    {
        public string Name => "ConnectionAuthConflict";
        public string Description =>
            "Detects CREATE CONNECTION statements that set TRUSTED_CONNECTION=TRUE alongside USER_ID or PASSWORD. " +
            "Windows authentication and SQL authentication are mutually exclusive.";

        /// <summary>
        /// Connector types that support TRUSTED_CONNECTION / USER_ID auth options (database connectors).
        /// File-based connectors (FLATFILE, EXCEL, etc.) are exempt.
        /// </summary>
        private static readonly HashSet<string> DatabaseConnectors = new(StringComparer.OrdinalIgnoreCase)
        {
            "MSSQL", "SQLSERVER", "POSTGRES", "POSTGRESQL", "ORACLE", "MYSQL", "SQLITE", "ODBC"
        };

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
            if (statement is CreateConnectionStatement conn)
            {
                CheckConnection(conn, results);
            }

            // Recurse into control-flow blocks so connections inside IF/WHILE/etc. are also checked
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

        private void CheckConnection(CreateConnectionStatement conn, List<LintResult> results)
        {
            // Only applies to database-type connectors
            if (conn.ConnectionType == null || !DatabaseConnectors.Contains(conn.ConnectionType))
                return;

            if (conn.Options == null || conn.Options.Count == 0)
                return;

            string GetLiteral(Expression? expr) => expr is LiteralExpression lit ? lit.Value?.ToString() ?? "" : "";

            var tcVal = GetLiteral(conn.Options.GetValueOrDefault("TRUSTED_CONNECTION"));
            bool hasTrustedConnection = tcVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            if (!hasTrustedConnection)
                return;

            bool hasUserId = conn.Options.ContainsKey("USER_ID") || conn.Options.ContainsKey("UID");
            bool hasPassword = conn.Options.ContainsKey("PASSWORD");

            if (hasUserId)
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = $"Connection '{conn.ConnectionName}': TRUSTED_CONNECTION=TRUE (Windows auth) and USER_ID cannot be combined. " +
                                   "Remove TRUSTED_CONNECTION or remove USER_ID/PASSWORD.",
                    LineNumber = conn.Line,
                    ColumnNumber = conn.Column
                });
            }
            else if (hasPassword)
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = $"Connection '{conn.ConnectionName}': TRUSTED_CONNECTION=TRUE (Windows auth) and PASSWORD cannot be combined. " +
                                   "Remove TRUSTED_CONNECTION or remove USER_ID/PASSWORD.",
                    LineNumber = conn.Line,
                    ColumnNumber = conn.Column
                });
            }
        }
    }
}
