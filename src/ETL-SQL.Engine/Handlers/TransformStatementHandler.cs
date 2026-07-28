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
/// Handles TRANSFORM statement and its supported algorithms (e.g. FILL_DATES, INTERPOLATE, DEDUPLICATE, PIVOT, TOP_N_OTHERS, PERIOD_COMPARISON, SHARE_OF_TOTAL, NORMALIZE).
/// </summary>
public class TransformStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;

    public Type SupportedStatementType => typeof(TransformStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (TransformStatement)statement;

        if (string.Equals(stmt.Algorithm, "FILL_DATES", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteFillDates(stmt, context);
        }
        else if (string.Equals(stmt.Algorithm, "INTERPOLATE", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteInterpolate(stmt, context);
        }
        else if (string.Equals(stmt.Algorithm, "DEDUPLICATE", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteDeduplicate(stmt, context);
        }
        else if (string.Equals(stmt.Algorithm, "PIVOT", StringComparison.OrdinalIgnoreCase))
        {
            await ExecutePivot(stmt, context);
        }
        else if (string.Equals(stmt.Algorithm, "TOP_N_OTHERS", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteTopNOthers(stmt, context);
        }
        else if (string.Equals(stmt.Algorithm, "PERIOD_COMPARISON", StringComparison.OrdinalIgnoreCase))
        {
            await ExecutePeriodComparison(stmt, context);
        }
        else if (string.Equals(stmt.Algorithm, "SHARE_OF_TOTAL", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteShareOfTotal(stmt, context);
        }
        else if (string.Equals(stmt.Algorithm, "NORMALIZE", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteNormalize(stmt, context);
        }
        else if (string.Equals(stmt.Algorithm, "ROLLING_AGGREGATE", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteRollingAggregate(stmt, context);
        }
        else
        {
            throw new ExecutionException($"Unsupported table transformation algorithm: {stmt.Algorithm}");
        }
    }

    private static async Task<(List<Row> Rows, List<string> Columns)> StageSourceRows(
        TransformStatement stmt,
        IExecutionContext context,
        string algorithmName)
    {
        if (stmt.SourceTable == null)
        {
            throw new ExecutionException($"{algorithmName} algorithm requires a source table (FROM clause)");
        }

        var source = await context.ResolveDataSourceAsync(stmt.SourceTable)
            ?? throw new ExecutionException($"Could not resolve source dataset: {stmt.SourceTable.TableName}");

        var rows = new List<Row>();
        var columns = (await source.GetColumnsAsync(context.CancellationToken)).ToList();
        long retainedBytes = 0;
        long operatorBudget = context.OperatorMemoryGrantMB > 0
            ? (long)context.OperatorMemoryGrantMB * 1024L * 1024L
            : 0L;

        using var lease = context.MemoryArbiter.AcquireLease();
        await foreach (var batch in source.ReadBatches(context.EffectiveBatchSize, context.CancellationToken))
        {
            foreach (var row in batch.Rows)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var clone = row.Clone();
                retainedBytes = checked(retainedBytes + clone.EstimateHeapBytes());
                if ((operatorBudget > 0 && retainedBytes > operatorBudget) || lease.RegisterAndCheckSpill(retainedBytes))
                {
                    throw new ExecutionException(
                        $"{algorithmName} exceeded its bounded memory grant while staging the source series. " +
                        "Increase Engine:OperatorMemoryGrantMB or reduce the data scope.");
                }
                rows.Add(clone);
            }
        }

        if (columns.Count == 0)
        {
            throw new ExecutionException($"Source dataset {stmt.SourceTable.TableName} has no columns.");
        }

        return (rows, columns);
    }

    private async Task ExecuteFillDates(TransformStatement stmt, IExecutionContext context)
    {
        var dateColumn = GetStringOption(stmt.Options, "DATE_COL", "FILL_DATES");
        var gapFillValueExpr = stmt.Options.TryGetValue("GAPS_FILL", out var grp) ? grp : new LiteralExpression(0m, TokenType.NUMERIC);
        var groupColumns = stmt.Options.TryGetValue("BY_GROUP", out var grpCol) ? GetGroupColumns(grpCol) : new List<string>();

        var (rows, columns) = await StageSourceRows(stmt, context, "FILL_DATES");
        ValidateColumns(stmt.SourceTable!.TableName, columns, dateColumn, groupColumns);

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

        var gapFillValue = await context.EvaluationContext.EvaluateValue(gapFillValueExpr, Row.Empty);
        var output = new DataTable();
        output.SetColumns(columns);

        int producedRows = 0;
        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var byDate = new Dictionary<DateTime, Row>();
            foreach (var row in group)
            {
                if (!TryGetDate(row[dateColumn], out var date))
                {
                    throw new ExecutionException($"FILL_DATES could not convert {dateColumn} value '{row[dateColumn]}' to a date.");
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
                    : CreateGapRow(output.Schema, columns, dateColumn, groupColumns, template, date, gapFillValue);

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
        _logger.Info("TRANSFORM FILL_DATES complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private async Task ExecuteInterpolate(TransformStatement stmt, IExecutionContext context)
    {
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "INTERPOLATE");
        var orderCol = GetStringOption(stmt.Options, "ORDER_COL", "INTERPOLATE");
        var method = stmt.Options.TryGetValue("METHOD", out var mExpr) ? GetStringOption(stmt.Options, "METHOD", "INTERPOLATE") : "LINEAR";
        var groupColumns = stmt.Options.TryGetValue("BY_GROUP", out var grpCol) ? GetGroupColumns(grpCol) : new List<string>();

        var (rows, columns) = await StageSourceRows(stmt, context, "INTERPOLATE");

        if (!columns.Contains(valueCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"INTERPOLATE VALUE_COL '{valueCol}' was not found in source dataset.");
        if (!columns.Contains(orderCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"INTERPOLATE ORDER_COL '{orderCol}' was not found in source dataset.");
        foreach (var groupColumn in groupColumns)
        {
            if (!columns.Contains(groupColumn, StringComparer.OrdinalIgnoreCase))
                throw new ExecutionException($"INTERPOLATE BY_GROUP column '{groupColumn}' was not found in source dataset.");
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

        var output = new DataTable();
        output.SetColumns(columns);

        int producedRows = 0;
        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var sortedGroup = group.OrderBy(row => GetNumericOrderValue(row[orderCol])).ToList();
            int n = sortedGroup.Count;

            if (string.Equals(method, "FORWARD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(method, "FORWARD_FILL", StringComparison.OrdinalIgnoreCase))
            {
                object? lastVal = null;
                for (int i = 0; i < n; i++)
                {
                    var val = GetValue(sortedGroup[i][valueCol]);
                    if (val != null)
                    {
                        lastVal = val;
                    }
                    else if (lastVal != null)
                    {
                        sortedGroup[i][valueCol] = lastVal;
                    }
                }
            }
            else if (string.Equals(method, "BACKWARD", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(method, "BACKWARD_FILL", StringComparison.OrdinalIgnoreCase))
            {
                object? nextVal = null;
                for (int i = n - 1; i >= 0; i--)
                {
                    var val = GetValue(sortedGroup[i][valueCol]);
                    if (val != null)
                    {
                        nextVal = val;
                    }
                    else if (nextVal != null)
                    {
                        sortedGroup[i][valueCol] = nextVal;
                    }
                }
            }
            else if (string.Equals(method, "LINEAR", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < n; i++)
                {
                    if (GetValue(sortedGroup[i][valueCol]) == null)
                    {
                        int prevIdx = -1;
                        for (int p = i - 1; p >= 0; p--)
                        {
                            if (GetValue(sortedGroup[p][valueCol]) != null)
                            {
                                prevIdx = p;
                                break;
                            }
                        }

                        int nextIdx = -1;
                        for (int nx = i + 1; nx < n; nx++)
                        {
                            if (GetValue(sortedGroup[nx][valueCol]) != null)
                            {
                                nextIdx = nx;
                                break;
                            }
                        }

                        if (prevIdx != -1 && nextIdx != -1)
                        {
                            var y0 = Convert.ToDecimal(GetValue(sortedGroup[prevIdx][valueCol]));
                            var y1 = Convert.ToDecimal(GetValue(sortedGroup[nextIdx][valueCol]));
                            var x0 = GetNumericOrderValue(sortedGroup[prevIdx][orderCol]);
                            var x1 = GetNumericOrderValue(sortedGroup[nextIdx][orderCol]);
                            var x = GetNumericOrderValue(sortedGroup[i][orderCol]);

                            if (x1 == x0)
                            {
                                sortedGroup[i][valueCol] = y0;
                            }
                            else
                            {
                                var y = y0 + (y1 - y0) * (x - x0) / (x1 - x0);
                                sortedGroup[i][valueCol] = y;
                            }
                        }
                    }
                }
            }
            else
            {
                throw new ExecutionException($"Unsupported INTERPOLATE method: {method}");
            }

            foreach (var row in sortedGroup)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await output.AddRowAsync(row);
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
        _logger.Info("TRANSFORM INTERPOLATE complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private async Task ExecuteDeduplicate(TransformStatement stmt, IExecutionContext context)
    {
        var keyCols = GetGroupColumns(stmt.Options.TryGetValue("KEY_COLS", out var kExpr) ? kExpr : throw new ExecutionException("DEDUPLICATE algorithm requires KEY_COLS parameter"));
        var orderByStr = stmt.Options.TryGetValue("ORDER_BY", out var oExpr) ? GetStringOption(stmt.Options, "ORDER_BY", "DEDUPLICATE") : null;
        var keep = stmt.Options.TryGetValue("KEEP", out var kpExpr) ? GetStringOption(stmt.Options, "KEEP", "DEDUPLICATE") : "FIRST";

        var (rows, columns) = await StageSourceRows(stmt, context, "DEDUPLICATE");

        foreach (var keyCol in keyCols)
        {
            if (!columns.Contains(keyCol, StringComparer.OrdinalIgnoreCase))
                throw new ExecutionException($"DEDUPLICATE KEY_COLS column '{keyCol}' was not found in source dataset.");
        }

        List<SortKey>? sortKeys = null;
        if (!string.IsNullOrWhiteSpace(orderByStr))
        {
            sortKeys = ParseOrderBy(orderByStr);
            foreach (var sk in sortKeys)
            {
                if (!columns.Contains(sk.ColumnName, StringComparer.OrdinalIgnoreCase))
                    throw new ExecutionException($"DEDUPLICATE ORDER_BY column '{sk.ColumnName}' was not found in source dataset.");
            }
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

        var output = new DataTable();
        output.SetColumns(columns);

        int producedRows = 0;
        var groups = rows.GroupBy(row => BuildGroupKey(row, keyCols));

        foreach (var group in groups)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var groupRows = group.ToList();
            if (groupRows.Count == 0) continue;

            if (sortKeys != null && sortKeys.Count > 0)
            {
                groupRows.Sort(new RowComparer(sortKeys));
            }

            Row keptRow;
            if (string.Equals(keep, "LAST", StringComparison.OrdinalIgnoreCase))
            {
                keptRow = groupRows[^1];
            }
            else if (string.Equals(keep, "FIRST", StringComparison.OrdinalIgnoreCase))
            {
                keptRow = groupRows[0];
            }
            else
            {
                throw new ExecutionException($"Unsupported DEDUPLICATE KEEP option: {keep}");
            }

            await output.AddRowAsync(keptRow);
            producedRows++;

            if (output.Rows.Count >= context.EffectiveBatchSize)
            {
                await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                output = new DataTable();
                output.SetColumns(columns);
            }
        }

        if (output.Rows.Count > 0)
        {
            await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
        }

        context.Telemetry.RowsProcessed += producedRows;
        _logger.Info("TRANSFORM DEDUPLICATE complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private async Task ExecutePivot(TransformStatement stmt, IExecutionContext context)
    {
        var rowFields = GetGroupColumns(stmt.Options.TryGetValue("ROW_FIELDS", out var rfExpr) ? rfExpr : throw new ExecutionException("PIVOT algorithm requires ROW_FIELDS parameter"));
        var pivotField = GetStringOption(stmt.Options, "PIVOT_FIELD", "PIVOT");
        var valueField = GetStringOption(stmt.Options, "VALUE_FIELD", "PIVOT");
        var aggregate = stmt.Options.TryGetValue("AGGREGATE", out var aggExpr) ? GetStringOption(stmt.Options, "AGGREGATE", "PIVOT") : "SUM";

        var (rows, columns) = await StageSourceRows(stmt, context, "PIVOT");

        foreach (var rf in rowFields)
        {
            if (!columns.Contains(rf, StringComparer.OrdinalIgnoreCase))
                throw new ExecutionException($"PIVOT ROW_FIELDS column '{rf}' was not found in source dataset.");
        }
        if (!columns.Contains(pivotField, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"PIVOT PIVOT_FIELD '{pivotField}' was not found in source dataset.");
        if (!columns.Contains(valueField, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"PIVOT VALUE_FIELD '{valueField}' was not found in source dataset.");

        var pivotValues = rows
            .Select(r => GetValue(r[pivotField])?.ToString() ?? "NULL")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        var targetColumns = new List<string>(rowFields);
        foreach (var pv in pivotValues)
        {
            if (!targetColumns.Contains(pv, StringComparer.OrdinalIgnoreCase))
            {
                targetColumns.Add(pv);
            }
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

        var output = new DataTable();
        output.SetColumns(targetColumns);

        int producedRows = 0;
        var groups = rows.GroupBy(row => BuildGroupKey(row, rowFields));

        foreach (var group in groups)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var groupRows = group.ToList();
            var firstRow = groupRows[0];

            var outRow = new Row(output.Schema);
            foreach (var rf in rowFields)
            {
                outRow[rf] = firstRow[rf];
            }

            foreach (var pv in pivotValues)
            {
                var matchingRows = groupRows
                    .Where(r => string.Equals(GetValue(r[pivotField])?.ToString() ?? "NULL", pv, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r[valueField])
                    .ToList();

                outRow[pv] = AggregateValues(matchingRows, aggregate);
            }

            await output.AddRowAsync(outRow);
            producedRows++;

            if (output.Rows.Count >= context.EffectiveBatchSize)
            {
                await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                output = new DataTable();
                output.SetColumns(targetColumns);
            }
        }

        if (output.Rows.Count > 0)
        {
            await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
        }

        context.Telemetry.RowsProcessed += producedRows;
        _logger.Info("TRANSFORM PIVOT complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private async Task ExecuteTopNOthers(TransformStatement stmt, IExecutionContext context)
    {
        var n = stmt.Options.TryGetValue("N", out var nExpr) ? Convert.ToInt32(await context.EvaluationContext.EvaluateValue(nExpr, Row.Empty)) : 5;
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "TOP_N_OTHERS");
        var categoryCol = GetStringOption(stmt.Options, "CATEGORY_COL", "TOP_N_OTHERS");
        var othersLabel = stmt.Options.TryGetValue("OTHERS_LABEL", out var oExpr) ? GetStringOption(stmt.Options, "OTHERS_LABEL", "TOP_N_OTHERS") : "Others";
        var aggregate = stmt.Options.TryGetValue("AGGREGATE", out var aggExpr) ? GetStringOption(stmt.Options, "AGGREGATE", "TOP_N_OTHERS") : "SUM";
        var groupColumns = stmt.Options.TryGetValue("BY_GROUP", out var grpCol) ? GetGroupColumns(grpCol) : new List<string>();

        var (rows, columns) = await StageSourceRows(stmt, context, "TOP_N_OTHERS");

        if (!columns.Contains(valueCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"TOP_N_OTHERS VALUE_COL '{valueCol}' was not found in source dataset.");
        if (!columns.Contains(categoryCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"TOP_N_OTHERS CATEGORY_COL '{categoryCol}' was not found in source dataset.");

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

        var output = new DataTable();
        output.SetColumns(columns);

        int producedRows = 0;
        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var groupRows = group.ToList();
            if (groupRows.Count == 0) continue;

            var categoryAggs = groupRows
                .GroupBy(r => GetValue(r[categoryCol])?.ToString() ?? "NULL", StringComparer.OrdinalIgnoreCase)
                .Select(cg => new
                {
                    Category = cg.Key,
                    Rows = cg.ToList(),
                    AggValue = Convert.ToDecimal(AggregateValues(cg.Select(r => r[valueCol]).ToList(), aggregate))
                })
                .OrderByDescending(c => c.AggValue)
                .ToList();

            var topCategories = categoryAggs.Take(n).Select(c => c.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var topRows = groupRows.Where(r => topCategories.Contains(GetValue(r[categoryCol])?.ToString() ?? "NULL")).ToList();
            foreach (var r in topRows)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await output.AddRowAsync(r.Clone());
                producedRows++;
            }

            var otherRows = groupRows.Where(r => !topCategories.Contains(GetValue(r[categoryCol])?.ToString() ?? "NULL")).ToList();
            if (otherRows.Count > 0)
            {
                var othersRow = new Row(output.Schema);
                var firstOther = otherRows[0];
                foreach (var col in columns)
                {
                    if (groupColumns.Contains(col, StringComparer.OrdinalIgnoreCase))
                    {
                        othersRow[col] = firstOther[col];
                    }
                    else if (string.Equals(col, categoryCol, StringComparison.OrdinalIgnoreCase))
                    {
                        othersRow[col] = othersLabel;
                    }
                    else if (string.Equals(col, valueCol, StringComparison.OrdinalIgnoreCase))
                    {
                        othersRow[col] = AggregateValues(otherRows.Select(r => r[valueCol]).ToList(), aggregate);
                    }
                    else
                    {
                        othersRow[col] = null;
                    }
                }
                await output.AddRowAsync(othersRow);
                producedRows++;
            }

            if (output.Rows.Count >= context.EffectiveBatchSize)
            {
                await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                output = new DataTable();
                output.SetColumns(columns);
            }
        }

        if (output.Rows.Count > 0)
        {
            await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
        }

        context.Telemetry.RowsProcessed += producedRows;
        _logger.Info("TRANSFORM TOP_N_OTHERS complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private async Task ExecutePeriodComparison(TransformStatement stmt, IExecutionContext context)
    {
        var dateCol = GetStringOption(stmt.Options, "DATE_COL", "PERIOD_COMPARISON");
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "PERIOD_COMPARISON");
        var period = GetStringOption(stmt.Options, "PERIOD", "PERIOD_COMPARISON");
        var groupColumns = stmt.Options.TryGetValue("BY_GROUP", out var grpCol) ? GetGroupColumns(grpCol) : new List<string>();
        var diffCol = stmt.Options.TryGetValue("DIFF_COL", out var dCol) ? GetStringOption(stmt.Options, "DIFF_COL", "PERIOD_COMPARISON") : $"{valueCol}_Diff";
        var pctCol = stmt.Options.TryGetValue("PCT_COL", out var pCol) ? GetStringOption(stmt.Options, "PCT_COL", "PERIOD_COMPARISON") : $"{valueCol}_Pct";

        var (rows, columns) = await StageSourceRows(stmt, context, "PERIOD_COMPARISON");

        if (!columns.Contains(dateCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"PERIOD_COMPARISON DATE_COL '{dateCol}' was not found in source dataset.");
        if (!columns.Contains(valueCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"PERIOD_COMPARISON VALUE_COL '{valueCol}' was not found in source dataset.");

        var targetColumns = new List<string>(columns);
        if (!targetColumns.Contains(diffCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(diffCol);
        if (!targetColumns.Contains(pctCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(pctCol);

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

        var output = new DataTable();
        output.SetColumns(targetColumns);

        int producedRows = 0;
        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var sortedGroup = group.OrderBy(row =>
            {
                if (!TryGetDate(row[dateCol], out var dt))
                    throw new ExecutionException($"PERIOD_COMPARISON could not convert {dateCol} value '{row[dateCol]}' to a date.");
                return dt;
            }).ToList();

            var byDate = sortedGroup.ToDictionary(row =>
            {
                TryGetDate(row[dateCol], out var dt);
                return dt.Date;
            });

            foreach (var row in sortedGroup)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                TryGetDate(row[dateCol], out var currentDate);

                var priorDate = AddPeriod(currentDate, period, -1);
                var outRow = new Row(output.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = row[col];
                }

                if (byDate.TryGetValue(priorDate.Date, out var priorRow))
                {
                    var curVal = Convert.ToDecimal(GetValue(row[valueCol]));
                    var priorVal = Convert.ToDecimal(GetValue(priorRow[valueCol]));

                    outRow[diffCol] = curVal - priorVal;
                    outRow[pctCol] = priorVal != 0 ? (curVal - priorVal) / priorVal * 100m : null;
                }
                else
                {
                    outRow[diffCol] = null;
                    outRow[pctCol] = null;
                }

                await output.AddRowAsync(outRow);
                producedRows++;

                if (output.Rows.Count >= context.EffectiveBatchSize)
                {
                    await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                    output = new DataTable();
                    output.SetColumns(targetColumns);
                }
            }
        }

        if (output.Rows.Count > 0)
        {
            await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
        }

        context.Telemetry.RowsProcessed += producedRows;
        _logger.Info("TRANSFORM PERIOD_COMPARISON complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private static DateTime AddPeriod(DateTime dt, string period, int count)
    {
        return period.ToUpperInvariant() switch
        {
            "DAY" => dt.AddDays(count),
            "MONTH" => dt.AddMonths(count),
            "YEAR" => dt.AddYears(count),
            _ => throw new ExecutionException($"Unsupported comparison period: {period}")
        };
    }

    private async Task ExecuteShareOfTotal(TransformStatement stmt, IExecutionContext context)
    {
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "SHARE_OF_TOTAL");
        var groupColumns = stmt.Options.TryGetValue("BY_GROUP", out var grpCol) ? GetGroupColumns(grpCol) : new List<string>();
        var shareCol = stmt.Options.TryGetValue("SHARE_COL", out var sCol) ? GetStringOption(stmt.Options, "SHARE_COL", "SHARE_OF_TOTAL") : $"{valueCol}_Share";

        var (rows, columns) = await StageSourceRows(stmt, context, "SHARE_OF_TOTAL");

        if (!columns.Contains(valueCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"SHARE_OF_TOTAL VALUE_COL '{valueCol}' was not found in source dataset.");

        var targetColumns = new List<string>(columns);
        if (!targetColumns.Contains(shareCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(shareCol);

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

        var output = new DataTable();
        output.SetColumns(targetColumns);

        int producedRows = 0;
        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var groupRows = group.ToList();
            var totalSum = groupRows.Select(r => Convert.ToDecimal(GetValue(r[valueCol]))).Sum();

            foreach (var row in groupRows)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var outRow = new Row(output.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = row[col];
                }

                var val = Convert.ToDecimal(GetValue(row[valueCol]));
                outRow[shareCol] = totalSum != 0 ? val / totalSum : 0m;

                await output.AddRowAsync(outRow);
                producedRows++;

                if (output.Rows.Count >= context.EffectiveBatchSize)
                {
                    await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                    output = new DataTable();
                    output.SetColumns(targetColumns);
                }
            }
        }

        if (output.Rows.Count > 0)
        {
            await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
        }

        context.Telemetry.RowsProcessed += producedRows;
        _logger.Info("TRANSFORM SHARE_OF_TOTAL complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private async Task ExecuteNormalize(TransformStatement stmt, IExecutionContext context)
    {
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "NORMALIZE");
        var method = stmt.Options.TryGetValue("METHOD", out var mExpr) ? GetStringOption(stmt.Options, "METHOD", "NORMALIZE") : "MIN_MAX";
        var groupColumns = stmt.Options.TryGetValue("BY_GROUP", out var grpCol) ? GetGroupColumns(grpCol) : new List<string>();
        var normCol = stmt.Options.TryGetValue("NORM_COL", out var nCol) ? GetStringOption(stmt.Options, "NORM_COL", "NORMALIZE") : $"{valueCol}_Normalized";

        var (rows, columns) = await StageSourceRows(stmt, context, "NORMALIZE");

        if (!columns.Contains(valueCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"NORMALIZE VALUE_COL '{valueCol}' was not found in source dataset.");

        var targetColumns = new List<string>(columns);
        if (!targetColumns.Contains(normCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(normCol);

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

        var output = new DataTable();
        output.SetColumns(targetColumns);

        int producedRows = 0;
        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var groupRows = group.ToList();
            if (groupRows.Count == 0) continue;

            if (string.Equals(method, "MIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                var vals = groupRows.Select(r => Convert.ToDecimal(GetValue(r[valueCol]))).ToList();
                var min = vals.Min();
                var max = vals.Max();
                var range = max - min;

                foreach (var row in groupRows)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var outRow = new Row(output.Schema);
                    foreach (var col in columns)
                    {
                        outRow[col] = row[col];
                    }

                    var val = Convert.ToDecimal(GetValue(row[valueCol]));
                    outRow[normCol] = range != 0 ? (val - min) / range : 0m;

                    await output.AddRowAsync(outRow);
                    producedRows++;

                    if (output.Rows.Count >= context.EffectiveBatchSize)
                    {
                        await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                        output = new DataTable();
                        output.SetColumns(targetColumns);
                    }
                }
            }
            else if (string.Equals(method, "Z_SCORE", StringComparison.OrdinalIgnoreCase))
            {
                var vals = groupRows.Select(r => Convert.ToDouble(GetValue(r[valueCol]))).ToList();
                var count = vals.Count;
                var mean = vals.Average();
                var sumOfSquares = vals.Select(v => Math.Pow(v - mean, 2)).Sum();
                var stddev = Math.Sqrt(sumOfSquares / count);

                foreach (var row in groupRows)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var outRow = new Row(output.Schema);
                    foreach (var col in columns)
                    {
                        outRow[col] = row[col];
                    }

                    var val = Convert.ToDouble(GetValue(row[valueCol]));
                    outRow[normCol] = stddev != 0 ? (decimal)((val - mean) / stddev) : 0m;

                    await output.AddRowAsync(outRow);
                    producedRows++;

                    if (output.Rows.Count >= context.EffectiveBatchSize)
                    {
                        await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                        output = new DataTable();
                        output.SetColumns(targetColumns);
                    }
                }
            }
            else
            {
                throw new ExecutionException($"Unsupported NORMALIZE method: {method}");
            }
        }

        if (output.Rows.Count > 0)
        {
            await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
        }

        context.Telemetry.RowsProcessed += producedRows;
        _logger.Info("TRANSFORM NORMALIZE complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private async Task ExecuteRollingAggregate(TransformStatement stmt, IExecutionContext context)
    {
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "ROLLING_AGGREGATE");
        var orderCol = GetStringOption(stmt.Options, "ORDER_COL", "ROLLING_AGGREGATE");
        var windowSize = stmt.Options.TryGetValue("WINDOW_SIZE", out var wExpr) ? Convert.ToInt32(await context.EvaluationContext.EvaluateValue(wExpr, Row.Empty)) : 7;
        var aggregate = stmt.Options.TryGetValue("AGGREGATE", out var aggExpr) ? GetStringOption(stmt.Options, "AGGREGATE", "ROLLING_AGGREGATE") : "AVG";
        var groupColumns = stmt.Options.TryGetValue("BY_GROUP", out var grpCol) ? GetGroupColumns(grpCol) : new List<string>();
        var rollingCol = stmt.Options.TryGetValue("ROLLING_COL", out var rCol) ? GetStringOption(stmt.Options, "ROLLING_COL", "ROLLING_AGGREGATE") : $"{valueCol}_Rolling";

        var (rows, columns) = await StageSourceRows(stmt, context, "ROLLING_AGGREGATE");

        if (!columns.Contains(valueCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"ROLLING_AGGREGATE VALUE_COL '{valueCol}' was not found in source dataset.");
        if (!columns.Contains(orderCol, StringComparer.OrdinalIgnoreCase))
            throw new ExecutionException($"ROLLING_AGGREGATE ORDER_COL '{orderCol}' was not found in source dataset.");

        var targetColumns = new List<string>(columns);
        if (!targetColumns.Contains(rollingCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(rollingCol);

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

        var output = new DataTable();
        output.SetColumns(targetColumns);

        int producedRows = 0;
        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var sortedGroup = group.OrderBy(row => GetNumericOrderValue(row[orderCol])).ToList();
            int n = sortedGroup.Count;

            for (int i = 0; i < n; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var startIdx = Math.Max(0, i - windowSize + 1);
                var windowRows = sortedGroup.GetRange(startIdx, i - startIdx + 1);

                var outRow = new Row(output.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = sortedGroup[i][col];
                }

                var windowValues = windowRows.Select(r => r[valueCol]).ToList();
                outRow[rollingCol] = AggregateValues(windowValues, aggregate);

                await output.AddRowAsync(outRow);
                producedRows++;

                if (output.Rows.Count >= context.EffectiveBatchSize)
                {
                    await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                    output = new DataTable();
                    output.SetColumns(targetColumns);
                }
            }
        }

        if (output.Rows.Count > 0)
        {
            await target.WriteBatches(new[] { output }.ToAsyncEnumerable(), append: true, context.CancellationToken);
        }

        context.Telemetry.RowsProcessed += producedRows;
        _logger.Info("TRANSFORM ROLLING_AGGREGATE complete: {RowCount} rows staged into {Target}", producedRows, stmt.TargetTable.TableName);
    }

    private static string GetStringOption(Dictionary<string, Expression> options, string key, string algorithm)
    {
        if (!options.TryGetValue(key, out var expr))
            throw new ExecutionException($"Algorithm {algorithm} requires option '{key}'");
        
        if (expr is LiteralExpression { Value: string s })
            return s;
        if (expr is IdentifierExpression id)
            return id.Name;
        throw new ExecutionException($"Option '{key}' for algorithm {algorithm} must be a string literal or identifier");
    }

    private static List<string> GetGroupColumns(Expression expr)
    {
        string val;
        if (expr is LiteralExpression { Value: string s })
            val = s;
        else if (expr is IdentifierExpression id)
            val = id.Name;
        else
            throw new ExecutionException("Option must be a string literal or identifier");
        
        return val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static void ValidateColumns(string sourceTableName, IReadOnlyCollection<string> columns, string dateColumn, List<string> groupColumns)
    {
        if (!columns.Contains(dateColumn, StringComparer.OrdinalIgnoreCase))
        {
            throw new ExecutionException($"FILL_DATES DATE_COL '{dateColumn}' was not found in {sourceTableName}.");
        }

        foreach (var groupColumn in groupColumns)
        {
            if (!columns.Contains(groupColumn, StringComparer.OrdinalIgnoreCase))
            {
                throw new ExecutionException($"FILL_DATES BY_GROUP column '{groupColumn}' was not found in {sourceTableName}.");
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
        string dateColumn,
        List<string> groupColumns,
        Row template,
        DateTime date,
        object? gapFillValue)
    {
        var row = new Row(schema);
        foreach (var column in columns)
        {
            if (column.Equals(dateColumn, StringComparison.OrdinalIgnoreCase))
            {
                row[column] = date;
            }
            else if (groupColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
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

    private static object? GetValue(object? val)
    {
        if (val == null || val == DBNull.Value) return null;
        return val;
    }

    private static decimal GetNumericOrderValue(object? val)
    {
        var v = GetValue(val);
        if (v == null) return 0m;
        if (v is DateTime dt) return dt.Ticks;
        if (decimal.TryParse(v.ToString(), out var d)) return d;
        throw new ExecutionException($"Cannot convert ORDER_COL value '{val}' to a numeric or date value for linear interpolation.");
    }

    private record SortKey(string ColumnName, bool Descending);

    private static List<SortKey> ParseOrderBy(string orderBy)
    {
        var result = new List<SortKey>();
        var parts = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var subParts = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (subParts.Length == 0) continue;
            var colName = subParts[0];
            bool descending = false;
            if (subParts.Length > 1)
            {
                descending = string.Equals(subParts[1], "DESC", StringComparison.OrdinalIgnoreCase);
            }
            result.Add(new SortKey(colName, descending));
        }
        return result;
    }

    private class RowComparer(List<SortKey> sortKeys) : IComparer<Row>
    {
        public int Compare(Row? x, Row? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            foreach (var sk in sortKeys)
            {
                var vx = x[sk.ColumnName];
                var vy = y[sk.ColumnName];
                int cmp = CompareValues(vx, vy);
                if (cmp != 0)
                {
                    return sk.Descending ? -cmp : cmp;
                }
            }
            return 0;
        }
    }

    private static int CompareValues(object? x, object? y)
    {
        var valX = GetValue(x);
        var valY = GetValue(y);

        if (valX == null) return valY == null ? 0 : -1;
        if (valY == null) return 1;

        if (valX is IComparable cx)
        {
            try
            {
                if (valX.GetType() != valY.GetType())
                {
                    var convertedY = Convert.ChangeType(valY, valX.GetType());
                    return cx.CompareTo(convertedY);
                }
                return cx.CompareTo(valY);
            }
            catch
            {
                // Fallback to string comparison if conversion fails
            }
        }
        return string.Compare(valX.ToString(), valY.ToString(), StringComparison.Ordinal);
    }

    private static object? AggregateValues(List<object?> values, string aggregate)
    {
        var nonNulls = values.Where(v => GetValue(v) != null).ToList();
        if (string.Equals(aggregate, "COUNT", StringComparison.OrdinalIgnoreCase))
        {
            return nonNulls.Count;
        }

        if (nonNulls.Count == 0) return null;

        if (string.Equals(aggregate, "SUM", StringComparison.OrdinalIgnoreCase))
        {
            return nonNulls.Select(v => Convert.ToDecimal(GetValue(v))).Sum();
        }
        if (string.Equals(aggregate, "AVG", StringComparison.OrdinalIgnoreCase))
        {
            return nonNulls.Select(v => Convert.ToDecimal(GetValue(v))).Average();
        }
        if (string.Equals(aggregate, "MIN", StringComparison.OrdinalIgnoreCase))
        {
            nonNulls.Sort(CompareValues);
            return nonNulls[0];
        }
        if (string.Equals(aggregate, "MAX", StringComparison.OrdinalIgnoreCase))
        {
            nonNulls.Sort(CompareValues);
            return nonNulls[^1];
        }

        throw new ExecutionException($"Unsupported PIVOT aggregate function: {aggregate}");
    }
}
