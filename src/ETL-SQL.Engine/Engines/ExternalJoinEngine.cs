using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine.Spill;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Implements disk-spilling hash joins for large datasets that exceed memory capacity.
    /// </summary>
    public class ExternalJoinEngine
    {
        private readonly IExecutionContext _context;
        private readonly ILogger _logger;
        public int PartitionCount => Math.Max(1, _context.ExternalHashPartitions);


        public ExternalJoinEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
        }
    
        /// <summary>Performs an external hash join by partitioning both left and right streams to disk before join processing.</summary>
        public async Task<List<Row>> ApplyHashJoinExternal(IAsyncEnumerable<Row> leftStream, IAsyncEnumerable<Row> rightStream, JoinClause join, List<string> leftKeys, List<string> rightKeys)
        {
            // 1. Partition Phase
            var leftPartitions = await PartitionStream(leftStream, leftKeys, "left");
            var rightPartitions = await PartitionStream(rightStream, rightKeys, "right");
    
            var results = new List<Row>();
    
            // 2. Join Phase (one partition at a time)
            for (int i = 0; i < PartitionCount; i++)
            {
                var leftName = leftPartitions[i];
                var rightName = rightPartitions[i];
    
                await using var leftReader = await _context.SpillStore.CreateReaderAsync(leftName);
                await using var rightReader = await _context.SpillStore.CreateReaderAsync(rightName);

                // Build hash table by streaming the right partition — never fully materialized
                var hashTable = new Dictionary<CompoundKey, List<Row>>();
                await foreach (var rightRow in rightReader.AsEnumerableAsync())
                {
                    var key = GetHashKey(rightRow, rightKeys);
                    if (!hashTable.TryGetValue(key, out var bucket)) { bucket = new List<Row>(); hashTable[key] = bucket; }
                    bucket.Add(rightRow);
                }

                bool isLeftJoin = join.JoinType.Contains("LEFT", StringComparison.OrdinalIgnoreCase);

                // Probe with streamed left partition
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
                                results.Add(combined);
                                producedMatch = true;
                            }
                        }
                    }

                    if (!producedMatch && isLeftJoin)
                        results.Add(left.Clone());
                }
            }
    
            return results;
        }

        private async Task<string[]> PartitionStream(IAsyncEnumerable<Row> stream, List<string> keys, string prefix)
        {
            var names = new string[PartitionCount];
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
                    int pIdx = (GetHashKey(row, keys).GetHashCode() & 0x7FFFFFFF) % PartitionCount;
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
                        usedPartitions++;
                        await writers[i].DisposeAsync();
                    }
                }
                _context.PartitionsCount += usedPartitions;
                _logger.Debug("Finished partitioning {Prefix}. Used {UsedPartitions} partitions. Context PartitionsCount: {PartitionsCount}", prefix, usedPartitions, _context.PartitionsCount);
            }

            return names;
        }


        private CompoundKey GetHashKey(Row row, List<string> keys)
        {
            var values = new object?[keys.Count];
            for (int i = 0; i < keys.Count; i++)
                values[i] = SpillSerializationHelper.UnwrapValue(row[keys[i]]);
            return new CompoundKey(values);
        }

        private Row CombineRows(Row left, Row right)
        {
            var combined = new Row();
            foreach (var kv in left.Columns) combined[kv.Key] = kv.Value;
            foreach (var kv in right.Columns) combined[kv.Key] = kv.Value;
            return combined;
        }
    }
}
