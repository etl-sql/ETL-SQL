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

    [Theory]
    [InlineData(ColumnarJoinKind.Inner)]
    [InlineData(ColumnarJoinKind.LeftOuter)]
    [InlineData(ColumnarJoinKind.LeftSemi)]
    [InlineData(ColumnarJoinKind.LeftAnti)]
    public void CompositeJoinMatchesRowReferenceAndRejectsPartialNullKeys(ColumnarJoinKind kind)
    {
        (string? Region, int? Id)[] leftKeys =
            { ("A", 1), (" A ", 1), ("A", 2), (null, 1), ("A", null), ("a", 1), ("X", 9) };
        (string? Region, int? Id)[] rightKeys =
            { ("A", 1), ("A", 1), ("A", 2), (null, 1), ("A", null), ("a", 1) };
        using var left = CreateCompositeBatch(leftKeys);
        using var right = CreateCompositeBatch(rightKeys);
        using var actual = ColumnBatchJoinKernels.JoinComposite(
            left, new[] { "Region", "Id" }, right, new[] { "Region", "Id" }, kind);

        var expected = new List<(int Left, int Right)>();
        for (var leftRow = 0; leftRow < leftKeys.Length; leftRow++)
        {
            var leftKey = leftKeys[leftRow];
            var matches = leftKey.Region == null || leftKey.Id == null
                ? Array.Empty<int>()
                : Enumerable.Range(0, rightKeys.Length)
                    .Where(rightRow => rightKeys[rightRow].Region != null && rightKeys[rightRow].Id != null &&
                        new CompoundKey(leftKey.Region, leftKey.Id).Equals(
                            new CompoundKey(rightKeys[rightRow].Region, rightKeys[rightRow].Id)))
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
    public void JoinPayloadProjectionGathersNativeBuffersAndNullsUnmatchedRightRows()
    {
        var left = CreatePayloadBatch(new[] { 1, 2, 3 }, new[] { "one", "two", "three" }, new[] { 10, 20, 30 });
        var right = CreatePayloadBatch(new[] { 2, 2 }, new[] { "right-a", "right-b" }, new[] { 200, 201 });
        var pairs = ColumnBatchJoinKernels.Join<int>(left, "Key", right, "Key", ColumnarJoinKind.LeftOuter);
        var projected = ColumnBatchJoinKernels.ProjectPayloads(
            left, right, pairs,
            new[] { "Label" }, new[] { "Label", "Payload" },
            new[] { "LeftLabel", "RightLabel", "RightPayload" });
        pairs.Dispose();
        left.Dispose();
        right.Dispose();

        using (projected)
        {
            Assert.Equal(new[] { "LeftLabel", "RightLabel", "RightPayload" },
                projected.Schema.Fields.Select(field => field.Name));
            Assert.Equal(new object?[] { "one", "two", "two", "three" },
                Enumerable.Range(0, projected.RowCount)
                    .Select(row => projected.GetUtf8Column("LeftLabel").GetBoxedValue(row)));
            Assert.Equal(new object?[] { null, "right-a", "right-b", null },
                Enumerable.Range(0, projected.RowCount)
                    .Select(row => projected.GetUtf8Column("RightLabel").GetBoxedValue(row)));
            var payload = projected.GetColumn<int>("RightPayload");
            Assert.True(payload.IsNull(0));
            Assert.Equal(200, payload.Values.Span[1]);
            Assert.Equal(201, payload.Values.Span[2]);
            Assert.True(payload.IsNull(3));
        }
    }

    [Fact]
    public void RuntimeDispatchSelectsFixedStringAndCompositeKernels()
    {
        using var fixedLeft = CreateBatch(new[] { 1, 2 }, new byte[] { 0 });
        using var fixedRight = CreateBatch(new[] { 2, 3 }, new byte[] { 0 });
        using var fixedPairs = ColumnBatchJoinKernels.JoinAuto(
            fixedLeft, new[] { "Key" }, fixedRight, new[] { "Key" }, ColumnarJoinKind.Inner);
        Assert.Equal(new[] { 1 }, fixedPairs.LeftRows.ToArray());

        using var stringLeft = CreateStringBatch(new string?[] { " A ", "x" });
        using var stringRight = CreateStringBatch(new string?[] { "A", "y" });
        using var stringPairs = ColumnBatchJoinKernels.JoinAuto(
            stringLeft, new[] { "Key" }, stringRight, new[] { "Key" }, ColumnarJoinKind.Inner);
        Assert.Equal(new[] { 0 }, stringPairs.LeftRows.ToArray());

        using var compositeLeft = CreateCompositeBatch(new[] { ("A", (int?)1), ("B", (int?)2) });
        using var compositeRight = CreateCompositeBatch(new[] { ("B", (int?)2), ("A", (int?)9) });
        using var compositePairs = ColumnBatchJoinKernels.JoinAuto(
            compositeLeft, new[] { "Region", "Id" }, compositeRight, new[] { "Region", "Id" },
            ColumnarJoinKind.Inner);
        Assert.Equal(new[] { 1 }, compositePairs.LeftRows.ToArray());
    }

    [Fact]
    public void ConstructedOrdinalPairsHoldGrantAndSupportOuterSentinels()
    {
        var arbiter = new MemoryGrantArbiter(1_000_000);
        var pairs = ColumnBatchJoinKernels.CreateOrdinalPairs(
            new[] { 0, 2 }, new[] { -1, -1 }, arbiter);

        Assert.Equal(new[] { 0, 2 }, pairs.LeftRows.ToArray());
        Assert.All(pairs.RightRows.ToArray(), row => Assert.Equal(-1, row));
        Assert.Equal(pairs.ReservedBytes, arbiter.ReservedBytes);
        pairs.Dispose();
        Assert.Equal(0, arbiter.ReservedBytes);

        Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            ColumnBatchJoinKernels.CreateOrdinalPairs(new[] { 0 }, new[] { -1 }, new MemoryGrantArbiter(1)));
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

    private static ColumnBatch CreateCompositeBatch((string? Region, int? Id)[] keys)
    {
        var idValues = keys.Select(key => key.Id ?? 0).ToArray();
        var idNulls = new byte[(keys.Length + 7) / 8];
        for (var i = 0; i < keys.Length; i++)
            if (keys[i].Id == null) idNulls[i >> 3] |= (byte)(1 << (i & 7));
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Region", typeof(string), "VARCHAR(20)"),
            new ColumnBatchField("Id", typeof(int), "INT")
        });
        return new ColumnBatch(schema, new IColumnBuffer[]
        {
            Utf8ColumnBuffer.FromStrings(keys.Select(key => key.Region).ToArray()),
            new ColumnBuffer<int>(idValues, idValues.Length, idNulls)
        }, keys.Length);
    }

    private static ColumnBatch CreatePayloadBatch(int[] keys, string[] labels, int[] payloads)
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(int), "INT"),
            new ColumnBatchField("Label", typeof(string), "VARCHAR(20)"),
            new ColumnBatchField("Payload", typeof(int), "INT")
        });
        return new ColumnBatch(schema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(keys, keys.Length),
            Utf8ColumnBuffer.FromStrings(labels),
            new ColumnBuffer<int>(payloads, payloads.Length)
        }, keys.Length);
    }
}
