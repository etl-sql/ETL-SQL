using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// High-scale window function processor that spills partitions to disk when data exceeds memory thresholds.
    /// Supports multi-pass grouping for incompatible window signatures (different PARTITION BY).
    /// </summary>
    public class ExternalWindowEngine
    {
        private readonly IExecutionContext _context;
        private readonly WindowEngine _inMemoryEngine;
        private readonly ExternalSortEngine _sortEngine;
        private readonly ILogger _logger;
        private readonly string _tempDir;
        private int PartitionCount => _context.ExternalHashPartitions;

        public ExternalWindowEngine(IExecutionContext context, WindowEngine inMemoryEngine, ILogger logger)
        {
            _context = context;
            _inMemoryEngine = inMemoryEngine;
            _sortEngine = new ExternalSortEngine(context, logger);
            _logger = logger;
            _tempDir = Path.Combine(Path.GetTempPath(), "ETL-SQL", "WinSpill", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        private record WindowSignature(List<Expression>? PartitionBy, List<OrderByClause>? OrderBy)
        {
            public virtual bool Equals(WindowSignature? other)
            {
                if (other == null) return false;
                if (!AreListsEqual(PartitionBy, other.PartitionBy)) return false;
                if (!AreListsEqual(OrderBy, other.OrderBy)) return false;
                return true;
            }

            public override int GetHashCode()
            {
                int hash = 17;
                if (PartitionBy != null) foreach (var p in PartitionBy) hash = hash * 31 + p.GetHashCode();
                if (OrderBy != null) foreach (var o in OrderBy) hash = hash * 31 + o.GetHashCode();
                return hash;
            }

            private static bool AreListsEqual<T>(List<T>? a, List<T>? b)
            {
                if (a == null && b == null) return true;
                if (a == null || b == null) return false;
                if (a.Count != b.Count) return false;
                for (int i = 0; i < a.Count; i++)
                    if (!a[i]!.Equals(b[i])) return false;
                return true;
            }
        }

        private class WindowGroup
        {
            public WindowSignature Signature { get; }
            public List<SelectColumn> Columns { get; } = new();
            public WindowGroup(WindowSignature sig) => Signature = sig;
        }

        /// <summary>
        /// Applies window functions by grouping columns into compatible clusters and processing each group.
        /// If multiple clusters exist, they are processed sequentially via intermediate spills.
        /// </summary>
        public async IAsyncEnumerable<Row> ApplyWindowFunctionsExternal(IAsyncEnumerable<Row> inputStream, SelectStatement stmt)
        {
            var windowCols = stmt.Columns.Where(c => c.Expression is FunctionCallExpression f && f.Window != null).ToList();
            if (windowCols.Count == 0)
            {
                await foreach (var row in inputStream) yield return row;
                yield break;
            }

            // 1. Cluster window functions by signature (PARTITION BY + ORDER BY)
            var groups = new List<WindowGroup>();
            foreach (var col in windowCols)
            {
                var f = (FunctionCallExpression)col.Expression;
                var sig = new WindowSignature(f.Window!.PartitionBy, f.Window.OrderBy);
                var group = groups.FirstOrDefault(g => g.Signature.Equals(sig));
                if (group == null)
                {
                    group = new WindowGroup(sig);
                    groups.Add(group);
                }
                group.Columns.Add(col);
            }

            _logger.WriteLine($"[yellow]HYPER-SCALE: Processing {windowCols.Count} window functions across {groups.Count} signature groups.[/]");

            try
            {
                IAsyncEnumerable<Row> currentStream = inputStream;

                // 2. Process each group sequentially
                for (int i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    bool isLastGroup = (i == groups.Count - 1);

                    currentStream = ProcessWindowGroup(currentStream, group, stmt);

                    // If not the last group, we might need to spill the intermediate result to a temp file
                    // because the next group's re-partitioning will re-read the entire stream.
                    if (!isLastGroup)
                    {
                        var intermediatePath = Path.Combine(_tempDir, $"inter_pass_{i}.tmp");
                        await SpillStreamToDisk(currentStream, intermediatePath);
                        currentStream = ReadPartitionStream(intermediatePath);
                    }
                }

                await foreach (var row in currentStream)
                {
                    yield return row;
                }
            }
            finally
            {
                CleanupTempDir();
            }
        }

        private async IAsyncEnumerable<Row> ProcessWindowGroup(IAsyncEnumerable<Row> stream, WindowGroup group, SelectStatement stmt)
        {
            _logger.WriteLine($"[blue]   - Group: {group.Columns.Count} cols, PARTITION BY ({(group.Signature.PartitionBy?.Count ?? 0)} expressions)[/]");

            // Phase A: Partition to Buckets
            var partitionPaths = await PartitionStream(stream, group.Signature.PartitionBy);

            // Phase B: Process each bucket
            foreach (var path in partitionPaths)
            {
                if (!File.Exists(path)) continue;

                var bucketRows = ReadPartitionStream(path);
                
                // If partition has ORDER BY, we must sort it first.
                // If the partition is too large for memory, ExternalSortEngine will handle the deep-spilling.
                if (group.Signature.OrderBy != null && group.Signature.OrderBy.Count > 0)
                {
                    bucketRows = _sortEngine.SortStreamAsync(bucketRows, group.Signature.OrderBy);
                }

                // Since window functions typically require the entire partition context (e.g., RANK, MAX over partition),
                // we load the bucket into memory HERE. 
                // WARNING: If a SINGLE partition exceeds WindowSpillThreshold, it will still load to memory here.
                // TODO: Implement window-specific streaming algorithms for ROW_NUMBER etc. (Deep-Spilling task).
                var rows = await bucketRows.ToListAsync();
                
                if (rows.Count > 0)
                {
                    // Use a temporary SelectStatement containing ONLY this group's columns to avoid re-evaluating others
                    var groupStmt = new SelectStatement(group.Columns, null, stmt.FromTable, new List<JoinClause>(), null, null, null, group.Signature.OrderBy);
                    var processedRows = await _inMemoryEngine.ApplyWindowFunctions(rows, groupStmt);
                    foreach (var row in processedRows) yield return row;
                }

                File.Delete(path);
            }
        }

        private async Task<string[]> PartitionStream(IAsyncEnumerable<Row> stream, List<Expression>? partitionBy)
        {
            var paths = new string[PartitionCount];
            var writers = new StreamWriter[PartitionCount];

            for (int i = 0; i < PartitionCount; i++)
            {
                paths[i] = Path.Combine(_tempDir, $"win_part_{Guid.NewGuid():N}.tmp");
                writers[i] = new StreamWriter(paths[i]);
            }

            try
            {
                await foreach (var row in stream)
                {
                    int pIdx = 0;
                    if (partitionBy != null && partitionBy.Count > 0)
                    {
                        int hash = 17;
                        foreach (var expr in partitionBy)
                        {
                            var val = await _context.EvaluateValue(expr, row);
                            hash = hash * 31 + (val?.GetHashCode() ?? 0);
                        }
                        pIdx = Math.Abs(hash % PartitionCount);
                    }

                    var json = JsonSerializer.Serialize(row.Columns);
                    _context.TotalSpilledBytes += System.Text.Encoding.UTF8.GetByteCount(json) + 1;
                    await writers[pIdx].WriteLineAsync(json);
                }
            }
            finally
            {
                int usedCount = 0;
                foreach (var w in writers)
                {
                    if (w.BaseStream.Length > 0) usedCount++;
                    w.Flush();
                    w.Dispose();
                }
                _context.PartitionsCount += usedCount;
            }

            return paths;
        }

        private async Task SpillStreamToDisk(IAsyncEnumerable<Row> stream, string path)
        {
            using var writer = new StreamWriter(path);
            await foreach (var row in stream)
            {
                var json = JsonSerializer.Serialize(row.Columns);
                await writer.WriteLineAsync(json);
            }
        }

        private async IAsyncEnumerable<Row> ReadPartitionStream(string path)
        {
            using var reader = new StreamReader(path);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var cols = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
                if (cols != null)
                {
                    var row = new Row();
                    foreach (var kvp in cols) row[kvp.Key] = JsonElementToValue(kvp.Value);
                    yield return row;
                }
            }
        }

        private static object? JsonElementToValue(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : (object?)element.GetDouble(),
                JsonValueKind.String => decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (object?)d : (DateTime.TryParse(element.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (object?)element.GetString()),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };

        private void CleanupTempDir()
        {
            if (Directory.Exists(_tempDir))
                try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
