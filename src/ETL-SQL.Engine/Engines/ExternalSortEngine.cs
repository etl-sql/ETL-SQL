using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Spill;
using ETL_SQL.Engine.Spill;

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
        public int ChunkSize => _context.ExternalSortChunkSize;

        public ExternalSortEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
        }

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

            // 1. Comparison function
            int Compare((Row Row, object?[] Keys) a, (Row Row, object?[] Keys) b)
            {
                for (int i = 0; i < orderBy.Count; i++)
                {
                    var res = _context.CompareConstants(a.Keys[i], b.Keys[i]);
                    // For PriorityQueue (Min-Heap), return positive to put it later, negative to put it earlier.
                    // If descending, invert the comparison: greater values should be "smaller" (returned earlier).
                    if (res != 0) return orderBy[i].Descending ? -res : res;
                }
                return 0;
            }

            // 2. Consume stream and spill chunks
            var currentChunk = new List<(Row Row, object?[] Keys)>();
            int chunkCounter = 0;
            await foreach (var row in inputStream)
            {
                var keys = new object?[orderBy.Count];
                for (int i = 0; i < orderBy.Count; i++)
                {
                    var val = await _context.EvaluateValue(orderBy[i].Expression, row);
                    keys[i] = ETL_SQL.Data.CompoundKey.NormalizeValue(val);
                }
                
                currentChunk.Add((row, keys));

                if (currentChunk.Count >= ChunkSize)
                {
                    currentChunk.Sort(Compare);
                    var chunkName = $"sort_chunk_{Guid.NewGuid():N}_{chunkCounter++}.tmp";
                    chunkPaths.Add(chunkName);

                    await using (var writer = await _context.SpillStore.CreateWriterAsync(chunkName))
                    {
                        foreach (var entry in currentChunk)
                        {
                            // Attach keys to row for spilling
                            entry.Row["_SYS_SORT_KEYS_"] = entry.Keys;
                            await writer.WriteRowAsync(entry.Row);
                        }
                    }
                    
                    _context.SortSpillCount++;
                    _context.PartitionsCount++;
                    currentChunk.Clear();
                }
            }

            if (currentChunk.Count > 0)
            {
                currentChunk.Sort(Compare);
                var prefix = Guid.NewGuid().ToString("N");
                var chunkName = $"sort_chunk_{prefix}_{chunkCounter++}.tmp";
                chunkPaths.Add(chunkName);
                
                await using (var writer = await _context.SpillStore.CreateWriterAsync(chunkName))
                {
                    foreach (var entry in currentChunk)
                    {
                        entry.Row["_SYS_SORT_KEYS_"] = entry.Keys;
                        await writer.WriteRowAsync(entry.Row);
                    }
                }
                _context.SortSpillCount++;
                _context.PartitionsCount++;
            }

            // 3. K-way Merge and yield
            if (chunkPaths.Count == 0) yield break;
            
            var readers = new List<ISpillReader>();
            try
            {
                foreach (var path in chunkPaths) 
                    readers.Add(await _context.SpillStore.CreateReaderAsync(path));

                var heap = new PriorityQueue<int, (Row Row, object?[] Keys)>(Comparer<(Row, object?[])>.Create(Compare));

                for (int i = 0; i < readers.Count; i++)
                {
                    var row = await readers[i].ReadRowAsync();
                    if (row != null)
                    {
                        var keysCol = row["_SYS_SORT_KEYS_"];
                        object?[] unwrapped;
                        if (keysCol is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            unwrapped = je.EnumerateArray().Select(x => SpillSerializationHelper.UnwrapValue(x)).Cast<object?>().ToArray();
                        }
                        else
                        {
                             var keys = keysCol as IEnumerable<object>;
                             unwrapped = keys?.Select(x => SpillSerializationHelper.UnwrapValue(x)).ToArray() ?? Array.Empty<object?>();
                        }
                        heap.Enqueue(i, (row, unwrapped));
                    }
                }

                while (heap.Count > 0)
                {
                    if (heap.TryDequeue(out int chunkIdx, out var first))
                    {
                        yield return first.Row;

                        var nextRow = await readers[chunkIdx].ReadRowAsync();
                        if (nextRow != null)
                        {
                            var keysCol = nextRow["_SYS_SORT_KEYS_"];
                            object?[] unwrapped;
                            if (keysCol is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                unwrapped = je.EnumerateArray().Select(x => SpillSerializationHelper.UnwrapValue(x)).Cast<object?>().ToArray();
                            }
                            else
                            {
                                 var keys = keysCol as IEnumerable<object>;
                                 unwrapped = keys?.Select(x => SpillSerializationHelper.UnwrapValue(x)).ToArray() ?? Array.Empty<object?>();
                            }
                            heap.Enqueue(chunkIdx, (nextRow, unwrapped));
                        }
                    }
                }
            }
            finally
            {
                foreach (var rd in readers) await rd.DisposeAsync();
            }
        }

    }
}
