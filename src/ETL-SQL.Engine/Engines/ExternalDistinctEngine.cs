using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

/// <summary>Hybrid projected-row DISTINCT with bounded, recursively-repartitioned hash state.</summary>
internal sealed class ExternalDistinctEngine
{
    // Matches ExternalJoinEngine: cap recursion so a pathological partition that never
    // splits (e.g. one over-represented distinct value) eventually falls back to a direct
    // in-memory dedup rather than recursing forever.
    private const int MaxRecursivePartitionDepth = 8;
    private const int MaxFanOutSampleRows = 4096;
    private const long MaxFanOutSampleBytes = 16L * 1024 * 1024;

    private readonly IExecutionContext _context;
    private int _partitionCount;

    public ExternalDistinctEngine(IExecutionContext context)
    {
        _context = context;
        _partitionCount = Math.Max(1, context.ExternalHashPartitions);
    }

    internal int PartitionCount => _partitionCount;
    internal long ColumnarBuildRows { get; private set; }

    public async IAsyncEnumerable<Row> ApplyAsync(IAsyncEnumerable<Row> source)
    {
        var threshold = Math.Max(1, _context.JoinSpillThreshold);
        var prefix = new List<Row>(Math.Min(threshold, 4096));
        await using var enumerator = source.GetAsyncEnumerator();
        while (prefix.Count < threshold && await enumerator.MoveNextAsync())
            prefix.Add(enumerator.Current);

        if (prefix.Count < threshold)
        {
            var seen = new HashSet<CompoundKey>();
            foreach (var row in prefix)
                if (seen.Add(Key(row))) yield return row;
            yield break;
        }

        ConfigurePartitionCount(prefix);

        var tempFiles = new List<string>();
        try
        {
            // Concatenate the buffered prefix back onto the remaining stream and partition once.
            var partitions = await PartitionAsync(Concat(prefix, enumerator), depth: 0, tempFiles);

            for (var i = 0; i < partitions.Names.Length; i++)
            {
                await foreach (var row in ProcessPartitionAsync(partitions.Names[i], partitions.Counts[i], depth: 0, tempFiles))
                    yield return row;
            }
        }
        finally
        {
            foreach (var name in tempFiles)
            {
                try { _context.SpillStore.DeleteChunk(name); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    private void ConfigurePartitionCount(IReadOnlyList<Row> prefix)
    {
        long inputBytes = 0;
        long keyBytes = 0;
        var frequencies = new Dictionary<CompoundKey, int>();
        var sampledRows = 0;
        foreach (var row in prefix)
        {
            if (sampledRows >= MaxFanOutSampleRows || inputBytes >= MaxFanOutSampleBytes) break;
            var key = Key(row);
            inputBytes = checked(inputBytes + row.EstimateHeapBytes());
            keyBytes = checked(keyBytes + RowMemory.EstimateKeyBytes(key));
            frequencies[key] = frequencies.TryGetValue(key, out var count) ? count + 1 : 1;
            sampledRows++;
        }
        if (sampledRows == 0) return;

        var budget = MemoryGovernor.Ceiling(_context);
        if (budget <= 0) budget = Math.Max(1L, (long)_context.OperatorMemoryGrantMB * 1024 * 1024);
        var hotFraction = frequencies.Values.Max() / (double)sampledRows;
        var plan = HashPartitionSizing.Calculate(
            inputBytes,
            sampledRows,
            (int)Math.Min(int.MaxValue, keyBytes / sampledRows),
            budget,
            estimatedDistinctKeys: frequencies.Count,
            largestKeyFraction: hotFraction,
            minimumPartitions: _partitionCount,
            maximumPartitions: Math.Max(1024, _partitionCount));
        _partitionCount = Math.Max(_partitionCount, plan.PartitionCount);
    }

    /// <summary>
    /// Deduplicates a single spilled partition. If the partition is small enough it is deduped
    /// in memory; otherwise it is recursively repartitioned with a depth-salted hash so each
    /// sub-partition's in-memory distinct set stays bounded (mirrors the external hash join).
    /// </summary>
    private async IAsyncEnumerable<Row> ProcessPartitionAsync(string name, long rowCount, int depth, List<string> tempFiles)
    {
        var threshold = Math.Max(1, _context.JoinSpillThreshold);
        long ceiling = MemoryGovernor.Ceiling(_context);
        bool canRepartition = PartitionCount > 1 && depth < MaxRecursivePartitionDepth;

        // Clearly large by row count → split up front (avoids building a huge set to then discard it).
        if (rowCount > threshold && canRepartition)
        {
            var sub = await TryRepartition(name, depth + 1, rowCount, tempFiles);
            if (sub != null)
            {
                await foreach (var row in RecurseSubPartitions(sub.Value, depth + 1, tempFiles))
                    yield return row;
                yield break;
            }
        }

        // Governor off → original streaming dedup (no extra buffering).
        if (ceiling <= 0)
        {
            await foreach (var row in DedupInMemory(name))
                yield return row;
            yield break;
        }

        // Governor on → build with a memory backstop so wide rows under the row threshold still
        // can't blow the ceiling. On pressure (null), try to repartition; if that can't help,
        // apply the governor policy.
        var built = await DedupToList(name, ceiling);
        if (built != null)
        {
            foreach (var blob in built.Blobs) yield return RowPacker.Unpack(blob, built.Columns);
            yield break;
        }

        if (canRepartition)
        {
            var sub = await TryRepartition(name, depth + 1, rowCount, tempFiles);
            if (sub != null)
            {
                await foreach (var row in RecurseSubPartitions(sub.Value, depth + 1, tempFiles))
                    yield return row;
                yield break;
            }
        }

        MemoryGovernor.EnforcePolicy(_context,
            "DISTINCT exceeded the memory governor ceiling (Engine:TotalMemoryGrantMB) and could not be " +
            "reduced further by repartitioning. Increase the ceiling, reduce cardinality, or set " +
            "Engine:MemoryGovernorPolicy = SpillOnly to churn to completion.");

        // SpillOnly churn: stream-dedup without the guard.
        await foreach (var row in DedupInMemory(name))
            yield return row;
    }

    private async IAsyncEnumerable<Row> RecurseSubPartitions(PartitionSet sub, int depth, List<string> tempFiles)
    {
        for (var i = 0; i < sub.Names.Length; i++)
        {
            if (sub.Counts[i] == 0) continue;
            await foreach (var row in ProcessPartitionAsync(sub.Names[i], sub.Counts[i], depth, tempFiles))
                yield return row;
        }
    }

    /// <summary>Repartitions a partition; returns the sub-set only if it actually split (else null).</summary>
    private async Task<PartitionSet?> TryRepartition(string name, int depth, long originalRowCount, List<string> tempFiles)
    {
        var sub = await PartitionAsync(ReadPartition(name), depth, tempFiles);
        var used = sub.Counts.Count(c => c > 0);
        var largest = sub.Counts.Length == 0 ? 0 : sub.Counts.Max();
        // No split (e.g. every row shares the same key) → caller falls back to in-memory/policy.
        if (used <= 1 || largest >= originalRowCount) return null;
        return sub;
    }

    /// <summary>
    /// Builds the distinct rows of a partition, holding each retained row as a compact packed
    /// <c>byte[]</c> (see <see cref="RowPacker"/>) rather than a fat <see cref="Row"/> object graph; a
    /// blob is decoded back to a <see cref="Row"/> only when it is yielded. Returns null if the
    /// accumulated build footprint (precise byte accounting, using the exact blob lengths) crosses the
    /// governor ceiling so the caller can repartition or apply policy. Only retained (newly distinct)
    /// rows count against the budget — a duplicate adds nothing to the in-memory set. The duplicate
    /// check still needs the <see cref="CompoundKey"/> in <c>seen</c>; only the output rows are packed.
    /// </summary>
    private async Task<PackedRows?> DedupToList(string name, long ceiling)
    {
        var seen = new HashSet<CompoundKey>();
        var packed = new PackedRows();
        var packer = new RowPacker();
        bool columnsCaptured = false;
        var guard = new MemoryBudgetGuard(ceiling);
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
        if (reader is IColumnarSpillReader columnarReader)
        {
            await foreach (var batch in columnarReader.AsColumnBatchesAsync())
            {
                using (batch)
                {
                    if (!columnsCaptured)
                    {
                        packed.Columns.AddRange(batch.Schema.Fields.Select(field => field.Name));
                        columnsCaptured = true;
                    }
                    for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                    {
                        ColumnarBuildRows++;
                        var key = Key(batch, rowIndex);
                        if (!seen.Add(key)) continue;
                        var blob = packer.Pack(batch, rowIndex);
                        packed.Blobs.Add(blob);
                        guard.Add(blob.Length + 24 + RowMemory.EstimateKeyBytes(key));
                        if (guard.Exceeded()) return null;
                    }
                }
            }
            return packed;
        }

        await foreach (var row in reader.AsEnumerableAsync())
        {
            var key = Key(row);
            if (seen.Add(key))
            {
                // Uniform partition schema → capture the column order once (matches the join build).
                if (!columnsCaptured)
                {
                    packed.Columns.AddRange(row.GetColumnNames());
                    columnsCaptured = true;
                }
                var blob = packer.Pack(row, packed.Columns);
                packed.Blobs.Add(blob);
                guard.Add(blob.Length + 24 + RowMemory.EstimateKeyBytes(key)); // blob + seen-set key
                if (guard.Exceeded()) return null;
            }
        }
        return packed;
    }

    private async IAsyncEnumerable<Row> DedupInMemory(string name)
    {
        var seen = new HashSet<CompoundKey>();
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
        if (reader is IColumnarSpillReader columnarReader)
        {
            await foreach (var batch in columnarReader.AsColumnBatchesAsync())
            {
                using (batch)
                {
                    for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                    {
                        ColumnarBuildRows++;
                        if (seen.Add(Key(batch, rowIndex)))
                            yield return RowPacker.MaterializeBatchRow(batch, rowIndex);
                    }
                }
            }
            yield break;
        }

        await foreach (var row in reader.AsEnumerableAsync())
            if (seen.Add(Key(row))) yield return row;
    }

    private async Task<PartitionSet> PartitionAsync(IAsyncEnumerable<Row> source, int depth, List<string> tempFiles)
    {
        var partitionCount = PartitionCount;
        var names = new string[partitionCount];
        var counts = new long[partitionCount];
        var writers = new ISpillWriter[partitionCount];
        var operationId = Guid.NewGuid().ToString("N");
        try
        {
            for (var i = 0; i < partitionCount; i++)
            {
                names[i] = $"distinct_d{depth}_{operationId}_{i}.tmp";
                tempFiles.Add(names[i]);
                writers[i] = await _context.SpillStore.CreateWriterAsync(names[i]);
            }

            await foreach (var row in source)
            {
                var partition = (RouteKey(row, depth).GetHashCode() & 0x7fffffff) % partitionCount;
                counts[partition]++;
                await writers[partition].WriteRowAsync(row);
            }
        }
        finally
        {
            var used = 0;
            foreach (var writer in writers)
            {
                if (writer != null) await writer.DisposeAsync();
            }
            for (var i = 0; i < counts.Length; i++)
                if (counts[i] > 0) used++;
            _context.Telemetry.PartitionsCount += used;
            _context.Telemetry.PartitionPassCount++;
        }

        return new PartitionSet(names, counts);
    }

    private async IAsyncEnumerable<Row> ReadPartition(string name)
    {
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
        await foreach (var row in reader.AsEnumerableAsync())
            yield return row;
    }

    private static async IAsyncEnumerable<Row> Concat(List<Row> prefix, IAsyncEnumerator<Row> rest)
    {
        foreach (var row in prefix) yield return row;
        while (await rest.MoveNextAsync()) yield return rest.Current;
    }

    /// <summary>The dedup equality key: the full projected row, unsalted.</summary>
    private static CompoundKey Key(Row row)
    {
        var names = row.GetColumnNames().ToArray();
        var values = new object?[names.Length];
        for (var i = 0; i < values.Length; i++) values[i] = row[names[i]];
        return new CompoundKey(values);
    }

    private static CompoundKey Key(ColumnBatch batch, int rowIndex)
    {
        var values = new object?[batch.Schema.Count];
        for (var column = 0; column < values.Length; column++)
            values[column] = RowPacker.ReadBatchValue(batch, column, rowIndex);
        return new CompoundKey(values);
    }

    /// <summary>
    /// The partition-routing key. At depth 0 it equals the dedup key; at deeper levels it is
    /// salted with the depth so repartitioning spreads rows differently while still routing
    /// identical rows together (correctness: equal rows always share a partition at every level).
    /// </summary>
    private static CompoundKey RouteKey(Row row, int depth)
    {
        if (depth == 0) return Key(row);
        var names = row.GetColumnNames().ToArray();
        var values = new object?[names.Length];
        for (var i = 0; i < values.Length; i++) values[i] = row[names[i]];
        return new CompoundKey(depth, values);
    }

    private readonly record struct PartitionSet(string[] Names, long[] Counts);

    /// <summary>The distinct rows of a partition held as compact packed blobs plus the shared column
    /// order captured from the first retained row, used to decode each blob back to a <see cref="Row"/>.</summary>
    private sealed class PackedRows
    {
        public readonly List<string> Columns = new();
        public readonly List<byte[]> Blobs = new();
    }
}
