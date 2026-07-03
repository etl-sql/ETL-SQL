using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Data;

public enum ColumnarJoinKind
{
    Inner,
    LeftOuter,
    LeftSemi,
    LeftAnti
}

/// <summary>Pooled packed left/right row ordinals produced by a native join.</summary>
public sealed class NativeJoinPairs : IDisposable
{
    private int[]? _leftRows;
    private int[]? _rightRows;
    private IMemoryGrantLease? _lease;

    internal NativeJoinPairs(int[] leftRows, int[] rightRows, int count, IMemoryGrantLease lease, long reservedBytes)
    {
        _leftRows = leftRows;
        _rightRows = rightRows;
        _lease = lease;
        Count = count;
        ReservedBytes = reservedBytes;
    }

    public int Count { get; }
    public long ReservedBytes { get; }
    public ReadOnlyMemory<int> LeftRows
        => (_leftRows ?? throw new ObjectDisposedException(nameof(NativeJoinPairs))).AsMemory(0, Count);
    public ReadOnlyMemory<int> RightRows
        => (_rightRows ?? throw new ObjectDisposedException(nameof(NativeJoinPairs))).AsMemory(0, Count);

    public void Dispose()
    {
        var left = Interlocked.Exchange(ref _leftRows, null);
        var right = Interlocked.Exchange(ref _rightRows, null);
        if (left != null) ArrayPool<int>.Shared.Return(left, clearArray: false);
        if (right != null) ArrayPool<int>.Shared.Return(right, clearArray: false);
        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}

public static class ColumnBatchJoinKernels
{
    public static NativeJoinPairs InnerJoin<T>(
        ColumnBatch left,
        string leftKeyColumn,
        ColumnBatch right,
        string rightKeyColumn,
        IMemoryGrantArbiter? memoryArbiter = null,
        SelectionVector? leftSelection = null,
        SelectionVector? rightSelection = null,
        CancellationToken cancellationToken = default) where T : unmanaged
        => Join<T>(left, leftKeyColumn, right, rightKeyColumn, ColumnarJoinKind.Inner,
            memoryArbiter, leftSelection, rightSelection, cancellationToken);

    public static NativeJoinPairs Join<T>(
        ColumnBatch left,
        string leftKeyColumn,
        ColumnBatch right,
        string rightKeyColumn,
        ColumnarJoinKind joinKind,
        IMemoryGrantArbiter? memoryArbiter = null,
        SelectionVector? leftSelection = null,
        SelectionVector? rightSelection = null,
        CancellationToken cancellationToken = default) where T : unmanaged
    {
        if (!Enum.IsDefined(joinKind)) throw new ArgumentOutOfRangeException(nameof(joinKind));
        var leftKeys = left.GetColumn<T>(leftKeyColumn);
        var rightKeys = right.GetColumn<T>(rightKeyColumn);
        return JoinCore(
            left.RowCount,
            right.RowCount,
            leftKeys.IsNull,
            rightKeys.IsNull,
            row => leftKeys.Values.Span[row],
            row => rightKeys.Values.Span[row],
            _ => Unsafe.SizeOf<T>(),
            joinKind,
            memoryArbiter,
            leftSelection,
            rightSelection,
            cancellationToken);
    }

    public static NativeJoinPairs JoinUtf8(
        ColumnBatch left,
        string leftKeyColumn,
        ColumnBatch right,
        string rightKeyColumn,
        ColumnarJoinKind joinKind,
        IMemoryGrantArbiter? memoryArbiter = null,
        SelectionVector? leftSelection = null,
        SelectionVector? rightSelection = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(joinKind)) throw new ArgumentOutOfRangeException(nameof(joinKind));
        var leftKeys = left.GetUtf8Column(leftKeyColumn);
        var rightKeys = right.GetUtf8Column(rightKeyColumn);
        return JoinCore<object>(
            left.RowCount,
            right.RowCount,
            leftKeys.IsNull,
            rightKeys.IsNull,
            row => Normalize(leftKeys, row),
            row => Normalize(rightKeys, row),
            row => rightKeys.GetUtf8Bytes(row).Length,
            joinKind,
            memoryArbiter,
            leftSelection,
            rightSelection,
            cancellationToken);

        static object Normalize(Utf8ColumnBuffer keys, int row)
            => CompoundKey.NormalizeValue(Encoding.UTF8.GetString(keys.GetUtf8Bytes(row)))!;
    }

