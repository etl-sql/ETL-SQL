using System;
using System.Linq;
using Spectre.Console;
using ETL_SQL.Core;

namespace ETL_SQL.TUI.UI
{
    public class PerformancePanel : IUIComponent
    {
        private readonly Evaluator _evaluator;
        private readonly EditorRenderer _renderer;

        public PerformancePanel(Evaluator evaluator, EditorRenderer renderer)
        {
            _evaluator = evaluator;
            _renderer = renderer;
        }

        public void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow = 0)
        {
            for (int i = 0; i < height; i++)
            {
                console.SetCursorPosition(x, y + i);
                console.Write(new string(' ', width));
            }

            if (height < 12)
            {
                console.SetCursorPosition(x, y + height / 2);
                console.WriteWidget(new Markup("[yellow]Viewing window too small. Use Ctrl+M to maximize.[/]").Centered());
                return;
            }

            var lastMetrics = _evaluator.ProfileMetrics.Count > 0 ? _evaluator.ProfileMetrics.Last() : null;

            var layoutTable = new Table().NoBorder().Expand();
            layoutTable.AddColumn("Chart");
            layoutTable.AddColumn("Details");

            // 1. Timing Breakdown Chart
            var chart = new BreakdownChart()
                .Width(Math.Max(10, width / 2 - 5))
                .AddItem("Exec", lastMetrics?.DurationMs ?? 0, Color.Green)
                .AddItem("Mem Delta", Math.Abs(lastMetrics?.MemoryDeltaBytes ?? 0) / 1024, Color.Blue);

            // 2. Telemetry Table
            var statsTable = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            statsTable.AddColumn("Metric");
            statsTable.AddColumn("Value");
            
            // Script-level performance metrics
            statsTable.AddRow("Lexing", $"{_evaluator.LastLexTimeMs} ms");
            statsTable.AddRow("Parsing", $"{_evaluator.LastParseTimeMs} ms");
            statsTable.AddRow("Core Exec", $"{_evaluator.LastExecTimeMs} ms");

            // Overall script-level Rows/s calculation
            long totalRows = _evaluator.ProfileMetrics.Sum(m => m.RowsProcessed);
            long scriptRps = 0;
            if (_evaluator.LastExecTimeMs > 0)
                scriptRps = totalRows * 1000 / _evaluator.LastExecTimeMs;
            
            statsTable.AddRow("Rows/s", $"[green]{scriptRps:N0}[/]");
            
            // Show peak memory consistent with @@PEAK_MEMORY_MB
            double peakMem = Math.Round((double)System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024), 2);
            statsTable.AddRow("Memory (Peak)", $"{peakMem} MB");
            
            if (lastMetrics != null && lastMetrics.SpilledBytes > 0)
                statsTable.AddRow("Disk Spilled", $"[yellow]{Math.Round((double)lastMetrics.SpilledBytes / (1024 * 1024), 2)} MB[/]");
            
            if (lastMetrics != null && lastMetrics.PartitionsCount > 0)
                statsTable.AddRow("Partitions", lastMetrics.PartitionsCount.ToString());

            if (lastMetrics != null && lastMetrics.RecursiveDepth > 0)
                statsTable.AddRow("Recursion Depth", lastMetrics.RecursiveDepth.ToString());

            layoutTable.AddRow(chart, statsTable);

            // 3. Execution Profile (Table of all statements)
            var profileTable = new Table().Border(TableBorder.None).Expand();
            profileTable.AddColumn("Time");
            profileTable.AddColumn("Statement");
            profileTable.AddColumn(new TableColumn("Rows").RightAligned());
            profileTable.AddColumn(new TableColumn("Dur").RightAligned());
            profileTable.AddColumn(new TableColumn("Mem").RightAligned());

            if (_evaluator.ProfileMetrics.Count == 0)
            {
                profileTable.AddRow(new Markup("[grey]No statement-level metrics recorded yet (Wait for query completion).[/]"), new Text(""), new Text(""), new Text(""), new Text(""));
            }
            else
            {
                int tableHeight = Math.Max(1, height - 10);
                var visibleMetrics = _evaluator.ProfileMetrics
                    .Skip(_renderer.ResultScrollRow)
                    .Take(tableHeight)
                    .ToList();

                foreach (var m in visibleMetrics)
                {
                    profileTable.AddRow(
                        new Markup($"[grey]{m.Timestamp:HH:mm:ss}[/]"),
                        new Markup(Markup.Escape(m.Sql.Length > 40 ? m.Sql.Substring(0, 37) + "..." : m.Sql)),
                        new Markup($"[cyan]{m.RowsProcessed:N0}[/]"),
                        new Markup($"[green]{m.DurationMs:N0}ms[/]"),
                        new Markup($"[blue]{(m.MemoryDeltaBytes / 1024.0):N1}K[/]")
                    );
                }
            }

            var rootTable = new Table().NoBorder().Expand();
            rootTable.AddColumn("Main");
            rootTable.AddRow(layoutTable);
            rootTable.AddRow(new Rule("[grey]Detailed Execution Profile[/]"));
            rootTable.AddRow(profileTable);

            var panel = new Panel(rootTable)
            {
                Header = new PanelHeader("[yellow]Performance Dashboard[/]"),
                Height = height,
                Width = width,
                Border = BoxBorder.Rounded
            };

            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }
    }
}
