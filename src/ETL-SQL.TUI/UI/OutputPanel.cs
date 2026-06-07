using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ETL_SQL.TUI.UI
{
    public enum OutputKind { Server, Pdf, Markdown, Csv, File, Portal }

    /// <summary>A durable location the TUI produced (a served URL or an exported file path).</summary>
    public sealed record OutputEntry(OutputKind Kind, string Location, DateTime Time)
    {
        public bool IsUrl =>
            Location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            Location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        public string KindLabel => Kind switch
        {
            OutputKind.Server   => "Serve",
            OutputKind.Pdf      => "PDF",
            OutputKind.Markdown => "Markdown",
            OutputKind.Csv      => "CSV",
            OutputKind.Portal   => "Portal",
            _                   => "File"
        };
    }

    /// <summary>
    /// The "Output" bottom-pane view: a persistent list of served URLs and export paths.
    /// URLs render as clickable hyperlinks; the focused list supports select/open/copy.
    /// </summary>
    public class OutputPanel
    {
        public void Render(IConsoleInterface console, int x, int y, int width, int height,
            IReadOnlyList<OutputEntry> entries, int selectedIndex, int scrollRow, bool focused)
        {
            for (int i = 0; i < height; i++) console.ClearLine(x, y + i, width);

            var rows = new List<IRenderable>();
            if (entries.Count == 0)
            {
                rows.Add(new Markup("[grey]No output yet. Serve or export a report and its location shows here.[/]"));
            }
            else
            {
                int visible = Math.Max(1, height - 3);
                int start = Math.Clamp(scrollRow, 0, Math.Max(0, entries.Count - visible));
                int last = Math.Min(entries.Count, start + visible);
                for (int i = start; i < last; i++)
                {
                    var e = entries[i];
                    string time = e.Time.ToString("HH:mm");
                    string kind = e.KindLabel.PadRight(9);
                    string loc = e.IsUrl
                        ? $"[link={e.Location}][underline blue]{Markup.Escape(e.Location)}[/][/]"
                        : Markup.Escape(e.Location);
                    string marker = i == selectedIndex ? "[bold yellow]▶[/]" : " ";
                    rows.Add(new Markup($"{marker} [grey]{time}[/] [cyan]{kind}[/] {loc}"));
                }
            }
            rows.Add(new Markup("[grey]↑↓ select · Enter open · c copy[/]"));

            var borderColor = TuiTheme.Instance.GetColor(
                focused ? TuiTheme.Instance.Ui.PanelFocusedBorder : TuiTheme.Instance.Ui.PanelUnfocusedBorder,
                focused ? Color.Yellow : Color.Grey);

            var panel = new Panel(new Rows(rows))
            {
                Header = new PanelHeader("[bold]Output[/]"),
                Width = width,
                Height = height,
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
            panel.BorderColor(borderColor);

            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }
    }
}
