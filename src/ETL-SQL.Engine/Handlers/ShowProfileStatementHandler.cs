using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
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
            if (context.ProfileMetrics.Count == 0)
            {
                if (!context.RedirectOutput)
                {
                    AnsiConsole.MarkupLine("[yellow]No profiling data captured. Ensure SET PROFILE ON; is called before your logic.[/]");
                }
                return;
            }

            if (context.RedirectOutput) return; // In JSON mode, we rely on the performance telemetry summary at script end.

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold cyan]Execution Profile[/]")
                .AddColumn("Time")
                .AddColumn("Statement")
                .AddColumn("Rows", c => c.RightAligned())
                .AddColumn("Index", c => c.Centered())
                .AddColumn("Duration (ms)", c => c.RightAligned())
                .AddColumn("Memory (KB)", c => c.RightAligned());

            foreach (var m in context.ProfileMetrics)
            {
                table.AddRow(
                    new Text(m.Timestamp.ToString("HH:mm:ss.fff")),
                    new Text(m.Sql),
                    new Text(m.RowsProcessed.ToString("N0")),
                    !string.IsNullOrEmpty(m.IndexName) ? new Markup($"[green]{Markup.Escape(m.IndexName)}[/]") : new Markup("[grey]--[/]"),
                    new Text(m.DurationMs.ToString("N0")),
                    new Text((m.MemoryDeltaBytes / 1024.0).ToString("N2"))
                );
            }

            long totalTime = context.ProfileMetrics.Sum(m => m.DurationMs);
            table.Caption($"[bold green]Total Script Execution Time: {totalTime:N0}ms[/]");

            AnsiConsole.Write(table);
            await Task.CompletedTask;
        }

        private string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = Math.Abs(bytes);
            for (i = 0; i < Suffix.Length && dblSByte >= 1024; i++, dblSByte /= 1024) { }

            return $"{(bytes < 0 ? "-" : "")}{dblSByte:0.##} {Suffix[i]}";
        }
    }
}
