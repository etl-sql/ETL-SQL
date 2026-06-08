using System;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// The "Variables" bottom-pane view (VS Code Variable Explorer parity): the script's
    /// variables with type and value, scrollable. Secret/sensitive values are masked.
    /// </summary>
    public class VariablesPanel
    {
        private readonly Evaluator _evaluator;
        private readonly EditorRenderer _renderer;

        public VariablesPanel(Evaluator evaluator, EditorRenderer renderer)
        {
            _evaluator = evaluator;
            _renderer = renderer;
        }

        public void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow, bool focused)
        {
            for (int i = 0; i < height; i++) console.ClearLine(x, y + i, width);

            var vars = _evaluator.VarContext.GetVariablesWithMetadata()
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var borderColor = TuiTheme.Instance.GetColor(
                focused ? TuiTheme.Instance.Ui.PanelFocusedBorder : TuiTheme.Instance.Ui.PanelUnfocusedBorder,
                focused ? Color.Yellow : Color.Grey);

            IRenderable content;
            if (vars.Count == 0)
            {
                content = new Markup("[grey]No variables yet. Run a script that declares or sets variables (e.g. DECLARE @x = 1).[/]");
            }
            else
            {
                var table = new Table().Border(TerminalCapabilities.Current.Table()).BorderColor(borderColor).Expand();
                table.AddColumn("[bold cyan]Name[/]");
                table.AddColumn("[bold cyan]Type[/]");
                table.AddColumn("[bold cyan]Value[/]");

                int visible = Math.Max(1, height - 4);
                int start = Math.Clamp(scrollRow, 0, Math.Max(0, vars.Count - visible));
                foreach (var kv in vars.Skip(start).Take(visible))
                {
                    var (value, meta) = kv.Value;
                    string type = !string.IsNullOrEmpty(meta.DataType) ? meta.DataType! : value?.GetType().Name ?? "null";
                    string display = (meta.IsSecret || meta.IsSensitive) ? "••••••" : FormatValue(value, Math.Max(10, width / 2));
                    table.AddRow(Markup.Escape(kv.Key), $"[grey]{Markup.Escape(type)}[/]", Markup.Escape(display));
                }
                content = table;
            }

            var panel = new Panel(content)
            {
                Header = new PanelHeader($"[bold]Variables ({vars.Count})[/]"),
                Width = width,
                Height = height,
                Border = TerminalCapabilities.Current.Box(),
                Padding = new Padding(1, 0, 1, 0)
            };
            panel.BorderColor(borderColor);

            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }

        // NULL is shown distinctly from an empty string; newlines flattened; long values truncated.
        private static string FormatValue(object? value, int max)
        {
            if (value == null) return "NULL";
            string s = (value.ToString() ?? "").Replace("\r", " ").Replace("\n", " ");
            if (s.Length > max) s = s.Substring(0, Math.Max(1, max - 1)) + "…";
            return s;
        }
    }
}
