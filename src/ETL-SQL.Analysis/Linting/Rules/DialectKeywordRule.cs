using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Analysis.Linting.Rules
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
            }
            else if (stmt is AlterConnectionStatement alter)
            {
                if (alter.ConnectionName != null && alter.ConnectionType != null)
                    map[alter.ConnectionName] = alter.ConnectionType;
            }
            else if (stmt is BlockStatement block)
            {
                foreach (var s in block.Statements) CollectConnections(s, map);
            }
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
            if (stmt is SelectStatement select)
            {
                var connName = select.FromTable?.ConnectionName;
                if (!string.IsNullOrEmpty(connName))
                {
                    var dialect = GetDialectExclusions(connName, connMap, registry, context);
                    if (dialect.Excluded != null)
                    {
                        // 1. Check Top/Limit/Percent properties
                        if (select.TopCount != null && dialect.Excluded.Contains("TOP"))
                        {
                            results.Add(new LintResult
                            {
                                RuleName = "DialectKeyword",
                                Severity = LintSeverity.Warning,
                                Message = $"Keyword 'TOP' is not supported in {dialect.ConnType} dialect for connection '{connName}'.",
                                LineNumber = select.Line,
                                ColumnNumber = select.Column
                            });
                        }
                        if (select.LimitCount != null && dialect.Excluded.Contains("LIMIT"))
                        {
                            results.Add(new LintResult
                            {
                                RuleName = "DialectKeyword",
                                Severity = LintSeverity.Warning,
                                Message = $"Keyword 'LIMIT' is not supported in {dialect.ConnType} dialect for connection '{connName}'.",
                                LineNumber = select.Line,
                                ColumnNumber = select.Column
                            });
                        }
                        if (select.IsTopPercent && dialect.Excluded.Contains("PERCENT"))
                        {
                            results.Add(new LintResult
                            {
                                RuleName = "DialectKeyword",
                                Severity = LintSeverity.Warning,
                                Message = $"Keyword 'PERCENT' is not supported in {dialect.ConnType} dialect for connection '{connName}'.",
                                LineNumber = select.Line,
                                ColumnNumber = select.Column
                            });
                        }

                        // 2. Walk expressions for excluded identifiers (like ROWNUM)
                        foreach (var col in select.Columns) AnalyzeExpression(col.Expression, connName, dialect.ConnType ?? "", dialect.Excluded, results);
                        if (select.WhereClause != null) AnalyzeExpression(select.WhereClause, connName, dialect.ConnType ?? "", dialect.Excluded, results);
                        if (select.GroupBy != null) foreach (var g in select.GroupBy) AnalyzeExpression(g, connName, dialect.ConnType ?? "", dialect.Excluded, results);
                        if (select.HavingClause != null) AnalyzeExpression(select.HavingClause, connName, dialect.ConnType ?? "", dialect.Excluded, results);
                        if (select.OrderBy != null) foreach (var o in select.OrderBy) AnalyzeExpression(o.Expression, connName, dialect.ConnType ?? "", dialect.Excluded, results);
                    }
                }
                
                // Recurse into subqueries in FROM and JOINs
                if (select.FromTable?.Subquery != null) AnalyzeStatement(select.FromTable.Subquery, connMap, registry, results, context);
                foreach (var join in select.Joins)
                {
                    if (join.Table.Subquery != null) AnalyzeStatement(join.Table.Subquery, connMap, registry, results, context);
                }
                return;
            }

            if (stmt is ExecutePushdownStatement pushdown)
            {
                var connName = pushdown.ConnectionName is IdentifierExpression id ? id.Name : pushdown.ConnectionName.ToSql();
                var dialect = GetDialectExclusions(connName, connMap, registry, context);

                if (dialect.Excluded != null)
                {
                    foreach (Match match in WordPattern.Matches(pushdown.SqlText))
                    {
                        if (dialect.Excluded.Contains(match.Value))
                        {
                            results.Add(new LintResult
                            {
                                RuleName = "DialectKeyword",
                                Severity = LintSeverity.Warning,
                                Message = $"Keyword '{match.Value.ToUpperInvariant()}' is not supported in {dialect.ConnType} dialect for connection '{connName}'. Check your pushdown SQL.",
                                LineNumber = pushdown.Line,
                                ColumnNumber = pushdown.Column
                            });
                        }
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

        private static (string? ConnType, HashSet<string>? Excluded) GetDialectExclusions(string connName, Dictionary<string, string> connMap, IConnectorRegistry registry, ILintContext context)
        {
            if (!connMap.TryGetValue(connName, out var connType))
            {
                connType = context.Metadata?.GetConnectionType(connName);
            }

            if (string.IsNullOrEmpty(connType)) return (null, null);

            var connector = registry.GetConnector(connType);
            return (connType, connector?.GetExcludedKeywords());
        }

        private static void AnalyzeExpression(Expression expr, string connName, string connType, HashSet<string> excluded, List<LintResult> results)
        {
            if (expr is IdentifierExpression id)
            {
                if (excluded.Contains(id.Name))
                {
                    results.Add(new LintResult
                    {
                        RuleName = "DialectKeyword",
                        Severity = LintSeverity.Warning,
                        Message = $"Identifier '{id.Name.ToUpperInvariant()}' is not supported in {connType} dialect for connection '{connName}'.",
                        LineNumber = id.Line,
                        ColumnNumber = id.Column
                    });
                }
            }
            else if (expr is FunctionCallExpression call)
            {
                if (excluded.Contains(call.FunctionName))
                {
                    results.Add(new LintResult
                    {
                        RuleName = "DialectKeyword",
                        Severity = LintSeverity.Warning,
                        Message = $"Function '{call.FunctionName.ToUpperInvariant()}' is not supported in {connType} dialect for connection '{connName}'.",
                        LineNumber = call.Line,
                        ColumnNumber = call.Column
                    });
                }
                foreach (var arg in call.Arguments) AnalyzeExpression(arg, connName, connType, excluded, results);
            }
            else if (expr is BinaryExpression binary)
            {
                AnalyzeExpression(binary.Left, connName, connType, excluded, results);
                AnalyzeExpression(binary.Right, connName, connType, excluded, results);
            }
            else if (expr is UnaryExpression unary)
            {
                AnalyzeExpression(unary.Expression, connName, connType, excluded, results);
            }
            else if (expr is MemberAccessExpression member)
            {
                AnalyzeExpression(member.Expression, connName, connType, excluded, results);
            }
            else if (expr is ListExpression list)
            {
                foreach (var item in list.Items) AnalyzeExpression(item, connName, connType, excluded, results);
            }
        }
    }
}
