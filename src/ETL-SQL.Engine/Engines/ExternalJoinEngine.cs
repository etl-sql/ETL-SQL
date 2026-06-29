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

    private readonly IExecutionContext _context;
    private readonly ILogger _logger;
    public int PartitionCount => Math.Max(1, _context.ExternalHashPartitions);


    private readonly IBufferManager? _bufferManager;

    public ExternalJoinEngine(IExecutionContext context, ILogger logger)
    {
        _context = context;
        _logger = logger;
        _bufferManager = _context.ServiceProvider?.GetService<IBufferManager>();
    }

    /// <summary>Performs an external hash join by partitioning both left and right streams to disk before join processing.</summary>
    public async IAsyncEnumerable<Row> ApplyHashJoinExternal(IAsyncEnumerable<Row> leftStream, IAsyncEnumerable<Row> rightStream, JoinClause join, List<string> leftKeys, List<string> rightKeys)
    {
        using var cursor = _bufferManager != null ? await _bufferManager.AcquireCursorAsync(_context.SessionId ?? "DEFAULT", owner: this) : null;
        var tempFiles = new List<string>();
        try
        {
            // 1. Partition Phase
            var leftPartitions = await PartitionStream(leftStream, leftKeys, "left", tempFiles);
            var rightPartitions = await PartitionStream(rightStream, rightKeys, "right", tempFiles);

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
        var hashTable = await BuildJoinHashTable(rightName, rightKeys, new HeapGrowthGuard(ceiling));

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
            hashTable = await BuildJoinHashTable(rightName, rightKeys, new HeapGrowthGuard(0));
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
    /// Builds the in-memory hash table from the (right) build side. Returns null if managed-heap
    /// growth crosses the governor ceiling mid-build, so the caller can repartition or apply policy
    /// before any probe row has been emitted.
    /// </summary>
    private async Task<Dictionary<CompoundKey, List<Row>>?> BuildJoinHashTable(string rightName, List<string> rightKeys, HeapGrowthGuard guard)
    {
        var hashTable = new Dictionary<CompoundKey, List<Row>>();
        await using var rightReader = await _context.SpillStore.CreateReaderAsync(rightName);
        await foreach (var rightRow in rightReader.AsEnumerableAsync())
        {
            var key = GetHashKey(rightRow, rightKeys);
            if (!hashTable.TryGetValue(key, out var bucket)) { bucket = new List<Row>(); hashTable[key] = bucket; }
            bucket.Add(rightRow);
            if (guard.Exceeded()) return null;
        }
        return hashTable;
    }

    /// <summary>Streams the probe (left) side against a built hash table, emitting join results.</summary>
    private async IAsyncEnumerable<Row> ProbeJoin(string leftName, Dictionary<CompoundKey, List<Row>> hashTable, JoinClause join, List<string> leftKeys)
    {
        bool isLeftJoin = join.JoinType.Contains("LEFT", StringComparison.OrdinalIgnoreCase);

        await using var leftReader = await _context.SpillStore.CreateReaderAsync(leftName);
        await foreach (var left in leftReader.AsEnumerableAsync())
        {
            var key = GetHashKey(left, leftKeys);
            bool producedMatch = false;

            if (hashTable.TryGetValue(key, out var matches))
            {
                foreach (var right in matches)
                {
                    var combined = CombineRows(left, right);
                    if (await _context.EvaluateCondition(join.Condition, combined))
                    {
                        yield return combined;
                        producedMatch = true;
                    }
                }
            }

            if (!producedMatch && isLeftJoin)
                yield return left.Clone();
        }
    }

    private async Task<PartitionSet> PartitionStream(IAsyncEnumerable<Row> stream, List<string> keys, string prefix, List<string> tempFiles)
    {
        return await PartitionStream(stream, keys, prefix, depth: 0, tempFiles);
    }

    private async Task<PartitionSet> RepartitionPartition(string sourceName, List<string> keys, string prefix, int depth, List<string> tempFiles)
    {
        return await PartitionStream(ReadPartitionStream(sourceName), keys, prefix, depth, tempFiles);
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

    private CompoundKey GetPartitionHashKey(Row row, List<string> keys, int depth)
    {
        var values = new object?[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            values[i] = SpillSerializationHelper.UnwrapValue(row[keys[i]]);
        return new CompoundKey(depth, values);
    }

    private Row CombineRows(Row left, Row right)
    {
        var combined = new Row();
        left.ForEachColumn((name, value) => combined[name] = value);
        right.ForEachColumn((name, value) => combined[name] = value);
        return combined;
    }

    private readonly record struct PartitionSet(string[] Names, long[] Counts);
}

