using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Provides stateless batch-stream pipeline transformations: progress interception,
/// column alignment, and FOR JSON / FOR XML serialization.
/// </summary>
internal sealed class BatchPipelineHelper
{
    /// <summary>
    /// Wraps a batch stream to fire a progress callback after each batch is yielded.
    /// </summary>
    public async IAsyncEnumerable<DataTable> InterceptProgress(
        IAsyncEnumerable<DataTable> chunks,
        Action<long>? onBatchProcessed,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            onBatchProcessed?.Invoke(chunk.Rows.Count);
            yield return chunk;
        }
    }

    /// <summary>
    /// Re-maps each batch so its columns match <paramref name="targetCols"/> by name,
    /// falling back to positional mapping when a name is not present.
    /// </summary>
    public async IAsyncEnumerable<DataTable> AlignColumns(
        IAsyncEnumerable<DataTable> batches,
        List<string> targetCols,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var batch in batches.WithCancellation(ct))
            yield return await AlignBatch(batch, targetCols);
    }

    /// <summary>
    /// Serializes batches as a single FOR JSON or FOR XML scalar result.
    /// </summary>
    public async IAsyncEnumerable<DataTable> EvaluateForClause(
        IAsyncEnumerable<DataTable> batches,
        ForClause forClause,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (forClause.Type == ForType.JSON)
        {
            var json = await ResultFormatter.FormatJsonAsync(
                batches, forClause.Mode, forClause.RootName,
                forClause.IncludeNullValues, forClause.WithoutArrayWrapper);
            var result = new DataTable();
            result.SetColumns(new[] { "JSON_F52E2B61" });
            var row = result.NewRow();
            row[0] = json;
            await result.AddRowAsync(row);
            yield return result;
        }
        else if (forClause.Type == ForType.XML)
        {
            var xml = await ResultFormatter.FormatXmlAsync(
                batches, forClause.Mode, forClause.RootName,
                forClause.IncludeNullValues, forClause.UseElements);
            var result = new DataTable();
            result.SetColumns(new[] { "XML_F52E2B61" });
            var row = result.NewRow();
            row[0] = xml;
            await result.AddRowAsync(row);
            yield return result;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private static async Task<DataTable> AlignBatch(DataTable batch, List<string> targetCols)
    {
        var newBatch = new DataTable();
        newBatch.SetColumns(targetCols);
        foreach (Row oldRow in batch.Rows)
            await newBatch.AddRowAsync(MapRow(oldRow, batch.ColumnNames, newBatch));
        return newBatch;
    }

    private static Row MapRow(Row oldRow, IReadOnlyList<string> sourceCols, DataTable targetTable)
    {
        var newRow = targetTable.NewRow();
        var targetCols = targetTable.ColumnNames;
        for (int i = 0; i < targetCols.Count; i++)
        {
            var target = targetCols[i];
            if (oldRow.HasColumn(target))
                newRow[i] = oldRow[target];
            else if (i < sourceCols.Count && !targetCols.Contains(sourceCols[i], StringComparer.OrdinalIgnoreCase))
                newRow[i] = oldRow[sourceCols[i]];
            else
                newRow[i] = null;
        }
        return newRow;
    }

    /// <summary>
    /// Decouples reading from writing using an asynchronous byte- and batch-bounded channel.
    /// Splits oversized batches and blocks the producer when queued bytes exceed the budget.
    /// </summary>
    public async IAsyncEnumerable<DataTable> Buffer(
        IAsyncEnumerable<DataTable> source,
        [EnumeratorCancellation] CancellationToken ct = default,
        long maxQueuedBytes = 0,
        long targetBatchBytes = 0)
    {
        long effectiveTargetBytes = targetBatchBytes > 0
            ? targetBatchBytes
            : ETL_SQL.Core.Spill.SpillFanOut.DefaultTargetBatchBytes;
        long effectiveMaxQueuedBytes = maxQueuedBytes > 0
            ? maxQueuedBytes
            : effectiveTargetBytes <= long.MaxValue / 2 ? effectiveTargetBytes * 2 : long.MaxValue;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producerCt = linkedCts.Token;

        // Bound both batch count (4) and total queued heap bytes so wide strings cannot outrun consumers.
        var channel = System.Threading.Channels.Channel.CreateBounded<(DataTable Batch, long Bytes)>(
            new System.Threading.Channels.BoundedChannelOptions(4)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
            });

        object gate = new();
        long queuedBytes = 0;
        int queuedBatches = 0;
        TaskCompletionSource<bool>? spaceAvailable = null;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await foreach (var rawBatch in source.WithCancellation(producerCt))
                {
                    foreach (var (subBatch, batchBytes) in SplitByByteBudget(rawBatch, effectiveTargetBytes))
                    {
                        while (true)
                        {
                            System.Threading.Tasks.Task? waitTask = null;
                            lock (gate)
                            {
                                if (queuedBatches == 0 || queuedBytes + batchBytes <= effectiveMaxQueuedBytes)
                                {
                                    queuedBytes += batchBytes;
                                    queuedBatches++;
                                    break;
                                }
                                spaceAvailable ??= new System.Threading.Tasks.TaskCompletionSource<bool>(
                                    System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                                waitTask = spaceAvailable.Task;
                            }
                            await waitTask.WaitAsync(producerCt).ConfigureAwait(false);
                        }

                        try
                        {
                            await channel.Writer.WriteAsync((subBatch, batchBytes), producerCt).ConfigureAwait(false);
                        }
                        catch
                        {
                            System.Threading.Tasks.TaskCompletionSource<bool>? toRelease = null;
                            lock (gate)
                            {
                                queuedBytes = Math.Max(0, queuedBytes - batchBytes);
                                if (queuedBatches > 0) queuedBatches--;
                                toRelease = spaceAvailable;
                                spaceAvailable = null;
                            }
                            toRelease?.TrySetResult(true);
                            throw;
                        }
                    }
                }
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, producerCt);

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    System.Threading.Tasks.TaskCompletionSource<bool>? toRelease = null;
                    lock (gate)
                    {
                        queuedBytes = Math.Max(0, queuedBytes - item.Bytes);
                        if (queuedBatches > 0) queuedBatches--;
                        toRelease = spaceAvailable;
                        spaceAvailable = null;
                    }
                    toRelease?.TrySetResult(true);
                    yield return item.Batch;
                }
            }
        }
        finally
        {
            linkedCts.Cancel();
        }
    }

    private static IEnumerable<(DataTable Batch, long Bytes)> SplitByByteBudget(DataTable batch, long targetBatchBytes)
    {
        long totalBytes = EstimateBatchBytes(batch);
        if (batch.Rows.Count <= 1 || targetBatchBytes <= 0 || totalBytes <= targetBatchBytes)
        {
            yield return (batch, totalBytes);
            yield break;
        }

        var sub = new DataTable { Schema = batch.Schema };
        long subBytes = 0;
        foreach (var row in batch.Rows)
        {
            long rowBytes = row.EstimateHeapBytes();
            if (sub.Rows.Count > 0 && subBytes + rowBytes > targetBatchBytes)
            {
                yield return (sub, subBytes);
                sub = new DataTable { Schema = batch.Schema };
                subBytes = 0;
            }
            sub.Rows.Add(row);
            subBytes += rowBytes;
        }
        if (sub.Rows.Count > 0)
            yield return (sub, subBytes);
    }

    private static long EstimateBatchBytes(DataTable batch)
    {
        long total = 0;
        foreach (var row in batch.Rows)
            total += row.EstimateHeapBytes();
        return total;
    }
}
