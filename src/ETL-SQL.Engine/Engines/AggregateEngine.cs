using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Core engine for performing in-memory aggregations (SUM, AVG, MIN, MAX, COUNT, etc.) with GROUP BY and HAVING support.
    /// </summary>
    public class AggregateEngine
    {
        private readonly IExecutionContext _context;

        public AggregateEngine(IExecutionContext context)
        {
            _context = context;
        }

        /// <summary>Applies aggregation logic to a buffer of rows, grouping them and calculating aggregate functions.</summary>
        public async Task<List<Row>> ApplyAggregation(List<Row> allBufferedRows, List<Expression>? groupBy, List<SelectColumn> finalColumns, List<string> colNames, Expression? havingClause = null)
        {
            var groups = new Dictionary<CompoundKey, List<Row>>();
            bool hasAgg = finalColumns.Any(c => IsAggregate(c.Expression));

            foreach (var row in allBufferedRows)
            {
                CompoundKey key;
                if (groupBy != null && groupBy.Count > 0)
                {
                    var vals = new object?[groupBy.Count];
                    for (int i = 0; i < groupBy.Count; i++)
                    {
                        vals[i] = await _context.EvaluateValue(groupBy[i], row);
                    }
                    key = new CompoundKey(vals);
                }
                else key = new CompoundKey("GLOBAL");

                if (!groups.TryGetValue(key, out var list)) { list = new List<Row>(); groups[key] = list; }
                list.Add(row);
            }

            if (groups.Count == 0 && hasAgg && (groupBy == null || groupBy.Count == 0)) groups[new CompoundKey("GLOBAL")] = new List<Row>();

            var resultRows = new List<Row>();
            var havingAggs = new List<FunctionCallExpression>();
            if (havingClause != null) CollectAggregates(havingClause, havingAggs);

            foreach (var groupRows in groups.Values)
            {
                // Verify HAVING clause before projecting
                if (havingClause != null)
                {
                    var havingContext = groupRows.Count > 0 ? groupRows[0].Clone() : new Row();
                    foreach (var agg in havingAggs)
                    {
                        var val = await EvaluateAggregate(agg, groupRows);
                        havingContext[$"AGG_{agg.ToSql().ToUpperInvariant()}"] = val;
                    }

                    if (!await _context.EvaluateCondition(havingClause, havingContext)) continue;
                }

                var resRow = new Row();
                for (int i = 0; i < finalColumns.Count; i++)
                {
                    if (IsAggregate(finalColumns[i].Expression))
                    {
                        var val = await EvaluateAggregate(finalColumns[i].Expression, groupRows);
                        resRow[colNames[i]] = val;
                        // Also store with AGG_ prefix for subsequent steps (Window, OrderBy)
                        resRow[$"AGG_{finalColumns[i].Expression.ToSql().ToUpperInvariant()}"] = val;
                    }
                    else
                        resRow[colNames[i]] = groupRows.Count > 0 ? await _context.EvaluateValue(finalColumns[i].Expression, groupRows[0]) : null;
                }
                resultRows.Add(resRow);
            }
            return resultRows;
        }

        public bool IsAggregate(Expression? expr)
        {
            if (expr is FunctionCallExpression f)
            {
                if (f.Window != null) return false;
                var name = f.FunctionName.ToUpperInvariant();
                return name == "COUNT" || name == "SUM" || name == "AVG" || name == "MIN" || name == "MAX"
                    || name == "STRING_AGG" || name == "LIST_AGG"
                    || name == "PERCENTILE_CONT" || name == "PERCENTILE_DISC";
            }
            if (expr is BinaryExpression b) return IsAggregate(b.Left) || IsAggregate(b.Right);
            return false;
        }

        private void CollectAggregates(Expression expr, List<FunctionCallExpression> aggs)
        {
            if (expr is FunctionCallExpression f && IsAggregate(f))
            {
                if (!aggs.Any(a => a.ToSql().Equals(f.ToSql(), StringComparison.OrdinalIgnoreCase))) 
                    aggs.Add(f);
            }
            else if (expr is BinaryExpression b)
            {
                CollectAggregates(b.Left, aggs);
                CollectAggregates(b.Right, aggs);
            }
            else if (expr is LikeExpression l)
            {
                CollectAggregates(l.Left, aggs);
            }
            else if (expr is InExpression i)
            {
                CollectAggregates(i.Left, aggs);
            }
            else if (expr is IsNullExpression n)
            {
                CollectAggregates(n.Expression, aggs);
            }
        }

        public async Task<object?> EvaluateAggregate(Expression expr, List<Row> rows)
        {
            if (expr is FunctionCallExpression f)
            {
                var name = f.FunctionName.ToUpperInvariant();
                var vals = new List<object?>();
                if (f.Arguments.Count > 0)
                {
                    foreach (var r in rows) vals.Add(await _context.EvaluateValue(f.Arguments[0], r));
                }

                return name switch
                {
                    "COUNT" => (decimal)vals.Count(v => v != null || (f.Arguments.Count == 1 && f.Arguments[0] is IdentifierExpression id && id.Name == "*")),
                    "SUM" => (decimal)vals.Where(v => v != null).Sum(v => Convert.ToDecimal(v)),
                    "AVG" => (decimal)vals.Where(v => v != null).Average(v => Convert.ToDecimal(v)),
                    "MIN" => vals.Where(v => v != null).Min(),
                    "MAX" => vals.Where(v => v != null).Max(),
                    "STRING_AGG" => string.Join(f.Arguments.Count >= 2 ? (await _context.EvaluateValue(f.Arguments[1], new Row()))?.ToString() ?? "" : ",",
                        f.WithinGroupOrderBy != null ? await SortRows(rows, f.WithinGroupOrderBy) : vals.Select(v => v?.ToString() ?? "")),
                    "PERCENTILE_CONT" => await EvaluatePercentileCont(f, rows),
                    "PERCENTILE_DISC" => await EvaluatePercentileDisc(f, rows),
                    _ => null
                };
            }
            return null;
        }

        public async Task<IEnumerable<string>> SortRows(List<Row> rows, List<OrderByClause> orderBy)
        {
            // Pre-evaluate sort keys to avoid Task.Result deadlocks in Sort()
            var sortData = new List<(Row row, object?[] keys)>();
            foreach (var row in rows)
            {
                var keys = new object?[orderBy.Count];
                for (int i = 0; i < orderBy.Count; i++)
                {
                    keys[i] = await _context.EvaluateValue(orderBy[i].Expression, row);
                }
                sortData.Add((row, keys));
            }

            sortData.Sort((a, b) =>
            {
                for (int i = 0; i < orderBy.Count; i++)
                {
                    int res = _context.CompareConstants(a.keys[i], b.keys[i]);
                    if (res != 0) return orderBy[i].Descending ? -res : res;
                }
                return 0;
            });

            var results = new List<string>();
            foreach (var item in sortData)
            {
                // For STRING_AGG, we typicaly just need the first sort expression as the value
                results.Add(item.keys[0]?.ToString() ?? "");
            }
            return results;
        }

        /// <summary>
        /// PERCENTILE_CONT(p) WITHIN GROUP (ORDER BY expr) — continuous interpolation.
        /// Returns a linearly interpolated value at the p-th percentile.
        /// </summary>
        private async Task<object?> EvaluatePercentileCont(FunctionCallExpression f, List<Row> rows)
        {
            if (f.Arguments.Count == 0 || f.WithinGroupOrderBy == null || f.WithinGroupOrderBy.Count == 0)
                return null;

            double p = Convert.ToDouble(await _context.EvaluateValue(f.Arguments[0], new Row()));
            p = Math.Max(0.0, Math.Min(1.0, p));

            // Collect and sort values from the WITHIN GROUP ORDER BY expression
            var sortExpr = f.WithinGroupOrderBy[0].Expression;
            var sortedVals = new List<decimal>();
            foreach (var r in rows)
            {
                var v = await _context.EvaluateValue(sortExpr, r);
                if (v != null) sortedVals.Add(Convert.ToDecimal(v));
            }
            if (sortedVals.Count == 0) return null;

            bool descending = f.WithinGroupOrderBy[0].Descending;
            sortedVals.Sort();
            if (descending) sortedVals.Reverse();

            if (sortedVals.Count == 1) return sortedVals[0];

            double rowNumber = p * (sortedVals.Count - 1);
            int lower = (int)Math.Floor(rowNumber);
            int upper = (int)Math.Ceiling(rowNumber);
            if (lower == upper) return sortedVals[lower];

            decimal fraction = (decimal)(rowNumber - lower);
            return sortedVals[lower] + fraction * (sortedVals[upper] - sortedVals[lower]);
        }

        /// <summary>
        /// PERCENTILE_DISC(p) WITHIN GROUP (ORDER BY expr) — discrete selection.
        /// Returns the first value where cumulative distribution >= p.
        /// </summary>
        private async Task<object?> EvaluatePercentileDisc(FunctionCallExpression f, List<Row> rows)
        {
            if (f.Arguments.Count == 0 || f.WithinGroupOrderBy == null || f.WithinGroupOrderBy.Count == 0)
                return null;

            double p = Convert.ToDouble(await _context.EvaluateValue(f.Arguments[0], new Row()));
            p = Math.Max(0.0, Math.Min(1.0, p));

            var sortExpr = f.WithinGroupOrderBy[0].Expression;
            var sortedVals = new List<decimal>();
            foreach (var r in rows)
            {
                var v = await _context.EvaluateValue(sortExpr, r);
                if (v != null) sortedVals.Add(Convert.ToDecimal(v));
            }
            if (sortedVals.Count == 0) return null;

            bool descending = f.WithinGroupOrderBy[0].Descending;
            sortedVals.Sort();
            if (descending) sortedVals.Reverse();

            for (int i = 0; i < sortedVals.Count; i++)
            {
                double cumDist = (double)(i + 1) / sortedVals.Count;
                if (cumDist >= p) return sortedVals[i];
            }
            return sortedVals[sortedVals.Count - 1];
        }
    }
}
