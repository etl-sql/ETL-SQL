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

namespace ETL_SQL.Engine.Engines
{
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
            // 1. Partition Phase
            var leftPartitions = await PartitionStream(leftStream, leftKeys, "left");
            var rightPartitions = await PartitionStream(rightStream, rightKeys, "right");

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
                    depth: 0))
                {
                    yield return row;
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
            int depth)
        {
            if (ShouldRepartition(rightRowCount, depth))
            {
                var nextDepth = depth + 1;
                var leftPartitions = await RepartitionPartition(leftName, leftKeys, $"left_d{nextDepth}", nextDepth);
                var rightPartitions = await RepartitionPartition(rightName, rightKeys, $"right_d{nextDepth}", nextDepth);
                var largestRightPartition = rightPartitions.Counts.Length == 0 ? 0 : rightPartitions.Counts.Max();
                var usedRightPartitions = rightPartitions.Counts.Count(c => c > 0);

                if (usedRightPartitions > 1 && largestRightPartition < rightRowCount)
                {
                    _logger.Debug(
                        "Recursively repartitioned external join partition at depth {Depth}. Rows: left={LeftRows}, right={RightRows}, largestRight={LargestRight}",
                        nextDepth,
                        leftRowCount,
                        rightRowCount,
                        largestRightPartition);

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
                            nextDepth))
                        {
                            yield return row;
                        }
                    }

                    yield break;
                }

                _logger.Debug(
                    "External join partition at depth {Depth} could not be reduced further. Falling back to direct partition join for {RightRows} right rows.",
                    depth,
                    rightRowCount);
            }

            await foreach (var row in JoinPartitionDirect(leftName, rightName, join, leftKeys, rightKeys))
                yield return row;
        }

        private bool ShouldRepartition(long rightRowCount, int depth)
        {
            return PartitionCount > 1
                && depth < MaxRecursivePartitionDepth
                && rightRowCount > Math.Max(1, _context.JoinSpillThreshold);
        }

        private async IAsyncEnumerable<Row> JoinPartitionDirect(string leftName, string rightName, JoinClause join, List<string> leftKeys, List<string> rightKeys)
        {
            await using var leftReader = await _context.SpillStore.CreateReaderAsync(leftName);
            await using var rightReader = await _context.SpillStore.CreateReaderAsync(rightName);

            var hashTable = new Dictionary<CompoundKey, List<Row>>();
            await foreach (var rightRow in rightReader.AsEnumerableAsync())
            {
                var key = GetHashKey(rightRow, rightKeys);
                if (!hashTable.TryGetValue(key, out var bucket)) { bucket = new List<Row>(); hashTable[key] = bucket; }
                bucket.Add(rightRow);
            }

            bool isLeftJoin = join.JoinType.Contains("LEFT", StringComparison.OrdinalIgnoreCase);

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

        private async Task<PartitionSet> PartitionStream(IAsyncEnumerable<Row> stream, List<string> keys, string prefix)
        {
            return await PartitionStream(stream, keys, prefix, depth: 0);
        }

        private async Task<PartitionSet> RepartitionPartition(string sourceName, List<string> keys, string prefix, int depth)
        {
            return await PartitionStream(ReadPartitionStream(sourceName), keys, prefix, depth);
        }

        private async Task<PartitionSet> PartitionStream(IAsyncEnumerable<Row> stream, List<string> keys, string prefix, int depth)
        {
            var names = new string[PartitionCount];
            var counts = new long[PartitionCount];
            var writers = new ETL_SQL.Core.Spill.ISpillWriter[PartitionCount];

            var uniquePrefix = Guid.NewGuid().ToString("N");
            for (int i = 0; i < PartitionCount; i++)
            {
                names[i] = $"{uniquePrefix}_{prefix}_{i}.tmp";
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
            foreach (var kv in left.Columns) combined[kv.Key] = kv.Value;
            foreach (var kv in right.Columns) combined[kv.Key] = kv.Value;
            return combined;
        }

        private readonly record struct PartitionSet(string[] Names, long[] Counts);
    }
}

