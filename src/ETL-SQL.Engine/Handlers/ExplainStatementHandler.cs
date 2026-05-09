using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Analysis.Explain;
using ETL_SQL.Data;
using Spectre.Console.Rendering;
using Spectre.Console;

namespace ETL_SQL.Engine.Handlers
{
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
            var columns = new List<string> { "ID", "Operation", "Details", "Cost" };
            if (stmt.IsAnalyze)
            {
                columns.Add("Actual Rows");
                columns.Add("Actual Time (ms)");
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
            if (stmt.IsAnalyze)
            {
                var oldProfiling = context.Telemetry.IsProfiling;
                var oldRedirect = context.RedirectOutput;
                context.Telemetry.IsProfiling = true;
                context.RedirectOutput = true; // Don't print the actual rows to console

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await foreach (var batch in context.ExecuteQuery(stmt.Query))
                    {
                        actualRows += batch.Rows.Count;
                    }
                }
                finally
                {
                    sw.Stop();
                    actualTime = sw.ElapsedMilliseconds;
                    context.Telemetry.IsProfiling = oldProfiling;
                    context.RedirectOutput = oldRedirect;
                }
            }

            await new ExplainPlanBuilder().BuildAsync(stmt.Query, plan, context, metrics);
            
            if (stmt.IsAnalyze)
            {
                // For now, map the total metrics to the final step of the plan
                var lastRow = plan.Rows.LastOrDefault();
                if (lastRow != null)
                {
                    lastRow["Actual Rows"] = actualRows;
                    lastRow["Actual Time (ms)"] = actualTime;
                }
            }

            // Populate the context's profile metrics so the UI Performance tab can see it
            metrics.DurationMs = plan.Rows.Sum(r => Convert.ToInt64(r["Cost"] ?? 0));
            context.Telemetry.ProfileMetrics.Add(metrics);
            
            context.LastResult = plan;
            context.LastResultSets.Add(plan);
            
            if (!context.RedirectOutput)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title(stmt.IsAnalyze ? "[bold yellow]Execution Plan (ANALYZE)[/]" : "[bold yellow]Execution Plan[/]")
                    .AddColumn("ID")
                    .AddColumn("Operation")
                    .AddColumn("Details")
                    .AddColumn("Cost", c => c.RightAligned());

                if (stmt.IsAnalyze)
                {
                    table.AddColumn("Actual Rows", c => c.RightAligned());
                    table.AddColumn("Actual Time", c => c.RightAligned());
                }

                foreach (var row in plan.Rows)
                {
                    if (stmt.IsAnalyze)
                    {
                        table.AddRow(
                            new Text(row["ID"]?.ToString() ?? ""),
                            new Text(row["Operation"]?.ToString() ?? ""),
                            new Text(row["Details"]?.ToString() ?? ""),
                            new Text(row["Cost"]?.ToString() ?? ""),
                            new Text(row["Actual Rows"]?.ToString() ?? "-"),
                            new Text(row["Actual Time (ms)"]?.ToString() ?? "-")
                        );
                    }
                    else
                    {
                        table.AddRow(
                            new Text(row["ID"]?.ToString() ?? ""),
                            new Text(row["Operation"]?.ToString() ?? ""),
                            new Text(row["Details"]?.ToString() ?? ""),
                            new Text(row["Cost"]?.ToString() ?? "")
                        );
                    }
                }
                if (stmt.IntoTable != null)
                {
                    var destination = await context.ResolveDataSourceAsync(stmt.IntoTable);
                    await destination.WriteBatches(new List<DataTable> { plan }.ToAsyncEnumerable());
                    context.Log($"Query plan stored in {stmt.IntoTable.TableName}.");
                }
                else
                {
                    AnsiConsole.Write(table);
                    AnsiConsole.MarkupLine($"[grey]Total Plan Cost:[/] [yellow]{metrics.DurationMs}[/]");
                    if (stmt.IsAnalyze)
                    {
                        AnsiConsole.MarkupLine($"[grey]Total Actual Time:[/] [green]{actualTime}ms[/]");
                        AnsiConsole.MarkupLine($"[grey]Total Actual Rows:[/] [green]{actualRows}[/]");
                    }
                }
            }
        }

    }
}



