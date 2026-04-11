using System;
using System.Linq;
using Spectre.Console;
using ETL_SQL.Core;

namespace ETL_SQL.TUI.UI
{
    public class PerformancePanel : IUIComponent
    {
        private readonly Evaluator _evaluator;

        public PerformancePanel(Evaluator evaluator)
        {
            _evaluator = evaluator;
        }

        public void Render(IConsoleInterface console, int x, int y, int width, int height)
        {
            for (int i = 0; i < height; i++)
            {
                console.SetCursorPosition(x, y + i);
                console.Write(new string(' ', width));
            }

            if (_evaluator.ProfileMetrics.Count == 0)
            {
                console.SetCursorPosition(x, y);
                console.WriteWidget(new Rule("[grey]No performance metrics recorded.[/]").RuleStyle("grey"));
                return;
            }

            var lastMetrics = _evaluator.ProfileMetrics.Last();

            var layoutTable = new Table().NoBorder().Expand();
            layoutTable.AddColumn("Chart");
            layoutTable.AddColumn("Details");

            // 1. Timing Breakdown Chart
            var chart = new BreakdownChart()
                .Width(Math.Max(10, width / 2 - 5))
                .AddItem("Exec", lastMetrics.DurationMs, Color.Green)
                .AddItem("Mem Delta", Math.Abs(lastMetrics.MemoryDeltaBytes) / 1024, Color.Blue);

            // 2. Telemetry Table
            var statsTable = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            statsTable.AddColumn("Metric");
            statsTable.AddColumn("Value");
            statsTable.AddRow("Rows/s", (lastMetrics.DurationMs > 0 ? (lastMetrics.RowsProcessed * 1000 / lastMetrics.DurationMs) : 0).ToString("N0"));
            statsTable.AddRow("Memory", $"{Math.Round((double)GC.GetTotalMemory(false) / (1024 * 1024), 2)} MB");
            
            if (lastMetrics.SpilledBytes > 0)
                statsTable.AddRow("Disk Spilled", $"[yellow]{Math.Round((double)lastMetrics.SpilledBytes / (1024 * 1024), 2)} MB[/]");
            
            if (lastMetrics.PartitionsCount > 0)
                statsTable.AddRow("Partitions", lastMetrics.PartitionsCount.ToString());

            if (lastMetrics.RecursiveDepth > 0)
                statsTable.AddRow("Recursion Depth", lastMetrics.RecursiveDepth.ToString());

            layoutTable.AddRow(chart, statsTable);

            var panel = new Panel(layoutTable)
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
