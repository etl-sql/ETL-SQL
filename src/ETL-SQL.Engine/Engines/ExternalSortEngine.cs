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
    /// High-scale sort engine that spills sorted chunks to disk when row count exceeds the in-memory threshold.
    /// Uses an external k-way merge sort: divide into sorted runs, then merge with a min-heap.
    /// </summary>
    public class ExternalSortEngine
    {
        private readonly IExecutionContext _context;
        private readonly ILogger _logger;
        private readonly string _tempDir;
        private const int CHUNK_SIZE = 100_000;

        public ExternalSortEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
            _tempDir = Path.Combine(Path.GetTempPath(), "ETL-SQL", "SortSpill", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        /// <summary>
        /// Sorts all rows using an external merge sort. Sort keys must be pre-evaluated.
        /// </summary>
        public async Task<List<Row>> SortExternal(
            List<Row> rows,
            List<OrderByClause> orderBy)
        {
            try
            {
                _logger.WriteLine($"[yellow]HYPER-SCALE: ORDER BY has {rows.Count:N0} rows — switching to external merge sort (spill-to-disk).[/]");

                // 1. Pre-evaluate all sort keys (async, must be done before sort)
                var keyed = new List<(Row Row, object?[] Keys)>(rows.Count);
                foreach (var row in rows)
                {
                    var keys = new object?[orderBy.Count];
                    for (int i = 0; i < orderBy.Count; i++)
                        keys[i] = await _context.EvaluateValue(orderBy[i].Expression, row);
                    keyed.Add((row, keys));
                }

                // 2. Comparison function
                int Compare((Row Row, object?[] Keys) a, (Row Row, object?[] Keys) b)
                {
                    for (int i = 0; i < orderBy.Count; i++)
                    {
                        var res = _context.CompareConstants(a.Keys[i], b.Keys[i]);
                        if (res != 0) return orderBy[i].Descending ? -res : res;
                    }
                    return 0;
                }

                // 3. Sort and spill chunks
                var chunkPaths = new List<string>();
                for (int offset = 0; offset < keyed.Count; offset += CHUNK_SIZE)
                {
                    var chunk = keyed.GetRange(offset, Math.Min(CHUNK_SIZE, keyed.Count - offset));
                    chunk.Sort(Compare);

                    var path = Path.Combine(_tempDir, $"chunk_{chunkPaths.Count}.tmp");
                    await WriteChunk(path, chunk);
                    chunkPaths.Add(path);
                }

                // 4. K-way merge
                return await MergeChunks(chunkPaths, Compare);
            }
            finally
            {
                if (Directory.Exists(_tempDir))
                    try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        private async Task WriteChunk(string path, List<(Row Row, object?[] Keys)> chunk)
        {
            using var writer = new StreamWriter(path);
            foreach (var (row, keys) in chunk)
            {
                var entry = new SortEntry { Columns = row.Columns, Keys = keys.Select(SerializeKey).ToArray() };
                var json = System.Text.Json.JsonSerializer.Serialize(entry);
                _context.TotalSpilledBytes += System.Text.Encoding.UTF8.GetByteCount(json) + 1;
                await writer.WriteLineAsync(json);
            }
        }

        private async Task<List<Row>> MergeChunks(List<string> paths, Comparison<(Row, object?[])> compare)
        {
            if (paths.Count == 0) return new List<Row>();
            if (paths.Count == 1)
            {
                var rows = new List<Row>();
                using var r = new StreamReader(paths[0]);
                string? line;
                while ((line = await r.ReadLineAsync()) != null)
                {
                    var e = System.Text.Json.JsonSerializer.Deserialize<SortEntry>(line);
                    if (e?.Columns != null)
                    {
                        var row = new Row();
                        foreach (var kvp in e.Columns) row[kvp.Key] = UnwrapJsonValue(kvp.Value);
                        rows.Add(row);
                    }
                }
                return rows;
            }

            // Open all chunk readers
            var readers = paths.Select(p => new StreamReader(p)).ToList();
            try
            {
                var heap = new PriorityQueue<int, (Row Row, object?[] Keys)>(Comparer<(Row, object?[])>.Create(compare));

                // Seed heap with first row from each chunk
                for (int i = 0; i < readers.Count; i++)
                {
                    var line = await readers[i].ReadLineAsync();
                    if (line != null)
                    {
                        var entry = ParseEntry(line);
                        if (entry != null) heap.Enqueue(i, entry.Value);
                    }
                }

                var result = new List<Row>();
                while (heap.Count > 0)
                {
                    if (heap.TryDequeue(out int chunkIdx, out var first))
                    {
                        result.Add(first.Item1);

                        var nextLine = await readers[chunkIdx].ReadLineAsync();
                        if (nextLine != null)
                        {
                            var entry = ParseEntry(nextLine);
                            if (entry != null) heap.Enqueue(chunkIdx, entry.Value);
                        }
                    }
                }

                return result;
            }
            finally
            {
                foreach (var rd in readers) rd.Dispose();
            }
        }

        private (Row, object?[])? ParseEntry(string line)
        {
            var e = System.Text.Json.JsonSerializer.Deserialize<SortEntry>(line);
            if (e?.Columns == null) return null;
            var row = new Row();
            foreach (var kvp in e.Columns) row[kvp.Key] = UnwrapJsonValue(kvp.Value);
            var keys = e.Keys?.Select(DeserializeKey).ToArray() ?? Array.Empty<object?>();
            return (row, keys);
        }

        private static object? UnwrapJsonValue(object? val)
        {
            if (val is System.Text.Json.JsonElement je)
                return je.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Number  => je.TryGetDecimal(out var d) ? d : (object?)je.GetDouble(),
                    System.Text.Json.JsonValueKind.True    => true,
                    System.Text.Json.JsonValueKind.False  => false,
                    System.Text.Json.JsonValueKind.String => je.GetString(),
                    System.Text.Json.JsonValueKind.Null   => null,
                    _                                     => (object?)je.ToString()
                };
            return val;
        }

        private static string? SerializeKey(object? v) => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v);

        private static object? DeserializeKey(string? s)
        {
            if (s == null) return null;
            if (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(s) is System.Text.Json.JsonElement el)
            {
                return el.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Number when el.TryGetDecimal(out var d) => d,
                    System.Text.Json.JsonValueKind.Number => el.GetDouble(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.String => el.GetString(),
                    _ => s
                };
            }
            return s;
        }

        private class SortEntry
        {
            public Dictionary<string, object?> Columns { get; set; } = new();
            public string?[]? Keys { get; set; }
        }
    }
}
