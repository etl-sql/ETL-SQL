using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SHOW PROFILE statement, displaying captured performance metrics for previous operations.
    /// </summary>
    public class ShowProfileStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowProfileStatement);
        
        /// <summary>Executes the SHOW PROFILE statement, rendering a detailed performance table.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowProfileStatement)statement;

            if (context.ProfileMetrics.Count == 0)
            {
                if (!context.RedirectOutput && stmt.IntoTable == null)
                {
                    AnsiConsole.MarkupLine("[yellow]No profiling data captured. Ensure SET PROFILE ON; is called before your logic.[/]");
                }
                return;
            }

            // Create a DataTable for potential INTO or redirect
            var dataTable = new DataTable();
            dataTable.AddColumn("Timestamp");
            dataTable.AddColumn("Statement");
            dataTable.AddColumn("RowsProcessed");
            dataTable.AddColumn("IndexUsed");
            dataTable.AddColumn("DurationMs");
            dataTable.AddColumn("MemoryKB");
            dataTable.AddColumn("SpilledBytes");
            dataTable.AddColumn("Partitions");

            foreach (var m in context.ProfileMetrics)
            {
                var row = new Row();
                row["Timestamp"] = m.Timestamp;
                row["Statement"] = m.Sql;
                row["RowsProcessed"] = m.RowsProcessed;
                row["IndexUsed"] = m.IndexName ?? "--";
                row["DurationMs"] = m.DurationMs;
                row["MemoryKB"] = m.MemoryDeltaBytes / 1024.0;
                row["SpilledBytes"] = m.SpilledBytes;
                row["Partitions"] = m.PartitionsCount;
                await dataTable.AddRowAsync(row);
            }

            if (stmt.IntoTable != null)
            {
                if (!context.Connections.ContainsKey(stmt.IntoTable))
                {
                    context.Connections[stmt.IntoTable] = new InMemoryDataSource();
                }
                var destination = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
                await destination.WriteBatches(new[] { dataTable }.ToAsyncEnumerable());
            }
            else if (context.RedirectOutput)
            {
                context.LastResult = dataTable;
            }
            else
            {
                // Console display using Spectre.Console
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title("[bold cyan]Execution Profile[/]")
                    .AddColumn("Time")
                    .AddColumn("Statement")
                    .AddColumn("Rows", c => c.RightAligned())
                    .AddColumn("Index", c => c.Centered())
                    .AddColumn("Duration (ms)", c => c.RightAligned())
                    .AddColumn("Memory (KB)", c => c.RightAligned())
                    .AddColumn("Spilled", c => c.RightAligned())
                    .AddColumn("Parts", c => c.Centered());

                foreach (var row in dataTable.Rows)
                {
                    table.AddRow(
                        new Text(row["Timestamp"] is DateTime dt ? dt.ToString("HH:mm:ss.fff") : row["Timestamp"]?.ToString() ?? ""),
                        new Text(row["Statement"]?.ToString() ?? ""),
                        new Text(Convert.ToInt64(row["RowsProcessed"]).ToString("N0")),
                        row["IndexUsed"]?.ToString() != "--" ? new Markup($"[green]{Markup.Escape(row["IndexUsed"]?.ToString() ?? "")}[/]") : new Markup("[grey]--[/]"),
                        new Text(Convert.ToInt64(row["DurationMs"]).ToString("N0")),
                        new Text(Convert.ToDouble(row["MemoryKB"]).ToString("N2")),
                        new Text(Convert.ToInt64(row["SpilledBytes"]).ToString("N0")),
                        new Text(Convert.ToInt32(row["Partitions"]).ToString())
                    );
                }

                long totalTime = context.ProfileMetrics.Sum(m => m.DurationMs);
                table.Caption($"[bold green]Total Script Execution Time: {totalTime:N0}ms[/]");

                AnsiConsole.Write(table);

                // Session Summary Panel
                var peakMem = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024);
                var cacheHits = context.SubqueryCacheHits;
                var cacheMisses = context.SubqueryCacheMisses;
                var hitRatio = (cacheHits + cacheMisses) > 0 ? (double)cacheHits / (cacheHits + cacheMisses) * 100 : 0;

                var summary = new Table().NoBorder().HideHeaders();
                summary.AddColumn("K"); summary.AddColumn("V");
                summary.AddRow("[cyan]Peak Working Set:[/]", $"[white]{peakMem:N0} MB[/]");
                summary.AddRow("[cyan]Subquery Cache:[/]", $"[white]{cacheHits} hits / {cacheMisses} misses ({hitRatio:N1}% ratio)[/]");
                summary.AddRow("[cyan]Sort Spills:[/]", $"[white]{context.SortSpillCount} runs[/]");
                summary.AddRow("[cyan]Aggregate Groups:[/]", $"[white]{context.AggregateGroupsCount:N0} unique keys[/]");
                summary.AddRow("[cyan]Expansion Ratio:[/]", $"[white]{context.AggregateExpansionRatio:N2}x[/]");

                AnsiConsole.Write(new Panel(summary)
                {
                    Header = new PanelHeader("[bold yellow] Session Performance Summary [/]", Justify.Center),
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(1, 0, 1, 0)
                });
            }

            await Task.CompletedTask;
        }
    }
}
