using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Planning;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine.Planning;
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
    public int MaxConcurrentMergeReaders { get; private set; }
    public int MergePassCount { get; private set; }
    public int IntermediateMergeRunCount { get; private set; }
    internal long IntermediateKeyStripCount { get; private set; }
    internal long PrefixResolvedComparisons { get; private set; }

    internal readonly record struct SortEntry(Row Row, object?[] Keys, ulong KeyPrefix);

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
    public IAsyncEnumerable<Row> SortStreamAsync(
        IAsyncEnumerable<Row> inputStream,
        List<OrderByClause> orderBy,
        Func<Row, Task<object?[]>>? extractKeysAsync = null,
        CancellationToken cancellationToken = default) =>
        SortStreamCoreAsync(inputStream, orderBy, extractKeysAsync, null, null, cancellationToken);

    internal IAsyncEnumerable<Row> SortStreamWithPrefixAsync(
        IAsyncEnumerable<Row> inputStream,
        List<OrderByClause> orderBy,
        Func<Row, Task<object?[]>> extractKeysAsync,
        List<(Row Row, object?[] Keys)> prefix,
        IMemoryGrantLease prefixLease,
        CancellationToken cancellationToken = default) =>
        SortStreamCoreAsync(inputStream, orderBy, extractKeysAsync, prefix, prefixLease, cancellationToken);

    private async IAsyncEnumerable<Row> SortStreamCoreAsync(
        IAsyncEnumerable<Row> inputStream,
        List<OrderByClause> orderBy,
        Func<Row, Task<object?[]>>? extractKeysAsync,
        List<(Row Row, object?[] Keys)>? prefix,
        IMemoryGrantLease? prefixLease,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var retainedLease = prefixLease;
        using var linkedCts = PipelineCursorScope.LinkCancellationTokens(_context.CancellationToken, cancellationToken, out var effectiveCancellationToken);
        PlanDecisionRecorder.Record(_context, "external-sort", "ExternalSort",
            PlanDecisionOutcome.Accepted, PlanDecisionReasonCodes.SemanticGuard,
            "External sort path accepted.",
            ("chunkSizeRows", ChunkSize.ToString()),
            ("memoryCeilingBytes", MemoryGovernor.Ceiling(_context).ToString()));

        using var cursor = await PipelineCursorScope.AcquireCursorAsync(
            _bufferManager,
            _context.SessionId ?? "DEFAULT",
            effectiveCancellationToken,
            owner: this);
        var arbiter = _context.MemoryArbiter ?? MemoryGrantArbiter.Shared;
        var chunkLease = arbiter.AcquireLease();
        var chunkPaths = new List<string>();

        // Every spill file ever created (initial chunks + intermediate merge runs) so the
        // outer finally can guarantee cleanup even across multi-pass merges.
        var cleanup = new HashSet<string>();

        // Pre-build the column name array once — avoids per-row string allocation.
        var keyColumnNames = BuildKeyColumnNames(orderBy.Count);

        try
        {
            // 1. Comparison function: resolve distinct primary keys in O(1) via their normalized
            //    64-bit prefix before falling back to full CompareConstants / tie-breaker columns.
            int Compare(SortEntry a, SortEntry b)
            {
                if (a.KeyPrefix != b.KeyPrefix)
                {
                    PrefixResolvedComparisons++;
                    return a.KeyPrefix < b.KeyPrefix ? -1 : 1;
                }

                for (int i = 0; i < orderBy.Count; i++)
                {
                    var res = orderBy[i].OutputOrder(_context.CompareConstants(a.Keys[i], b.Keys[i]), a.Keys[i], b.Keys[i]);
                    if (res != 0) return res;
                }
                return 0;
            }

            // 2. Consume stream and spill chunks. The memory guard flushes the current chunk early
            // when the accumulated chunk footprint (precise byte accounting) crosses the governor
            // ceiling, so a chunk of very wide rows can't exceed the budget even if its row count is
            // under ChunkSize.
            bool caseSensitive = _context.CaseSensitiveComparison;
            var currentChunk = new List<SortEntry>(prefix?.Count ?? 0);
            int chunkCounter = 0;
            var memGuard = new MemoryBudgetGuard(MemoryGovernor.Ceiling(_context));

            async Task SpillCurrentChunkAsync()
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
                chunkLease.Dispose();
                chunkLease = arbiter.AcquireLease();
            }

            // The admitted Top-N prefix already owns its grant. Spill it before reading
            // more input, preserving the evaluated keys and releasing its row references.
            if (prefix is { Count: > 0 })
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                foreach (var entry in prefix)
                {
                    for (int i = 0; i < entry.Keys.Length; i++)
                        entry.Keys[i] = CompoundKey.NormalizeValue(entry.Keys[i]);
                    currentChunk.Add(new SortEntry(entry.Row, entry.Keys, ComputeKeyPrefix(entry.Keys, orderBy, caseSensitive)));
                }
                await SpillCurrentChunkAsync();
                retainedLease?.Dispose();
            }
            await foreach (var row in inputStream.WithCancellation(effectiveCancellationToken))
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                var keys = new object?[orderBy.Count];
                var extracted = extractKeysAsync != null ? await extractKeysAsync(row) : null;
                for (int i = 0; i < orderBy.Count; i++)
                {
                    var val = extracted != null ? extracted[i] : await _context.EvaluateValue(orderBy[i].Expression, row);
                    keys[i] = CompoundKey.NormalizeValue(val);
                }

                currentChunk.Add(new SortEntry(row, keys, ComputeKeyPrefix(keys, orderBy, caseSensitive)));
                memGuard.Add(row.EstimateHeapBytes() + RowMemory.EstimateValuesBytes(keys));
                bool spillForArbiter = currentChunk.Count > 0 && chunkLease.RegisterAndCheckSpill(memGuard.BytesAccumulated);

                if (currentChunk.Count >= ChunkSize || (currentChunk.Count > 0 && (memGuard.Exceeded() || spillForArbiter)))
                {
                    await SpillCurrentChunkAsync();
                }
            }

            if (currentChunk.Count > 0)
            {
                currentChunk.Sort(Compare);
                if (chunkPaths.Count == 0)
                {
                    foreach (var entry in currentChunk)
                    {
                        effectiveCancellationToken.ThrowIfCancellationRequested();
                        yield return entry.Row;
                    }

                    yield break;
                }

                var chunkName = $"sort_chunk_{Guid.NewGuid():N}_{chunkCounter++}.tmp";
                chunkPaths.Add(chunkName);
                cleanup.Add(chunkName);
                await SpillChunkAsync(chunkName, currentChunk, keyColumnNames);
                _context.Telemetry.SortSpillCount++;
                _context.Telemetry.PartitionsCount++;
                currentChunk.Clear();
            }

            chunkLease.Dispose();

            // 3. Bounded multi-pass k-way merge.
            if (chunkPaths.Count == 0) yield break;

            // Reduction passes: while more chunks remain than we are willing to open at once,
            // merge groups of MaxMergeFanIn into intermediate runs. Consumed inputs are deleted
            // immediately so disk usage stays bounded to roughly one extra level of spill.
            int passNo = 0;
            while (chunkPaths.Count > MaxMergeFanIn)
            {
                MergePassCount++;
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
                    await MergeChunksToFileAsync(merged, group, keyColumnNames, orderBy, Compare);
                    nextLevel.Add(merged);
                    _context.Telemetry.SortSpillCount++;
                    IntermediateMergeRunCount++;

                    foreach (var consumed in group)
                    {
                        try { _context.SpillStore.DeleteChunk(consumed); cleanup.Remove(consumed); }
                        catch (Exception ex) { _logger.Warning($"Error cleaning up external sort run {consumed}: {ex.Message}"); }
                    }
                }
                chunkPaths = nextLevel;
                passNo++;
            }

            // Final pass: merge the remaining (<= MaxMergeFanIn) chunks and strip sentinel keys.
            MergePassCount++;
            await foreach (var entry in MergeChunksAsync(chunkPaths, keyColumnNames, orderBy, Compare, stripKeys: true).WithCancellation(effectiveCancellationToken))
                yield return entry.Row;
        }
        finally
        {
            prefix?.Clear();
            chunkLease.Dispose();
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
    private async IAsyncEnumerable<SortEntry> MergeChunksAsync(
        List<string> inputChunks,
        string[] keyColumnNames,
        List<OrderByClause> orderBy,
        Comparison<SortEntry> compare,
        bool stripKeys = true)
    {
        var readers = new List<ISpillReader>();
        bool caseSensitive = _context.CaseSensitiveComparison;
        Dictionary<TableSchema, (TableSchema? Stripped, int[] KeptIndices)>? strippedSchemas = stripKeys ? new() : null;
        try
        {
            foreach (var path in inputChunks)
                readers.Add(await _context.SpillStore.CreateReaderAsync(path));
            MaxConcurrentMergeReaders = Math.Max(MaxConcurrentMergeReaders, readers.Count);

            var heap = new PriorityQueue<int, SortEntry>(Comparer<SortEntry>.Create(compare));

            for (int i = 0; i < readers.Count; i++)
            {
                var row = await readers[i].ReadRowAsync();
                if (row != null)
                {
                    var keys = ExtractKeys(row, keyColumnNames);
                    if (strippedSchemas != null)
                        row = StripFinalSentinelColumns(row, keyColumnNames, strippedSchemas);
                    heap.Enqueue(i, new SortEntry(row, keys, ComputeKeyPrefix(keys, orderBy, caseSensitive)));
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
                        var keys = ExtractKeys(nextRow, keyColumnNames);
                        if (strippedSchemas != null)
                            nextRow = StripFinalSentinelColumns(nextRow, keyColumnNames, strippedSchemas);
                        heap.Enqueue(chunkIdx, new SortEntry(nextRow, keys, ComputeKeyPrefix(keys, orderBy, caseSensitive)));
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
    /// Merges a bounded group of spilled chunks into a single new intermediate run while leaving
    /// the <c>_SYS_SK_*</c> sentinel columns attached so the final pass can read and strip them once.
    /// </summary>
    private async Task MergeChunksToFileAsync(
        string outName,
        List<string> inputChunks,
        string[] keyColumnNames,
        List<OrderByClause> orderBy,
        Comparison<SortEntry> compare)
    {
        await using var writer = await _context.SpillStore.CreateWriterAsync(outName);
        await foreach (var entry in MergeChunksAsync(inputChunks, keyColumnNames, orderBy, compare, stripKeys: false))
        {
            await writer.WriteRowAsync(entry.Row);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes an order-preserving 64-bit sort prefix for the primary <c>ORDER BY</c> key so
    /// chunk sorting and tournament-tree sift comparisons avoid dynamic constant comparison when
    /// prefixes differ. Equal prefixes always fall back to <see cref="IExecutionContext.CompareConstants"/>.
    /// </summary>
    internal static ulong ComputeKeyPrefix(object?[] keys, IReadOnlyList<OrderByClause> orderBy, bool caseSensitive)
    {
        if (orderBy.Count == 0 || keys.Length == 0) return 0UL;

        var clause = orderBy[0];
        var key0 = keys[0];
        if (key0 is null or DBNull)
            return clause.PlacesNullsFirst ? 0UL : ulong.MaxValue;

        const long minIntBucket = -(1L << 57);
        const long maxIntBucket = (1L << 57) - 1L;
        ulong ascRank;

        if (key0 is byte[] bytes)
        {
            ulong payload = 0UL;
            int count = Math.Min(bytes.Length, 6);
            for (int k = 0; k < count; k++)
                payload |= ((ulong)bytes[k] + 1UL) << ((5 - k) * 9);
            ascRank = (4UL << 60) | payload;
        }
        else if (TryOrderAsNumber(key0, out decimal dec))
        {
            decimal floored = decimal.Floor(dec);
            ulong payload;
            if (floored < minIntBucket)
            {
                payload = 0UL;
            }
            else if (floored > maxIntBucket)
            {
                payload = ((1UL << 58) + 1UL) << 1;
            }
            else
            {
                long f = (long)floored;
                ulong biased = (ulong)(f - minIntBucket) + 1UL;
                ulong fracBit = dec > floored ? 1UL : 0UL;
                payload = (biased << 1) | fracBit;
            }
            ascRank = (1UL << 60) | payload;
        }
        else if (TryOrderAsDate(key0, out DateTime dt))
        {
            ulong ticksPrefix = (ulong)Math.Max(0L, dt.Ticks) >> 3;
            ascRank = (2UL << 60) | ticksPrefix;
        }
        else
        {
            string text = key0.ToString() ?? string.Empty;
            ulong payload = 0UL;
            int count = Math.Min(text.Length, 3);
            for (int k = 0; k < count; k++)
            {
                char ch = text[k];
                int shift = (2 - k) * 17;
                if (caseSensitive)
                {
                    payload |= ((ulong)ch + 1UL) << shift;
                }
                else
                {
                    // Surrogates and above-surrogate BMP code units tie on prefix so OrdinalIgnoreCase tie-breaks.
                    if (ch >= 0xD800)
                    {
                        payload |= (0xD800UL + 1UL) << shift;
                        break;
                    }
                    char upper = char.ToUpperInvariant(ch);
                    if (upper >= 0xD800)
                    {
                        payload |= (0xD800UL + 1UL) << shift;
                        break;
                    }
                    payload |= ((ulong)upper + 1UL) << shift;
                }
            }
            ascRank = (3UL << 60) | payload;
        }

        return clause.Descending ? (ulong.MaxValue - 1UL - ascRank) : ascRank;
    }

    private static bool TryOrderAsNumber(object value, out decimal number)
    {
        if (value is decimal d) { number = d; return true; }
        if (value is long l) { number = l; return true; }
        if (value is int i) { number = i; return true; }
        number = 0;
        if (value is string text && !HasDigit(text)) return false;
        return decimal.TryParse(
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out number);
    }

    private static bool TryOrderAsDate(object value, out DateTime date)
    {
        if (value is string text && !HasDigit(text))
        {
            date = default;
            return false;
        }
        return EvaluationUtils.TryToDateTime(value, out date);
    }

    private static bool HasDigit(string text)
    {
        foreach (var c in text)
            if (c is >= '0' and <= '9') return true;
        return false;
    }

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
        List<SortEntry> chunk,
        string[] keyColumnNames)
    {
        await using var writer = await _context.SpillStore.CreateWriterAsync(chunkName);
        await writer.WriteRowsAsync(StampSortKeys(chunk, keyColumnNames));
    }

    private static IEnumerable<Row> StampSortKeys(
        IEnumerable<SortEntry> rows,
        string[] keyColumnNames)
    {
        foreach (var entry in rows)
        {
            // Stamp each key into its own dedicated column before writing the row.
            // These sentinel columns stay intact through intermediate passes and are
            // stripped only on the final merge pass.
            for (int k = 0; k < keyColumnNames.Length; k++)
                entry.Row[keyColumnNames[k]] = entry.Keys[k];

            try
            {
                yield return entry.Row;
            }
            finally
            {
                // The spill writer snapshots each yielded row before advancing the iterator.
                // Remove sentinels as soon as it advances, including when a write fails.
                for (int k = 0; k < keyColumnNames.Length; k++)
                    entry.Row.RemoveColumn(keyColumnNames[k]);
            }
        }
    }

    /// <summary>
    /// Reads the per-column sort keys back from a spilled row without removing columns during intermediate passes.
    /// </summary>
    private static object?[] ExtractKeys(Row row, string[] keyColumnNames)
    {
        var keys = new object?[keyColumnNames.Length];
        for (int i = 0; i < keyColumnNames.Length; i++)
        {
            if (row.TryGetValue(keyColumnNames[i], out var raw))
                keys[i] = CompoundKey.NormalizeValue(SpillSerializationHelper.UnwrapValue(raw));
        }
        return keys;
    }

    /// <summary>
    /// Removes <c>_SYS_SK_*</c> sentinel columns from output rows on the final pass without mutating
    /// the spill reader's shared schema or forcing per-row dictionary materialization.
    /// </summary>
    private static Row StripFinalSentinelColumns(
        Row row,
        string[] keyColumnNames,
        Dictionary<TableSchema, (TableSchema? Stripped, int[] KeptIndices)> schemaCache)
    {
        if (row.HasDynamicColumns)
        {
            for (int i = 0; i < keyColumnNames.Length; i++)
                row.RemoveColumn(keyColumnNames[i]);
        }

        var schema = row.Schema;
        if (schema == null) return row;

        if (!schemaCache.TryGetValue(schema, out var plan))
        {
            var kept = new List<int>(schema.ColumnCount);
            var keptNames = new List<string>(schema.ColumnCount);
            for (int i = 0; i < schema.ColumnCount; i++)
            {
                string name = schema.GetName(i);
                bool isSentinel = false;
                for (int k = 0; k < keyColumnNames.Length; k++)
                {
                    if (string.Equals(name, keyColumnNames[k], StringComparison.OrdinalIgnoreCase))
                    {
                        isSentinel = true;
                        break;
                    }
                }
                if (!isSentinel)
                {
                    kept.Add(i);
                    keptNames.Add(name);
                }
            }

            if (kept.Count == schema.ColumnCount)
            {
                plan = (null, Array.Empty<int>());
            }
            else
            {
                var stripped = new TableSchema(keptNames, schema.Constraints);
                schema.CopyAliasesTo(stripped);
                plan = (stripped, kept.ToArray());
            }
            schemaCache[schema] = plan;
        }

        if (plan.Stripped == null) return row;

        var values = new object?[plan.KeptIndices.Length];
        for (int i = 0; i < plan.KeptIndices.Length; i++)
            values[i] = row[plan.KeptIndices[i]];
        var result = new Row(plan.Stripped, values)
        {
            MayLackColumns = row.MayLackColumns,
            DataQualityReplayProvenance = row.DataQualityReplayProvenance,
        };
        if (row.HasDynamicColumns)
        {
            foreach (var col in row.GetColumnNames())
            {
                if (schema.GetIndex(col) < 0)
                    result[col] = row[col];
            }
        }
        return result;
    }
}
