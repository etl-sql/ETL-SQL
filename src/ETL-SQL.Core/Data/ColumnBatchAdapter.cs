using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Data;

/// <summary>
/// Compatibility boundary between boxed row batches and native column batches. Column-capable
/// operators should exchange <see cref="ColumnBatch"/> directly and avoid this adapter internally.
/// </summary>
public static class ColumnBatchAdapter
{
    /// <summary>
    /// Creates an independently owned native batch containing the selected rows and columns.
    /// Values stay in typed buffers; no <see cref="Row"/> or <see cref="DataTable"/> is created.
    /// </summary>
    public static ColumnBatch Compact(
        ColumnBatch batch,
        IReadOnlyList<string> columns,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? outputColumns = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0) throw new ArgumentException("At least one column is required.", nameof(columns));
        if (outputColumns != null && outputColumns.Count != columns.Count)
            throw new ArgumentException("Output column count must match projected column count.", nameof(outputColumns));

        var ordinals = columns.Select(batch.Schema.GetOrdinal).ToArray();
        if (ordinals.Distinct().Count() != ordinals.Length)
            throw new ArgumentException("A compacted batch cannot contain duplicate columns.", nameof(columns));
        var selectedRows = selection?.Indices ?? default;
        var rowCount = selection?.Count ?? batch.RowCount;
        var output = new List<IColumnBuffer>(ordinals.Length);
        try
        {
            foreach (var ordinal in ordinals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Add(CopyColumn(batch.Columns[ordinal], batch.RowCount, selectedRows, selection != null, cancellationToken));
            }
            var schema = new ColumnBatchSchema(ordinals.Select((ordinal, index) =>
            {
                var field = batch.Schema.Fields[ordinal];
                return outputColumns == null ? field : field with { Name = outputColumns[index] };
            }));
            return new ColumnBatch(schema, output, rowCount);
        }
        catch
        {
            foreach (var column in output) column.Dispose();
            throw;
        }
    }

    public static Type GetPhysicalType(string logicalType) => BaseType(logicalType) switch
    {
        "TINYINT" => typeof(byte),
        "SMALLINT" => typeof(short),
        "INT" or "INTEGER" => typeof(int),
        "BIGINT" => typeof(long),
        "FLOAT" or "DOUBLE" or "REAL" => typeof(double),
        "DECIMAL" or "NUMERIC" or "MONEY" => typeof(decimal),
        "BIT" or "BOOL" or "BOOLEAN" => typeof(byte),
        "DATE" or "DATETIME" or "TIMESTAMP" => typeof(DateTime),
        "TIME" => typeof(TimeSpan),
        "GUID" or "UUID" or "UNIQUEIDENTIFIER" => typeof(Guid),
        "STRING" or "VARCHAR" or "NVARCHAR" or "TEXT" or "NTEXT" or "CHAR" or "NCHAR" or
            "VARCHAR2" or "JSON" or "XML" or "PATH" => typeof(string),
        _ => throw new NotSupportedException($"Logical type '{logicalType}' does not yet have a native column buffer.")
    };

    public static ColumnBatch FromDataTable(
        DataTable table,
        IReadOnlyDictionary<string, ColumnDefinition>? logicalSchema = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Schema.ColumnCount == 0)
            throw new ArgumentException("Cannot create a column batch from a table with no columns.", nameof(table));

        var fields = new List<ColumnBatchField>(table.Schema.ColumnCount);
        var columns = new List<IColumnBuffer>(table.Schema.ColumnCount);
        try
        {
            for (var ordinal = 0; ordinal < table.Schema.ColumnCount; ordinal++)
            {
                var name = table.Schema.GetName(ordinal);
                var logicalType = logicalSchema != null && logicalSchema.TryGetValue(name, out var definition)
                    ? definition.DataType
                    : InferLogicalType(table.Rows, ordinal);
                var column = BuildColumn(table.Rows, ordinal, logicalType, out var physicalType);
                fields.Add(new ColumnBatchField(name, physicalType, logicalType, IsNullable(table.Rows, ordinal)));
                columns.Add(column);
            }

            return new ColumnBatch(new ColumnBatchSchema(fields), columns, table.Rows.Count);
        }
        catch
        {
            foreach (var column in columns) column.Dispose();
            throw;
        }
    }

    public static DataTable ToDataTable(ColumnBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var table = new DataTable();
        table.SetColumns(batch.Schema.Fields.Select(field => field.Name));

        for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
        {
            var row = table.NewRow();
            for (var columnIndex = 0; columnIndex < batch.Schema.Count; columnIndex++)
            {
                var field = batch.Schema.Fields[columnIndex];
                var value = batch.Columns[columnIndex].GetBoxedValue(rowIndex);
                row[columnIndex] = RestoreEngineValue(value, field.LogicalType);
            }
            table.Rows.Add(row);
        }
        return table;
    }

    public static DataTable ToDataTable(
        ColumnBatch batch,
        IReadOnlyList<string> sourceColumns,
        IReadOnlyList<string> outputColumns,
        SelectionVector? selection = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(sourceColumns);
        ArgumentNullException.ThrowIfNull(outputColumns);
        if (sourceColumns.Count == 0 || sourceColumns.Count != outputColumns.Count)
            throw new ArgumentException("Projected source and output columns must have the same non-zero count.");

        var sourceOrdinals = sourceColumns.Select(batch.Schema.GetOrdinal).ToArray();
        var table = new DataTable();
        table.SetColumns(outputColumns);
        void MaterializeRow(int rowIndex)
        {
            if ((uint)rowIndex >= (uint)batch.RowCount)
                throw new ArgumentOutOfRangeException(nameof(selection), "Selection vector contains an invalid row ordinal.");
            var row = table.NewRow();
            for (var outputOrdinal = 0; outputOrdinal < sourceOrdinals.Length; outputOrdinal++)
            {
                var sourceOrdinal = sourceOrdinals[outputOrdinal];
                var field = batch.Schema.Fields[sourceOrdinal];
                row[outputOrdinal] = RestoreEngineValue(
                    batch.Columns[sourceOrdinal].GetBoxedValue(rowIndex), field.LogicalType);
            }
            table.Rows.Add(row);
        }
        if (selection == null)
        {
            for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++) MaterializeRow(rowIndex);
        }
        else
        {
            foreach (var rowIndex in selection.Indices.Span) MaterializeRow(rowIndex);
        }
        return table;
    }

    private static IColumnBuffer BuildColumn(
        IReadOnlyList<Row> rows,
        int ordinal,
        string logicalType,
        out Type physicalType)
    {
        physicalType = GetPhysicalType(logicalType);
        switch (BaseType(logicalType))
        {
            case "TINYINT":
                return BuildFixed(rows, ordinal, Convert.ToByte);
            case "SMALLINT":
                return BuildFixed(rows, ordinal, Convert.ToInt16);
            case "INT":
            case "INTEGER":
                return BuildFixed(rows, ordinal, Convert.ToInt32);
            case "BIGINT":
                return BuildFixed(rows, ordinal, Convert.ToInt64);
            case "FLOAT":
            case "DOUBLE":
            case "REAL":
                return BuildFixed(rows, ordinal, Convert.ToDouble);
            case "DECIMAL":
            case "NUMERIC":
            case "MONEY":
                return BuildFixed(rows, ordinal, Convert.ToDecimal);
            case "BIT":
            case "BOOL":
            case "BOOLEAN":
                return BuildFixed(rows, ordinal, value => Convert.ToBoolean(value) ? (byte)1 : (byte)0);
            case "DATE":
            case "DATETIME":
            case "TIMESTAMP":
                return BuildFixed(rows, ordinal, value => value is DateTime date ? date : Convert.ToDateTime(value));
            case "TIME":
                return BuildFixed(rows, ordinal, value => value is TimeSpan time ? time : TimeSpan.Parse(value.ToString()!));
            case "GUID":
            case "UUID":
            case "UNIQUEIDENTIFIER":
                return BuildFixed(rows, ordinal, value => value is Guid guid ? guid : Guid.Parse(value.ToString()!));
            case "STRING":
            case "VARCHAR":
            case "NVARCHAR":
            case "TEXT":
            case "NTEXT":
            case "CHAR":
            case "NCHAR":
            case "VARCHAR2":
            case "JSON":
            case "XML":
            case "PATH":
                return Utf8ColumnBuffer.FromStrings(rows.Select(row =>
                {
                    var value = row[ordinal];
                    return value == null || value == DBNull.Value ? null : value.ToString();
                }).ToArray());
            default:
                throw new NotSupportedException($"Logical type '{logicalType}' does not yet have a native column buffer.");
        }
    }

    private static IColumnBuffer CopyColumn(
        IColumnBuffer source,
        int sourceRowCount,
        ReadOnlyMemory<int> selectedRows,
        bool hasSelection,
        CancellationToken cancellationToken)
    {
        if (source is Utf8ColumnBuffer utf8)
        {
            return hasSelection
                ? utf8.Compact(selectedRows.Span, sourceRowCount, cancellationToken)
                : utf8.Clone(cancellationToken);
        }

        if (source is ColumnBuffer<byte> bytes) return CopyFixed(bytes, sourceRowCount, selectedRows, hasSelection, cancellationToken);
        if (source is ColumnBuffer<short> shorts) return CopyFixed(shorts, sourceRowCount, selectedRows, hasSelection, cancellationToken);
        if (source is ColumnBuffer<int> ints) return CopyFixed(ints, sourceRowCount, selectedRows, hasSelection, cancellationToken);
        if (source is ColumnBuffer<long> longs) return CopyFixed(longs, sourceRowCount, selectedRows, hasSelection, cancellationToken);
        if (source is ColumnBuffer<double> doubles) return CopyFixed(doubles, sourceRowCount, selectedRows, hasSelection, cancellationToken);
        if (source is ColumnBuffer<decimal> decimals) return CopyFixed(decimals, sourceRowCount, selectedRows, hasSelection, cancellationToken);
        if (source is ColumnBuffer<DateTime> dates) return CopyFixed(dates, sourceRowCount, selectedRows, hasSelection, cancellationToken);
        if (source is ColumnBuffer<TimeSpan> times) return CopyFixed(times, sourceRowCount, selectedRows, hasSelection, cancellationToken);
        if (source is ColumnBuffer<Guid> guids) return CopyFixed(guids, sourceRowCount, selectedRows, hasSelection, cancellationToken);
        throw new NotSupportedException($"Physical type '{source.ElementType.Name}' cannot be compacted.");
    }

    private static ColumnBuffer<T> CopyFixed<T>(
        ColumnBuffer<T> source,
        int sourceRowCount,
        ReadOnlyMemory<int> selectedRows,
        bool hasSelection,
        CancellationToken cancellationToken) where T : unmanaged
    {
        var count = hasSelection ? selectedRows.Length : sourceRowCount;
        var result = ColumnBuffer<T>.Rent(count);
        try
        {
            var sourceValues = source.Values.Span;
            var outputValues = result.Values.Span;
            for (var output = 0; output < count; output++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = hasSelection ? ValidateSelectedRow(selectedRows.Span[output], sourceRowCount) : output;
                if (source.IsNull(input)) result.SetNull(output);
                else outputValues[output] = sourceValues[input];
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static int ValidateSelectedRow(int row, int rowCount)
        => (uint)row < (uint)rowCount
            ? row
            : throw new ArgumentOutOfRangeException(nameof(row), "Selection vector contains an invalid row ordinal.");

    private static ColumnBuffer<T> BuildFixed<T>(
        IReadOnlyList<Row> rows,
        int ordinal,
        Func<object, T> convert) where T : unmanaged
    {
        var buffer = ColumnBuffer<T>.Rent(rows.Count);
        try
        {
            var values = buffer.Values.Span;
            for (var i = 0; i < rows.Count; i++)
            {
                var value = rows[i][ordinal];
                if (value == null || value == DBNull.Value)
                {
                    buffer.SetNull(i);
                    continue;
                }
                values[i] = convert(value);
            }
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    public static object? RestoreEngineValue(object? value, string logicalType)
    {
        if (value == null) return null;
        return BaseType(logicalType) switch
        {
            "BIT" or "BOOL" or "BOOLEAN" => Convert.ToByte(value) != 0,
            _ => TypeConverter.Cast(value, logicalType)
        };
    }

    private static string InferLogicalType(IReadOnlyList<Row> rows, int ordinal)
    {
        var value = rows.Select(row => row[ordinal]).FirstOrDefault(candidate => candidate != null && candidate != DBNull.Value);
        return value switch
        {
            byte => "TINYINT",
            short => "SMALLINT",
            int => "INT",
            long => "BIGINT",
            float or double => "DOUBLE",
            decimal => "DECIMAL",
            bool => "BOOLEAN",
            DateTime => "DATETIME",
            TimeSpan => "TIME",
            Guid => "UUID",
            string => "VARCHAR",
            null => "VARCHAR",
            _ => throw new NotSupportedException($"CLR type '{value.GetType().Name}' does not yet have a native column buffer.")
        };
    }

    private static bool IsNullable(IReadOnlyList<Row> rows, int ordinal)
        => rows.Any(row => row[ordinal] == null || row[ordinal] == DBNull.Value);

    private static string BaseType(string logicalType)
        => logicalType.Split('(', 2)[0].Trim().ToUpperInvariant();
}
