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

    [Fact]
    public void MultiKeyUtf8RunHonorsCollationDirectionNullsAndStableTies()
    {
        using var batch = CreateStringBatch();
        using var insensitive = ColumnBatchSortKernels.CreateRun(batch, new[]
        {
            new NativeSortKey("Region", NullsFirst: false, StringComparison: StringComparison.OrdinalIgnoreCase),
            new NativeSortKey("Score", Descending: true)
        });
        Assert.Equal(new[] { 1, 2, 4, 0, 5, 3 }, insensitive.Ordinals.ToArray());

        using var ordinal = ColumnBatchSortKernels.CreateRun(batch, new[]
        {
            new NativeSortKey("Region", NullsFirst: false),
            new NativeSortKey("Score", Descending: true)
        });
        Assert.Equal(new[] { 1, 4, 2, 0, 5, 3 }, ordinal.Ordinals.ToArray());
    }

    [Fact]
    public void Utf8OrdinalSortMatchesUtf16OrderingForSupplementaryCharacters()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Value", typeof(string), "VARCHAR(20)")
        });
        using var batch = new ColumnBatch(schema, new IColumnBuffer[]
        {
            Utf8ColumnBuffer.FromStrings(new[] { "\uE000", "\U00010000" })
        }, 2);
        using var run = ColumnBatchSortKernels.CreateRun(batch, new[] { new NativeSortKey("Value") });

        Assert.Equal(new[] { 1, 0 }, run.Ordinals.ToArray());
    }

    [Fact]
    public void CrossBatchComparisonMatchesRunCollationAndKeyOrdering()
    {
        using var first = CreateStringBatch(
            new string?[] { "b", "A", null }, new[] { 2, 1, 9 });
        using var second = CreateStringBatch(
            new string?[] { "A", "a", "á" }, new[] { 2, 1, 0 });
        var keys = new[]
        {
            new NativeSortKey("Region", NullsFirst: false, StringComparison: StringComparison.OrdinalIgnoreCase),
            new NativeSortKey("Score", Descending: true)
        };
        var rows = new List<(ColumnBatch Batch, int Row, int Global)>
        {
            (first, 0, 0), (first, 1, 1), (first, 2, 2),
            (second, 0, 3), (second, 1, 4), (second, 2, 5)
        };

        rows.Sort((left, right) =>
        {
            var order = ColumnBatchSortKernels.CompareRows(
                left.Batch, left.Row, right.Batch, right.Row, keys);
            return order != 0 ? order : left.Global.CompareTo(right.Global);
        });

        Assert.Equal(new[] { 3, 1, 4, 0, 5, 2 }, rows.Select(item => item.Global));
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

    private static ColumnBatch CreateStringBatch()
        => CreateStringBatch(
            new string?[] { "b", "A", "a", null, "A", "á" },
            new[] { 2, 2, 1, 9, 1, 0 });

    private static ColumnBatch CreateStringBatch(string?[] regions, int[] scores)
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Region", typeof(string), "VARCHAR(20)"),
            new ColumnBatchField("Score", typeof(int), "INT")
        });
        return new ColumnBatch(schema, new IColumnBuffer[]
        {
            Utf8ColumnBuffer.FromStrings(regions),
            new ColumnBuffer<int>(scores, scores.Length)
        }, regions.Length);
    }
}
