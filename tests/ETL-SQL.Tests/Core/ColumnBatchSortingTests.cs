using System;
using System.Linq;
using System.Threading;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class ColumnBatchSortingTests
{
    [Fact]
    public void SortRunOrdersTypedKeysNullsAndTiesWithoutRows()
    {
        using var batch = CreateBatch();
        using var run = ColumnBatchSortKernels.CreateRun<int>(batch, "Key");
        Assert.Equal(new[] { 2, 1, 4, 0, 3 }, run.Ordinals.ToArray());
    }

    [Fact]
    public void DescendingOrderKeepsExplicitNullPlacement()
    {
        using var batch = CreateBatch();
        using var run = ColumnBatchSortKernels.CreateRun<int>(
            batch, "Key", descending: true, nullsFirst: false);
        Assert.Equal(new[] { 0, 3, 1, 4, 2 }, run.Ordinals.ToArray());
    }

    [Fact]
    public void SortRunConsumesSelectionAndHoldsResultGrant()
    {
        using var batch = CreateBatch();
        using var selected = ColumnBatchKernels.SelectComparison(batch, "Payload", ColumnComparison.GreaterThan, 15);
        var arbiter = new MemoryGrantArbiter(1_000_000);
        var run = ColumnBatchSortKernels.CreateRun<int>(
            batch, "Key", memoryArbiter: arbiter, selection: selected);

        Assert.Equal(new[] { 2, 1, 3 }, run.Ordinals.ToArray());
        Assert.Equal(run.ReservedBytes, arbiter.ReservedBytes);
        run.Dispose();
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public void SortRunFailsBoundedlyAndHonorsCancellation()
    {
        using var batch = CreateBatch();
        var constrained = new MemoryGrantArbiter(1);
        Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            ColumnBatchSortKernels.CreateRun<int>(batch, "Key", memoryArbiter: constrained));
        Assert.Equal(0, constrained.ReservedBytes);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ColumnBatchSortKernels.CreateRun<int>(batch, "Key", cancellationToken: cancellation.Token));
    }

    private static ColumnBatch CreateBatch()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(int), "INT"),
            new ColumnBatchField("Payload", typeof(int), "INT")
        });
        return new ColumnBatch(schema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(new[] { 3, 1, 0, 3, 1 }, 5, new byte[] { 0b0000_0100 }),
            new ColumnBuffer<int>(new[] { 10, 20, 30, 40, 5 }, 5)
        }, 5);
    }
}
