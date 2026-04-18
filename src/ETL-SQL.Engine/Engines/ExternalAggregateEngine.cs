using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// High-scale aggregation engine that spills to disk (partitioned files) when data exceeds memory thresholds.
    /// </summary>
    public class ExternalAggregateEngine
    {
        private readonly IExecutionContext _context;
        private readonly ILogger _logger;
        private readonly AggregateEngine _inMemoryEngine;
        public int PartitionCount => Math.Max(1, _context.ExternalHashPartitions);


        public ExternalAggregateEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
            _inMemoryEngine = new AggregateEngine(context, logger);
        }

        /// <summary>Applies aggregation by partitioning the stream into disk files and processing each partition sequentially.</summary>
        public async IAsyncEnumerable<Row> ApplyAggregationExternal(IAsyncEnumerable<Row> inputStream, List<Expression>? groupBy, List<SelectColumn> finalColumns, List<string> colNames, Expression? havingClause = null, GroupingSetClause? groupingSet = null)
        {
            bool yieldedAny = false;
            try
            {
                // 1. Partition Phase (supports one-pass expansion for grouping sets)
                string[] partitionPaths;
                List<List<Expression>> expandedSets = null;
                
                if (groupingSet != null && groupingSet.Type != GroupingSetType.None)
                {
                    expandedSets = ExpandGroupingSets(groupingSet);
                    partitionPaths = await PartitionStreamMultiSet(inputStream, expandedSets);
                    
                    // Ensure we have a reference list of ALL participating columns for NULL substitution later
                    if (groupBy == null || groupBy.Count == 0)
                    {
                        groupBy = expandedSets.SelectMany(s => s)
                            .GroupBy(e => e.ToSql().ToLower())
                            .Select(g => g.First())
                            .ToList();
                    }
                }
                else
                {
                    partitionPaths = await PartitionStream(inputStream, groupBy);
                }

                // 2. Aggregate Phase (one partition at a time)
                foreach (var name in partitionPaths)
                {
                    await using var reader = await _context.SpillStore.CreateReaderAsync(name);
                    var groups = new Dictionary<CompoundKey, (List<Row> Rows, int SetIndex)>();

                    await foreach (var row in reader.AsEnumerableAsync())
                    {
                        // Extract metadata (SetIndex) stored in the partition row
                        int setIndex = Convert.ToInt32(row["__SET_IDX"] ?? 0);
                        var activeGroupBy = expandedSets != null ? expandedSets[setIndex] : groupBy;

                        ETL_SQL.Data.CompoundKey key;
                        if (activeGroupBy != null && activeGroupBy.Count > 0)
                        {
                            var vals = new object?[activeGroupBy.Count];
                            for (int i = 0; i < activeGroupBy.Count; i++)
                            {
                                var colKey = activeGroupBy[i].ToSql();
                                var rawVal = row.Columns.TryGetValue(colKey, out var v) ? v : await _context.EvaluateValue(activeGroupBy[i], row);
                                vals[i] = ETL_SQL.Data.CompoundKey.NormalizeValue(rawVal is System.Text.Json.JsonElement je ? JsonElementToValue(je) : rawVal);
                            }
                            key = new ETL_SQL.Data.CompoundKey(setIndex, vals);
                        }
                        else key = new ETL_SQL.Data.CompoundKey(setIndex, "GLOBAL");

                        if (!groups.TryGetValue(key, out var bucket)) 
                        { 
                            bucket = (new List<Row>(), setIndex); 
                            groups[key] = bucket; 
                        }
                        bucket.Rows.Add(row);
                    }

                    foreach (var bucket in groups.Values)
                    {
                        var activeGroupBy = expandedSets != null ? expandedSets[bucket.SetIndex] : groupBy;
                        var partResults = await _inMemoryEngine.ApplyAggregation(bucket.Rows, activeGroupBy, finalColumns, colNames, havingClause);
                        
                        _context.AggregateGroupsCount += partResults.Count;

                        // Handle GROUPING() / NULL substitution for sub-sets
                        if (expandedSets != null && groupBy != null)
                        {
                            var activeKeys = new HashSet<string>(activeGroupBy.Select(e => e.ToSql()), StringComparer.OrdinalIgnoreCase);
                            foreach (var resRow in partResults)
                            {
                                foreach (var expr in groupBy)
                                {
                                    if (!activeKeys.Contains(expr.ToSql()))
                                    {
                                        var colName = expr is IdentifierExpression id ? id.Name.Split('.').Last() : expr.ToSql();
                                        var matchIdx = colNames.FindIndex(c => c.Equals(colName, StringComparison.OrdinalIgnoreCase));
                                        if (matchIdx >= 0) resRow[colNames[matchIdx]] = null;
                                    }
                                }
                                yieldedAny = true;
                                yield return resRow;
                            }
                        }
                        else
                        {
                            foreach (var resRow in partResults) 
                            { 
                                yieldedAny = true; 
                                yield return resRow; 
                            }
                        }
                    }
                }

                // Handle global aggregation if no rows were found but aggregates exist
                // Bug Fix: Only yield if we haven't already produced rows (avoids duplicates in partitioned external agg)
                if (!yieldedAny && finalColumns.Any(c => _inMemoryEngine.IsAggregate(c.Expression)) 
                    && (groupBy == null || groupBy.Count == 0) && (groupingSet == null || groupingSet.Type == GroupingSetType.None))
                {
                    var globals = await _inMemoryEngine.ApplyAggregation(new List<Row>(), groupBy, finalColumns, colNames, havingClause);
                    foreach (var g in globals) yield return g;
                }
            }
            finally
            {
                // Root SpillStore in Evaluator will handle cleanup
            }
        }

        private async Task<string[]> PartitionStream(IAsyncEnumerable<Row> stream, List<Expression>? groupBy)
        {
            var names = new string[PartitionCount];
            var writers = new ETL_SQL.Core.Spill.ISpillWriter[PartitionCount];
 
            var prefix = Guid.NewGuid().ToString("N");
            for (int i = 0; i < PartitionCount; i++)
            {
                names[i] = $"agg_{prefix}_{i}.tmp";
                writers[i] = await _context.SpillStore.CreateWriterAsync(names[i]);
            }

            try
            {
                await foreach (var row in stream)
                {
                    int pIdx = 0;
                    if (groupBy != null && groupBy.Count > 0)
                    {
                        var vals = new object?[groupBy.Count];
                        for (int i = 0; i < groupBy.Count; i++)
                        {
                            var rawVal = await _context.EvaluateValue(groupBy[i], row);
                            vals[i] = ETL_SQL.Data.CompoundKey.NormalizeValue(rawVal);
                        }
                        pIdx = Math.Abs(new ETL_SQL.Data.CompoundKey(vals).GetHashCode() % PartitionCount);
                    }

                    row["__SET_IDX"] = 0;
                    await writers[pIdx].WriteRowAsync(row);
                }
            }
            finally
            {
                int used = 0;
                foreach (var w in writers) 
                { 
                    if (w != null)
                    {
                        used++;
                        await w.DisposeAsync();
                    }
                }
                _context.PartitionsCount += used;
            }
            return names;
        }

        private async Task<string[]> PartitionStreamMultiSet(IAsyncEnumerable<Row> stream, List<List<Expression>> sets)
        {
            var names = new string[PartitionCount];
            var writers = new ETL_SQL.Core.Spill.ISpillWriter[PartitionCount];
            var prefix = Guid.NewGuid().ToString("N");
            for (int i = 0; i < PartitionCount; i++)
            {
                names[i] = $"agg_{prefix}_{i}.tmp";
                writers[i] = await _context.SpillStore.CreateWriterAsync(names[i]);
            }

            long totalInput = 0;
            long totalExpanded = 0;

            try
            {
                await foreach (var row in stream)
                {
                    totalInput++;
                    for (int sIdx = 0; sIdx < sets.Count; sIdx++)
                    {
                        totalExpanded++;
                        var activeGroupBy = sets[sIdx];
                        int pIdx = 0;

                        if (activeGroupBy.Count > 0)
                        {
                            var vals = new object?[activeGroupBy.Count];
                            for (int i = 0; i < activeGroupBy.Count; i++)
                            {
                                var rawVal = await _context.EvaluateValue(activeGroupBy[i], row);
                                vals[i] = ETL_SQL.Data.CompoundKey.NormalizeValue(rawVal);
                            }
                            // Include set index in hash to distribute sets better
                            pIdx = Math.Abs(new ETL_SQL.Data.CompoundKey(sIdx, vals).GetHashCode() % PartitionCount);
                        }
                        else pIdx = Math.Abs(sIdx % PartitionCount);

                        // Attach SetIndex to the row so it can be identified during merge phase
                        row["__SET_IDX"] = sIdx;
                        await writers[pIdx].WriteRowAsync(row);
                    }
                }
            }
            finally
            {
                int used = 0;
                foreach (var w in writers) 
                { 
                    if (w != null)
                    {
                        used++;
                        await w.DisposeAsync();
                    }
                }
                _context.PartitionsCount += used;
                if (totalInput > 0) _context.AggregateExpansionRatio = (double)totalExpanded / totalInput;
                _logger.Debug("[HYPER-SCALE] Expanded {Input} rows into {Expanded} intermediate rows for GroupingSets (Ratio: {Ratio:F2}).", totalInput, totalExpanded, _context.AggregateExpansionRatio);
            }
            return names;
        }


        private static List<List<Expression>> ExpandGroupingSets(GroupingSetClause clause)
        {
            var cols = clause.GroupSets[0];
            int n = cols.Count;

            if (clause.Type == GroupingSetType.Rollup)
            {
                var result = new List<List<Expression>>();
                for (int i = n; i >= 0; i--) result.Add(cols.Take(i).ToList());
                return result;
            }

            if (clause.Type == GroupingSetType.Cube)
            {
                var result = new List<List<Expression>>();
                for (int mask = (1 << n) - 1; mask >= 0; mask--)
                {
                    var subset = new List<Expression>();
                    for (int bit = 0; bit < n; bit++) if ((mask & (1 << bit)) != 0) subset.Add(cols[bit]);
                    result.Add(subset);
                }
                return result;
            }

            return clause.GroupSets.Select(s => s.ToList()).ToList();
        }

        private static object? JsonElementToValue(System.Text.Json.JsonElement element) =>
            element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number  => element.TryGetDecimal(out var d) ? d : (object?)element.GetDouble(),
                System.Text.Json.JsonValueKind.String  => decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (object?)d : (DateTime.TryParse(element.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (object?)element.GetString()),
                System.Text.Json.JsonValueKind.True    => (object?)true,
                System.Text.Json.JsonValueKind.False   => (object?)false,
                System.Text.Json.JsonValueKind.Null    => null,
                _                                      => element.GetRawText()
            };
    }
}
