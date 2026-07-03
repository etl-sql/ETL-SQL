using System;
using System.Threading;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class ColumnBatchGroupingTests
{
    [Fact]
    public void AverageIsNullWhenGroupHasNoNonNullValues()
    {
        var state = new NativeAggregateState<int>(RowCount: 3, NonNullCount: 0, Sum: 0, Min: 0, Max: 0);
        Assert.Null(state.Average);
    }

    [Fact]
    public void GroupsTypedAndNullKeysWithSqlAggregateState()
    {
        using var batch = CreateBatch();
        using var result = ColumnBatchGroupKernels.GroupAggregate<int, int>(batch, "Key", "Value");

        Assert.Equal(3, result.Groups.Count);
        var one = result.Groups[new NativeGroupKey<int>(false, 1)];
        Assert.Equal(2, one.RowCount);
        Assert.Equal(1, one.NonNullCount);
        Assert.Equal(10m, one.Sum);
        Assert.Equal(10m, one.Average);
        Assert.Equal(10, one.Min);
        Assert.Equal(10, one.Max);

        var nullKey = result.Groups[new NativeGroupKey<int>(true, default)];
        Assert.Equal(2, nullKey.RowCount);
        Assert.Equal(1, nullKey.NonNullCount);
        Assert.Equal(7m, nullKey.Sum);
        Assert.Equal(7m, nullKey.Average);

        var two = result.Groups[new NativeGroupKey<int>(false, 2)];
        Assert.Equal(5m, two.Average);
    }

    [Fact]
    public void GroupingConsumesSelectionVectorsDirectly()
    {
        using var batch = CreateBatch();
        using var selected = ColumnBatchKernels.SelectComparison(batch, "Value", ColumnComparison.GreaterThan, 6);
        using var result = ColumnBatchGroupKernels.GroupAggregate<int, int>(
            batch, "Key", "Value", selection: selected);

        Assert.Equal(2, result.Groups.Count);
        Assert.Equal(10m, result.Groups[new NativeGroupKey<int>(false, 1)].Sum);
        Assert.Equal(7m, result.Groups[new NativeGroupKey<int>(true, default)].Sum);
    }

    [Fact]
    public void GroupStateHoldsAndReleasesItsMemoryGrant()
    {
        using var batch = CreateBatch();
        var arbiter = new MemoryGrantArbiter(1_000_000);
        var result = ColumnBatchGroupKernels.GroupAggregate<int, int>(
            batch, "Key", "Value", memoryArbiter: arbiter);

        Assert.Equal(result.EstimatedBytes, arbiter.ReservedBytes);
        Assert.True(result.EstimatedBytes > 0);
        result.Dispose();
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public void GroupStateAccumulatesAcrossBatchesUnderOneMemoryGrant()
    {
        using var first = CreateBatch();
        using var second = new ColumnBatch(first.Schema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(new[] { 1, 3 }, 2),
            new ColumnBuffer<int>(new[] { 20, 4 }, 2)
        }, 2);
        var arbiter = new MemoryGrantArbiter(1_000_000);
        using var result = ColumnBatchGroupKernels.GroupAggregate<int, int>(
            first, "Key", "Value", memoryArbiter: arbiter);
        var firstReservation = result.EstimatedBytes;

        result.Accumulate(second, "Key", "Value");

        var one = result.Groups[new NativeGroupKey<int>(false, 1)];
        Assert.Equal(3, one.RowCount);
        Assert.Equal(2, one.NonNullCount);
        Assert.Equal(30m, one.Sum);
        Assert.Equal(15m, one.Average);
        Assert.Equal(4, result.Groups.Count);
        Assert.True(result.EstimatedBytes > firstReservation);
        Assert.Equal(result.EstimatedBytes, arbiter.ReservedBytes);
    }

    [Fact]
    public void KeyOnlyCountStateAccumulatesNullKeysAcrossBatches()
    {
        using var first = CreateBatch();
        using var second = CreateBatch();
        var arbiter = new MemoryGrantArbiter(1_000_000);
        using var result = new NativeGroupCountResult<int>(arbiter);

        result.Accumulate(first, "Key");
        result.Accumulate(second, "Key");

        Assert.Equal(4, result.Groups[new NativeGroupKey<int>(false, 1)]);
        Assert.Equal(4, result.Groups[new NativeGroupKey<int>(true, default)]);
        Assert.Equal(result.EstimatedBytes, arbiter.ReservedBytes);
    }

    [Fact]
    public void StringCountStateNormalizesTrimmedNumericAndNullKeysAcrossBatches()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(string), "VARCHAR(20)")
        });
        using var first = new ColumnBatch(schema, new IColumnBuffer[]
        {
            Utf8ColumnBuffer.FromStrings(new string?[] { "A", " A ", "1", null })
        }, 4);
        using var second = new ColumnBatch(schema, new IColumnBuffer[]
        {
            Utf8ColumnBuffer.FromStrings(new string?[] { "1.0", "a", null })
        }, 3);
        var arbiter = new MemoryGrantArbiter(1_000_000);
        using var result = new NativeStringGroupCountResult(arbiter);

        result.Accumulate(first, "Key");
        result.Accumulate(second, "Key");

        Assert.Equal(2, result.Groups[new NativeStringGroupKey(false, "A")]);
        Assert.Equal(1, result.Groups[new NativeStringGroupKey(false, "a")]);
        Assert.Equal(2, result.Groups[new NativeStringGroupKey(false, 1m)]);
        Assert.Equal(2, result.Groups[new NativeStringGroupKey(true, null)]);
        Assert.Equal(result.EstimatedBytes, arbiter.ReservedBytes);
    }

    [Fact]
    public void GroupingFailsBoundedlyWhenCardinalityExceedsGrant()
    {
        using var batch = CreateBatch();
        var arbiter = new MemoryGrantArbiter(1);

        var error = Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            ColumnBatchGroupKernels.GroupAggregate<int, int>(batch, "Key", "Value", memoryArbiter: arbiter));

        Assert.Contains("spill-capable grouped execution", error.Message);
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public void CancellationReleasesPartialGroupState()
    {
        using var batch = CreateBatch();
        var arbiter = new MemoryGrantArbiter(1_000_000);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ColumnBatchGroupKernels.GroupAggregate<int, int>(
                batch, "Key", "Value", arbiter, cancellationToken: cancellation.Token));
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    private static ColumnBatch CreateBatch()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(int), "INT"),
            new ColumnBatchField("Value", typeof(int), "INT")
        });
        var keys = new ColumnBuffer<int>(new[] { 1, 1, 2, 0, 0 }, 5, new byte[] { 0b0001_1000 });
        var values = new ColumnBuffer<int>(new[] { 10, 0, 5, 7, 0 }, 5, new byte[] { 0b0001_0010 });
        return new ColumnBatch(schema, new IColumnBuffer[] { keys, values }, 5);
    }
}
