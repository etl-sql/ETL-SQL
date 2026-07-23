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

        var batch = new DataTable();
        batch.SetColumns(new[] { "DateKey", "Date", "Year", "Quarter", "Month", "MonthName", "Day", "DayOfWeek", "DayName", "IsWeekend", "FiscalYear" });

        int totalRows = 0;
        for (var dt = startDate.Date; dt <= endDate.Date; dt = dt.AddDays(1))
        {
            var row = batch.NewRow();
            row["DateKey"] = dt.Year * 10000 + dt.Month * 100 + dt.Day;
            row["Date"] = dt;
            row["Year"] = dt.Year;
            row["Quarter"] = ((dt.Month - 1) / 3) + 1;
            row["Month"] = dt.Month;
            row["MonthName"] = dt.ToString("MMMM");
            row["Day"] = dt.Day;
            row["DayOfWeek"] = (int)dt.DayOfWeek;
            row["DayName"] = dt.ToString("dddd");
            row["IsWeekend"] = dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday;
            row["FiscalYear"] = dt.Year; // Default fiscal year matches calendar year

            await batch.AddRowAsync(row);
            totalRows++;

            if (batch.Rows.Count >= context.EffectiveBatchSize)
            {
                await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable(), append: true);
                batch = new DataTable();
                batch.SetColumns(new[] { "DateKey", "Date", "Year", "Quarter", "Month", "MonthName", "Day", "DayOfWeek", "DayName", "IsWeekend", "FiscalYear" });
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
