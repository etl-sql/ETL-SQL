using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using ETL_SQL.Data;

namespace ETL_SQL.TUI.UI
{
    public class ResultViewer
    {
        private readonly DataTable _table;
        private int _scrollRow = 0;
        private int _maxDisplayRows = 10;

        public ResultViewer(DataTable table)
        {
            _table = table;
        }

        public void View()
        {
            if (_table == null || !_table.Rows.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No results to display.[/]");
                return;
            }

            bool done = false;
            while (!done)
            {
                Console.Clear();
                AnsiConsole.Write(new Rule($"[bold green]Query Results (Rows {_scrollRow + 1} to {Math.Min(_scrollRow + _maxDisplayRows, _table.Rows.Count)} of {_table.Rows.Count})[/]").RuleStyle("green"));
                Console.WriteLine();

                var displayTable = new Table().Border(TableBorder.Rounded);
                foreach (var col in _table.ColumnNames) displayTable.AddColumn(new TableColumn($"[bold blue]{Markup.Escape(col)}[/]").Centered());

                var rows = _table.Rows.Skip(_scrollRow).Take(_maxDisplayRows);
                foreach (var row in rows)
                {
                    displayTable.AddRow(row.Columns.Values.Select(v => v != null ? Markup.Escape(v.ToString()!) : "[grey]NULL[/]").ToArray());
                }

                AnsiConsole.Write(displayTable);
                Console.WriteLine();
                AnsiConsole.MarkupLine("[grey]UP/DOWN: Scroll | ESC/ENTER: Close[/]");

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter) done = true;
                if (key.Key == ConsoleKey.UpArrow && _scrollRow > 0) _scrollRow--;
                if (key.Key == ConsoleKey.DownArrow && _scrollRow + _maxDisplayRows < _table.Rows.Count) _scrollRow++;
            }
        }
    }
}
