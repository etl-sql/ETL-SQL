using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

internal sealed class ColumnarCompositeGroupedAggregatePlan : IColumnarGroupedAggregatePlan
{
    private readonly IExecutionContext _context;
    private readonly string[] _keyColumns;
    private readonly string[] _valueColumns;
    private readonly Slot[] _slots;
    private readonly Expression? _havingClause;
    private readonly Dictionary<CompositeKey, GroupState> _groups = new();
    private readonly IMemoryGrantLease _lease;
    private string[]? _keyLogicalTypes;
    private string[]? _valueLogicalTypes;
    private long _estimatedBytes;

    private ColumnarCompositeGroupedAggregatePlan(
        IExecutionContext context,
        string[] keyColumns,
        string[] valueColumns,
        Slot[] slots,
        Expression? havingClause)
    {
        _context = context;
        _keyColumns = keyColumns;
        _valueColumns = valueColumns;
        _slots = slots;
        _havingClause = havingClause;
        _lease = context.MemoryArbiter.AcquireLease();
    }

    public static bool TryCreate(
        IExecutionContext context,
        SelectStatement statement,
        out ColumnarCompositeGroupedAggregatePlan? plan)
    {
        plan = null;
        if (statement.FromTable.TableOperators.Count != 0 || statement.Joins.Count != 0
            || statement.GroupBy is not { Count: > 1 }
            || statement.GroupBy.Any(expression => expression is not IdentifierExpression)
            || statement.GroupingSet != null || statement.OrderBy != null || statement.Offset != null
            || statement.LimitCount != null || statement.TopCount != null || statement.IsDistinct
            || statement.QualifyClause != null || statement.Sample != null || statement.IsTopPercent
            || statement.GroupByAll || statement.OrderByAll)
            return false;

        var keyColumns = statement.GroupBy.Cast<IdentifierExpression>()
            .Select(key => key.Name.Split('.').Last()).ToArray();
        if (keyColumns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keyColumns.Length) return false;
        var keyIndexes = keyColumns.Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();
        var slots = new List<Slot>(statement.Columns.Count);
        var projectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aggregateAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in statement.Columns)
        {
            if (column.Expression is IdentifierExpression identifier
                && keyIndexes.TryGetValue(identifier.Name.Split('.').Last(), out var keyIndex))
            {
                slots.Add(new Slot(SlotKind.Key, keyIndex, -1, false));
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
            var valueIndex = -1;
            if (!countStar)
            {
                if (function.Arguments.Count != 1 || function.Arguments[0] is not IdentifierExpression value)
                    return false;
                var valueName = value.Name.Split('.').Last();
                valueIndex = values.FindIndex(existing => existing.Equals(valueName, StringComparison.OrdinalIgnoreCase));
                if (valueIndex < 0) { valueIndex = values.Count; values.Add(valueName); }
            }
            slots.Add(new Slot(kind, -1, valueIndex, countStar));
            var outputName = column.Alias ?? column.Expression.ToSql();
            projectedNames.Add(outputName);
            aggregateAliases[column.Expression.ToSql()] = outputName;
        }

        if (!TryRewriteHaving(statement.HavingClause, aggregateAliases, projectedNames, out var having)) return false;
        plan = new ColumnarCompositeGroupedAggregatePlan(
            context, keyColumns, values.ToArray(), slots.ToArray(), having);
        return true;
    }

    public bool CanApply(ColumnBatch batch)
    {
        try
        {
            if (_keyColumns.Any(name => !IsSupportedKey(batch.GetColumn(name).ElementType))) return false;
            if (_valueColumns.Any(name => !IsNumeric(batch.GetColumn(name).ElementType))) return false;
            return true;
        }
        catch (KeyNotFoundException) { return false; }
    }

