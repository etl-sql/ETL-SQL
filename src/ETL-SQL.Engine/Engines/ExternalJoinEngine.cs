using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Implements disk-spilling hash joins for large datasets that exceed memory capacity.
    /// </summary>
    public class ExternalJoinEngine
    {
        private readonly IExecutionContext _context;
        private readonly ILogger _logger;
        private readonly string _tempDir;

        private int PartitionCount => _context.ExternalHashPartitions;



        public ExternalJoinEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
            _tempDir = Path.Combine(Path.GetTempPath(), "ETL-SQL", "JoinSpill", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        /// <summary>Performs an external hash join by partitioning both left and right streams to disk before join processing.</summary>
        public async Task<List<Row>> ApplyHashJoinExternal(IAsyncEnumerable<Row> leftStream, IAsyncEnumerable<Row> rightStream, JoinClause join, List<string> leftKeys, List<string> rightKeys)
        {
            try
            {
                // 1. Partition Phase
                var leftPartitions = await PartitionStream(leftStream, leftKeys, "left");
                var rightPartitions = await PartitionStream(rightStream, rightKeys, "right");

                var results = new List<Row>();

                // 2. Join Phase (one partition at a time)
                for (int i = 0; i < PartitionCount; i++)

                {
                    var leftPath = leftPartitions[i];
                    var rightPath = rightPartitions[i];

                    if (!File.Exists(leftPath) || !File.Exists(rightPath)) continue;

                    var leftRows = await ReadPartition(leftPath);
                    var rightRows = await ReadPartition(rightPath);

                    // Perform standard in-memory hash join on this partition
                    var partResults = await PerformInMemoryHashJoin(leftRows, rightRows, join, leftKeys, rightKeys);
                    results.AddRange(partResults);

                    // Clean up partition files
                    File.Delete(leftPath);
                    File.Delete(rightPath);
                }

                return results;
            }
            finally
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            }
        }

        private async Task<string[]> PartitionStream(IAsyncEnumerable<Row> stream, List<string> keys, string prefix)
        {
            var paths = new string[PartitionCount];
            var writers = new StreamWriter[PartitionCount];

            for (int i = 0; i < PartitionCount; i++)

            {
                paths[i] = Path.Combine(_tempDir, $"{prefix}_{i}.tmp");
                writers[i] = new StreamWriter(paths[i]);
            }

            try
            {
                await foreach (var row in stream)
                {
                    var hash = GetKeyHash(row, keys);
                    int pIdx = Math.Abs(hash % PartitionCount);

                    var json = System.Text.Json.JsonSerializer.Serialize(row.Columns);
                    var bytes = System.Text.Encoding.UTF8.GetByteCount(json) + 2; // + newline
                    _context.TotalSpilledBytes += bytes;
                    if (prefix == "left" && bytes > 0 && Math.Abs(hash % 20000) == 0) _logger.Debug("[DIAG] Spilled {Bytes} bytes to partition {PartitionIndex}. Total bytes spilled: {TotalSpilledBytes}", bytes, pIdx, _context.TotalSpilledBytes);
                    await writers[pIdx].WriteLineAsync(json);
                }
            }
            finally
            {
                int usedPartitions = 0;
                foreach (var w in writers) 
                { 
                    try
                    {
                        if (w.BaseStream.Length > 0) usedPartitions++;
                        w.Flush(); 
                        w.Close(); 
                    }
                    catch { /* Best effort cleanup */ }
                }
                _context.PartitionsCount += usedPartitions;
                _logger.Debug("Finished partitioning {Prefix}. Used {UsedPartitions} partitions. Context PartitionsCount: {PartitionsCount}", prefix, usedPartitions, _context.PartitionsCount);
            }

            return paths;
        }

        private async Task<List<Row>> ReadPartition(string path)
        {
            var rows = new List<Row>();
            using var reader = new StreamReader(path);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var cols = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(line);
                if (cols != null)
                {
                    var row = new Row();
                    foreach (var kvp in cols) row[kvp.Key] = UnwrapJsonValue(kvp.Value);
                    rows.Add(row);
                }
            }
            return rows;
        }

        private static object? UnwrapJsonValue(object? val)
        {
            if (val is System.Text.Json.JsonElement je)
                return je.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Number  => je.TryGetDecimal(out var d) ? d : (object?)je.GetDouble(),
                    System.Text.Json.JsonValueKind.True    => true,
                    System.Text.Json.JsonValueKind.False   => false,
                    System.Text.Json.JsonValueKind.String  => DateTime.TryParse(je.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : je.GetString(),
                    System.Text.Json.JsonValueKind.Null    => null,
                    _                                     => (object?)je.ToString()
                };
            return val;
        }

        private int GetKeyHash(Row row, List<string> keys)
        {
            int hash = 17;
            foreach (var k in keys)
            {
                var val = row[k];
                hash = hash * 31 + (val?.GetHashCode() ?? 0);
            }
            return hash;
        }

        private async Task<List<Row>> PerformInMemoryHashJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join, List<string> leftKeys, List<string> rightKeys)
        {
            // Standard Hash Join logic similar to JoinEngine but for a partition
            var results = new List<Row>();
            var hashTable = new Dictionary<CompoundKey, List<Row>>();
            foreach (var r in rightRows)
            {
                var key = GetHashKey(r, rightKeys);
                if (!hashTable.TryGetValue(key, out var list)) { list = new List<Row>(); hashTable[key] = list; }
                list.Add(r);
            }

            foreach (var left in leftRows)
            {
                var key = GetHashKey(left, leftKeys);
                if (hashTable.TryGetValue(key, out var matches))
                {
                    foreach (var right in matches)
                    {
                        var combined = CombineRows(left, right);
                        if (await _context.EvaluateCondition(join.Condition, combined)) results.Add(combined);
                    }
                }
                else if (join.JoinType.Contains("LEFT", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(left.Clone());
                }
            }
            return results;
        }

        private CompoundKey GetHashKey(Row row, List<string> keys)
        {
            var values = new object?[keys.Count];
            for (int i = 0; i < keys.Count; i++) values[i] = row[keys[i]];
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
