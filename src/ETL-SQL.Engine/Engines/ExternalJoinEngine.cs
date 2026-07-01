using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Engines;
/// <summary>
/// Implements disk-spilling hash joins for large datasets that exceed memory capacity.
/// </summary>
public class ExternalJoinEngine
{
    private const int MaxRecursivePartitionDepth = 8;
    private const int MaxFanOutSampleRows = 4096;
    private const long MaxFanOutSampleBytes = 16L * 1024 * 1024;

    private readonly IExecutionContext _context;
    private readonly ILogger _logger;
    private int _partitionCount;
    public int PartitionCount => _partitionCount;
    internal long ColumnarBuildRows { get; private set; }
    internal long ColumnarProbeRows { get; private set; }
    internal long ColumnarRepartitionRows { get; private set; }


    private readonly IBufferManager? _bufferManager;

    public ExternalJoinEngine(IExecutionContext context, ILogger logger)
    {
        _context = context;
        _logger = logger;
        _partitionCount = Math.Max(1, _context.ExternalHashPartitions);
        _bufferManager = _context.ServiceProvider?.GetService<IBufferManager>();
    }

    /// <summary>Performs an external hash join by partitioning both left and right streams to disk before join processing.</summary>
    public async IAsyncEnumerable<Row> ApplyHashJoinExternal(IAsyncEnumerable<Row> leftStream, IAsyncEnumerable<Row> rightStream, JoinClause join, List<string> leftKeys, List<string> rightKeys, long? knownBuildRowCount = null, long? knownBuildBytes = null)
    {
        using var cursor = _bufferManager != null ? await _bufferManager.AcquireCursorAsync(_context.SessionId ?? "DEFAULT", owner: this) : null;
        var tempFiles = new List<string>();
        try
        {
            await using var rightEnumerator = rightStream.GetAsyncEnumerator(_context.CancellationToken);
            var rightSample = new List<Row>(MaxFanOutSampleRows);
            long sampledBytes = 0;
            while (rightSample.Count < MaxFanOutSampleRows
                && sampledBytes < MaxFanOutSampleBytes
                && await rightEnumerator.MoveNextAsync())
            {
                var row = rightEnumerator.Current;
                rightSample.Add(row);
                sampledBytes = checked(sampledBytes + row.EstimateHeapBytes());
            }
            ConfigurePartitionCount(rightSample, rightKeys, knownBuildRowCount, knownBuildBytes);

            // 1. Partition Phase
            var leftPartitions = await PartitionStream(leftStream, leftKeys, "left", tempFiles);
            var rightPartitions = await PartitionStream(
                ReplaySample(rightSample, rightEnumerator), rightKeys, "right", tempFiles);

            // 2. Join Phase (one partition at a time)
            for (int i = 0; i < PartitionCount; i++)
            {
                await foreach (var row in JoinPartition(
                    leftPartitions.Names[i],
                    rightPartitions.Names[i],
                    leftPartitions.Counts[i],
                    rightPartitions.Counts[i],
                    join,
                    leftKeys,
                    rightKeys,
                    depth: 0,
                    tempFiles))
                {
                    yield return row;
                }
            }
        }
        finally
        {
            foreach (var path in tempFiles)
            {
                try
                {
                    _context.SpillStore.DeleteChunk(path);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error cleaning up external join chunk {path}: {ex.Message}");
                }
            }
        }
    }

