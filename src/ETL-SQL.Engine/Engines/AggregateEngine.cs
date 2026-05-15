using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Core engine for performing in-memory aggregations (SUM, AVG, MIN, MAX, COUNT, etc.) with GROUP BY and HAVING support.
    /// </summary>
    public class AggregateEngine
    {
        private readonly IExecutionContext _context;
        private readonly ILogger _logger;

        public AggregateEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>Applies aggregation logic to a stream of rows, grouping them and calculating aggregate functions.</summary>
        public async Task<List<Row>> ApplyAggregation(IAsyncEnumerable<Row> inputStream, List<Expression>? groupBy, List<SelectColumn> finalColumns, List<string> colNames, Expression? havingClause = null, GroupingSetClause? groupingSet = null)
        {
            // When groupingSet is present, expand into multiple GROUP BY passes and union the results.
            if (groupingSet != null && groupingSet.Type != GroupingSetType.None)
            {
                var expandedSets = ExpandGroupingSets(groupingSet);
                var allResults = new List<Row>();
                
                // For grouping sets, we must materialize once to avoid multiple enumerations.
                var allBufferedRows = await inputStream.ToListAsync();

                foreach (var activeGroupBy in expandedSets)
                {
                    // Pass the materialized list to avoid further materialization in recursive calls
                    var setRows = await ApplyAggregation(allBufferedRows.ToAsyncEnumerable(), activeGroupBy, finalColumns, colNames, havingClause, null);
                    
                    // Mark which columns were NULL-substituted (GROUPING() support)
                    var activeKeys = new HashSet<string>(activeGroupBy!.Select(e => NormalizedToSql(e)), StringComparer.OrdinalIgnoreCase);
                    
                    foreach (var row in setRows)
                    {
                        // For every groupBy column NOT in this set, null out its output column
                        if (groupBy != null)
                        {
                            foreach (var expr in groupBy)
                            {
                                if (!activeKeys.Contains(NormalizedToSql(expr)))
                                {
                                    var colName = expr is IdentifierExpression id ? id.Name.Split('.').Last() : NormalizedToSql(expr);
                                    
                                    // Match the output column name
                                    var matchIdx = colNames.FindIndex(c => c.Equals(colName, StringComparison.OrdinalIgnoreCase));
                                    
                                    if (matchIdx == -1)
                                    {
                                        // Fallback 1: match by the expression's SQL representation in the final columns
                                        matchIdx = finalColumns.FindIndex(fc => NormalizedToSql(fc.Expression).Equals(NormalizedToSql(expr), StringComparison.OrdinalIgnoreCase));
                                    }
                                    
                                    if (matchIdx == -1)
                                    {
                                        // Fallback 2: match by alias if the expression is an identifier
                                        if (expr is IdentifierExpression idExpr)
                                        {
                                            matchIdx = finalColumns.FindIndex(fc => string.Equals(fc.Alias, idExpr.Name, StringComparison.OrdinalIgnoreCase));
                                        }
                                    }

                                    if (matchIdx >= 0) row[colNames[matchIdx]] = null;
                                }
                            }
                        }
                    }
                    allResults.AddRange(setRows);
                }
                return allResults;
            }

            bool hasAgg = finalColumns.Any(c => IsAggregate(c.Expression));
            var aggregateSpecs = new List<(int ColumnIndex, FunctionCallExpression Function, IAggregateState State)>();
            var havingAggSpecs = new List<(FunctionCallExpression Function, IAggregateState State)>();

            // Identify all aggregates in SELECT
            for (int i = 0; i < finalColumns.Count; i++)
            {
                if (finalColumns[i].Expression is FunctionCallExpression f && IsAggregate(f))
                {
                    aggregateSpecs.Add((i, f, CreateState(f)));
                }
            }

            // Identify all aggregates in HAVING
            if (havingClause != null)
            {
                var havingAggs = new List<FunctionCallExpression>();
                CollectAggregates(havingClause, havingAggs);
                foreach (var f in havingAggs)
                {
                    havingAggSpecs.Add((f, CreateState(f)));
                }
            }

            // Single-Pass Aggregation
            var groupStates = new Dictionary<CompoundKey, (IAggregateState[] SelectStates, IAggregateState[] HavingStates)>();
            
            await foreach (var row in inputStream)
            {
                CompoundKey key;
                if (groupBy != null && groupBy.Count > 0)
                {
                    if (groupBy.Count == 1)
                    {
                        key = new CompoundKey(await EvaluateGroupExpr(groupBy[0], finalColumns, row));
                    }
                    else if (groupBy.Count == 2)
                    {
                        key = new CompoundKey(await EvaluateGroupExpr(groupBy[0], finalColumns, row), await EvaluateGroupExpr(groupBy[1], finalColumns, row));
                    }
                    else if (groupBy.Count == 3)
                    {
                        key = new CompoundKey(await EvaluateGroupExpr(groupBy[0], finalColumns, row), await EvaluateGroupExpr(groupBy[1], finalColumns, row), await EvaluateGroupExpr(groupBy[2], finalColumns, row));
                    }
                    else
                    {
                        var vals = new object?[groupBy.Count];
                        for (int i = 0; i < groupBy.Count; i++) vals[i] = await EvaluateGroupExpr(groupBy[i], finalColumns, row);
                        key = new CompoundKey(vals);
                    }
                }
                else key = new CompoundKey("GLOBAL");

                if (!groupStates.TryGetValue(key, out var states))
                {
                    var sStates = aggregateSpecs.Select(s => CreateState(s.Function)).ToArray();
                    var hStates = havingAggSpecs.Select(s => CreateState(s.Function)).ToArray();
                    states = (sStates, hStates);
                    groupStates[key] = states;
                }

                // Update states
                for (int i = 0; i < aggregateSpecs.Count; i++)
                {
                    await states.SelectStates[i].Update(row, aggregateSpecs[i].Function, _context);
                }
                for (int i = 0; i < havingAggSpecs.Count; i++)
                {
                    await states.HavingStates[i].Update(row, havingAggSpecs[i].Function, _context);
                }
            }

            // Handle global aggregation if no rows but aggregates present
            if (groupStates.Count == 0 && hasAgg && (groupBy == null || groupBy.Count == 0))
            {
                var sStates = aggregateSpecs.Select(s => CreateState(s.Function)).ToArray();
                var hStates = havingAggSpecs.Select(s => CreateState(s.Function)).ToArray();
                groupStates[new CompoundKey("GLOBAL")] = (sStates, hStates);
            }

            var resultRows = new List<Row>();
            foreach (var kvp in groupStates)
            {
                var key = kvp.Key;
                var states = kvp.Value;

                var resRow = new Row();
                
                // Finalize aggregates
                for (int i = 0; i < aggregateSpecs.Count; i++)
                {
                    var val = await states.SelectStates[i].Finalize(this);
                    resRow[colNames[aggregateSpecs[i].ColumnIndex]] = val;
                    resRow[$"AGG_{aggregateSpecs[i].Function.ToSql().ToUpperInvariant()}"] = val;
                }

                // Check HAVING
                if (havingClause != null)
                {
                    var havingContext = resRow.Clone(); // Use finalized aggregates
                    for (int i = 0; i < havingAggSpecs.Count; i++)
                    {
                        var val = await states.HavingStates[i].Finalize(this);
                        havingContext[$"AGG_{havingAggSpecs[i].Function.ToSql().ToUpperInvariant()}"] = val;
                    }
                    // For grouping columns in HAVING
                    if (groupBy != null)
                    {
                        for (int i = 0; i < groupBy.Count; i++)
                        {
                            var colName = groupBy[i] is IdentifierExpression id ? id.Name.Split('.').Last() : groupBy[i].ToSql();
                            havingContext[colName] = key[i];
                        }
                    }

                    if (!await _context.EvaluateCondition(havingClause, havingContext)) continue;
                }

                // Fill non-aggregate columns (grouping columns)
                for (int i = 0; i < finalColumns.Count; i++)
                {
                    if (resRow.HasColumn(colNames[i])) continue; // Already filled by aggregate

                    int groupIdx = -1;
                    if (groupBy != null)
                    {
                        groupIdx = groupBy.FindIndex(g => NormalizedToSql(g).Equals(NormalizedToSql(finalColumns[i].Expression), StringComparison.OrdinalIgnoreCase));
                        if (groupIdx == -1) // check for alias match
                        {
                            groupIdx = groupBy.FindIndex(g => g is IdentifierExpression id && string.Equals(id.Name, finalColumns[i].Alias, StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (groupIdx >= 0 && groupIdx < key.Length)
                    {
                        resRow[colNames[i]] = key[groupIdx];
                    }
                    else if (IsWindowFunction(finalColumns[i].Expression)) { /* Handled later */ }
                    else
                    {
                        // For columns that are neither aggregate nor grouping, just use null or first value (though this is strictly invalid SQL without grouping)
                        resRow[colNames[i]] = null;
                    }
                }

                resultRows.Add(resRow);
            }

            return resultRows;
        }

        private async Task<object?> EvaluateGroupExpr(Expression expr, List<SelectColumn> finalColumns, Row row)
        {
            if (expr is IdentifierExpression id && finalColumns.FirstOrDefault(c => string.Equals(c.Alias, id.Name, StringComparison.OrdinalIgnoreCase)) is SelectColumn col)
            {
                return await _context.EvaluateValue(col.Expression, row);
            }
            return await _context.EvaluateValue(expr, row);
        }

        /// <summary>Expands a GroupingSetClause into a list of effective GROUP BY lists (one per pass).</summary>
        public List<List<Expression>> ExpandGroupingSets(GroupingSetClause clause)
        {
            var cols = clause.GroupSets[0]; // ROLLUP/CUBE: single list; GroupingSets: N lists
            int n = cols.Count;
            int limit = _context.MaxGroupingSets;

            int totalSets = clause.Type switch
            {
                GroupingSetType.Rollup => n + 1,
                GroupingSetType.Cube => (int)Math.Pow(2, n),
                _ => clause.GroupSets.Count
            };

            if (totalSets > limit)
            {
                throw new ExecutionException($"{clause.Type} operation exceeds the maximum grouping sets limit ({limit}). " +
                    $"It would produce {totalSets:N0} sets. Use SET MAX_GROUPING_SETS to increase if necessary.");
            }

            if (totalSets > 64)
            {
                _logger.Warning("{Type} operation generates {Count:N0} grouping sets, which may impact performance.", clause.Type, totalSets);
            }

            if (clause.Type == GroupingSetType.Rollup)
            {
                // ROLLUP(a, b, c) → (a,b,c), (a,b), (a), ()
                var result = new List<List<Expression>>();
                for (int i = n; i >= 0; i--)
                    result.Add(cols.Take(i).ToList());
                return result;
            }

            if (clause.Type == GroupingSetType.Cube)
            {
                // CUBE(a, b, c) → all 2^n subsets in descending cardinality order
                var result = new List<List<Expression>>();
                for (int mask = (1 << n) - 1; mask >= 0; mask--)
                {
                    var subset = new List<Expression>();
                    for (int bit = 0; bit < n; bit++)
                        if ((mask & (1 << bit)) != 0) subset.Add(cols[bit]);
                    result.Add(subset);
                }
                return result;
            }

            // GroupingSets: use the sets as-is
            return clause.GroupSets.Select(s => s.ToList()).ToList();
        }

        private string NormalizedToSql(Expression e)
        {
            if (e == null) return "";
            var sql = e.ToSql().ToUpperInvariant();
            // Remove parentheses for matching purposes
            while (sql.StartsWith("(") && sql.EndsWith(")")) sql = sql.Substring(1, sql.Length - 2);
            return sql.Trim();
        }

        public bool IsAggregate(Expression? expr)
        {
            if (expr is FunctionCallExpression f)
            {
                if (f.Window != null) return false;
                var name = f.FunctionName.ToUpperInvariant();
                return name == "COUNT" || name == "SUM" || name == "AVG" || name == "MIN" || name == "MAX"
                    || name == "STRING_AGG" || name == "LIST_AGG"
                    || name == "PERCENTILE_CONT" || name == "PERCENTILE_DISC"
                    || name == "VAR" || name == "VARP" || name == "VAR_SAMP" || name == "VAR_POP"
                    || name == "STDEV" || name == "STDEVP" || name == "STDDEV" || name == "STDDEV_SAMP" || name == "STDDEV_POP"
                    || name == "CORR" || name == "COVAR_SAMP" || name == "COVAR_POP";
            }
            if (expr is BinaryExpression b) return IsAggregate(b.Left) || IsAggregate(b.Right);
            return false;
        }

        public bool IsWindowFunction(Expression expr) => WindowEngine.ContainsWindowFunction(expr);

        private void CollectAggregates(Expression expr, List<FunctionCallExpression> aggs)
        {
            if (expr is FunctionCallExpression f && IsAggregate(f))
            {
                var fSql = f.ToSql();
                if (!aggs.Any(a => a.ToSql().Equals(fSql, StringComparison.OrdinalIgnoreCase))) 
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
                
                // Collect results for all arguments
                var valsByArg = new List<List<object?>>();
                for (int i = 0; i < f.Arguments.Count; i++)
                {
                    var argVals = new List<object?>();
                    foreach (var r in rows) argVals.Add(await _context.EvaluateValue(f.Arguments[i], r));
                    valsByArg.Add(argVals);
                }

                switch (name)
                {
                    case "COUNT":
                        if (f.Arguments.Count == 0) return (decimal)rows.Count;
                        var countVals = f.IsDistinct ? valsByArg[0].Distinct() : valsByArg[0];
                        return (decimal)countVals.Count(v => v != null || (f.Arguments[0] is IdentifierExpression id && id.Name == "*"));
                    case "SUM":
                        var sumVals = f.IsDistinct ? valsByArg[0].Distinct() : valsByArg[0];
                        return (decimal)sumVals.Where(v => v != null).Sum(v => SafeToDecimal(v));
                    case "AVG":
                        var avgVals = f.IsDistinct ? valsByArg[0].Distinct() : valsByArg[0];
                        return (decimal)avgVals.Where(v => v != null).Average(v => SafeToDecimal(v));
                    case "MIN":
                        return valsByArg[0].Where(v => v != null).Min();
                    case "MAX":
                        return valsByArg[0].Where(v => v != null).Max();
                    case "STRING_AGG":
                        return string.Join(f.Arguments.Count >= 2 ? (await _context.EvaluateValue(f.Arguments[1], new Row()))?.ToString() ?? "" : ",",
                            f.WithinGroupOrderBy != null ? await SortRows(rows, f.WithinGroupOrderBy) : valsByArg[0].Select(v => v?.ToString() ?? ""));
                    case "PERCENTILE_CONT":
                        return await EvaluatePercentileCont(f, rows);
                    case "PERCENTILE_DISC":
                        return await EvaluatePercentileDisc(f, rows);
                    case "VAR":
                    case "VAR_SAMP":
                        return CalculateVariance(valsByArg[0], false);
                    case "VARP":
                    case "VAR_POP":
                        return CalculateVariance(valsByArg[0], true);
                    case "STDEV":
                    case "STDDEV_SAMP":
                    case "STDDEV":
                        return CalculateStDev(valsByArg[0], false);
                    case "STDEVP":
                    case "STDDEV_POP":
                        return CalculateStDev(valsByArg[0], true);
                    case "COVAR_SAMP":
                        return CalculateCovariance(valsByArg[0], valsByArg[1], false);
                    case "COVAR_POP":
                        return CalculateCovariance(valsByArg[0], valsByArg[1], true);
                    case "CORR":
                        return CalculateCorrelation(valsByArg[0], valsByArg[1]);
                    default:
                        return null;
                }
            }
            return null;
        }

        private decimal? CalculateVariance(List<object?> vals, bool population)
        {
            var numbers = vals.Where(v => v != null).Select(v => SafeToDecimal(v)).ToList();
            int n = numbers.Count;
            if (n == 0 || (!population && n == 1)) return null;

            decimal avg = numbers.Average();
            decimal sumSqDiff = numbers.Sum(x => (x - avg) * (x - avg));
            return sumSqDiff / (population ? n : n - 1);
        }

        private decimal? CalculateStDev(List<object?> vals, bool population)
        {
            var var = CalculateVariance(vals, population);
            if (var == null) return null;
            return (decimal)Math.Sqrt((double)var.Value);
        }

        private decimal? CalculateCovariance(List<object?> xVals, List<object?> yVals, bool population)
        {
            var xNums = new List<decimal>();
            var yNums = new List<decimal>();
            for (int i = 0; i < xVals.Count; i++)
            {
                if (xVals[i] != null && yVals[i] != null)
                {
                    xNums.Add(SafeToDecimal(xVals[i]));
                    yNums.Add(SafeToDecimal(yVals[i]));
                }
            }

            int n = xNums.Count;
            if (n == 0 || (!population && n == 1)) return null;

            decimal xAvg = xNums.Average();
            decimal yAvg = yNums.Average();
            decimal sumDiffProd = 0;
            for (int i = 0; i < n; i++)
            {
                sumDiffProd += (xNums[i] - xAvg) * (yNums[i] - yAvg);
            }

            return sumDiffProd / (population ? n : n - 1);
        }

        private decimal? CalculateCorrelation(List<object?> xVals, List<object?> yVals)
        {
            var cov = CalculateCovariance(xVals, yVals, true);
            if (cov == null) return null;

            // Specifically for correlation, we need the standard deviations of the SAME set of pairs (where neither is NULL)
            var xNums = new List<decimal>();
            var yNums = new List<decimal>();
            for (int i = 0; i < xVals.Count; i++)
            {
                if (xVals[i] != null && yVals[i] != null)
                {
                    xNums.Add(SafeToDecimal(xVals[i]));
                    yNums.Add(SafeToDecimal(yVals[i]));
                }
            }
            
            if (xNums.Count == 0) return null;
            
            double xStd = Math.Sqrt((double)CalculateVariance(xNums.Cast<object?>().ToList(), true)!);
            double yStd = Math.Sqrt((double)CalculateVariance(yNums.Cast<object?>().ToList(), true)!);

            if (xStd == 0 || yStd == 0) return null;
            return (decimal)((double)cov.Value / (xStd * yStd));
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
                if (v != null) sortedVals.Add(SafeToDecimal(v));
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
                if (v != null) sortedVals.Add(SafeToDecimal(v));
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
        private static decimal SafeToDecimal(object? v)
        {
            if (v == null) return 0m;
            if (v is DateTime dt) throw new ExecutionException($"Cannot perform numeric aggregation on a date value: '{dt:yyyy-MM-dd HH:mm:ss}'. Ensure you are not summing a grouping column like 'month'.");
            if (v is TimeSpan ts) throw new ExecutionException($"Cannot perform numeric aggregation on a time duration: '{ts}'.");
            try { return Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception ex) { throw new ExecutionException($"Invalid numeric value for aggregation: '{v}'. Details: {ex.Message}"); }
        }

        private IAggregateState CreateState(FunctionCallExpression f)
        {
            var name = f.FunctionName.ToUpperInvariant();
            return name switch
            {
                "SUM" => new SumState(f.IsDistinct),
                "COUNT" => new CountState(f.IsDistinct, f.Arguments.Count == 0 || (f.Arguments[0] is IdentifierExpression id && id.Name == "*")),
                "AVG" => new AvgState(f.IsDistinct),
                "MIN" => new MinState(),
                "MAX" => new MaxState(),
                _ => new GenericState(f)
            };
        }

        private interface IAggregateState
        {
            ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context);
            ValueTask<object?> Finalize(AggregateEngine engine);
        }

        private class SumState : IAggregateState
        {
            private decimal _sum = 0;
            private bool _hasValue = false;
            private HashSet<object?>? _distinctValues;
            private readonly bool _isDistinct;

            public SumState(bool isDistinct) { _isDistinct = isDistinct; if (_isDistinct) _distinctValues = new HashSet<object?>(); }

            public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
            {
                var val = await context.EvaluateValue(f.Arguments[0], row);
                if (val != null)
                {
                    if (_isDistinct)
                    {
                        if (_distinctValues!.Add(val))
                        {
                            _sum += SafeToDecimal(val);
                            _hasValue = true;
                        }
                    }
                    else
                    {
                        _sum += SafeToDecimal(val);
                        _hasValue = true;
                    }
                }
            }

            public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>(_hasValue ? _sum : null);
        }

        private class CountState : IAggregateState
        {
            private long _count = 0;
            private HashSet<object?>? _distinctValues;
            private readonly bool _isDistinct;
            private readonly bool _isStar;

            public CountState(bool isDistinct, bool isStar) { _isDistinct = isDistinct; _isStar = isStar; if (_isDistinct) _distinctValues = new HashSet<object?>(); }

            public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
            {
                if (_isStar)
                {
                    _count++;
                    return;
                }

                var val = await context.EvaluateValue(f.Arguments[0], row);
                if (val != null)
                {
                    if (_isDistinct)
                    {
                        if (_distinctValues!.Add(val)) _count++;
                    }
                    else _count++;
                }
            }

            public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>((decimal)_count);
        }

        private class AvgState : IAggregateState
        {
            private decimal _sum = 0;
            private long _count = 0;
            private HashSet<object?>? _distinctValues;
            private readonly bool _isDistinct;

            public AvgState(bool isDistinct) { _isDistinct = isDistinct; if (_isDistinct) _distinctValues = new HashSet<object?>(); }

            public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
            {
                var val = await context.EvaluateValue(f.Arguments[0], row);
                if (val != null)
                {
                    if (_isDistinct)
                    {
                        if (_distinctValues!.Add(val))
                        {
                            _sum += SafeToDecimal(val);
                            _count++;
                        }
                    }
                    else
                    {
                        _sum += SafeToDecimal(val);
                        _count++;
                    }
                }
            }

            public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>(_count > 0 ? _sum / _count : null);
        }

        private class MinState : IAggregateState
        {
            private object? _min = null;

            public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
            {
                var val = await context.EvaluateValue(f.Arguments[0], row);
                if (val != null)
                {
                    if (_min == null || ((IComparable)CompoundKey.NormalizeValue(val)).CompareTo(CompoundKey.NormalizeValue(_min)) < 0)
                        _min = val;
                }
            }

            public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>(_min);
        }

        private class MaxState : IAggregateState
        {
            private object? _max = null;

            public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
            {
                var val = await context.EvaluateValue(f.Arguments[0], row);
                if (val != null)
                {
                    if (_max == null || ((IComparable)CompoundKey.NormalizeValue(val)).CompareTo(CompoundKey.NormalizeValue(_max)) > 0)
                        _max = val;
                }
            }

            public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>(_max);
        }

        private class GenericState : IAggregateState
        {
            private readonly FunctionCallExpression _f;
            private readonly List<Row> _rows = new List<Row>();

            public GenericState(FunctionCallExpression f) { _f = f; }

            public ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
            {
                _rows.Add(row);
                return default;
            }

            public async ValueTask<object?> Finalize(AggregateEngine engine)
            {
                return await engine.EvaluateAggregate(_f, _rows);
            }
        }
    }
}
