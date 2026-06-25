using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Common;
public record TreeLine(
    string Indent,
    string Connector,
    string Label,
    string Stats,
    ExecutionStatus Status,
    bool IsSummary);

/// <summary>
/// Converts an ExecutionTree into a compact, platform-agnostic list of text lines.
/// No Spectre or terminal dependencies — callers apply color.
/// </summary>
public class ExecutionTreeAsciiRenderer
{
    public int CollapseThreshold { get; }

    public ExecutionTreeAsciiRenderer(int collapseThreshold = 5)
    {
        CollapseThreshold = collapseThreshold;
    }

    public List<TreeLine> Render(ExecutionTree tree)
    {
        var lines = new List<TreeLine>();
        var roots = tree.RootNodeIds
            .Select(id => tree.GetNode(id))
            .Where(n => n != null)
            .Cast<ExecutionNode>()
            .ToList();

        foreach (var root in roots)
            AppendNode(tree, root, "", lines);

        return lines;
    }

    // ── Root node (no connector prefix) ──────────────────────────────────
    private void AppendNode(ExecutionTree tree, ExecutionNode node, string continuation, List<TreeLine> lines)
    {
        var children = Children(tree, node);
        lines.Add(new TreeLine(continuation, "", NodeLabel(node, children), FormatStats(node), node.Status, false));
        AppendChildren(tree, node, children, continuation, lines);
    }

    // ── Non-root node ─────────────────────────────────────────────────────
    private void AppendChildNode(ExecutionTree tree, ExecutionNode node,
                                 string indent, string connector, string childCont, List<TreeLine> lines)
    {
        var children = Children(tree, node);
        lines.Add(new TreeLine(indent, connector, NodeLabel(node, children), FormatStats(node), node.Status, false));
        AppendChildren(tree, node, children, childCont, lines);
    }

    private void AppendChildren(ExecutionTree tree, ExecutionNode parent,
                                List<ExecutionNode> children, string continuation, List<TreeLine> lines)
    {
        if (children.Count == 0) return;

        bool collapse = parent.IsParallelBlock && children.Count > CollapseThreshold;

        if (collapse)
        {
            int showFirst = Math.Min(2, children.Count - 1);
            for (int i = 0; i < showFirst; i++)
                AppendChildNode(tree, children[i], continuation, "├─ ", continuation + "│  ", lines);

            int hiddenCount = children.Count - showFirst - 1;
            if (hiddenCount > 0)
            {
                var hidden = children.Skip(showFirst).Take(hiddenCount).ToList();
                lines.Add(new TreeLine(continuation, "┊  ", BuildSummary(hiddenCount, hidden), "", ExecutionStatus.Waiting, true));
            }

            AppendChildNode(tree, children[^1], continuation, "└─ ", continuation + "   ", lines);
        }
        else
        {
            for (int i = 0; i < children.Count; i++)
            {
                bool isLast = i == children.Count - 1;
                string connector = isLast ? "└─ " : "├─ ";
                string childCont = continuation + (isLast ? "   " : "│  ");
                AppendChildNode(tree, children[i], continuation, connector, childCont, lines);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string NodeLabel(ExecutionNode node, List<ExecutionNode> children) =>
        node.IsParallelBlock ? $"PARALLEL ({children.Count})" : node.Name;

    private static List<ExecutionNode> Children(ExecutionTree tree, ExecutionNode node) =>
        node.ChildIds
            .Select(id => tree.GetNode(id))
            .Where(n => n != null)
            .Cast<ExecutionNode>()
            .ToList();

    private static string BuildSummary(int count, List<ExecutionNode> nodes)
    {
        int running = nodes.Count(n => n.Status == ExecutionStatus.Running);
        int faulted = nodes.Count(n => n.Status == ExecutionStatus.Faulted);
        int completed = nodes.Count(n => n.Status == ExecutionStatus.Completed);
        int waiting = nodes.Count(n => n.Status == ExecutionStatus.Waiting);

        var parts = new List<string>();
        if (running > 0) parts.Add($"{running} ●");
        if (faulted > 0) parts.Add($"{faulted} ✗");
        if (completed > 0) parts.Add($"{completed} ✓");
        if (waiting > 0) parts.Add($"{waiting} ·");

        string desc = parts.Count > 0 ? string.Join(", ", parts) : "all pending";
        return $"... {count} more  ({desc})";
    }

    public static string FormatStats(ExecutionNode node)
    {
        if (node.Status == ExecutionStatus.Waiting) return "";
        if (node.Status == ExecutionStatus.Running)
        {
            var ms = node.GetElapsedMs();
            return ms > 0 ? FormatMs(ms) + "…" : "…";
        }
        var elapsed = node.GetElapsedMs();
        var rows = node.RowsProcessed;
        string t = FormatMs(elapsed);
        return rows > 0 ? $"{t}  {FormatRows(rows)}" : t;
    }

    private static string FormatMs(double ms) =>
        ms >= 60_000 ? $"{ms / 60_000:N1}m" :
        ms >= 1_000 ? $"{ms / 1_000:N1}s" :
                       $"{ms:N0}ms";

    private static string FormatRows(long r) =>
        r >= 1_000_000 ? $"{r / 1_000_000.0:N1}M" :
        r >= 1_000 ? $"{r / 1_000.0:N1}k" :
                         $"{r}r";
}
