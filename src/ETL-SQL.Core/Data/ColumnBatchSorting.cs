using System;
using System.Buffers;
using System.Threading;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Data;

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
                var leftNull = column.IsNull(left);
                var rightNull = column.IsNull(right);
                int order;
                if (leftNull || rightNull)
                    order = leftNull == rightNull ? 0 : (leftNull == nullsFirst ? -1 : 1);
                else
                    order = values.Span[left].CompareTo(values.Span[right]);
                if (descending && !leftNull && !rightNull) order = -order;
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
}