    private void ConfigurePartitionCount(
        IReadOnlyList<Row> sample,
        IReadOnlyList<string> keys,
        long? knownBuildRowCount,
        long? knownBuildBytes)
    {
        if (sample.Count == 0) return;
        long inputBytes = 0;
        long keyBytes = 0;
        var frequencies = new Dictionary<CompoundKey, int>();
        foreach (var row in sample)
        {
            inputBytes = checked(inputBytes + row.EstimateHeapBytes());
            var key = GetHashKey(row, keys.ToList());
            keyBytes = checked(keyBytes + RowMemory.EstimateKeyBytes(key));
            frequencies[key] = frequencies.TryGetValue(key, out var count) ? count + 1 : 1;
        }
        var budget = MemoryGovernor.Ceiling(_context);
        if (budget <= 0) budget = Math.Max(1L, (long)_context.OperatorMemoryGrantMB * 1024 * 1024);
        var hotFraction = frequencies.Count == 0 ? 0 : frequencies.Values.Max() / (double)sample.Count;
        var hasExactTotal = knownBuildRowCount >= 0 && knownBuildBytes >= 0;
        var plannedRows = hasExactTotal ? knownBuildRowCount!.Value : sample.Count;
        var plannedBytes = hasExactTotal ? knownBuildBytes!.Value : inputBytes;
        var estimatedDistinct = hasExactTotal
            ? Math.Min(plannedRows, (long)Math.Ceiling(frequencies.Count * (plannedRows / (double)sample.Count)))
            : frequencies.Count;
        var plan = HashPartitionSizing.Calculate(
            plannedBytes,
            plannedRows,
            (int)Math.Min(int.MaxValue, Math.Max(0, keyBytes / sample.Count)),
            budget,
            estimatedDistinctKeys: (int)Math.Min(int.MaxValue, estimatedDistinct),
            largestKeyFraction: hotFraction,
            minimumPartitions: hasExactTotal ? 1 : Math.Max(1, _partitionCount),
            maximumPartitions: Math.Max(1024, _partitionCount));
        _partitionCount = hasExactTotal ? plan.PartitionCount : Math.Max(_partitionCount, plan.PartitionCount);
        _logger.Debug(
            "External join sampled {SampleRows} build rows ({SampleBytes} bytes) and selected fan-out {FanOut}; estimated passes={Passes}, hotKey={HotKey}.",
            sample.Count, inputBytes, _partitionCount, plan.EstimatedPartitionPasses, plan.HasUnsplittableHotKey);
    }

    private static async IAsyncEnumerable<Row> ReplaySample(
        IReadOnlyList<Row> sample,
        IAsyncEnumerator<Row> remainder)
    {
        foreach (var row in sample) yield return row;
        while (await remainder.MoveNextAsync()) yield return remainder.Current;
    }

    private async IAsyncEnumerable<Row> JoinPartition(
        string leftName,
        string rightName,
        long leftRowCount,
        long rightRowCount,
        JoinClause join,
        List<string> leftKeys,
        List<string> rightKeys,
        int depth,
        List<string> tempFiles)
    {
        // Row-count-driven repartition (grace hash join): split an oversized build side up front.
        if (ShouldRepartition(rightRowCount, depth))
        {
            var split = await TryRepartitionBothSides(leftName, rightName, leftKeys, rightKeys, rightRowCount, depth + 1, tempFiles);
            if (split != null)
            {
                await foreach (var row in JoinSubPartitions(split.Value.Left, split.Value.Right, join, leftKeys, rightKeys, depth + 1, tempFiles))
                    yield return row;
                yield break;
            }

            _logger.Debug(
                "External join partition at depth {Depth} could not be reduced further by row count. Falling back to a memory-guarded direct join for {RightRows} right rows.",
                depth,
                rightRowCount);
        }

        // Memory-guarded direct join. The (right) build side is read fully before any probe row is
        // emitted, so if heap growth crosses the governor ceiling mid-build (e.g. wide rows under the
        // row threshold) we can repartition or apply policy without having yielded anything.
        long ceiling = MemoryGovernor.Ceiling(_context);
        var hashTable = await BuildJoinHashTable(rightName, rightKeys, new MemoryBudgetGuard(ceiling));

        if (hashTable == null)
        {
            if (depth < MaxRecursivePartitionDepth && PartitionCount > 1)
            {
                var split = await TryRepartitionBothSides(leftName, rightName, leftKeys, rightKeys, rightRowCount, depth + 1, tempFiles);
                if (split != null)
                {
                    await foreach (var row in JoinSubPartitions(split.Value.Left, split.Value.Right, join, leftKeys, rightKeys, depth + 1, tempFiles))
                        yield return row;
                    yield break;
                }
            }

            MemoryGovernor.EnforcePolicy(_context,
                "JOIN build side exceeded the memory governor ceiling (Engine:TotalMemoryGrantMB) and could not be " +
                "reduced further by repartitioning. Increase the ceiling, reduce join-key skew, or set " +
                "Engine:MemoryGovernorPolicy = SpillOnly to churn to completion.");

            // SpillOnly churn: rebuild unguarded and continue.
            hashTable = await BuildJoinHashTable(rightName, rightKeys, new MemoryBudgetGuard(0));
        }

        await foreach (var row in ProbeJoin(leftName, hashTable!, join, leftKeys))
            yield return row;
    }

