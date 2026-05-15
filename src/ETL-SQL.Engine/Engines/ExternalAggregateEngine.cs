using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Spill;
using ETL_SQL.Core.Execution;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;

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
        private readonly IBufferManager? _bufferManager;
        public int PartitionCount => Math.Max(1, _context.ExternalHashPartitions);


        public ExternalAggregateEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
            _inMemoryEngine = new AggregateEngine(context, logger);
            _bufferManager = _context.ServiceProvider?.GetService<IBufferManager>();
        }

        /// <summary>Applies aggregation by partitioning the stream into disk files and processing each partition sequentially.</summary>
        public async IAsyncEnumerable<Row> ApplyAggregationExternal(IAsyncEnumerable<Row> inputStream, List<Expression>? groupBy, List<SelectColumn> finalColumns, List<string> colNames, Expression? havingClause = null, GroupingSetClause? groupingSet = null)
        {
            using var cursor = _bufferManager != null ? await _bufferManager.AcquireCursorAsync(_context.SessionId ?? "DEFAULT", owner: this) : null;
            bool yieldedAny = false;
            try
            {
                // 1. Partition Phase (supports one-pass expansion for grouping sets)
                string[] partitionPaths;
                List<List<Expression>>? expandedSets = null;
                
                if (groupingSet != null && groupingSet.Type != GroupingSetType.None)
                {
                    expandedSets = _inMemoryEngine.ExpandGroupingSets(groupingSet);
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
                                vals[i] = SpillSerializationHelper.UnwrapValue(rawVal);
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
                        var partResults = await _inMemoryEngine.ApplyAggregation(bucket.Rows.ToAsyncEnumerable(), activeGroupBy, finalColumns, colNames, havingClause);
                        
                        _context.Telemetry.AggregateGroupsCount += partResults.Count;

                        // Handle GROUPING() / NULL substitution for sub-sets
                        if (expandedSets != null && groupBy != null)
                        {
                            var activeKeys = new HashSet<string>(activeGroupBy!.Select(e => NormalizedToSql(e)), StringComparer.OrdinalIgnoreCase);
                            foreach (var row in partResults)
                            {
                                foreach (var expr in groupBy)
                                {
                                    if (!activeKeys.Contains(NormalizedToSql(expr)))
                                    {
                                        var colName = expr is IdentifierExpression id ? id.Name.Split('.').Last() : NormalizedToSql(expr);
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
                                yieldedAny = true;
                                yield return row;
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
                    var globals = await _inMemoryEngine.ApplyAggregation(AsyncEnumerable.Empty<Row>(), groupBy, finalColumns, colNames, havingClause);
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
                        pIdx = (new ETL_SQL.Data.CompoundKey(vals).GetHashCode() & 0x7FFFFFFF) % PartitionCount;
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
                _context.Telemetry.PartitionsCount += used;
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
                            pIdx = (new ETL_SQL.Data.CompoundKey(sIdx, vals).GetHashCode() & 0x7FFFFFFF) % PartitionCount;
                        }
                        else pIdx = (sIdx & 0x7FFFFFFF) % PartitionCount;

                        // Attach SetIndex to a CLONE of the row so it can be identified during merge phase
                        // and doesn't interfere with other sets or buffered writers.
                        var rowToStore = row.Clone();
                        rowToStore["__SET_IDX"] = sIdx;
                        await writers[pIdx].WriteRowAsync(rowToStore);
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
                _context.Telemetry.PartitionsCount += used;
                if (totalInput > 0) _context.Telemetry.AggregateExpansionRatio = (double)totalExpanded / totalInput;
                _logger.Debug("[HYPER-SCALE] Expanded {Input} rows into {Expanded} intermediate rows for GroupingSets (Ratio: {Ratio:F2}).", totalInput, totalExpanded, _context.Telemetry.AggregateExpansionRatio);
            }
            return names;
        }



        private string NormalizedToSql(Expression e)
        {
            if (e == null) return "";
            var sql = e.ToSql().ToUpperInvariant();
            // Remove parentheses for matching purposes
            while (sql.StartsWith("(") && sql.EndsWith(")")) sql = sql.Substring(1, sql.Length - 2);
            return sql.Trim();
        }
    }
}

