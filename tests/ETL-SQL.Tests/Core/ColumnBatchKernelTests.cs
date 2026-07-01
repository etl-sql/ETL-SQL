using System;
using System.Linq;
using System.Threading;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class ColumnBatchKernelTests
{
    [Fact]
    public void ProjectionIsZeroCopyAndRetainsSourceLifetime()
    {
        var batch = CreateBatch();
        using var projection = ColumnBatchKernels.Project(batch, "Name", "Id");

        Assert.Equal(new[] { "Name", "Id" }, projection.Schema.Fields.Select(field => field.Name));
        Assert.Same(batch.GetColumn("Name"), projection.GetColumn("Name"));
        batch.Dispose();
        Assert.Equal(4, projection.RowCount);
        Assert.Equal(3, projection.GetColumn("Id").GetBoxedValue(2));
    }

    [Fact]
    public void ComparisonExcludesNullsAndSelectionVectorsCompose()
    {
        using var batch = CreateBatch();
        using var greaterThanOne = ColumnBatchKernels.SelectComparison(
            batch, "Id", ColumnComparison.GreaterThan, 1);
        using var active = ColumnBatchKernels.SelectBoolean(batch, "Active", expected: true, greaterThanOne);

        Assert.Equal(new[] { 2 }, greaterThanOne.Indices.ToArray());
        Assert.Equal(new[] { 2 }, active.Indices.ToArray());
    }

    [Fact]
    public void NullKernelImplementsIsNullAndIsNotNull()
    {
        using var batch = CreateBatch();
        using var nulls = ColumnBatchKernels.SelectNull(batch, "Id", isNull: true);
        using var nonNulls = ColumnBatchKernels.SelectNull(batch, "Id", isNull: false);

        Assert.Equal(new[] { 1 }, nulls.Indices.ToArray());
        Assert.Equal(new[] { 0, 2, 3 }, nonNulls.Indices.ToArray());
    }

    [Fact]
    public void CancelledKernelReturnsPooledStorageBeforeThrowing()
    {
        using var batch = CreateBatch();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ColumnBatchKernels.SelectComparison(batch, "Id", ColumnComparison.Equal, 1, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void NativeAggregatesMatchSqlNullSemantics()
    {
        using var batch = CreateBatch();

        Assert.Equal(4, ColumnBatchKernels.Count(batch));
        Assert.Equal(3, ColumnBatchKernels.Count(batch, "Id"));
        Assert.Equal(5, ColumnBatchKernels.Sum<int>(batch, "Id"));
        Assert.Equal(5m, ColumnBatchKernels.SumDecimal<int>(batch, "Id"));
        Assert.Equal(5d / 3d, ColumnBatchKernels.Average<int>(batch, "Id"));
        Assert.Equal(5m / 3m, ColumnBatchKernels.AverageDecimal<int>(batch, "Id"));
        var range = ColumnBatchKernels.MinMax<int>(batch, "Id");
        Assert.True(range.HasValue);
        Assert.Equal(1, range.Min);
        Assert.Equal(3, range.Max);
    }

    [Fact]
    public void NativeAggregatesConsumeSelectionWithoutMaterializingRows()
    {
        using var batch = CreateBatch();
        using var selected = ColumnBatchKernels.SelectComparison(batch, "Id", ColumnComparison.GreaterThan, 1);

        Assert.Equal(1, ColumnBatchKernels.Count(batch, selection: selected));
        Assert.Equal(1, ColumnBatchKernels.Count(batch, "Id", selected));
        Assert.Equal(3, ColumnBatchKernels.Sum<int>(batch, "Id", selected));
        Assert.Equal(3d, ColumnBatchKernels.Average<int>(batch, "Id", selected));
    }

    [Fact]
    public void EmptyAggregateInputReturnsNullExceptCount()
    {
        using var batch = CreateBatch();
        using var nullId = ColumnBatchKernels.SelectNull(batch, "Id", isNull: true);

        Assert.Equal(1, ColumnBatchKernels.Count(batch, selection: nullId));
        Assert.Equal(0, ColumnBatchKernels.Count(batch, "Id", nullId));
        Assert.Null(ColumnBatchKernels.Sum<int>(batch, "Id", nullId));
        Assert.Null(ColumnBatchKernels.SumDecimal<int>(batch, "Id", nullId));
        Assert.Null(ColumnBatchKernels.Average<int>(batch, "Id", nullId));
        Assert.False(ColumnBatchKernels.MinMax<int>(batch, "Id", nullId).HasValue);
    }

    [Fact]
    public void ArithmeticPredicateUsesTypedBuffersAndSqlNullExclusion()
    {
        using var batch = CreateBatch();
        using var selected = ColumnBatchKernels.SelectArithmeticComparison(
            batch,
            "Id",
            ColumnArithmetic.Multiply,
            2,
            ColumnComparison.GreaterThan,
            2);

        Assert.Equal(new[] { 2 }, selected.Indices.ToArray());
        Assert.Throws<DivideByZeroException>(() => ColumnBatchKernels.SelectArithmeticComparison(
            batch, "Id", ColumnArithmetic.Divide, 0, ColumnComparison.Equal, 1));
    }

    [Fact]
    public void DecimalSumAndAverageDoNotOverflowAtPhysicalIntegerWidth()
    {
        var schema = new ColumnBatchSchema(new[] { new ColumnBatchField("Value", typeof(int), "INT") });
        using var batch = new ColumnBatch(
            schema, new IColumnBuffer[] { new ColumnBuffer<int>(new[] { int.MaxValue, int.MaxValue }, 2) }, 2);

        Assert.Equal(4_294_967_294m, ColumnBatchKernels.SumDecimal<int>(batch, "Value"));
        Assert.Equal(2_147_483_647m, ColumnBatchKernels.AverageDecimal<int>(batch, "Value"));
    }

    private static ColumnBatch CreateBatch()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Id", typeof(int), "INT"),
            new ColumnBatchField("Name", typeof(string), "VARCHAR(20)"),
            new ColumnBatchField("Active", typeof(byte), "BOOLEAN")
        });
        var ids = new ColumnBuffer<int>(new[] { 1, 0, 3, 1 }, 4, new byte[] { 0b0000_0010 });
        var names = Utf8ColumnBuffer.FromStrings(new string?[] { "one", "null-id", "three", "one-again" });
        var active = new ColumnBuffer<byte>(new byte[] { 1, 1, 1, 0 }, 4);
        return new ColumnBatch(schema, new IColumnBuffer[] { ids, names, active }, 4);
    }
}