    /// <summary>Recurses into the per-partition joins produced by a repartition step.</summary>
    private async IAsyncEnumerable<Row> JoinSubPartitions(PartitionSet left, PartitionSet right, JoinClause join, List<string> leftKeys, List<string> rightKeys, int depth, List<string> tempFiles)
    {
        for (int i = 0; i < PartitionCount; i++)
        {
            await foreach (var row in JoinPartition(left.Names[i], right.Names[i], left.Counts[i], right.Counts[i], join, leftKeys, rightKeys, depth, tempFiles))
                yield return row;
        }
    }

    /// <summary>
    /// Repartitions both sides at the given depth (depth-salted hash). Returns the pair only if the
    /// build side actually split (more than one used partition, all smaller than the original) so the
    /// caller can recurse; returns null when recursion can't help (e.g. severe key skew).
    /// </summary>
    private async Task<(PartitionSet Left, PartitionSet Right)?> TryRepartitionBothSides(
        string leftName, string rightName, List<string> leftKeys, List<string> rightKeys,
        long rightRowCount, int nextDepth, List<string> tempFiles)
    {
        var left = await RepartitionPartition(leftName, leftKeys, $"left_d{nextDepth}", nextDepth, tempFiles);
        var right = await RepartitionPartition(rightName, rightKeys, $"right_d{nextDepth}", nextDepth, tempFiles);
        var largestRight = right.Counts.Length == 0 ? 0 : right.Counts.Max();
        var usedRight = right.Counts.Count(c => c > 0);
        if (usedRight > 1 && largestRight < rightRowCount)
        {
            _logger.Debug("Recursively repartitioned external join partition at depth {Depth}. largestRight={LargestRight}", nextDepth, largestRight);
            return (left, right);
        }
        return null;
    }

    private bool ShouldRepartition(long rightRowCount, int depth)
    {
        return PartitionCount > 1
            && depth < MaxRecursivePartitionDepth
            && rightRowCount > Math.Max(1, _context.JoinSpillThreshold);
    }

