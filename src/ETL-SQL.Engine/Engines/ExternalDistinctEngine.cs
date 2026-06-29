using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
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

    private readonly IExecutionContext _context;

    public ExternalDistinctEngine(IExecutionContext context) => _context = context;

    private int PartitionCount => Math.Max(1, _context.ExternalHashPartitions);

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

    /// <summary>
    /// Deduplicates a single spilled partition. If the partition is small enough it is deduped
    /// in memory; otherwise it is recursively repartitioned with a depth-salted hash so each
    /// sub-partition's in-memory distinct set stays bounded (mirrors the external hash join).
    /// </summary>
    private async IAsyncEnumerable<Row> ProcessPartitionAsync(string name, long rowCount, int depth, List<string> tempFiles)
    {
        var threshold = Math.Max(1, _context.JoinSpillThreshold);

        if (rowCount <= threshold || depth >= MaxRecursivePartitionDepth || PartitionCount <= 1)
        {
            await foreach (var row in DedupInMemory(name))
                yield return row;
            yield break;
        }

        var nextDepth = depth + 1;
        var sub = await PartitionAsync(ReadPartition(name), nextDepth, tempFiles);
        var usedSubPartitions = sub.Counts.Count(c => c > 0);
        var largestSub = sub.Counts.Length == 0 ? 0 : sub.Counts.Max();

        // If repartitioning failed to split the partition (e.g. every row shares the same key),
        // recursion can't help — dedup directly. An identical-key partition has a tiny distinct
        // set regardless of row count, so the in-memory fallback is safe.
        if (usedSubPartitions <= 1 || largestSub >= rowCount)
        {
            await foreach (var row in DedupInMemory(name))
                yield return row;
            yield break;
        }

        for (var i = 0; i < sub.Names.Length; i++)
        {
            await foreach (var row in ProcessPartitionAsync(sub.Names[i], sub.Counts[i], nextDepth, tempFiles))
                yield return row;
        }
    }

    private async IAsyncEnumerable<Row> DedupInMemory(string name)
    {
        var seen = new HashSet<CompoundKey>();
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
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
}
