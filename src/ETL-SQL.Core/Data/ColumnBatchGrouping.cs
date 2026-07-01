using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Data;

public readonly record struct NativeGroupKey<T>(bool IsNull, T Value) where T : unmanaged;

public readonly record struct NativeAggregateState<T>(
    long RowCount,
    long NonNullCount,
    decimal Sum,
    T Min,
    T Max) where T : unmanaged;

/// <summary>Memory-accounted native grouped state. Dispose after consuming the groups.</summary>
public sealed class NativeGroupAggregateResult<TKey, TValue> : IDisposable
    where TKey : unmanaged
    where TValue : unmanaged
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
    public long EstimatedBytes { get; }

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
        var keyColumn = batch.GetColumn<TKey>(keyColumnName);
        var valueColumn = batch.GetColumn<TValue>(valueColumnName);
        var keyValues = keyColumn.Values;
        var values = valueColumn.Values;
        var groups = new Dictionary<NativeGroupKey<TKey>, NativeAggregateState<TValue>>();
        var lease = (memoryArbiter ?? UnlimitedMemoryGrantArbiter.Instance).AcquireLease();
        long estimatedBytes = 0;

        try
        {
            void AggregateRow(int row)
            {
                var key = keyColumn.IsNull(row)
                    ? new NativeGroupKey<TKey>(true, default)
                    : new NativeGroupKey<TKey>(false, keyValues.Span[row]);

                if (!groups.TryGetValue(key, out var state))
                {
                    var prospective = checked(estimatedBytes + EstimateGroupBytes<TKey, TValue>());
                    if (lease.RegisterAndCheckSpill(prospective))
                        throw new ExecutionException(
                            $"Native GROUP BY requires more than {estimatedBytes:N0} bytes of aggregate state. " +
                            "Increase Engine:TotalMemoryGrantMB or use spill-capable grouped execution.");
                    estimatedBytes = prospective;
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

            return new NativeGroupAggregateResult<TKey, TValue>(groups, lease, estimatedBytes);
        }
        catch
        {
            groups.Clear();
            lease.Dispose();
            throw;
        }
    }

    private static long EstimateGroupBytes<TKey, TValue>()
        where TKey : unmanaged
        where TValue : unmanaged
        => 64L + Unsafe.SizeOf<NativeGroupKey<TKey>>() + Unsafe.SizeOf<NativeAggregateState<TValue>>();
}