    /// <summary>
    /// Builds the hash table from the (right) build side, holding each build row as a compact packed
    /// <c>byte[]</c> (see <see cref="RowPacker"/>) rather than a fat <see cref="Row"/> object graph —
    /// the dominant memory cost of a large hash-join build. Rows are decoded back to a <see cref="Row"/>
    /// only on a probe-key match. Returns null if the accumulated build footprint (precise byte
    /// accounting, using the exact blob lengths) crosses the governor ceiling mid-build, so the caller
    /// can repartition or apply policy before any probe row has been emitted.
    /// </summary>
    private async Task<PackedBuildTable?> BuildJoinHashTable(string rightName, List<string> rightKeys, MemoryBudgetGuard guard)
    {
        var table = new PackedBuildTable();
        var packer = new RowPacker();
        bool columnsCaptured = false;
        await using var rightReader = await _context.SpillStore.CreateReaderAsync(rightName);
        if (rightReader is IColumnarSpillReader columnarReader)
        {
            await foreach (var batch in columnarReader.AsColumnBatchesAsync())
            {
                using (batch)
                {
                    if (!columnsCaptured)
                    {
                        table.Columns.AddRange(batch.Schema.Fields.Select(field => field.Name));
                        columnsCaptured = true;
                    }
                    for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                    {
                        var keyValues = new object?[rightKeys.Count];
                        for (var keyIndex = 0; keyIndex < rightKeys.Count; keyIndex++)
                        {
                            var columnIndex = batch.Schema.GetOrdinal(rightKeys[keyIndex]);
                            keyValues[keyIndex] = RowPacker.ReadBatchValue(batch, columnIndex, rowIndex);
                        }
                        var key = new CompoundKey(keyValues);
                        var blob = packer.Pack(batch, rowIndex);
                        AddBuildRow(table, key, blob, guard);
                        ColumnarBuildRows++;
                        if (guard.Exceeded()) return null;
                    }
                }
            }
            return table;
        }

        await foreach (var rightRow in rightReader.AsEnumerableAsync())
        {
            // The build side has a uniform schema; capture the column order once (matches how the
            // Arrow spill writer infers its schema from the first row).
            if (!columnsCaptured)
            {
                table.Columns.AddRange(rightRow.GetColumnNames());
                columnsCaptured = true;
            }

            var key = GetHashKey(rightRow, rightKeys);
            var blob = packer.Pack(rightRow, table.Columns);
            AddBuildRow(table, key, blob, guard);
            if (guard.Exceeded()) return null;
        }
        return table;
    }

    private static void AddBuildRow(
        PackedBuildTable table,
        CompoundKey key,
        byte[] blob,
        MemoryBudgetGuard guard)
    {
        int idx = table.Rows.Count;
        table.Rows.Add(blob);
        if (!table.Index.TryGetValue(key, out var bucket))
        {
            bucket = new List<int>();
            table.Index[key] = bucket;
            guard.Add(RowMemory.EstimateKeyBytes(key) + 48);
        }
        bucket.Add(idx);
        guard.Add(blob.Length + 24);
    }

    /// <summary>Streams the probe (left) side against a packed build table, emitting join results.</summary>
    private async IAsyncEnumerable<Row> ProbeJoin(string leftName, PackedBuildTable table, JoinClause join, List<string> leftKeys)
    {
        bool isLeftJoin = join.JoinType.Contains("LEFT", StringComparison.OrdinalIgnoreCase) || join.JoinType.Contains("FULL", StringComparison.OrdinalIgnoreCase);
        bool isRightJoin = join.JoinType.Contains("RIGHT", StringComparison.OrdinalIgnoreCase) || join.JoinType.Contains("FULL", StringComparison.OrdinalIgnoreCase);
        // Track matched build rows by physical index (one bit per packed blob) for RIGHT/FULL outer.
        var matched = isRightJoin ? new bool[table.Rows.Count] : null;

        await using var leftReader = await _context.SpillStore.CreateReaderAsync(leftName);
        if (leftReader is IColumnarSpillReader columnarReader)
        {
            await foreach (var batch in columnarReader.AsColumnBatchesAsync())
            {
                using (batch)
                {
                    for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                    {
                        ColumnarProbeRows++;
                        var key = GetHashKey(batch, rowIndex, leftKeys);
                        bool producedMatch = false;
                        Row? left = null;
                        if (table.Index.TryGetValue(key, out var matches))
                        {
                            left = RowPacker.MaterializeBatchRow(batch, rowIndex);
                            foreach (var idx in matches)
                            {
                                var right = RowPacker.Unpack(table.Rows[idx], table.Columns);
                                var combined = CombineRows(left, right);
                                if (await _context.EvaluateCondition(join.Condition, combined))
                                {
                                    yield return combined;
                                    producedMatch = true;
                                    if (matched != null) matched[idx] = true;
                                }
                            }
                        }
                        if (!producedMatch && isLeftJoin)
                            yield return (left ?? RowPacker.MaterializeBatchRow(batch, rowIndex)).Clone();
                    }
                }
            }
        }
        else
        {
            await foreach (var left in leftReader.AsEnumerableAsync())
            {
                var key = GetHashKey(left, leftKeys);
                bool producedMatch = false;

                if (table.Index.TryGetValue(key, out var matches))
                {
                    foreach (var idx in matches)
                    {
                        var right = RowPacker.Unpack(table.Rows[idx], table.Columns);
                        var combined = CombineRows(left, right);
                        if (await _context.EvaluateCondition(join.Condition, combined))
                        {
                            yield return combined;
                            producedMatch = true;
                            if (matched != null) matched[idx] = true;
                        }
                    }
                }

                if (!producedMatch && isLeftJoin)
                    yield return left.Clone();
            }
        }

        if (isRightJoin && matched != null)
        {
            for (int idx = 0; idx < table.Rows.Count; idx++)
            {
                if (!matched[idx])
                    yield return CombineRows(null, RowPacker.Unpack(table.Rows[idx], table.Columns));
            }
        }
    }

