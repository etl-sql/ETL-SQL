using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using Spectre.Console.Rendering;
using Spectre.Console;

namespace ETL_SQL.Engine.Handlers
{
    public class Counter { public int Value { get; set; } = 1; }

    /// <summary>
    /// Handles the EXPLAIN statement, generating and displaying a high-level execution plan for a given query.
    /// </summary>
    public class ExplainStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ExplainStatement);
        /// <summary>Executes the EXPLAIN statement, building a plan table and displaying it via Spectre.Console.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExplainStatement)statement;
            
            var plan = new DataTable();
            var columns = new List<string> { "ID", "Operation", "Details", "Cost" };
            if (stmt.IsAnalyze)
            {
                columns.Add("Actual Rows");
                columns.Add("Actual Time (ms)");
            }
            plan.SetColumns(columns.ToArray());
            
            Counter id = new Counter();
            var metrics = new ExecutionMetrics 
            { 
                Sql = (stmt.IsAnalyze ? "EXPLAIN ANALYZE: " : "EXPLAIN: ") + stmt.Query.ToSql(),
                Timestamp = DateTime.Now
            };

            // If ANALYZE, run the actual query first to collect metrics
            long actualRows = 0;
            long actualTime = 0;
            if (stmt.IsAnalyze)
            {
                var oldProfiling = context.IsProfiling;
                var oldRedirect = context.RedirectOutput;
                context.IsProfiling = true;
                context.RedirectOutput = true; // Don't print the actual rows to console

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await foreach (var batch in context.ExecuteQuery(stmt.Query))
                    {
                        actualRows += batch.Rows.Count;
                    }
                }
                finally
                {
                    sw.Stop();
                    actualTime = sw.ElapsedMilliseconds;
                    context.IsProfiling = oldProfiling;
                    context.RedirectOutput = oldRedirect;
                }
            }

            if (stmt.Query is SelectStatement select)
            {
                await GenerateSelectPlan(select, plan, id, context, metrics);
            }
            else if (stmt.Query is SetOperationStatement setOp)
            {
                await plan.AddRowAsync(new Row { 
                    ["ID"] = id.Value++, 
                    ["Operation"] = $"Set Operation ({setOp.Operation.ToString().Replace("_"," ")})", 
                    ["Details"] = "", 
                    ["Cost"] = 0 
                });
            }
            
            if (stmt.IsAnalyze)
            {
                // For now, map the total metrics to the final step of the plan
                var lastRow = plan.Rows.LastOrDefault();
                if (lastRow != null)
                {
                    lastRow["Actual Rows"] = actualRows;
                    lastRow["Actual Time (ms)"] = actualTime;
                }
            }

            // Populate the context's profile metrics so the UI Performance tab can see it
            metrics.DurationMs = plan.Rows.Sum(r => Convert.ToInt64(r["Cost"] ?? 0));
            context.ProfileMetrics.Add(metrics);
            
            context.LastResult = plan;
            
            if (!context.RedirectOutput)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title(stmt.IsAnalyze ? "[bold yellow]Execution Plan (ANALYZE)[/]" : "[bold yellow]Execution Plan[/]")
                    .AddColumn("ID")
                    .AddColumn("Operation")
                    .AddColumn("Details")
                    .AddColumn("Cost", c => c.RightAligned());

                if (stmt.IsAnalyze)
                {
                    table.AddColumn("Actual Rows", c => c.RightAligned());
                    table.AddColumn("Actual Time", c => c.RightAligned());
                }

                foreach (var row in plan.Rows)
                {
                    if (stmt.IsAnalyze)
                    {
                        table.AddRow(
                            new Text(row["ID"]?.ToString() ?? ""),
                            new Text(row["Operation"]?.ToString() ?? ""),
                            new Text(row["Details"]?.ToString() ?? ""),
                            new Text(row["Cost"]?.ToString() ?? ""),
                            new Text(row["Actual Rows"]?.ToString() ?? "-"),
                            new Text(row["Actual Time (ms)"]?.ToString() ?? "-")
                        );
                    }
                    else
                    {
                        table.AddRow(
                            new Text(row["ID"]?.ToString() ?? ""),
                            new Text(row["Operation"]?.ToString() ?? ""),
                            new Text(row["Details"]?.ToString() ?? ""),
                            new Text(row["Cost"]?.ToString() ?? "")
                        );
                    }
                }
                if (stmt.IntoTable != null)
                {
                    var destination = await context.ResolveDataSourceAsync(stmt.IntoTable);
                    await destination.WriteBatches(new List<DataTable> { plan }.ToAsyncEnumerable());
                    context.Log($"Query plan stored in {stmt.IntoTable.TableName}.");
                }
                else
                {
                    AnsiConsole.Write(table);
                    AnsiConsole.MarkupLine($"[grey]Total Plan Cost:[/] [yellow]{metrics.DurationMs}[/]");
                    if (stmt.IsAnalyze)
                    {
                        AnsiConsole.MarkupLine($"[grey]Total Actual Time:[/] [green]{actualTime}ms[/]");
                        AnsiConsole.MarkupLine($"[grey]Total Actual Rows:[/] [green]{actualRows}[/]");
                    }
                }
            }
        }

        private async Task GenerateSelectPlan(SelectStatement select, DataTable plan, Counter id, IExecutionContext context, ExecutionMetrics metrics)
        {
            // From
            var source = await context.ResolveDataSourceAsync(select.FromTable);
            var op = "Scan";
            var details = "Source: " + select.FromTable.ToSql();
            
            if (source is InMemoryDataSource mem)
            {
                var indexedCols = context.GetIndexedColumns(select.WhereClause, select.FromTable.Alias ?? select.FromTable.TableName);
                foreach (var col in indexedCols)
                {
                    if (mem.HasIndex(col))
                    {
                        op = "Index Seek";
                        details += $" (Index: {col})";
                        if (string.IsNullOrEmpty(metrics.IndexName)) metrics.IndexName = col;
                        break;
                    }
                }
            }
            
            await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = op, ["Details"] = details, ["Cost"] = op == "Index Seek" ? 1 : 2 });
            metrics.PartitionsCount++;

            // Joins
            if (select.Joins != null)
            {
                foreach (var join in select.Joins)
                {
                    metrics.PartitionsCount++;
                    var hashKeysLeft = new List<string>();
                    var hashKeysRight = new List<string>();
                    var leftAlias = select.FromTable.Alias ?? select.FromTable.TableName;
                    var rightAlias = join.Table.Alias ?? join.Table.TableName;
                    
                    bool isHash = IsHashJoinPossible(join.Condition, leftAlias, rightAlias, hashKeysLeft, hashKeysRight);
                    var joinSource = await context.ResolveDataSourceAsync(join.Table);
                    
                    var joinOp = isHash ? "Hash Join" : "Join";
                    var joinDetails = $"Type: {join.JoinType}, Table: {join.Table.ToSql()}, Condition: {join.Condition.ToSql()}";
                    
                    if (joinSource is InMemoryDataSource memJoin)
                    {
                        var joinIndexedCols = context.GetIndexedColumns(join.Condition, rightAlias);
                        foreach (var col in joinIndexedCols)
                        {
                            if (memJoin.HasIndex(col))
                            {
                                joinOp = "Index Join";
                                joinDetails += $" (Index: {col})";
                                if (string.IsNullOrEmpty(metrics.IndexName)) metrics.IndexName = col;
                                break;
                            }
                        }
                    }

                    if (isHash && joinOp != "Index Join") joinDetails += $", Hash Keys: {string.Join(", ", hashKeysLeft)}";
                    
                    await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = joinOp, ["Details"] = joinDetails, ["Cost"] = joinOp == "Index Join" ? 3 : (isHash ? 5 : 10) });
                }
            }
            
            // Filter (Where)
            if (select.WhereClause != null)
            {
                var detailsWhere = select.WhereClause.ToSql();
                if (detailsWhere.Contains("SELECT")) detailsWhere += " [Subquery]";
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Filter", ["Details"] = detailsWhere, ["Cost"] = 2 });
            }
            
            // Aggregate
            bool hasAgg = select.Columns.Any(c => IsAggregate(c.Expression));
            if (select.GroupBy != null || hasAgg)
            {
                var detailsAgg = select.GroupBy != null && select.GroupBy.Count > 0 
                    ? "Group By: " + string.Join(", ", select.GroupBy.Select(g => g.ToSql())) 
                    : "Global Aggregate";
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Aggregate", ["Details"] = detailsAgg, ["Cost"] = 5 });
            }
            
            // Distinct
            if (select.IsDistinct)
            {
                 await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Distinct", ["Details"] = "", ["Cost"] = 3 });
            }

            // Window Functions
            if (select.Columns.Any(c => c.Expression is FunctionCallExpression f && f.Window != null))
            {
                 await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Window Calculation", ["Details"] = "", ["Cost"] = 4 });
            }

            // Sort (Order By)
            if (select.OrderBy != null && select.OrderBy.Count > 0)
            {
                var detailsSort = string.Join(", ", select.OrderBy.Select(o => o.ToSql()));
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Sort", ["Details"] = detailsSort, ["Cost"] = 10 });
            }

            // Limit
            if (select.ToSql().Contains("LIMIT", StringComparison.OrdinalIgnoreCase) || select.ToSql().Contains("TOP", StringComparison.OrdinalIgnoreCase))
            {
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Top/Limit", ["Details"] = "", ["Cost"] = 1 });
            }

            if (select.IsRecursive) metrics.RecursiveDepth = Math.Max(metrics.RecursiveDepth, context.MaxRecursiveDepth > 0 ? context.MaxRecursiveDepth : 1);
        }

        private bool IsHashJoinPossible(Expression cond, string? leftAlias, string? rightAlias, List<string> leftKeys, List<string> rightKeys)
        {
            if (cond is BinaryExpression b && b.Operator == TokenType.EQUALS)
            {
                if (b.Left is IdentifierExpression L && b.Right is IdentifierExpression R)
                {
                    var lName = GetColumnName(L.Name);
                    var rName = GetColumnName(R.Name);
                    
                    if (IsFromAlias(L.Name, leftAlias) && IsFromAlias(R.Name, rightAlias))
                    {
                        leftKeys.Add(lName);
                        rightKeys.Add(rName);
                        return true;
                    }
                    if (IsFromAlias(R.Name, leftAlias) && IsFromAlias(L.Name, rightAlias))
                    {
                        leftKeys.Add(rName);
                        rightKeys.Add(lName);
                        return true;
                    }
                }
            }
            if (cond is BinaryExpression b2 && b2.Operator == TokenType.AND)
            {
                bool resL = IsHashJoinPossible(b2.Left, leftAlias, rightAlias, leftKeys, rightKeys);
                bool resR = IsHashJoinPossible(b2.Right, leftAlias, rightAlias, leftKeys, rightKeys);
                return resL || resR;
            }
            return false;
        }

        private bool IsFromAlias(string identifier, string? alias)
        {
            if (string.IsNullOrEmpty(alias)) return true;
            if (identifier.Contains(".")) return identifier.StartsWith(alias + ".", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private string GetColumnName(string identifier)
        {
            int dot = identifier.IndexOf('.');
            return dot >= 0 ? identifier.Substring(dot + 1) : identifier;
        }

        private bool IsAggregate(Expression? expr)
        {
            if (expr is FunctionCallExpression f)
            {
                var name = f.FunctionName.ToUpperInvariant();
                return name == "COUNT" || name == "SUM" || name == "AVG" || name == "MIN" || name == "MAX";
            }
            if (expr is BinaryExpression b) return IsAggregate(b.Left) || IsAggregate(b.Right);
            return false;
        }
    }
}


