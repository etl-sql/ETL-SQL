using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class ColumnBatchPartitioningTests
{
    [Fact]
    public void HashRoutingVisitsEveryRowExactlyOnceAndKeepsEqualKeysTogether()
    {
        using var batch = CreateBatch();
        using var routing = ColumnBatchPartitionKernels.HashPartition<int>(batch, "Key", 3);

        var routed = Enumerable.Range(0, routing.PartitionCount)
            .SelectMany(partition => routing.GetPartition(partition).ToArray())
            .OrderBy(row => row)
            .ToArray();
        Assert.Equal(Enumerable.Range(0, batch.RowCount), routed);

        var partitionByRow = new Dictionary<int, int>();
        for (var partition = 0; partition < routing.PartitionCount; partition++)
            foreach (var row in routing.GetPartition(partition).Span)
                partitionByRow[row] = partition;
        Assert.Equal(partitionByRow[0], partitionByRow[2]); // equal key 10
        Assert.Equal(partitionByRow[3], partitionByRow[4]); // NULL keys route together
        Assert.Equal(0, partitionByRow[3]);
    }

    [Fact]
    public void HashRoutingConsumesOnlySelectedRows()
    {
        using var batch = CreateBatch();
        using var selected = ColumnBatchKernels.SelectComparison(batch, "Value", ColumnComparison.GreaterThan, 2);
        using var routing = ColumnBatchPartitionKernels.HashPartition<int>(batch, "Key", 4, selected);

        var routed = Enumerable.Range(0, routing.PartitionCount)
            .SelectMany(partition => routing.GetPartition(partition).ToArray())
            .OrderBy(row => row)
            .ToArray();
        Assert.Equal(new[] { 2, 3, 4 }, routed);
        Assert.Equal(selected.Count, routing.RowCount);
    }

    [Fact]
    public void CancellationReturnsAllTemporaryPoolRentals()
    {
        using var batch = CreateBatch();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ColumnBatchPartitionKernels.HashPartition<int>(
                batch, "Key", 4, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void RoutingRejectsInvalidPartitionCounts()
    {
        using var batch = CreateBatch();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ColumnBatchPartitionKernels.HashPartition<int>(batch, "Key", 0));
    }

    private static ColumnBatch CreateBatch()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(int), "INT"),
            new ColumnBatchField("Value", typeof(int), "INT")
        });
        var keys = new ColumnBuffer<int>(new[] { 10, 20, 10, 0, 0 }, 5, new byte[] { 0b0001_1000 });
        var values = new ColumnBuffer<int>(new[] { 1, 2, 3, 4, 5 }, 5);
        return new ColumnBatch(schema, new IColumnBuffer[] { keys, values }, 5);
    }
}