    private async Task<PartitionSet> PartitionStream(IAsyncEnumerable<Row> stream, List<string> keys, string prefix, List<string> tempFiles)
    {
        return await PartitionStream(stream, keys, prefix, depth: 0, tempFiles);
    }

    private async Task<PartitionSet> RepartitionPartition(string sourceName, List<string> keys, string prefix, int depth, List<string> tempFiles)
    {
        if (!_context.SpillFormat.Equals("Json", StringComparison.OrdinalIgnoreCase))
            return await RepartitionPartitionColumnar(sourceName, keys, prefix, depth, tempFiles);
        return await PartitionStream(ReadPartitionStream(sourceName), keys, prefix, depth, tempFiles);
    }

    private async Task<PartitionSet> RepartitionPartitionColumnar(
        string sourceName,
        List<string> keys,
        string prefix,
        int depth,
        List<string> tempFiles)
    {
        var names = new string[PartitionCount];
        var counts = new long[PartitionCount];
        var writers = new ISpillWriter[PartitionCount];
        var uniquePrefix = Guid.NewGuid().ToString("N");
        for (var i = 0; i < PartitionCount; i++)
        {
            names[i] = $"{uniquePrefix}_{prefix}_{i}.tmp";
            tempFiles.Add(names[i]);
            writers[i] = await _context.SpillStore.CreateWriterAsync(names[i]);
        }

        try
        {
            await using var reader = await _context.SpillStore.CreateReaderAsync(sourceName);
            var columnarReader = (IColumnarSpillReader)reader;
            await foreach (var batch in columnarReader.AsColumnBatchesAsync())
            {
                using (batch)
                {
                    var routes = Enumerable.Range(0, PartitionCount).Select(_ => new List<int>()).ToArray();
                    for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                    {
                        var key = GetPartitionHashKey(batch, rowIndex, keys, depth);
                        routes[(key.GetHashCode() & 0x7fffffff) % PartitionCount].Add(rowIndex);
                    }
                    var columns = batch.Schema.Fields.Select(field => field.Name).ToArray();
                    for (var partition = 0; partition < routes.Length; partition++)
                    {
                        if (routes[partition].Count == 0) continue;
                        using var selection = SelectionVector.FromIndices(routes[partition]);
                        using var compacted = ColumnBatchAdapter.Compact(
                            batch, columns, selection, _context.CancellationToken);
                        await ((IColumnarSpillWriter)writers[partition]).WriteBatchAsync(compacted);
                        counts[partition] += compacted.RowCount;
                        ColumnarRepartitionRows += compacted.RowCount;
                    }
                }
            }
        }
        finally
        {
            var usedPartitions = 0;
            for (var i = 0; i < writers.Length; i++)
            {
                if (counts[i] > 0) usedPartitions++;
                await writers[i].DisposeAsync();
            }
            _context.Telemetry.PartitionsCount += usedPartitions;
            _context.Telemetry.PartitionPassCount++;
        }
        return new PartitionSet(names, counts);
    }

