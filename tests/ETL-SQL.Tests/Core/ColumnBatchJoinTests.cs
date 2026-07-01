using System;
using System.Linq;
using System.Threading;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class ColumnBatchJoinTests
{
    [Fact]
    public void InnerJoinProducesDuplicateCrossProductAndExcludesNulls()
    {
        using var left = CreateBatch(new[] { 1, 2, 2, 0 }, 0b0000_1000);
        using var right = CreateBatch(new[] { 2, 2, 3, 0 }, 0b0000_1000);
        using var pairs = ColumnBatchJoinKernels.InnerJoin<int>(left, "Key", right, "Key");

        Assert.Equal(4, pairs.Count);
        Assert.Equal(new[] { 1, 1, 2, 2 }, pairs.LeftRows.ToArray());
        Assert.Equal(new[] { 0, 1, 0, 1 }, pairs.RightRows.ToArray());
    }

    [Fact]
    public void InnerJoinConsumesSelectionsOnBothSides()
    {
        using var left = CreateBatch(new[] { 1, 2, 2, 0 }, 0b0000_1000);
        using var right = CreateBatch(new[] { 2, 2, 3, 0 }, 0b0000_1000);
        using var leftSelection = ColumnBatchKernels.SelectComparison(left, "Payload", ColumnComparison.Equal, 20);
        using var rightSelection = ColumnBatchKernels.SelectComparison(right, "Payload", ColumnComparison.Equal, 10);
        using var pairs = ColumnBatchJoinKernels.InnerJoin<int>(
            left, "Key", right, "Key", leftSelection: leftSelection, rightSelection: rightSelection);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(new[] { 1, 2 }, pairs.LeftRows.ToArray());
        Assert.Equal(new[] { 0, 0 }, pairs.RightRows.ToArray());
    }

    [Fact]
    public void JoinResultHoldsMemoryGrantUntilDisposed()
    {
        using var left = CreateBatch(new[] { 1, 2, 2, 0 }, 0b0000_1000);
        using var right = CreateBatch(new[] { 2, 2, 3, 0 }, 0b0000_1000);
        var arbiter = new MemoryGrantArbiter(1_000_000);
        var pairs = ColumnBatchJoinKernels.InnerJoin<int>(left, "Key", right, "Key", arbiter);

        Assert.Equal(pairs.ReservedBytes, arbiter.ReservedBytes);
        pairs.Dispose();
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public void JoinFailsBoundedlyAndCancellationReleasesState()
    {
        using var left = CreateBatch(new[] { 1, 2, 2, 0 }, 0b0000_1000);
        using var right = CreateBatch(new[] { 2, 2, 3, 0 }, 0b0000_1000);
        var constrained = new MemoryGrantArbiter(1);
        Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            ColumnBatchJoinKernels.InnerJoin<int>(left, "Key", right, "Key", constrained));
        Assert.Equal(0, constrained.ReservedBytes);

        var cancellable = new MemoryGrantArbiter(1_000_000);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ColumnBatchJoinKernels.InnerJoin<int>(
                left, "Key", right, "Key", cancellable, cancellationToken: cancellation.Token));
        Assert.Equal(0, cancellable.ReservedBytes);
    }

    private static ColumnBatch CreateBatch(int[] keys, byte nullBitmap)
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(int), "INT"),
            new ColumnBatchField("Payload", typeof(int), "INT")
        });
        return new ColumnBatch(schema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(keys, keys.Length, new[] { nullBitmap }),
            new ColumnBuffer<int>(new[] { 10, 20, 20, 40 }, keys.Length)
        }, keys.Length);
    }
}
