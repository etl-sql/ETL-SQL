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
/// High-scale sort engine that spills sorted chunks to disk when row count exceeds the in-memory threshold.
/// Uses an external k-way merge sort: divide into sorted runs, then merge with a min-heap.
/// </summary>
public class ExternalSortEngine
{
    private readonly IExecutionContext _context;
    private readonly ILogger _logger;
    private readonly IBufferManager? _bufferManager;
    public int ChunkSize => _context.ExternalSortChunkSize;

    // Column-name prefix for individual sort-key columns written to spill chunks.
    // Using one column per key avoids JSON array serialization and lets Arrow store
    // each value in its native typed column (Int64, Decimal128, Double, Boolean,
    // Timestamp, or String), which eliminates millions of JsonElement allocations
    // and string deserialization cycles on medium/large sort workloads.
    private const string SortKeyPrefix = "_SYS_SK_";

    // Maximum number of spill chunks merged simultaneously in a single pass. Caps the
    // number of concurrently open spill readers / file handles regardless of total chunk
    // count: with a 10k chunk size, 50M rows produces ~5,000 chunks, which would otherwise
    // open ~5,000 readers at once at merge time. When the chunk count exceeds this cap the
    // merge runs in multiple passes (merge groups of N into intermediate runs, then merge
    // those), keeping open handles bounded at the cost of extra sequential I/O.
    private const int MaxMergeFanIn = 64;

