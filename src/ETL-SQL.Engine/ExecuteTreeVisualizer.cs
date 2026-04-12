using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Engine
{
    /// <summary>
    /// High-performance visualizer for the graphical execution tree using Spectre.Console.
    /// Lives in Engine so both App (headless executor) and TUI can render execution trees
    /// without a circular project dependency.
    /// </summary>
    public class ExecuteTreeVisualizer
    {
        private readonly ExecutionTree _tree;
        private bool _isDisplayEnabled = true;

        public ExecuteTreeVisualizer(ExecutionTree tree)
        {
            _tree = tree;
        }

        /// <summary>Sets whether the visual HUD should be rendered to the console.</summary>
        public void SetDisplayEnabled(bool enabled) => _isDisplayEnabled = enabled;

        /// <summary>
        /// Starts a live-rendering loop for the execution tree.
        /// Throttled to 10Hz (100ms) to prevent CPU starvation.
        /// </summary>
        public async Task RenderLiveAsync(CancellationToken ct)
        {
            if (!_isDisplayEnabled || Console.IsOutputRedirected)
            {
                return;
            }

            await AnsiConsole.Live(CreateRenderable())
                .AutoClear(false)
                .StartAsync(async ctx =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        ctx.UpdateTarget(CreateRenderable());
                        await Task.Delay(100, ct);
                    }
                });
        }

        /// <summary>Builds the Tree-Table renderable object with support for scrolling.</summary>
        public IRenderable CreateRenderable(int skip = 0, int take = int.MaxValue)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey15)
                .Title("[bold cyan]ETL-SQL Graphical Execution Progress[/]")
                .AddColumn("Pipeline Hierarchy")
                .AddColumn(new TableColumn("Rows").RightAligned())
                .AddColumn(new TableColumn("Time").RightAligned())
                .AddColumn(new TableColumn("Speed").RightAligned());

            int currentRow = 0;
            int count = 0;
            foreach (var rootId in _tree.RootNodeIds)
            {
                var rootNode = _tree.GetNode(rootId);
                if (rootNode != null)
                    AddNodeToTable(table, rootNode, 0, ref currentRow, skip, take, ref count);
            }

            return table;
        }

        private void AddNodeToTable(Table table, ExecutionNode node, int depth, ref int currentRow, int skip, int take, ref int count)
        {
            if (count >= take) return;

            if (currentRow >= skip)
            {
                var statusStyle = GetStatusStyle(node.Status);
                var indent = new string(' ', depth * 2);
                var prefix = depth > 0 ? "└─ " : "";

                var nameMarkup = $"{indent}[grey]{prefix}[/]{statusStyle.Icon} {statusStyle.Color}{Markup.Escape(node.Name)}[/]";

                var elapsed = node.GetElapsedMs();
                var timeStr = elapsed > 1000 ? $"{(elapsed / 1000.0):N1}s" : $"{elapsed:N0}ms";
                var velocity = node.GetVelocity();
                var velStr = velocity > 1000 ? $"{(velocity / 1000.0):N1}k/s" : $"{velocity:N0} r/s";

                table.AddRow(
                    new Markup(nameMarkup),
                    new Markup($"{statusStyle.Color}{node.RowsProcessed:N0}[/]"),
                    new Markup($"[grey]{timeStr}[/]"),
                    new Markup(node.Status == ExecutionStatus.Running ? $"[yellow]{velStr}[/]" : "[grey]--[/]")
                );
                count++;
            }
            currentRow++;

            foreach (var childId in node.ChildIds)
            {
                var child = _tree.GetNode(childId);
                if (child != null)
                    AddNodeToTable(table, child, depth + 1, ref currentRow, skip, take, ref count);
            }
        }

        private (string Icon, string Color) GetStatusStyle(ExecutionStatus status) => status switch
        {
            ExecutionStatus.Waiting   => ("⏳", "[grey]"),
            ExecutionStatus.Running   => ("▶️", "[bold cyan]"),
            ExecutionStatus.Completed => ("✅", "[bold green]"),
            ExecutionStatus.Faulted   => ("❌", "[bold red]"),
            _                         => ("❓", "[white]")
        };
    }
}
