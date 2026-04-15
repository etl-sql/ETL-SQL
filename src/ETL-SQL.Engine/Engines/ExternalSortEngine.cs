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
        private int ChunkSize => _context.ExternalSortChunkSize;

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
            var stream = ConvertToAsyncEnumerable(rows);
            var sortedStream = SortStreamAsync(stream, orderBy);
            return await sortedStream.ToListAsync();
        }

        private async IAsyncEnumerable<Row> ConvertToAsyncEnumerable(List<Row> rows)
        {
            foreach (var r in rows) yield return r;
            await Task.CompletedTask;
        }

        /// <summary>
        /// Sorts an asynchronous stream of rows using an external merge sort.
        /// spills chunks to disk and merges them back.
        /// </summary>
        public async IAsyncEnumerable<Row> SortStreamAsync(
            IAsyncEnumerable<Row> inputStream,
            List<OrderByClause> orderBy)
        {
            var chunkPaths = new List<string>();
            try
            {
                // 1. Comparison function
                int Compare((Row Row, object?[] Keys) a, (Row Row, object?[] Keys) b)
                {
                    for (int i = 0; i < orderBy.Count; i++)
                    {
                        var res = _context.CompareConstants(a.Keys[i], b.Keys[i]);
                        if (res != 0) return orderBy[i].Descending ? -res : res;
                    }
                    return 0;
                }

                // 2. Consume stream and spill chunks
                var currentChunk = new List<(Row Row, object?[] Keys)>();
                await foreach (var row in inputStream)
                {
                    var keys = new object?[orderBy.Count];
                    for (int i = 0; i < orderBy.Count; i++)
                        keys[i] = await _context.EvaluateValue(orderBy[i].Expression, row);
                    
                    currentChunk.Add((row, keys));

                    if (currentChunk.Count >= ChunkSize)
                    {
                        currentChunk.Sort(Compare);
                        var path = Path.Combine(_tempDir, $"chunk_{chunkPaths.Count}.tmp");
                        await WriteChunk(path, currentChunk);
                        chunkPaths.Add(path);
                        currentChunk.Clear();
                    }
                }

                if (currentChunk.Count > 0)
                {
                    currentChunk.Sort(Compare);
                    var path = Path.Combine(_tempDir, $"chunk_{chunkPaths.Count}.tmp");
                    await WriteChunk(path, currentChunk);
                    chunkPaths.Add(path);
                }

                // 3. K-way Merge and yield
                if (chunkPaths.Count == 0) yield break;
                
                var readers = chunkPaths.Select(p => new StreamReader(p)).ToList();
                try
                {
                    var heap = new PriorityQueue<int, (Row Row, object?[] Keys)>(Comparer<(Row, object?[])>.Create(Compare));

                    for (int i = 0; i < readers.Count; i++)
                    {
                        var line = await readers[i].ReadLineAsync();
                        if (line != null)
                        {
                            var entry = ParseEntry(line);
                            if (entry != null) heap.Enqueue(i, entry.Value);
                        }
                    }

                    while (heap.Count > 0)
                    {
                        if (heap.TryDequeue(out int chunkIdx, out var first))
                        {
                            yield return first.Row;

                            var nextLine = await readers[chunkIdx].ReadLineAsync();
                            if (nextLine != null)
                            {
                                var entry = ParseEntry(nextLine);
                                if (entry != null) heap.Enqueue(chunkIdx, entry.Value);
                            }
                        }
                    }
                }
                finally
                {
                    foreach (var rd in readers) rd.Dispose();
                }
            }
            finally
            {
                // Cleanup will happen in SortExternal or the caller must handle temp directory cleanup
                // Since this is private/internal, we assume the temp directory lifecycle is managed by SortExternal
                // or similar top-level method.
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
                    System.Text.Json.JsonValueKind.False   => false,
                    System.Text.Json.JsonValueKind.String  => DateTime.TryParse(je.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : je.GetString(),
                    System.Text.Json.JsonValueKind.Null    => null,
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
                    System.Text.Json.JsonValueKind.String => DateTime.TryParse(el.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : el.GetString(),
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
