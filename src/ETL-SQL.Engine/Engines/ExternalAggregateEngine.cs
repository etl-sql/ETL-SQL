using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Engines;
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
        string[]? partitionPaths = null;
        long[]? partitionRowCounts = null;
        long[][]? setCounts = null; // per-(partition, set) counts for the grouping-set path
        try
        {
            // 1. Partition Phase (supports one-pass expansion for grouping sets)
            List<List<Expression>>? expandedSets = null;

            if (groupingSet != null && groupingSet.Type != GroupingSetType.None)
            {
                expandedSets = _inMemoryEngine.ExpandGroupingSets(groupingSet);
                var multiSet = await PartitionStreamMultiSet(inputStream, expandedSets);
                partitionPaths = multiSet.Names;
                setCounts = multiSet.SetCounts;

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
                var partitioned = await PartitionStream(inputStream, groupBy);
                partitionPaths = partitioned.Names;
                partitionRowCounts = partitioned.RowCounts;
            }

            // 2. Aggregate Phase (one partition at a time)
            for (var partitionIndex = 0; partitionIndex < partitionPaths.Length; partitionIndex++)
            {
                var name = partitionPaths[partitionIndex];
                if (expandedSets == null)
                {
                    if (partitionRowCounts?[partitionIndex] == 0)
                        continue;

                    await using var directReader = await _context.SpillStore.CreateReaderAsync(name);
                    var partResults = await _inMemoryEngine.ApplyAggregation(
                        directReader.AsEnumerableAsync(),
                        groupBy,
                        finalColumns,
                        colNames,
                        havingClause);

                    _context.Telemetry.AggregateGroupsCount += partResults.Count;
                    foreach (var resRow in partResults)
                    {
                        yieldedAny = true;
                        yield return resRow;
                    }

                    continue;
                }

                // Aggregate each grouping set incrementally: stream the partition once per set into the
                // (already single-pass, O(groups)) in-memory engine, instead of buffering every partition
                // row in a Dictionary<key, List<Row>>. The old buffering was O(rows-in-partition) and the
                // main RAM risk for CUBE/ROLLUP at scale; this trades a per-set re-read of the partition
                // file for bounded memory.
                for (int setIdx = 0; setIdx < expandedSets.Count; setIdx++)
                {
                    // Skip (partition, set) pairs with no rows — calling the in-memory engine on an empty
                    // stream would emit a spurious global-aggregate row.
                    if (setCounts != null && setCounts[partitionIndex][setIdx] == 0) continue;

                    var activeGroupBy = expandedSets[setIdx];
                    var partResults = await _inMemoryEngine.ApplyAggregation(
                        ReadPartitionForSet(name, setIdx), activeGroupBy, finalColumns, colNames, havingClause);

                    if (partResults.Count == 0) continue;
                    _context.Telemetry.AggregateGroupsCount += partResults.Count;

                    // Handle GROUPING() / NULL substitution for sub-sets
                    if (groupBy != null)
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
            if (partitionPaths != null)
            {
                foreach (var path in partitionPaths)
                {
                    try
                    {
                        _context.SpillStore.DeleteChunk(path);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error cleaning up external aggregate partition {path}: {ex.Message}");
                    }
                }
            }
        }
    }

    private sealed record PartitionResult(string[] Names, long[] RowCounts);

    private async Task<PartitionResult> PartitionStream(IAsyncEnumerable<Row> stream, List<Expression>? groupBy)
    {
        var names = new string[PartitionCount];
        var rowCounts = new long[PartitionCount];
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
                rowCounts[pIdx]++;
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
        return new PartitionResult(names, rowCounts);
    }

    private async Task<MultiSetPartitionResult> PartitionStreamMultiSet(IAsyncEnumerable<Row> stream, List<List<Expression>> sets)
    {
        var names = new string[PartitionCount];
        var writers = new ETL_SQL.Core.Spill.ISpillWriter[PartitionCount];
        // Per-(partition, set) row counts so the aggregate phase can skip empty (partition, set) pairs
        // — otherwise an empty per-set stream would trigger the in-memory engine's "no rows but
        // aggregates → emit a global row" fallback and produce spurious result rows.
        var setCounts = new long[PartitionCount][];
        for (int i = 0; i < PartitionCount; i++) setCounts[i] = new long[sets.Count];
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
                    setCounts[pIdx][sIdx]++;
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
        return new MultiSetPartitionResult(names, setCounts);
    }

    private sealed record MultiSetPartitionResult(string[] Names, long[][] SetCounts);



    /// <summary>Streams a spilled partition, yielding only the rows tagged with the given grouping-set index.</summary>
    private async IAsyncEnumerable<Row> ReadPartitionForSet(string name, int setIdx)
    {
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
        await foreach (var row in reader.AsEnumerableAsync())
        {
            if (Convert.ToInt32(row["__SET_IDX"] ?? 0) == setIdx)
                yield return row;
        }
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

