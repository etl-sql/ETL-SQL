using System;

namespace ETL_SQL.Core.Spill;

/// <summary>
/// Sizes the row buffer of each spill writer when an operator keeps one writer open per partition.
/// A writer buffers rows until it flushes a batch, so with a full batch per writer the buffered rows
/// grew with the partition count, and the planners raise the partition count with the input size:
/// memory followed the input instead of the batch size. The writers share one budget of
/// <see cref="BufferedBatches"/> batches instead, never flushing below
/// <see cref="MinimumFlushRows"/> rows so very wide fan-outs still write useful batches.
/// </summary>
public static class SpillFanOut
{
    /// <summary>Batches' worth of rows that all open partition writers buffer together.</summary>
    public const int BufferedBatches = 16;

    /// <summary>Smallest flush size a partition writer uses, unless the batch is smaller.</summary>
    public const int MinimumFlushRows = 256;

    /// <summary>Rows each of <paramref name="partitionCount"/> open writers buffers before flushing.</summary>
    public static int FlushRows(int batchRows, int partitionCount)
    {
        batchRows = Math.Max(1, batchRows);
        if (partitionCount <= 1) return batchRows;
        var shared = (long)batchRows * BufferedBatches / partitionCount;
        return (int)Math.Clamp(shared, Math.Min(MinimumFlushRows, batchRows), batchRows);
    }
}