    public void Accumulate(ColumnBatch batch, SelectionVector? selection)
    {
        _keyLogicalTypes ??= _keyColumns.Select(name => batch.Schema.Fields[batch.Schema.GetOrdinal(name)].LogicalType).ToArray();
        _valueLogicalTypes ??= _valueColumns.Select(name => batch.Schema.Fields[batch.Schema.GetOrdinal(name)].LogicalType).ToArray();
        var keys = _keyColumns.Select(batch.GetColumn).ToArray();
        var values = _valueColumns.Select(batch.GetColumn).ToArray();

        void Add(int row)
        {
            var key = CompositeKey.Create(keys, row, out var keyBytes);
            if (!_groups.TryGetValue(key, out var state))
            {
                var prospective = checked(_estimatedBytes + 96L + keyBytes + 40L * values.Length);
                if (_lease.RegisterAndCheckSpill(prospective))
                    throw new ExecutionException(
                        $"Native composite GROUP BY requires more than {_estimatedBytes:N0} bytes of aggregate state. " +
                        "Increase Engine:TotalMemoryGrantMB or use spill-capable grouped execution.");
                _estimatedBytes = prospective;
                state = new GroupState(values.Length);
                _groups.Add(key, state);
            }
            state.RowCount++;
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index].IsNull(row)) continue;
                state.Values[index].Add(Convert.ToDecimal(values[index].GetBoxedValue(row)));
            }
        }

        if (selection == null)
        {
            for (var row = 0; row < batch.RowCount; row++)
            {
                if ((row & 1023) == 0) _context.CancellationToken.ThrowIfCancellationRequested();
                Add(row);
            }
        }
        else
        {
            foreach (var row in selection.Indices.Span)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
                if ((uint)row >= (uint)batch.RowCount)
                    throw new ArgumentOutOfRangeException(nameof(selection));
                Add(row);
            }
        }
    }

    public async Task<DataTable> FinalizeResultAsync(IReadOnlyList<string> outputNames)
    {
        var table = new DataTable();
        table.SetColumns(outputNames);
        foreach (var (key, state) in _groups)
        {
            var row = table.NewRow();
            for (var index = 0; index < _slots.Length; index++)
            {
                var slot = _slots[index];
                if (slot.Kind == SlotKind.Key)
                {
                    row[index] = ColumnBatchAdapter.RestoreEngineValue(
                        key[slot.KeyIndex], _keyLogicalTypes![slot.KeyIndex]);
                    continue;
                }
                if (slot.CountStar) { row[index] = (decimal)state.RowCount; continue; }
                ref var aggregate = ref state.Values[slot.ValueIndex];
                row[index] = slot.Kind switch
                {
                    SlotKind.Count => (decimal)aggregate.Count,
                    SlotKind.Sum => aggregate.Count == 0 ? null : aggregate.Sum,
                    SlotKind.Average => aggregate.Count == 0 ? null : aggregate.Sum / aggregate.Count,
                    SlotKind.Min => aggregate.Count == 0 ? null : ColumnBatchAdapter.RestoreEngineValue(
                        aggregate.Min, _valueLogicalTypes![slot.ValueIndex]),
                    SlotKind.Max => aggregate.Count == 0 ? null : ColumnBatchAdapter.RestoreEngineValue(
                        aggregate.Max, _valueLogicalTypes![slot.ValueIndex]),
                    _ => null
                };
            }
            if (_havingClause == null || await _context.EvaluateCondition(_havingClause, row)) table.Rows.Add(row);
        }
        return table;
    }

    public void Dispose()
    {
        _groups.Clear();
        _lease.Dispose();
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
            case LiteralExpression: rewritten = expression; return true;
            case IdentifierExpression identifier when projectedNames.Contains(identifier.Name.Split('.').Last()):
                rewritten = new IdentifierExpression(identifier.Name.Split('.').Last()); return true;
            case FunctionCallExpression function when aggregateAliases.TryGetValue(function.ToSql(), out var alias):
                rewritten = new IdentifierExpression(alias); return true;
            case BinaryExpression binary
                when TryRewriteHaving(binary.Left, aggregateAliases, projectedNames, out var left)
                    && TryRewriteHaving(binary.Right, aggregateAliases, projectedNames, out var right):
                rewritten = new BinaryExpression(left!, binary.Operator, right!); return true;
            case UnaryExpression unary when TryRewriteHaving(unary.Expression, aggregateAliases, projectedNames, out var inner):
                rewritten = new UnaryExpression(unary.Operator, inner!); return true;
            case IsNullExpression isNull when TryRewriteHaving(isNull.Expression, aggregateAliases, projectedNames, out var nullInner):
                rewritten = new IsNullExpression(nullInner!, isNull.Not); return true;
            default: rewritten = null; return false;
        }
    }

    private sealed class GroupState(int valueCount)
    {
        public long RowCount;
        public ValueState[] Values { get; } = new ValueState[valueCount];
    }

    private struct ValueState
    {
        public long Count;
        public decimal Sum;
        public decimal Min;
        public decimal Max;

        public void Add(decimal value)
        {
            if (Count == 0) Min = Max = value;
            else { if (value < Min) Min = value; if (value > Max) Max = value; }
            Count++;
            Sum = checked(Sum + value);
        }
    }

    private sealed record Slot(SlotKind Kind, int KeyIndex, int ValueIndex, bool CountStar);
    private enum SlotKind { Unsupported, Key, Count, Sum, Average, Min, Max }

    private readonly struct CompositeKey : IEquatable<CompositeKey>
    {
        private readonly object? _first;
        private readonly object? _second;
        private readonly object? _third;
        private readonly object?[]? _overflow;
        private readonly int _count;
        private readonly int _hashCode;

        private CompositeKey(object? first, object? second, object? third, object?[]? overflow, int count)
        {
            _first = first;
            _second = second;
            _third = third;
            _overflow = overflow;
            _count = count;
            var hash = new HashCode();
            hash.Add(first);
            if (count > 1) hash.Add(second);
            if (count > 2) hash.Add(third);
            if (overflow != null) foreach (var value in overflow) hash.Add(value);
            _hashCode = hash.ToHashCode();
        }

        public object? this[int index] => index switch
        {
            0 when _count > 0 => _first,
            1 when _count > 1 => _second,
            2 when _count > 2 => _third,
            _ when index >= 3 && index < _count => _overflow![index - 3],
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public static CompositeKey Create(IReadOnlyList<IColumnBuffer> columns, int row, out long keyBytes)
        {
            object? first = null, second = null, third = null;
            object?[]? overflow = columns.Count > 3 ? new object?[columns.Count - 3] : null;
            keyBytes = 0;
            for (var index = 0; index < columns.Count; index++)
            {
                var value = CompoundKey.NormalizeValue(columns[index].GetBoxedValue(row));
                if (value is string text) keyBytes = checked(keyBytes + text.Length * 2L);
                switch (index)
                {
                    case 0: first = value; break;
                    case 1: second = value; break;
                    case 2: third = value; break;
                    default: overflow![index - 3] = value; break;
                }
            }
            return new CompositeKey(first, second, third, overflow, columns.Count);
        }

        public bool Equals(CompositeKey other)
        {
            if (_hashCode != other._hashCode || _count != other._count) return false;
            for (var index = 0; index < _count; index++)
                if (!Equals(this[index], other[index])) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is CompositeKey other && Equals(other);
        public override int GetHashCode() => _hashCode;
    }
}
