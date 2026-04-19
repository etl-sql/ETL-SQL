using System;
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
                console.SetCursorPosition(x, y + i);
                console.Write(new string(' ', width));
            }

            if (_evaluator.LastResultSets.Count == 0)
            {
                console.SetCursorPosition(x, y);
                console.WriteWidget(new Rule("[grey]Results[/]").RuleStyle("grey"));
                return;
            }

            var res = _evaluator.LastResultSets[_renderer.ActiveResultSetIndex];
            string stats = $"[cyan]Set {_renderer.ActiveResultSetIndex + 1}/{_evaluator.LastResultSets.Count} | Time: {res.ExecutionTimeMs}ms | Rows: {res.TotalRowsMatched}{(res.TotalRowsMatched >= 1000 ? "+" : "")}[/]";

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Expand();
            var visibleColumns = res.ColumnNames.Skip(_renderer.ResultScrollCol).Take(10).ToList();
            foreach (var col in visibleColumns) table.AddColumn($"[bold cyan]{Markup.Escape(col)}[/]");

            if (res.Rows.Any())
            {
                int start = _renderer.ResultScrollRow;
                int count = Math.Max(1, height - 4);
                int end = Math.Min(start + count, res.Rows.Count);
                for (int i = start; i < end; i++)
                {
                    var row = res.Rows[i];
                    table.AddRow(visibleColumns.Select(c => Markup.Escape(row[c]?.ToString() ?? "")).ToArray());
                }
            }

            var borderColor = _renderer.ResultsFocus ? Color.Yellow : Color.Cyan;
            var panel = new Panel(table) { Header = new PanelHeader(stats), Height = height, Width = width, Border = BoxBorder.Rounded, BorderStyle = new Style(borderColor), Padding = new Padding(0, 0, 0, 0) };
            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }
    }
}
