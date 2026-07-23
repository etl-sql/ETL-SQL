using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles FILL_DATES(...) INTO #target by materializing missing daily rows per optional group.
/// </summary>
public class FillDatesStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;

    public Type SupportedStatementType => typeof(FillDatesStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (FillDatesStatement)statement;
        var source = await context.ResolveDataSourceAsync(stmt.SourceTable)
            ?? throw new ExecutionException($"Could not resolve source dataset: {stmt.SourceTable.TableName}");

        var rows = new List<Row>();
        var columns = new List<string>();
        long retainedBytes = 0;
        long operatorBudget = context.OperatorMemoryGrantMB > 0
            ? (long)context.OperatorMemoryGrantMB * 1024L * 1024L
            : 0L;

        using var lease = context.MemoryArbiter.AcquireLease();
        await foreach (var batch in source.ReadBatches(context.EffectiveBatchSize, context.CancellationToken))
        {
            if (columns.Count == 0)
            {
                columns.AddRange(batch.ColumnNames);
                ValidateColumns(stmt, columns);
            }

            foreach (var row in batch.Rows)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var clone = row.Clone();
                retainedBytes = checked(retainedBytes + clone.EstimateHeapBytes());
                if ((operatorBudget > 0 && retainedBytes > operatorBudget) || lease.RegisterAndCheckSpill(retainedBytes))
                {
                    throw new ExecutionException(
                        "FILL_DATES exceeded its bounded memory grant while staging the source series. " +
                        "Increase Engine:OperatorMemoryGrantMB or reduce the date/group scope.");
                }
                rows.Add(clone);
            }
        }

        if (columns.Count == 0)
        {
            throw new ExecutionException($"Source dataset {stmt.SourceTable.TableName} has no columns.");
        }

        if (stmt.TargetTable.TableName.StartsWith("#") && !context.Connections.ContainsKey(stmt.TargetTable.TableName))
        {
            context.Connections[stmt.TargetTable.TableName] = new InMemoryDataSource
            {
                Validator = context as IDataValidator,
                ExecutionContext = context,
                MaxInMemoryBatches = context.MaxInMemoryBatches
            };
        }

        var target = await context.ResolveDataSourceAsync(stmt.TargetTable)
            ?? throw new ExecutionException($"Could not resolve target dataset: {stmt.TargetTable.TableName}");
        await target.TruncateAsync();

        var gapFillValue = await context.EvaluationContext.EvaluateValue(stmt.GapFillValue, Row.Empty);
        var output = new DataTable();
        output.SetColumns(columns);

        int producedRows = 0;
        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, stmt.GroupColumns)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var byDate = new Dictionary<DateTime, Row>();
            foreach (var row in group)
            {
                if (!TryGetDate(row[stmt.DateColumn], out var date))
                {
                    throw new ExecutionException($"FILL_DATES could not convert {stmt.DateColumn} value '{row[stmt.DateColumn]}' to a date.");
                }
                byDate[date.Date] = row;
            }

            if (byDate.Count == 0) continue;
            var minDate = byDate.Keys.Min();
            var maxDate = byDate.Keys.Max();
            var template = group.First();

            for (var date = minDate; date <= maxDate; date = date.AddDays(1))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var outRow = byDate.TryGetValue(date, out var existing)
                    ? existing.Clone()
                    : CreateGapRow(output.Schema, columns, stmt, template, date, gapFillValue);

                await output.AddRowAsync(outRow);
                producedRows++;

                if (output.Rows.Count >= context.EffectiveBatchSize)
                {
                    await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                    output = new DataTable();
                    output.SetColumns(columns);
                }
            }
        }

        if (output.Rows.Count > 0)
        {
            await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
        }

        context.Telemetry.RowsProcessed += producedRows;
        _logger.Info("FILL_DATES complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private static void ValidateColumns(FillDatesStatement stmt, IReadOnlyCollection<string> columns)
    {
        if (!columns.Contains(stmt.DateColumn, StringComparer.OrdinalIgnoreCase))
        {
            throw new ExecutionException($"FILL_DATES DATE_COL '{stmt.DateColumn}' was not found in {stmt.SourceTable.TableName}.");
        }

        foreach (var groupColumn in stmt.GroupColumns)
        {
            if (!columns.Contains(groupColumn, StringComparer.OrdinalIgnoreCase))
            {
                throw new ExecutionException($"FILL_DATES BY_GROUP column '{groupColumn}' was not found in {stmt.SourceTable.TableName}.");
            }
        }
    }

    private static string BuildGroupKey(Row row, IReadOnlyList<string> groupColumns)
    {
        if (groupColumns.Count == 0) return string.Empty;
        return string.Join('\u001f', groupColumns.Select(c => row[c]?.ToString() ?? "\0"));
    }

    private static Row CreateGapRow(
        TableSchema schema,
        IReadOnlyList<string> columns,
        FillDatesStatement stmt,
        Row template,
        DateTime date,
        object? gapFillValue)
    {
        var row = new Row(schema);
        foreach (var column in columns)
        {
            if (column.Equals(stmt.DateColumn, StringComparison.OrdinalIgnoreCase))
            {
                row[column] = date;
            }
            else if (stmt.GroupColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                row[column] = template[column];
            }
            else
            {
                row[column] = gapFillValue;
            }
        }
        return row;
    }

    private static bool TryGetDate(object? value, out DateTime date)
    {
        if (value is DateTime dt)
        {
            date = dt.Date;
            return true;
        }

        return DateTime.TryParse(value?.ToString(), out date);
    }
}
