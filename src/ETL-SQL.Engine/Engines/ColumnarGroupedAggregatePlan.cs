using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

internal sealed class ColumnarGroupedAggregatePlan : IDisposable
{
    private readonly IExecutionContext _context;
    private readonly string _keyColumn;
    private readonly string? _valueColumn;
    private readonly Slot[] _slots;
    private IState? _state;

    private ColumnarGroupedAggregatePlan(
        IExecutionContext context, string keyColumn, string? valueColumn, Slot[] slots)
    {
        _context = context;
        _keyColumn = keyColumn;
        _valueColumn = valueColumn;
        _slots = slots;
    }

    public static bool TryCreate(
        IExecutionContext context,
        SelectStatement statement,
        out ColumnarGroupedAggregatePlan? plan)
    {
        plan = null;
        if (statement.FromTable.TableOperators.Count != 0
            || statement.Joins.Count != 0
            || statement.GroupBy?.Count != 1 || statement.GroupBy[0] is not IdentifierExpression key
            || statement.GroupingSet != null || statement.HavingClause != null
            || statement.OrderBy != null || statement.Offset != null || statement.LimitCount != null
            || statement.TopCount != null || statement.IsDistinct || statement.QualifyClause != null
            || statement.Sample != null || statement.IsTopPercent || statement.GroupByAll || statement.OrderByAll)
            return false;
        var keyColumn = key.Name.Split('.').Last();
        string? valueColumn = null;
        var slots = new List<Slot>(statement.Columns.Count);
        foreach (var column in statement.Columns)
        {
            if (column.Expression is IdentifierExpression identifier
                && identifier.Name.Split('.').Last().Equals(keyColumn, StringComparison.OrdinalIgnoreCase))
            {
                slots.Add(new Slot(SlotKind.Key));
                continue;
            }
            if (column.Expression is not FunctionCallExpression function
                || function.IsDistinct || function.Filter != null || function.Window != null)
                return false;
            var kind = function.FunctionName.ToUpperInvariant() switch
            {
                "COUNT" => SlotKind.Count,
                "SUM" => SlotKind.Sum,
                "AVG" => SlotKind.Average,
                "MIN" => SlotKind.Min,
                "MAX" => SlotKind.Max,
                _ => SlotKind.Unsupported
            };
            if (kind == SlotKind.Unsupported) return false;
            var countStar = kind == SlotKind.Count && (function.Arguments.Count == 0
                || function.Arguments[0] is StarExpression
                || function.Arguments[0] is IdentifierExpression { Name: "*" });
            string? argument = null;
            if (!countStar)
            {
                if (function.Arguments.Count != 1 || function.Arguments[0] is not IdentifierExpression value)
                    return false;
                argument = value.Name.Split('.').Last();
                if (valueColumn != null && !valueColumn.Equals(argument, StringComparison.OrdinalIgnoreCase))
                    return false;
                valueColumn = argument;
            }
            slots.Add(new Slot(kind, countStar));
        }
        plan = new ColumnarGroupedAggregatePlan(context, keyColumn, valueColumn, slots.ToArray());
        return true;
    }

    public bool CanApply(ColumnBatch batch)
    {
        IColumnBuffer key;
        try
        {
            key = batch.GetColumn(_keyColumn);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        if (!IsSupportedKey(key.ElementType)) return false;
        if (_valueColumn == null) return true;
        try { return IsNumeric(batch.GetColumn(_valueColumn).ElementType); }
        catch (KeyNotFoundException) { return false; }
    }

    public void Accumulate(ColumnBatch batch, SelectionVector? selection)
    {
        _state ??= CreateState(batch);
        _state.Accumulate(batch, selection);
    }

    public DataTable FinalizeResult(IReadOnlyList<string> outputNames)
    {
        var table = new DataTable();
        table.SetColumns(outputNames);
        _state?.WriteRows(table, _slots);
        return table;
    }

    public void Dispose() => _state?.Dispose();

    private IState CreateState(ColumnBatch batch)
    {
        var keyType = batch.GetColumn(_keyColumn).ElementType;
        if (_valueColumn == null)
        {
            var countType = typeof(CountState<>).MakeGenericType(keyType);
            return (IState)Activator.CreateInstance(
                countType, _context, _keyColumn,
                batch.Schema.Fields[batch.Schema.GetOrdinal(_keyColumn)].LogicalType)!;
        }
        var valueType = batch.GetColumn(_valueColumn).ElementType;
        var stateType = typeof(State<,>).MakeGenericType(keyType, valueType);
        return (IState)Activator.CreateInstance(
            stateType, _context, _keyColumn, _valueColumn,
            batch.Schema.Fields[batch.Schema.GetOrdinal(_keyColumn)].LogicalType,
            batch.Schema.Fields[batch.Schema.GetOrdinal(_valueColumn)].LogicalType)!;
    }

    private static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool IsSupportedKey(Type type)
        => IsNumeric(type) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid);

