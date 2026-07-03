using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ETL_SQL.Data;

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

    /// <summary>
    /// Routes UTF-8 keys using the same normalization and salted hash as one-column CompoundKey.
    /// Equal numeric/date strings and trimmed strings therefore cannot split across partitions.
    /// </summary>
    public static NativePartitionRouting HashPartitionNormalizedUtf8(
        ColumnBatch batch,
        string keyColumnName,
        int partitionCount,
        int hashSalt = 0,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        if (partitionCount <= 0) throw new ArgumentOutOfRangeException(nameof(partitionCount));
        var column = batch.GetUtf8Column(keyColumnName);
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
                object? normalized = null;
                if (!column.IsNull(row))
                    normalized = CompoundKey.NormalizeValue(Encoding.UTF8.GetString(column.GetUtf8Bytes(row)));
                var hash = CompoundKey.GetNormalizedHashCode(hashSalt, normalized);
                return (hash & 0x7fffffff) % partitionCount;
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

    /// <summary>
    /// Routes one or more typed/UTF-8 keys using the exact normalized hash sequence used by
    /// <see cref="CompoundKey"/>, without allocating an object array or per-partition lists per row.
    /// </summary>
    public static NativePartitionRouting HashPartitionNormalized(
        ColumnBatch batch,
        IReadOnlyList<string> keyColumnNames,
        int partitionCount,
        int hashSalt = 0,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        if (keyColumnNames == null || keyColumnNames.Count == 0)
            throw new ArgumentException("At least one partition key is required.", nameof(keyColumnNames));
        if (partitionCount <= 0) throw new ArgumentOutOfRangeException(nameof(partitionCount));
        var columns = keyColumnNames.Select(batch.GetColumn).ToArray();
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
                var hash = new HashCode();
                hash.Add(hashSalt);
                foreach (var column in columns)
                {
                    object? value = null;
                    if (!column.IsNull(row))
                    {
                        value = column is Utf8ColumnBuffer utf8
                            ? Encoding.UTF8.GetString(utf8.GetUtf8Bytes(row))
                            : column.GetBoxedValue(row);
                        value = CompoundKey.NormalizeValue(value);
                    }
                    hash.Add(value);
                }
                return (hash.ToHashCode() & 0x7fffffff) % partitionCount;
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
