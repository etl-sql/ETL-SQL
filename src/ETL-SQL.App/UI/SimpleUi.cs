using System;
using System.IO;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.App;

namespace ETL_SQL.UI
{
    public class SimpleUi
    {
        private readonly CliContext _ctx;

        public SimpleUi(CliContext ctx)
        {
            _ctx = ctx;
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
            
            var runCtx = new CliContext
            {
                Command = "run",
                ScriptFile = script,
                BatchSize = _ctx.BatchSize,
                IsPerfMode = true, // Force performance charts on interactive execution
                IsVerbose = _ctx.IsVerbose,
                EstimatedRows = _ctx.EstimatedRows
            };

            await EngineRunner.Run(runCtx);
            
            AnsiConsole.MarkupLine("\n[green]Execution Complete.[/] Press any key to return to menu...");
            Console.ReadKey(true);
            AnsiConsole.Clear();
            AnsiConsole.Write(new FigletText("ETL-SQL").Centered().Color(Color.Green));
            AnsiConsole.MarkupLine("[bold grey]Interactive Simple UI Mode[/]\n");
        }
    }
}
