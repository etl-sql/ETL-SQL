using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Data;

public sealed record NativeSortKey(
    string ColumnName,
    bool Descending = false,
    bool NullsFirst = true,
    StringComparison StringComparison = StringComparison.Ordinal);

/// <summary>Pooled sorted row ordinals for one native run.</summary>
public sealed class NativeSortRun : IDisposable
{
    private int[]? _ordinals;
    private IMemoryGrantLease? _lease;

    internal NativeSortRun(int[] ordinals, int count, IMemoryGrantLease lease, long reservedBytes)
    {
        _ordinals = ordinals;
        _lease = lease;
        Count = count;
        ReservedBytes = reservedBytes;
    }

    public int Count { get; }
    public long ReservedBytes { get; }
    public ReadOnlyMemory<int> Ordinals
        => (_ordinals ?? throw new ObjectDisposedException(nameof(NativeSortRun))).AsMemory(0, Count);

    public void Dispose()
    {
        var ordinals = Interlocked.Exchange(ref _ordinals, null);
        if (ordinals != null) ArrayPool<int>.Shared.Return(ordinals, clearArray: false);
        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}

public static class ColumnBatchSortKernels
{
    public static int CompareRows(
        ColumnBatch leftBatch,
        int leftRow,
        ColumnBatch rightBatch,
        int rightRow,
        IReadOnlyList<NativeSortKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) throw new ArgumentException("At least one sort key is required.", nameof(keys));
        if ((uint)leftRow >= (uint)leftBatch.RowCount) throw new ArgumentOutOfRangeException(nameof(leftRow));
        if ((uint)rightRow >= (uint)rightBatch.RowCount) throw new ArgumentOutOfRangeException(nameof(rightRow));
        foreach (var key in keys)
        {
            var left = leftBatch.GetColumn(key.ColumnName);
            var right = rightBatch.GetColumn(key.ColumnName);
            if (left.ElementType != right.ElementType)
                throw new NotSupportedException("Native sort key physical types changed across batches.");
            var nullOrder = CompareCrossNulls(left, leftRow, right, rightRow, key.NullsFirst, out var bothPresent);
            if (!bothPresent)
            {
                if (nullOrder != 0) return nullOrder;
                continue;
            }
            var order = ComparePresent(left, leftRow, right, rightRow, key.StringComparison);
            if (order != 0) return key.Descending ? -order : order;
        }
        return 0;
    }

    public static NativeSortRun CreateRun<T>(
        ColumnBatch batch,
        string keyColumnName,
        bool descending = false,
        bool nullsFirst = true,
        IMemoryGrantArbiter? memoryArbiter = null,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default) where T : unmanaged, IComparable<T>
    {
        var column = batch.GetColumn<T>(keyColumnName);
        var values = column.Values;
        return CreateRunCore(batch, (left, right) =>
        {
            var leftNull = column.IsNull(left);
            var rightNull = column.IsNull(right);
            if (leftNull || rightNull)
                return leftNull == rightNull ? 0 : (leftNull == nullsFirst ? -1 : 1);
            var order = values.Span[left].CompareTo(values.Span[right]);
            return descending ? -order : order;
        }, memoryArbiter, selection, cancellationToken);
    }