    private static NativeJoinPairs JoinCore<TKey>(
        int leftRowCount,
        int rightRowCount,
        Func<int, bool> leftIsNull,
        Func<int, bool> rightIsNull,
        Func<int, TKey> getLeftKey,
        Func<int, TKey> getRightKey,
        Func<int, int> getRightKeySize,
        ColumnarJoinKind joinKind,
        IMemoryGrantArbiter? memoryArbiter,
        SelectionVector? leftSelection,
        SelectionVector? rightSelection,
        CancellationToken cancellationToken) where TKey : notnull
    {
        var build = new Dictionary<TKey, List<int>>();
        var arbiter = memoryArbiter ?? UnlimitedMemoryGrantArbiter.Instance;
        var lease = arbiter.AcquireLease();
        int[]? leftRows = null;
        int[]? rightRows = null;
        var count = 0;
        long reservedBytes = 0;

        try
        {
            VisitOrdinals(rightRowCount, rightSelection, cancellationToken, row =>
            {
                if (rightIsNull(row)) return;
                var key = getRightKey(row);
                if (!build.TryGetValue(key, out var rows))
                {
                    Reserve(lease, ref reservedBytes, 48L + getRightKeySize(row));
                    rows = new List<int>();
                    build.Add(key, rows);
                }
                Reserve(lease, ref reservedBytes, sizeof(int) + 4L);
                rows.Add(row);
            });

            VisitOrdinals(leftRowCount, leftSelection, cancellationToken, leftRow =>
            {
                List<int>? matches = null;
                var matched = !leftIsNull(leftRow)
                    && build.TryGetValue(getLeftKey(leftRow), out matches);
                if (matched)
                {
                    if (joinKind == ColumnarJoinKind.LeftAnti) return;
                    if (joinKind == ColumnarJoinKind.LeftSemi)
                    {
                        Append(leftRow, matches![0]);
                        return;
                    }
                    foreach (var rightRow in matches!) Append(leftRow, rightRow);
                    return;
                }

                if (joinKind is ColumnarJoinKind.LeftOuter or ColumnarJoinKind.LeftAnti)
                    Append(leftRow, -1);
            });

            EnsureOutputCapacity(ref leftRows, ref rightRows, 1, lease, ref reservedBytes);
            foreach (var rows in build.Values) rows.Clear();
            build.Clear();
            lease.Dispose();
            lease = arbiter.AcquireLease();
            reservedBytes = (long)(leftRows!.Length + rightRows!.Length) * sizeof(int);
            if (lease.RegisterAndCheckSpill(reservedBytes))
                throw new ExecutionException("Native join output could not reacquire its result-lifetime memory grant.");
            var result = new NativeJoinPairs(leftRows!, rightRows!, count, lease, reservedBytes);
            leftRows = null;
            rightRows = null;
            return result;

            void Append(int leftRow, int rightRow)
            {
                EnsureOutputCapacity(ref leftRows, ref rightRows, count + 1, lease, ref reservedBytes);
                leftRows![count] = leftRow;
                rightRows![count] = rightRow;
                count++;
            }
        }
        catch
        {
            if (leftRows != null) ArrayPool<int>.Shared.Return(leftRows, clearArray: false);
            if (rightRows != null) ArrayPool<int>.Shared.Return(rightRows, clearArray: false);
            lease.Dispose();
            throw;
        }
        finally
        {
            foreach (var rows in build.Values) rows.Clear();
            build.Clear();
        }
    }

    private static void EnsureOutputCapacity(
        ref int[]? leftRows,
        ref int[]? rightRows,
        int required,
        IMemoryGrantLease lease,
        ref long reservedBytes)
    {
        if (leftRows != null && required <= leftRows.Length) return;
        var requested = leftRows == null ? 16 : checked(leftRows.Length * 2);
        while (requested < required) requested = checked(requested * 2);
        int[]? newLeft = null;
        int[]? newRight = null;
        try
        {
            newLeft = ArrayPool<int>.Shared.Rent(requested);
            newRight = ArrayPool<int>.Shared.Rent(requested);
            var previousBytes = leftRows == null ? 0L : (long)(leftRows.Length + rightRows!.Length) * sizeof(int);
            var nextBytes = (long)(newLeft.Length + newRight.Length) * sizeof(int);
            Reserve(lease, ref reservedBytes, nextBytes - previousBytes);
            if (leftRows != null)
            {
                leftRows.CopyTo(newLeft, 0);
                rightRows!.CopyTo(newRight, 0);
                ArrayPool<int>.Shared.Return(leftRows, clearArray: false);
                ArrayPool<int>.Shared.Return(rightRows, clearArray: false);
            }
            leftRows = newLeft;
            rightRows = newRight;
            newLeft = null;
            newRight = null;
        }
        finally
        {
            if (newLeft != null) ArrayPool<int>.Shared.Return(newLeft, clearArray: false);
            if (newRight != null) ArrayPool<int>.Shared.Return(newRight, clearArray: false);
        }
    }

    private static void Reserve(IMemoryGrantLease lease, ref long reservedBytes, long additionalBytes)
    {
        var prospective = checked(reservedBytes + additionalBytes);
        if (lease.RegisterAndCheckSpill(prospective))
            throw new ExecutionException(
                $"Native hash join requires more than {reservedBytes:N0} bytes. " +
                "Increase Engine:TotalMemoryGrantMB or use spill-partitioned join execution.");
        reservedBytes = prospective;
    }

    private static void VisitOrdinals(
        int rowCount,
        SelectionVector? selection,
        CancellationToken cancellationToken,
        Action<int> visit)
    {
        if (selection == null)
        {
            for (var row = 0; row < rowCount; row++)
            {
                if ((row & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                visit(row);
            }
            return;
        }

        var ordinals = selection.Indices.Span;
        for (var position = 0; position < ordinals.Length; position++)
        {
            if ((position & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var row = ordinals[position];
            if ((uint)row >= (uint)rowCount)
                throw new ArgumentOutOfRangeException(nameof(selection), "Selection vector contains an invalid row ordinal.");
            visit(row);
        }
    }
}
