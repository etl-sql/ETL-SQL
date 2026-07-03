using System;
using System.Linq;
using System.Threading;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
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
    public void LeftOuterJoinPreservesUnmatchedAndNullKeysWithSentinel()
    {
        using var left = CreateBatch(new[] { 1, 2, 2, 0 }, 0b0000_1000);
        using var right = CreateBatch(new[] { 2, 2, 3, 0 }, 0b0000_1000);
        using var pairs = ColumnBatchJoinKernels.Join<int>(
            left, "Key", right, "Key", ColumnarJoinKind.LeftOuter);

        Assert.Equal(new[] { 0, 1, 1, 2, 2, 3 }, pairs.LeftRows.ToArray());
        Assert.Equal(new[] { -1, 0, 1, 0, 1, -1 }, pairs.RightRows.ToArray());
    }

    [Fact]
    public void LeftSemiAndAntiReturnEachEligibleLeftRowOnce()
    {
        using var left = CreateBatch(new[] { 1, 2, 2, 0 }, 0b0000_1000);
        using var right = CreateBatch(new[] { 2, 2, 3, 0 }, 0b0000_1000);

        using var semi = ColumnBatchJoinKernels.Join<int>(
            left, "Key", right, "Key", ColumnarJoinKind.LeftSemi);
        Assert.Equal(new[] { 1, 2 }, semi.LeftRows.ToArray());
        Assert.Equal(new[] { 0, 0 }, semi.RightRows.ToArray());

        using var anti = ColumnBatchJoinKernels.Join<int>(
            left, "Key", right, "Key", ColumnarJoinKind.LeftAnti);
        Assert.Equal(new[] { 0, 3 }, anti.LeftRows.ToArray());
        Assert.All(anti.RightRows.ToArray(), row => Assert.Equal(-1, row));
    }

    [Fact]
    public void OuterJoinHonorsSelectionsBeforeMatching()
    {
        using var left = CreateBatch(new[] { 1, 2, 2, 0 }, 0b0000_1000);
        using var right = CreateBatch(new[] { 2, 2, 3, 0 }, 0b0000_1000);
        using var leftSelection = SelectionVector.FromIndices(new[] { 0, 1, 3 });
        using var rightSelection = SelectionVector.FromIndices(new[] { 1 });
        using var pairs = ColumnBatchJoinKernels.Join<int>(
            left, "Key", right, "Key", ColumnarJoinKind.LeftOuter,
            leftSelection: leftSelection, rightSelection: rightSelection);

        Assert.Equal(new[] { 0, 1, 3 }, pairs.LeftRows.ToArray());
        Assert.Equal(new[] { -1, 1, -1 }, pairs.RightRows.ToArray());
    }

    [Theory]
    [InlineData(ColumnarJoinKind.Inner)]
    [InlineData(ColumnarJoinKind.LeftOuter)]
    [InlineData(ColumnarJoinKind.LeftSemi)]
    [InlineData(ColumnarJoinKind.LeftAnti)]
    public void NativeJoinMatchesRowReferenceAcrossDuplicatesAndNulls(ColumnarJoinKind kind)
    {
        var random = new Random(1776);
        var leftKeys = Enumerable.Range(0, 128).Select(_ => random.Next(0, 12)).ToArray();
        var rightKeys = Enumerable.Range(0, 96).Select(_ => random.Next(0, 12)).ToArray();
        var leftNulls = Enumerable.Range(0, leftKeys.Length).Where(index => index % 17 == 0).ToHashSet();
        var rightNulls = Enumerable.Range(0, rightKeys.Length).Where(index => index % 13 == 0).ToHashSet();
        using var left = CreateBatch(leftKeys, ToBitmap(leftKeys.Length, leftNulls));
        using var right = CreateBatch(rightKeys, ToBitmap(rightKeys.Length, rightNulls));
        using var actual = ColumnBatchJoinKernels.Join<int>(left, "Key", right, "Key", kind);

        var expected = new List<(int Left, int Right)>();
        for (var leftRow = 0; leftRow < leftKeys.Length; leftRow++)
        {
            var matches = leftNulls.Contains(leftRow)
                ? Array.Empty<int>()
                : Enumerable.Range(0, rightKeys.Length)
                    .Where(rightRow => !rightNulls.Contains(rightRow) && leftKeys[leftRow] == rightKeys[rightRow])
                    .ToArray();
            if (matches.Length > 0)
            {
                if (kind == ColumnarJoinKind.LeftAnti) continue;
                if (kind == ColumnarJoinKind.LeftSemi) expected.Add((leftRow, matches[0]));
                else expected.AddRange(matches.Select(rightRow => (leftRow, rightRow)));
            }
            else if (kind is ColumnarJoinKind.LeftOuter or ColumnarJoinKind.LeftAnti)
            {
                expected.Add((leftRow, -1));
            }
        }

        Assert.Equal(expected.Select(pair => pair.Left), actual.LeftRows.ToArray());
        Assert.Equal(expected.Select(pair => pair.Right), actual.RightRows.ToArray());

        static byte[] ToBitmap(int count, HashSet<int> nulls)
        {
            var bitmap = new byte[(count + 7) / 8];
            foreach (var index in nulls) bitmap[index >> 3] |= (byte)(1 << (index & 7));
            return bitmap;
        }
    }

    [Theory]
    [InlineData(ColumnarJoinKind.Inner)]
    [InlineData(ColumnarJoinKind.LeftOuter)]
    [InlineData(ColumnarJoinKind.LeftSemi)]
    [InlineData(ColumnarJoinKind.LeftAnti)]
    public void Utf8JoinMatchesNormalizedRowReference(ColumnarJoinKind kind)
    {
        string?[] leftKeys = { "A", " A ", "1", null, "a", "missing" };
        string?[] rightKeys = { "A", "1.0", "A", null, "a" };
        using var left = CreateStringBatch(leftKeys);
        using var right = CreateStringBatch(rightKeys);
        using var actual = ColumnBatchJoinKernels.JoinUtf8(left, "Key", right, "Key", kind);

        var expected = new List<(int Left, int Right)>();
        for (var leftRow = 0; leftRow < leftKeys.Length; leftRow++)
        {
            var normalizedLeft = CompoundKey.NormalizeValue(leftKeys[leftRow]);
            var matches = normalizedLeft == null
                ? Array.Empty<int>()
                : Enumerable.Range(0, rightKeys.Length)
                    .Where(rightRow => rightKeys[rightRow] != null &&
                        Equals(normalizedLeft, CompoundKey.NormalizeValue(rightKeys[rightRow])))
                    .ToArray();
            if (matches.Length > 0)
            {
                if (kind == ColumnarJoinKind.LeftAnti) continue;
                if (kind == ColumnarJoinKind.LeftSemi) expected.Add((leftRow, matches[0]));
                else expected.AddRange(matches.Select(rightRow => (leftRow, rightRow)));
            }
            else if (kind is ColumnarJoinKind.LeftOuter or ColumnarJoinKind.LeftAnti)
            {
                expected.Add((leftRow, -1));
            }
        }

        Assert.Equal(expected.Select(pair => pair.Left), actual.LeftRows.ToArray());
        Assert.Equal(expected.Select(pair => pair.Right), actual.RightRows.ToArray());
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
        => CreateBatch(keys, new[] { nullBitmap });

    private static ColumnBatch CreateBatch(int[] keys, byte[] nullBitmap)
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(int), "INT"),
            new ColumnBatchField("Payload", typeof(int), "INT")
        });
        return new ColumnBatch(schema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(keys, keys.Length, nullBitmap),
            new ColumnBuffer<int>(keys.Length == 4
                ? new[] { 10, 20, 20, 40 }
                : Enumerable.Range(1, keys.Length).Select(value => value * 10).ToArray(), keys.Length)
        }, keys.Length);
    }

    private static ColumnBatch CreateStringBatch(string?[] keys)
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(string), "VARCHAR(20)"),
            new ColumnBatchField("Payload", typeof(int), "INT")
        });
        return new ColumnBatch(schema, new IColumnBuffer[]
        {
            Utf8ColumnBuffer.FromStrings(keys),
            new ColumnBuffer<int>(Enumerable.Range(1, keys.Length).ToArray(), keys.Length)
        }, keys.Length);
    }
}