    public ExternalSortEngine(IExecutionContext context, ILogger logger)
    {
        _context = context;
        _logger = logger;
        _bufferManager = _context.ServiceProvider?.GetService<IBufferManager>();
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
    /// Spills chunks to disk and merges them back.
    /// Sort keys are stored as individual named Arrow columns (<c>_SYS_SK_0</c>, <c>_SYS_SK_1</c>, ...)
    /// so Arrow handles native type serialization without JSON encoding overhead.
    /// </summary>
    public async IAsyncEnumerable<Row> SortStreamAsync(
        IAsyncEnumerable<Row> inputStream,
        List<OrderByClause> orderBy)
    {
        using var cursor = _bufferManager != null ? await _bufferManager.AcquireCursorAsync(_context.SessionId ?? "DEFAULT", owner: this) : null;
        var chunkPaths = new List<string>();

        // Every spill file ever created (initial chunks + intermediate merge runs) so the
        // outer finally can guarantee cleanup even across multi-pass merges.
        var cleanup = new HashSet<string>();

        // Pre-build the column name array once — avoids per-row string allocation.
        var keyColumnNames = BuildKeyColumnNames(orderBy.Count);

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

            // 2. Consume stream and spill chunks. The memory guard flushes the current chunk early
            // when the accumulated chunk footprint (precise byte accounting) crosses the governor
            // ceiling, so a chunk of very wide rows can't exceed the budget even if its row count is
            // under ChunkSize.
            var currentChunk = new List<(Row Row, object?[] Keys)>();
            int chunkCounter = 0;
            var memGuard = new MemoryBudgetGuard(MemoryGovernor.Ceiling(_context));
            await foreach (var row in inputStream)
            {
                var keys = new object?[orderBy.Count];
                for (int i = 0; i < orderBy.Count; i++)
                {
                    var val = await _context.EvaluateValue(orderBy[i].Expression, row);
                    keys[i] = CompoundKey.NormalizeValue(val);
                }

                currentChunk.Add((row, keys));
                memGuard.Add(row.EstimateHeapBytes() + RowMemory.EstimateValuesBytes(keys));

                if (currentChunk.Count >= ChunkSize || (currentChunk.Count > 0 && memGuard.Exceeded()))
                {
                    currentChunk.Sort(Compare);
                    var chunkName = $"sort_chunk_{Guid.NewGuid():N}_{chunkCounter++}.tmp";
                    chunkPaths.Add(chunkName);
                    cleanup.Add(chunkName);
                    await SpillChunkAsync(chunkName, currentChunk, keyColumnNames);
                    _context.Telemetry.SortSpillCount++;
                    _context.Telemetry.PartitionsCount++;
                    currentChunk.Clear();
                    memGuard.Reset();
                }
            }

            if (currentChunk.Count > 0)
            {
                currentChunk.Sort(Compare);
                if (chunkPaths.Count == 0)
                {
                    foreach (var entry in currentChunk)
                        yield return entry.Row;

                    yield break;
                }

                var prefix = Guid.NewGuid().ToString("N");
                var chunkName = $"sort_chunk_{prefix}_{chunkCounter++}.tmp";
                chunkPaths.Add(chunkName);
                cleanup.Add(chunkName);
                await SpillChunkAsync(chunkName, currentChunk, keyColumnNames);
                _context.Telemetry.SortSpillCount++;
                _context.Telemetry.PartitionsCount++;
            }

            // 3. Bounded multi-pass k-way merge.
            if (chunkPaths.Count == 0) yield break;

            // Reduction passes: while more chunks remain than we are willing to open at once,
            // merge groups of MaxMergeFanIn into intermediate runs. Consumed inputs are deleted
            // immediately so disk usage stays bounded to roughly one extra level of spill.
            int passNo = 0;
            while (chunkPaths.Count > MaxMergeFanIn)
            {
                var nextLevel = new List<string>();
                for (int i = 0; i < chunkPaths.Count; i += MaxMergeFanIn)
                {
                    var group = chunkPaths.GetRange(i, Math.Min(MaxMergeFanIn, chunkPaths.Count - i));
                    if (group.Count == 1)
                    {
                        // A lone trailing chunk just carries forward to the next level untouched.
                        nextLevel.Add(group[0]);
                        continue;
                    }

                    var merged = $"sort_merge_p{passNo}_{Guid.NewGuid():N}_{nextLevel.Count}.tmp";
                    cleanup.Add(merged);
                    await MergeChunksToFileAsync(merged, group, keyColumnNames, Compare);
                    nextLevel.Add(merged);
                    _context.Telemetry.SortSpillCount++;

                    foreach (var consumed in group)
                    {
                        try { _context.SpillStore.DeleteChunk(consumed); cleanup.Remove(consumed); }
                        catch (Exception ex) { _logger.Warning($"Error cleaning up external sort run {consumed}: {ex.Message}"); }
                    }
                }
                chunkPaths = nextLevel;
                passNo++;
            }

            // Final pass: merge the remaining (<= MaxMergeFanIn) chunks and stream rows out.
            await foreach (var entry in MergeChunksAsync(chunkPaths, keyColumnNames, Compare))
                yield return entry.Row;
        }
        finally
        {
            foreach (var path in cleanup)
            {
                try
                {
                    _context.SpillStore.DeleteChunk(path);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error cleaning up external sort chunk {path}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// k-way merges the given spilled chunks via a min-heap, yielding rows (with their
    /// extracted sort keys) in fully sorted order. Opens one reader per input chunk — callers
    /// must keep the input count bounded (see <see cref="MaxMergeFanIn"/>).
    /// </summary>
    private async IAsyncEnumerable<(Row Row, object?[] Keys)> MergeChunksAsync(
        List<string> inputChunks,
        string[] keyColumnNames,
        Comparison<(Row Row, object?[] Keys)> compare)
    {
        var readers = new List<ISpillReader>();
        try
        {
            foreach (var path in inputChunks)
                readers.Add(await _context.SpillStore.CreateReaderAsync(path));

            var heap = new PriorityQueue<int, (Row Row, object?[] Keys)>(Comparer<(Row Row, object?[] Keys)>.Create(compare));

            for (int i = 0; i < readers.Count; i++)
            {
                var row = await readers[i].ReadRowAsync();
                if (row != null)
                {
                    var keys = ExtractAndStripKeys(row, keyColumnNames);
                    heap.Enqueue(i, (row, keys));
                }
            }

            while (heap.Count > 0)
            {
                if (heap.TryDequeue(out int chunkIdx, out var first))
                {
                    yield return first;

                    var nextRow = await readers[chunkIdx].ReadRowAsync();
                    if (nextRow != null)
                    {
                        var keys = ExtractAndStripKeys(nextRow, keyColumnNames);
                        heap.Enqueue(chunkIdx, (nextRow, keys));
                    }
                }
            }
        }
        finally
        {
            foreach (var rd in readers) await rd.DisposeAsync();
        }
    }

    /// <summary>
    /// Merges a bounded group of spilled chunks into a single new intermediate run, re-stamping
    /// the sort keys into their sentinel columns so the next merge pass can read them back.
    /// </summary>
    private async Task MergeChunksToFileAsync(
        string outName,
        List<string> inputChunks,
        string[] keyColumnNames,
        Comparison<(Row Row, object?[] Keys)> compare)
    {
        await using var writer = await _context.SpillStore.CreateWriterAsync(outName);
        await foreach (var entry in MergeChunksAsync(inputChunks, keyColumnNames, compare))
        {
            for (int k = 0; k < keyColumnNames.Length; k++)
                entry.Row[keyColumnNames[k]] = entry.Keys[k];

            await writer.WriteRowAsync(entry.Row);

            for (int k = 0; k < keyColumnNames.Length; k++)
                entry.Row.RemoveColumn(keyColumnNames[k]);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the stable array of sort-key column names once per sort operation.
    /// </summary>
    private static string[] BuildKeyColumnNames(int count)
    {
        var names = new string[count];
        for (int i = 0; i < count; i++)
            names[i] = SortKeyPrefix + i;
        return names;
    }

    /// <summary>
    /// Writes a sorted chunk to a spill file. Each sort key is written as a separate
    /// named column (<c>_SYS_SK_0</c>, <c>_SYS_SK_1</c>, ...) so Arrow stores the value
    /// in its native typed column rather than encoding it inside a JSON string.
    /// </summary>
    private async Task SpillChunkAsync(
        string chunkName,
        List<(Row Row, object?[] Keys)> chunk,
        string[] keyColumnNames)
    {
        await using var writer = await _context.SpillStore.CreateWriterAsync(chunkName);
        foreach (var entry in chunk)
        {
            // Stamp each key into its own dedicated column before writing the row.
            // These sentinel columns are stripped by ExtractAndStripKeys on the read path.
            for (int k = 0; k < keyColumnNames.Length; k++)
                entry.Row[keyColumnNames[k]] = entry.Keys[k];

            await writer.WriteRowAsync(entry.Row);

            // Clean up the sentinel columns immediately so the in-memory Row objects
            // don't accumulate extra columns if the chunk gets reused by the caller.
            for (int k = 0; k < keyColumnNames.Length; k++)
                entry.Row.RemoveColumn(keyColumnNames[k]);
        }
    }

    /// <summary>
    /// Reads the per-column sort keys back from a spilled row and removes them from
    /// the row so the caller never sees the sentinel columns in the final output.
    /// </summary>
    private static object?[] ExtractAndStripKeys(Row row, string[] keyColumnNames)
    {
        var keys = new object?[keyColumnNames.Length];
        for (int i = 0; i < keyColumnNames.Length; i++)
        {
            if (row.TryGetValue(keyColumnNames[i], out var raw))
            {
                keys[i] = SpillSerializationHelper.UnwrapValue(raw);
                row.RemoveColumn(keyColumnNames[i]);
            }
        }
        return keys;
    }
}
