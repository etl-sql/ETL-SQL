using ETL_SQL.Core;
using ETL_SQL.Core.Planning;
using ETL_SQL.Data;

namespace ETL_SQL.Analysis.Explain
{
    public class ExplainPlanBuilder
    {
        public async Task BuildAsync(Statement query, DataTable plan, IExecutionContext context, ExecutionMetrics metrics)
        {
            var id = new PlanCounter();

            if (query is SelectStatement select)
            {
                select = await SemiJoinPushdownOptimizer.OptimizeAsync(select, context);
                await GenerateSelectPlan(select, plan, id, context, metrics);
            }
            else if (query is SetOperationStatement setOp)
            {
                await plan.AddRowAsync(new Row
                {
                    ["ID"] = id.Value++,
                    ["Operation"] = $"Set Operation ({setOp.Operation.ToString().Replace("_", " ")})",
                    ["Details"] = string.Empty,
                    ["Cost"] = 0,
                    ["Est. Rows"] = "--"
                });
            }
        }

        private async Task GenerateSelectPlan(SelectStatement select, DataTable plan, PlanCounter id, IExecutionContext context, ExecutionMetrics metrics)
        {
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

            long estRows = source is InMemoryDataSource mem2 ? mem2.EstimatedRowCount : -1;
            await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = op, ["Details"] = details, ["Cost"] = op == "Index Seek" ? 1 : 2, ["Mode"] = "STREAMING", ["Est. Rows"] = estRows >= 0 ? estRows : (object)"--" });
            metrics.PartitionsCount++;

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
                    if (join.Table.Metadata != null && join.Table.Metadata.TryGetValue("SEMI_JOIN_PUSHDOWN", out var semijoinDetail))
                    {
                        joinDetails += $", {semijoinDetail}";
                    }

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

                    long estJoinRows = joinSource is InMemoryDataSource memJoin2 ? memJoin2.EstimatedRowCount : -1;
                    await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = joinOp, ["Details"] = joinDetails, ["Cost"] = joinOp == "Index Join" ? 3 : isHash ? 5 : 10, ["Mode"] = "BLOCKING", ["Est. Rows"] = estJoinRows >= 0 ? estJoinRows : (object)"--" });
                }
            }

            if (select.WhereClause != null)
            {
                var detailsWhere = select.WhereClause.ToSql();
                if (detailsWhere.Contains("SELECT")) detailsWhere += " [Subquery]";
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Filter", ["Details"] = detailsWhere, ["Cost"] = 2, ["Mode"] = "STREAMING", ["Est. Rows"] = "--" });
            }

            bool hasAgg = select.Columns.Any(c => IsAggregate(c.Expression));
            if (select.GroupBy != null || hasAgg)
            {
                var detailsAgg = select.GroupBy != null && select.GroupBy.Count > 0
                    ? "Group By: " + string.Join(", ", select.GroupBy.Select(g => g.ToSql()))
                    : "Global Aggregate";
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Aggregate", ["Details"] = detailsAgg, ["Cost"] = 5, ["Mode"] = "BLOCKING", ["Est. Rows"] = "--" });
            }

            if (select.IsDistinct)
            {
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Distinct", ["Details"] = string.Empty, ["Cost"] = 3, ["Mode"] = "BLOCKING", ["Est. Rows"] = "--" });
            }

            if (select.Columns.Any(c => c.Expression is FunctionCallExpression f && f.Window != null))
            {
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Window Calculation", ["Details"] = string.Empty, ["Cost"] = 4, ["Mode"] = "BLOCKING", ["Est. Rows"] = "--" });
            }

            bool hasTopN = (select.LimitCount != null || select.TopCount != null) && !select.IsTopPercent && !select.WithTies && !select.IsDistinct && select.QualifyClause == null && !hasAgg;
            if (select.OrderBy != null && select.OrderBy.Count > 0)
            {
                var detailsSort = string.Join(", ", select.OrderBy.Select(o => o.ToSql()));
                var sortMode = hasTopN ? "STREAMING" : "BLOCKING";
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Sort", ["Details"] = detailsSort, ["Cost"] = hasTopN ? 3 : 10, ["Mode"] = sortMode, ["Est. Rows"] = "--" });
            }

            if (select.LimitCount != null || select.TopCount != null)
            {
                await plan.AddRowAsync(new Row { ["ID"] = id.Value++, ["Operation"] = "Top/Limit", ["Details"] = string.Empty, ["Cost"] = 1, ["Mode"] = "STREAMING", ["Est. Rows"] = "--" });
            }

            if (select.IsRecursive) metrics.RecursiveDepth = Math.Max(metrics.RecursiveDepth, context.MaxRecursiveDepth > 0 ? context.MaxRecursiveDepth : 1);
        }

        private static bool IsHashJoinPossible(Expression cond, string? leftAlias, string? rightAlias, List<string> leftKeys, List<string> rightKeys)
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

        private static bool IsFromAlias(string identifier, string? alias)
        {
            if (string.IsNullOrEmpty(alias)) return true;
            if (identifier.Contains(".")) return identifier.StartsWith(alias + ".", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private static string GetColumnName(string identifier)
        {
            int dot = identifier.IndexOf('.');
            return dot >= 0 ? identifier[(dot + 1)..] : identifier;
        }

        private static bool IsAggregate(Expression? expr)
        {
            if (expr is FunctionCallExpression f)
            {
                var name = f.FunctionName.ToUpperInvariant();
                return name == "COUNT" || name == "SUM" || name == "AVG" || name == "MIN" || name == "MAX" || name == "TOTAL" || name == "GROUP_CONCAT";
            }

            if (expr is BinaryExpression b) return IsAggregate(b.Left) || IsAggregate(b.Right);
            return false;
        }

        private sealed class PlanCounter
        {
            public int Value { get; set; } = 1;
        }
    }
}
