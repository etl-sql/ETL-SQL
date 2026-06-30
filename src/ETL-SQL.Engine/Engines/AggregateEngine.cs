using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

/// <summary>
/// Internal signal raised by <see cref="AggregateEngine.ApplyAggregation"/> when its in-memory
/// group state grows past the RAM governor ceiling. The external aggregate engine catches this to
/// repartition the offending partition (or apply the governor policy), rather than letting the
/// in-memory build consume unbounded RAM.
/// </summary>
internal sealed class AggregateMemoryPressureException : Exception
{
    public AggregateMemoryPressureException(string message) : base(message) { }
}

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
    /// <param name="memoryCeilingBytes">
    /// When &gt; 0, the single-pass group build tracks the live group-state footprint by precise byte
    /// accounting (the real memory of incremental aggregation is O(groups), not O(rows)) and throws
    /// <see cref="AggregateMemoryPressureException"/> once it exceeds the ceiling, so the caller (the
    /// external aggregate engine) can repartition instead of growing unbounded. 0 (default) disables
    /// the check — all existing callers are unaffected. Note: holistic aggregates that buffer whole
    /// rows per group (GenericState) grow beyond this per-group estimate; bounding those is tracked
    /// separately (TODO "Holistic aggregates buffer whole rows").
    /// </param>
    public async Task<List<Row>> ApplyAggregation(IAsyncEnumerable<Row> inputStream, List<Expression>? groupBy, List<SelectColumn> finalColumns, List<string> colNames, Expression? havingClause = null, GroupingSetClause? groupingSet = null, long memoryCeilingBytes = 0)
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
                var setRows = await ApplyAggregation(allBufferedRows.ToAsyncEnumerable(), activeGroupBy, finalColumns, colNames, havingClause, null, memoryCeilingBytes);

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

        // Identify all aggregates in SELECT.
        // Top-level aggregate functions map to a column index; aggregates nested inside CASE /
        // scalar wrappers (e.g. COALESCE(SUM(x), 0)) use ColumnIndex=-1 and are accessed
        // via the AGG_<expr> key written into the result row during finalization.
        for (int i = 0; i < finalColumns.Count; i++)
        {
            if (finalColumns[i].Expression is FunctionCallExpression f && IsAggregateFunction(f))
            {
                aggregateSpecs.Add((i, f, CreateState(f)));
            }
            else if (IsAggregate(finalColumns[i].Expression))
            {
                var nestedAggs = new List<FunctionCallExpression>();
                CollectAggregates(finalColumns[i].Expression, nestedAggs);
                foreach (var nested in nestedAggs)
                {
                    if (!aggregateSpecs.Any(s => s.Function.ToSql().Equals(nested.ToSql(), StringComparison.OrdinalIgnoreCase)))
                        aggregateSpecs.Add((-1, nested, CreateState(nested)));
                }
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

        // RAM governor (precise byte accounting): track the live group-state footprint and bail out
        // (so the external engine can repartition) before it consumes unbounded RAM. We count bytes as
        // groups are created — O(groups) is the real memory of incremental aggregation — instead of
        // sampling the GC heap, which was process-wide and reactive.
        var budget = new MemoryBudgetGuard(memoryCeilingBytes);

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

                if (budget.Enabled)
                {
                    // Each new group adds its key plus a fixed per-state cost (most states are O(1)
                    // running accumulators) plus dictionary-entry overhead.
                    budget.Add(RowMemory.EstimateKeyBytes(key) + (sStates.Length + hStates.Length) * 64L + 48L);
                    if (budget.Exceeded())
                        throw new AggregateMemoryPressureException(
                            $"Aggregation in-memory group state exceeded the memory governor ceiling (~{memoryCeilingBytes / (1024 * 1024)} MB).");
                }
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
        var schema = new TableSchema(colNames);
        foreach (var kvp in groupStates)
        {
            var key = kvp.Key;
            var states = kvp.Value;

            var resRow = new Row(schema);

            // Finalize aggregates
            for (int i = 0; i < aggregateSpecs.Count; i++)
            {
                var val = await states.SelectStates[i].Finalize(this);
                if (aggregateSpecs[i].ColumnIndex >= 0)
                    resRow[aggregateSpecs[i].ColumnIndex] = val;
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
                if (aggregateSpecs.Any(spec => spec.ColumnIndex == i)) continue; // Already filled by aggregate

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
                    resRow[i] = key[groupIdx];
                }
                else if (IsWindowFunction(finalColumns[i].Expression)) { /* Handled later */ }
                else if (IsAggregate(finalColumns[i].Expression))
                {
                    resRow[i] = await _context.EvaluateValue(finalColumns[i].Expression, resRow);
                }
                else
                {
                    // For columns that are neither aggregate nor grouping, try to evaluate them (e.g. constant/literal expressions)
                    try
                    {
                        resRow[i] = await _context.EvaluateValue(finalColumns[i].Expression, resRow);
                    }
                    catch
                    {
                        resRow[i] = null;
                    }
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
        if (expr == null) return false;
        if (expr is FunctionCallExpression f)
        {
            if (IsAggregateFunction(f)) return true;

            return f.Arguments.Any(a => IsAggregate(a));
        }
        if (expr is BinaryExpression b) return IsAggregate(b.Left) || IsAggregate(b.Right);
        if (expr is UnaryExpression u) return IsAggregate(u.Expression);
        if (expr is CaseExpression c)
        {
            if (IsAggregate(c.InputExpression)) return true;
            if (c.WhenClauses.Any(w => IsAggregate(w.Condition) || IsAggregate(w.Result))) return true;
            return IsAggregate(c.ElseResult);
        }
        if (expr is IsNullExpression isnull) return IsAggregate(isnull.Expression);
        if (expr is IsDistinctFromExpression idf) return IsAggregate(idf.Left) || IsAggregate(idf.Right);
        if (expr is InExpression inExpr)
        {
            if (IsAggregate(inExpr.Left)) return true;
            if (inExpr.Right is ListExpression list) return list.Items.Any(e => IsAggregate(e));
            return IsAggregate(inExpr.Right);
        }
        if (expr is BetweenExpression between) return IsAggregate(between.Left) || IsAggregate(between.Start) || IsAggregate(between.End);
        if (expr is LikeExpression like) return IsAggregate(like.Left) || IsAggregate(like.Pattern) || IsAggregate(like.EscapeChar);
        if (expr is ListExpression l) return l.Items.Any(e => IsAggregate(e));

        return false;
    }

    public bool IsWindowFunction(Expression expr) => WindowEngine.ContainsWindowFunction(expr);

    private static bool IsAggregateFunction(FunctionCallExpression f)
    {
        return f.Window == null && IsAggregateFunctionName(f.FunctionName);
    }

    private static bool IsAggregateFunctionName(string functionName)
    {
        var name = functionName.ToUpperInvariant();
        return name == "COUNT" || name == "SUM" || name == "AVG" || name == "MIN" || name == "MAX"
            || name == "EVERY" || name == "ANY" || name == "SOME"
            || name == "APPROX_COUNT_DISTINCT"
            || name == "STRING_AGG" || name == "LIST_AGG"
            || name == "PERCENTILE_CONT" || name == "PERCENTILE_DISC"
            || name == "VAR" || name == "VARP" || name == "VAR_SAMP" || name == "VAR_POP"
            || name == "STDEV" || name == "STDEVP" || name == "STDDEV" || name == "STDDEV_SAMP" || name == "STDDEV_POP"
            || name == "CORR" || name == "COVAR_SAMP" || name == "COVAR_POP"
            || name == "TOTAL" || name == "GROUP_CONCAT";
    }

    private void CollectAggregates(Expression expr, List<FunctionCallExpression> aggs)
    {
        if (expr == null) return;
        if (expr is FunctionCallExpression f)
        {
            if (IsAggregateFunction(f))
            {
                var fSql = f.ToSql();
                if (!aggs.Any(a => a.ToSql().Equals(fSql, StringComparison.OrdinalIgnoreCase)))
                    aggs.Add(f);
            }
            else
            {
                foreach (var arg in f.Arguments) CollectAggregates(arg, aggs);
            }
        }
        else if (expr is BinaryExpression b)
        {
            CollectAggregates(b.Left, aggs);
            CollectAggregates(b.Right, aggs);
        }
        else if (expr is UnaryExpression u) CollectAggregates(u.Expression, aggs);
        else if (expr is CaseExpression c)
        {
            if (c.InputExpression != null) CollectAggregates(c.InputExpression, aggs);
            foreach (var w in c.WhenClauses)
            {
                CollectAggregates(w.Condition, aggs);
                CollectAggregates(w.Result, aggs);
            }
            if (c.ElseResult != null) CollectAggregates(c.ElseResult, aggs);
        }
        else if (expr is IsNullExpression isnull) CollectAggregates(isnull.Expression, aggs);
        else if (expr is IsDistinctFromExpression idf) { CollectAggregates(idf.Left, aggs); CollectAggregates(idf.Right, aggs); }
        else if (expr is InExpression inExpr)
        {
            CollectAggregates(inExpr.Left, aggs);
            CollectAggregates(inExpr.Right, aggs);
        }
        else if (expr is ListExpression l)
        {
            foreach (var item in l.Items) CollectAggregates(item, aggs);
        }
        else if (expr is BetweenExpression between)
        {
            CollectAggregates(between.Left, aggs);
            CollectAggregates(between.Start, aggs);
            CollectAggregates(between.End, aggs);
        }
        else if (expr is LikeExpression like)
        {
            CollectAggregates(like.Left, aggs);
            CollectAggregates(like.Pattern, aggs);
            if (like.EscapeChar != null) CollectAggregates(like.EscapeChar, aggs);
        }
    }

    public async Task<object?> EvaluateAggregate(Expression expr, List<Row> rows)
    {
        if (expr is FunctionCallExpression f)
        {
            var name = f.FunctionName.ToUpperInvariant();
            if (f.Filter != null)
            {
                var filteredRows = new List<Row>();
                foreach (var row in rows)
                {
                    if (await _context.EvaluateCondition(f.Filter, row))
                        filteredRows.Add(row);
                }
                rows = filteredRows;
            }

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
                case "APPROX_COUNT_DISTINCT":
                    var approxState = new HyperLogLogState();
                    foreach (var val in valsByArg[0])
                        approxState.Add(val);
                    return approxState.Estimate();
                case "SUM":
                    {
                        var sumVals = f.IsDistinct ? valsByArg[0].Distinct() : valsByArg[0];
                        var nonNulls = sumVals.Where(v => v != null).ToList();
                        if (nonNulls.Count == 0) return null;
                        return (decimal)nonNulls.Sum(v => SafeToDecimal(v, _context));
                    }
                case "AVG":
                    {
                        var avgVals = f.IsDistinct ? valsByArg[0].Distinct() : valsByArg[0];
                        var nonNulls = avgVals.Where(v => v != null).ToList();
                        if (nonNulls.Count == 0) return null;
                        var avg = nonNulls.Average(v => SafeToDecimal(v, _context));
                        bool allIntsVal = true;
                        foreach (var val in nonNulls)
                        {
                            if (!IsIntegerType(val))
                            {
                                allIntsVal = false;
                                break;
                            }
                        }
                        if (allIntsVal)
                        {
                            avg = Math.Truncate(avg);
                        }
                        return avg;
                    }
                case "MIN":
                    {
                        var nonNulls = valsByArg[0].Where(v => v != null).ToList();
                        if (nonNulls.Count == 0) return null;
                        return nonNulls.Min();
                    }
                case "MAX":
                    {
                        var nonNulls = valsByArg[0].Where(v => v != null).ToList();
                        if (nonNulls.Count == 0) return null;
                        return nonNulls.Max();
                    }
                case "EVERY":
                    return EvaluateBooleanAggregate(valsByArg[0], requireAll: true);
                case "ANY":
                case "SOME":
                    return EvaluateBooleanAggregate(valsByArg[0], requireAll: false);
                case "STRING_AGG":
                    return string.Join(f.Arguments.Count >= 2 ? (await _context.EvaluateValue(f.Arguments[1], new Row()))?.ToString() ?? "" : ",",
                        f.WithinGroupOrderBy != null ? await SortRows(rows, f.WithinGroupOrderBy) : valsByArg[0].Select(v => v?.ToString() ?? ""));
                case "GROUP_CONCAT":
                    {
                        if (valsByArg.Count == 0) return null;
                        var vals = valsByArg[0];
                        var nonNullVals = vals.Where(v => v != null && v != DBNull.Value);
                        if (f.IsDistinct)
                        {
                            nonNullVals = nonNullVals.Distinct();
                        }
                        var listToJoin = nonNullVals.Select(v => v!.ToString()).ToList();
                        if (listToJoin.Count == 0) return null;

                        string separator = ",";
                        if (f.Arguments.Count >= 2)
                        {
                            var sepObj = rows.Count > 0 ? await _context.EvaluateValue(f.Arguments[1], rows[0]) : await _context.EvaluateValue(f.Arguments[1], new Row());
                            separator = sepObj?.ToString() ?? ",";
                        }
                        return string.Join(separator, listToJoin);
                    }
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
        var numbers = vals.Where(v => v != null).Select(v => SafeToDecimal(v, _context)).ToList();
        int n = numbers.Count;
        if (n == 0 || (!population && n == 1)) return null;

        decimal avg = numbers.Average();
        decimal sumSqDiff = numbers.Sum(x => (x - avg) * (x - avg));
        return sumSqDiff / (population ? n : n - 1);
    }

    private static bool? EvaluateBooleanAggregate(IEnumerable<object?> vals, bool requireAll)
    {
        bool hasValue = false;
        bool result = requireAll;
        foreach (var val in vals)
        {
            if (val == null || val == DBNull.Value) continue;
            hasValue = true;
            bool boolVal;
            try { boolVal = Convert.ToBoolean(val); }
            catch { boolVal = !string.IsNullOrEmpty(val.ToString()); }

            if (requireAll && !boolVal) return false;
            if (!requireAll && boolVal) return true;
            result = boolVal;
        }
        return hasValue ? result : null;
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
                xNums.Add(SafeToDecimal(xVals[i], _context));
                yNums.Add(SafeToDecimal(yVals[i], _context));
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
                xNums.Add(SafeToDecimal(xVals[i], _context));
                yNums.Add(SafeToDecimal(yVals[i], _context));
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
            if (v != null) sortedVals.Add(SafeToDecimal(v, _context));
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
            if (v != null) sortedVals.Add(SafeToDecimal(v, _context));
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
    private static decimal SafeToDecimal(object? v, IExecutionContext? context = null)
    {
        if (v == null) return 0m;
        if (v is System.Numerics.BigInteger bi) return (decimal)bi;
        if (v is DateTime dt) throw new ExecutionException($"Cannot perform numeric aggregation on a date value: '{dt:yyyy-MM-dd HH:mm:ss}'. Ensure you are not summing a grouping column like 'month'.");
        if (v is TimeSpan ts) throw new ExecutionException($"Cannot perform numeric aggregation on a time duration: '{ts}'.");
        try { return Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex)
        {
            if (context?.SecurityService?.IsTestMode == true)
            {
                return 0m;
            }
            throw new ExecutionException($"Invalid numeric value for aggregation: '{v}'. Details: {ex.Message}");
        }
    }

    private static bool IsIntegerType(object? val)
    {
        if (val == null) return false;
        return val is int || val is long || val is short || val is byte || val is sbyte || val is uint || val is ulong || val is ushort || val is System.Numerics.BigInteger;
    }

    private IAggregateState CreateState(FunctionCallExpression f)
    {
        var name = f.FunctionName.ToUpperInvariant();
        return name switch
        {
            "SUM" => new SumState(f.IsDistinct),
            "TOTAL" => new TotalState(f.IsDistinct),
            "COUNT" => new CountState(f.IsDistinct, f.Arguments.Count == 0 || (f.Arguments[0] is IdentifierExpression id && id.Name == "*")),
            "AVG" => new AvgState(f.IsDistinct),
            "MIN" => new MinState(),
            "MAX" => new MaxState(),
            "EVERY" => new BooleanAggregateState(requireAll: true),
            "ANY" => new BooleanAggregateState(requireAll: false),
            "SOME" => new BooleanAggregateState(requireAll: false),
            "APPROX_COUNT_DISTINCT" => new ApproxCountDistinctState(),
            "VAR" or "VAR_SAMP" or "VARP" or "VAR_POP"
                or "STDEV" or "STDDEV" or "STDDEV_SAMP" or "STDEVP" or "STDDEV_POP" => new VarianceState(name),
            "COVAR_SAMP" or "COVAR_POP" or "CORR" => new CovarianceState(name),
            _ => new GenericState(f)
        };
    }

    private interface IAggregateState
    {
        ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context);
        ValueTask<object?> Finalize(AggregateEngine engine);
    }

    private static async ValueTask<bool> PassesFilter(Row row, FunctionCallExpression f, IExecutionContext context)
        => f.Filter == null || await context.EvaluateCondition(f.Filter, row);

    private class SumState : IAggregateState
    {
        private decimal _sum = 0;
        private bool _hasValue = false;
        private HashSet<object?>? _distinctValues;
        private readonly bool _isDistinct;

        public SumState(bool isDistinct) { _isDistinct = isDistinct; if (_isDistinct) _distinctValues = new HashSet<object?>(); }

        public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
        {
            if (!await PassesFilter(row, f, context)) return;
            var val = await context.EvaluateValue(f.Arguments[0], row);
            if (val != null)
            {
                if (_isDistinct)
                {
                    if (_distinctValues!.Add(val))
                    {
                        _sum += SafeToDecimal(val, context);
                        _hasValue = true;
                    }
                }
                else
                {
                    _sum += SafeToDecimal(val, context);
                    _hasValue = true;
                }
            }
        }

        public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>(_hasValue ? _sum : null);
    }

    private class TotalState : IAggregateState
    {
        private decimal _sum = 0;
        private HashSet<object?>? _distinctValues;
        private readonly bool _isDistinct;

        public TotalState(bool isDistinct)
        {
            _isDistinct = isDistinct;
            if (_isDistinct) _distinctValues = new HashSet<object?>();
        }

        public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
        {
            if (!await PassesFilter(row, f, context)) return;
            var val = await context.EvaluateValue(f.Arguments[0], row);
            if (val != null)
            {
                if (_isDistinct)
                {
                    if (_distinctValues!.Add(val))
                    {
                        _sum += SafeToDecimal(val, context);
                    }
                }
                else
                {
                    _sum += SafeToDecimal(val, context);
                }
            }
        }

        public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>(_sum);
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
            if (!await PassesFilter(row, f, context)) return;
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
        private bool _allIntegers = true;

        public AvgState(bool isDistinct) { _isDistinct = isDistinct; if (_isDistinct) _distinctValues = new HashSet<object?>(); }

        public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
        {
            if (!await PassesFilter(row, f, context)) return;
            var val = await context.EvaluateValue(f.Arguments[0], row);
            if (val != null)
            {
                if (_allIntegers)
                {
                    if (!IsIntegerType(val))
                    {
                        _allIntegers = false;
                    }
                }

                if (_isDistinct)
                {
                    if (_distinctValues!.Add(val))
                    {
                        _sum += SafeToDecimal(val, context);
                        _count++;
                    }
                }
                else
                {
                    _sum += SafeToDecimal(val, context);
                    _count++;
                }
            }
        }

        public ValueTask<object?> Finalize(AggregateEngine engine)
        {
            if (_count == 0) return new ValueTask<object?>((object?)null);
            decimal result = _sum / _count;
            if (_allIntegers)
            {
                result = Math.Truncate(result);
            }
            return new ValueTask<object?>(result);
        }
    }

    private class MinState : IAggregateState
    {
        private object? _min = null;

        public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
        {
            if (!await PassesFilter(row, f, context)) return;
            var val = await context.EvaluateValue(f.Arguments[0], row);
            if (val != null)
            {
                if (_min == null || ((IComparable)CompoundKey.NormalizeValue(val)!).CompareTo(CompoundKey.NormalizeValue(_min!)) < 0)
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
            if (!await PassesFilter(row, f, context)) return;
            var val = await context.EvaluateValue(f.Arguments[0], row);
            if (val != null)
            {
                if (_max == null || ((IComparable)CompoundKey.NormalizeValue(val)!).CompareTo(CompoundKey.NormalizeValue(_max!)) > 0)
                    _max = val;
            }
        }

        public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>(_max);
    }

    private class BooleanAggregateState : IAggregateState
    {
        private readonly bool _requireAll;
        private bool _hasValue;
        private bool _result;

        public BooleanAggregateState(bool requireAll)
        {
            _requireAll = requireAll;
            _result = requireAll;
        }

        public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
        {
            if (!await PassesFilter(row, f, context)) return;
            var val = await context.EvaluateValue(f.Arguments[0], row);
            if (val == null || val == DBNull.Value) return;
            _hasValue = true;
            bool boolVal;
            try { boolVal = Convert.ToBoolean(val); }
            catch { boolVal = !string.IsNullOrEmpty(val.ToString()); }

            if (_requireAll && !boolVal) _result = false;
            else if (!_requireAll && boolVal) _result = true;
            else if (!_requireAll && !_result) _result = false;
        }

        public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>(_hasValue ? _result : null);
    }

    private sealed class ApproxCountDistinctState : IAggregateState
    {
        private readonly HyperLogLogState _state = new();

        public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
        {
            if (!await PassesFilter(row, f, context)) return;
            var val = await context.EvaluateValue(f.Arguments[0], row);
            _state.Add(val);
        }

        public ValueTask<object?> Finalize(AggregateEngine engine) => new ValueTask<object?>(_state.Estimate());
    }

    private sealed class HyperLogLogState
    {
        private const int Precision = 14;
        private const int RegisterCount = 1 << Precision;
        private readonly byte[] _registers = new byte[RegisterCount];

        public void Add(object? value)
        {
            if (value == null || value == DBNull.Value) return;

            ulong hash = HashValue(value);
            int index = (int)(hash >> (64 - Precision));
            ulong remaining = hash << Precision;
            int rank = CountLeadingZeros(remaining, 64 - Precision) + 1;
            if (rank > _registers[index]) _registers[index] = (byte)rank;
        }

        public decimal Estimate()
        {
            double sum = 0;
            int zeros = 0;
            foreach (var register in _registers)
            {
                sum += Math.Pow(2.0, -register);
                if (register == 0) zeros++;
            }

            const double alpha = 0.7213 / (1.0 + 1.079 / RegisterCount);
            double estimate = alpha * RegisterCount * RegisterCount / sum;

            if (estimate <= 2.5 * RegisterCount && zeros > 0)
                estimate = RegisterCount * Math.Log((double)RegisterCount / zeros);

            return (decimal)Math.Round(estimate, 2);
        }

        private static ulong HashValue(object value)
        {
            string stableValue = $"{value.GetType().FullName}:{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}";
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes(stableValue), hash);
            return BinaryPrimitives.ReadUInt64BigEndian(hash[..8]);
        }

        private static int CountLeadingZeros(ulong value, int width)
        {
            if (value == 0) return width;
            int count = 0;
            ulong mask = 1UL << 63;
            for (int i = 0; i < width; i++)
            {
                if ((value & mask) != 0) break;
                count++;
                mask >>= 1;
            }
            return count;
        }
    }

    private class GenericState : IAggregateState
    {
        private readonly FunctionCallExpression _f;
        private readonly List<Row> _rows = new List<Row>();

        public GenericState(FunctionCallExpression f) { _f = f; }

        public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
        {
            if (!await PassesFilter(row, f, context)) return;
            _rows.Add(row);
        }

        public async ValueTask<object?> Finalize(AggregateEngine engine)
        {
            return await engine.EvaluateAggregate(_f, _rows);
        }
    }

    /// <summary>
    /// Incremental single-pass VAR/VAR_SAMP/VARP/VAR_POP/STDEV/STDDEV(_SAMP/_POP)/STDEVP state via
    /// Welford's online algorithm — O(1) memory per group instead of buffering every row through
    /// <see cref="GenericState"/>. Accumulates in double for numerical stability and returns decimal.
    /// </summary>
    private sealed class VarianceState : IAggregateState
    {
        private readonly bool _population;
        private readonly bool _sqrt;
        private long _n;
        private double _mean;
        private double _m2;

        public VarianceState(string name)
        {
            var u = name.ToUpperInvariant();
            _sqrt = u.StartsWith("STDEV", StringComparison.Ordinal) || u.StartsWith("STDDEV", StringComparison.Ordinal);
            _population = u is "VARP" or "VAR_POP" or "STDEVP" or "STDDEV_POP";
        }

        public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
        {
            if (!await PassesFilter(row, f, context)) return;
            var val = await context.EvaluateValue(f.Arguments[0], row);
            if (val == null) return;
            double x = (double)SafeToDecimal(val, context);
            _n++;
            double delta = x - _mean;
            _mean += delta / _n;
            _m2 += delta * (x - _mean);
        }

        public ValueTask<object?> Finalize(AggregateEngine engine)
        {
            if (_n == 0 || (!_population && _n == 1)) return new ValueTask<object?>((object?)null);
            double variance = _population ? _m2 / _n : _m2 / (_n - 1);
            if (variance < 0) variance = 0; // clamp tiny negative drift from floating-point rounding
            double result = _sqrt ? Math.Sqrt(variance) : variance;
            return new ValueTask<object?>((object?)(decimal)result);
        }
    }

    /// <summary>
    /// Incremental single-pass COVAR_SAMP/COVAR_POP/CORR state via the online co-moment algorithm.
    /// Pairs are counted only when both arguments are non-null (matching the buffered semantics).
    /// O(1) memory per group; correlation uses population co-moments (the per-n factors cancel).
    /// </summary>
    private sealed class CovarianceState : IAggregateState
    {
        private readonly bool _population;
        private readonly bool _correlation;
        private long _n;
        private double _meanX;
        private double _meanY;
        private double _c;
        private double _m2x;
        private double _m2y;

        public CovarianceState(string name)
        {
            var u = name.ToUpperInvariant();
            _correlation = u == "CORR";
            _population = _correlation || u == "COVAR_POP";
        }

        public async ValueTask Update(Row row, FunctionCallExpression f, IExecutionContext context)
        {
            if (!await PassesFilter(row, f, context)) return;
            var xv = await context.EvaluateValue(f.Arguments[0], row);
            var yv = await context.EvaluateValue(f.Arguments[1], row);
            if (xv == null || yv == null) return;
            double x = (double)SafeToDecimal(xv, context);
            double y = (double)SafeToDecimal(yv, context);
            _n++;
            double dx = x - _meanX;
            double dy = y - _meanY;
            _meanX += dx / _n;
            _meanY += dy / _n;
            _c += dx * (y - _meanY);
            _m2x += dx * (x - _meanX);
            _m2y += dy * (y - _meanY);
        }

        public ValueTask<object?> Finalize(AggregateEngine engine)
        {
            if (_correlation)
            {
                if (_n == 0) return new ValueTask<object?>((object?)null);
                double denom = Math.Sqrt(_m2x * _m2y);
                if (denom == 0 || double.IsNaN(denom)) return new ValueTask<object?>((object?)null);
                return new ValueTask<object?>((object?)(decimal)(_c / denom));
            }
            if (_n == 0 || (!_population && _n == 1)) return new ValueTask<object?>((object?)null);
            double cov = _population ? _c / _n : _c / (_n - 1);
            return new ValueTask<object?>((object?)(decimal)cov);
        }
    }
}
