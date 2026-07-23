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
/// Handles the GENERATE CALENDAR statement, creating a standard Date/Calendar dimension #temp table.
/// </summary>
public class GenerateCalendarStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;

    public Type SupportedStatementType => typeof(GenerateCalendarStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (GenerateCalendarStatement)statement;

        var startVal = await context.EvaluationContext.EvaluateValue(stmt.StartDate, new Row());
        var endVal = await context.EvaluationContext.EvaluateValue(stmt.EndDate, new Row());

        if (!EvaluationUtils.TryToDateTime(startVal, out var startDate))
        {
            throw new ExecutionException($"Invalid start date for GENERATE CALENDAR: {startVal}");
        }

        if (!EvaluationUtils.TryToDateTime(endVal, out var endDate))
        {
            throw new ExecutionException($"Invalid end date for GENERATE CALENDAR: {endVal}");
        }

        if (endDate < startDate)
        {
            throw new ExecutionException($"End date {endDate:yyyy-MM-dd} cannot be earlier than start date {startDate:yyyy-MM-dd} in GENERATE CALENDAR.");
        }

        // Target DataSource setup
        if (stmt.Target.TableName.StartsWith("#") && !context.Connections.ContainsKey(stmt.Target.TableName))
        {
            context.Connections[stmt.Target.TableName] = new InMemoryDataSource
            {
                Validator = context as IDataValidator,
                ExecutionContext = context,
                MaxInMemoryBatches = context.MaxInMemoryBatches
            };
        }

        var destination = await context.ResolveDataSourceAsync(stmt.Target);
        if (destination == null)
        {
            throw new ExecutionException($"Could not resolve target data source: {stmt.Target.TableName}");
        }

        await destination.TruncateAsync();

        var columns = new[] {
            "DateKey", "Date", "FullDateISO", "Year", "Quarter", "YearQuarter",
            "Month", "MonthName", "MonthShortName", "YearMonth", "Day", "DayOfWeek",
            "DayName", "DayShortName", "DayOfYear", "ISOWeek", "IsWeekend", "IsWeekday",
            "IsMonthStart", "IsMonthEnd", "IsQuarterStart", "IsQuarterEnd", "IsYearStart",
            "IsYearEnd", "FiscalYear", "FiscalQuarter", "RelativeDays"
        };

        var batch = new DataTable();
        batch.SetColumns(columns);

        var today = DateTime.Today;
        int totalRows = 0;
        for (var dt = startDate.Date; dt <= endDate.Date; dt = dt.AddDays(1))
        {
            var row = batch.NewRow();
            int quarter = ((dt.Month - 1) / 3) + 1;
            bool isWeekend = dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday;
            bool isMonthEnd = dt.Day == DateTime.DaysInMonth(dt.Year, dt.Month);

            row["DateKey"] = dt.Year * 10000 + dt.Month * 100 + dt.Day;
            row["Date"] = dt;
            row["FullDateISO"] = dt.ToString("yyyy-MM-dd");
            row["Year"] = dt.Year;
            row["Quarter"] = quarter;
            row["YearQuarter"] = $"{dt.Year}-Q{quarter}";
            row["Month"] = dt.Month;
            row["MonthName"] = dt.ToString("MMMM");
            row["MonthShortName"] = dt.ToString("MMM");
            row["YearMonth"] = dt.Year * 100 + dt.Month;
            row["Day"] = dt.Day;
            row["DayOfWeek"] = (int)dt.DayOfWeek;
            row["DayName"] = dt.ToString("dddd");
            row["DayShortName"] = dt.ToString("ddd");
            row["DayOfYear"] = dt.DayOfYear;
            row["ISOWeek"] = System.Globalization.ISOWeek.GetWeekOfYear(dt);
            row["IsWeekend"] = isWeekend;
            row["IsWeekday"] = !isWeekend;
            row["IsMonthStart"] = dt.Day == 1;
            row["IsMonthEnd"] = isMonthEnd;
            row["IsQuarterStart"] = dt.Day == 1 && (dt.Month == 1 || dt.Month == 4 || dt.Month == 7 || dt.Month == 10);
            row["IsQuarterEnd"] = isMonthEnd && (dt.Month == 3 || dt.Month == 6 || dt.Month == 9 || dt.Month == 12);
            row["IsYearStart"] = dt.Day == 1 && dt.Month == 1;
            row["IsYearEnd"] = dt.Day == 31 && dt.Month == 12;
            row["FiscalYear"] = dt.Year;
            row["FiscalQuarter"] = $"FQ{quarter}";
            row["RelativeDays"] = (dt.Date - today).Days;

            await batch.AddRowAsync(row);
            totalRows++;

            if (batch.Rows.Count >= context.EffectiveBatchSize)
            {
                await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable(), append: true);
                batch = new DataTable();
                batch.SetColumns(columns);
            }
        }

        if (batch.Rows.Count > 0)
        {
            await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable(), append: true);
        }

        context.Telemetry.RowsProcessed += totalRows;
        _logger.Info("Generated CALENDAR table with {RowCount} rows into {Target}", totalRows, stmt.Target.TableName);
    }
}
