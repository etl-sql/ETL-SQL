using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
/// Implements disk-spilling hash joins for large datasets that exceed memory capacity.
/// </summary>
public class ExternalJoinEngine
{
    private const int MaxRecursivePartitionDepth = 8;
    private const int MaxFanOutSampleRows = 4096;
    private const long MaxFanOutSampleBytes = 16L * 1024 * 1024;

    private readonly IExecutionContext _context;
    private readonly ILogger _logger;
    private int _partitionCount;
    public int PartitionCount => _partitionCount;
    internal long ColumnarBuildRows { get; private set; }
    internal long ColumnarProbeRows { get; private set; }
    internal long ColumnarRepartitionRows { get; private set; }


    private readonly IBufferManager? _bufferManager;

    public ExternalJoinEngine(IExecutionContext context, ILogger logger)
    {
        _context = context;
        _logger = logger;
        _partitionCount = Math.Max(1, _context.ExternalHashPartitions);
        _bufferManager = _context.ServiceProvider?.GetService<IBufferManager>();
    }

    /// <summary>
    /// The column names of both sides, used to give a row with no match the other side's columns
    /// as NULL. The engine records each side from the first row it reads.
    /// </summary>
    internal OuterJoinShape Shape { get; init; } = new();

    /// <summary>Performs an external hash join by partitioning both left and right streams to disk before join processing.</summary>
    public async IAsyncEnumerable<Row> ApplyHashJoinExternal(
        IAsyncEnumerable<Row> leftStream,
        IAsyncEnumerable<Row> rightStream,
        JoinClause join,
        List<string> leftKeys,
        List<string> rightKeys,
        long? knownBuildRowCount = null,
        long? knownBuildBytes = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = PipelineCursorScope.LinkCancellationTokens(_context.CancellationToken, cancellationToken, out var effectiveCancellationToken);
        using var cursor = await PipelineCursorScope.AcquireCursorAsync(
            _bufferManager,
            _context.SessionId ?? "DEFAULT",
            effectiveCancellationToken,
            owner: this);
        var tempFiles = new List<string>();
        try
        {
            await using var rightEnumerator = rightStream.GetAsyncEnumerator(effectiveCancellationToken);
            var rightSample = new List<Row>(MaxFanOutSampleRows);
            long sampledBytes = 0;
            while (rightSample.Count < MaxFanOutSampleRows
                && sampledBytes < MaxFanOutSampleBytes
                && await rightEnumerator.MoveNextAsync())
            {
                var row = rightEnumerator.Current;
                rightSample.Add(row);
                sampledBytes = checked(sampledBytes + row.EstimateHeapBytes());
            }
            if (rightSample.Count > 0) Shape.SeeRight(rightSample[0]);
            ConfigurePartitionCount(rightSample, rightKeys, knownBuildRowCount, knownBuildBytes);
            PlanDecisionRecorder.Record(_context, "external-join", "ExternalJoin",
                PlanDecisionOutcome.Accepted, PlanDecisionReasonCodes.SemanticGuard,
                "External hash join path accepted.",
                ("partitionCount", PartitionCount.ToString()),
                ("knownBuildRowCount", knownBuildRowCount?.ToString()),
                ("knownBuildBytes", knownBuildBytes?.ToString()),
                ("memoryCeilingBytes", MemoryGovernor.Ceiling(_context).ToString()));

            // 1. Partition Phase
            var leftPartitions = await PartitionStream(leftStream, leftKeys, "left", tempFiles);
            var rightPartitions = await PartitionStream(
                ReplaySample(rightSample, rightEnumerator), rightKeys, "right", tempFiles);

            // 2. Join Phase (one partition at a time)
            for (int i = 0; i < PartitionCount; i++)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                await foreach (var row in JoinPartition(
                    leftPartitions.Names[i],
                    rightPartitions.Names[i],
                    leftPartitions.Counts[i],
                    rightPartitions.Counts[i],
                    join,
                    leftKeys,
                    rightKeys,
                    depth: 0,
                    tempFiles,
                    effectiveCancellationToken))
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    yield return row;
                }
            }
        }
        finally
        {
            while (tempFiles.Count > 0)
                DeleteTempChunk(tempFiles[^1], tempFiles);
        }
    }

    private void ConfigurePartitionCount(
        IReadOnlyList<Row> sample,
        IReadOnlyList<string> keys,
        long? knownBuildRowCount,
        long? knownBuildBytes)
    {
        if (sample.Count == 0) return;
        long inputBytes = 0;
        long keyBytes = 0;
        var keyList = keys as List<string> ?? keys.ToList();
        var frequencies = new Dictionary<CompoundKey, int>();
        foreach (var row in sample)
        {
            inputBytes = checked(inputBytes + row.EstimateHeapBytes());
            var key = GetHashKey(row, keyList);
            keyBytes = checked(keyBytes + RowMemory.EstimateKeyBytes(key));
            frequencies[key] = frequencies.TryGetValue(key, out var count) ? count + 1 : 1;
        }
        var budget = MemoryGovernor.Ceiling(_context);
        if (budget <= 0) budget = Math.Max(1L, (long)_context.EffectiveOperatorMemoryGrantMB * 1024 * 1024);
        var hotFraction = frequencies.Count == 0 ? 0 : frequencies.Values.Max() / (double)sample.Count;
        var hasPlannedTotal = knownBuildRowCount >= 0;
        var plannedRows = hasPlannedTotal ? knownBuildRowCount!.Value : sample.Count;
        var plannedBytes = knownBuildBytes >= 0
            ? knownBuildBytes!.Value
            : hasPlannedTotal
                ? checked((long)Math.Ceiling(inputBytes / (double)sample.Count * plannedRows))
                : inputBytes;
        var estimatedDistinct = hasPlannedTotal
            ? Math.Min(plannedRows, (long)Math.Ceiling(frequencies.Count * (plannedRows / (double)sample.Count)))
            : frequencies.Count;
        var plan = HashPartitionSizing.Calculate(
            plannedBytes,
            plannedRows,
            (int)Math.Min(int.MaxValue, Math.Max(0, keyBytes / sample.Count)),
            budget,
            estimatedDistinctKeys: (int)Math.Min(int.MaxValue, estimatedDistinct),
            largestKeyFraction: hotFraction,
            minimumPartitions: hasPlannedTotal ? 1 : Math.Max(1, _partitionCount),
            maximumPartitions: Math.Max(1024, _partitionCount));
        _partitionCount = hasPlannedTotal ? plan.PartitionCount : Math.Max(_partitionCount, plan.PartitionCount);
        _logger.Debug(
            "External join sampled {SampleRows} build rows ({SampleBytes} bytes) and selected fan-out {FanOut}; estimated passes={Passes}, hotKey={HotKey}.",
            sample.Count, inputBytes, _partitionCount, plan.EstimatedPartitionPasses, plan.HasUnsplittableHotKey);
    }

    private static async IAsyncEnumerable<Row> ReplaySample(
        IReadOnlyList<Row> sample,
        IAsyncEnumerator<Row> remainder)
    {
        foreach (var row in sample) yield return row;
        while (await remainder.MoveNextAsync()) yield return remainder.Current;
    }

    private async IAsyncEnumerable<Row> JoinPartition(
        string leftName,
        string rightName,
        long leftRowCount,
        long rightRowCount,
        JoinClause join,
        List<string> leftKeys,
        List<string> rightKeys,
        int depth,
        List<string> tempFiles,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Memory-guarded direct join. The (right) build side is read fully before any probe row is
        // emitted, so if heap growth crosses the governor ceiling mid-build (e.g. wide rows under the
        // row threshold) we can repartition or apply policy without having yielded anything.
        long ceiling = MemoryGovernor.Ceiling(_context);
        var arbiter = _context.MemoryArbiter ?? MemoryGrantArbiter.Shared;
        var buildLease = arbiter.AcquireLease();
        PackedBuildTable? hashTable = null;
        try
        {
            hashTable = await BuildJoinHashTable(rightName, rightKeys, ceiling, buildLease, rightRowCount);
        }
        catch
        {
            buildLease.Dispose();
            throw;
        }

        if (hashTable == null)
        {
            // Release rejected build footprint before recursing or enforcing policy.
            buildLease.Dispose();
            PlanDecisionRecorder.Record(_context, "external-join.memory", "ExternalJoin",
                PlanDecisionOutcome.Degraded, PlanDecisionReasonCodes.MemoryAdmissionRejected,
                "External join build partition exceeded the memory ceiling; attempting recursive repartition.",
                ("depth", depth.ToString()),
                ("rightRows", rightRowCount.ToString()),
                ("memoryCeilingBytes", ceiling.ToString()),
                ("fallbackDestination", "recursive-repartition"));

            if (depth < MaxRecursivePartitionDepth && PartitionCount > 1)
            {
                var split = await TryRepartitionBothSides(leftName, rightName, leftKeys, rightKeys, rightRowCount, depth + 1, tempFiles);
                if (split != null)
                {
                    // Readers for both parent chunks closed in RepartitionPartition; delete superseded files before recursing.
                    DeleteTempChunk(leftName, tempFiles);
                    DeleteTempChunk(rightName, tempFiles);
                    await foreach (var row in JoinSubPartitions(split.Value.Left, split.Value.Right, join, leftKeys, rightKeys, depth + 1, tempFiles, cancellationToken))
                        yield return row;
                    yield break;
                }
            }

            PlanDecisionRecorder.Record(_context, "external-join.memory", "ExternalJoin",
                _context.MemoryGovernorPolicy == MemoryGovernorPolicy.SpillOrFail
                    ? PlanDecisionOutcome.Rejected
                    : PlanDecisionOutcome.Degraded,
                PlanDecisionReasonCodes.MemoryAdmissionRejected,
                "External join build partition could not be reduced under the memory ceiling.",
                ("depth", depth.ToString()),
                ("rightRows", rightRowCount.ToString()),
                ("memoryCeilingBytes", ceiling.ToString()),
                ("fallbackDestination", _context.MemoryGovernorPolicy == MemoryGovernorPolicy.SpillOrFail
                    ? "execution-error"
                    : "spill-only-churn"));

            MemoryGovernor.EnforcePolicy(_context,
                "JOIN build side exceeded the memory governor ceiling (Engine:TotalMemoryGrantMB) and could not be " +
                "reduced further by repartitioning. Increase the ceiling, reduce join-key skew, or set " +
                "Engine:MemoryGovernorPolicy = SpillOnly to churn to completion.");

            // SpillOnly churn: rebuild unguarded and continue.
            hashTable = await BuildJoinHashTable(rightName, rightKeys, 0, lease: null, rightRowCount);
        }

        // Build side is materialized in PackedBuildTable and its reader has closed; delete build partition before probing.
        DeleteTempChunk(rightName, tempFiles);

        try
        {
            await foreach (var row in ProbeJoin(leftName, hashTable!, join, leftKeys, cancellationToken))
                yield return row;
        }
        finally
        {
            hashTable?.Dispose();
            buildLease.Dispose();
            DeleteTempChunk(leftName, tempFiles);
        }
    }

    /// <summary>Recurses into the per-partition joins produced by a repartition step.</summary>
    private async IAsyncEnumerable<Row> JoinSubPartitions(
        PartitionSet left, PartitionSet right, JoinClause join, List<string> leftKeys, List<string> rightKeys,
        int depth, List<string> tempFiles, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < PartitionCount; i++)
        {
            await foreach (var row in JoinPartition(left.Names[i], right.Names[i], left.Counts[i], right.Counts[i], join, leftKeys, rightKeys, depth, tempFiles, cancellationToken))
                yield return row;
        }
    }

    /// <summary>
    /// Repartitions both sides at the given depth (depth-salted hash). Returns the pair only if the
    /// build side actually split (more than one used partition, all smaller than the original) so the
    /// caller can recurse; returns null when recursion can't help (e.g. severe key skew).
    /// </summary>
    private async Task<(PartitionSet Left, PartitionSet Right)?> TryRepartitionBothSides(
        string leftName, string rightName, List<string> leftKeys, List<string> rightKeys,
        long rightRowCount, int nextDepth, List<string> tempFiles)
    {
        var left = await RepartitionPartition(leftName, leftKeys, $"left_d{nextDepth}", nextDepth, tempFiles);
        var right = await RepartitionPartition(rightName, rightKeys, $"right_d{nextDepth}", nextDepth, tempFiles);
        var largestRight = right.Counts.Length == 0 ? 0 : right.Counts.Max();
        var usedRight = right.Counts.Count(c => c > 0);
        if (usedRight > 1 && largestRight < rightRowCount)
        {
            _logger.Debug("Recursively repartitioned external join partition at depth {Depth}. largestRight={LargestRight}", nextDepth, largestRight);
            return (left, right);
        }

        // Split did not reduce skew; discard unused child partitions so parent replay does not retain both generations.
        foreach (var child in left.Names)
            DeleteTempChunk(child, tempFiles);
        foreach (var child in right.Names)
            DeleteTempChunk(child, tempFiles);
        return null;
    }

    private void DeleteTempChunk(string chunkName, List<string> tempFiles)
    {
        try
        {
            _context.SpillStore.DeleteChunk(chunkName);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error cleaning up external join chunk {chunkName}: {ex.Message}");
        }
        finally
        {
            tempFiles.Remove(chunkName);
        }
    }

    /// <summary>
    /// Builds the hash table from the (right) build side, packing each build row into budget-capped
    /// pooled <c>byte[]</c> slabs rather than fat <see cref="Row"/> object graphs or per-row arrays.
    /// Rows are decoded back to a <see cref="Row"/> only on a probe-key match. Returns null if the
    /// accumulated build footprint crosses the governor ceiling mid-build, so the caller can
    /// repartition or apply policy before any probe row has been emitted.
    /// </summary>
    private async Task<PackedBuildTable?> BuildJoinHashTable(string rightName, List<string> rightKeys, long budgetBytes, IMemoryGrantLease? lease, long expectedRows = 0)
    {
        var guard = new MemoryBudgetGuard(budgetBytes);
        bool caseSensitive = _context.CaseSensitiveComparison;
        long cappedExpected = budgetBytes > 0
            ? Math.Min(expectedRows, Math.Max(64L, budgetBytes / 64))
            : expectedRows;
        int initialCapacity = cappedExpected > 0 ? (int)Math.Min(1 << 20, cappedExpected) : 0;
        var table = new PackedBuildTable(budgetBytes, caseSensitive, rightKeys.Count, initialCapacity);
        using var packer = new RowPacker();
        bool columnsCaptured = false;
        int keyCount = rightKeys.Count;
        int extraCount = Math.Max(0, keyCount - 1);
        JoinKey[] extraKeyBuffer = extraCount > 0 ? new JoinKey[extraCount] : Array.Empty<JoinKey>();
        try
        {
            await using var rightReader = await _context.SpillStore.CreateReaderAsync(rightName);
            if (rightReader is IColumnarSpillReader columnarReader)
            {
                ColumnBatchSchema? cachedBatchSchema = null;
                int[] keyOrdinals = keyCount > 0 ? new int[keyCount] : Array.Empty<int>();
                await foreach (var batch in columnarReader.AsColumnBatchesAsync())
                {
                    using (batch)
                    {
                        if (!columnsCaptured)
                        {
                            table.Columns.AddRange(batch.Schema.Fields.Select(field => field.Name));
                            columnsCaptured = true;
                        }
                        if (batch.RowCount == 0) continue;

                        if (!ReferenceEquals(cachedBatchSchema, batch.Schema))
                        {
                            cachedBatchSchema = batch.Schema;
                            for (int k = 0; k < keyCount; k++)
                                keyOrdinals[k] = cachedBatchSchema.GetOrdinal(rightKeys[k]);
                        }
                        var packPlan = RowPacker.BatchColumnPlan.For(batch);

                        for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                        {
                            JoinKey key0 = keyCount > 0
                                ? JoinKey.FromBatchColumn(batch, keyOrdinals[0], rowIndex)
                                : JoinKey.Null;
                            bool hasNullKey = keyCount > 0 && key0.IsNull;
                            for (var k = 1; k < keyCount; k++)
                            {
                                var extra = JoinKey.FromBatchColumn(batch, keyOrdinals[k], rowIndex);
                                extraKeyBuffer[k - 1] = extra;
                                if (extra.IsNull) hasNullKey = true;
                            }

                            var (slotIdx, byteLength) = packer.PackInto(batch, rowIndex, packPlan, table);
                            table.IndexRow(key0, extraKeyBuffer, hasNullKey, slotIdx, byteLength, guard);
                            ColumnarBuildRows++;
                            if (guard.Exceeded() || (lease?.RegisterAndCheckSpill(guard.BytesAccumulated) == true))
                            {
                                table.Dispose();
                                return null;
                            }
                        }
                    }
                }
                return table;
            }

            TableSchema? cachedRowSchema = null;
            int[] rowKeySlots = keyCount > 0 ? new int[keyCount] : Array.Empty<int>();
            var vtReader = rightReader as ISortSpillReader;
            while (true)
            {
                var rightRow = vtReader != null
                    ? await vtReader.ReadRowValueTaskAsync()
                    : await rightReader.ReadRowAsync();
                if (rightRow == null) break;

                // The build side has a uniform schema; capture the column order once (matches how the
                // Arrow spill writer infers its schema from the first row).
                if (!columnsCaptured)
                {
                    table.Columns.AddRange(rightRow.GetColumnNames());
                    columnsCaptured = true;
                }

                JoinKey key0 = JoinKey.Null;
                bool hasNullKey = false;
                if (keyCount > 0)
                {
                    ExtractRowJoinKeys(rightRow, rightKeys, rowKeySlots, ref cachedRowSchema, out key0, extraKeyBuffer, out hasNullKey);
                }

                var (slotIdx, byteLength) = packer.PackInto(rightRow, table.Columns, table);
                table.IndexRow(key0, extraKeyBuffer, hasNullKey, slotIdx, byteLength, guard);
                if (guard.Exceeded() || (lease?.RegisterAndCheckSpill(guard.BytesAccumulated) == true))
                {
                    table.Dispose();
                    return null;
                }
            }
            return table;
        }
        catch
        {
            table.Dispose();
            throw;
        }
    }

    /// <summary>Streams the probe (left) side against a packed build table, emitting join results.</summary>
    private async IAsyncEnumerable<Row> ProbeJoin(
        string leftName, PackedBuildTable table, JoinClause join, List<string> leftKeys,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // SEMI and ANTI return left rows only, each at most once. "LEFT SEMI JOIN" is not an outer join.
        bool isSemi = join.JoinType.Contains("SEMI", StringComparison.OrdinalIgnoreCase);
        bool isAnti = join.JoinType.Contains("ANTI", StringComparison.OrdinalIgnoreCase);
        bool leftOnly = isSemi || isAnti;
        bool isLeftJoin = !leftOnly && (join.JoinType.Contains("LEFT", StringComparison.OrdinalIgnoreCase) || join.JoinType.Contains("FULL", StringComparison.OrdinalIgnoreCase));
        bool isRightJoin = !leftOnly && (join.JoinType.Contains("RIGHT", StringComparison.OrdinalIgnoreCase) || join.JoinType.Contains("FULL", StringComparison.OrdinalIgnoreCase));
        // Track matched build rows by physical index (one bit per packed slot) for RIGHT/FULL outer.
        var matched = isRightJoin ? new bool[table.RowCount] : null;
        var compiledCondition = RowExpressionCompiler.TryCompilePredicate(_context, join.Condition, out var predicate)
            ? predicate
            : null;
        int keyCount = leftKeys.Count;
        int extraCount = Math.Max(0, keyCount - 1);
        JoinKey[] extraKeyBuffer = extraCount > 0 ? new JoinKey[extraCount] : Array.Empty<JoinKey>();

        await using var leftReader = await _context.SpillStore.CreateReaderAsync(leftName);
        if (leftReader is IColumnarSpillReader columnarReader)
        {
            ColumnBatchSchema? cachedBatchSchema = null;
            int[] keyOrdinals = keyCount > 0 ? new int[keyCount] : Array.Empty<int>();
            RowPacker.NameLayout? leftRowLayout = null;
            BatchCombinedLayout? batchCombinedLayout = null;
            object?[]? scratchCombinedValues = null;
            Row? scratchCombinedRow = null;

            await foreach (var batch in columnarReader.AsColumnBatchesAsync())
            {
                using (batch)
                {
                    if (batch.RowCount == 0) continue;

                    if (!ReferenceEquals(cachedBatchSchema, batch.Schema))
                    {
                        cachedBatchSchema = batch.Schema;
                        for (int k = 0; k < keyCount; k++)
                            keyOrdinals[k] = cachedBatchSchema.GetOrdinal(leftKeys[k]);
                        leftRowLayout = RowPacker.LayoutFor(cachedBatchSchema, cachedBatchSchema.Fields.Select(f => f.Name).ToList());
                        if (table.RowCount > 0)
                        {
                            batchCombinedLayout = BatchCombinedLayout.Create(cachedBatchSchema, table.Columns);
                            if (leftOnly && compiledCondition != null)
                            {
                                scratchCombinedValues = new object?[batchCombinedLayout.Schema.ColumnCount];
                                scratchCombinedRow = new Row(batchCombinedLayout.Schema, scratchCombinedValues);
                            }
                        }
                    }
                    var packPlan = RowPacker.BatchColumnPlan.For(batch);

                    for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ColumnarProbeRows++;

                        JoinKey key0 = keyCount > 0
                            ? JoinKey.FromBatchColumn(batch, keyOrdinals[0], rowIndex)
                            : JoinKey.Null;
                        bool hasNullKey = keyCount > 0 && key0.IsNull;
                        for (var k = 1; k < keyCount; k++)
                        {
                            var extra = JoinKey.FromBatchColumn(batch, keyOrdinals[k], rowIndex);
                            extraKeyBuffer[k - 1] = extra;
                            if (extra.IsNull) hasNullKey = true;
                        }

                        bool producedMatch = false;
                        if (!hasNullKey && table.TryGetChain(key0, extraKeyBuffer, out var chain))
                        {
                            var layout = batchCombinedLayout!;
                            if (leftOnly && scratchCombinedRow != null && scratchCombinedValues != null)
                            {
                                FillLeftValuesFromBatch(batch, packPlan, rowIndex, layout, scratchCombinedValues);
                                for (var idx = chain.Head; idx >= 0; idx = table.GetNextInBucket(idx))
                                {
                                    table.UnpackRowInto(idx, scratchCombinedValues, layout.RightSlots);
                                    if (compiledCondition!(scratchCombinedRow))
                                    {
                                        producedMatch = true;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                object?[]? firstLeftValues = null;
                                for (var idx = chain.Head; idx >= 0; idx = table.GetNextInBucket(idx))
                                {
                                    var values = new object?[layout.Schema.ColumnCount];
                                    if (firstLeftValues == null)
                                    {
                                        FillLeftValuesFromBatch(batch, packPlan, rowIndex, layout, values);
                                        firstLeftValues = values;
                                    }
                                    else
                                    {
                                        CopyNonOverwrittenLeftValues(firstLeftValues, values, layout);
                                    }

                                    table.UnpackRowInto(idx, values, layout.RightSlots);
                                    var combined = new Row(layout.Schema, values);
                                    bool isMatch = compiledCondition != null
                                        ? compiledCondition(combined)
                                        : await _context.EvaluateCondition(join.Condition, combined);
                                    if (isMatch)
                                    {
                                        producedMatch = true;
                                        if (leftOnly) break;
                                        yield return combined;
                                        if (matched != null) matched[idx] = true;
                                    }
                                }
                            }
                        }

                        if (leftOnly)
                        {
                            if (producedMatch == isSemi)
                                yield return RowPacker.MaterializeBatchScalarRow(batch, packPlan, leftRowLayout!, rowIndex);
                        }
                        else if (!producedMatch && isLeftJoin)
                        {
                            yield return Shape.Unmatched(RowPacker.MaterializeBatchScalarRow(batch, packPlan, leftRowLayout!, rowIndex));
                        }
                    }
                }
            }
        }
        else
        {
            TableSchema? cachedRowSchema = null;
            int[] rowKeySlots = keyCount > 0 ? new int[keyCount] : Array.Empty<int>();
            var vtReader = leftReader as ISortSpillReader;
            while (true)
            {
                var left = vtReader != null
                    ? await vtReader.ReadRowValueTaskAsync()
                    : await leftReader.ReadRowAsync();
                if (left == null) break;

                cancellationToken.ThrowIfCancellationRequested();
                JoinKey key0 = JoinKey.Null;
                bool hasNullKey = false;
                if (keyCount > 0)
                {
                    ExtractRowJoinKeys(left, leftKeys, rowKeySlots, ref cachedRowSchema, out key0, extraKeyBuffer, out hasNullKey);
                }

                bool producedMatch = false;
                if (!hasNullKey && table.TryGetChain(key0, extraKeyBuffer, out var chain))
                {
                    for (var idx = chain.Head; idx >= 0; idx = table.GetNextInBucket(idx))
                    {
                        var right = table.UnpackRow(idx);
                        var combined = CombineRows(left, right);
                        bool isMatch = compiledCondition != null
                            ? compiledCondition(combined)
                            : await _context.EvaluateCondition(join.Condition, combined);
                        if (isMatch)
                        {
                            producedMatch = true;
                            if (leftOnly) break;
                            yield return combined;
                            if (matched != null) matched[idx] = true;
                        }
                    }
                }

                if (leftOnly)
                {
                    if (producedMatch == isSemi) yield return left;
                }
                else if (!producedMatch && isLeftJoin)
                    yield return Shape.Unmatched(left);
            }
        }

        if (isRightJoin && matched != null)
        {
            for (int idx = 0; idx < table.RowCount; idx++)
            {
                if (!matched[idx])
                {
                    var unmatched = CombineRows(null, table.UnpackRow(idx));
                    Shape.Complete(unmatched);
                    yield return unmatched;
                }
            }
        }
    }

    private static void FillLeftValuesFromBatch(
        ColumnBatch batch,
        RowPacker.BatchColumnPlan packPlan,
        int rowIndex,
        BatchCombinedLayout layout,
        object?[] destination)
    {
        var leftSlots = layout.LeftSlots;
        var overwritten = layout.LeftOverwritten;
        for (int i = 0; i < leftSlots.Length; i++)
        {
            if (overwritten[i]) continue;
            destination[leftSlots[i]] = RowPacker.ReadBatchScalar(batch, packPlan, i, rowIndex);
        }
    }

    private static void CopyNonOverwrittenLeftValues(
        object?[] source,
        object?[] destination,
        BatchCombinedLayout layout)
    {
        var leftSlots = layout.LeftSlots;
        var overwritten = layout.LeftOverwritten;
        for (int i = 0; i < leftSlots.Length; i++)
        {
            if (overwritten[i]) continue;
            int slot = leftSlots[i];
            destination[slot] = source[slot];
        }
    }

    private async Task<PartitionSet> PartitionStream(IAsyncEnumerable<Row> stream, List<string> keys, string prefix, List<string> tempFiles)
    {
        return await PartitionStream(stream, keys, prefix, depth: 0, tempFiles);
    }

    private async Task<PartitionSet> RepartitionPartition(string sourceName, List<string> keys, string prefix, int depth, List<string> tempFiles)
    {
        if (!_context.SpillFormat.Equals("Json", StringComparison.OrdinalIgnoreCase))
            return await RepartitionPartitionColumnar(sourceName, keys, prefix, depth, tempFiles);
        return await PartitionStream(ReadPartitionStream(sourceName), keys, prefix, depth, tempFiles);
    }

    private async Task<PartitionSet> RepartitionPartitionColumnar(
        string sourceName,
        List<string> keys,
        string prefix,
        int depth,
        List<string> tempFiles)
    {
        var names = new string[PartitionCount];
        var counts = new long[PartitionCount];
        var writers = new ISpillWriter[PartitionCount];
        var uniquePrefix = Guid.NewGuid().ToString("N");
        for (var i = 0; i < PartitionCount; i++)
        {
            names[i] = $"{uniquePrefix}_{prefix}_{i}.tmp";
            tempFiles.Add(names[i]);
            writers[i] = await _context.SpillStore.CreateWriterAsync(
                names[i],
                SpillFanOut.FlushRows(_context.EffectiveBatchSize, PartitionCount),
                SpillFanOut.FlushBytes(_context.EffectiveBatchTargetBytes, PartitionCount));
        }

        try
        {
            await using var reader = await _context.SpillStore.CreateReaderAsync(sourceName);
            var columnarReader = (IColumnarSpillReader)reader;
            await foreach (var batch in columnarReader.AsColumnBatchesAsync())
            {
                using (batch)
                {
                    using var nativeRouting = ColumnBatchPartitionKernels.HashPartitionNormalized(
                        batch, keys, PartitionCount, depth,
                        cancellationToken: _context.CancellationToken);
                    var columns = batch.Schema.Fields.Select(field => field.Name).ToArray();
                    for (var partition = 0; partition < PartitionCount; partition++)
                    {
                        var routed = nativeRouting.GetPartition(partition);
                        if (routed.Length == 0) continue;
                        using var selection = SelectionVector.FromIndices(routed.Span);
                        using var compacted = ColumnBatchAdapter.Compact(
                            batch, columns, selection, _context.CancellationToken);
                        await ((IColumnarSpillWriter)writers[partition]).WriteBatchAsync(compacted);
                        counts[partition] += compacted.RowCount;
                        ColumnarRepartitionRows += compacted.RowCount;
                    }
                }
            }
        }
        finally
        {
            var usedPartitions = 0;
            for (var i = 0; i < writers.Length; i++)
            {
                if (counts[i] > 0) usedPartitions++;
                await writers[i].DisposeAsync();
            }
            _context.Telemetry.PartitionsCount += usedPartitions;
            _context.Telemetry.PartitionPassCount++;
        }
        return new PartitionSet(names, counts);
    }

    private async Task<PartitionSet> PartitionStream(IAsyncEnumerable<Row> stream, List<string> keys, string prefix, int depth, List<string> tempFiles)
    {
        var names = new string[PartitionCount];
        var counts = new long[PartitionCount];
        var writers = new ISpillWriter[PartitionCount];

        var uniquePrefix = Guid.NewGuid().ToString("N");
        for (int i = 0; i < PartitionCount; i++)
        {
            names[i] = $"{uniquePrefix}_{prefix}_{i}.tmp";
            tempFiles.Add(names[i]);
            writers[i] = await _context.SpillStore.CreateWriterAsync(
                names[i],
                SpillFanOut.FlushRows(_context.EffectiveBatchSize, PartitionCount),
                SpillFanOut.FlushBytes(_context.EffectiveBatchTargetBytes, PartitionCount));
        }

        try
        {
            bool isLeftInput = depth == 0 && prefix == "left";
            bool caseSensitive = _context.CaseSensitiveComparison;
            TableSchema? cachedSchema = null;
            int keyCount = keys.Count;
            int[] keySlots = keyCount > 0 ? new int[keyCount] : Array.Empty<int>();
            int extraCount = Math.Max(0, keyCount - 1);
            JoinKey[] extraKeyBuffer = extraCount > 0 ? new JoinKey[extraCount] : Array.Empty<JoinKey>();

            await foreach (var row in stream)
            {
                if (isLeftInput) Shape.SeeLeft(row);
                int pIdx = GetPartitionIndex(row, keys, keySlots, ref cachedSchema, extraKeyBuffer, depth, caseSensitive);
                counts[pIdx]++;
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
                    if (counts[i] > 0) usedPartitions++;
                    await writers[i].DisposeAsync();
                }
            }

            _context.Telemetry.PartitionsCount += usedPartitions;
            _context.Telemetry.PartitionPassCount++;
            _logger.Debug("Finished partitioning {Prefix}. Used {UsedPartitions} partitions. Context PartitionsCount: {PartitionsCount}", prefix, usedPartitions, _context.Telemetry.PartitionsCount);
        }

        return new PartitionSet(names, counts);
    }

    private async IAsyncEnumerable<Row> ReadPartitionStream(string name)
    {
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
        if (reader is ISortSpillReader vtReader)
        {
            while (true)
            {
                var row = await vtReader.ReadRowValueTaskAsync();
                if (row == null) yield break;
                yield return row;
            }
        }
        await foreach (var row in reader.AsEnumerableAsync())
            yield return row;
    }

    private int GetPartitionIndex(
        Row row,
        List<string> keys,
        int[] keySlots,
        ref TableSchema? cachedSchema,
        JoinKey[] extraKeyBuffer,
        int depth,
        bool caseSensitive)
    {
        int keyCount = keys.Count;
        if (keyCount == 0)
            return (JoinKey.ComputeEmptyHashCode(depth) & 0x7FFFFFFF) % PartitionCount;

        ExtractRowJoinKeys(row, keys, keySlots, ref cachedSchema, out var key0, extraKeyBuffer, out _);
        int hash = JoinKey.ComputeHashCode(depth, key0, extraKeyBuffer, caseSensitive);
        return (hash & 0x7FFFFFFF) % PartitionCount;
    }

    private static void ExtractRowJoinKeys(
        Row row,
        List<string> keys,
        int[] keySlots,
        ref TableSchema? cachedSchema,
        out JoinKey key0,
        Span<JoinKey> extraKeys,
        out bool hasNullKey)
    {
        var schema = row.Schema;
        if (schema != null && !ReferenceEquals(schema, cachedSchema))
        {
            cachedSchema = schema;
            for (int i = 0; i < keys.Count; i++)
                keySlots[i] = schema.GetIndex(keys[i]);
        }

        object? v0 = schema != null && keySlots[0] >= 0 ? row[keySlots[0]] : row[keys[0]];
        key0 = JoinKey.FromValue(v0);
        hasNullKey = key0.IsNull;

        for (int i = 1; i < keys.Count; i++)
        {
            object? vi = schema != null && keySlots[i] >= 0 ? row[keySlots[i]] : row[keys[i]];
            var extra = JoinKey.FromValue(vi);
            extraKeys[i - 1] = extra;
            if (extra.IsNull) hasNullKey = true;
        }
    }

    private CompoundKey GetHashKey(Row row, List<string> keys)
    {
        var values = new object?[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            values[i] = SpillSerializationHelper.UnwrapValue(row[keys[i]]);
        return CompoundKey.CreateJoinKey(_context.CaseSensitiveComparison, values);
    }

    // One join step sees one left and one right layout, so a single cached entry covers it.
    private CombinedLayout? _combinedLayout;

    /// <summary>
    /// Combined row layout mapping a columnar probe <see cref="ColumnBatchSchema"/> and packed
    /// build column list into one output <see cref="TableSchema"/>. Tracks which left columns are
    /// overwritten by the right side so probe matching skips reading overwritten left cells.
    /// </summary>
    private sealed class BatchCombinedLayout
    {
        public TableSchema Schema { get; }
        public int[] LeftSlots { get; }
        public bool[] LeftOverwritten { get; }
        public int[] RightSlots { get; }

        private BatchCombinedLayout(TableSchema schema, int[] leftSlots, bool[] leftOverwritten, int[] rightSlots)
        {
            Schema = schema;
            LeftSlots = leftSlots;
            LeftOverwritten = leftOverwritten;
            RightSlots = rightSlots;
        }

        public static BatchCombinedLayout Create(ColumnBatchSchema leftSchema, IReadOnlyList<string> rightColumns)
        {
            var names = new List<string>(leftSchema.Count + rightColumns.Count);
            var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var leftSlots = new int[leftSchema.Count];
            for (int i = 0; i < leftSchema.Count; i++)
            {
                var name = leftSchema.Fields[i].Name;
                if (!slots.TryGetValue(name, out var slot))
                {
                    slot = names.Count;
                    slots[name] = slot;
                    names.Add(name);
                }
                leftSlots[i] = slot;
            }

            var rightSlots = new int[rightColumns.Count];
            var rightWrittenSlots = new HashSet<int>();
            for (int i = 0; i < rightColumns.Count; i++)
            {
                var name = rightColumns[i];
                if (!slots.TryGetValue(name, out var slot))
                {
                    slot = names.Count;
                    slots[name] = slot;
                    names.Add(name);
                }
                rightSlots[i] = slot;
                rightWrittenSlots.Add(slot);
            }

            var leftOverwritten = new bool[leftSlots.Length];
            for (int i = 0; i < leftSlots.Length; i++)
                leftOverwritten[i] = rightWrittenSlots.Contains(leftSlots[i]);

            return new BatchCombinedLayout(new TableSchema(names), leftSlots, leftOverwritten, rightSlots);
        }
    }

    /// <summary>
    /// The combined row layout for one pair of input schemas: the distinct column names in
    /// first-seen order (left, then right) and, per input column, its slot. A right column whose
    /// name the left already has maps to the same slot and overwrites it, exactly as the
    /// name-keyed fallback below does.
    /// </summary>
    private sealed class CombinedLayout(TableSchema? left, TableSchema right, TableSchema schema, int[] leftSlots, int[] rightSlots)
    {
        public TableSchema? Left { get; } = left;
        public TableSchema Right { get; } = right;
        public TableSchema Schema { get; } = schema;
        public int[] LeftSlots { get; } = leftSlots;
        public int[] RightSlots { get; } = rightSlots;

        public static CombinedLayout Create(TableSchema? left, TableSchema right)
        {
            var names = new List<string>();
            var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int[] Map(TableSchema side)
            {
                var map = new int[side.ColumnCount];
                for (var i = 0; i < map.Length; i++)
                {
                    var name = side.GetName(i);
                    if (!slots.TryGetValue(name, out var slot))
                    {
                        slot = names.Count;
                        slots[name] = slot;
                        names.Add(name);
                    }
                    map[i] = slot;
                }
                return map;
            }
            var leftSlots = left == null ? Array.Empty<int>() : Map(left);
            var rightSlots = Map(right);
            return new CombinedLayout(left, right, new TableSchema(names), leftSlots, rightSlots);
        }
    }

    private Row CombineRows(Row? left, Row right)
    {
        // Rows held positionally by their schemas combine into a cached layout: one values array
        // per output row instead of a per-row name dictionary (about a third of the external
        // join's allocation before this path existed).
        var rightSchema = right.Schema;
        var leftSchema = left?.Schema;
        if (rightSchema != null && !right.HasDynamicColumns && (left == null || (leftSchema != null && !left.HasDynamicColumns)))
        {
            var layout = _combinedLayout;
            if (layout == null || !ReferenceEquals(layout.Left, leftSchema) || !ReferenceEquals(layout.Right, rightSchema))
                _combinedLayout = layout = CombinedLayout.Create(leftSchema, rightSchema);
            var values = new object?[layout.Schema.ColumnCount];
            for (var i = 0; i < layout.LeftSlots.Length; i++) values[layout.LeftSlots[i]] = left![i];
            for (var i = 0; i < layout.RightSlots.Length; i++) values[layout.RightSlots[i]] = right[i];
            return new Row(layout.Schema, values)
            {
                MayLackColumns = (left?.MayLackColumns ?? false) || right.MayLackColumns,
                DataQualityReplayProvenance = left?.DataQualityReplayProvenance,
            };
        }

        var combined = new Row();
        combined.MayLackColumns = (left?.MayLackColumns ?? false) || right.MayLackColumns;
        combined.DataQualityReplayProvenance = left?.DataQualityReplayProvenance;
        left?.ForEachColumn((name, value) => combined[name] = value);
        right.ForEachColumn((name, value) => combined[name] = value);
        return combined;
    }

    private readonly record struct PartitionSet(string[] Names, long[] Counts);

    /// <summary>
    /// The probe-side hash table: build rows held in budget-capped pooled <c>byte[]</c> slabs with
    /// a pooled flat <see cref="RowSlot"/> index chaining duplicate keys in insertion order, indexed
    /// by a pooled unboxed <see cref="JoinKey"/> hash table.
    /// <see cref="Columns"/> is the shared column order captured from the first build row, used to
    /// decode a slab slice back into a <see cref="Row"/> on a probe match.
    /// </summary>
    internal sealed class PackedBuildTable : IDisposable
    {
        internal const int MinSlabBytes = 4 * 1024;
        internal const int MaxDefaultSlabBytes = 64 * 1024;
        private const int InitialSlotCapacity = 64;
        private const int InitialBucketCapacity = 128;

        private sealed class Slab
        {
            public readonly byte[] Buffer;
            public int Used;
            public Slab(byte[] buffer) => Buffer = buffer;
        }

        internal struct RowSlot
        {
            public int SlabIndex;
            public int Offset;
            public int Length;
            public int NextInBucket;
        }

        private struct KeyEntry
        {
            public int HashCode;
            public JoinKey Key0;
            public int ExtraKeyOffset;
            public int Head;
            public int Tail;
            public int NextInHash;
        }

        internal readonly record struct BucketChain(int Head, int Tail);

        private readonly bool _caseSensitive;
        private readonly int _keyCount;
        private readonly int _extraKeyCount;
        private readonly List<Slab> _slabs = new();
        private RowSlot[] _rows;
        private int _rowCount;
        private int[] _buckets;
        private KeyEntry[] _entries;
        private int _entryCount;
        private JoinKey[]? _extraKeys;
        private int _extraKeyCountUsed;
        private bool _disposed;

        public readonly List<string> Columns = new();
        public int TargetSlabBytes { get; }

        public PackedBuildTable(long budgetBytes = 0, bool caseSensitive = false, int keyCount = 1, int expectedRows = 0)
        {
            _caseSensitive = caseSensitive;
            _keyCount = Math.Max(0, keyCount);
            _extraKeyCount = Math.Max(0, _keyCount - 1);
            TargetSlabBytes = budgetBytes > 0
                ? (int)Math.Clamp(budgetBytes / 4, MinSlabBytes, MaxDefaultSlabBytes)
                : MaxDefaultSlabBytes;
            int slotCap = expectedRows > 0
                ? Math.Clamp(expectedRows, InitialSlotCapacity, 1 << 20)
                : InitialSlotCapacity;
            int bucketTarget = expectedRows > 0
                ? Math.Clamp((int)Math.Min(1 << 20, expectedRows * 2L), InitialBucketCapacity, 1 << 20)
                : InitialBucketCapacity;
            int bucketCap = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)bucketTarget);
            _rows = ArrayPool<RowSlot>.Shared.Rent(slotCap);
            _buckets = ArrayPool<int>.Shared.Rent(bucketCap);
            Array.Fill(_buckets, -1);
            _entries = ArrayPool<KeyEntry>.Shared.Rent(slotCap);
            if (_extraKeyCount > 0)
                _extraKeys = ArrayPool<JoinKey>.Shared.Rent(slotCap * _extraKeyCount);
        }

        public int RowCount => _rowCount;
        public int SlabCount => _slabs.Count;

        public (int RowIndex, int ByteLength) AppendRowBytes(byte[] source, int sourceOffset, int length)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int slabIndex = _slabs.Count - 1;
            if (slabIndex < 0 || length > _slabs[slabIndex].Buffer.Length - _slabs[slabIndex].Used)
            {
                int requestBytes = Math.Max(TargetSlabBytes, Math.Max(1, length));
                _slabs.Add(new Slab(ArrayPool<byte>.Shared.Rent(requestBytes)));
                slabIndex = _slabs.Count - 1;
            }

            var slab = _slabs[slabIndex];
            int offset = slab.Used;
            if (length > 0)
            {
                Buffer.BlockCopy(source, sourceOffset, slab.Buffer, offset, length);
                slab.Used += length;
            }

            int rowIndex = _rowCount;
            if (rowIndex == _rows.Length)
            {
                var grown = ArrayPool<RowSlot>.Shared.Rent(_rows.Length * 2);
                Array.Copy(_rows, 0, grown, 0, _rowCount);
                ArrayPool<RowSlot>.Shared.Return(_rows, clearArray: true);
                _rows = grown;
            }

            _rows[rowIndex] = new RowSlot
            {
                SlabIndex = slabIndex,
                Offset = offset,
                Length = length,
                NextInBucket = -1,
            };
            _rowCount++;
            return (rowIndex, length);
        }

        public void IndexRow(CompoundKey key, int rowIndex, int blobLength, MemoryBudgetGuard guard)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int keyLen = key.Length;
            if (keyLen == 0)
            {
                IndexRow(JoinKey.Null, ReadOnlySpan<JoinKey>.Empty, hasNullKey: false, rowIndex, blobLength, guard);
                return;
            }

            var key0 = JoinKey.FromValue(key[0]);
            bool hasNullKey = key0.IsNull;
            int extraLen = keyLen - 1;
            var extraKeys = extraLen == 0 ? Array.Empty<JoinKey>() : new JoinKey[extraLen];
            for (int i = 1; i < keyLen; i++)
            {
                var extra = JoinKey.FromValue(key[i]);
                extraKeys[i - 1] = extra;
                if (extra.IsNull) hasNullKey = true;
            }
            IndexRow(key0, extraKeys, hasNullKey, rowIndex, blobLength, guard);
        }

        public void IndexRow(
            in JoinKey key0,
            ReadOnlySpan<JoinKey> extraKeys,
            bool hasNullKey,
            int rowIndex,
            int blobLength,
            MemoryBudgetGuard guard)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            guard.Add(blobLength + 16);
            if (hasNullKey) return;

            int hash = _keyCount == 0
                ? JoinKey.ComputeEmptyHashCode(0)
                : JoinKey.ComputeHashCode(0, key0, extraKeys, _caseSensitive);
            int bucket = (hash & 0x7FFFFFFF) & (_buckets.Length - 1);

            for (int entryIdx = _buckets[bucket]; entryIdx >= 0; entryIdx = _entries[entryIdx].NextInHash)
            {
                ref var existing = ref _entries[entryIdx];
                if (existing.HashCode == hash && KeysMatch(in existing, in key0, extraKeys))
                {
                    _rows[existing.Tail].NextInBucket = rowIndex;
                    existing.Tail = rowIndex;
                    return;
                }
            }

            if (_entryCount == _entries.Length)
                GrowEntries();
            if (_entryCount >= (_buckets.Length * 3) / 4)
            {
                GrowBuckets();
                bucket = (hash & 0x7FFFFFFF) & (_buckets.Length - 1);
            }

            int extraOffset = -1;
            if (_extraKeyCount > 0)
            {
                extraOffset = _extraKeyCountUsed;
                EnsureExtraKeyCapacity(_extraKeyCountUsed + _extraKeyCount);
                extraKeys.CopyTo(_extraKeys.AsSpan(extraOffset, _extraKeyCount));
                _extraKeyCountUsed += _extraKeyCount;
            }

            int newEntryIdx = _entryCount++;
            _entries[newEntryIdx] = new KeyEntry
            {
                HashCode = hash,
                Key0 = key0,
                ExtraKeyOffset = extraOffset,
                Head = rowIndex,
                Tail = rowIndex,
                NextInHash = _buckets[bucket],
            };
            _buckets[bucket] = newEntryIdx;

            guard.Add(EstimateJoinKeyBytes(in key0, extraKeys) + 32);
        }

        public bool TryGetChain(in JoinKey key0, ReadOnlySpan<JoinKey> extraKeys, out BucketChain chain)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entryCount == 0)
            {
                chain = default;
                return false;
            }

            int hash = _keyCount == 0
                ? JoinKey.ComputeEmptyHashCode(0)
                : JoinKey.ComputeHashCode(0, key0, extraKeys, _caseSensitive);
            int bucket = (hash & 0x7FFFFFFF) & (_buckets.Length - 1);

            for (int entryIdx = _buckets[bucket]; entryIdx >= 0; entryIdx = _entries[entryIdx].NextInHash)
            {
                ref readonly var entry = ref _entries[entryIdx];
                if (entry.HashCode == hash && KeysMatch(in entry, in key0, extraKeys))
                {
                    chain = new BucketChain(entry.Head, entry.Tail);
                    return true;
                }
            }

            chain = default;
            return false;
        }

        public bool TryGetChain(CompoundKey key, out BucketChain chain)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entryCount == 0)
            {
                chain = default;
                return false;
            }

            int keyLen = key.Length;
            if (keyLen == 0)
                return TryGetChain(JoinKey.Null, ReadOnlySpan<JoinKey>.Empty, out chain);

            var key0 = JoinKey.FromValue(key[0]);
            if (key0.IsNull)
            {
                chain = default;
                return false;
            }

            int extraLen = keyLen - 1;
            var extraKeys = extraLen == 0 ? Array.Empty<JoinKey>() : new JoinKey[extraLen];
            for (int i = 1; i < keyLen; i++)
            {
                var extra = JoinKey.FromValue(key[i]);
                if (extra.IsNull)
                {
                    chain = default;
                    return false;
                }
                extraKeys[i - 1] = extra;
            }
            return TryGetChain(key0, extraKeys, out chain);
        }

        private bool KeysMatch(in KeyEntry entry, in JoinKey key0, ReadOnlySpan<JoinKey> extraKeys)
        {
            if (_keyCount == 0) return true;
            if (!JoinKey.Equals(in entry.Key0, in key0, _caseSensitive)) return false;
            if (_extraKeyCount == 0) return true;

            var stored = _extraKeys.AsSpan(entry.ExtraKeyOffset, _extraKeyCount);
            for (int i = 0; i < _extraKeyCount; i++)
            {
                if (!JoinKey.Equals(in stored[i], in extraKeys[i], _caseSensitive))
                    return false;
            }
            return true;
        }

        private static long EstimateJoinKeyBytes(in JoinKey key0, ReadOnlySpan<JoinKey> extraKeys)
        {
            long bytes = 32 + 8 + key0.EstimateHeapBytes();
            for (int i = 0; i < extraKeys.Length; i++)
                bytes += 8 + extraKeys[i].EstimateHeapBytes();
            return bytes;
        }

        private void GrowEntries()
        {
            var grown = ArrayPool<KeyEntry>.Shared.Rent(_entries.Length * 2);
            Array.Copy(_entries, 0, grown, 0, _entryCount);
            ArrayPool<KeyEntry>.Shared.Return(_entries, clearArray: true);
            _entries = grown;
        }

        private void GrowBuckets()
        {
            int newSize = _buckets.Length * 2;
            var grown = ArrayPool<int>.Shared.Rent(newSize);
            Array.Fill(grown, -1);
            int mask = grown.Length - 1;
            for (int i = 0; i < _entryCount; i++)
            {
                int bucket = (_entries[i].HashCode & 0x7FFFFFFF) & mask;
                _entries[i].NextInHash = grown[bucket];
                grown[bucket] = i;
            }
            ArrayPool<int>.Shared.Return(_buckets, clearArray: false);
            _buckets = grown;
        }

        private void EnsureExtraKeyCapacity(int required)
        {
            if (_extraKeys != null && required <= _extraKeys.Length) return;
            int nextSize = _extraKeys == null ? InitialSlotCapacity * _extraKeyCount : Math.Max(_extraKeys.Length * 2, required);
            var grown = ArrayPool<JoinKey>.Shared.Rent(nextSize);
            if (_extraKeys != null)
            {
                if (_extraKeyCountUsed > 0)
                    Array.Copy(_extraKeys, 0, grown, 0, _extraKeyCountUsed);
                ArrayPool<JoinKey>.Shared.Return(_extraKeys, clearArray: true);
            }
            _extraKeys = grown;
        }

        public Row UnpackRow(int rowIndex)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var slot = _rows[rowIndex];
            return RowPacker.Unpack(_slabs[slot.SlabIndex].Buffer, slot.Offset, slot.Length, Columns);
        }

        public void UnpackRowInto(int rowIndex, object?[] destination, int[] targetSlots)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var slot = _rows[rowIndex];
            RowPacker.UnpackInto(_slabs[slot.SlabIndex].Buffer.AsSpan(slot.Offset, slot.Length), Columns.Count, destination, targetSlots);
        }

        public int GetNextInBucket(int rowIndex)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _rows[rowIndex].NextInBucket;
        }

        internal IReadOnlyList<(byte[] Buffer, int Used)> GetSlabBuffersForTests() =>
            _slabs.Select(s => (s.Buffer, s.Used)).ToArray();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var i = 0; i < _slabs.Count; i++)
            {
                var slab = _slabs[i];
                if (slab.Used > 0)
                    CryptographicOperations.ZeroMemory(slab.Buffer.AsSpan(0, slab.Used));
                ArrayPool<byte>.Shared.Return(slab.Buffer);
            }
            _slabs.Clear();
            if (_rows != null)
            {
                ArrayPool<RowSlot>.Shared.Return(_rows, clearArray: true);
                _rows = null!;
            }
            if (_buckets != null)
            {
                ArrayPool<int>.Shared.Return(_buckets, clearArray: false);
                _buckets = null!;
            }
            if (_entries != null)
            {
                ArrayPool<KeyEntry>.Shared.Return(_entries, clearArray: true);
                _entries = null!;
            }
            if (_extraKeys != null)
            {
                ArrayPool<JoinKey>.Shared.Return(_extraKeys, clearArray: true);
                _extraKeys = null;
            }
            _rowCount = 0;
            _entryCount = 0;
            _extraKeyCountUsed = 0;
            Columns.Clear();
        }
    }
}
