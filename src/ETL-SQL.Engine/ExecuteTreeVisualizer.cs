using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Engine;
/// <summary>
/// High-performance visualizer for the graphical execution tree.
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

        while (!ct.IsCancellationRequested)
        {
            Console.WriteLine(CreateTextSnapshot());
            await Task.Delay(100, ct);
        }
    }

    /// <summary>Builds a text snapshot of the execution tree with support for scrolling.</summary>
    public string CreateTextSnapshot(int skip = 0, int take = int.MaxValue)
    {
        var rows = new List<string[]>();
        rows.Add(["Pipeline Hierarchy", "Rows", "Time", "Speed"]);

        int currentRow = 0;
        int count = 0;
        foreach (var rootId in _tree.RootNodeIds)
        {
            var rootNode = _tree.GetNode(rootId);
            if (rootNode != null)
                AddNodeToRows(rows, rootNode, 0, ref currentRow, skip, take, ref count);
        }

        return RenderRows(rows);
    }

    private void AddNodeToRows(List<string[]> rows, ExecutionNode node, int depth, ref int currentRow, int skip, int take, ref int count)
    {
        if (count >= take) return;

        if (currentRow >= skip)
        {
            var statusStyle = GetStatusStyle(node.Status);
            var indent = new string(' ', depth * 2);
            var prefix = depth > 0 ? "└─ " : "";
            var name = $"{indent}{prefix}{statusStyle.Icon} {node.Name}";

            var elapsed = node.GetElapsedMs();
            var timeStr = elapsed > 1000 ? $"{(elapsed / 1000.0):N1}s" : $"{elapsed:N0}ms";
            var velocity = node.GetVelocity();
            var velStr = velocity > 1000 ? $"{(velocity / 1000.0):N1}k/s" : $"{velocity:N0} r/s";

            rows.Add([
                name,
                $"{node.RowsProcessed:N0}",
                timeStr,
                node.Status == ExecutionStatus.Running ? velStr : "--"
            ]);
            count++;
        }
        currentRow++;

        foreach (var childId in node.ChildIds)
        {
            var child = _tree.GetNode(childId);
            if (child != null)
                AddNodeToRows(rows, child, depth + 1, ref currentRow, skip, take, ref count);
        }
    }

    private static string RenderRows(IReadOnlyList<string[]> rows)
    {
        var widths = Enumerable.Range(0, rows[0].Length)
            .Select(index => rows.Max(row => row[index].Length))
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("ETL-SQL Graphical Execution Progress");
        foreach (var row in rows)
            builder.AppendLine("| " + string.Join(" | ", row.Select((value, index) => value.PadRight(widths[index]))) + " |");
        return builder.ToString().TrimEnd();
    }

    private (string Icon, string Color) GetStatusStyle(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Waiting => ("WAIT", ""),
        ExecutionStatus.Running => ("RUN", ""),
        ExecutionStatus.Completed => ("DONE", ""),
        ExecutionStatus.Faulted => ("FAIL", ""),
        _ => ("?", "")
    };
}