    private async Task<PartitionSet> PartitionStream(IAsyncEnumerable<Row> stream, List<string> keys, string prefix, int depth, List<string> tempFiles)
    {
        var names = new string[PartitionCount];
        var counts = new long[PartitionCount];
        var writers = new ETL_SQL.Core.Spill.ISpillWriter[PartitionCount];

        var uniquePrefix = Guid.NewGuid().ToString("N");
        for (int i = 0; i < PartitionCount; i++)
        {
            names[i] = $"{uniquePrefix}_{prefix}_{i}.tmp";
            tempFiles.Add(names[i]);
            writers[i] = await _context.SpillStore.CreateWriterAsync(names[i]);
        }

        try
        {
            await foreach (var row in stream)
            {
                int pIdx = GetPartitionIndex(row, keys, depth);
                counts[pIdx]++;
                await writers[pIdx].WriteRowAsync(row);
            }
        }
        finally
        {
            int usedPartitions = 0;
            for (int i = 0; i < writers.Length; i++)
            {
                if (writers[i] != null)
                {
                    if (counts[i] > 0) usedPartitions++;
                    await writers[i].DisposeAsync();
                }
            }

            _context.Telemetry.PartitionsCount += usedPartitions;
            _context.Telemetry.PartitionPassCount++;
            _logger.Debug("Finished partitioning {Prefix}. Used {UsedPartitions} partitions. Context PartitionsCount: {PartitionsCount}", prefix, usedPartitions, _context.Telemetry.PartitionsCount);
        }

        return new PartitionSet(names, counts);
    }

    private async IAsyncEnumerable<Row> ReadPartitionStream(string name)
    {
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
        await foreach (var row in reader.AsEnumerableAsync())
            yield return row;
    }

    private int GetPartitionIndex(Row row, List<string> keys, int depth)
    {
        var key = depth == 0 ? GetHashKey(row, keys) : GetPartitionHashKey(row, keys, depth);
        return (key.GetHashCode() & 0x7FFFFFFF) % PartitionCount;
    }

    private CompoundKey GetHashKey(Row row, List<string> keys)
    {
        var values = new object?[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            values[i] = SpillSerializationHelper.UnwrapValue(row[keys[i]]);
        return new CompoundKey(values);
    }

    private static CompoundKey GetHashKey(ColumnBatch batch, int rowIndex, List<string> keys)
    {
        var values = new object?[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            var column = batch.Schema.GetOrdinal(keys[i]);
            values[i] = RowPacker.ReadBatchValue(batch, column, rowIndex);
        }
        return new CompoundKey(values);
    }

    private CompoundKey GetPartitionHashKey(Row row, List<string> keys, int depth)
    {
        var values = new object?[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            values[i] = SpillSerializationHelper.UnwrapValue(row[keys[i]]);
        return new CompoundKey(depth, values);
    }

    private static CompoundKey GetPartitionHashKey(
        ColumnBatch batch,
        int rowIndex,
        List<string> keys,
        int depth)
    {
        var values = new object?[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            var column = batch.Schema.GetOrdinal(keys[i]);
            values[i] = RowPacker.ReadBatchValue(batch, column, rowIndex);
        }
        return new CompoundKey(depth, values);
    }

    private Row CombineRows(Row? left, Row right)
    {
        var combined = new Row();
        left?.ForEachColumn((name, value) => combined[name] = value);
        right.ForEachColumn((name, value) => combined[name] = value);
        return combined;
    }

    private readonly record struct PartitionSet(string[] Names, long[] Counts);

    /// <summary>
    /// The probe-side hash table: build rows held as compact packed blobs (<see cref="Rows"/>) with a
    /// key index (<see cref="Index"/>) mapping each join key to the physical row indices that carry it.
    /// <see cref="Columns"/> is the shared column order captured from the first build row, used to
    /// decode a blob back into a <see cref="Row"/> on a probe match.
    /// </summary>
    private sealed class PackedBuildTable
    {
        public readonly List<string> Columns = new();
        public readonly List<byte[]> Rows = new();
        public readonly Dictionary<CompoundKey, List<int>> Index = new();
    }
}

