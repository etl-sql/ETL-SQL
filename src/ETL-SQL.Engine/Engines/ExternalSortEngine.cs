using System;
using System.Collections.Generic;
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
using ETL_SQL.Engine.Services;
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

    internal readonly record struct SortEntry(Row Row, SortKey Key0, int ExtraKeyOffset, ulong KeyPrefix);

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
        SortStreamCoreAsync(
            inputStream,
            orderBy,
            extractKeysAsync != null ? row => new ValueTask<object?[]>(extractKeysAsync(row)) : null,
            null,
            null,
            cancellationToken);

    internal IAsyncEnumerable<Row> SortStreamWithPrefixAsync(
        IAsyncEnumerable<Row> inputStream,
        List<OrderByClause> orderBy,
        Func<Row, ValueTask<object?[]>> extractKeysAsync,
        List<(Row Row, object?[] Keys)> prefix,
        IMemoryGrantLease prefixLease,
        CancellationToken cancellationToken = default) =>
        SortStreamCoreAsync(inputStream, orderBy, extractKeysAsync, prefix, prefixLease, cancellationToken);

    private async IAsyncEnumerable<Row> SortStreamCoreAsync(
        IAsyncEnumerable<Row> inputStream,
        List<OrderByClause> orderBy,
        Func<Row, ValueTask<object?[]>>? extractKeysAsync,
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
            bool caseSensitive = _context.CaseSensitiveComparison;
            int keyCount = orderBy.Count;
            int extraCount = Math.Max(0, keyCount - 1);
            var inputExtraKeys = extraCount > 0 ? new List<SortKey>() : null;
            var inputComparer = new SortEntryComparer(this, orderBy, caseSensitive, inputExtraKeys);
            var keySlots = extractKeysAsync == null && keyCount > 0 ? BuildKeySlotEvaluators(orderBy) : null;
            bool allKeysSync = keySlots != null && Array.TrueForAll(keySlots, static s => s.CanEvaluateSynchronously);

            // 2. Consume stream and spill chunks. The memory guard flushes the current chunk early
            // when the accumulated chunk footprint (precise byte accounting) crosses the governor
            // ceiling, so a chunk of very wide rows can't exceed the budget even if its row count is
            // under ChunkSize.
            var currentChunk = new List<SortEntry>(prefix?.Count ?? 0);
            int chunkCounter = 0;
            var memGuard = new MemoryBudgetGuard(MemoryGovernor.Ceiling(_context));

            async Task SpillCurrentChunkAsync()
            {
                currentChunk.Sort(inputComparer);
                var chunkName = $"sort_chunk_{Guid.NewGuid():N}_{chunkCounter++}.tmp";
                chunkPaths.Add(chunkName);
                cleanup.Add(chunkName);
                await SpillChunkAsync(chunkName, currentChunk, keyColumnNames, inputExtraKeys);
                _context.Telemetry.SortSpillCount++;
                _context.Telemetry.PartitionsCount++;
                currentChunk.Clear();
                inputExtraKeys?.Clear();
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
                    var key0 = keyCount > 0 ? SortKey.FromValue(entry.Keys[0]) : SortKey.Null;
                    int extraOffset = -1;
                    if (extraCount > 0)
                    {
                        extraOffset = inputExtraKeys!.Count;
                        for (int i = 1; i < keyCount; i++)
                            inputExtraKeys.Add(SortKey.FromValue(entry.Keys[i]));
                    }
                    ulong keyPrefix = keyCount > 0 ? ComputeKeyPrefix(key0, orderBy[0], caseSensitive) : 0UL;
                    currentChunk.Add(new SortEntry(entry.Row, key0, extraOffset, keyPrefix));
                }
                await SpillCurrentChunkAsync();
                retainedLease?.Dispose();
            }

            await foreach (var row in inputStream.WithCancellation(effectiveCancellationToken))
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                SortKey key0 = SortKey.Null;
                int extraOffset = -1;
                long keyHeapBytes = 0;

                if (keyCount > 0)
                {
                    if (extractKeysAsync != null)
                    {
                        var extracted = await extractKeysAsync(row);
                        key0 = SortKey.FromValue(extracted[0]);
                        keyHeapBytes = key0.EstimateHeapBytes();
                        if (extraCount > 0)
                        {
                            extraOffset = inputExtraKeys!.Count;
                            for (int i = 1; i < keyCount; i++)
                            {
                                var k = SortKey.FromValue(extracted[i]);
                                inputExtraKeys.Add(k);
                                keyHeapBytes += k.EstimateHeapBytes();
                            }
                        }
                    }
                    else if (allKeysSync)
                    {
                        key0 = SortKey.FromValue(keySlots![0].EvaluateSync(row));
                        keyHeapBytes = key0.EstimateHeapBytes();
                        if (extraCount > 0)
                        {
                            extraOffset = inputExtraKeys!.Count;
                            for (int i = 1; i < keyCount; i++)
                            {
                                var k = SortKey.FromValue(keySlots[i].EvaluateSync(row));
                                inputExtraKeys.Add(k);
                                keyHeapBytes += k.EstimateHeapBytes();
                            }
                        }
                    }
                    else
                    {
                        key0 = SortKey.FromValue(await keySlots![0].EvaluateAsync(_context, row));
                        keyHeapBytes = key0.EstimateHeapBytes();
                        if (extraCount > 0)
                        {
                            extraOffset = inputExtraKeys!.Count;
                            for (int i = 1; i < keyCount; i++)
                            {
                                var k = SortKey.FromValue(await keySlots[i].EvaluateAsync(_context, row));
                                inputExtraKeys.Add(k);
                                keyHeapBytes += k.EstimateHeapBytes();
                            }
                        }
                    }
                }

                ulong keyPrefix = keyCount > 0 ? ComputeKeyPrefix(key0, orderBy[0], caseSensitive) : 0UL;
                currentChunk.Add(new SortEntry(row, key0, extraOffset, keyPrefix));
                memGuard.Add(row.EstimateHeapBytes() + 64L + (extraCount * 40L) + keyHeapBytes);
                bool spillForArbiter = currentChunk.Count > 0 && chunkLease.RegisterAndCheckSpill(memGuard.BytesAccumulated);

                if (currentChunk.Count >= ChunkSize || (currentChunk.Count > 0 && (memGuard.Exceeded() || spillForArbiter)))
                {
                    await SpillCurrentChunkAsync();
                }
            }

            if (currentChunk.Count > 0)
            {
                currentChunk.Sort(inputComparer);
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
                await SpillChunkAsync(chunkName, currentChunk, keyColumnNames, inputExtraKeys);
                _context.Telemetry.SortSpillCount++;
                _context.Telemetry.PartitionsCount++;
                currentChunk.Clear();
                inputExtraKeys?.Clear();
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
                    await MergeChunksToFileAsync(merged, group, keyColumnNames, orderBy);
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
            await foreach (var entry in MergeChunksAsync(chunkPaths, keyColumnNames, orderBy, stripKeys: true).WithCancellation(effectiveCancellationToken))
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
        bool stripKeys = true)
    {
        var readers = new List<ISpillReader>(inputChunks.Count);
        bool caseSensitive = _context.CaseSensitiveComparison;
        int keyCount = keyColumnNames.Length;
        int extraCount = Math.Max(0, keyCount - 1);
        var mergeExtraKeys = extraCount > 0 ? new SortKey[inputChunks.Count * extraCount] : null;
        var mergeComparer = new SortEntryComparer(this, orderBy, caseSensitive, mergeExtraKeys);
        Dictionary<TableSchema, (TableSchema? Stripped, int[] KeptIndices)>? strippedSchemas = stripKeys ? new() : null;
        try
        {
            foreach (var path in inputChunks)
                readers.Add(await _context.SpillStore.CreateReaderAsync(path));
            MaxConcurrentMergeReaders = Math.Max(MaxConcurrentMergeReaders, readers.Count);

            var heap = new PriorityQueue<int, SortEntry>(mergeComparer);

            for (int i = 0; i < readers.Count; i++)
            {
                var entry = await ReadNextSortEntryAsync(
                    readers[i], i, keyColumnNames, orderBy, caseSensitive, stripKeys, strippedSchemas, mergeExtraKeys, extraCount);
                if (entry.HasValue)
                    heap.Enqueue(i, entry.Value);
            }

            while (heap.Count > 0)
            {
                if (heap.TryDequeue(out int chunkIdx, out var first))
                {
                    yield return first;

                    var reader = readers[chunkIdx];
                    int extraOffset = extraCount > 0 ? chunkIdx * extraCount : -1;
                    if (reader is ISortSpillReader { SupportsSortFastPath: true } fastReader)
                    {
                        if (fastReader.TryReadBufferedSortRow(
                                keyColumnNames,
                                stripKeys,
                                out var nextRow,
                                out var key0,
                                extraCount > 0 ? mergeExtraKeys.AsSpan(extraOffset, extraCount) : Span<SortKey>.Empty))
                        {
                            ulong prefix = keyCount > 0 ? ComputeKeyPrefix(key0, orderBy[0], caseSensitive) : 0UL;
                            heap.Enqueue(chunkIdx, new SortEntry(nextRow, key0, extraOffset, prefix));
                        }
                        else
                        {
                            var next = await ReadSlowFastReaderAsync(
                                fastReader, extraOffset, keyColumnNames, orderBy, caseSensitive, stripKeys, mergeExtraKeys, extraCount);
                            if (next.HasValue)
                                heap.Enqueue(chunkIdx, next.Value);
                        }
                    }
                    else
                    {
                        var next = await ReadNextSortEntryAsync(
                            reader, chunkIdx, keyColumnNames, orderBy, caseSensitive, stripKeys, strippedSchemas, mergeExtraKeys, extraCount);
                        if (next.HasValue)
                            heap.Enqueue(chunkIdx, next.Value);
                    }
                }
            }
        }
        finally
        {
            foreach (var rd in readers) await rd.DisposeAsync();
        }
    }

    private static async ValueTask<SortEntry?> ReadSlowFastReaderAsync(
        ISortSpillReader fastReader,
        int extraOffset,
        string[] keyColumnNames,
        List<OrderByClause> orderBy,
        bool caseSensitive,
        bool stripKeys,
        SortKey[]? mergeExtraKeys,
        int extraCount)
    {
        if (!await fastReader.MoveToNextBatchAsync())
            return null;

        if (!fastReader.TryReadBufferedSortRow(
                keyColumnNames,
                stripKeys,
                out var row,
                out var key0,
                extraCount > 0 ? mergeExtraKeys.AsSpan(extraOffset, extraCount) : Span<SortKey>.Empty))
        {
            return null;
        }

        ulong prefix = keyColumnNames.Length > 0 ? ComputeKeyPrefix(key0, orderBy[0], caseSensitive) : 0UL;
        return new SortEntry(row, key0, extraOffset, prefix);
    }

    private static async ValueTask<SortEntry?> ReadNextSortEntryAsync(
        ISpillReader reader,
        int chunkIdx,
        string[] keyColumnNames,
        List<OrderByClause> orderBy,
        bool caseSensitive,
        bool stripKeys,
        Dictionary<TableSchema, (TableSchema? Stripped, int[] KeptIndices)>? strippedSchemas,
        SortKey[]? mergeExtraKeys,
        int extraCount)
    {
        int extraOffset = extraCount > 0 ? chunkIdx * extraCount : -1;
        if (reader is ISortSpillReader { SupportsSortFastPath: true } fastReader)
        {
            if (fastReader.TryReadBufferedSortRow(
                    keyColumnNames,
                    stripKeys,
                    out var fastRow,
                    out var key0,
                    extraCount > 0 ? mergeExtraKeys.AsSpan(extraOffset, extraCount) : Span<SortKey>.Empty))
            {
                ulong prefix = keyColumnNames.Length > 0 ? ComputeKeyPrefix(key0, orderBy[0], caseSensitive) : 0UL;
                return new SortEntry(fastRow, key0, extraOffset, prefix);
            }

            return await ReadSlowFastReaderAsync(
                fastReader, extraOffset, keyColumnNames, orderBy, caseSensitive, stripKeys, mergeExtraKeys, extraCount);
        }

        var row = reader is ISortSpillReader vtReader
            ? await vtReader.ReadRowValueTaskAsync()
            : await reader.ReadRowAsync();
        if (row == null) return null;

        var fallbackKey0 = ExtractKeysFromRow(row, keyColumnNames, mergeExtraKeys, extraOffset);
        if (strippedSchemas != null)
            row = StripFinalSentinelColumns(row, keyColumnNames, strippedSchemas);
        ulong fallbackPrefix = keyColumnNames.Length > 0 ? ComputeKeyPrefix(fallbackKey0, orderBy[0], caseSensitive) : 0UL;
        return new SortEntry(row, fallbackKey0, extraOffset, fallbackPrefix);
    }

    /// <summary>
    /// Merges a bounded group of spilled chunks into a single new intermediate run while leaving
    /// the <c>_SYS_SK_*</c> sentinel columns attached so the final pass can read and strip them once.
    /// </summary>
    private async Task MergeChunksToFileAsync(
        string outName,
        List<string> inputChunks,
        string[] keyColumnNames,
        List<OrderByClause> orderBy)
    {
        await using var writer = await _context.SpillStore.CreateWriterAsync(outName);
        await foreach (var entry in MergeChunksAsync(inputChunks, keyColumnNames, orderBy, stripKeys: false))
        {
            await writer.WriteRowAsync(entry.Row);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private KeySlotEvaluator[] BuildKeySlotEvaluators(List<OrderByClause> orderBy)
    {
        var slots = new KeySlotEvaluator[orderBy.Count];
        for (int i = 0; i < orderBy.Count; i++)
            slots[i] = new KeySlotEvaluator(_context, orderBy[i].Expression);
        return slots;
    }

    private sealed class KeySlotEvaluator
    {
        private readonly string? _directName;
        private readonly RowExpressionCompiler.RowValue? _compiled;
        private readonly Expression _expression;
        private TableSchema? _cachedSchema;
        private int _cachedIndex = -1;

        public KeySlotEvaluator(IExecutionContext context, Expression expression)
        {
            _expression = expression;
            if (expression is IdentifierExpression id
                && !id.Name.StartsWith("@", StringComparison.Ordinal)
                && !id.Name.Equals("*", StringComparison.OrdinalIgnoreCase)
                && !id.Name.EndsWith(".CONNECTION_STRING", StringComparison.OrdinalIgnoreCase)
                && !context.Connections.ContainsKey(id.Name)
                && !context.FunctionRegistry.IsRegistered(id.Name)
                && context.OuterRowStack.Count == 0)
            {
                _directName = id.Name;
            }
            if (RowExpressionCompiler.TryCompileValue(context, expression, out var compiled))
                _compiled = compiled;
        }

        public bool CanEvaluateSynchronously => _compiled != null;

        public object? EvaluateSync(Row row)
        {
            if (_directName != null)
            {
                var schema = row.Schema;
                if (schema != null)
                {
                    if (!ReferenceEquals(schema, _cachedSchema))
                    {
                        _cachedSchema = schema;
                        _cachedIndex = schema.GetIndex(_directName);
                    }
                    if (_cachedIndex >= 0)
                        return row[_cachedIndex];
                }
                if (row.TryGetValue(_directName, out var val))
                    return val;
            }
            return _compiled!(row);
        }

        public ValueTask<object?> EvaluateAsync(IExecutionContext context, Row row)
        {
            if (_compiled != null)
                return new ValueTask<object?>(EvaluateSync(row));
            return context.EvaluateValue(_expression, row);
        }
    }

    private sealed class SortEntryComparer : IComparer<SortEntry>
    {
        private readonly ExternalSortEngine _engine;
        private readonly List<OrderByClause> _orderBy;
        private readonly bool _caseSensitive;
        private readonly IReadOnlyList<SortKey>? _extraKeys;

        public SortEntryComparer(
            ExternalSortEngine engine,
            List<OrderByClause> orderBy,
            bool caseSensitive,
            IReadOnlyList<SortKey>? extraKeys)
        {
            _engine = engine;
            _orderBy = orderBy;
            _caseSensitive = caseSensitive;
            _extraKeys = extraKeys;
        }

        public int Compare(SortEntry a, SortEntry b)
        {
            if (a.KeyPrefix != b.KeyPrefix)
            {
                _engine.PrefixResolvedComparisons++;
                return a.KeyPrefix < b.KeyPrefix ? -1 : 1;
            }

            if (_orderBy.Count == 0) return 0;

            int res = SortKey.Compare(a.Key0, b.Key0, _orderBy[0], _caseSensitive);
            if (res != 0) return res;

            for (int i = 1; i < _orderBy.Count; i++)
            {
                var ka = _extraKeys![a.ExtraKeyOffset + (i - 1)];
                var kb = _extraKeys[b.ExtraKeyOffset + (i - 1)];
                res = SortKey.Compare(ka, kb, _orderBy[i], _caseSensitive);
                if (res != 0) return res;
            }
            return 0;
        }
    }

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

        ulong ascRank = ComputeAscRankFromObject(key0, caseSensitive);
        return clause.Descending ? (ulong.MaxValue - 1UL - ascRank) : ascRank;
    }

    internal static ulong ComputeKeyPrefix(in SortKey key0, OrderByClause clause, bool caseSensitive)
    {
        if (key0.IsNull)
            return clause.PlacesNullsFirst ? 0UL : ulong.MaxValue;

        ulong ascRank = key0.Kind switch
        {
            SortKeyKind.Bytes => PrefixFromBytes(key0.BytesValue!),
            SortKeyKind.Decimal => PrefixFromDecimal(key0.DecimalValue),
            SortKeyKind.DateTime => PrefixFromDateTime(key0.DateTimeValue),
            SortKeyKind.String when !SortKey.HasDigit(key0.StringValue!) => PrefixFromText(key0.StringValue!, caseSensitive),
            SortKeyKind.Boolean => PrefixFromText(key0.BooleanValue ? bool.TrueString : bool.FalseString, caseSensitive),
            _ => ComputeAscRankFromObject(key0.ToObject()!, caseSensitive),
        };

        return clause.Descending ? (ulong.MaxValue - 1UL - ascRank) : ascRank;
    }

    private static ulong ComputeAscRankFromObject(object key0, bool caseSensitive)
    {
        if (key0 is byte[] bytes)
            return PrefixFromBytes(bytes);
        if (TryOrderAsNumber(key0, out decimal dec))
            return PrefixFromDecimal(dec);
        if (TryOrderAsDate(key0, out DateTime dt))
            return PrefixFromDateTime(dt);
        return PrefixFromText(key0.ToString() ?? string.Empty, caseSensitive);
    }

    private static ulong PrefixFromBytes(byte[] bytes)
    {
        ulong payload = 0UL;
        int count = Math.Min(bytes.Length, 6);
        for (int k = 0; k < count; k++)
            payload |= ((ulong)bytes[k] + 1UL) << ((5 - k) * 9);
        return (4UL << 60) | payload;
    }

    private static ulong PrefixFromDecimal(decimal dec)
    {
        const long minIntBucket = -(1L << 57);
        const long maxIntBucket = (1L << 57) - 1L;
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
        return (1UL << 60) | payload;
    }

    private static ulong PrefixFromDateTime(DateTime dt)
    {
        ulong ticksPrefix = (ulong)Math.Max(0L, dt.Ticks) >> 3;
        return (2UL << 60) | ticksPrefix;
    }

    private static ulong PrefixFromText(string text, bool caseSensitive)
    {
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
        return (3UL << 60) | payload;
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
        string[] keyColumnNames,
        List<SortKey>? extraKeys)
    {
        await using var writer = await _context.SpillStore.CreateWriterAsync(chunkName);
        await writer.WriteRowsAsync(StampSortKeys(chunk, keyColumnNames, extraKeys));
    }

    private static IEnumerable<Row> StampSortKeys(
        IEnumerable<SortEntry> rows,
        string[] keyColumnNames,
        List<SortKey>? extraKeys)
    {
        // Rows held by a schema are stamped into a copy over that schema plus the key columns,
        // built once per input schema: one values array per row instead of adding (and then
        // removing) dynamic dictionary columns on the caller's row.
        TableSchema? stampSource = null;
        TableSchema? stamped = null;
        foreach (var entry in rows)
        {
            var schema = entry.Row.Schema;
            if (schema != null && !entry.Row.HasDynamicColumns)
            {
                if (!ReferenceEquals(schema, stampSource))
                {
                    stamped = null;
                    var names = new List<string>(schema.ColumnCount + keyColumnNames.Length);
                    for (int i = 0; i < schema.ColumnCount; i++) names.Add(schema.GetName(i));
                    var clash = false;
                    foreach (var key in keyColumnNames)
                    {
                        if (schema.GetIndex(key) >= 0) { clash = true; break; }
                        names.Add(key);
                    }
                    if (!clash)
                    {
                        stamped = new TableSchema(names);
                        schema.CopyAliasesTo(stamped);
                    }
                    stampSource = schema;
                }

                if (stamped != null)
                {
                    int canonical = schema.ColumnCount;
                    var values = new object?[canonical + keyColumnNames.Length];
                    for (int i = 0; i < canonical; i++) values[i] = entry.Row[i];
                    if (keyColumnNames.Length > 0)
                    {
                        values[canonical] = entry.Key0.ToObject();
                        for (int k = 1; k < keyColumnNames.Length; k++)
                            values[canonical + k] = extraKeys![entry.ExtraKeyOffset + (k - 1)].ToObject();
                    }
                    yield return new Row(stamped, values) { MayLackColumns = entry.Row.MayLackColumns };
                    continue;
                }
            }

            // Stamp each key into its own dedicated column before writing the row.
            // These sentinel columns stay intact through intermediate passes and are
            // stripped only on the final merge pass.
            if (keyColumnNames.Length > 0)
            {
                entry.Row[keyColumnNames[0]] = entry.Key0.ToObject();
                for (int k = 1; k < keyColumnNames.Length; k++)
                    entry.Row[keyColumnNames[k]] = extraKeys![entry.ExtraKeyOffset + (k - 1)].ToObject();
            }

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
    private static SortKey ExtractKeysFromRow(
        Row row,
        string[] keyColumnNames,
        SortKey[]? mergeExtraKeys,
        int extraOffset)
    {
        if (keyColumnNames.Length == 0) return SortKey.Null;

        SortKey key0 = row.TryGetValue(keyColumnNames[0], out var raw0)
            ? SortKey.FromValue(SpillSerializationHelper.UnwrapValue(raw0))
            : SortKey.Null;

        for (int i = 1; i < keyColumnNames.Length; i++)
        {
            mergeExtraKeys![extraOffset + (i - 1)] = row.TryGetValue(keyColumnNames[i], out var raw)
                ? SortKey.FromValue(SpillSerializationHelper.UnwrapValue(raw))
                : SortKey.Null;
        }
        return key0;
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
