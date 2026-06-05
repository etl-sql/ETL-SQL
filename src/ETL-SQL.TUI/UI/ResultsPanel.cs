using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using ETL_SQL.Core;

namespace ETL_SQL.TUI.UI
{
    public class ResultsPanel : IUIComponent
    {
        private readonly Evaluator _evaluator;
        private readonly EditorRenderer _renderer;

        public ResultsPanel(Evaluator evaluator, EditorRenderer renderer)
        {
            _evaluator = evaluator;
            _renderer = renderer;
        }

        public void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow = 0)
        {
            for (int i = 0; i < height; i++)
            {
                console.ClearLine(x, y + i, width);
            }

            if (_evaluator.LastResultSets.Count == 0)
            {
                console.SetCursorPosition(x, y);
                console.WriteWidget(new Rule("[grey]Results[/]").RuleStyle("grey"));
                return;
            }

            var res = _evaluator.LastResultSets[_renderer.ActiveResultSetIndex];

            // Apply filter
            bool hasFilter = !string.IsNullOrEmpty(_renderer.FilterText);
            var rows = hasFilter
                ? res.Rows.Where(row => res.ColumnNames.Any(c =>
                    (row[c]?.ToString() ?? "").Contains(_renderer.FilterText, StringComparison.OrdinalIgnoreCase)))
                  .ToList()
                : res.Rows;

            string filterInfo = hasFilter
                ? $" | [yellow]Filter: {Markup.Escape(_renderer.FilterText)}  {rows.Count}/{res.Rows.Count}[/]"
                : "";
            string stats = $"[cyan]Set {_renderer.ActiveResultSetIndex + 1}/{_evaluator.LastResultSets.Count} | {res.ExecutionTimeMs}ms | {res.TotalRowsMatched}{(res.TotalRowsMatched >= 1000 ? "+" : "")} rows[/]{filterInfo}";

            var tableColor = TuiTheme.Instance.GetColor(
                _renderer.ResultsFocus ? TuiTheme.Instance.Ui.PanelFocusedBorder : TuiTheme.Instance.Ui.PanelUnfocusedBorder, 
                _renderer.ResultsFocus ? Color.Grey37 : Color.Grey);
            var table = new Table().Border(TableBorder.Rounded).BorderColor(tableColor).Expand();
            var visibleColumns = res.ColumnNames.Skip(_renderer.ResultScrollCol).Take(10).ToList();
            foreach (var col in visibleColumns) table.AddColumn($"[bold cyan]{Markup.Escape(col)}[/]");

            if (rows.Any())
            {
                int start = _renderer.ResultScrollRow;
                int count = Math.Max(1, height - 4);
                int end = Math.Min(start + count, rows.Count);
                for (int i = start; i < end; i++)
                {
                    var row = rows[i];
                    table.AddRow(visibleColumns.Select(c => Markup.Escape(row[c]?.ToString() ?? "")).ToArray());
                }
            }

            var borderStyleStr = hasFilter 
                ? TuiTheme.Instance.Ui.ResultsFocusedBorder 
                : (_renderer.ResultsFocus ? TuiTheme.Instance.Ui.ResultsFocusedBorder : TuiTheme.Instance.Ui.ResultsUnfocusedBorder);
            var borderStyle = TuiTheme.Instance.GetStyle(borderStyleStr, new Style(hasFilter ? Color.Yellow : (_renderer.ResultsFocus ? Color.Yellow : Color.Cyan)));
            var panel = new Panel(table) { Header = new PanelHeader(stats), Height = height, Width = width, Border = BoxBorder.Rounded, BorderStyle = borderStyle, Padding = new Padding(0, 0, 0, 0) };
            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }

        public void RenderCompare(IConsoleInterface console, int x, int y, int width, int height, Evaluator evaluator, EditorRenderer renderer)
        {
            // Clear area
            for (int i = 0; i < height; i++)
            {
                console.ClearLine(x, y + i, width);
            }

            int count = evaluator.LastResultSets.Count;
            if (count == 0)
            {
                console.SetCursorPosition(x, y);
                console.WriteWidget(new Rule("[grey]No results to compare[/]").RuleStyle("grey"));
                return;
            }

            // Divide height evenly; each pane gets at least 4 rows (border + header + 1 data + border)
            int paneHeight = Math.Max(4, height / count);
            int currentY = y;

            for (int i = 0; i < count; i++)
            {
                int availableHeight = (i == count - 1)
                    ? Math.Max(4, y + height - currentY)   // last pane gets remainder
                    : paneHeight;

                bool isFocused = i == renderer.CompareFocusIndex;
                string filter  = renderer.CompareFilters.Count  > i ? renderer.CompareFilters[i]  : "";
                int    scroll  = renderer.CompareScrollRows.Count > i ? renderer.CompareScrollRows[i] : 0;

                RenderSingleComparePane(console, x, currentY, width, availableHeight, evaluator, i, isFocused, scroll, filter);
                currentY += availableHeight;
                if (currentY >= y + height) break;
            }
        }

        private static void RenderSingleComparePane(IConsoleInterface console, int x, int y, int width, int height,
            Evaluator evaluator, int setIndex, bool focused, int scrollRow, string filter)
        {
            var res = evaluator.LastResultSets[setIndex];

            bool hasFilter = !string.IsNullOrEmpty(filter);
            var rows = hasFilter
                ? res.Rows.Where(row => res.ColumnNames.Any(c =>
                    (row[c]?.ToString() ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList()
                : res.Rows;

            string filterInfo = hasFilter ? $" | [yellow]Filter: {Markup.Escape(filter)}  {rows.Count}/{res.Rows.Count}[/]" : "";
            string focusTag   = focused ? " [bold magenta]◀[/]" : "";
            string header     = $"[cyan]Set {setIndex + 1} | {res.ExecutionTimeMs}ms | {res.TotalRowsMatched}{(res.TotalRowsMatched >= 1000 ? "+" : "")} rows[/]{filterInfo}{focusTag}";

            var table = new Table().Border(TableBorder.Rounded).BorderColor(TuiTheme.Instance.GetColor(TuiTheme.Instance.Ui.PanelUnfocusedBorder, Color.Grey)).Expand();
            var visibleColumns = res.ColumnNames.Take(10).ToList();
            foreach (var col in visibleColumns)
                table.AddColumn($"[bold cyan]{Markup.Escape(col)}[/]");

            int dataRows = Math.Max(1, height - 4);
            int start    = scrollRow;
            int end      = Math.Min(start + dataRows, rows.Count);
            for (int i = start; i < end; i++)
            {
                var row = rows[i];
                table.AddRow(visibleColumns.Select(c => Markup.Escape(row[c]?.ToString() ?? "")).ToArray());
            }

            var borderStyle = TuiTheme.Instance.GetStyle(
                focused ? TuiTheme.Instance.Ui.CompareFocusedBorder : TuiTheme.Instance.Ui.CompareUnfocusedBorder,
                new Style(focused ? Color.Magenta : Color.Grey23));
            var panel = new Panel(table)
            {
                Header = new PanelHeader(header),
                Height = height,
                Width  = width,
                Border = BoxBorder.Rounded,
                BorderStyle = borderStyle,
                Padding = new Padding(0, 0, 0, 0)
            };

            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }
    }
}
