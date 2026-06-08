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

        /// <summary>The rows of a result set after the (case-insensitive substring) row filter.</summary>
        public static IReadOnlyList<ETL_SQL.Data.Row> FilterRows(ETL_SQL.Data.DataTable res, string? filter)
        {
            if (string.IsNullOrEmpty(filter)) return res.Rows;
            return res.Rows.Where(row => res.ColumnNames.Any(c =>
                (row[c]?.ToString() ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        /// <summary>Adjusts a scroll offset so <paramref name="active"/> stays within the visible window.</summary>
        public static int FollowScroll(int scroll, int active, int visible, int total)
        {
            if (total <= 0) return 0;
            scroll = Math.Clamp(scroll, 0, Math.Max(0, total - 1));
            if (active < scroll) return active;
            if (active >= scroll + visible) return active - visible + 1;
            return scroll;
        }

        // A grid cell: NULL shown distinctly from an empty string; newlines flattened and long
        // values clipped (the full value is available via the inspector). Active cell is highlighted.
        private static string FormatCell(object? value, bool isActive)
        {
            const int maxCellLen = 48;
            bool isNull = value == null;
            string raw = isNull ? "NULL" : (value!.ToString() ?? "");
            string flat = raw.Replace("\r", " ").Replace("\n", " ");
            if (flat.Length > maxCellLen) flat = flat.Substring(0, maxCellLen - 1) + "…";
            string escaped = Markup.Escape(flat);

            if (isActive) return $"[black on yellow]{escaped}[/]";
            if (isNull) return $"[grey italic]{escaped}[/]";
            return escaped;
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

            int setIndex = Math.Clamp(_renderer.ActiveResultSetIndex, 0, _evaluator.LastResultSets.Count - 1);
            var res = _evaluator.LastResultSets[setIndex];

            bool hasFilter = !string.IsNullOrEmpty(_renderer.FilterText);
            var rows = FilterRows(res, _renderer.FilterText);

            // Clamp the active cell into the data, then scroll so it stays visible.
            int visibleRowCount = Math.Max(1, height - 4);
            const int visibleColCount = 10;
            int activeRow = rows.Count == 0 ? 0 : Math.Clamp(_renderer.ActiveResultRow, 0, rows.Count - 1);
            int activeCol = res.ColumnNames.Count == 0 ? 0 : Math.Clamp(_renderer.ActiveResultCol, 0, res.ColumnNames.Count - 1);
            _renderer.ActiveResultRow = activeRow;
            _renderer.ActiveResultCol = activeCol;
            _renderer.ResultScrollRow = FollowScroll(_renderer.ResultScrollRow, activeRow, visibleRowCount, rows.Count);
            _renderer.ResultScrollCol = FollowScroll(_renderer.ResultScrollCol, activeCol, visibleColCount, res.ColumnNames.Count);

            string filterInfo = hasFilter
                ? $" | [yellow]Filter: {Markup.Escape(_renderer.FilterText)}  {rows.Count}/{res.Rows.Count}[/]"
                : "";
            string cellInfo = rows.Count > 0 ? $" | [grey]R{activeRow + 1} C{activeCol + 1}[/]" : "";
            string stats = $"[cyan]Set {setIndex + 1}/{_evaluator.LastResultSets.Count} | {res.ExecutionTimeMs}ms | {res.TotalRowsMatched}{(res.TotalRowsMatched >= 1000 ? "+" : "")} rows[/]{filterInfo}{cellInfo}";

            var tableColor = TuiTheme.Instance.GetColor(
                _renderer.ResultsFocus ? TuiTheme.Instance.Ui.PanelFocusedBorder : TuiTheme.Instance.Ui.PanelUnfocusedBorder,
                _renderer.ResultsFocus ? Color.Grey37 : Color.Grey);
            var table = new Table().Border(TableBorder.Rounded).BorderColor(tableColor).Expand();
            int colOffset = _renderer.ResultScrollCol;
            var visibleColumns = res.ColumnNames.Skip(colOffset).Take(visibleColCount).ToList();
            foreach (var col in visibleColumns) table.AddColumn($"[bold cyan]{Markup.Escape(col)}[/]");

            if (rows.Count > 0)
            {
                int start = _renderer.ResultScrollRow;
                int end = Math.Min(start + visibleRowCount, rows.Count);
                for (int i = start; i < end; i++)
                {
                    var row = rows[i];
                    var cells = new string[visibleColumns.Count];
                    for (int c = 0; c < visibleColumns.Count; c++)
                    {
                        bool isActive = _renderer.ResultsFocus && i == activeRow && (colOffset + c) == activeCol;
                        cells[c] = FormatCell(row[visibleColumns[c]], isActive);
                    }
                    table.AddRow(cells);
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
                int    colScroll = renderer.CompareScrollCols.Count > i ? renderer.CompareScrollCols[i] : 0;

                RenderSingleComparePane(console, x, currentY, width, availableHeight, evaluator, i, isFocused, scroll, colScroll, filter);
                currentY += availableHeight;
                if (currentY >= y + height) break;
            }
        }

        /// <summary>Number of columns shown at once in a compare pane.</summary>
        public const int ComparePaneColumns = 10;

        /// <summary>Largest horizontal-scroll offset that still leaves one column visible.</summary>
        public static int MaxColumnOffset(int columnCount) => Math.Max(0, columnCount - 1);

        /// <summary>Clamps a desired horizontal-scroll offset to the valid range for a column count.</summary>
        public static int ClampColumnOffset(int scrollCol, int columnCount) =>
            Math.Clamp(scrollCol, 0, MaxColumnOffset(columnCount));

        private static void RenderSingleComparePane(IConsoleInterface console, int x, int y, int width, int height,
            Evaluator evaluator, int setIndex, bool focused, int scrollRow, int scrollCol, string filter)
        {
            var res = evaluator.LastResultSets[setIndex];

            bool hasFilter = !string.IsNullOrEmpty(filter);
            var rows = hasFilter
                ? res.Rows.Where(row => res.ColumnNames.Any(c =>
                    (row[c]?.ToString() ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList()
                : res.Rows;

            // Horizontal scroll: clamp the offset so at least one column stays visible.
            int colOffset = ClampColumnOffset(scrollCol, res.ColumnNames.Count);
            var visibleColumns = res.ColumnNames.Skip(colOffset).Take(ComparePaneColumns).ToList();
            int lastCol = colOffset + visibleColumns.Count;
            string colInfo = res.ColumnNames.Count > ComparePaneColumns
                ? $" | [grey]cols {colOffset + 1}-{lastCol}/{res.ColumnNames.Count}[/]"
                : "";

            string filterInfo = hasFilter ? $" | [yellow]Filter: {Markup.Escape(filter)}  {rows.Count}/{res.Rows.Count}[/]" : "";
            string focusTag   = focused ? " [bold magenta]◀[/]" : "";
            string header     = $"[cyan]Set {setIndex + 1} | {res.ExecutionTimeMs}ms | {res.TotalRowsMatched}{(res.TotalRowsMatched >= 1000 ? "+" : "")} rows[/]{colInfo}{filterInfo}{focusTag}";

            var table = new Table().Border(TableBorder.Rounded).BorderColor(TuiTheme.Instance.GetColor(TuiTheme.Instance.Ui.PanelUnfocusedBorder, Color.Grey)).Expand();
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
