using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace ETL_SQL.Core.Data;

/// <summary>Contiguous pooled row ordinals split into hash partitions by prefix offsets.</summary>
public sealed class NativePartitionRouting : IDisposable
{
    private int[]? _ordinals;
    private int[]? _offsets;
    private readonly int _rowCount;

    internal NativePartitionRouting(int[] ordinals, int[] offsets, int rowCount, int partitionCount)
    {
        _ordinals = ordinals;
        _offsets = offsets;
        _rowCount = rowCount;
        PartitionCount = partitionCount;
    }

    public int PartitionCount { get; }
    public int RowCount => _rowCount;
    public long AllocatedBytes
        => (long)GetOrdinals().Length * sizeof(int) + (long)GetOffsets().Length * sizeof(int);

    public ReadOnlyMemory<int> GetPartition(int partition)
    {
        if ((uint)partition >= (uint)PartitionCount) throw new ArgumentOutOfRangeException(nameof(partition));
        var offsets = GetOffsets();
        return GetOrdinals().AsMemory(offsets[partition], offsets[partition + 1] - offsets[partition]);
    }

    public void Dispose()
    {
        var ordinals = Interlocked.Exchange(ref _ordinals, null);
        var offsets = Interlocked.Exchange(ref _offsets, null);
        if (ordinals != null) ArrayPool<int>.Shared.Return(ordinals, clearArray: false);
        if (offsets != null) ArrayPool<int>.Shared.Return(offsets, clearArray: true);
    }

    private int[] GetOrdinals() => _ordinals ?? throw new ObjectDisposedException(nameof(NativePartitionRouting));
    private int[] GetOffsets() => _offsets ?? throw new ObjectDisposedException(nameof(NativePartitionRouting));
}

public static class ColumnBatchPartitionKernels
{
    public static NativePartitionRouting HashPartition<T>(
        ColumnBatch batch,
        string keyColumnName,
        int partitionCount,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default) where T : unmanaged
    {
        if (partitionCount <= 0) throw new ArgumentOutOfRangeException(nameof(partitionCount));
        var column = batch.GetColumn<T>(keyColumnName);
        var values = column.Values;
        var candidateCount = selection?.Count ?? batch.RowCount;
        int[]? counts = null;
        int[]? offsets = null;
        int[]? cursors = null;
        int[]? ordinals = null;

        try
        {
            counts = ArrayPool<int>.Shared.Rent(partitionCount);
            offsets = ArrayPool<int>.Shared.Rent(partitionCount + 1);
            cursors = ArrayPool<int>.Shared.Rent(partitionCount);
            ordinals = ArrayPool<int>.Shared.Rent(Math.Max(1, candidateCount));
            counts.AsSpan(0, partitionCount).Clear();
            offsets.AsSpan(0, partitionCount + 1).Clear();

            int PartitionFor(int row)
            {
                if (column.IsNull(row)) return 0;
                var hash = EqualityComparer<T>.Default.GetHashCode(values.Span[row]);
                return (int)((uint)hash % (uint)partitionCount);
            }

            VisitOrdinals(batch.RowCount, selection, cancellationToken, row => counts[PartitionFor(row)]++);
            for (var partition = 0; partition < partitionCount; partition++)
                offsets[partition + 1] = checked(offsets[partition] + counts[partition]);
            offsets.AsSpan(0, partitionCount).CopyTo(cursors);
            VisitOrdinals(batch.RowCount, selection, cancellationToken, row =>
            {
                var partition = PartitionFor(row);
                ordinals[cursors[partition]++] = row;
            });

            var result = new NativePartitionRouting(ordinals, offsets, candidateCount, partitionCount);
            ordinals = null;
            offsets = null;
            return result;
        }
        finally
        {
            if (counts != null) ArrayPool<int>.Shared.Return(counts, clearArray: true);
            if (cursors != null) ArrayPool<int>.Shared.Return(cursors, clearArray: true);
            if (ordinals != null) ArrayPool<int>.Shared.Return(ordinals, clearArray: false);
            if (offsets != null) ArrayPool<int>.Shared.Return(offsets, clearArray: true);
        }
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
