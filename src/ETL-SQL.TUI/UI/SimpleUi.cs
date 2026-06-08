using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Common;

namespace ETL_SQL.TUI.UI
{
    public class SimpleUi
    {
        private readonly CliContext _ctx;
        private readonly IServiceProvider? _serviceProvider;

        public SimpleUi(CliContext ctx, IServiceProvider? serviceProvider = null)
        {
            _ctx = ctx;
            _serviceProvider = serviceProvider;
        }

        public async Task RunAsync()
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new FigletText("ETL-SQL").Centered().Color(Color.Green));
            AnsiConsole.MarkupLine("[bold grey]Interactive Simple UI Mode[/]\n");
            
            bool running = true;
            FileInfo? currentScript = _ctx.ScriptFile;

            while (running)
            {
                if (currentScript != null && currentScript.Exists)
                {
                    AnsiConsole.MarkupLine($"\n[yellow]Loaded Script:[/] [cyan]{currentScript.FullName}[/]");
                    var executeNow = AnsiConsole.Confirm("Execute loaded script now?");
                    if (executeNow)
                    {
                        await ExecuteScript(currentScript);
                    }
                    currentScript = null; // Clear so it prompts the menu again cleanly
                }
                
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("\n[bold]Select an action:[/]")
                        .PageSize(10)
                        .AddChoices(new[] {
                            "Load Script",
                            "Exit"
                        }));

                if (choice == "Exit")
                {
                    running = false;
                }
                else if (choice == "Load Script")
                {
                    var input = AnsiConsole.Ask<string>("[green]Enter path to script:[/]");
                    var fi = new FileInfo(input.Trim('"'));
                    if (fi.Exists)
                    {
                        currentScript = fi;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]File not found:[/] {fi.FullName}");
                    }
                }
            }
        }

        private async Task ExecuteScript(FileInfo script)
        {
            AnsiConsole.MarkupLine($"[gray]Executing {script.Name}...[/]\n");

            if (_serviceProvider == null)
            {
                AnsiConsole.MarkupLine("[red]No service provider — cannot execute.[/]");
                Console.ReadKey(true);
                return;
            }

            var runCtx = new CliContext
            {
                Command = "run",
                ScriptFile = script,
                BatchSize = _ctx.BatchSize,
                IsVerbose = _ctx.IsVerbose,
            };

            var source = await File.ReadAllTextAsync(script.FullName);
            var logger = _serviceProvider.GetRequiredService<ILogger>();
            await using var session = new ExecutionSession(_serviceProvider, runCtx, logger);
            var result = await session.ExecuteAsync(source);

            if (result.Success)
            {
                foreach (var dataTable in result.ResultsTables)
                {
                    var spectreTable = new Table().Border(TerminalCapabilities.Current.Table());
                    foreach (var col in dataTable.ColumnNames) spectreTable.AddColumn(col);
                    foreach (var row in dataTable.Rows)
                        spectreTable.AddRow(row.Columns.Values.Select(v => v?.ToString() ?? "").ToArray());
                    AnsiConsole.Write(spectreTable);
                }
                AnsiConsole.MarkupLine($"\n[green]OK[/] — {result.ExecutionTimeMs}ms — {result.RowsProcessed:N0} rows");
            }
            else
            {
                foreach (var d in result.Diagnostics)
                    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(d.Message)}");
                AnsiConsole.MarkupLine($"\n[red]FAILED[/] — {result.ExecutionTimeMs}ms");
            }

            AnsiConsole.MarkupLine("\nPress any key to return to menu...");
            Console.ReadKey(true);
            AnsiConsole.Clear();
            AnsiConsole.Write(new FigletText("ETL-SQL").Centered().Color(Color.Green));
            AnsiConsole.MarkupLine("[bold grey]Interactive Simple UI Mode[/]\n");
        }
    }
}
