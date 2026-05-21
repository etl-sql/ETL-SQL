using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Linting.Rules
{
    public class SchemaValidationRule : ILintRule
    {
        public string Name => "SchemaValidation";
        public string Description => "Validates that tables and columns exist in the connected sources.";

        public async Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            if (context.Metadata == null) return results;

            var scriptConnections = CollectConnections(script);

            foreach (var statement in script.Statements)
            {
                await AnalyzeStatementAsync(statement, context, results, scriptConnections);
            }
            return results;
        }

        private Dictionary<string, CreateConnectionStatement> CollectConnections(Script script)
        {
            var connections = new Dictionary<string, CreateConnectionStatement>(StringComparer.OrdinalIgnoreCase);
            CollectConnectionsFromStatements(script.Statements, connections);
            return connections;
        }

        private void CollectConnectionsFromStatements(IEnumerable<Statement> statements, Dictionary<string, CreateConnectionStatement> connections)
        {
            foreach (var stmt in statements)
            {
                if (stmt is CreateConnectionStatement conn)
                {
                    connections[conn.ConnectionName] = conn;
                }
                else if (stmt is BlockStatement block)
                {
                    CollectConnectionsFromStatements(block.Statements, connections);
                }
                else if (stmt is IfStatement ifStmt)
                {
                    CollectConnectionsFromStatements(new[] { ifStmt.IfBody }, connections);
                    if (ifStmt.ElseIfClauses != null)
                    {
                        foreach (var ei in ifStmt.ElseIfClauses)
                        {
                            CollectConnectionsFromStatements(new[] { ei.Body }, connections);
                        }
                    }
                    if (ifStmt.ElseBody != null)
                    {
                        CollectConnectionsFromStatements(new[] { ifStmt.ElseBody }, connections);
                    }
                }
                else if (stmt is WhileStatement whileStmt)
                {
                    CollectConnectionsFromStatements(new[] { whileStmt.Body }, connections);
                }
                else if (stmt is ForStatement forStmt)
                {
                    CollectConnectionsFromStatements(new[] { forStmt.Body }, connections);
                }
                else if (stmt is ForeachStatement foreachStmt)
                {
                    CollectConnectionsFromStatements(new[] { foreachStmt.Body }, connections);
                }
                else if (stmt is TryCatchStatement tryCatch)
                {
                    CollectConnectionsFromStatements(new[] { tryCatch.TryBody }, connections);
                    CollectConnectionsFromStatements(new[] { tryCatch.CatchBody }, connections);
                }
            }
        }

        private string? ResolvePath(string path, string documentUri)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (System.IO.Path.IsPathRooted(path))
            {
                return path;
            }

            if (!string.IsNullOrWhiteSpace(documentUri))
            {
                try
                {
                    string baseDir = "";
                    if (documentUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        var uri = new Uri(documentUri);
                        baseDir = System.IO.Path.GetDirectoryName(uri.LocalPath) ?? "";
                    }
                    else if (System.IO.Path.IsPathRooted(documentUri))
                    {
                        baseDir = System.IO.Path.GetDirectoryName(documentUri) ?? "";
                    }

                    if (!string.IsNullOrEmpty(baseDir))
                    {
                        return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
                    }
                }
                catch
                {
                    // Ignore URI/Path exceptions
                }
            }

            try
            {
                return System.IO.Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }

        private async Task AnalyzeStatementAsync(Statement statement, ILintContext context, List<LintResult> results, Dictionary<string, CreateConnectionStatement> scriptConnections)
        {
            if (statement is SelectStatement select)
            {
                var tablesInScope = new List<(string Conn, string Table, string? Alias)>();
                
                // 1. Resolve Tables in FROM and JOIN
                if (select.FromTable != null)
                {
                    await ValidateTableRefAsync(select.FromTable, context, results, tablesInScope, scriptConnections, isInsert: false);
                }
                foreach (var join in select.Joins)
                {
                    await ValidateTableRefAsync(join.Table, context, results, tablesInScope, scriptConnections, isInsert: false);
                }

                // 2. Validate Columns
                foreach (var col in select.Columns)
                {
                    await ValidateExpressionAsync(col.Expression, context, results, tablesInScope);
                }
                if (select.WhereClause != null) await ValidateExpressionAsync(select.WhereClause, context, results, tablesInScope);
                if (select.GroupBy != null) foreach (var g in select.GroupBy) await ValidateExpressionAsync(g, context, results, tablesInScope);
                if (select.HavingClause != null) await ValidateExpressionAsync(select.HavingClause, context, results, tablesInScope);
                if (select.OrderBy != null) foreach (var o in select.OrderBy) await ValidateExpressionAsync(o.Expression, context, results, tablesInScope);
            }
            else if (statement is InsertStatement insert)
            {
                var tablesInScope = new List<(string Conn, string Table, string? Alias)>();
                await ValidateTableRefAsync(insert.TargetTable, context, results, tablesInScope, scriptConnections, isInsert: true);
                if (insert.SelectQuery != null) await AnalyzeStatementAsync(insert.SelectQuery, context, results, scriptConnections);
                if (insert.Columns != null && tablesInScope.Any())
                {
                    var scope = tablesInScope[0];
                    var cols = (await context.Metadata!.GetColumnsAsync(scope.Conn, scope.Table)).ToList();
                    
                    // Relaxed validation for file connectors: if no columns are found, assume implied schema creation
                    string? connType = null;
                    if (scriptConnections.TryGetValue(scope.Conn, out var connStmt))
                    {
                        connType = connStmt.ConnectionType;
                    }
                    else
                    {
                        connType = context.Metadata!.GetConnectionType(scope.Conn);
                    }
                    if (cols.Count == 0 && IsFileConnector(connType))
                    {
                        return;
                    }

                    foreach (var col in insert.Columns)
                    {
                        if (!cols.Any(c => string.Equals(c, col, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new LintResult {
                                RuleName = Name,
                                Severity = LintSeverity.Warning,
                                Message = $"Column '{col}' not found in target table '{scope.Table}'.",
                                LineNumber = insert.Line,
                                ColumnNumber = insert.Column
                            });
                        }
                    }
                }
            }
            else if (statement is UpdateStatement update)
            {
                var tablesInScope = new List<(string Conn, string Table, string? Alias)>();
                await ValidateTableRefAsync(update.TargetTable, context, results, tablesInScope, scriptConnections, isInsert: false);
                foreach (var assign in update.Assignments)
                {
                    await ValidateExpressionAsync(assign.Value, context, results, tablesInScope);
                    // Validate target column
                    if (tablesInScope.Any())
                    {
                        var scope = tablesInScope[0];
                        var cols = await context.Metadata!.GetColumnsAsync(scope.Conn, scope.Table);
                        if (!cols.Any(c => string.Equals(c, assign.ColumnName, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new LintResult {
                                RuleName = Name,
                                Severity = LintSeverity.Warning,
                                Message = $"Column '{assign.ColumnName}' not found in target table '{scope.Table}'.",
                                LineNumber = assign.Line,
                                ColumnNumber = assign.Column
                            });
                        }
                    }
                }
                if (update.WhereClause != null) await ValidateExpressionAsync(update.WhereClause, context, results, tablesInScope);
            }
            else if (statement is DeleteStatement delete)
            {
                var tablesInScope = new List<(string Conn, string Table, string? Alias)>();
                await ValidateTableRefAsync(delete.TargetTable, context, results, tablesInScope, scriptConnections, isInsert: false);
                if (delete.WhereClause != null) await ValidateExpressionAsync(delete.WhereClause, context, results, tablesInScope);
            }
            else if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) await AnalyzeStatementAsync(s, context, results, scriptConnections);
            }
            else if (statement is IfStatement ifStmt)
            {
                await AnalyzeStatementAsync(ifStmt.IfBody, context, results, scriptConnections);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) await AnalyzeStatementAsync(ei.Body, context, results, scriptConnections);
                if (ifStmt.ElseBody != null) await AnalyzeStatementAsync(ifStmt.ElseBody, context, results, scriptConnections);
            }
            else if (statement is WhileStatement whileStmt)
            {
                await AnalyzeStatementAsync(whileStmt.Body, context, results, scriptConnections);
            }
            else if (statement is ForStatement forStmt)
            {
                await AnalyzeStatementAsync(forStmt.Body, context, results, scriptConnections);
            }
            else if (statement is ForeachStatement foreachStmt)
            {
                await AnalyzeStatementAsync(foreachStmt.Body, context, results, scriptConnections);
            }
            else if (statement is TryCatchStatement tryCatch)
            {
                await AnalyzeStatementAsync(tryCatch.TryBody, context, results, scriptConnections);
                await AnalyzeStatementAsync(tryCatch.CatchBody, context, results, scriptConnections);
            }
        }

        private async Task ValidateTableRefAsync(TableReference tableRef, ILintContext context, List<LintResult> results, List<(string Conn, string Table, string? Alias)> tablesInScope, Dictionary<string, CreateConnectionStatement> scriptConnections, bool isInsert)
        {
            if (tableRef.Subquery != null)
            {
                // Recursive analysis for subqueries if needed
                return;
            }

            var connName = tableRef.ConnectionName ?? context.Metadata!.GetConnections().FirstOrDefault() ?? "DEFAULT";
            string? connType = null;
            CreateConnectionStatement? connStmt = null;
            if (scriptConnections.TryGetValue(connName, out connStmt))
            {
                connType = connStmt.ConnectionType;
            }
            else
            {
                connType = context.Metadata!.GetConnectionType(connName);
            }
            
            // Skip validation for engine-side temporary tables (#) or built-in DUAL
            if ((tableRef.ConnectionName == null && tableRef.TableName.StartsWith("#")) || 
                string.Equals(tableRef.TableName, "DUAL", StringComparison.OrdinalIgnoreCase))
            {
                // We use the table name as the connection identity for engine-side tables
                tablesInScope.Add((tableRef.TableName, tableRef.TableName, tableRef.Alias));
                return;
            }

            if (IsFileConnector(connType))
            {
                if (connStmt != null && connStmt.TargetExpression is LiteralExpression targetLit && targetLit.Value is string targetPath)
                {
                    if (!isInsert)
                    {
                        var resolvedPath = ResolvePath(targetPath, context.DocumentUri);
                        if (!string.IsNullOrEmpty(resolvedPath))
                        {
                            try
                            {
                                if (!System.IO.File.Exists(resolvedPath))
                                {
                                    results.Add(new LintResult
                                    {
                                        RuleName = Name,
                                        Severity = LintSeverity.Warning,
                                        Message = $"File '{targetPath}' for connection '{connName}' does not exist.",
                                        LineNumber = tableRef.Line,
                                        ColumnNumber = tableRef.Column
                                    });
                                }
                            }
                            catch
                            {
                                // Ignore
                            }
                        }
                    }
                }
            }

            if (string.Equals(tableRef.TableName, "FILE", StringComparison.OrdinalIgnoreCase))
            {
                if (IsFileConnector(connType))
                {
                    tablesInScope.Add((connName, tableRef.TableName, tableRef.Alias));
                    return;
                }
            }

            // Skip validation for DOCKER (b1)
            if (connType == "DOCKER")
            {
                tablesInScope.Add((connName, tableRef.TableName, tableRef.Alias));
                return;
            }

            var tables = await context.Metadata!.GetTablesAsync(connName);
            if (tables == null) return; 
            
            string searchName = NormalizeName(tableRef.TableName);
            if (!tables.Any(t => string.Equals(NormalizeName(t), searchName, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new LintResult {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Table '{tableRef.TableName}' not found in connection '{connName}'.",
                    LineNumber = tableRef.Line,
                    ColumnNumber = tableRef.Column
                });
            }
            else
            {
                tablesInScope.Add((connName, tableRef.TableName, tableRef.Alias));
            }
        }

        private string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string clean = name.Trim('[', ']', '"');
            if (clean.Contains('.')) clean = clean.Split('.').Last();
            return clean;
        }

        private async Task ValidateExpressionAsync(Expression expr, ILintContext context, List<LintResult> results, List<(string Conn, string Table, string? Alias)> tablesInScope)
        {
            if (expr is IdentifierExpression id)
            {
                // Handle qualified names (table.col or conn.table.col)
                var parts = id.Name.Split('.');
                if (parts.Length == 1)
                {
                    if (parts[0] == "*") return; // Asterisk is a meta-column, skip physical validation
                    
                    // Unqualified column - check all tables in scope
                    bool found = false;
                    foreach (var scope in tablesInScope)
                    {
                        // Skip validation for engine-managed temporary tables (#)
                        if (scope.Table.StartsWith("#"))
                        {
                            found = true;
                            break;
                        }

                        var cols = await context.Metadata!.GetColumnsAsync(scope.Conn, scope.Table);
                        if (cols.Any(c => string.Equals(c, parts[0], StringComparison.OrdinalIgnoreCase)))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found && tablesInScope.Any())
                    {
                         results.Add(new LintResult {
                            RuleName = Name,
                            Severity = LintSeverity.Warning,
                            Message = $"Column '{parts[0]}' not found in any table in the current scope.",
                            LineNumber = id.Line,
                            ColumnNumber = id.Column
                        });
                    }
                }
                else if (parts.Length == 2)
                {
                    // table.col or alias.col
                    var qualifier = parts[0];
                    var colName = parts[1];
                    var scope = tablesInScope.FirstOrDefault(s => 
                        string.Equals(s.Alias, qualifier, StringComparison.OrdinalIgnoreCase) || 
                        string.Equals(s.Table, qualifier, StringComparison.OrdinalIgnoreCase));

                    if (scope != default)
                    {
                        // Skip validation for engine-managed temporary tables (#)
                        if (scope.Table.StartsWith("#")) return;

                        var cols = await context.Metadata!.GetColumnsAsync(scope.Conn, scope.Table);
                        if (!cols.Any(c => string.Equals(c, colName, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new LintResult {
                                RuleName = Name,
                                Severity = LintSeverity.Warning,
                                Message = $"Column '{colName}' not found in table '{scope.Table}'.",
                                LineNumber = id.Line,
                                ColumnNumber = id.Column
                            });
                        }
                    }
                }
            }
            else if (expr is BinaryExpression binary)
            {
                await ValidateExpressionAsync(binary.Left, context, results, tablesInScope);
                await ValidateExpressionAsync(binary.Right, context, results, tablesInScope);
            }
            else if (expr is FunctionCallExpression call)
            {
                foreach (var arg in call.Arguments) await ValidateExpressionAsync(arg, context, results, tablesInScope);
            }
            // Add more expression types (Unary, etc.)
        }

        private bool IsFileConnector(string? type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            var t = type.ToUpperInvariant();
            return t == "FLATFILE" || t == "CSV" || t == "EXCEL" || t == "JSON" || 
                   t == "XML" || t == "AVRO" || t == "PARQUET";
        }
    }
}
