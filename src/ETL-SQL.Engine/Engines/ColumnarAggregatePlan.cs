using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

internal sealed class ColumnarAggregatePlan
{
    private readonly IExecutionContext _context;
    private readonly List<Slot> _slots;

    private ColumnarAggregatePlan(IExecutionContext context, List<Slot> slots)
    {
        _context = context;
        _slots = slots;
    }

    public static bool TryCreate(IExecutionContext context, IReadOnlyList<SelectColumn> columns, out ColumnarAggregatePlan? plan)
    {
        var slots = new List<Slot>(columns.Count);
        foreach (var column in columns)
        {
            if (column.Expression is not FunctionCallExpression function
                || function.IsDistinct || function.Filter != null || function.Window != null
                || function.FunctionName.ToUpperInvariant() is not ("COUNT" or "SUM" or "AVG" or "MIN" or "MAX"))
            {
                plan = null;
                return false;
            }

            var name = function.FunctionName.ToUpperInvariant();
            var countStar = name == "COUNT" && (function.Arguments.Count == 0
                || function.Arguments[0] is StarExpression
                || function.Arguments[0] is IdentifierExpression { Name: "*" });
            string? sourceColumn = null;
            if (!countStar)
            {
                if (function.Arguments.Count != 1 || function.Arguments[0] is not IdentifierExpression identifier)
                {
                    plan = null;
                    return false;
                }
                sourceColumn = identifier.Name.Split('.').Last();
            }
            slots.Add(new Slot(name, sourceColumn, countStar));
        }
        plan = slots.Count == 0 ? null : new ColumnarAggregatePlan(context, slots);
        return plan != null;
    }

    public bool CanApply(ColumnBatch batch)
    {
        foreach (var slot in _slots)
        {
            if (slot.CountStar) continue;
            IColumnBuffer column;
            try { column = batch.GetColumn(slot.SourceColumn!); }
            catch (KeyNotFoundException) { return false; }
            if (slot.Name == "COUNT") continue;
            var numeric = column.ElementType == typeof(byte) || column.ElementType == typeof(short)
                || column.ElementType == typeof(int) || column.ElementType == typeof(long)
                || column.ElementType == typeof(float) || column.ElementType == typeof(double)
                || column.ElementType == typeof(decimal);
            var fixedWidthComparable = numeric || column.ElementType == typeof(DateTime)
                || column.ElementType == typeof(DateTimeOffset) || column.ElementType == typeof(TimeSpan)
                || column.ElementType == typeof(Guid);
            if (slot.Name is "SUM" or "AVG" ? !numeric : !fixedWidthComparable)
                return false;
        }
        return true;
    }

    public void Accumulate(ColumnBatch batch, SelectionVector? selection)
    {
        foreach (var slot in _slots)
        {
            if (slot.CountStar)
            {
                slot.Count += ColumnBatchKernels.Count(batch, selection: selection, cancellationToken: _context.CancellationToken);
                continue;
            }

            var column = batch.GetColumn(slot.SourceColumn!);
            var count = ColumnBatchKernels.Count(
                batch, slot.SourceColumn, selection, _context.CancellationToken);
            if (slot.Name == "COUNT")
            {
                slot.Count += count;
                continue;
            }

            if (slot.Name is "SUM" or "AVG")
            {
                var sum = SumDecimal(batch, slot.SourceColumn!, column.ElementType, selection);
                if (sum.HasValue)
                {
                    slot.Sum = checked(slot.Sum + sum.Value);
                    slot.Count += count;
                }
            }
            if (slot.Name is "MIN" or "MAX")
            {
                var (hasValue, min, max) = MinMax(batch, slot.SourceColumn!, column.ElementType, selection);
                if (!hasValue) continue;
                slot.LogicalType ??= batch.Schema.Fields[batch.Schema.GetOrdinal(slot.SourceColumn!)].LogicalType;
                var candidate = slot.Name == "MIN" ? min : max;
                candidate = ColumnBatchAdapter.RestoreEngineValue(candidate, slot.LogicalType);
                if (!slot.HasExtremum || (slot.Name == "MIN"
                        ? _context.CompareConstants(candidate, slot.Extremum) < 0
                        : _context.CompareConstants(candidate, slot.Extremum) > 0))
                {
                    slot.Extremum = candidate;
                    slot.HasExtremum = true;
                }
            }
        }
    }

