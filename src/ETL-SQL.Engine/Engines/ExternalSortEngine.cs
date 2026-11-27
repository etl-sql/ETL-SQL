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
    internal int LastSpilledSentinelColumnCount { get; private set; }
    internal long StampedSpillRowCount { get; private set; }
    internal long ColumnarIntermediateMergeRows { get; private set; }
    internal long ColumnarSortInputRows { get; private set; }

    internal readonly record struct SortEntry(Row Row, SortKey Key0, int ExtraKeyOffset, ulong KeyPrefix);
    private readonly record struct ColumnarKeyEntry(SortKey Key0, int ExtraKeyOffset, ulong KeyPrefix);
    private readonly record struct ColumnarChunkEntry(int BatchIndex, int RowIndex, SortKey Key0, int ExtraKeyOffset, ulong KeyPrefix);

    // Column-name prefix for individual sort-key columns written to spill chunks when a key
    // cannot be read directly from an existing column (expressions, variables, outer refs, etc.).
    // Using one column per stamped key avoids JSON array serialization and lets Arrow store
    // each value in its native typed column (Int64, Decimal128, Double, Boolean,
    // Timestamp, or String), which eliminates millions of JsonElement allocations
    // and string deserialization cycles on medium/large sort workloads.
    internal const string SortKeyPrefix = "_SYS_SK_";

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

    /// <summary>
    /// Sorts a spilled chunk directly using columnar batch reads when all <c>ORDER BY</c> keys
    /// are plain columns in an Arrow spill file, avoiding per-row <see cref="Row"/> materialization
    /// and scalar boxing during chunk sorting and intermediate merges. Falls back transparently to
    /// <see cref="SortStreamAsync"/> for JSON spills or expression sort keys.
    /// </summary>
    internal async IAsyncEnumerable<Row> SortSpillChunkAsync(
        string chunkName,
        List<OrderByClause> orderBy,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = PipelineCursorScope.LinkCancellationTokens(_context.CancellationToken, cancellationToken, out var effectiveCancellationToken);
        bool allPlainCandidates = orderBy.Count > 0;
        for (int i = 0; i < orderBy.Count; i++)
        {
            if (!IsCandidatePlainColumn(orderBy[i].Expression, _context))
            {
                allPlainCandidates = false;
                break;
            }
        }

        if (!allPlainCandidates || _context.SpillFormat.Equals("Json", StringComparison.OrdinalIgnoreCase))
        {
            await foreach (var row in SortStreamAsync(ReadSpillRowsForFallback(chunkName), orderBy, null, effectiveCancellationToken))
                yield return row;
            yield break;
        }

        await using var reader = await _context.SpillStore.CreateReaderAsync(chunkName);
        if (reader is not IColumnarSpillReader columnarReader)
        {
            await foreach (var row in SortStreamAsync(reader.AsEnumerableAsync(), orderBy, null, effectiveCancellationToken))
                yield return row;
            yield break;
        }

        await using var batchEnumerator = columnarReader.AsColumnBatchesAsync().GetAsyncEnumerator(effectiveCancellationToken);
        ColumnBatch? firstBatch = null;
        while (await batchEnumerator.MoveNextAsync())
        {
            if (batchEnumerator.Current.RowCount > 0)
            {
                firstBatch = batchEnumerator.Current;
                break;
            }
            batchEnumerator.Current.Dispose();
        }

        if (firstBatch == null)
            yield break;

        var schema = firstBatch.Schema;
        var keyOrdinals = TryResolveBatchOrderOrdinals(schema, orderBy);
        if (keyOrdinals == null || !ColumnBatchRowBuilder.CanBuild(schema))
        {
            await foreach (var row in SortStreamAsync(
                MaterializeRemainingBatches(firstBatch, batchEnumerator),
                orderBy,
                null,
                effectiveCancellationToken))
            {
                yield return row;
            }
            yield break;
        }

        using var cursor = await PipelineCursorScope.AcquireCursorAsync(
            _bufferManager,
            _context.SessionId ?? "DEFAULT",
            effectiveCancellationToken,
            owner: this);
        var arbiter = _context.MemoryArbiter ?? MemoryGrantArbiter.Shared;
        var chunkLease = arbiter.AcquireLease();
        var chunkPaths = new List<string>();
        var cleanup = new HashSet<string>();
        var chunkBatches = new List<ColumnBatch>();

        try
        {
            bool caseSensitive = _context.CaseSensitiveComparison;
            int keyCount = orderBy.Count;
            int extraCount = Math.Max(0, keyCount - 1);
            var extraKeys = extraCount > 0 ? new List<SortKey>() : null;
            var chunkComparer = new ColumnarChunkEntryComparer(this, orderBy, caseSensitive, extraKeys);
            var currentChunk = new List<ColumnarChunkEntry>();
            var memGuard = new MemoryBudgetGuard(MemoryGovernor.Ceiling(_context));
            int chunkCounter = 0;

            var readKeyColumnNames = new string[keyCount];
            for (int k = 0; k < keyCount; k++)
                readKeyColumnNames[k] = schema.Fields[keyOrdinals[k]].Name;

            async Task SpillColumnarChunkAsync(ColumnBatch? retainBatch = null)
            {
                currentChunk.Sort(chunkComparer);
                var outName = $"sort_chunk_{Guid.NewGuid():N}_{chunkCounter++}.tmp";
                chunkPaths.Add(outName);
                cleanup.Add(outName);
                await WriteSortedColumnarChunkAsync(outName, schema, chunkBatches, currentChunk);
                _context.Telemetry.SortSpillCount++;
                _context.Telemetry.PartitionsCount++;
                foreach (var b in chunkBatches)
                {
                    if (!ReferenceEquals(b, retainBatch))
                        b.Dispose();
                }
                chunkBatches.Clear();
                currentChunk.Clear();
                extraKeys?.Clear();
                memGuard.Reset();
                chunkLease.Dispose();
                chunkLease = arbiter.AcquireLease();
                if (retainBatch != null)
                {
                    chunkBatches.Add(retainBatch);
                    memGuard.Add(retainBatch.AllocatedBytes);
                }
            }

            ColumnBatch? batch = firstBatch;
            firstBatch = null;
            while (batch != null)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                if (batch.RowCount == 0)
                {
                    batch.Dispose();
                }
                else
                {
                    int batchIdx = chunkBatches.Count;
                    chunkBatches.Add(batch);
                    memGuard.Add(batch.AllocatedBytes);

                    for (int rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                    {
                        var key0 = SortKey.FromBatchColumn(batch, keyOrdinals[0], rowIndex);
                        int extraOffset = -1;
                        long keyHeapBytes = key0.EstimateHeapBytes();
                        if (extraCount > 0)
                        {
                            extraOffset = extraKeys!.Count;
                            for (int k = 1; k < keyCount; k++)
                            {
                                var extra = SortKey.FromBatchColumn(batch, keyOrdinals[k], rowIndex);
                                extraKeys.Add(extra);
                                keyHeapBytes += extra.EstimateHeapBytes();
                            }
                        }

                        ulong keyPrefix = ComputeKeyPrefix(key0, orderBy[0], caseSensitive);
                        currentChunk.Add(new ColumnarChunkEntry(batchIdx, rowIndex, key0, extraOffset, keyPrefix));
                        memGuard.Add(48L + (extraCount * 40L) + keyHeapBytes);
                        ColumnarSortInputRows++;

                        bool spillForArbiter = currentChunk.Count > 0 && chunkLease.RegisterAndCheckSpill(memGuard.BytesAccumulated);
                        if (currentChunk.Count >= ChunkSize || (currentChunk.Count > 0 && (memGuard.Exceeded() || spillForArbiter)))
                        {
                            bool hasMoreRowsInBatch = rowIndex + 1 < batch.RowCount;
                            await SpillColumnarChunkAsync(hasMoreRowsInBatch ? batch : null);
                            batchIdx = hasMoreRowsInBatch ? 0 : -1;
                        }
                    }
                }

                batch = await batchEnumerator.MoveNextAsync() ? batchEnumerator.Current : null;
            }

            if (currentChunk.Count > 0)
            {
                currentChunk.Sort(chunkComparer);
                if (chunkPaths.Count == 0)
                {
                    var layout = RowPacker.LayoutFor(schema);
                    var plans = new RowPacker.BatchColumnPlan[chunkBatches.Count];
                    for (int b = 0; b < chunkBatches.Count; b++)
                        plans[b] = RowPacker.BatchColumnPlan.For(chunkBatches[b]);

                    foreach (var entry in currentChunk)
                    {
                        effectiveCancellationToken.ThrowIfCancellationRequested();
                        yield return RowPacker.MaterializeBatchScalarRow(
                            chunkBatches[entry.BatchIndex], plans[entry.BatchIndex], layout, entry.RowIndex);
                    }
                    yield break;
                }

                var finalChunkName = $"sort_chunk_{Guid.NewGuid():N}_{chunkCounter++}.tmp";
                chunkPaths.Add(finalChunkName);
                cleanup.Add(finalChunkName);
                await WriteSortedColumnarChunkAsync(finalChunkName, schema, chunkBatches, currentChunk);
                _context.Telemetry.SortSpillCount++;
                _context.Telemetry.PartitionsCount++;
                foreach (var b in chunkBatches) b.Dispose();
                chunkBatches.Clear();
                currentChunk.Clear();
                extraKeys?.Clear();
            }

            chunkLease.Dispose();
            if (chunkPaths.Count == 0) yield break;

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
                        nextLevel.Add(group[0]);
                        continue;
                    }

                    var merged = $"sort_merge_p{passNo}_{Guid.NewGuid():N}_{nextLevel.Count}.tmp";
                    cleanup.Add(merged);
                    await MergeChunksToFileAsync(merged, group, readKeyColumnNames, null, orderBy);
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

            MergePassCount++;
            await foreach (var entry in MergeChunksAsync(chunkPaths, readKeyColumnNames, null, orderBy, stripKeys: false).WithCancellation(effectiveCancellationToken))
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                yield return entry.Row;
            }
        }
        finally
        {
            firstBatch?.Dispose();
            foreach (var b in chunkBatches) b.Dispose();
            chunkBatches.Clear();
            chunkLease.Dispose();
            foreach (var path in cleanup)
            {
                try { _context.SpillStore.DeleteChunk(path); }
                catch (Exception ex) { _logger.Warning($"Error cleaning up external sort chunk {path}: {ex.Message}"); }
            }
        }
    }

    private async IAsyncEnumerable<Row> ReadSpillRowsForFallback(string chunkName)
    {
        await using var reader = await _context.SpillStore.CreateReaderAsync(chunkName);
        await foreach (var row in reader.AsEnumerableAsync())
            yield return row;
    }

    private static async IAsyncEnumerable<Row> MaterializeRemainingBatches(
        ColumnBatch firstBatch,
        IAsyncEnumerator<ColumnBatch> enumerator)
    {
        ColumnBatch? current = firstBatch;
        try
        {
            while (current != null)
            {
                var layout = RowPacker.LayoutFor(current.Schema);
                var plan = RowPacker.BatchColumnPlan.For(current);
                for (int r = 0; r < current.RowCount; r++)
                    yield return RowPacker.MaterializeBatchScalarRow(current, plan, layout, r);
                current.Dispose();
                current = null;
                if (await enumerator.MoveNextAsync())
                    current = enumerator.Current;
            }
        }
        finally
        {
            current?.Dispose();
        }
    }

    private static int[]? TryResolveBatchOrderOrdinals(ColumnBatchSchema schema, List<OrderByClause> orderBy)
    {
        var ordinals = new int[orderBy.Count];
        for (int k = 0; k < orderBy.Count; k++)
        {
            var keyName = ((IdentifierExpression)orderBy[k].Expression).Name;
            if (schema.TryGetOrdinal(keyName, out var ord))
            {
                ordinals[k] = ord;
                continue;
            }

            var dot = keyName.LastIndexOf('.');
            if (dot >= 0 && schema.TryGetOrdinal(keyName[(dot + 1)..], out var bareOrd))
            {
                ordinals[k] = bareOrd;
                continue;
            }

            return null;
        }
        return ordinals;
    }

    private async Task WriteSortedColumnarChunkAsync(
        string chunkName,
        ColumnBatchSchema schema,
        List<ColumnBatch> batches,
        List<ColumnarChunkEntry> sortedEntries)
    {
        await using var writer = await _context.SpillStore.CreateWriterAsync(chunkName);
        if (writer is IColumnarSpillWriter columnarWriter)
        {
            int flushRows = Math.Max(1, _context.EffectiveBatchSize);
            long flushBytes = _context.EffectiveBatchTargetBytes;
            using var builder = new ColumnBatchRowBuilder(schema, Math.Min(flushRows, Math.Max(1, sortedEntries.Count)));
            for (int i = 0; i < sortedEntries.Count; i++)
            {
                var entry = sortedEntries[i];
                builder.AppendFrom(batches[entry.BatchIndex], entry.RowIndex);
                if (builder.Count >= builder.Capacity || (flushBytes > 0 && builder.EstimatedBytes >= flushBytes))
                {
                    using var outBatch = builder.Flush();
                    await columnarWriter.WriteBatchAsync(outBatch);
                }
            }
            if (builder.Count > 0)
            {
                using var outBatch = builder.Flush();
                await columnarWriter.WriteBatchAsync(outBatch);
            }
            return;
        }

        var layout = RowPacker.LayoutFor(schema);
        var plans = new RowPacker.BatchColumnPlan[batches.Count];
        for (int b = 0; b < batches.Count; b++)
            plans[b] = RowPacker.BatchColumnPlan.For(batches[b]);
        foreach (var entry in sortedEntries)
            await writer.WriteRowAsync(RowPacker.MaterializeBatchScalarRow(batches[entry.BatchIndex], plans[entry.BatchIndex], layout, entry.RowIndex));
    }

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

        try
        {
            bool caseSensitive = _context.CaseSensitiveComparison;
            int keyCount = orderBy.Count;
            int extraCount = Math.Max(0, keyCount - 1);
            var inputExtraKeys = extraCount > 0 ? new List<SortKey>() : null;
            var inputComparer = new SortEntryComparer(this, orderBy, caseSensitive, inputExtraKeys);
            var keySlots = extractKeysAsync == null && keyCount > 0 ? BuildKeySlotEvaluators(orderBy) : null;
            bool allKeysSync = keySlots != null && Array.TrueForAll(keySlots, static s => s.CanEvaluateSynchronously);

            bool canConsiderUnstamping = extractKeysAsync == null && keyCount > 0;
            var candidateUnstamp = new bool[keyCount];
            for (int i = 0; i < keyCount; i++)
                candidateUnstamp[i] = canConsiderUnstamping && IsCandidatePlainColumn(orderBy[i].Expression, _context);

            bool spillPlanInitialized = false;
            string[]? readKeyColumnNames = null;
            bool[]? unstampedKey = null;
            TableSchema?[]? verifiedSchemas = null;
            bool[]? anyChunkStamped = null;
            bool[]? chunkStampedBuffer = keyCount > 0 ? new bool[keyCount] : null;

            async Task SpillChunkWithPlanAsync(string chunkName, List<SortEntry> chunk, List<SortKey>? extraKeys)
            {
                if (keyCount == 0)
                {
                    readKeyColumnNames ??= Array.Empty<string>();
                    anyChunkStamped ??= Array.Empty<bool>();
                    LastSpilledSentinelColumnCount = 0;
                    await SpillChunkAsync(chunkName, chunk, Array.Empty<int>(), Array.Empty<string>(), extraKeys);
                    return;
                }

                bool isFirstChunk = !spillPlanInitialized;
                if (isFirstChunk)
                {
                    readKeyColumnNames = new string[keyCount];
                    unstampedKey = new bool[keyCount];
                    verifiedSchemas = new TableSchema?[keyCount];
                    anyChunkStamped = new bool[keyCount];

                    var firstRow = chunk[0].Row;
                    for (int k = 0; k < keyCount; k++)
                    {
                        if (candidateUnstamp[k])
                        {
                            var keyName = ((IdentifierExpression)orderBy[k].Expression).Name;
                            var resolved = ResolveRowColumnName(keyName, firstRow, out var vSchema);
                            if (resolved != null)
                            {
                                readKeyColumnNames[k] = resolved;
                                unstampedKey[k] = true;
                                verifiedSchemas[k] = vSchema;
                                chunkStampedBuffer![k] = false;
                                continue;
                            }
                        }

                        readKeyColumnNames[k] = SortKeyPrefix + k;
                        unstampedKey[k] = false;
                        anyChunkStamped[k] = true;
                        chunkStampedBuffer![k] = true;
                    }
                    spillPlanInitialized = true;
                }
                else
                {
                    for (int k = 0; k < keyCount; k++)
                        chunkStampedBuffer![k] = !unstampedKey![k];
                }

                VerifyChunkUnstampedKeys(
                    chunk,
                    startIndex: isFirstChunk ? 1 : 0,
                    isFirstChunk,
                    orderBy,
                    unstampedKey!,
                    readKeyColumnNames!,
                    verifiedSchemas!,
                    chunkStampedBuffer!,
                    anyChunkStamped!);

                int stampedCount = 0;
                for (int k = 0; k < keyCount; k++)
                {
                    if (chunkStampedBuffer![k]) stampedCount++;
                }

                if (stampedCount == 0)
                {
                    LastSpilledSentinelColumnCount = 0;
                    await SpillChunkAsync(chunkName, chunk, Array.Empty<int>(), Array.Empty<string>(), extraKeys);
                    return;
                }

                var stampedKeyIndices = new int[stampedCount];
                var stampedColumnNames = new string[stampedCount];
                int writeIdx = 0;
                for (int k = 0; k < keyCount; k++)
                {
                    if (!chunkStampedBuffer![k]) continue;
                    stampedKeyIndices[writeIdx] = k;
                    stampedColumnNames[writeIdx] = SortKeyPrefix + k;
                    writeIdx++;
                }

                LastSpilledSentinelColumnCount = stampedCount;
                StampedSpillRowCount += chunk.Count;
                await SpillChunkAsync(chunkName, chunk, stampedKeyIndices, stampedColumnNames, extraKeys);
            }

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
                await SpillChunkWithPlanAsync(chunkName, currentChunk, inputExtraKeys);
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
                await SpillChunkWithPlanAsync(chunkName, currentChunk, inputExtraKeys);
                _context.Telemetry.SortSpillCount++;
                _context.Telemetry.PartitionsCount++;
                currentChunk.Clear();
                inputExtraKeys?.Clear();
            }

            chunkLease.Dispose();

            // 3. Bounded multi-pass k-way merge.
            if (chunkPaths.Count == 0) yield break;

            var sentinelColumnNames = BuildSentinelColumnNames(anyChunkStamped);

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
                    await MergeChunksToFileAsync(merged, group, readKeyColumnNames!, sentinelColumnNames, orderBy);
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

            // Final pass: merge the remaining (<= MaxMergeFanIn) chunks and strip sentinel keys if any were stamped.
            MergePassCount++;
            bool stripSentinels = sentinelColumnNames is { Length: > 0 };
            await foreach (var entry in MergeChunksAsync(chunkPaths, readKeyColumnNames!, sentinelColumnNames, orderBy, stripKeys: stripSentinels).WithCancellation(effectiveCancellationToken))
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                yield return entry.Row;
            }
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
        string[]? sentinelColumnNames,
        List<OrderByClause> orderBy,
        bool stripKeys = true)
    {
        var readers = new List<ISpillReader>(inputChunks.Count);
        bool caseSensitive = _context.CaseSensitiveComparison;
        int keyCount = keyColumnNames.Length;
        int extraCount = Math.Max(0, keyCount - 1);
        var mergeExtraKeys = extraCount > 0 ? new SortKey[inputChunks.Count * extraCount] : null;
        var mergeComparer = new SortEntryComparer(this, orderBy, caseSensitive, mergeExtraKeys);
        Dictionary<TableSchema, (TableSchema? Stripped, int[] KeptIndices)>? strippedSchemas =
            stripKeys && sentinelColumnNames is { Length: > 0 } ? new() : null;
        try
        {
            foreach (var path in inputChunks)
                readers.Add(await _context.SpillStore.CreateReaderAsync(path));
            MaxConcurrentMergeReaders = Math.Max(MaxConcurrentMergeReaders, readers.Count);

            var heap = new PriorityQueue<int, SortEntry>(mergeComparer);

            for (int i = 0; i < readers.Count; i++)
            {
                var entry = await ReadNextSortEntryAsync(
                    readers[i], i, keyColumnNames, sentinelColumnNames, orderBy, caseSensitive, stripKeys, strippedSchemas, mergeExtraKeys, extraCount);
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
                                sentinelColumnNames,
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
                                fastReader, extraOffset, keyColumnNames, sentinelColumnNames, orderBy, caseSensitive, stripKeys, mergeExtraKeys, extraCount);
                            if (next.HasValue)
                                heap.Enqueue(chunkIdx, next.Value);
                        }
                    }
                    else
                    {
                        var next = await ReadNextSortEntryAsync(
                            reader, chunkIdx, keyColumnNames, sentinelColumnNames, orderBy, caseSensitive, stripKeys, strippedSchemas, mergeExtraKeys, extraCount);
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
        string[]? sentinelColumnNames,
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
                sentinelColumnNames,
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
        string[]? sentinelColumnNames,
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
                    sentinelColumnNames,
                    stripKeys,
                    out var fastRow,
                    out var key0,
                    extraCount > 0 ? mergeExtraKeys.AsSpan(extraOffset, extraCount) : Span<SortKey>.Empty))
            {
                ulong prefix = keyColumnNames.Length > 0 ? ComputeKeyPrefix(key0, orderBy[0], caseSensitive) : 0UL;
                return new SortEntry(fastRow, key0, extraOffset, prefix);
            }

            return await ReadSlowFastReaderAsync(
                fastReader, extraOffset, keyColumnNames, sentinelColumnNames, orderBy, caseSensitive, stripKeys, mergeExtraKeys, extraCount);
        }

        var row = reader is ISortSpillReader vtReader
            ? await vtReader.ReadRowValueTaskAsync()
            : await reader.ReadRowAsync();
        if (row == null) return null;

        var fallbackKey0 = ExtractKeysFromRow(row, keyColumnNames, sentinelColumnNames, mergeExtraKeys, extraOffset);
        if (strippedSchemas != null)
            row = StripFinalSentinelColumns(row, sentinelColumnNames!, strippedSchemas);
        ulong fallbackPrefix = keyColumnNames.Length > 0 ? ComputeKeyPrefix(fallbackKey0, orderBy[0], caseSensitive) : 0UL;
        return new SortEntry(row, fallbackKey0, extraOffset, fallbackPrefix);
    }

    /// <summary>
    /// Merges a bounded group of spilled chunks into a single new intermediate run while leaving
    /// any <c>_SYS_SK_*</c> sentinel columns attached so the final pass can read and strip them once.
    /// Uses zero-boxing columnar batch reads/writes when all input chunks are compatible Arrow spills.
    /// </summary>
    private async Task MergeChunksToFileAsync(
        string outName,
        List<string> inputChunks,
        string[] keyColumnNames,
        string[]? sentinelColumnNames,
        List<OrderByClause> orderBy)
    {
        if (!_context.SpillFormat.Equals("Json", StringComparison.OrdinalIgnoreCase)
            && await TryMergeChunksToFileColumnarAsync(outName, inputChunks, keyColumnNames, sentinelColumnNames, orderBy))
        {
            return;
        }

        await using var writer = await _context.SpillStore.CreateWriterAsync(outName);
        await foreach (var entry in MergeChunksAsync(inputChunks, keyColumnNames, sentinelColumnNames, orderBy, stripKeys: false))
        {
            await writer.WriteRowAsync(entry.Row);
        }
    }

    private sealed class ColumnarMergeCursor(ISpillReader reader, IAsyncEnumerator<ColumnBatch> enumerator, ColumnBatch firstBatch)
        : IAsyncDisposable
    {
        public ISpillReader Reader { get; } = reader;
        public IAsyncEnumerator<ColumnBatch> Enumerator { get; } = enumerator;
        public ColumnBatch? CurrentBatch { get; set; } = firstBatch;
        public int RowIndex { get; set; }

        public async ValueTask<bool> AdvanceAsync()
        {
            if (CurrentBatch == null) return false;
            RowIndex++;
            if (RowIndex < CurrentBatch.RowCount)
                return true;

            CurrentBatch.Dispose();
            CurrentBatch = null;
            RowIndex = 0;

            while (await Enumerator.MoveNextAsync())
            {
                if (Enumerator.Current.RowCount > 0)
                {
                    CurrentBatch = Enumerator.Current;
                    return true;
                }
                Enumerator.Current.Dispose();
            }
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            CurrentBatch?.Dispose();
            CurrentBatch = null;
            await Enumerator.DisposeAsync();
            await Reader.DisposeAsync();
        }
    }

    private async Task<bool> TryMergeChunksToFileColumnarAsync(
        string outName,
        List<string> inputChunks,
        string[] keyColumnNames,
        string[]? sentinelColumnNames,
        List<OrderByClause> orderBy)
    {
        var cursors = new List<ColumnarMergeCursor>(inputChunks.Count);
        var emptyReaders = new List<ISpillReader>();
        try
        {
            ColumnBatchSchema? sharedSchema = null;
            foreach (var path in inputChunks)
            {
                var reader = await _context.SpillStore.CreateReaderAsync(path);
                if (reader is not IColumnarSpillReader columnarReader)
                {
                    await reader.DisposeAsync();
                    return false;
                }

                var enumerator = columnarReader.AsColumnBatchesAsync().GetAsyncEnumerator(_context.CancellationToken);
                ColumnBatch? firstBatch = null;
                while (await enumerator.MoveNextAsync())
                {
                    if (enumerator.Current.RowCount > 0)
                    {
                        firstBatch = enumerator.Current;
                        break;
                    }
                    enumerator.Current.Dispose();
                }

                if (firstBatch == null)
                {
                    await enumerator.DisposeAsync();
                    emptyReaders.Add(reader);
                    continue;
                }

                if (sharedSchema == null)
                {
                    if (!ColumnBatchRowBuilder.CanBuild(firstBatch.Schema))
                    {
                        firstBatch.Dispose();
                        await enumerator.DisposeAsync();
                        await reader.DisposeAsync();
                        return false;
                    }
                    sharedSchema = firstBatch.Schema;
                }
                else if (!ColumnBatchRowBuilder.HasCompatibleSchema(sharedSchema, firstBatch.Schema))
                {
                    firstBatch.Dispose();
                    await enumerator.DisposeAsync();
                    await reader.DisposeAsync();
                    return false;
                }

                cursors.Add(new ColumnarMergeCursor(reader, enumerator, firstBatch));
            }

            if (sharedSchema == null || cursors.Count == 0)
            {
                await using var emptyWriter = await _context.SpillStore.CreateWriterAsync(outName);
                return true;
            }

            var keyOrdinals = ResolveMergeKeyOrdinals(sharedSchema, keyColumnNames, sentinelColumnNames);
            if (keyOrdinals == null)
                return false;

            MaxConcurrentMergeReaders = Math.Max(MaxConcurrentMergeReaders, cursors.Count);

            await using var writer = await _context.SpillStore.CreateWriterAsync(outName);
            if (writer is not IColumnarSpillWriter columnarWriter)
                return false;

            bool caseSensitive = _context.CaseSensitiveComparison;
            int keyCount = keyOrdinals.Length;
            int extraCount = Math.Max(0, keyCount - 1);
            var mergeExtraKeys = extraCount > 0 ? new SortKey[cursors.Count * extraCount] : null;
            var mergeComparer = new ColumnarKeyEntryComparer(this, orderBy, caseSensitive, mergeExtraKeys);
            var heap = new PriorityQueue<int, ColumnarKeyEntry>(mergeComparer);

            for (int i = 0; i < cursors.Count; i++)
            {
                var entry = ReadColumnarMergeKey(cursors[i].CurrentBatch!, cursors[i].RowIndex, i, keyOrdinals, orderBy, caseSensitive, mergeExtraKeys, extraCount);
                heap.Enqueue(i, entry);
            }

            int flushRows = Math.Max(1, _context.EffectiveBatchSize);
            long flushBytes = _context.EffectiveBatchTargetBytes;
            using var builder = new ColumnBatchRowBuilder(sharedSchema, flushRows);

            while (heap.TryDequeue(out int cursorIdx, out _))
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
                var cursor = cursors[cursorIdx];
                builder.AppendFrom(cursor.CurrentBatch!, cursor.RowIndex);
                ColumnarIntermediateMergeRows++;

                if (builder.Count >= builder.Capacity || (flushBytes > 0 && builder.EstimatedBytes >= flushBytes))
                {
                    using var outBatch = builder.Flush();
                    await columnarWriter.WriteBatchAsync(outBatch);
                }

                if (await cursor.AdvanceAsync())
                {
                    var nextEntry = ReadColumnarMergeKey(cursor.CurrentBatch!, cursor.RowIndex, cursorIdx, keyOrdinals, orderBy, caseSensitive, mergeExtraKeys, extraCount);
                    heap.Enqueue(cursorIdx, nextEntry);
                }
            }

            if (builder.Count > 0)
            {
                using var outBatch = builder.Flush();
                await columnarWriter.WriteBatchAsync(outBatch);
            }

            return true;
        }
        finally
        {
            foreach (var c in cursors) await c.DisposeAsync();
            foreach (var r in emptyReaders) await r.DisposeAsync();
        }
    }

    private static int[]? ResolveMergeKeyOrdinals(
        ColumnBatchSchema schema,
        string[] keyColumnNames,
        string[]? sentinelColumnNames)
    {
        var ordinals = new int[keyColumnNames.Length];
        for (int k = 0; k < keyColumnNames.Length; k++)
        {
            int idx = -1;
            if (sentinelColumnNames is { Length: > 0 })
            {
                string sentinelName = $"{SortKeyPrefix}{k}";
                for (int s = 0; s < sentinelColumnNames.Length; s++)
                {
                    if (string.Equals(sentinelColumnNames[s], sentinelName, StringComparison.OrdinalIgnoreCase)
                        && schema.TryGetOrdinal(sentinelName, out var sentinelOrd))
                    {
                        idx = sentinelOrd;
                        break;
                    }
                }
            }
            if (idx < 0 && schema.TryGetOrdinal(keyColumnNames[k], out var colOrd))
                idx = colOrd;
            if (idx < 0)
                return null;
            ordinals[k] = idx;
        }
        return ordinals;
    }

    private static ColumnarKeyEntry ReadColumnarMergeKey(
        ColumnBatch batch,
        int rowIndex,
        int cursorIdx,
        int[] keyOrdinals,
        List<OrderByClause> orderBy,
        bool caseSensitive,
        SortKey[]? mergeExtraKeys,
        int extraCount)
    {
        if (keyOrdinals.Length == 0)
            return new ColumnarKeyEntry(SortKey.Null, -1, 0UL);

        var key0 = SortKey.FromBatchColumn(batch, keyOrdinals[0], rowIndex);
        int extraOffset = extraCount > 0 ? cursorIdx * extraCount : -1;
        for (int k = 1; k < keyOrdinals.Length; k++)
            mergeExtraKeys![extraOffset + (k - 1)] = SortKey.FromBatchColumn(batch, keyOrdinals[k], rowIndex);
        ulong prefix = ComputeKeyPrefix(key0, orderBy[0], caseSensitive);
        return new ColumnarKeyEntry(key0, extraOffset, prefix);
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

    private sealed class ColumnarKeyEntryComparer(
        ExternalSortEngine engine,
        List<OrderByClause> orderBy,
        bool caseSensitive,
        IReadOnlyList<SortKey>? extraKeys) : IComparer<ColumnarKeyEntry>
    {
        public int Compare(ColumnarKeyEntry a, ColumnarKeyEntry b)
        {
            if (a.KeyPrefix != b.KeyPrefix)
            {
                engine.PrefixResolvedComparisons++;
                return a.KeyPrefix < b.KeyPrefix ? -1 : 1;
            }

            if (orderBy.Count == 0) return 0;

            int res = SortKey.Compare(a.Key0, b.Key0, orderBy[0], caseSensitive);
            if (res != 0) return res;

            for (int i = 1; i < orderBy.Count; i++)
            {
                var ka = extraKeys![a.ExtraKeyOffset + (i - 1)];
                var kb = extraKeys[b.ExtraKeyOffset + (i - 1)];
                res = SortKey.Compare(ka, kb, orderBy[i], caseSensitive);
                if (res != 0) return res;
            }
            return 0;
        }
    }

    private sealed class ColumnarChunkEntryComparer(
        ExternalSortEngine engine,
        List<OrderByClause> orderBy,
        bool caseSensitive,
        IReadOnlyList<SortKey>? extraKeys) : IComparer<ColumnarChunkEntry>
    {
        public int Compare(ColumnarChunkEntry a, ColumnarChunkEntry b)
        {
            if (a.KeyPrefix != b.KeyPrefix)
            {
                engine.PrefixResolvedComparisons++;
                return a.KeyPrefix < b.KeyPrefix ? -1 : 1;
            }

            if (orderBy.Count == 0) return 0;

            int res = SortKey.Compare(a.Key0, b.Key0, orderBy[0], caseSensitive);
            if (res != 0) return res;

            for (int i = 1; i < orderBy.Count; i++)
            {
                var ka = extraKeys![a.ExtraKeyOffset + (i - 1)];
                var kb = extraKeys[b.ExtraKeyOffset + (i - 1)];
                res = SortKey.Compare(ka, kb, orderBy[i], caseSensitive);
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

    private static bool IsCandidatePlainColumn(Expression expression, IExecutionContext context)
    {
        if (expression is not IdentifierExpression id || string.IsNullOrEmpty(id.Name))
            return false;
        return !id.Name.StartsWith("@", StringComparison.Ordinal)
            && !id.Name.Equals("*", StringComparison.OrdinalIgnoreCase)
            && !id.Name.Contains(".CONNECTION_STRING", StringComparison.OrdinalIgnoreCase)
            && !context.Connections.ContainsKey(id.Name)
            && !context.FunctionRegistry.IsRegistered(id.Name)
            && context.OuterRowStack.Count == 0;
    }

    private static string? ResolveRowColumnName(string name, Row row, out TableSchema? verifiedSchema)
    {
        var schema = row.Schema;
        if (schema != null && !row.HasDynamicColumns)
        {
            int idx = schema.GetIndex(name);
            if (idx >= 0)
            {
                string canonical = schema.GetName(idx);
                if (schema.GetIndex(canonical) == idx)
                {
                    verifiedSchema = schema;
                    return canonical;
                }
                // Duplicate canonical column names with an alias pointing to a non-first slot:
                // stamp so slot disambiguation stays exact.
                verifiedSchema = null;
                return null;
            }

            var match = RowExpressionCompiler.FindMatch(name, schema.GetNamesWithAliases());
            if (!match.IsAmbiguous && match.ResolvedKey != null)
            {
                int resolvedIdx = schema.GetIndex(match.ResolvedKey);
                if (resolvedIdx >= 0 && schema.GetIndex(schema.GetName(resolvedIdx)) == resolvedIdx)
                {
                    verifiedSchema = schema;
                    return schema.GetName(resolvedIdx);
                }
            }

            verifiedSchema = null;
            return null;
        }

        // Dynamic / schema-less row (or schema-backed row with dynamic columns).
        verifiedSchema = null;
        if (schema != null)
        {
            int schemaIdx = schema.GetIndex(name);
            if (schemaIdx >= 0)
            {
                string canonical = schema.GetName(schemaIdx);
                return schema.GetIndex(canonical) == schemaIdx ? canonical : null;
            }
        }

        foreach (var existing in row.GetColumnNames())
        {
            if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                return existing;
        }

        var names = row.GetColumnNames();
        var nameList = names as IReadOnlyList<string> ?? new List<string>(names);
        var dynamicMatch = RowExpressionCompiler.FindMatch(name, nameList);
        if (!dynamicMatch.IsAmbiguous && dynamicMatch.ResolvedKey != null)
        {
            if (schema != null)
            {
                int resolvedIdx = schema.GetIndex(dynamicMatch.ResolvedKey);
                if (resolvedIdx >= 0 && schema.GetIndex(schema.GetName(resolvedIdx)) != resolvedIdx)
                    return null;
            }
            return dynamicMatch.ResolvedKey;
        }

        return null;
    }

    private static void VerifyChunkUnstampedKeys(
        List<SortEntry> chunk,
        int startIndex,
        bool isFirstChunk,
        List<OrderByClause> orderBy,
        bool[] unstampedKey,
        string[] readKeyColumnNames,
        TableSchema?[] verifiedSchemas,
        bool[] chunkStamped,
        bool[] anyChunkStamped)
    {
        bool anyUnstamped = false;
        for (int k = 0; k < unstampedKey.Length; k++)
        {
            if (unstampedKey[k]) { anyUnstamped = true; break; }
        }
        if (!anyUnstamped) return;

        for (int r = startIndex; r < chunk.Count; r++)
        {
            var row = chunk[r].Row;
            for (int k = 0; k < unstampedKey.Length; k++)
            {
                if (!unstampedKey[k]) continue;
                var vs = verifiedSchemas[k];
                if (vs != null && ReferenceEquals(row.Schema, vs) && !row.HasDynamicColumns)
                    continue;

                var keyName = ((IdentifierExpression)orderBy[k].Expression).Name;
                var resolved = ResolveRowColumnName(keyName, row, out _);
                if (!string.Equals(resolved, readKeyColumnNames[k], StringComparison.OrdinalIgnoreCase))
                {
                    unstampedKey[k] = false;
                    chunkStamped[k] = true;
                    anyChunkStamped[k] = true;
                    if (isFirstChunk)
                        readKeyColumnNames[k] = SortKeyPrefix + k;
                }
            }
        }
    }

    private static string[]? BuildSentinelColumnNames(bool[]? anyChunkStamped)
    {
        if (anyChunkStamped == null || anyChunkStamped.Length == 0) return null;
        int count = 0;
        for (int i = 0; i < anyChunkStamped.Length; i++)
        {
            if (anyChunkStamped[i]) count++;
        }
        if (count == 0) return null;

        var names = new string[count];
        int idx = 0;
        for (int i = 0; i < anyChunkStamped.Length; i++)
        {
            if (anyChunkStamped[i])
                names[idx++] = SortKeyPrefix + i;
        }
        return names;
    }

    /// <summary>
    /// Writes a sorted chunk to a spill file. Plain-column keys resolved to an existing column
    /// are written directly via the original rows without copying or stamping; remaining keys
    /// are stamped as separate named columns (<c>_SYS_SK_*</c>) so Arrow stores native typed values.
    /// </summary>
    private async Task SpillChunkAsync(
        string chunkName,
        List<SortEntry> chunk,
        int[] stampedKeyIndices,
        string[] stampedColumnNames,
        List<SortKey>? extraKeys)
    {
        await using var writer = await _context.SpillStore.CreateWriterAsync(chunkName);
        var rows = stampedKeyIndices.Length == 0
            ? UnstampedRows(chunk)
            : StampSortKeys(chunk, stampedKeyIndices, stampedColumnNames, extraKeys);
        await writer.WriteRowsAsync(rows);
    }

    private static IEnumerable<Row> UnstampedRows(List<SortEntry> rows)
    {
        foreach (var entry in rows)
            yield return entry.Row;
    }

    private static object? GetKeyObject(in SortEntry entry, int keyIndex, List<SortKey>? extraKeys) =>
        keyIndex == 0
            ? entry.Key0.ToObject()
            : extraKeys![entry.ExtraKeyOffset + (keyIndex - 1)].ToObject();

    private static IEnumerable<Row> StampSortKeys(
        IEnumerable<SortEntry> rows,
        int[] stampedKeyIndices,
        string[] stampedColumnNames,
        List<SortKey>? extraKeys)
    {
        // Rows held by a schema are stamped into a copy over that schema plus the stamped key columns,
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
                    var names = new List<string>(schema.ColumnCount + stampedColumnNames.Length);
                    for (int i = 0; i < schema.ColumnCount; i++) names.Add(schema.GetName(i));
                    var clash = false;
                    foreach (var key in stampedColumnNames)
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
                    var values = new object?[canonical + stampedColumnNames.Length];
                    for (int i = 0; i < canonical; i++) values[i] = entry.Row[i];
                    for (int s = 0; s < stampedKeyIndices.Length; s++)
                        values[canonical + s] = GetKeyObject(entry, stampedKeyIndices[s], extraKeys);
                    yield return new Row(stamped, values) { MayLackColumns = entry.Row.MayLackColumns };
                    continue;
                }
            }

            // Stamp each non-column key into its own dedicated column before writing the row.
            // These sentinel columns stay intact through intermediate passes and are
            // stripped only on the final merge pass.
            for (int s = 0; s < stampedColumnNames.Length; s++)
                entry.Row[stampedColumnNames[s]] = GetKeyObject(entry, stampedKeyIndices[s], extraKeys);

            try
            {
                yield return entry.Row;
            }
            finally
            {
                // The spill writer snapshots each yielded row before advancing the iterator.
                // Remove sentinels as soon as it advances, including when a write fails.
                for (int s = 0; s < stampedColumnNames.Length; s++)
                    entry.Row.RemoveColumn(stampedColumnNames[s]);
            }
        }
    }

    /// <summary>
    /// Reads the per-column sort keys back from a spilled row without removing columns during intermediate passes.
    /// </summary>
    private static SortKey ExtractKeysFromRow(
        Row row,
        string[] keyColumnNames,
        string[]? sentinelColumnNames,
        SortKey[]? mergeExtraKeys,
        int extraOffset)
    {
        if (keyColumnNames.Length == 0) return SortKey.Null;

        SortKey key0 = ReadRowKey(row, 0, keyColumnNames[0], sentinelColumnNames);
        for (int i = 1; i < keyColumnNames.Length; i++)
            mergeExtraKeys![extraOffset + (i - 1)] = ReadRowKey(row, i, keyColumnNames[i], sentinelColumnNames);
        return key0;
    }

    private static SortKey ReadRowKey(Row row, int keyIndex, string keyColumnName, string[]? sentinelColumnNames)
    {
        if (sentinelColumnNames is { Length: > 0 })
        {
            string sentinelName = $"{SortKeyPrefix}{keyIndex}";
            for (int s = 0; s < sentinelColumnNames.Length; s++)
            {
                if (string.Equals(sentinelColumnNames[s], sentinelName, StringComparison.OrdinalIgnoreCase)
                    && row.TryGetValue(sentinelName, out var sentinelRaw))
                {
                    return SortKey.FromValue(SpillSerializationHelper.UnwrapValue(sentinelRaw));
                }
            }
        }

        return row.TryGetValue(keyColumnName, out var raw)
            ? SortKey.FromValue(SpillSerializationHelper.UnwrapValue(raw))
            : SortKey.Null;
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
