using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles TRANSFORM statement and its supported algorithms (e.g. FILL_DATES, INTERPOLATE, DEDUPLICATE, PIVOT, TOP_N_OTHERS, PERIOD_COMPARISON, SHARE_OF_TOTAL, NORMALIZE, ROLLING_AGGREGATE).
/// </summary>
public class TransformStatementHandler(ILogger logger) : IStatementHandler
{
    private static readonly HashSet<string> SupportedAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVG", "COUNT", "MIN", "MAX"
    };

    private readonly ILogger _logger = logger;

    public Type SupportedStatementType => typeof(TransformStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (TransformStatement)statement;

        switch (stmt.Algorithm.ToUpperInvariant())
        {
            case "FILL_DATES":
                await ExecuteFillDates(stmt, context);
                break;
            case "INTERPOLATE":
                await ExecuteInterpolate(stmt, context);
                break;
            case "DEDUPLICATE":
                await ExecuteDeduplicate(stmt, context);
                break;
            case "PIVOT":
                await ExecutePivot(stmt, context);
                break;
            case "TOP_N_OTHERS":
                await ExecuteTopNOthers(stmt, context);
                break;
            case "PERIOD_COMPARISON":
                await ExecutePeriodComparison(stmt, context);
                break;
            case "SHARE_OF_TOTAL":
                await ExecuteShareOfTotal(stmt, context);
                break;
            case "NORMALIZE":
                await ExecuteNormalize(stmt, context);
                break;
            case "ROLLING_AGGREGATE":
                await ExecuteRollingAggregate(stmt, context);
                break;
            default:
                throw new ExecutionException($"Unsupported table transformation algorithm: {stmt.Algorithm}");
        }
    }

    private static async Task<(List<Row> Rows, List<string> Columns, string SourceName)> StageSourceRows(
        TransformStatement stmt,
        IExecutionContext context,
        string algorithmName)
    {
        var sourceTable = stmt.SourceTable ?? stmt.TargetTable;
        var source = await context.ResolveReadSourceAsync(sourceTable)
            ?? throw new ExecutionException($"Could not resolve source dataset: {sourceTable.TableName}");

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
            throw new ExecutionException($"Source dataset {sourceTable.TableName} has no columns.");
        }

        return (rows, columns, sourceTable.TableName);
    }

    private async Task ExecuteFillDates(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "FILL_DATES", "DATE_COL", "GAPS_FILL", "BY_GROUP");
        var dateColumn = GetStringOption(stmt.Options, "DATE_COL", "FILL_DATES");
        var gapFillValueExpr = stmt.Options.TryGetValue("GAPS_FILL", out var grp)
            ? grp
            : new LiteralExpression(0m, TokenType.NUMERIC);
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");

        var (rows, columns, sourceName) = await StageSourceRows(stmt, context, "FILL_DATES");
        ValidateColumnsExist(sourceName, columns, "FILL_DATES",
            ("DATE_COL", [dateColumn]),
            ("BY_GROUP", groupColumns));

        var gapFillValue = await context.EvaluationContext.EvaluateValue(gapFillValueExpr, Row.Empty);
        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "FILL_DATES", columns, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var byDate = new Dictionary<DateTime, List<Row>>();
            Row? template = null;
            foreach (var row in group)
            {
                if (!TryGetDate(row[dateColumn], out var date))
                {
                    throw new ExecutionException($"FILL_DATES could not convert {dateColumn} value '{row[dateColumn]}' to a date.");
                }
                template ??= row;
                var day = date.Date;
                if (!byDate.TryGetValue(day, out var dayRows))
                {
                    dayRows = new List<Row>();
                    byDate[day] = dayRows;
                }
                dayRows.Add(row);
            }

            if (byDate.Count == 0 || template == null) continue;
            var minDate = byDate.Keys.Min();
            var maxDate = byDate.Keys.Max();

            for (var date = minDate; date <= maxDate; date = date.AddDays(1))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (byDate.TryGetValue(date, out var existingRows))
                {
                    foreach (var existing in existingRows)
                    {
                        await sink.WriteRowAsync(existing.Clone());
                    }
                }
                else
                {
                    await sink.WriteRowAsync(
                        CreateGapRow(sink.Schema, columns, dateColumn, groupColumns, template, date, gapFillValue));
                }
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteInterpolate(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "INTERPOLATE", "VALUE_COL", "ORDER_COL", "METHOD", "BY_GROUP");
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "INTERPOLATE");
        var orderCol = GetStringOption(stmt.Options, "ORDER_COL", "INTERPOLATE");
        var method = stmt.Options.ContainsKey("METHOD")
            ? GetStringOption(stmt.Options, "METHOD", "INTERPOLATE")
            : "LINEAR";
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");

        bool isForward = string.Equals(method, "FORWARD", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(method, "FORWARD_FILL", StringComparison.OrdinalIgnoreCase);
        bool isBackward = string.Equals(method, "BACKWARD", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(method, "BACKWARD_FILL", StringComparison.OrdinalIgnoreCase);
        bool isLinear = string.Equals(method, "LINEAR", StringComparison.OrdinalIgnoreCase);
        if (!isForward && !isBackward && !isLinear)
        {
            throw new ExecutionException($"Unsupported INTERPOLATE method: {method}");
        }

        var (rows, columns, sourceName) = await StageSourceRows(stmt, context, "INTERPOLATE");
        ValidateColumnsExist(sourceName, columns, "INTERPOLATE",
            ("VALUE_COL", [valueCol]),
            ("ORDER_COL", [orderCol]),
            ("BY_GROUP", groupColumns));

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "INTERPOLATE", columns, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var sortedGroup = group
                .Select(row => (Row: row, Order: GetOrderValue(row[orderCol], "INTERPOLATE", orderCol)))
                .OrderBy(item => item.Order)
                .ToList();
            int n = sortedGroup.Count;

            if (isForward)
            {
                object? lastVal = null;
                for (int i = 0; i < n; i++)
                {
                    var val = GetValue(sortedGroup[i].Row[valueCol]);
                    if (val != null)
                    {
                        lastVal = val;
                    }
                    else if (lastVal != null)
                    {
                        sortedGroup[i].Row[valueCol] = lastVal;
                    }
                }
            }
            else if (isBackward)
            {
                object? nextVal = null;
                for (int i = n - 1; i >= 0; i--)
                {
                    var val = GetValue(sortedGroup[i].Row[valueCol]);
                    if (val != null)
                    {
                        nextVal = val;
                    }
                    else if (nextVal != null)
                    {
                        sortedGroup[i].Row[valueCol] = nextVal;
                    }
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    if (GetValue(sortedGroup[i].Row[valueCol]) != null) continue;

                    int prevIdx = -1;
                    for (int p = i - 1; p >= 0; p--)
                    {
                        if (GetValue(sortedGroup[p].Row[valueCol]) != null)
                        {
                            prevIdx = p;
                            break;
                        }
                    }

                    int nextIdx = -1;
                    for (int nx = i + 1; nx < n; nx++)
                    {
                        if (GetValue(sortedGroup[nx].Row[valueCol]) != null)
                        {
                            nextIdx = nx;
                            break;
                        }
                    }

                    if (prevIdx != -1 && nextIdx != -1)
                    {
                        var y0 = ToDecimal(GetValue(sortedGroup[prevIdx].Row[valueCol])!, "INTERPOLATE", valueCol);
                        var y1 = ToDecimal(GetValue(sortedGroup[nextIdx].Row[valueCol])!, "INTERPOLATE", valueCol);
                        var x0 = sortedGroup[prevIdx].Order;
                        var x1 = sortedGroup[nextIdx].Order;
                        var x = sortedGroup[i].Order;

                        sortedGroup[i].Row[valueCol] = x1 == x0
                            ? y0
                            : LinearInterpolate(y0, y1, x0, x1, x);
                    }
                }
            }

            foreach (var item in sortedGroup)
            {
                await sink.WriteRowAsync(item.Row);
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteDeduplicate(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "DEDUPLICATE", "KEY_COLS", "ORDER_BY", "KEEP");
        if (!stmt.Options.TryGetValue("KEY_COLS", out var kExpr))
        {
            throw new ExecutionException("DEDUPLICATE algorithm requires KEY_COLS parameter");
        }
        var keyCols = GetGroupColumns(kExpr);
        if (keyCols.Count == 0)
        {
            throw new ExecutionException("DEDUPLICATE KEY_COLS must specify at least one column.");
        }

        var orderByStr = stmt.Options.ContainsKey("ORDER_BY")
            ? GetStringOption(stmt.Options, "ORDER_BY", "DEDUPLICATE")
            : null;
        var keep = stmt.Options.ContainsKey("KEEP")
            ? GetStringOption(stmt.Options, "KEEP", "DEDUPLICATE")
            : "FIRST";

        if (!string.Equals(keep, "FIRST", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(keep, "LAST", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExecutionException($"Unsupported DEDUPLICATE KEEP option: {keep}");
        }

        List<SortKey>? sortKeys = !string.IsNullOrWhiteSpace(orderByStr) ? ParseOrderBy(orderByStr) : null;

        var (rows, columns, sourceName) = await StageSourceRows(stmt, context, "DEDUPLICATE");
        ValidateColumnsExist(sourceName, columns, "DEDUPLICATE",
            ("KEY_COLS", keyCols),
            ("ORDER_BY", sortKeys?.Select(sk => sk.ColumnName) ?? []));

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "DEDUPLICATE", columns, _logger);

        var comparer = sortKeys is { Count: > 0 } ? new RowComparer(sortKeys) : null;
        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, keyCols)))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            // LINQ OrderBy is a stable merge sort in .NET, preserving input order on ties.
            var groupRows = comparer != null
                ? group.OrderBy(r => r, comparer).ToList()
                : group.ToList();
            if (groupRows.Count == 0) continue;

            var keptRow = string.Equals(keep, "LAST", StringComparison.OrdinalIgnoreCase)
                ? groupRows[^1]
                : groupRows[0];

            await sink.WriteRowAsync(keptRow);
        }

        await sink.CompleteAsync();
    }

    private async Task ExecutePivot(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "PIVOT", "ROW_FIELDS", "PIVOT_FIELD", "VALUE_FIELD", "AGGREGATE");
        if (!stmt.Options.TryGetValue("ROW_FIELDS", out var rfExpr))
        {
            throw new ExecutionException("PIVOT algorithm requires ROW_FIELDS parameter");
        }
        var rowFields = GetGroupColumns(rfExpr);
        var pivotField = GetStringOption(stmt.Options, "PIVOT_FIELD", "PIVOT");
        var valueField = GetStringOption(stmt.Options, "VALUE_FIELD", "PIVOT");
        var aggregate = stmt.Options.ContainsKey("AGGREGATE")
            ? GetStringOption(stmt.Options, "AGGREGATE", "PIVOT")
            : "SUM";
        ValidateAggregate(aggregate, "PIVOT");

        var (rows, columns, sourceName) = await StageSourceRows(stmt, context, "PIVOT");
        ValidateColumnsExist(sourceName, columns, "PIVOT",
            ("ROW_FIELDS", rowFields),
            ("PIVOT_FIELD", [pivotField]),
            ("VALUE_FIELD", [valueField]));

        var pivotEntries = rows
            .Select(r => GetValue(r[pivotField]))
            .Select(v => (IsNull: v == null, ColumnName: v?.ToString() ?? "NULL"))
            .GroupBy(x => (x.IsNull, x.ColumnName.ToUpperInvariant()))
            .Select(g => g.First())
            .OrderBy(x => x.ColumnName, StringComparer.Ordinal)
            .ToList();

        var targetColumns = new List<string>(rowFields);
        foreach (var entry in pivotEntries)
        {
            if (!targetColumns.Contains(entry.ColumnName, StringComparer.OrdinalIgnoreCase))
            {
                targetColumns.Add(entry.ColumnName);
            }
        }

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "PIVOT", targetColumns, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, rowFields)))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var groupRows = group.ToList();
            var firstRow = groupRows[0];

            var outRow = new Row(sink.Schema);
            foreach (var rf in rowFields)
            {
                outRow[rf] = firstRow[rf];
            }

            foreach (var entry in pivotEntries)
            {
                var matchingValues = groupRows
                    .Where(r =>
                    {
                        var val = GetValue(r[pivotField]);
                        return entry.IsNull
                            ? val == null
                            : val != null && string.Equals(val.ToString(), entry.ColumnName, StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(r => r[valueField])
                    .ToList();

                outRow[entry.ColumnName] = AggregateValues(matchingValues, aggregate, "PIVOT");
            }

            await sink.WriteRowAsync(outRow);
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteTopNOthers(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "TOP_N_OTHERS", "N", "VALUE_COL", "CATEGORY_COL", "OTHERS_LABEL", "AGGREGATE", "BY_GROUP");
        var n = await EvaluateIntOption(stmt.Options, "N", "TOP_N_OTHERS", 5, context);
        if (n < 0)
        {
            throw new ExecutionException($"TOP_N_OTHERS N must be >= 0, got {n}.");
        }
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "TOP_N_OTHERS");
        var categoryCol = GetStringOption(stmt.Options, "CATEGORY_COL", "TOP_N_OTHERS");
        var othersLabel = stmt.Options.ContainsKey("OTHERS_LABEL")
            ? GetStringOption(stmt.Options, "OTHERS_LABEL", "TOP_N_OTHERS")
            : "Others";
        var aggregate = stmt.Options.ContainsKey("AGGREGATE")
            ? GetStringOption(stmt.Options, "AGGREGATE", "TOP_N_OTHERS")
            : "SUM";
        ValidateAggregate(aggregate, "TOP_N_OTHERS");
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");

        var (rows, columns, sourceName) = await StageSourceRows(stmt, context, "TOP_N_OTHERS");
        ValidateColumnsExist(sourceName, columns, "TOP_N_OTHERS",
            ("VALUE_COL", [valueCol]),
            ("CATEGORY_COL", [categoryCol]),
            ("BY_GROUP", groupColumns));

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "TOP_N_OTHERS", columns, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var groupRows = group.ToList();
            if (groupRows.Count == 0) continue;

            var categoryAggs = groupRows
                .GroupBy(r =>
                {
                    var cat = GetValue(r[categoryCol]);
                    return (IsNull: cat == null, UpperKey: cat?.ToString()?.ToUpperInvariant() ?? string.Empty);
                })
                .Select(cg =>
                {
                    var catRows = cg.ToList();
                    var aggRaw = AggregateValues(catRows.Select(r => r[valueCol]).ToList(), aggregate, "TOP_N_OTHERS");
                    decimal? aggVal = aggRaw == null ? null : Convert.ToDecimal(aggRaw, CultureInfo.InvariantCulture);
                    return new
                    {
                        RawCategory = catRows[0][categoryCol],
                        Rows = catRows,
                        AggRaw = aggRaw,
                        AggValue = aggVal
                    };
                })
                .OrderByDescending(c => c.AggValue.HasValue)
                .ThenByDescending(c => c.AggValue)
                .ToList();

            var topEntries = categoryAggs.Take(n).ToList();
            var otherEntries = categoryAggs.Skip(n).ToList();

            foreach (var entry in topEntries)
            {
                var outRow = new Row(sink.Schema);
                var template = entry.Rows[0];
                foreach (var col in columns)
                {
                    if (groupColumns.Contains(col, StringComparer.OrdinalIgnoreCase))
                    {
                        outRow[col] = template[col];
                    }
                    else if (string.Equals(col, categoryCol, StringComparison.OrdinalIgnoreCase))
                    {
                        outRow[col] = entry.RawCategory;
                    }
                    else if (string.Equals(col, valueCol, StringComparison.OrdinalIgnoreCase))
                    {
                        outRow[col] = entry.AggRaw;
                    }
                    else
                    {
                        outRow[col] = null;
                    }
                }
                await sink.WriteRowAsync(outRow);
            }

            if (otherEntries.Count > 0)
            {
                var otherRows = otherEntries.SelectMany(e => e.Rows).ToList();
                var othersRow = new Row(sink.Schema);
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
                        othersRow[col] = AggregateValues(otherRows.Select(r => r[valueCol]).ToList(), aggregate, "TOP_N_OTHERS");
                    }
                    else
                    {
                        othersRow[col] = null;
                    }
                }
                await sink.WriteRowAsync(othersRow);
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecutePeriodComparison(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "PERIOD_COMPARISON", "DATE_COL", "VALUE_COL", "PERIOD", "BY_GROUP", "DIFF_COL", "PCT_COL");
        var dateCol = GetStringOption(stmt.Options, "DATE_COL", "PERIOD_COMPARISON");
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "PERIOD_COMPARISON");
        var period = GetStringOption(stmt.Options, "PERIOD", "PERIOD_COMPARISON");
        ValidatePeriod(period);
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");
        var diffCol = stmt.Options.ContainsKey("DIFF_COL")
            ? GetStringOption(stmt.Options, "DIFF_COL", "PERIOD_COMPARISON")
            : $"{valueCol}_Diff";
        var pctCol = stmt.Options.ContainsKey("PCT_COL")
            ? GetStringOption(stmt.Options, "PCT_COL", "PERIOD_COMPARISON")
            : $"{valueCol}_Pct";

        var (rows, columns, sourceName) = await StageSourceRows(stmt, context, "PERIOD_COMPARISON");
        ValidateColumnsExist(sourceName, columns, "PERIOD_COMPARISON",
            ("DATE_COL", [dateCol]),
            ("VALUE_COL", [valueCol]),
            ("BY_GROUP", groupColumns));

        var targetColumns = new List<string>(columns);
        if (!targetColumns.Contains(diffCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(diffCol);
        if (!targetColumns.Contains(pctCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(pctCol);

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "PERIOD_COMPARISON", targetColumns, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var sortedGroup = group
                .Select(row =>
                {
                    if (!TryGetDate(row[dateCol], out var dt))
                    {
                        throw new ExecutionException($"PERIOD_COMPARISON could not convert {dateCol} value '{row[dateCol]}' to a date.");
                    }
                    return (Row: row, Date: dt);
                })
                .OrderBy(item => item.Date)
                .ToList();

            var byDate = new Dictionary<DateTime, decimal?>();
            foreach (var dayGroup in sortedGroup.GroupBy(item => item.Date.Date))
            {
                var nonNullDayValues = dayGroup
                    .Select(item => GetValue(item.Row[valueCol]))
                    .Where(v => v != null)
                    .Select(v => ToDecimal(v!, "PERIOD_COMPARISON", valueCol))
                    .ToList();
                byDate[dayGroup.Key] = nonNullDayValues.Count > 0 ? nonNullDayValues.Sum() : null;
            }

            foreach (var item in sortedGroup)
            {
                var row = item.Row;
                var currentDate = item.Date;

                var outRow = new Row(sink.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = row[col];
                }

                var rawCurVal = GetValue(row[valueCol]);
                if (rawCurVal != null && TryLookupPriorValue(byDate, currentDate, period, out var priorVal) && priorVal.HasValue)
                {
                    var curVal = ToDecimal(rawCurVal, "PERIOD_COMPARISON", valueCol);
                    outRow[diffCol] = curVal - priorVal.Value;
                    outRow[pctCol] = priorVal.Value != 0m ? (curVal - priorVal.Value) / priorVal.Value * 100m : null;
                }
                else
                {
                    outRow[diffCol] = null;
                    outRow[pctCol] = null;
                }

                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private static bool TryLookupPriorValue(
        Dictionary<DateTime, decimal?> byDate,
        DateTime currentDate,
        string period,
        out decimal? priorValue)
    {
        var exactPrior = AddPeriod(currentDate, period, -1);
        if (byDate.TryGetValue(exactPrior.Date, out priorValue))
        {
            return true;
        }

        // Month-end alignment: e.g., 2026-02-28 (month end) looks back to 2026-01-31 when 2026-01-28 is absent.
        if ((string.Equals(period, "MONTH", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(period, "YEAR", StringComparison.OrdinalIgnoreCase)) &&
            currentDate.Day == DateTime.DaysInMonth(currentDate.Year, currentDate.Month))
        {
            var monthEndPrior = new DateTime(
                exactPrior.Year,
                exactPrior.Month,
                DateTime.DaysInMonth(exactPrior.Year, exactPrior.Month));
            if (byDate.TryGetValue(monthEndPrior, out priorValue))
            {
                return true;
            }
        }

        priorValue = null;
        return false;
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
        ValidateAllowedOptions(stmt, "SHARE_OF_TOTAL", "VALUE_COL", "BY_GROUP", "SHARE_COL");
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "SHARE_OF_TOTAL");
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");
        var shareCol = stmt.Options.ContainsKey("SHARE_COL")
            ? GetStringOption(stmt.Options, "SHARE_COL", "SHARE_OF_TOTAL")
            : $"{valueCol}_Share";

        var (rows, columns, sourceName) = await StageSourceRows(stmt, context, "SHARE_OF_TOTAL");
        ValidateColumnsExist(sourceName, columns, "SHARE_OF_TOTAL",
            ("VALUE_COL", [valueCol]),
            ("BY_GROUP", groupColumns));

        var targetColumns = new List<string>(columns);
        if (!targetColumns.Contains(shareCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(shareCol);

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "SHARE_OF_TOTAL", targetColumns, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var groupRows = group.ToList();
            var totalSum = groupRows
                .Select(r => GetValue(r[valueCol]))
                .Where(v => v != null)
                .Select(v => ToDecimal(v!, "SHARE_OF_TOTAL", valueCol))
                .Sum();

            foreach (var row in groupRows)
            {
                var outRow = new Row(sink.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = row[col];
                }

                var rawVal = GetValue(row[valueCol]);
                if (rawVal == null)
                {
                    outRow[shareCol] = null;
                }
                else
                {
                    var val = ToDecimal(rawVal, "SHARE_OF_TOTAL", valueCol);
                    outRow[shareCol] = totalSum != 0m ? val / totalSum : 0m;
                }

                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteNormalize(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "NORMALIZE", "VALUE_COL", "METHOD", "BY_GROUP", "NORM_COL");
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "NORMALIZE");
        var method = stmt.Options.ContainsKey("METHOD")
            ? GetStringOption(stmt.Options, "METHOD", "NORMALIZE")
            : "MIN_MAX";
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");
        var normCol = stmt.Options.ContainsKey("NORM_COL")
            ? GetStringOption(stmt.Options, "NORM_COL", "NORMALIZE")
            : $"{valueCol}_Normalized";

        bool isMinMax = string.Equals(method, "MIN_MAX", StringComparison.OrdinalIgnoreCase);
        bool isZScore = string.Equals(method, "Z_SCORE", StringComparison.OrdinalIgnoreCase);
        if (!isMinMax && !isZScore)
        {
            throw new ExecutionException($"Unsupported NORMALIZE method: {method}");
        }

        var (rows, columns, sourceName) = await StageSourceRows(stmt, context, "NORMALIZE");
        ValidateColumnsExist(sourceName, columns, "NORMALIZE",
            ("VALUE_COL", [valueCol]),
            ("BY_GROUP", groupColumns));

        var targetColumns = new List<string>(columns);
        if (!targetColumns.Contains(normCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(normCol);

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "NORMALIZE", targetColumns, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var groupRows = group.ToList();
            if (groupRows.Count == 0) continue;

            if (isMinMax)
            {
                var nonNullVals = groupRows
                    .Select(r => GetValue(r[valueCol]))
                    .Where(v => v != null)
                    .Select(v => ToDecimal(v!, "NORMALIZE", valueCol))
                    .ToList();
                var min = nonNullVals.Count > 0 ? nonNullVals.Min() : 0m;
                var max = nonNullVals.Count > 0 ? nonNullVals.Max() : 0m;
                var range = max - min;

                foreach (var row in groupRows)
                {
                    var outRow = new Row(sink.Schema);
                    foreach (var col in columns)
                    {
                        outRow[col] = row[col];
                    }

                    var rawVal = GetValue(row[valueCol]);
                    if (rawVal == null)
                    {
                        outRow[normCol] = null;
                    }
                    else
                    {
                        var val = ToDecimal(rawVal, "NORMALIZE", valueCol);
                        outRow[normCol] = range != 0m ? (val - min) / range : 0m;
                    }

                    await sink.WriteRowAsync(outRow);
                }
            }
            else
            {
                var nonNullVals = groupRows
                    .Select(r => GetValue(r[valueCol]))
                    .Where(v => v != null)
                    .Select(v => ToDouble(v!, "NORMALIZE", valueCol))
                    .ToList();
                var count = nonNullVals.Count;
                var mean = count > 0 ? nonNullVals.Average() : 0.0;
                var sumOfSquares = count > 0 ? nonNullVals.Select(v => Math.Pow(v - mean, 2)).Sum() : 0.0;
                var stddev = count > 0 ? Math.Sqrt(sumOfSquares / count) : 0.0;

                foreach (var row in groupRows)
                {
                    var outRow = new Row(sink.Schema);
                    foreach (var col in columns)
                    {
                        outRow[col] = row[col];
                    }

                    var rawVal = GetValue(row[valueCol]);
                    if (rawVal == null)
                    {
                        outRow[normCol] = null;
                    }
                    else
                    {
                        var val = ToDouble(rawVal, "NORMALIZE", valueCol);
                        outRow[normCol] = stddev != 0.0 ? (decimal)((val - mean) / stddev) : 0m;
                    }

                    await sink.WriteRowAsync(outRow);
                }
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteRollingAggregate(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "ROLLING_AGGREGATE", "VALUE_COL", "ORDER_COL", "WINDOW_SIZE", "AGGREGATE", "BY_GROUP", "ROLLING_COL");
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "ROLLING_AGGREGATE");
        var orderCol = GetStringOption(stmt.Options, "ORDER_COL", "ROLLING_AGGREGATE");
        var windowSize = await EvaluateIntOption(stmt.Options, "WINDOW_SIZE", "ROLLING_AGGREGATE", 7, context);
        if (windowSize < 1)
        {
            throw new ExecutionException($"ROLLING_AGGREGATE WINDOW_SIZE must be >= 1, got {windowSize}.");
        }
        var aggregate = stmt.Options.ContainsKey("AGGREGATE")
            ? GetStringOption(stmt.Options, "AGGREGATE", "ROLLING_AGGREGATE")
            : "AVG";
        ValidateAggregate(aggregate, "ROLLING_AGGREGATE");
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");
        var rollingCol = stmt.Options.ContainsKey("ROLLING_COL")
            ? GetStringOption(stmt.Options, "ROLLING_COL", "ROLLING_AGGREGATE")
            : $"{valueCol}_Rolling";

        var (rows, columns, sourceName) = await StageSourceRows(stmt, context, "ROLLING_AGGREGATE");
        ValidateColumnsExist(sourceName, columns, "ROLLING_AGGREGATE",
            ("VALUE_COL", [valueCol]),
            ("ORDER_COL", [orderCol]),
            ("BY_GROUP", groupColumns));

        var targetColumns = new List<string>(columns);
        if (!targetColumns.Contains(rollingCol, StringComparer.OrdinalIgnoreCase)) targetColumns.Add(rollingCol);

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "ROLLING_AGGREGATE", targetColumns, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var sortedGroup = group
                .Select(row => (Row: row, Order: GetOrderValue(row[orderCol], "ROLLING_AGGREGATE", orderCol)))
                .OrderBy(item => item.Order)
                .ToList();
            int n = sortedGroup.Count;

            for (int i = 0; i < n; i++)
            {
                var startIdx = Math.Max(0, i - windowSize + 1);
                var windowValues = new List<object?>(i - startIdx + 1);
                for (int w = startIdx; w <= i; w++)
                {
                    windowValues.Add(sortedGroup[w].Row[valueCol]);
                }

                var outRow = new Row(sink.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = sortedGroup[i].Row[col];
                }

                outRow[rollingCol] = AggregateValues(windowValues, aggregate, "ROLLING_AGGREGATE");
                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private static void ValidateAllowedOptions(
        TransformStatement stmt,
        string algorithm,
        params string[] allowedKeys)
    {
        var allowed = new HashSet<string>(allowedKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in stmt.Options.Keys)
        {
            if (!allowed.Contains(key))
            {
                throw new ExecutionException(
                    $"Unknown option '{key}' for TRANSFORM {algorithm}. Allowed options: {string.Join(", ", allowedKeys)}.");
            }
        }
    }

    private static void ValidateColumnsExist(
        string sourceTableName,
        IReadOnlyCollection<string> columns,
        string algorithm,
        params (string OptionName, IEnumerable<string> ColumnNames)[] checks)
    {
        foreach (var (optionName, columnNames) in checks)
        {
            foreach (var col in columnNames)
            {
                if (!columns.Contains(col, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ExecutionException(
                        $"{algorithm} {optionName} column '{col}' was not found in {sourceTableName}.");
                }
            }
        }
    }

    private static void ValidateAggregate(string aggregate, string algorithm)
    {
        if (!SupportedAggregates.Contains(aggregate))
        {
            throw new ExecutionException($"Unsupported {algorithm} aggregate function: {aggregate}");
        }
    }

    private static void ValidatePeriod(string period)
    {
        if (!string.Equals(period, "DAY", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(period, "MONTH", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(period, "YEAR", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExecutionException($"Unsupported comparison period: {period}");
        }
    }

    private static async Task<int> EvaluateIntOption(
        Dictionary<string, Expression> options,
        string key,
        string algorithm,
        int defaultValue,
        IExecutionContext context)
    {
        if (!options.TryGetValue(key, out var expr))
        {
            return defaultValue;
        }

        var raw = GetValue(await context.EvaluationContext.EvaluateValue(expr, Row.Empty));
        if (raw == null)
        {
            throw new ExecutionException($"{algorithm} {key} cannot be NULL.");
        }

        try
        {
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ExecutionException($"{algorithm} {key} value '{raw}' must be an integer.", ex);
        }
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

    private static List<string> GetOptionalColumnList(Dictionary<string, Expression> options, string key)
    {
        return options.TryGetValue(key, out var expr) ? GetGroupColumns(expr) : new List<string>();
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

    private static string BuildGroupKey(Row row, IReadOnlyList<string> groupColumns)
    {
        if (groupColumns.Count == 0) return string.Empty;

        var parts = new string[groupColumns.Count];
        for (int i = 0; i < groupColumns.Count; i++)
        {
            var val = GetValue(row[groupColumns[i]]);
            if (val == null)
            {
                parts[i] = "N";
                continue;
            }

            string text = val switch
            {
                decimal dec => (dec / 1.00000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
                DateTime dt => dt.Ticks.ToString(CultureInfo.InvariantCulture),
                DateOnly dOnly => dOnly.ToDateTime(TimeOnly.MinValue).Ticks.ToString(CultureInfo.InvariantCulture),
                _ => Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty
            };
            parts[i] = $"V{text.Length}:{text}";
        }
        return string.Join('|', parts);
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
                // Keep the column's type: a DATE column holds DateOnly, DATETIME holds DateTime.
                row[column] = template[column] is DateOnly ? DateOnly.FromDateTime(date) : date;
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
        var v = GetValue(value);
        if (v == null)
        {
            date = default;
            return false;
        }
        if (v is DateTime dt)
        {
            date = dt.Date;
            return true;
        }

        return EvaluationUtils.TryToDateTime(v, out date);
    }

    private static object? GetValue(object? val)
    {
        if (val == null || val == DBNull.Value) return null;
        return val;
    }

    private static decimal GetOrderValue(object? val, string algorithm, string orderCol)
    {
        var v = GetValue(val);
        if (v == null)
        {
            throw new ExecutionException($"{algorithm} ORDER_COL '{orderCol}' cannot contain NULL values.");
        }
        if (v is DateTime dt) return dt.Ticks;
        if (v is DateOnly dateOnly) return dateOnly.ToDateTime(TimeOnly.MinValue).Ticks;
        if (v is DateTimeOffset dto) return dto.DateTime.Ticks;
        if (v is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
        }
        if (v is string s)
        {
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }
            if (EvaluationUtils.TryToDateTime(s, out var parsedDate))
            {
                return parsedDate.Ticks;
            }
        }
        throw new ExecutionException($"Cannot convert {algorithm} ORDER_COL '{orderCol}' value '{val}' to a numeric or date value.");
    }

    private static decimal LinearInterpolate(decimal y0, decimal y1, decimal x0, decimal x1, decimal x)
    {
        try
        {
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }
        catch (OverflowException)
        {
            double frac = (double)(x - x0) / (double)(x1 - x0);
            return (decimal)((double)y0 + ((double)(y1 - y0) * frac));
        }
    }

    private static decimal ToDecimal(object value, string algorithm, string column)
    {
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ExecutionException($"{algorithm} column '{column}' value '{value}' is not numeric.", ex);
        }
    }

    private static double ToDouble(object value, string algorithm, string column)
    {
        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ExecutionException($"{algorithm} column '{column}' value '{value}' is not numeric.", ex);
        }
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
            if (subParts.Length > 2)
            {
                throw new ExecutionException($"Invalid DEDUPLICATE ORDER_BY clause: '{part}'.");
            }
            var colName = subParts[0];
            bool descending = false;
            if (subParts.Length == 2)
            {
                if (string.Equals(subParts[1], "DESC", StringComparison.OrdinalIgnoreCase))
                {
                    descending = true;
                }
                else if (!string.Equals(subParts[1], "ASC", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ExecutionException($"Invalid DEDUPLICATE ORDER_BY sort direction '{subParts[1]}' in '{part}'. Expected ASC or DESC.");
                }
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
        return EvaluationUtils.CompareConstants(x, y, caseSensitive: true);
    }

    private static object? AggregateValues(List<object?> values, string aggregate, string algorithm)
    {
        var nonNulls = values.Select(GetValue).Where(v => v != null).Cast<object>().ToList();
        if (string.Equals(aggregate, "COUNT", StringComparison.OrdinalIgnoreCase))
        {
            return (decimal)nonNulls.Count;
        }

        if (nonNulls.Count == 0) return null;

        if (string.Equals(aggregate, "SUM", StringComparison.OrdinalIgnoreCase))
        {
            return nonNulls.Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).Sum();
        }
        if (string.Equals(aggregate, "AVG", StringComparison.OrdinalIgnoreCase))
        {
            return nonNulls.Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).Average();
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

        throw new ExecutionException($"Unsupported {algorithm} aggregate function: {aggregate}");
    }

    private sealed class TransformOutputSink : IAsyncDisposable
    {
        private readonly TransformStatement _stmt;
        private readonly IExecutionContext _context;
        private readonly string _algorithm;
        private readonly List<string> _targetColumns;
        private readonly ILogger _logger;
        private readonly IDataSource? _target;
        private DataTable _batch;

        public TableSchema Schema => _batch.Schema;
        public int ProducedRows { get; private set; }

        private TransformOutputSink(
            TransformStatement stmt,
            IExecutionContext context,
            string algorithm,
            List<string> targetColumns,
            ILogger logger,
            IDataSource? target,
            DataTable batch)
        {
            _stmt = stmt;
            _context = context;
            _algorithm = algorithm;
            _targetColumns = targetColumns;
            _logger = logger;
            _target = target;
            _batch = batch;
        }

        public static async Task<TransformOutputSink> CreateAsync(
            TransformStatement stmt,
            IExecutionContext context,
            string algorithm,
            List<string> targetColumns,
            ILogger logger)
        {
            var initialBatch = new DataTable();
            initialBatch.SetColumns(targetColumns);

            if (context.IsWhatIf)
            {
                if (!stmt.TargetTable.TableName.StartsWith("#", StringComparison.Ordinal))
                {
                    _ = await context.ResolveDataSourceAsync(stmt.TargetTable)
                        ?? throw new ExecutionException($"Could not resolve target dataset: {stmt.TargetTable.TableName}");
                }
                return new TransformOutputSink(stmt, context, algorithm, targetColumns, logger, target: null, initialBatch);
            }

            if (stmt.TargetTable.TableName.StartsWith("#", StringComparison.Ordinal) &&
                !context.Connections.ContainsKey(stmt.TargetTable.TableName))
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

            if (target is AppendOnlyColumnDataSource colStore &&
                stmt.TargetTable.TableName.StartsWith("#", StringComparison.Ordinal))
            {
                var existingColumns = (await ((IDataSource)colStore).GetColumnsAsync(context.CancellationToken)).ToList();
                if (!existingColumns.SequenceEqual(targetColumns, StringComparer.OrdinalIgnoreCase))
                {
                    var replacement = new InMemoryDataSource
                    {
                        Validator = context as IDataValidator,
                        ExecutionContext = context,
                        MaxInMemoryBatches = context.MaxInMemoryBatches
                    };
                    replacement.SetSchema(targetColumns.Select(c => new ColumnDefinition(c, "UNKNOWN", false)));
                    context.Connections[stmt.TargetTable.TableName] = replacement;
                    target = replacement;
                }
            }
            else if (target is InMemoryDataSource inMemoryTarget)
            {
                bool schemaDiffers = inMemoryTarget.Schema.Count != targetColumns.Count ||
                    !inMemoryTarget.Schema.Keys.SequenceEqual(targetColumns, StringComparer.OrdinalIgnoreCase);
                if (schemaDiffers)
                {
                    inMemoryTarget.SetSchema(targetColumns.Select(c => new ColumnDefinition(c, "UNKNOWN", false)));
                }
            }

            return new TransformOutputSink(stmt, context, algorithm, targetColumns, logger, target, initialBatch);
        }

        public async Task WriteRowAsync(Row row)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            ProducedRows++;

            if (_context.IsWhatIf) return;

            await _batch.AddRowAsync(row);
            if (_batch.Rows.Count >= _context.EffectiveBatchSize)
            {
                await _target!.WriteBatches(new[] { _batch }.ToAsyncEnumerable(), append: true, _context.CancellationToken);
                _batch = new DataTable();
                _batch.SetColumns(_targetColumns);
            }
        }

        public async Task CompleteAsync()
        {
            if (_context.IsWhatIf)
            {
                _logger.WriteLine(
                    $"WHAT IF: Would write {ProducedRows} rows to {_stmt.TargetTable.TableName} via TRANSFORM {_algorithm}.",
                    ConsoleColor.Yellow);
                return;
            }

            if (_batch.Rows.Count > 0)
            {
                await _target!.WriteBatches(new[] { _batch }.ToAsyncEnumerable(), append: true, _context.CancellationToken);
                _batch = new DataTable();
                _batch.SetColumns(_targetColumns);
            }

            _context.Telemetry.RowsProcessed += ProducedRows;
            _logger.Info("TRANSFORM {Algorithm} complete: {RowCount} rows staged into {Target}",
                _algorithm, ProducedRows, _stmt.TargetTable.TableName);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
