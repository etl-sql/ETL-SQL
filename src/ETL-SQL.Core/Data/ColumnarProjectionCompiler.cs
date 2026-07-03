using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Data;

/// <summary>
/// Conservative projection binder for the native scan island. It reads typed column buffers directly
/// and materializes Rows only at the required DataTable result boundary.
/// </summary>
public static class ColumnarProjectionCompiler
{
    public static bool CanProject(ColumnBatch batch, IReadOnlyList<SelectColumn> columns)
        => columns.Count > 0 && columns.All(column => CanProject(batch, column.Expression));

    public static bool CanProjectToSchema(
        ColumnBatch batch,
        IReadOnlyList<SelectColumn> columns,
        IReadOnlyList<ColumnBatchField> outputFields)
    {
        if (columns.Count == 0 || columns.Count != outputFields.Count || !CanProject(batch, columns)) return false;
        for (var index = 0; index < columns.Count; index++)
        {
            var output = outputFields[index];
            if (columns[index].Expression is IdentifierExpression identifier)
            {
                TryGetColumn(batch, identifier, out var ordinal);
                var source = batch.Schema.Fields[ordinal];
                if (source.ElementType != output.ElementType
                    || !source.LogicalType.Equals(output.LogicalType, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else if (output.ElementType != typeof(decimal))
            {
                return false;
            }
        }
        return true;
    }

    public static ColumnBatch ProjectToColumnBatch(
        ColumnBatch batch,
        IReadOnlyList<SelectColumn> columns,
        IReadOnlyList<ColumnBatchField> outputFields,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanProjectToSchema(batch, columns, outputFields))
            throw new NotSupportedException("The native projection is incompatible with the requested output schema.");
        var selectedRows = selection?.Indices ?? default;
        var hasSelection = selection != null;
        var rowCount = selection?.Count ?? batch.RowCount;
        var buffers = new List<IColumnBuffer>(columns.Count);
        try
        {
            for (var index = 0; index < columns.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (columns[index].Expression is IdentifierExpression identifier)
                {
                    TryGetColumn(batch, identifier, out var ordinal);
                    buffers.Add(ColumnBatchAdapter.CopyColumn(
                        batch.Columns[ordinal], batch.RowCount, selectedRows, hasSelection, cancellationToken));
                    continue;
                }

                var output = ColumnBuffer<decimal>.Rent(rowCount);
                try
                {
                    for (var position = 0; position < rowCount; position++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var sourceRow = hasSelection ? selectedRows.Span[position] : position;
                        var value = Evaluate(batch, columns[index].Expression, sourceRow);
                        if (value == null || value == DBNull.Value) output.SetNull(position);
                        else output.Values.Span[position] = Convert.ToDecimal(value);
                    }
                    buffers.Add(output);
                }
                catch
                {
                    output.Dispose();
                    throw;
                }
            }
            return new ColumnBatch(new ColumnBatchSchema(outputFields), buffers, rowCount);
        }
        catch
        {
            foreach (var buffer in buffers) buffer.Dispose();
            throw;
        }
    }

    public static DataTable ProjectToDataTable(
        ColumnBatch batch,
        IReadOnlyList<SelectColumn> columns,
        IReadOnlyList<string> outputNames,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        if (columns.Count == 0 || columns.Count != outputNames.Count)
            throw new ArgumentException("Projection columns and output names must have the same non-zero count.");
        if (!CanProject(batch, columns))
            throw new NotSupportedException("The projection contains an expression that is not supported by native buffers.");

        var table = new DataTable();
        table.SetColumns(outputNames);
        void ProjectRow(int rowIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((uint)rowIndex >= (uint)batch.RowCount)
                throw new ArgumentOutOfRangeException(nameof(selection), "Selection contains an invalid row ordinal.");
            var row = table.NewRow();
            for (var output = 0; output < columns.Count; output++)
                row[output] = Evaluate(batch, columns[output].Expression, rowIndex);
            table.Rows.Add(row);
        }

        if (selection == null)
        {
            for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++) ProjectRow(rowIndex);
        }
        else
        {
            foreach (var rowIndex in selection.Indices.Span) ProjectRow(rowIndex);
        }
        return table;
    }

    private static bool CanProject(ColumnBatch batch, Expression expression)
    {
        if (expression is IdentifierExpression identifier)
            return TryGetColumn(batch, identifier, out _);
        if (expression is not BinaryExpression binary || !IsArithmetic(binary.Operator)) return false;
        if (binary.Left is IdentifierExpression left && binary.Right is LiteralExpression)
            return TryGetNumericColumn(batch, left);
        if (binary.Left is LiteralExpression && binary.Right is IdentifierExpression right)
            return TryGetNumericColumn(batch, right);
        return false;
    }

    private static object? Evaluate(ColumnBatch batch, Expression expression, int rowIndex)
    {
        if (expression is IdentifierExpression identifier)
        {
            TryGetColumn(batch, identifier, out var ordinal);
            var field = batch.Schema.Fields[ordinal];
            return ColumnBatchAdapter.RestoreEngineValue(batch.Columns[ordinal].GetBoxedValue(rowIndex), field.LogicalType);
        }

        var binary = (BinaryExpression)expression;
        var left = binary.Left is IdentifierExpression leftIdentifier
            ? ReadColumn(batch, leftIdentifier, rowIndex)
            : ((LiteralExpression)binary.Left).Value;
        var right = binary.Right is IdentifierExpression rightIdentifier
            ? ReadColumn(batch, rightIdentifier, rowIndex)
            : ((LiteralExpression)binary.Right).Value;
        return BinaryOperatorFactory.Execute(binary.Operator, left, right);
    }

    private static object? ReadColumn(ColumnBatch batch, IdentifierExpression identifier, int rowIndex)
    {
        TryGetColumn(batch, identifier, out var ordinal);
        var field = batch.Schema.Fields[ordinal];
        return ColumnBatchAdapter.RestoreEngineValue(batch.Columns[ordinal].GetBoxedValue(rowIndex), field.LogicalType);
    }

    private static bool TryGetNumericColumn(ColumnBatch batch, IdentifierExpression identifier)
    {
        if (!TryGetColumn(batch, identifier, out var ordinal)) return false;
        var type = batch.Columns[ordinal].ElementType;
        return type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    private static bool TryGetColumn(ColumnBatch batch, IdentifierExpression identifier, out int ordinal)
    {
        try
        {
            ordinal = batch.Schema.GetOrdinal(identifier.Name.Split('.').Last());
            return true;
        }
        catch (KeyNotFoundException)
        {
            ordinal = -1;
            return false;
        }
    }

    private static bool IsArithmetic(TokenType token)
        => token is TokenType.PLUS or TokenType.MINUS or TokenType.STAR or TokenType.SLASH or TokenType.MODULO;
}
