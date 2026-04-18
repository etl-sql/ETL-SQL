using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Data;

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
                
                List<Row> leftRows  = await leftReader.AsEnumerableAsync().ToListAsync();
                List<Row> rightRows = await rightReader.AsEnumerableAsync().ToListAsync();
                
                if (leftRows.Count == 0) continue;
    
                // Perform standard in-memory hash join on this partition
                var partResults = await PerformInMemoryHashJoin(leftRows, rightRows, join, leftKeys, rightKeys);
                results.AddRange(partResults);
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
                    int rawHash = GetKeyHash(row, keys);
                    int pIdx = (rawHash & 0x7FFFFFFF) % PartitionCount;
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


        private static object? UnwrapJsonValue(object? val)
        {
            if (val == null || val == DBNull.Value) return null;

            if (val is System.Text.Json.JsonElement je)
            {
                val = je.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Number when je.TryGetDecimal(out var dv) => dv,
                    System.Text.Json.JsonValueKind.Number => je.GetDouble(),
                    System.Text.Json.JsonValueKind.True  => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.String => je.GetString(),
                    System.Text.Json.JsonValueKind.Null  => null,
                    _                   => (object?)je.ToString()
                };
                if (val == null) return null;
            }
            
            if (val is string s)
            {
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dec2)) 
                    return dec2;
                if (EvaluationUtils.SafeTryParseDate(s, out var dt2))
                    return dt2;
                return s.Trim(); 
            }

            return val;
        }

        private int GetKeyHash(Row row, List<string> keys)
        {
            var values = new object?[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                values[i] = row[keys[i]];
            }
            return new CompoundKey(values).GetHashCode();
        }

        private async Task<List<Row>> PerformInMemoryHashJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join, List<string> leftKeys, List<string> rightKeys)
        {
            // Standard Hash Join logic similar to JoinEngine but for a partition
            var results = new List<Row>();
            var hashTable = new Dictionary<ETL_SQL.Data.CompoundKey, List<Row>>();
            foreach (var r in rightRows)
            {
                var key = GetHashKey(r, rightKeys);
                if (!hashTable.TryGetValue(key, out var list)) { list = new List<Row>(); hashTable[key] = list; }
                list.Add(r);
            }

            foreach (var left in leftRows)
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

                if (!producedMatch && join.JoinType.Contains("LEFT", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(left.Clone());
                }
            }
            return results;
        }

        private CompoundKey GetHashKey(Row row, List<string> keys)
        {
            var values = new object?[keys.Count];
            for (int i = 0; i < keys.Count; i++) 
            {
                var val = row[keys[i]];
                values[i] = UnwrapJsonValue(val);
            }
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
