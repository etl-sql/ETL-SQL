using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

internal interface IColumnarGroupedAggregatePlan : IDisposable
{
    bool CanApply(ColumnBatch batch);
    void Accumulate(ColumnBatch batch, SelectionVector? selection);
    Task<DataTable> FinalizeResultAsync(IReadOnlyList<string> outputNames);
}

internal sealed class ColumnarGroupedAggregatePlan : IColumnarGroupedAggregatePlan
{
    private readonly IExecutionContext _context;
    private readonly string _keyColumn;
    private readonly string[] _valueColumns;
    private readonly Slot[] _slots;
    private readonly Expression? _havingClause;
    private readonly List<IState> _states = new();

    private ColumnarGroupedAggregatePlan(
        IExecutionContext context, string keyColumn, string[] valueColumns, Slot[] slots, Expression? havingClause)
    {
        _context = context;
        _keyColumn = keyColumn;
        _valueColumns = valueColumns;
        _slots = slots;
        _havingClause = havingClause;
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
            || statement.GroupingSet != null
            || statement.OrderBy != null || statement.Offset != null || statement.LimitCount != null
            || statement.TopCount != null || statement.IsDistinct || statement.QualifyClause != null
            || statement.Sample != null || statement.IsTopPercent || statement.GroupByAll || statement.OrderByAll)
            return false;
        var keyColumn = key.Name.Split('.').Last();
        var valueColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slots = new List<Slot>(statement.Columns.Count);
        var projectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aggregateAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in statement.Columns)
        {
            if (column.Expression is IdentifierExpression identifier
                && identifier.Name.Split('.').Last().Equals(keyColumn, StringComparison.OrdinalIgnoreCase))
            {
                slots.Add(new Slot(SlotKind.Key, null, false));
                projectedNames.Add(column.Alias ?? identifier.Name.Split('.').Last());
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
                valueColumns.Add(argument);
            }
            slots.Add(new Slot(kind, argument, countStar));
            var outputName = column.Alias ?? column.Expression.ToSql();
            projectedNames.Add(outputName);
            aggregateAliases[column.Expression.ToSql()] = outputName;
        }
        if (valueColumns.Count > 1 && !slots.Any(slot => slot.Kind == SlotKind.Key)) return false;
        if (!TryRewriteHaving(statement.HavingClause, aggregateAliases, projectedNames, out var havingClause))
            return false;
        plan = new ColumnarGroupedAggregatePlan(
            context, keyColumn, valueColumns.ToArray(), slots.ToArray(), havingClause);
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
        foreach (var valueColumn in _valueColumns)
        {
            try { if (!IsNumeric(batch.GetColumn(valueColumn).ElementType)) return false; }
            catch (KeyNotFoundException) { return false; }
        }
        return true;
    }

    public void Accumulate(ColumnBatch batch, SelectionVector? selection)
    {
        if (_states.Count == 0)
        {
            if (_valueColumns.Length == 0) _states.Add(CreateState(batch, null));
            else foreach (var valueColumn in _valueColumns) _states.Add(CreateState(batch, valueColumn));
        }
        foreach (var state in _states) state.Accumulate(batch, selection);
    }

    public async Task<DataTable> FinalizeResultAsync(IReadOnlyList<string> outputNames)
    {
        var table = new DataTable();
        table.SetColumns(outputNames);
        for (var index = 0; index < _states.Count; index++)
            _states[index].WriteRows(table, _slots, createRows: index == 0);
        if (_havingClause != null)
        {
            for (var index = table.Rows.Count - 1; index >= 0; index--)
                if (!await _context.EvaluateCondition(_havingClause, table.Rows[index]))
                    table.Rows.RemoveAt(index);
        }
        return table;
    }

    public void Dispose()
    {
        foreach (var state in _states) state.Dispose();
        _states.Clear();
    }

    private IState CreateState(ColumnBatch batch, string? valueColumn)
    {
        var keyType = batch.GetColumn(_keyColumn).ElementType;
        if (valueColumn == null)
        {
            if (keyType == typeof(string))
                return new StringCountState(_context, _keyColumn);
            var countType = typeof(CountState<>).MakeGenericType(keyType);
            return (IState)Activator.CreateInstance(
                countType, _context, _keyColumn,
                batch.Schema.Fields[batch.Schema.GetOrdinal(_keyColumn)].LogicalType)!;
        }
        var valueType = batch.GetColumn(valueColumn).ElementType;
        if (keyType == typeof(string))
        {
            var stringStateType = typeof(StringState<>).MakeGenericType(valueType);
            return (IState)Activator.CreateInstance(
                stringStateType, _context, _keyColumn, valueColumn,
                batch.Schema.Fields[batch.Schema.GetOrdinal(valueColumn)].LogicalType)!;
        }
        var stateType = typeof(State<,>).MakeGenericType(keyType, valueType);
        return (IState)Activator.CreateInstance(
            stateType, _context, _keyColumn, valueColumn,
            batch.Schema.Fields[batch.Schema.GetOrdinal(_keyColumn)].LogicalType,
            batch.Schema.Fields[batch.Schema.GetOrdinal(valueColumn)].LogicalType)!;
    }

    private static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool IsSupportedKey(Type type)
        => IsNumeric(type) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(string);

    private static bool TryRewriteHaving(
        Expression? expression,
        IReadOnlyDictionary<string, string> aggregateAliases,
        IReadOnlySet<string> projectedNames,
        out Expression? rewritten)
    {
        if (expression == null) { rewritten = null; return true; }
        switch (expression)
        {
            case LiteralExpression:
                rewritten = expression;
                return true;
            case IdentifierExpression identifier when projectedNames.Contains(identifier.Name.Split('.').Last()):
                rewritten = new IdentifierExpression(identifier.Name.Split('.').Last());
                return true;
            case FunctionCallExpression function when aggregateAliases.TryGetValue(function.ToSql(), out var alias):
                rewritten = new IdentifierExpression(alias);
                return true;
            case BinaryExpression binary
                when TryRewriteHaving(binary.Left, aggregateAliases, projectedNames, out var left)
                    && TryRewriteHaving(binary.Right, aggregateAliases, projectedNames, out var right):
                rewritten = new BinaryExpression(left!, binary.Operator, right!);
                return true;
            case UnaryExpression unary
                when TryRewriteHaving(unary.Expression, aggregateAliases, projectedNames, out var inner):
                rewritten = new UnaryExpression(unary.Operator, inner!);
                return true;
            case IsNullExpression isNull
                when TryRewriteHaving(isNull.Expression, aggregateAliases, projectedNames, out var nullInner):
                rewritten = new IsNullExpression(nullInner!, isNull.Not);
                return true;
            default:
                rewritten = null;
                return false;
        }
    }

    private interface IState : IDisposable
    {
        void Accumulate(ColumnBatch batch, SelectionVector? selection);
        void WriteRows(DataTable table, IReadOnlyList<Slot> slots, bool createRows);
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

        public void WriteRows(DataTable table, IReadOnlyList<Slot> slots, bool createRows)
        {
            if (_result == null) return;
            var nullKey = new object();
            Dictionary<object, Row>? existing = null;
            if (!createRows)
                existing = table.Rows.ToDictionary(row => row[GetKeyOrdinal(slots)] ?? nullKey);
            foreach (var (key, state) in _result.Groups)
            {
                var restoredKey = key.IsNull ? null : ColumnBatchAdapter.RestoreEngineValue(key.Value, _keyLogicalType);
                var row = createRows
                    ? table.NewRow()
                    : existing![restoredKey ?? nullKey];
                for (var index = 0; index < slots.Count; index++)
                {
                    var slot = slots[index];
                    if (slot.Kind == SlotKind.Key)
                    {
                        if (createRows) row[index] = restoredKey;
                        continue;
                    }
                    if (slot.Kind == SlotKind.Count && slot.CountStar)
                    {
                        if (createRows) row[index] = (decimal)state.RowCount;
                        continue;
                    }
                    if (!string.Equals(slot.ValueColumn, _valueColumn, StringComparison.OrdinalIgnoreCase))
                        continue;
                    row[index] = slot.Kind switch
                    {
                        SlotKind.Count => (decimal)state.NonNullCount,
                        SlotKind.Sum => state.NonNullCount == 0 ? null : state.Sum,
                        SlotKind.Average => state.Average,
                        SlotKind.Min => state.NonNullCount == 0 ? null : ColumnBatchAdapter.RestoreEngineValue(state.Min, _valueLogicalType),
                        SlotKind.Max => state.NonNullCount == 0 ? null : ColumnBatchAdapter.RestoreEngineValue(state.Max, _valueLogicalType),
                        _ => null
                    };
                }
                if (createRows) table.Rows.Add(row);
            }
        }

        private static int GetKeyOrdinal(IReadOnlyList<Slot> slots)
        {
            for (var index = 0; index < slots.Count; index++)
                if (slots[index].Kind == SlotKind.Key) return index;
            throw new InvalidOperationException("Grouped native output requires its key column.");
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

        public void WriteRows(DataTable table, IReadOnlyList<Slot> slots, bool createRows)
        {
            if (!createRows) throw new InvalidOperationException("Key-only count state must create grouped output rows.");
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

    private sealed class StringState<TValue> : IState where TValue : unmanaged, INumber<TValue>
    {
        private readonly IExecutionContext _context;
        private readonly string _keyColumn;
        private readonly string _valueColumn;
        private readonly string _valueLogicalType;
        private readonly NativeStringGroupAggregateResult<TValue> _result;

        public StringState(
            IExecutionContext context,
            string keyColumn,
            string valueColumn,
            string valueLogicalType)
        {
            _context = context;
            _keyColumn = keyColumn;
            _valueColumn = valueColumn;
            _valueLogicalType = valueLogicalType;
            _result = new NativeStringGroupAggregateResult<TValue>(context.MemoryArbiter);
        }

        public void Accumulate(ColumnBatch batch, SelectionVector? selection)
            => _result.Accumulate(
                batch, _keyColumn, _valueColumn, selection, _context.CancellationToken);

        public void WriteRows(DataTable table, IReadOnlyList<Slot> slots, bool createRows)
        {
            var nullKey = new object();
            Dictionary<object, Row>? existing = null;
            if (!createRows)
                existing = table.Rows.ToDictionary(row => row[GetKeyOrdinal(slots)] ?? nullKey);
            foreach (var (key, state) in _result.Groups)
            {
                var restoredKey = key.IsNull ? null : key.Value;
                var row = createRows ? table.NewRow() : existing![restoredKey ?? nullKey];
                for (var index = 0; index < slots.Count; index++)
                {
                    var slot = slots[index];
                    if (slot.Kind == SlotKind.Key)
                    {
                        if (createRows) row[index] = restoredKey;
                        continue;
                    }
                    if (slot.Kind == SlotKind.Count && slot.CountStar)
                    {
                        if (createRows) row[index] = (decimal)state.RowCount;
                        continue;
                    }
                    if (!string.Equals(slot.ValueColumn, _valueColumn, StringComparison.OrdinalIgnoreCase))
                        continue;
                    row[index] = slot.Kind switch
                    {
                        SlotKind.Count => (decimal)state.NonNullCount,
                        SlotKind.Sum => state.NonNullCount == 0 ? null : state.Sum,
                        SlotKind.Average => state.Average,
                        SlotKind.Min => state.NonNullCount == 0 ? null : ColumnBatchAdapter.RestoreEngineValue(state.Min, _valueLogicalType),
                        SlotKind.Max => state.NonNullCount == 0 ? null : ColumnBatchAdapter.RestoreEngineValue(state.Max, _valueLogicalType),
                        _ => null
                    };
                }
                if (createRows) table.Rows.Add(row);
            }
        }

        private static int GetKeyOrdinal(IReadOnlyList<Slot> slots)
        {
            for (var index = 0; index < slots.Count; index++)
                if (slots[index].Kind == SlotKind.Key) return index;
            throw new InvalidOperationException("Grouped native output requires its key column.");
        }

        public void Dispose() => _result.Dispose();
    }

    private sealed class StringCountState : IState
    {
        private readonly IExecutionContext _context;
        private readonly string _keyColumn;
        private readonly NativeStringGroupCountResult _result;

        public StringCountState(IExecutionContext context, string keyColumn)
        {
            _context = context;
            _keyColumn = keyColumn;
            _result = new NativeStringGroupCountResult(context.MemoryArbiter);
        }

        public void Accumulate(ColumnBatch batch, SelectionVector? selection)
            => _result.Accumulate(batch, _keyColumn, selection, _context.CancellationToken);

        public void WriteRows(DataTable table, IReadOnlyList<Slot> slots, bool createRows)
        {
            if (!createRows) throw new InvalidOperationException("String count state must create grouped output rows.");
            foreach (var (key, count) in _result.Groups)
            {
                var row = table.NewRow();
                for (var index = 0; index < slots.Count; index++)
                {
                    row[index] = slots[index].Kind switch
                    {
                        SlotKind.Key => key.IsNull ? null : key.Value,
                        SlotKind.Count when slots[index].CountStar => (decimal)count,
                        _ => null
                    };
                }
                table.Rows.Add(row);
            }
        }

        public void Dispose() => _result.Dispose();
    }

    private sealed record Slot(SlotKind Kind, string? ValueColumn, bool CountStar);
    private enum SlotKind { Unsupported, Key, Count, Sum, Average, Min, Max }
}
