using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Explain;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the EXPLAIN statement, generating and displaying a high-level execution plan for a given query.
/// </summary>
public class ExplainStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ExplainStatement);
    /// <summary>Executes the EXPLAIN statement, building a plan table and displaying it via Spectre.Console.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ExplainStatement)statement;

        var plan = new DataTable();
        var columns = new List<string> { "ID", "Operation", "Details", "Cost", "Mode", "Est. Rows" };
        if (stmt.IsAnalyze)
        {
            columns.Add("Actual Rows");
            columns.Add("Actual Time (ms)");
            columns.Add("Spill Bytes");
            columns.Add("Spill Count");
        }
        plan.SetColumns(columns.ToArray());

        var metrics = new ExecutionMetrics
        {
            Sql = (stmt.IsAnalyze ? "EXPLAIN ANALYZE: " : "EXPLAIN: ") + stmt.Query.ToSql(),
            Timestamp = DateTime.Now
        };

        // If ANALYZE, run the actual query first to collect metrics
        long actualRows = 0;
        long actualTime = 0;
        long spillBytes = 0;
        int spillCount = 0;
        if (stmt.IsAnalyze)
        {
            var oldProfiling = context.Telemetry.IsProfiling;
            var oldRedirect = context.RedirectOutput;
            context.Telemetry.IsProfiling = true;
            context.RedirectOutput = true;

            long spillBefore = context.Telemetry.TotalSpilledBytes;
            int sortSpillBefore = context.Telemetry.SortSpillCount;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await foreach (var batch in context.ExecuteQuery(stmt.Query))
                    actualRows += batch.Rows.Count;
            }
            finally
            {
                sw.Stop();
                actualTime = sw.ElapsedMilliseconds;
                spillBytes = context.Telemetry.TotalSpilledBytes - spillBefore;
                spillCount = context.Telemetry.SortSpillCount - sortSpillBefore;
                context.Telemetry.IsProfiling = oldProfiling;
                context.RedirectOutput = oldRedirect;
            }
        }

        await new ExplainPlanBuilder().BuildAsync(stmt.Query, plan, context, metrics);

        if (stmt.IsAnalyze)
        {
            // Initialize ANALYZE columns on all rows.
            foreach (var planRow in plan.Rows)
            {
                planRow["Actual Rows"] = "--";
                planRow["Actual Time (ms)"] = "--";
                planRow["Spill Bytes"] = 0L;
                planRow["Spill Count"] = 0;
            }

            // Assign total elapsed time and row count to the last plan row.
            var lastRow = plan.Rows.LastOrDefault();
            if (lastRow != null)
            {
                lastRow["Actual Rows"] = actualRows;
                lastRow["Actual Time (ms)"] = actualTime;
            }

            // Assign spill stats to the Sort row; fall back to last row if no Sort present.
            if (spillBytes > 0 || spillCount > 0)
            {
                var sortRow = plan.Rows.LastOrDefault(r => r["Operation"]?.ToString() == "Sort")
                              ?? lastRow;
                if (sortRow != null)
                {
                    sortRow["Spill Bytes"] = spillBytes;
                    sortRow["Spill Count"] = spillCount;
                }
            }
        }

        // Populate the context's profile metrics so the UI Performance tab can see it
        metrics.DurationMs = plan.Rows.Sum(r => Convert.ToInt64(r["Cost"] ?? 0));
        context.Telemetry.ProfileMetrics.Add(metrics);

        context.LastResult = plan;
        context.LastResultSets.Add(plan);

        if (stmt.IntoTable != null)
        {
            var destination = await context.ResolveDataSourceAsync(stmt.IntoTable);
            await destination.WriteBatches(new List<DataTable> { plan }.ToAsyncEnumerable());
            context.Log($"Query plan stored in {stmt.IntoTable.TableName}.");
        }
        else
        {
            context.OnResultSet?.Invoke(plan);
            if (!context.RedirectOutput)
            {
                ResultFormatter.PrintTable(plan);
                context.Log($"Total Plan Cost: {metrics.DurationMs}", ConsoleColor.Yellow);
                if (stmt.IsAnalyze)
                {
                    context.Log($"Total Actual Time: {actualTime}ms", ConsoleColor.Green);
                    context.Log($"Total Actual Rows: {actualRows}", ConsoleColor.Green);
                }
            }
        }
    }

}