    private interface IState : IDisposable
    {
        void Accumulate(ColumnBatch batch, SelectionVector? selection);
        void WriteRows(DataTable table, IReadOnlyList<Slot> slots);
    }

    private sealed class State<TKey, TValue> : IState
        where TKey : unmanaged
        where TValue : unmanaged, INumber<TValue>
    {
        private readonly IExecutionContext _context;
        private readonly string _keyColumn;
        private readonly string _valueColumn;
        private readonly string _keyLogicalType;
        private readonly string _valueLogicalType;
        private NativeGroupAggregateResult<TKey, TValue>? _result;

        public State(
            IExecutionContext context,
            string keyColumn,
            string valueColumn,
            string keyLogicalType,
            string valueLogicalType)
        {
            _context = context;
            _keyColumn = keyColumn;
            _valueColumn = valueColumn;
            _keyLogicalType = keyLogicalType;
            _valueLogicalType = valueLogicalType;
        }

        public void Accumulate(ColumnBatch batch, SelectionVector? selection)
        {
            if (_result == null)
                _result = ColumnBatchGroupKernels.GroupAggregate<TKey, TValue>(
                    batch, _keyColumn, _valueColumn, _context.MemoryArbiter,
                    selection, _context.CancellationToken);
            else
                _result.Accumulate(batch, _keyColumn, _valueColumn, selection, _context.CancellationToken);
        }

        public void WriteRows(DataTable table, IReadOnlyList<Slot> slots)
        {
            if (_result == null) return;
            foreach (var (key, state) in _result.Groups)
            {
                var row = table.NewRow();
                for (var index = 0; index < slots.Count; index++)
                {
                    row[index] = slots[index].Kind switch
                    {
                        SlotKind.Key => key.IsNull ? null : ColumnBatchAdapter.RestoreEngineValue(key.Value, _keyLogicalType),
                        SlotKind.Count => (decimal)(slots[index].CountStar ? state.RowCount : state.NonNullCount),
                        SlotKind.Sum => state.NonNullCount == 0 ? null : state.Sum,
                        SlotKind.Average => state.Average,
                        SlotKind.Min => state.NonNullCount == 0 ? null : ColumnBatchAdapter.RestoreEngineValue(state.Min, _valueLogicalType),
                        SlotKind.Max => state.NonNullCount == 0 ? null : ColumnBatchAdapter.RestoreEngineValue(state.Max, _valueLogicalType),
                        _ => null
                    };
                }
                table.Rows.Add(row);
            }
        }

        public void Dispose() => _result?.Dispose();
    }

    private sealed class CountState<TKey> : IState where TKey : unmanaged
    {
        private readonly IExecutionContext _context;
        private readonly string _keyColumn;
        private readonly string _keyLogicalType;
        private readonly NativeGroupCountResult<TKey> _result;

        public CountState(IExecutionContext context, string keyColumn, string keyLogicalType)
        {
            _context = context;
            _keyColumn = keyColumn;
            _keyLogicalType = keyLogicalType;
            _result = new NativeGroupCountResult<TKey>(context.MemoryArbiter);
        }

        public void Accumulate(ColumnBatch batch, SelectionVector? selection)
            => _result.Accumulate(batch, _keyColumn, selection, _context.CancellationToken);

        public void WriteRows(DataTable table, IReadOnlyList<Slot> slots)
        {
            foreach (var (key, count) in _result.Groups)
            {
                var row = table.NewRow();
                for (var index = 0; index < slots.Count; index++)
                {
                    row[index] = slots[index].Kind switch
                    {
                        SlotKind.Key => key.IsNull ? null : ColumnBatchAdapter.RestoreEngineValue(key.Value, _keyLogicalType),
                        SlotKind.Count when slots[index].CountStar => (decimal)count,
                        _ => null
                    };
                }
                table.Rows.Add(row);
            }
        }

        public void Dispose() => _result.Dispose();
    }

    private sealed record Slot(SlotKind Kind, bool CountStar = false);
    private enum SlotKind { Unsupported, Key, Count, Sum, Average, Min, Max }
}
