using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// High-scale aggregation engine that spills to disk (partitioned files) when data exceeds memory thresholds.
    /// </summary>
    public class ExternalAggregateEngine
    {
        private readonly IExecutionContext _context;
        private readonly AggregateEngine _inMemoryEngine;
        private readonly ILogger _logger;
        private readonly string _tempDir;
        private int PartitionCount => _context.ExternalHashPartitions;



        public ExternalAggregateEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
            _inMemoryEngine = new AggregateEngine(context, logger);
            _tempDir = Path.Combine(Path.GetTempPath(), "ETL-SQL", "AggSpill", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
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
                foreach (var path in partitionPaths)
                {
                    if (!File.Exists(path)) continue;

                    var stream = ReadPartitionStream(path);
                    var groups = new Dictionary<CompoundKey, (List<Row> Rows, int SetIndex)>();

                    await foreach (var row in stream)
                    {
                        // Extract metadata (SetIndex) stored in the partition row
                        int setIndex = Convert.ToInt32(row["__SET_IDX"] ?? 0);
                        var activeGroupBy = expandedSets != null ? expandedSets[setIndex] : groupBy;

                        CompoundKey key;
                        if (activeGroupBy != null && activeGroupBy.Count > 0)
                        {
                            var vals = new object?[activeGroupBy.Count];
                            for (int i = 0; i < activeGroupBy.Count; i++)
                                vals[i] = row.Columns.TryGetValue(activeGroupBy[i].ToSql(), out var v) ? v : await _context.EvaluateValue(activeGroupBy[i], row);
                            key = new CompoundKey(setIndex, vals);
                        }
                        else key = new CompoundKey(setIndex, "GLOBAL");

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

                    File.Delete(path);
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
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            }
        }

        private async Task<string[]> PartitionStream(IAsyncEnumerable<Row> stream, List<Expression>? groupBy)
        {
            var paths = new string[PartitionCount];
            var writers = new StreamWriter[PartitionCount];

            for (int i = 0; i < PartitionCount; i++)
            {
                paths[i] = Path.Combine(_tempDir, $"agg_{i}.tmp");
                writers[i] = new StreamWriter(paths[i]);
            }

            try
            {
                await foreach (var row in stream)
                {
                    int pIdx = 0;
                    if (groupBy != null && groupBy.Count > 0)
                    {
                        int hash = 17;
                        foreach (var g in groupBy)
                        {
                            var val = await _context.EvaluateValue(g, row);
                            hash = hash * 31 + (val?.GetHashCode() ?? 0);
                        }
                        pIdx = Math.Abs(hash % PartitionCount);
                    }

                    row["__SET_IDX"] = 0;
                    var json = System.Text.Json.JsonSerializer.Serialize(row.Columns);
                    _context.TotalSpilledBytes += System.Text.Encoding.UTF8.GetByteCount(json) + 2;
                    await writers[pIdx].WriteLineAsync(json);
                }
            }
            finally
            {
                int used = 0;
                foreach (var w in writers) { if (w.BaseStream.Length > 0) used++; w.Flush(); w.Close(); }
                _context.PartitionsCount += used;
            }
            return paths;
        }

        private async Task<string[]> PartitionStreamMultiSet(IAsyncEnumerable<Row> stream, List<List<Expression>> sets)
        {
            var paths = new string[PartitionCount];
            var writers = new StreamWriter[PartitionCount];
            for (int i = 0; i < PartitionCount; i++)
            {
                paths[i] = Path.Combine(_tempDir, $"agg_{i}.tmp");
                writers[i] = new StreamWriter(paths[i]);
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
                            int hash = 17;
                            hash = hash * 31 + sIdx; // Include set index in hash to distribute sets better
                            foreach (var g in activeGroupBy)
                            {
                                var val = await _context.EvaluateValue(g, row);
                                hash = hash * 31 + (val?.GetHashCode() ?? 0);
                            }
                            pIdx = Math.Abs(hash % PartitionCount);
                        }
                        else pIdx = Math.Abs(sIdx % PartitionCount);

                        // Attach SetIndex to the row so it can be identified during merge phase
                        row["__SET_IDX"] = sIdx;
                        var json = System.Text.Json.JsonSerializer.Serialize(row.Columns);
                        _context.TotalSpilledBytes += System.Text.Encoding.UTF8.GetByteCount(json) + 2;
                        await writers[pIdx].WriteLineAsync(json);
                    }
                }
            }
            finally
            {
                int used = 0;
                foreach (var w in writers) { if (w.BaseStream.Length > 0) used++; w.Flush(); w.Close(); }
                _context.PartitionsCount += used;
                if (totalInput > 0) _context.AggregateExpansionRatio = (double)totalExpanded / totalInput;
                _logger.Debug("[HYPER-SCALE] Expanded {Input} rows into {Expanded} intermediate rows for GroupingSets (Ratio: {Ratio:F2}).", totalInput, totalExpanded, _context.AggregateExpansionRatio);
            }
            return paths;
        }

        private async IAsyncEnumerable<Row> ReadPartitionStream(string path)
        {
            using var reader = new StreamReader(path);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var cols = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(line);
                if (cols != null)
                {
                    var row = new Row();
                    foreach (var kvp in cols) row[kvp.Key] = JsonElementToValue(kvp.Value);
                    yield return row;
                }
            }
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
