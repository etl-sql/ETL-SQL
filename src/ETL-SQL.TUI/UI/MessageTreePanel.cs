using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using Spectre.Console;

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
            => Render(console, x, y, width, height, treeScroll: 0, msgScroll: scrollRow, focus: EditorFocus.Messages);

        public void Render(IConsoleInterface console, int x, int y, int width, int height, int treeScroll, int msgScroll, EditorFocus focus)
        {
            if (height < 4 || width < 14) return;

            // Clear area first
            for (int i = 0; i < height; i++)
            {
                console.ClearLine(x, y + i, width);
            }

            // Tree column gets ~35% of inner width, capped to keep it readable
            int innerRows = height - 2;   // Panel Top(1) + Bottom(1). Header is on the border.
            int treeColContent = Math.Min(44, Math.Max(18, (width - 6) * 35 / 100));

            // Build tree markup
            var treeLines = _ascii.Render(_evaluator.Telemetry.ExecutionTree);
            var visibleTree = treeLines.Skip(treeScroll).Take(innerRows).ToList();
            string treeMarkup = FormatTreeMarkup(visibleTree, treeColContent);

            // Build message markup
            int msgColWidth = width - treeColContent - 6;

            // Expand messages into wrapped lines
            var allLines = new List<DisplayLine>();
            foreach (var m in _evaluator.Messages)
            {
                var wrapped = WrapText(m.Message, msgColWidth);
                foreach (var lineText in wrapped)
                {
                    allLines.Add(new DisplayLine(lineText, m.Color));
                }
            }

            var visibleLines = allLines.Skip(msgScroll).Take(innerRows).ToList();

            // Safety: if scroll is out of bounds, show the last page
            if (allLines.Count > 0 && !visibleLines.Any() && msgScroll > 0)
            {
                msgScroll = Math.Max(0, allLines.Count - innerRows);
                visibleLines = allLines.Skip(msgScroll).Take(innerRows).ToList();
            }

            string msgMarkup = FormatLinesMarkup(visibleLines);

            // Column headers with scroll indicators and focus highlights
            bool treeFocused = focus == EditorFocus.ExecutionTree;
            bool msgFocused = focus == EditorFocus.Messages;

            string treeHeader = treeFocused ? "[bold cyan]▶ Pipeline[/]" : "[cyan]Pipeline[/]";
            if (treeScroll > 0) treeHeader += $" [grey]↑{treeScroll}[/]";

            string msgHeader = msgFocused ? "[bold yellow]▶ Messages[/]" : "[yellow]Messages[/]";
            if (msgScroll > 0) msgHeader += $" [grey]↑{msgScroll}[/]";
            else if (allLines.Count > innerRows) msgHeader += " [grey]↓[/]";

            var table = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .NoSafeBorder()
                .Width(width - 2) // Accounting for panel borders
                .Expand()
                .AddColumn(new TableColumn("").Width(treeColContent))
                .AddColumn(new TableColumn(""));

            table.AddRow(
                new Markup(string.IsNullOrEmpty(treeMarkup) ? "[grey]No pipeline data.[/]" : treeMarkup),
                new Markup(string.IsNullOrEmpty(msgMarkup) ? "[grey]No messages.[/]" : msgMarkup));

            var panel = new Panel(table)
            {
                Header = new PanelHeader($"{treeHeader} [grey]│[/] {msgHeader}"),
                Height = height,
                Border = TerminalCapabilities.Current.Box(),
                BorderStyle = TuiTheme.Instance.GetStyle(
                    treeFocused || msgFocused ? TuiTheme.Instance.Ui.PanelFocusedBorder : TuiTheme.Instance.Ui.PanelUnfocusedBorder,
                    new Style(treeFocused || msgFocused ? Color.Grey37 : Color.Grey23)),
                Padding = new Padding(0, 0, 0, 0)
            };

            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
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
                    ExecutionStatus.Faulted => ("✗", "[bold red]"),
                    ExecutionStatus.Running => ("●", "[bold blue]"),
                    _ => ("·", "[grey]")
                };

                string labelColor = line.Status switch
                {
                    ExecutionStatus.Faulted => "[red]",
                    ExecutionStatus.Running => "[bold blue]",
                    _ => "[white]"
                };

                // Reserve space for: indent + connector + icon + space + stats
                int prefixLen = line.Indent.Length + line.Connector.Length + 2;
                int statsLen = line.Stats.Length > 0 ? line.Stats.Length + 2 : 0;
                int labelMax = Math.Max(3, colWidth - prefixLen - statsLen);
                string label = Truncate(line.Label, labelMax);

                string indentStr = Markup.Escape(line.Indent + line.Connector);
                string statsStr = line.Stats.Length > 0
                    ? $"  [grey]{Markup.Escape(line.Stats)}[/]"
                    : "";

                sb.Append($"[grey]{indentStr}[/]{iconColor}{icon}[/] {labelColor}{Markup.Escape(label)}[/]{statsStr}");
            }

            return sb.ToString();
        }

        private static string FormatLinesMarkup(List<DisplayLine> lines)
        {
            if (lines.Count == 0) return "";
            return string.Join("\n", lines.Select(l =>
            {
                var colorMarkup = l.Color switch
                {
                    ConsoleColor.Red or ConsoleColor.DarkRed => "[red]",
                    ConsoleColor.Yellow or ConsoleColor.DarkYellow => "[yellow]",
                    ConsoleColor.Green or ConsoleColor.DarkGreen => "[green]",
                    ConsoleColor.Cyan or ConsoleColor.DarkCyan => "[cyan]",
                    ConsoleColor.Blue or ConsoleColor.DarkBlue => "[blue]",
                    ConsoleColor.Gray or ConsoleColor.DarkGray => "[grey]",
                    _ => ""
                };

                string escaped = Markup.Escape(l.Text);
                return string.IsNullOrEmpty(colorMarkup) ? escaped : $"{colorMarkup}{escaped}[/]";
            }));
        }

        private static List<string> WrapText(string text, int width)
        {
            if (width < 1) return new List<string> { text };
            var result = new List<string>();
            var lines = text.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    result.Add("");
                    continue;
                }

                int start = 0;
                while (start < line.Length)
                {
                    int length = Math.Min(width, line.Length - start);
                    result.Add(line.Substring(start, length));
                    start += length;
                }
            }
            return result;
        }

        private record DisplayLine(string Text, ConsoleColor Color);

        private static string Truncate(string s, int max)
        {
            if (max < 1) return "";
            if (s.Length <= max) return s;
            return max > 1 ? s[..(max - 1)] + "…" : s[..max];
        }
    }
}
