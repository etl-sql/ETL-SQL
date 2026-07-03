using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
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

    [Fact]
    public void Utf8RoutingUsesCompoundKeyNormalizationAndSalt()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(string), "VARCHAR(20)")
        });
        using var batch = new ColumnBatch(schema, new IColumnBuffer[]
        {
            Utf8ColumnBuffer.FromStrings(new string?[] { "A", " A ", "1", "1.0", null, "a" })
        }, 6);
        using var routing = ColumnBatchPartitionKernels.HashPartitionNormalizedUtf8(
            batch, "Key", 7, hashSalt: 3);

        var partitionByRow = new Dictionary<int, int>();
        for (var partition = 0; partition < routing.PartitionCount; partition++)
            foreach (var row in routing.GetPartition(partition).Span) partitionByRow[row] = partition;
        Assert.Equal(partitionByRow[0], partitionByRow[1]);
        Assert.Equal(partitionByRow[2], partitionByRow[3]);
        Assert.Equal(
            new CompoundKey(3, new object?[] { 1m }).GetHashCode(),
            CompoundKey.GetNormalizedHashCode(3, 1m));
        Assert.Equal(
            (CompoundKey.GetNormalizedHashCode(3, null) & 0x7fffffff) % 7,
            partitionByRow[4]);
    }

    [Fact]
    public void CompositeRoutingMatchesCompoundKeyHashWithoutPerRowKeyArrays()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("TextKey", typeof(string), "VARCHAR(20)"),
            new ColumnBatchField("NumberKey", typeof(int), "INT")
        });
        using var batch = new ColumnBatch(schema, new IColumnBuffer[]
        {
            Utf8ColumnBuffer.FromStrings(new string?[] { "A", " A ", "1", "1.0", null }),
            new ColumnBuffer<int>(new[] { 5, 5, 7, 7, 5 }, 5)
        }, 5);
        using var routing = ColumnBatchPartitionKernels.HashPartitionNormalized(
            batch, new[] { "TextKey", "NumberKey" }, 11, hashSalt: 4);

        var partitionByRow = new Dictionary<int, int>();
        for (var partition = 0; partition < routing.PartitionCount; partition++)
            foreach (var row in routing.GetPartition(partition).Span) partitionByRow[row] = partition;
        Assert.Equal(partitionByRow[0], partitionByRow[1]);
        Assert.Equal(partitionByRow[2], partitionByRow[3]);
        Assert.Equal(
            (new CompoundKey(4, new object?[] { "A", 5 }).GetHashCode() & 0x7fffffff) % 11,
            partitionByRow[0]);
        Assert.Equal(
            (new CompoundKey(4, new object?[] { null, 5 }).GetHashCode() & 0x7fffffff) % 11,
            partitionByRow[4]);
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
