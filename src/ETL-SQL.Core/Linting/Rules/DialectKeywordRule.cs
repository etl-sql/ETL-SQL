using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Warns when a pushdown SQL block uses keywords excluded by the target connector's dialect.
    /// For example, using TOP in a Postgres pushdown, or LIMIT in a SQL Server pushdown.
    /// Requires ConnectorRegistry to be initialized; silently skips if it is not available.
    /// </summary>
    public class DialectKeywordRule : ILintRule
    {
        public string Name => "DialectKeyword";
        public string Description => "Warns when a pushdown SQL block uses keywords unsupported by the target connector's dialect.";

        private static readonly Regex WordPattern = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b");

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            var registry = ConnectorRegistry.Instance;
            if (registry == null)
                return Task.FromResult<IEnumerable<LintResult>>(results);

            // Build connection name → connector type map from CREATE CONNECTION statements
            var connMap = BuildConnectionMap(script.Statements);

            foreach (var statement in script.Statements)
            {
                AnalyzeStatement(statement, connMap, registry, results, context);
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private static Dictionary<string, string> BuildConnectionMap(IEnumerable<Statement> statements)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var stmt in statements)
            {
                CollectConnections(stmt, map);
            }
            return map;
        }

        private static void CollectConnections(Statement stmt, Dictionary<string, string> map)
        {
            if (stmt is CreateConnectionStatement conn)
            {
                if (conn.ConnectionName != null && conn.ConnectionType != null)
                    map[conn.ConnectionName] = conn.ConnectionType;
                return;
            }

            // Recurse into control flow blocks
            if (stmt is BlockStatement block)
                foreach (var s in block.Statements) CollectConnections(s, map);
            else if (stmt is IfStatement ifStmt)
            {
                CollectConnections(ifStmt.IfBody, map);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) CollectConnections(ei.Body, map);
                if (ifStmt.ElseBody != null) CollectConnections(ifStmt.ElseBody, map);
            }
            else if (stmt is WhileStatement whileStmt) CollectConnections(whileStmt.Body, map);
            else if (stmt is ForStatement forStmt) CollectConnections(forStmt.Body, map);
            else if (stmt is ForeachStatement foreachStmt) CollectConnections(foreachStmt.Body, map);
            else if (stmt is TryCatchStatement tryCatch)
            {
                CollectConnections(tryCatch.TryBody, map);
                CollectConnections(tryCatch.CatchBody, map);
            }
        }

        private static void AnalyzeStatement(Statement stmt, Dictionary<string, string> connMap,
            IConnectorRegistry registry, List<LintResult> results, ILintContext context)
        {
            if (stmt is ExecutePushdownStatement pushdown)
            {
                var connName = pushdown.ConnectionName is IdentifierExpression id ? id.Name : pushdown.ConnectionName.ToSql();
                
                if (!connMap.TryGetValue(connName, out var connType))
                {
                    // Fallback to metadata from context (for persistent sessions)
                    connType = context.Metadata?.GetConnectionType(connName);
                }

                if (string.IsNullOrEmpty(connType)) return;

                var connector = registry.GetConnector(connType);
                if (connector == null) return;

                var excluded = connector.GetExcludedKeywords();
                if (excluded.Count == 0) return;

                foreach (Match match in WordPattern.Matches(pushdown.SqlText))
                {
                    if (excluded.Contains(match.Value))
                    {
                        results.Add(new LintResult
                        {
                            RuleName = "DialectKeyword",
                            Severity = LintSeverity.Warning,
                            Message = $"Keyword '{match.Value.ToUpperInvariant()}' is not supported in {connType} dialect. Check your pushdown SQL for '{connName}'.",
                            LineNumber = pushdown.Line,
                            ColumnNumber = pushdown.Column
                        });
                    }
                }
                return;
            }

            // Recurse into control flow
            if (stmt is BlockStatement block)
                foreach (var s in block.Statements) AnalyzeStatement(s, connMap, registry, results, context);
            else if (stmt is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody, connMap, registry, results, context);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, connMap, registry, results, context);
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, connMap, registry, results, context);
            }
            else if (stmt is WhileStatement whileStmt) AnalyzeStatement(whileStmt.Body, connMap, registry, results, context);
            else if (stmt is ForStatement forStmt) AnalyzeStatement(forStmt.Body, connMap, registry, results, context);
            else if (stmt is ForeachStatement foreachStmt) AnalyzeStatement(foreachStmt.Body, connMap, registry, results, context);
            else if (stmt is TryCatchStatement tryCatch)
            {
                AnalyzeStatement(tryCatch.TryBody, connMap, registry, results, context);
                AnalyzeStatement(tryCatch.CatchBody, connMap, registry, results, context);
            }
        }
    }
}
