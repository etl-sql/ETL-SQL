using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Spectre.Console;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Lower panel that shows the execution tree on the left and message log on the right.
    /// Replaces the separate MessagePanel + TreePanel pair.
    /// </summary>
    public class MessageTreePanel : IUIComponent
    {
        private readonly Evaluator _evaluator;
        private static readonly ExecutionTreeAsciiRenderer _ascii = new(collapseThreshold: 5);

        public MessageTreePanel(Evaluator evaluator)
        {
            _evaluator = evaluator;
        }

        // IUIComponent: scrollRow drives messages; tree always starts at 0.
        public void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow = 0)
            => Render(console, x, y, width, height, treeScroll: 0, msgScroll: scrollRow);

        public void Render(IConsoleInterface console, int x, int y, int width, int height, int treeScroll, int msgScroll)
        {
            if (height < 4 || width < 14) return;

            // Clear area first
            for (int i = 0; i < height; i++)
            {
                console.SetCursorPosition(x, y + i);
                console.Write(new string(' ', width));
            }

            // Tree column gets ~35% of inner width, capped to keep it readable
            int innerRows = height - 3;   // table header row + top border + bottom border
            int treeColContent = Math.Min(44, Math.Max(18, (width - 6) * 35 / 100));

            // Build tree markup
            var treeLines = _ascii.Render(_evaluator.ExecutionTree);
            var visibleTree = treeLines.Skip(treeScroll).Take(innerRows).ToList();
            string treeMarkup = FormatTreeMarkup(visibleTree, treeColContent);

            // Build message markup
            var visibleMsgs = _evaluator.Messages.Skip(msgScroll).Take(innerRows).ToList();
            string msgMarkup = FormatMsgMarkup(visibleMsgs);

            // Column headers with scroll indicators
            string treeHeader = treeScroll > 0 ? $"[bold cyan]Pipeline[/] [grey]↑{treeScroll}[/]" : "[bold cyan]Pipeline[/]";
            string msgHeader  = msgScroll  > 0 ? $"[yellow]Messages[/] [grey]↑{msgScroll}[/]"    : "[yellow]Messages[/]";

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey23)
                .Width(width)
                .AddColumn(new TableColumn(treeHeader).Width(treeColContent))
                .AddColumn(new TableColumn(msgHeader));

            table.AddRow(
                new Markup(string.IsNullOrEmpty(treeMarkup) ? "[grey]No pipeline data.[/]" : treeMarkup),
                new Markup(string.IsNullOrEmpty(msgMarkup)  ? "[grey]No messages.[/]"      : msgMarkup));

            console.SetCursorPosition(x, y);
            console.WriteWidget(table);
        }

        private static string FormatTreeMarkup(List<TreeLine> lines, int colWidth)
        {
            if (lines.Count == 0) return "";
            var sb = new StringBuilder();

            foreach (var line in lines)
            {
                if (sb.Length > 0) sb.Append('\n');

                if (line.IsSummary)
                {
                    sb.Append($"[grey]{Markup.Escape(line.Indent + line.Connector + line.Label)}[/]");
                    continue;
                }

                var (icon, iconColor) = line.Status switch
                {
                    ExecutionStatus.Completed => ("✓", "[bold green]"),
                    ExecutionStatus.Faulted   => ("✗", "[bold red]"),
                    ExecutionStatus.Running   => ("●", "[bold blue]"),
                    _                         => ("·", "[grey]")
                };

                string labelColor = line.Status switch
                {
                    ExecutionStatus.Faulted => "[red]",
                    ExecutionStatus.Running => "[bold blue]",
                    _                       => "[white]"
                };

                // Reserve space for: indent + connector + icon + space + stats
                int prefixLen = line.Indent.Length + line.Connector.Length + 2;
                int statsLen  = line.Stats.Length > 0 ? line.Stats.Length + 2 : 0;
                int labelMax  = Math.Max(3, colWidth - prefixLen - statsLen);
                string label  = Truncate(line.Label, labelMax);

                string indentStr = Markup.Escape(line.Indent + line.Connector);
                string statsStr  = line.Stats.Length > 0
                    ? $"  [grey]{Markup.Escape(line.Stats)}[/]"
                    : "";

                sb.Append($"[grey]{indentStr}[/]{iconColor}{icon}[/] {labelColor}{Markup.Escape(label)}[/]{statsStr}");
            }

            return sb.ToString();
        }

        private static string FormatMsgMarkup(List<string> messages)
        {
            if (messages.Count == 0) return "";
            return string.Join("\n", messages.Select(m => Markup.Escape(m)));
        }

        private static string Truncate(string s, int max)
        {
            if (max < 1) return "";
            if (s.Length <= max) return s;
            return max > 1 ? s[..(max - 1)] + "…" : s[..max];
        }
    }
}