    public static NativeSortRun CreateRun(
        ColumnBatch batch,
        IReadOnlyList<NativeSortKey> keys,
        IMemoryGrantArbiter? memoryArbiter = null,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) throw new ArgumentException("At least one sort key is required.", nameof(keys));
        var comparers = new Func<int, int, int>[keys.Count];
        for (var index = 0; index < keys.Count; index++)
            comparers[index] = CreateKeyComparer(batch.GetColumn(keys[index].ColumnName), keys[index]);
        return CreateRunCore(batch, (left, right) =>
        {
            for (var index = 0; index < comparers.Length; index++)
            {
                var order = comparers[index](left, right);
                if (order != 0) return order;
            }
            return 0;
        }, memoryArbiter, selection, cancellationToken);
    }

    private static NativeSortRun CreateRunCore(
        ColumnBatch batch,
        Func<int, int, int> compareKeys,
        IMemoryGrantArbiter? memoryArbiter,
        SelectionVector? selection,
        CancellationToken cancellationToken)
    {
        var count = selection?.Count ?? batch.RowCount;
        var arbiter = memoryArbiter ?? UnlimitedMemoryGrantArbiter.Instance;
        var lease = arbiter.AcquireLease();
        int[]? ordinals = null;
        int[]? scratch = null;
        long reservedBytes = 0;

        try
        {
            ordinals = ArrayPool<int>.Shared.Rent(Math.Max(1, count));
            scratch = ArrayPool<int>.Shared.Rent(Math.Max(1, count));
            reservedBytes = (long)(ordinals.Length + scratch.Length) * sizeof(int);
            if (lease.RegisterAndCheckSpill(reservedBytes))
                throw new ExecutionException(
                    $"Native sort run requires {reservedBytes:N0} bytes. " +
                    "Increase Engine:TotalMemoryGrantMB or reduce the bounded run size.");

            if (selection == null)
            {
                for (var row = 0; row < count; row++) ordinals[row] = row;
            }
            else
            {
                var selected = selection.Indices.Span;
                for (var position = 0; position < selected.Length; position++)
                {
                    var row = selected[position];
                    if ((uint)row >= (uint)batch.RowCount)
                        throw new ArgumentOutOfRangeException(nameof(selection), "Selection vector contains an invalid row ordinal.");
                    ordinals[position] = row;
                }
            }

            int Compare(int left, int right)
            {
                var order = compareKeys(left, right);
                return order != 0 ? order : left.CompareTo(right);
            }

            var source = ordinals;
            var destination = scratch;
            for (var width = 1; width < count; width = width > count / 2 ? count : width * 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mergeWidth = width > count / 2 ? count : width * 2;
                for (var start = 0; start < count; start += mergeWidth)
                {
                    var middle = (int)Math.Min((long)start + width, count);
                    var end = (int)Math.Min((long)start + mergeWidth, count);
                    var left = start;
                    var right = middle;
                    var output = start;
                    while (left < middle && right < end)
                    {
                        if ((output & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        destination[output++] = Compare(source[left], source[right]) <= 0
                            ? source[left++]
                            : source[right++];
                    }
                    while (left < middle) destination[output++] = source[left++];
                    while (right < end) destination[output++] = source[right++];
                }
                (source, destination) = (destination, source);
            }
            if (!ReferenceEquals(source, ordinals)) source.AsSpan(0, count).CopyTo(ordinals);

            ArrayPool<int>.Shared.Return(scratch, clearArray: false);
            scratch = null;
            lease.Dispose();
            lease = arbiter.AcquireLease();
            reservedBytes = (long)ordinals.Length * sizeof(int);
            if (lease.RegisterAndCheckSpill(reservedBytes))
                throw new ExecutionException("Native sort output could not reacquire its result-lifetime memory grant.");
            var result = new NativeSortRun(ordinals, count, lease, reservedBytes);
            ordinals = null;
            return result;
        }
        catch
        {
            if (ordinals != null) ArrayPool<int>.Shared.Return(ordinals, clearArray: false);
            if (scratch != null) ArrayPool<int>.Shared.Return(scratch, clearArray: false);
            lease.Dispose();
            throw;
        }
    }

    private static Func<int, int, int> CreateKeyComparer(IColumnBuffer column, NativeSortKey key)
    {
        if (column is Utf8ColumnBuffer utf8)
        {
            if (key.StringComparison is not StringComparison.Ordinal and not StringComparison.OrdinalIgnoreCase)
                throw new NotSupportedException("Native UTF-8 sorting supports ordinal collations only.");
            return CompareRows;

            int CompareRows(int left, int right)
            {
                var nullOrder = CompareNulls(column, left, right, key.NullsFirst, out var bothPresent);
                if (!bothPresent) return nullOrder;
                var leftBytes = utf8.GetUtf8Bytes(left);
                var rightBytes = utf8.GetUtf8Bytes(right);
                int order;
                if (key.StringComparison == StringComparison.Ordinal && IsAscii(leftBytes) && IsAscii(rightBytes))
                    order = leftBytes.SequenceCompareTo(rightBytes);
                else if (IsAscii(leftBytes) && IsAscii(rightBytes))
                    order = CompareAsciiIgnoreCase(leftBytes, rightBytes);
                else
                    order = string.Compare(Encoding.UTF8.GetString(leftBytes), Encoding.UTF8.GetString(rightBytes),
                        key.StringComparison);
                return key.Descending ? -order : order;
            }
        }

        if (column is ColumnBuffer<byte> bytes) return Fixed(bytes, key);
        if (column is ColumnBuffer<short> shorts) return Fixed(shorts, key);
        if (column is ColumnBuffer<int> ints) return Fixed(ints, key);
        if (column is ColumnBuffer<long> longs) return Fixed(longs, key);
        if (column is ColumnBuffer<double> doubles) return Fixed(doubles, key);
        if (column is ColumnBuffer<decimal> decimals) return Fixed(decimals, key);
        if (column is ColumnBuffer<DateTime> dates) return Fixed(dates, key);
        if (column is ColumnBuffer<DateTimeOffset> offsets) return Fixed(offsets, key);
        if (column is ColumnBuffer<TimeSpan> times) return Fixed(times, key);
        if (column is ColumnBuffer<Guid> guids) return Fixed(guids, key);
        throw new NotSupportedException($"Physical type '{column.ElementType.Name}' cannot be sorted natively.");

        static Func<int, int, int> Fixed<T>(ColumnBuffer<T> typed, NativeSortKey sortKey)
            where T : unmanaged, IComparable<T>
            => (left, right) =>
            {
                var nullOrder = CompareNulls(typed, left, right, sortKey.NullsFirst, out var bothPresent);
                if (!bothPresent) return nullOrder;
                var order = typed.Values.Span[left].CompareTo(typed.Values.Span[right]);
                return sortKey.Descending ? -order : order;
            };
    }

    private static int ComparePresent(
        IColumnBuffer left,
        int leftRow,
        IColumnBuffer right,
        int rightRow,
        StringComparison stringComparison)
    {
        if (left is Utf8ColumnBuffer leftUtf8 && right is Utf8ColumnBuffer rightUtf8)
        {
            if (stringComparison is not StringComparison.Ordinal and not StringComparison.OrdinalIgnoreCase)
                throw new NotSupportedException("Native UTF-8 sorting supports ordinal collations only.");
            var leftBytes = leftUtf8.GetUtf8Bytes(leftRow);
            var rightBytes = rightUtf8.GetUtf8Bytes(rightRow);
            if (IsAscii(leftBytes) && IsAscii(rightBytes))
                return stringComparison == StringComparison.Ordinal
                    ? leftBytes.SequenceCompareTo(rightBytes)
                    : CompareAsciiIgnoreCase(leftBytes, rightBytes);
            return string.Compare(Encoding.UTF8.GetString(leftBytes), Encoding.UTF8.GetString(rightBytes),
                stringComparison);
        }
        if (left is ColumnBuffer<byte> lb && right is ColumnBuffer<byte> rb) return lb.Values.Span[leftRow].CompareTo(rb.Values.Span[rightRow]);
        if (left is ColumnBuffer<short> ls && right is ColumnBuffer<short> rs) return ls.Values.Span[leftRow].CompareTo(rs.Values.Span[rightRow]);
        if (left is ColumnBuffer<int> li && right is ColumnBuffer<int> ri) return li.Values.Span[leftRow].CompareTo(ri.Values.Span[rightRow]);
        if (left is ColumnBuffer<long> ll && right is ColumnBuffer<long> rl) return ll.Values.Span[leftRow].CompareTo(rl.Values.Span[rightRow]);
        if (left is ColumnBuffer<double> ld && right is ColumnBuffer<double> rd) return ld.Values.Span[leftRow].CompareTo(rd.Values.Span[rightRow]);
        if (left is ColumnBuffer<decimal> lm && right is ColumnBuffer<decimal> rm) return lm.Values.Span[leftRow].CompareTo(rm.Values.Span[rightRow]);
        if (left is ColumnBuffer<DateTime> ldt && right is ColumnBuffer<DateTime> rdt) return ldt.Values.Span[leftRow].CompareTo(rdt.Values.Span[rightRow]);
        if (left is ColumnBuffer<DateTimeOffset> ldo && right is ColumnBuffer<DateTimeOffset> rdo) return ldo.Values.Span[leftRow].CompareTo(rdo.Values.Span[rightRow]);
        if (left is ColumnBuffer<TimeSpan> lt && right is ColumnBuffer<TimeSpan> rt) return lt.Values.Span[leftRow].CompareTo(rt.Values.Span[rightRow]);
        if (left is ColumnBuffer<Guid> lg && right is ColumnBuffer<Guid> rg) return lg.Values.Span[leftRow].CompareTo(rg.Values.Span[rightRow]);
        throw new NotSupportedException($"Physical type '{left.ElementType.Name}' cannot be sorted natively.");
    }

    private static int CompareNulls(
        IColumnBuffer column,
        int left,
        int right,
        bool nullsFirst,
        out bool bothPresent)
    {
        var leftNull = column.IsNull(left);
        var rightNull = column.IsNull(right);
        bothPresent = !leftNull && !rightNull;
        return bothPresent || leftNull == rightNull ? 0 : (leftNull == nullsFirst ? -1 : 1);
    }

    private static int CompareCrossNulls(
        IColumnBuffer left,
        int leftRow,
        IColumnBuffer right,
        int rightRow,
        bool nullsFirst,
        out bool bothPresent)
    {
        var leftNull = left.IsNull(leftRow);
        var rightNull = right.IsNull(rightRow);
        bothPresent = !leftNull && !rightNull;
        return bothPresent || leftNull == rightNull ? 0 : (leftNull == nullsFirst ? -1 : 1);
    }

    private static bool IsAscii(ReadOnlySpan<byte> value)
    {
        foreach (var item in value) if (item >= 0x80) return false;
        return true;
    }

    private static int CompareAsciiIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var index = 0; index < length; index++)
        {
            var a = left[index] is >= (byte)'a' and <= (byte)'z' ? left[index] - 32 : left[index];
            var b = right[index] is >= (byte)'a' and <= (byte)'z' ? right[index] - 32 : right[index];
            if (a != b) return a.CompareTo(b);
        }
        return left.Length.CompareTo(right.Length);
    }
}
