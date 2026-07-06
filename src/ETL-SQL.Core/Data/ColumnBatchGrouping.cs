using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Data;

public readonly record struct NativeGroupKey<T>(bool IsNull, T Value) where T : unmanaged;

public readonly record struct NativeStringGroupKey(bool IsNull, object? Value);

/// <summary>Memory-accounted COUNT(*) grouping over normalized UTF-8 keys.</summary>
public sealed class NativeStringGroupCountResult : IDisposable
{
    private Dictionary<NativeStringGroupKey, long>? _groups = new();
    private IMemoryGrantLease? _lease;

    public NativeStringGroupCountResult(IMemoryGrantArbiter? memoryArbiter = null)
    {
        _lease = (memoryArbiter ?? UnlimitedMemoryGrantArbiter.Instance).AcquireLease();
        Groups = new ReadOnlyDictionary<NativeStringGroupKey, long>(_groups);
    }

    public IReadOnlyDictionary<NativeStringGroupKey, long> Groups { get; }
    public long EstimatedBytes { get; private set; }

    public void Accumulate(
        ColumnBatch batch,
        string keyColumnName,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        var groups = _groups ?? throw new ObjectDisposedException(nameof(NativeStringGroupCountResult));
        var lease = _lease ?? throw new ObjectDisposedException(nameof(NativeStringGroupCountResult));
        var keys = batch.GetUtf8Column(keyColumnName);
        void CountRow(int row)
        {
            NativeStringGroupKey key;
            var keyBytes = 0;
            if (keys.IsNull(row)) key = new NativeStringGroupKey(true, null);
            else
            {
                var encoded = keys.GetUtf8Bytes(row);
                keyBytes = encoded.Length;
                key = new NativeStringGroupKey(
                    false, CompoundKey.NormalizeValue(Encoding.UTF8.GetString(encoded)));
            }
            if (!groups.TryGetValue(key, out var count))
            {
                var prospective = checked(EstimatedBytes + 96L + keyBytes * 2L);
                if (lease.RegisterAndCheckSpill(prospective))
                    throw new ExecutionException(
                        $"Native string GROUP BY requires more than {EstimatedBytes:N0} bytes of count state. " +
                        "Increase Engine:TotalMemoryGrantMB or use spill-capable grouped execution.");
                EstimatedBytes = prospective;
            }
            groups[key] = count + 1;
        }

        if (selection == null)
        {
            for (var row = 0; row < batch.RowCount; row++)
            {
                if ((row & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                CountRow(row);
            }
        }
        else
        {
            foreach (var row in selection.Indices.Span)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((uint)row >= (uint)batch.RowCount)
                    throw new ArgumentOutOfRangeException(nameof(selection), "Selection contains an invalid row ordinal.");
                CountRow(row);
            }
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _groups, null)?.Clear();
        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}

public readonly record struct NativeAggregateState<T>(
    long RowCount,
    long NonNullCount,
    decimal Sum,
    T Min,
    T Max) where T : unmanaged
{
    public decimal? Average => NonNullCount == 0 ? null : Sum / NonNullCount;
}

/// <summary>Memory-accounted numeric aggregate state keyed by normalized UTF-8 values.</summary>
public sealed class NativeStringGroupAggregateResult<TValue> : IDisposable
    where TValue : unmanaged, INumber<TValue>
{
    private Dictionary<NativeStringGroupKey, NativeAggregateState<TValue>>? _groups = new();
    private IMemoryGrantLease? _lease;

    public NativeStringGroupAggregateResult(IMemoryGrantArbiter? memoryArbiter = null)
    {
        _lease = (memoryArbiter ?? UnlimitedMemoryGrantArbiter.Instance).AcquireLease();
        Groups = new ReadOnlyDictionary<NativeStringGroupKey, NativeAggregateState<TValue>>(_groups);
    }

    public IReadOnlyDictionary<NativeStringGroupKey, NativeAggregateState<TValue>> Groups { get; }
    public long EstimatedBytes { get; private set; }

    public void Accumulate(
        ColumnBatch batch,
        string keyColumnName,
        string valueColumnName,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        var groups = _groups ?? throw new ObjectDisposedException(nameof(NativeStringGroupAggregateResult<TValue>));
        var lease = _lease ?? throw new ObjectDisposedException(nameof(NativeStringGroupAggregateResult<TValue>));
        var keys = batch.GetUtf8Column(keyColumnName);
        var values = batch.GetColumn<TValue>(valueColumnName);
        void AggregateRow(int row)
        {
            NativeStringGroupKey key;
            var keyBytes = 0;
            if (keys.IsNull(row)) key = new NativeStringGroupKey(true, null);
            else
            {
                var encoded = keys.GetUtf8Bytes(row);
                keyBytes = encoded.Length;
                key = new NativeStringGroupKey(
                    false, CompoundKey.NormalizeValue(Encoding.UTF8.GetString(encoded)));
            }
            if (!groups.TryGetValue(key, out var state))
            {
                var prospective = checked(
                    EstimatedBytes + 96L + keyBytes * 2L + Unsafe.SizeOf<NativeAggregateState<TValue>>());
                if (lease.RegisterAndCheckSpill(prospective))
                    throw new ExecutionException(
                        $"Native string GROUP BY requires more than {EstimatedBytes:N0} bytes of aggregate state. " +
                        "Increase Engine:TotalMemoryGrantMB or use spill-capable grouped execution.");
                EstimatedBytes = prospective;
                state = new NativeAggregateState<TValue>(0, 0, 0, default, default);
            }

            state = state with { RowCount = state.RowCount + 1 };
            if (!values.IsNull(row))
            {
                var value = values.Values.Span[row];
                state = state.NonNullCount == 0
                    ? state with { NonNullCount = 1, Sum = decimal.CreateChecked(value), Min = value, Max = value }
                    : state with
                    {
                        NonNullCount = state.NonNullCount + 1,
                        Sum = checked(state.Sum + decimal.CreateChecked(value)),
                        Min = value < state.Min ? value : state.Min,
                        Max = value > state.Max ? value : state.Max
                    };
            }
            groups[key] = state;
        }

        if (selection == null)
        {
            for (var row = 0; row < batch.RowCount; row++)
            {
                if ((row & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                AggregateRow(row);
            }
        }
        else
        {
            foreach (var row in selection.Indices.Span)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((uint)row >= (uint)batch.RowCount)
                    throw new ArgumentOutOfRangeException(nameof(selection), "Selection contains an invalid row ordinal.");
                AggregateRow(row);
            }
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _groups, null)?.Clear();
        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}

/// <summary>Memory-accounted key-only grouped row counts for COUNT(*) plans.</summary>
public sealed class NativeGroupCountResult<TKey> : IDisposable where TKey : unmanaged
{
    private Dictionary<NativeGroupKey<TKey>, long>? _groups = new();
    private IMemoryGrantLease? _lease;

    public NativeGroupCountResult(IMemoryGrantArbiter? memoryArbiter = null)
    {
        _lease = (memoryArbiter ?? UnlimitedMemoryGrantArbiter.Instance).AcquireLease();
        Groups = new ReadOnlyDictionary<NativeGroupKey<TKey>, long>(_groups);
    }

    public IReadOnlyDictionary<NativeGroupKey<TKey>, long> Groups { get; }
    public long EstimatedBytes { get; private set; }

    public void Accumulate(
        ColumnBatch batch,
        string keyColumnName,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        var groups = _groups ?? throw new ObjectDisposedException(nameof(NativeGroupCountResult<TKey>));
        var lease = _lease ?? throw new ObjectDisposedException(nameof(NativeGroupCountResult<TKey>));
        var keyColumn = batch.GetColumn<TKey>(keyColumnName);
        var keyValues = keyColumn.Values;
        void CountRow(int row)
        {
            var key = keyColumn.IsNull(row)
                ? new NativeGroupKey<TKey>(true, default)
                : new NativeGroupKey<TKey>(false, keyValues.Span[row]);
            if (!groups.TryGetValue(key, out var count))
            {
                var prospective = checked(EstimatedBytes + 64L + Unsafe.SizeOf<NativeGroupKey<TKey>>() + sizeof(long));
                if (lease.RegisterAndCheckSpill(prospective))
                    throw new ExecutionException(
                        $"Native GROUP BY requires more than {EstimatedBytes:N0} bytes of count state. " +
                        "Increase Engine:TotalMemoryGrantMB or use spill-capable grouped execution.");
                EstimatedBytes = prospective;
            }
            groups[key] = count + 1;
        }

        if (selection == null)
        {
            for (var row = 0; row < batch.RowCount; row++)
            {
                if ((row & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                CountRow(row);
            }
        }
        else
        {
            var ordinals = selection.Indices.Span;
            for (var position = 0; position < ordinals.Length; position++)
            {
                if ((position & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var row = ordinals[position];
                if ((uint)row >= (uint)batch.RowCount)
                    throw new ArgumentOutOfRangeException(nameof(selection), "Selection vector contains an invalid row ordinal.");
                CountRow(row);
            }
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _groups, null)?.Clear();
        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}

/// <summary>Memory-accounted native grouped state. Dispose after consuming the groups.</summary>
public sealed class NativeGroupAggregateResult<TKey, TValue> : IDisposable
    where TKey : unmanaged
    where TValue : unmanaged, INumber<TValue>
{
    private Dictionary<NativeGroupKey<TKey>, NativeAggregateState<TValue>>? _groups;
    private IMemoryGrantLease? _lease;

    internal NativeGroupAggregateResult(
        Dictionary<NativeGroupKey<TKey>, NativeAggregateState<TValue>> groups,
        IMemoryGrantLease lease,
        long estimatedBytes)
    {
        _groups = groups;
        _lease = lease;
        Groups = new ReadOnlyDictionary<NativeGroupKey<TKey>, NativeAggregateState<TValue>>(groups);
        EstimatedBytes = estimatedBytes;
    }

    public IReadOnlyDictionary<NativeGroupKey<TKey>, NativeAggregateState<TValue>> Groups { get; }
    public long EstimatedBytes { get; private set; }

    public void Accumulate(
        ColumnBatch batch,
        string keyColumnName,
        string valueColumnName,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        var groups = _groups ?? throw new ObjectDisposedException(nameof(NativeGroupAggregateResult<TKey, TValue>));
        var lease = _lease ?? throw new ObjectDisposedException(nameof(NativeGroupAggregateResult<TKey, TValue>));
        var keyColumn = batch.GetColumn<TKey>(keyColumnName);
        var valueColumn = batch.GetColumn<TValue>(valueColumnName);
        var keyValues = keyColumn.Values;
        var values = valueColumn.Values;

        void AggregateRow(int row)
        {
            var key = keyColumn.IsNull(row)
                ? new NativeGroupKey<TKey>(true, default)
                : new NativeGroupKey<TKey>(false, keyValues.Span[row]);
            if (!groups.TryGetValue(key, out var state))
            {
                var prospective = checked(EstimatedBytes + EstimateGroupBytes());
                if (lease.RegisterAndCheckSpill(prospective))
                    throw new ExecutionException(
                        $"Native GROUP BY requires more than {EstimatedBytes:N0} bytes of aggregate state. " +
                        "Increase Engine:TotalMemoryGrantMB or use spill-capable grouped execution.");
                EstimatedBytes = prospective;
                state = new NativeAggregateState<TValue>(0, 0, 0, default, default);
            }

            state = state with { RowCount = state.RowCount + 1 };
            if (!valueColumn.IsNull(row))
            {
                var value = values.Span[row];
                state = state.NonNullCount == 0
                    ? state with { NonNullCount = 1, Sum = decimal.CreateChecked(value), Min = value, Max = value }
                    : state with
                    {
                        NonNullCount = state.NonNullCount + 1,
                        Sum = checked(state.Sum + decimal.CreateChecked(value)),
                        Min = value < state.Min ? value : state.Min,
                        Max = value > state.Max ? value : state.Max
                    };
            }
            groups[key] = state;
        }

        if (selection == null)
        {
            for (var row = 0; row < batch.RowCount; row++)
            {
                if ((row & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                AggregateRow(row);
            }
        }
        else
        {
            var ordinals = selection.Indices.Span;
            for (var position = 0; position < ordinals.Length; position++)
            {
                if ((position & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var row = ordinals[position];
                if ((uint)row >= (uint)batch.RowCount)
                    throw new ArgumentOutOfRangeException(nameof(selection), "Selection vector contains an invalid row ordinal.");
                AggregateRow(row);
            }
        }
    }

    private static long EstimateGroupBytes()
        => 64L + Unsafe.SizeOf<NativeGroupKey<TKey>>() + Unsafe.SizeOf<NativeAggregateState<TValue>>();

    public void Dispose()
    {
        var groups = Interlocked.Exchange(ref _groups, null);
        groups?.Clear();
        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}

public static partial class ColumnBatchGroupKernels
{
    public static NativeGroupAggregateResult<TKey, TValue> GroupAggregate<TKey, TValue>(
        ColumnBatch batch,
        string keyColumnName,
        string valueColumnName,
        IMemoryGrantArbiter? memoryArbiter = null,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
        where TKey : unmanaged
        where TValue : unmanaged, INumber<TValue>
    {
        var groups = new Dictionary<NativeGroupKey<TKey>, NativeAggregateState<TValue>>();
        var lease = (memoryArbiter ?? UnlimitedMemoryGrantArbiter.Instance).AcquireLease();
        var result = new NativeGroupAggregateResult<TKey, TValue>(groups, lease, 0);

        try
        {
            result.Accumulate(batch, keyColumnName, valueColumnName, selection, cancellationToken);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}
