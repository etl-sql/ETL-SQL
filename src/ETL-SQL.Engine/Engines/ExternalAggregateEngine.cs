using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
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
    // Mirrors ExternalJoinEngine/ExternalDistinctEngine: cap recursive repartition depth so a
    // partition that cannot be split (e.g. a single huge group) falls back to the governor policy
    // instead of recursing forever.
    private const int MaxRecursivePartitionDepth = 8;
    private const int MaxFanOutSampleRows = 4096;
    private const long MaxFanOutSampleBytes = 16L * 1024 * 1024;

    private readonly IExecutionContext _context;
    private readonly ILogger _logger;
    private readonly AggregateEngine _inMemoryEngine;
    private readonly IBufferManager? _bufferManager;
    private int _partitionCount;
    public int PartitionCount => _partitionCount;
    internal long ColumnarAggregateRows { get; private set; }
    internal long ColumnarRepartitionRows { get; private set; }
    internal long ColumnarFilterInputRows { get; private set; }
    internal long ColumnarFilterOutputRows { get; private set; }


    public ExternalAggregateEngine(IExecutionContext context, ILogger logger)
    {
        _context = context;
        _logger = logger;
        _partitionCount = Math.Max(1, context.ExternalHashPartitions);
        _inMemoryEngine = new AggregateEngine(context, logger);
        _bufferManager = _context.ServiceProvider?.GetService<IBufferManager>();
    }

    /// <summary>Applies aggregation by partitioning the stream into disk files and processing each partition sequentially.</summary>
    public async IAsyncEnumerable<Row> ApplyAggregationExternal(IAsyncEnumerable<Row> inputStream, List<Expression>? groupBy, List<SelectColumn> finalColumns, List<string> colNames, Expression? havingClause = null, GroupingSetClause? groupingSet = null, long? knownRowCount = null, long? knownInputBytes = null)
    {
        using var cursor = _bufferManager != null ? await _bufferManager.AcquireCursorAsync(_context.SessionId ?? "DEFAULT", owner: this) : null;
        bool yieldedAny = false;
        string[]? partitionPaths = null;
        long[]? partitionRowCounts = null;
        long[][]? setCounts = null; // per-(partition, set) counts for the grouping-set path
        var tempFiles = new List<string>(); // sub-partition files created by governor repartitioning
        try
        {
            // 1. Partition Phase (supports one-pass expansion for grouping sets)
            List<List<Expression>>? expandedSets = null;

            if (groupingSet != null && groupingSet.Type != GroupingSetType.None)
            {
                expandedSets = _inMemoryEngine.ExpandGroupingSets(groupingSet);
                var multiSet = await PartitionStreamMultiSet(inputStream, expandedSets, knownRowCount, knownInputBytes);
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
                var partitioned = await PartitionStream(inputStream, groupBy, knownRowCount, knownInputBytes);
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

                    await foreach (var resRow in AggregatePartitionGoverned(
                        name, groupBy, finalColumns, colNames, havingClause, depth: 0, tempFiles))
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
            var allPartitionFiles = new List<string>(tempFiles);
            if (partitionPaths != null) allPartitionFiles.AddRange(partitionPaths);
            foreach (var path in allPartitionFiles)
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

    /// <summary>
    /// Aggregates one spilled partition under the RAM governor. The partition is built in memory with
    /// a heap-growth ceiling (<c>Engine:TotalMemoryGrantMB</c>); if that ceiling is breached the
    /// partition is recursively repartitioned (depth-salted hash, so identical group keys co-locate)
    /// until each sub-partition fits, or — when it cannot be split further — the governor policy
    /// applies (SpillOrFail throws; SpillOnly churns the build to completion ungoverned).
    /// </summary>
    private async IAsyncEnumerable<Row> AggregatePartitionGoverned(
        string name, List<Expression>? groupBy, List<SelectColumn> finalColumns,
        List<string> colNames, Expression? havingClause, int depth, List<string> tempFiles)
    {
        long ceiling = _context.MemoryArbiter?.TotalBudgetBytes ?? 0;

        var native = await TryAggregatePartitionColumnar(
            name, groupBy, finalColumns, colNames, havingClause);
        if (native != null)
        {
            _context.Telemetry.AggregateGroupsCount += native.Count;
            foreach (var row in native) yield return row;
            yield break;
        }

        // Governor off (no ceiling configured): original unbounded behavior.
        if (ceiling <= 0)
        {
            await using var reader = await _context.SpillStore.CreateReaderAsync(name);
            var plain = await _inMemoryEngine.ApplyAggregation(
                reader.AsEnumerableAsync(), groupBy, finalColumns, colNames, havingClause);
            _context.Telemetry.AggregateGroupsCount += plain.Count;
            foreach (var r in plain) yield return r;
            yield break;
        }

        List<Row>? results = null;
        try
        {
            await using var reader = await _context.SpillStore.CreateReaderAsync(name);
            results = await _inMemoryEngine.ApplyAggregation(
                reader.AsEnumerableAsync(), groupBy, finalColumns, colNames, havingClause, null, ceiling);
        }
        catch (AggregateMemoryPressureException)
        {
            // results stays null → repartition / policy below.
        }

        if (results != null)
        {
            _context.Telemetry.AggregateGroupsCount += results.Count;
            foreach (var r in results) yield return r;
            yield break;
        }

        // Under memory pressure. Try to split this partition further (only useful for multi-group
        // partitions; a single huge group routes entirely to one sub-partition and won't reduce).
        if (PartitionCount > 1 && depth < MaxRecursivePartitionDepth && groupBy != null && groupBy.Count > 0)
        {
            var sub = await RepartitionAggPartition(name, groupBy, depth + 1, tempFiles);
            var usedSub = sub.RowCounts.Count(c => c > 0);
            var largestSub = sub.RowCounts.Length == 0 ? 0 : sub.RowCounts.Max();
            var originalCount = sub.RowCounts.Sum();

            if (usedSub > 1 && largestSub < originalCount)
            {
                _logger.Debug("[MEMORY_GOVERNOR] Repartitioned aggregate partition at depth {Depth} into {Used} sub-partitions under memory pressure.", depth + 1, usedSub);
                for (var i = 0; i < sub.Names.Length; i++)
                {
                    if (sub.RowCounts[i] == 0) continue;
                    await foreach (var r in AggregatePartitionGoverned(sub.Names[i], groupBy, finalColumns, colNames, havingClause, depth + 1, tempFiles))
                        yield return r;
                }
                yield break;
            }
        }

        // Cannot reduce further → apply governor policy.
        if (_context.MemoryGovernorPolicy == MemoryGovernorPolicy.SpillOrFail)
        {
            throw new ExecutionException(
                "GROUP BY exceeded the memory governor ceiling (Engine:TotalMemoryGrantMB) and could not be reduced further by repartitioning. " +
                "Increase Engine:TotalMemoryGrantMB, reduce grouping cardinality, or set Engine:MemoryGovernorPolicy = SpillOnly to churn to completion.");
        }

        _logger.Warning("[MEMORY_GOVERNOR] Aggregate partition could not be reduced under the memory ceiling; churning to completion (MemoryGovernorPolicy=SpillOnly).");
        await using var churnReader = await _context.SpillStore.CreateReaderAsync(name);
        var churn = await _inMemoryEngine.ApplyAggregation(
            churnReader.AsEnumerableAsync(), groupBy, finalColumns, colNames, havingClause);
        _context.Telemetry.AggregateGroupsCount += churn.Count;
        foreach (var r in churn) yield return r;
    }

    private async Task<List<Row>?> TryAggregatePartitionColumnar(
        string name,
        List<Expression>? groupBy,
        List<SelectColumn> finalColumns,
        List<string> colNames,
        Expression? havingClause)
    {
        var statement = new SelectStatement(
            finalColumns, null, new TableReference("#spill"), new List<JoinClause>(), null,
            groupBy, havingClause);
        if (!ColumnarGroupedAggregatePlan.TryCreate(_context, statement, out var plan) || plan == null)
            return null;
        try
        {
            using (plan)
            await using (var reader = await _context.SpillStore.CreateReaderAsync(name))
            {
                if (reader is not IColumnarSpillReader columnarReader) return null;
                long nativeRows = 0;
                await foreach (var batch in columnarReader.AsColumnBatchesAsync())
                {
                    using (batch)
                    {
                        if (!plan.CanApply(batch)) return null;
                        plan.Accumulate(batch, selection: null);
                        nativeRows += batch.RowCount;
                    }
                }
                var rows = (await plan.FinalizeResultAsync(colNames)).Rows;
                ColumnarAggregateRows += nativeRows;
                return rows;
            }
        }
        catch (ExecutionException ex) when (
            ex.Message.StartsWith("Native ", StringComparison.Ordinal)
            && ex.Message.Contains("GROUP BY requires", StringComparison.Ordinal))
        {
            return null;
        }
    }

    /// <summary>
    /// Re-partitions an already-spilled aggregate partition into <see cref="PartitionCount"/> new
    /// sub-partitions using a depth-salted hash of the GROUP BY keys, so identical group keys still
    /// co-locate while different keys spread across sub-partitions.
    /// </summary>
    private async Task<PartitionResult> RepartitionAggPartition(string sourceName, List<Expression> groupBy, int depth, List<string> tempFiles)
    {
        if (!_context.SpillFormat.Equals("Json", StringComparison.OrdinalIgnoreCase)
            && groupBy.All(expression => expression is IdentifierExpression))
            return await RepartitionAggPartitionColumnar(sourceName, groupBy, depth, tempFiles);

        var names = new string[PartitionCount];
        var rowCounts = new long[PartitionCount];
        var writers = new ETL_SQL.Core.Spill.ISpillWriter[PartitionCount];
        var prefix = Guid.NewGuid().ToString("N");
        for (int i = 0; i < PartitionCount; i++)
        {
            names[i] = $"agg_d{depth}_{prefix}_{i}.tmp";
            tempFiles.Add(names[i]);
            writers[i] = await _context.SpillStore.CreateWriterAsync(names[i]);
        }

        try
        {
            await using var reader = await _context.SpillStore.CreateReaderAsync(sourceName);
            await foreach (var row in reader.AsEnumerableAsync())
            {
                var vals = new object?[groupBy.Count];
                for (int i = 0; i < groupBy.Count; i++)
                {
                    var rawVal = await _context.EvaluateValue(groupBy[i], row);
                    vals[i] = ETL_SQL.Data.CompoundKey.NormalizeValue(rawVal);
                }
                int pIdx = (new ETL_SQL.Data.CompoundKey(depth, vals).GetHashCode() & 0x7FFFFFFF) % PartitionCount;
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
            _context.Telemetry.PartitionPassCount++;
        }

        return new PartitionResult(names, rowCounts);
    }

    private sealed record PartitionResult(string[] Names, long[] RowCounts);

    private async Task<PartitionResult> PartitionStream(
        IAsyncEnumerable<Row> stream,
        List<Expression>? groupBy,
        long? knownRowCount,
        long? knownInputBytes)
    {
        await using var enumerator = stream.GetAsyncEnumerator(_context.CancellationToken);
        var sample = new List<Row>(MaxFanOutSampleRows);
        long sampledBytes = 0;
        while (sample.Count < MaxFanOutSampleRows
            && sampledBytes < MaxFanOutSampleBytes
            && await enumerator.MoveNextAsync())
        {
            var row = enumerator.Current;
            sample.Add(row);
            sampledBytes = checked(sampledBytes + row.EstimateHeapBytes());
        }
        await ConfigurePartitionCount(sample, groupBy, knownRowCount, knownInputBytes);

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
            await foreach (var row in ReplaySample(sample, enumerator))
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
            _context.Telemetry.PartitionPassCount++;
        }
        return new PartitionResult(names, rowCounts);
    }

    private async Task<PartitionResult> RepartitionAggPartitionColumnar(
        string sourceName,
        List<Expression> groupBy,
        int depth,
        List<string> tempFiles)
    {
        var names = new string[PartitionCount];
        var rowCounts = new long[PartitionCount];
        var writers = new ISpillWriter[PartitionCount];
        var prefix = Guid.NewGuid().ToString("N");
        for (var partition = 0; partition < PartitionCount; partition++)
        {
            names[partition] = $"agg_d{depth}_{prefix}_{partition}.tmp";
            tempFiles.Add(names[partition]);
            writers[partition] = await _context.SpillStore.CreateWriterAsync(names[partition]);
        }

        try
        {
            var keyNames = groupBy.Cast<IdentifierExpression>()
                .Select(identifier => identifier.Name.Split('.').Last()).ToArray();
            await using var reader = await _context.SpillStore.CreateReaderAsync(sourceName);
            await foreach (var batch in ((IColumnarSpillReader)reader).AsColumnBatchesAsync())
            {
                using (batch)
                {
                    var keyOrdinals = keyNames.Select(batch.Schema.GetOrdinal).ToArray();
                    var routes = Enumerable.Range(0, PartitionCount).Select(_ => new List<int>()).ToArray();
                    for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                    {
                        var values = new object?[keyOrdinals.Length];
                        for (var key = 0; key < values.Length; key++)
                            values[key] = RowPacker.ReadBatchValue(batch, keyOrdinals[key], rowIndex);
                        var compound = new CompoundKey(depth, values);
                        routes[(compound.GetHashCode() & 0x7fffffff) % PartitionCount].Add(rowIndex);
                    }
                    var columns = batch.Schema.Fields.Select(field => field.Name).ToArray();
                    for (var partition = 0; partition < routes.Length; partition++)
                    {
                        if (routes[partition].Count == 0) continue;
                        using var selection = SelectionVector.FromIndices(routes[partition]);
                        using var compacted = ColumnBatchAdapter.Compact(
                            batch, columns, selection, _context.CancellationToken);
                        await ((IColumnarSpillWriter)writers[partition]).WriteBatchAsync(compacted);
                        rowCounts[partition] += compacted.RowCount;
                        ColumnarRepartitionRows += compacted.RowCount;
                    }
                }
            }
        }
        finally
        {
            for (var partition = 0; partition < writers.Length; partition++)
                await writers[partition].DisposeAsync();
            _context.Telemetry.PartitionsCount += rowCounts.Count(count => count > 0);
            _context.Telemetry.PartitionPassCount++;
        }
        return new PartitionResult(names, rowCounts);
    }

    private async Task ConfigurePartitionCount(
        IReadOnlyList<Row> sample,
        List<Expression>? groupBy,
        long? knownRowCount,
        long? knownInputBytes)
    {
        if (sample.Count == 0 || groupBy == null || groupBy.Count == 0) return;
        long inputBytes = 0;
        long keyBytes = 0;
        var frequencies = new Dictionary<CompoundKey, int>();
        foreach (var row in sample)
        {
            inputBytes = checked(inputBytes + row.EstimateHeapBytes());
            var values = new object?[groupBy.Count];
            for (var i = 0; i < groupBy.Count; i++)
                values[i] = CompoundKey.NormalizeValue(await _context.EvaluateValue(groupBy[i], row));
            var key = new CompoundKey(values);
            keyBytes = checked(keyBytes + RowMemory.EstimateKeyBytes(key));
            frequencies[key] = frequencies.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var budget = MemoryGovernor.Ceiling(_context);
        if (budget <= 0) budget = Math.Max(1L, (long)_context.OperatorMemoryGrantMB * 1024 * 1024);
        var hotFraction = frequencies.Values.Max() / (double)sample.Count;
        var hasExactTotal = knownRowCount >= 0 && knownInputBytes >= 0;
        var plannedRows = hasExactTotal ? knownRowCount!.Value : sample.Count;
        var plannedBytes = hasExactTotal ? knownInputBytes!.Value : inputBytes;
        var estimatedDistinct = hasExactTotal
            ? Math.Min(plannedRows, (long)Math.Ceiling(frequencies.Count * (plannedRows / (double)sample.Count)))
            : frequencies.Count;
        var plan = HashPartitionSizing.Calculate(
            plannedBytes,
            plannedRows,
            (int)Math.Min(int.MaxValue, keyBytes / sample.Count),
            budget,
            estimatedDistinctKeys: (int)Math.Min(int.MaxValue, estimatedDistinct),
            largestKeyFraction: hotFraction,
            minimumPartitions: hasExactTotal ? 1 : _partitionCount,
            maximumPartitions: Math.Max(1024, _partitionCount));
        _partitionCount = hasExactTotal ? plan.PartitionCount : Math.Max(_partitionCount, plan.PartitionCount);
        _logger.Debug(
            "External aggregate sampled {SampleRows} rows ({SampleBytes} bytes) and selected fan-out {FanOut}; estimated passes={Passes}, hotKey={HotKey}.",
            sample.Count, inputBytes, _partitionCount, plan.EstimatedPartitionPasses, plan.HasUnsplittableHotKey);
    }

    private static async IAsyncEnumerable<Row> ReplaySample(
        IReadOnlyList<Row> sample,
        IAsyncEnumerator<Row> remainder)
    {
        foreach (var row in sample) yield return row;
        while (await remainder.MoveNextAsync()) yield return remainder.Current;
    }

    private async Task<MultiSetPartitionResult> PartitionStreamMultiSet(
        IAsyncEnumerable<Row> stream,
        List<List<Expression>> sets,
        long? knownRowCount,
        long? knownInputBytes)
    {
        await using var enumerator = stream.GetAsyncEnumerator(_context.CancellationToken);
        var sample = new List<Row>(MaxFanOutSampleRows);
        long sampledBytes = 0;
        while (sample.Count < MaxFanOutSampleRows
            && sampledBytes < MaxFanOutSampleBytes
            && await enumerator.MoveNextAsync())
        {
            var row = enumerator.Current;
            sample.Add(row);
            sampledBytes = checked(sampledBytes + row.EstimateHeapBytes());
        }
        await ConfigureGroupingSetPartitionCount(sample, sets, knownRowCount, knownInputBytes);

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
            await foreach (var row in ReplaySample(sample, enumerator))
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
            _context.Telemetry.PartitionPassCount++;
            if (totalInput > 0) _context.Telemetry.AggregateExpansionRatio = (double)totalExpanded / totalInput;
            _logger.Debug("[HYPER-SCALE] Expanded {Input} rows into {Expanded} intermediate rows for GroupingSets (Ratio: {Ratio:F2}).", totalInput, totalExpanded, _context.Telemetry.AggregateExpansionRatio);
        }
        return new MultiSetPartitionResult(names, setCounts);
    }

    private async Task ConfigureGroupingSetPartitionCount(
        IReadOnlyList<Row> sample,
        IReadOnlyList<List<Expression>> sets,
        long? knownRowCount,
        long? knownInputBytes)
    {
        if (sample.Count == 0 || sets.Count == 0) return;
        long inputBytes = 0;
        long keyBytes = 0;
        long expandedRows = 0;
        var frequencies = new Dictionary<CompoundKey, int>();
        foreach (var row in sample)
        {
            var rowBytes = row.EstimateHeapBytes();
            foreach (var (activeGroupBy, setIndex) in sets.Select((set, index) => (set, index)))
            {
                inputBytes = checked(inputBytes + rowBytes);
                expandedRows++;
                var values = new object?[activeGroupBy.Count];
                for (var i = 0; i < activeGroupBy.Count; i++)
                    values[i] = CompoundKey.NormalizeValue(await _context.EvaluateValue(activeGroupBy[i], row));
                var key = new CompoundKey(setIndex, values);
                keyBytes = checked(keyBytes + RowMemory.EstimateKeyBytes(key));
                frequencies[key] = frequencies.TryGetValue(key, out var count) ? count + 1 : 1;
            }
        }

        var budget = MemoryGovernor.Ceiling(_context);
        if (budget <= 0) budget = Math.Max(1L, (long)_context.OperatorMemoryGrantMB * 1024 * 1024);
        var hotFraction = frequencies.Values.Max() / (double)expandedRows;
        var hasExactTotal = knownRowCount >= 0 && knownInputBytes >= 0;
        var plannedRows = hasExactTotal ? checked(knownRowCount!.Value * sets.Count) : expandedRows;
        var plannedBytes = hasExactTotal ? checked(knownInputBytes!.Value * sets.Count) : inputBytes;
        var estimatedDistinct = hasExactTotal
            ? Math.Min(plannedRows, (long)Math.Ceiling(frequencies.Count * (plannedRows / (double)expandedRows)))
            : frequencies.Count;
        var plan = HashPartitionSizing.Calculate(
            plannedBytes,
            plannedRows,
            (int)Math.Min(int.MaxValue, keyBytes / expandedRows),
            budget,
            estimatedDistinctKeys: (int)Math.Min(int.MaxValue, estimatedDistinct),
            largestKeyFraction: hotFraction,
            minimumPartitions: hasExactTotal ? 1 : _partitionCount,
            maximumPartitions: Math.Max(1024, _partitionCount));
        _partitionCount = hasExactTotal ? plan.PartitionCount : Math.Max(_partitionCount, plan.PartitionCount);
        _logger.Debug(
            "External grouping sets sampled {SampleRows} input rows ({ExpandedRows} expanded) and selected fan-out {FanOut}; estimated passes={Passes}, hotKey={HotKey}.",
            sample.Count, expandedRows, _partitionCount, plan.EstimatedPartitionPasses, plan.HasUnsplittableHotKey);
    }

    private sealed record MultiSetPartitionResult(string[] Names, long[][] SetCounts);



    /// <summary>Streams a spilled partition, yielding only the rows tagged with the given grouping-set index.</summary>
    private async IAsyncEnumerable<Row> ReadPartitionForSet(string name, int setIdx)
    {
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
        if (reader is IColumnarSpillReader columnarReader)
        {
            await foreach (var batch in columnarReader.AsColumnBatchesAsync())
            {
                using (batch)
                {
                    var setColumn = batch.Schema.GetOrdinal("__SET_IDX");
                    for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                    {
                        ColumnarFilterInputRows++;
                        var value = RowPacker.ReadBatchValue(batch, setColumn, rowIndex);
                        if (Convert.ToInt32(value ?? 0) != setIdx) continue;
                        ColumnarFilterOutputRows++;
                        yield return RowPacker.MaterializeBatchRow(batch, rowIndex);
                    }
                }
            }
            yield break;
        }

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

