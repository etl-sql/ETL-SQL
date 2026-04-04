using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Linting.Rules
{
    public class SchemaValidationRule : ILintRule
    {
        public string Name => "SchemaValidation";
        public string Description => "Validates that tables and columns exist in the connected sources.";

        public async Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            if (context.Metadata == null) return results;

            foreach (var statement in script.Statements)
            {
                await AnalyzeStatementAsync(statement, context, results);
            }
            return results;
        }

        private async Task AnalyzeStatementAsync(Statement statement, ILintContext context, List<LintResult> results)
        {
            if (statement is SelectStatement select)
            {
                var tablesInScope = new List<(string Conn, string Table, string? Alias)>();
                
                // 1. Resolve Tables in FROM and JOIN
                if (select.FromTable != null)
                {
                    await ValidateTableRefAsync(select.FromTable, context, results, tablesInScope);
                }
                foreach (var join in select.Joins)
                {
                    await ValidateTableRefAsync(join.Table, context, results, tablesInScope);
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
                await ValidateTableRefAsync(insert.TargetTable, context, results, tablesInScope);
                if (insert.SelectQuery != null) await AnalyzeStatementAsync(insert.SelectQuery, context, results);
                // Validate columns in INTO clause
                if (insert.Columns != null && tablesInScope.Any())
                {
                    var scope = tablesInScope[0];
                    var cols = await context.Metadata!.GetColumnsAsync(scope.Conn, scope.Table);
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
                await ValidateTableRefAsync(update.TargetTable, context, results, tablesInScope);
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
                await ValidateTableRefAsync(delete.TargetTable, context, results, tablesInScope);
                if (delete.WhereClause != null) await ValidateExpressionAsync(delete.WhereClause, context, results, tablesInScope);
            }
            else if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) await AnalyzeStatementAsync(s, context, results);
            }
        }

        private async Task ValidateTableRefAsync(TableReference tableRef, ILintContext context, List<LintResult> results, List<(string Conn, string Table, string? Alias)> tablesInScope)
        {
            if (tableRef.Subquery != null)
            {
                // Recursive analysis for subqueries if needed
                return;
            }

            var connName = tableRef.ConnectionName ?? context.Metadata!.GetConnections().FirstOrDefault() ?? "DEFAULT";
            
            // Skip temp tables - they are dynamic and not in the static metadata
            if (tableRef.TableName.StartsWith("#"))
            {
                tablesInScope.Add((connName, tableRef.TableName, tableRef.Alias));
                return;
            }

            var tables = await context.Metadata!.GetTablesAsync(connName);
            
            if (!tables.Any(t => string.Equals(t, tableRef.TableName, StringComparison.OrdinalIgnoreCase)))
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

        private async Task ValidateExpressionAsync(Expression expr, ILintContext context, List<LintResult> results, List<(string Conn, string Table, string? Alias)> tablesInScope)
        {
            if (expr is IdentifierExpression id)
            {
                // Handle qualified names (table.col or conn.table.col)
                var parts = id.Name.Split('.');
                if (parts.Length == 1)
                {
                    // Unqualified column - check all tables in scope
                    bool found = false;
                    foreach (var scope in tablesInScope)
                    {
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
    }
}