    public DataTable FinalizeResult(IReadOnlyList<string> outputNames)
    {
        var table = new DataTable();
        table.SetColumns(outputNames);
        var row = table.NewRow();
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            row[i] = slot.Name switch
            {
                "COUNT" => (decimal)slot.Count,
                "SUM" => slot.Count == 0 ? null : slot.Sum,
                "AVG" => slot.Count == 0 ? null : slot.Sum / slot.Count,
                "MIN" or "MAX" => slot.HasExtremum ? slot.Extremum : null,
                _ => null
            };
        }
        table.Rows.Add(row);
        return table;
    }

    private static decimal? SumDecimal(ColumnBatch batch, string column, Type type, SelectionVector? selection)
    {
        if (type == typeof(byte)) return ColumnBatchKernels.SumDecimal<byte>(batch, column, selection);
        if (type == typeof(short)) return ColumnBatchKernels.SumDecimal<short>(batch, column, selection);
        if (type == typeof(int)) return ColumnBatchKernels.SumDecimal<int>(batch, column, selection);
        if (type == typeof(long)) return ColumnBatchKernels.SumDecimal<long>(batch, column, selection);
        if (type == typeof(float)) return ColumnBatchKernels.SumDecimal<float>(batch, column, selection);
        if (type == typeof(double)) return ColumnBatchKernels.SumDecimal<double>(batch, column, selection);
        if (type == typeof(decimal)) return ColumnBatchKernels.SumDecimal<decimal>(batch, column, selection);
        return null;
    }

    private static (bool HasValue, object? Min, object? Max) MinMax(
        ColumnBatch batch, string column, Type type, SelectionVector? selection)
    {
        if (type == typeof(byte)) return Box(ColumnBatchKernels.MinMax<byte>(batch, column, selection));
        if (type == typeof(short)) return Box(ColumnBatchKernels.MinMax<short>(batch, column, selection));
        if (type == typeof(int)) return Box(ColumnBatchKernels.MinMax<int>(batch, column, selection));
        if (type == typeof(long)) return Box(ColumnBatchKernels.MinMax<long>(batch, column, selection));
        if (type == typeof(float)) return Box(ColumnBatchKernels.MinMax<float>(batch, column, selection));
        if (type == typeof(double)) return Box(ColumnBatchKernels.MinMax<double>(batch, column, selection));
        if (type == typeof(decimal)) return Box(ColumnBatchKernels.MinMax<decimal>(batch, column, selection));
        if (type == typeof(DateTime)) return Box(ColumnBatchKernels.MinMax<DateTime>(batch, column, selection));
        if (type == typeof(DateTimeOffset)) return Box(ColumnBatchKernels.MinMax<DateTimeOffset>(batch, column, selection));
        if (type == typeof(TimeSpan)) return Box(ColumnBatchKernels.MinMax<TimeSpan>(batch, column, selection));
        if (type == typeof(Guid)) return Box(ColumnBatchKernels.MinMax<Guid>(batch, column, selection));
        return (false, null, null);
    }

    private static (bool, object?, object?) Box<T>(NativeMinMax<T> range)
        => (range.HasValue, range.Min, range.Max);

    private sealed class Slot(string name, string? sourceColumn, bool countStar)
    {
        public string Name { get; } = name;
        public string? SourceColumn { get; } = sourceColumn;
        public bool CountStar { get; } = countStar;
        public long Count { get; set; }
        public decimal Sum { get; set; }
        public bool HasExtremum { get; set; }
        public object? Extremum { get; set; }
        public string? LogicalType { get; set; }
    }
}
