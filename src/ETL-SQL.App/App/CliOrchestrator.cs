using System;
using System.IO;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Spectre.Console;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.App
{
    public class CliOrchestrator
    {
        // Define options at class level to avoid null lookups in Dispatch
        private static readonly Option<int> BatchSizeOption = new(new[] { "--batch-size", "-b" }, () => 10000, "The size of data chunks to process in memory.");
        private static readonly Option<bool> PerfOption = new(new[] { "--perf", "-p" }, "Display performance metrics after execution.");
        private static readonly Option<bool> VerboseOption = new(new[] { "--verbose", "-v" }, "Print detailed execution tracking.");
        private static readonly Option<string?> LogOption = new(new[] { "--log", "-l" }, () => null, "Enable logging. Optional: specify path/directory.") { Arity = ArgumentArity.ZeroOrOne };
        private static readonly Option<bool> SilentOption = new(new[] { "--silent", "-s" }, "Remove all console messages.");
        private static readonly Option<string?> PreviewOption = new(new[] { "--preview", "-pr" }, "Preview top N results (e.g. 20, 100, *)") { Arity = ArgumentArity.ZeroOrOne };
        private static readonly Option<int> EstimateOption = new(new[] { "--estimate", "-e" }, () => 1000000, "Estimated total rows for progress UI.");
        private static readonly Option<string> PassOption = new(new[] { "--pass" }, "Master password for encryption.");
        private static readonly Option<bool> JsonOption = new(new[] { "--json" }, "Output results and messages in structured JSON format.");
        private static readonly Option<bool> PageOption = new(new[] { "--page", "-pa" }, "Pause and page between multiple result sets in the console.");
        private static readonly Option<string?> SessionOption = new(new[] { "--session" }, "Enable session persistence with the specified session ID.");
        private static readonly Option<string[]> VarOption = new(new[] { "--var", "-d" }, "Inject a variable into the script (e.g. @Name=Value).") { AllowMultipleArgumentsPerToken = true };
        private static readonly Option<bool> ProgressOption = new(new[] { "--progress", "-g" }, "Display real-time graphical execution progress.");
        
        private static readonly Argument<string> RunScriptArg = new("script", "The ETL-SQL script to execute.");
        private static readonly Argument<string> EncryptValueArg = new("value", "The string to encrypt.");
        private static readonly Argument<string> TestValArg = new("testVal", () => "unit", "Test category: unit, integration, etc.");

        public static RootCommand BuildRootCommand(Func<CliContext, Task<int>> handler)
        {
            var rootCommand = new RootCommand("ETL-SQL Engine - Modern Data Integration Tool");

            // 1. RUN Command
            var runCommand = new Command("run", "Execute an ETL-SQL script")
            {
                RunScriptArg,
                BatchSizeOption, PerfOption, VerboseOption, LogOption, SilentOption, PreviewOption, JsonOption, PageOption, SessionOption, VarOption, ProgressOption
            };
            runCommand.SetHandler(async (context) => await Dispatch(context, "run", handler));

            // 2. TEST Command
            var testCommand = new Command("test", "Run internal diagnostics or unit tests")
            {
                TestValArg
            };
            testCommand.SetHandler(async (context) => await Dispatch(context, "test", handler));

            // 4. ENCRYPT Command
            var encryptCommand = new Command("encrypt", "Utility to encrypt a string for secure connections")
            {
                EncryptValueArg,
                PassOption
            };
            encryptCommand.SetHandler(async (context) => await Dispatch(context, "encrypt", handler));

            var generateCommand = new Command("generate", "Generate mock data for testing projects")
            {
                EstimateOption
            };
            generateCommand.SetHandler(async (context) => await Dispatch(context, "generate", handler));

            // 5. SESSION Command
            var sessionCommand = new Command("session", "Manage ad-hoc execution sessions");
            var clearSubcommand = new Command("clear", "Clear a session state")
            {
                new Argument<string>("id", "The session ID to clear")
            };
            clearSubcommand.SetHandler(async (context) => await Dispatch(context, "session-clear", handler));
            sessionCommand.AddCommand(clearSubcommand);

            // 6. UI Command (for REPL and windowed mode)
            var uiCommand = new Command("ui", "Interactive user interface commands");
            var replSubcommand = new Command("repl", "Start the JSON-based REPL protocol for IDE integration")
            {
                BatchSizeOption, PerfOption, VerboseOption, LogOption, JsonOption, SessionOption, VarOption
            };
            replSubcommand.SetHandler(async (context) => await Dispatch(context, "ui-repl", handler));

            var simpleSubcommand = new Command("simple", "Start the simple interactive menu UI")
            {
                BatchSizeOption, VerboseOption
            };
            simpleSubcommand.SetHandler(async (context) => await Dispatch(context, "ui-simple", handler));

            var editSubcommand = new Command("edit", "Start the modern windowed Terminal IDE (default)")
            {
                new Argument<string?>("file", "Optional file to pre-load") { Arity = ArgumentArity.ZeroOrOne },
                BatchSizeOption, VerboseOption, SessionOption
            };
            editSubcommand.SetHandler(async (context) => await Dispatch(context, "ui-edit", handler));

            var oldSubcommand = new Command("old", "Start the legacy Spectre-based console editor")
            {
                new Argument<string?>("file", "Optional file to pre-load") { Arity = ArgumentArity.ZeroOrOne },
                BatchSizeOption, VerboseOption
            };
            oldSubcommand.SetHandler(async (context) => await Dispatch(context, "ui-old", handler));

            uiCommand.AddCommand(replSubcommand);
            uiCommand.AddCommand(simpleSubcommand);
            uiCommand.AddCommand(editSubcommand);
            uiCommand.AddCommand(oldSubcommand);
            
            // 7. DOCTOR Command (Health Check)
            var doctorCommand = new Command("doctor", "Perform a system health check to verify the environment");
            doctorCommand.SetHandler(async (context) => await Dispatch(context, "doctor", handler));

            rootCommand.AddCommand(runCommand);
            rootCommand.AddCommand(testCommand);
            rootCommand.AddCommand(encryptCommand);
            rootCommand.AddCommand(generateCommand);
            rootCommand.AddCommand(sessionCommand);
            rootCommand.AddCommand(uiCommand);
            rootCommand.AddCommand(doctorCommand);

            return rootCommand;
        }

        private static async Task Dispatch(InvocationContext context, string commandName, Func<CliContext, Task<int>> handler)
        {
            var res = context.ParseResult;
            var cliContext = new CliContext
            {
                Command = commandName,
                BatchSize = res.FindResultFor(BatchSizeOption) != null ? res.GetValueForOption(BatchSizeOption) : 10000,
                IsPerfMode = res.FindResultFor(PerfOption) != null && res.GetValueForOption(PerfOption),
                IsVerbose = res.FindResultFor(VerboseOption) != null && res.GetValueForOption(VerboseOption),
                IsSilentMode = res.FindResultFor(SilentOption) != null && res.GetValueForOption(SilentOption),
                EstimatedRows = res.FindResultFor(EstimateOption) != null ? res.GetValueForOption(EstimateOption) : 1000000,
                PreviewVal = res.FindResultFor(PreviewOption) != null ? res.GetValueForOption(PreviewOption) : null,
                Password = res.FindResultFor(PassOption) != null ? res.GetValueForOption(PassOption) : null,
                LogPath = res.FindResultFor(LogOption)?.GetValueOrDefault<string?>() ?? "logs/",
                IsLogMode = res.FindResultFor(LogOption) != null,
                IsJsonMode = res.FindResultFor(JsonOption) != null && res.GetValueForOption(JsonOption),
                EnablePaging = res.FindResultFor(PageOption) != null && res.GetValueForOption(PageOption),
                DisplayProgress = res.FindResultFor(ProgressOption) != null && res.GetValueForOption(ProgressOption)
            };

            if (commandName == "run")
            {
                var input = res.GetValueForArgument(RunScriptArg);
                cliContext.ScriptFile = string.IsNullOrWhiteSpace(input) ? null : new FileInfo(input.Trim('"', '\'', ' '));
            }
            else if (commandName == "encrypt")
            {
                cliContext.EncryptValue = res.GetValueForArgument(EncryptValueArg);
            }
            else if (commandName == "test")
            {
                cliContext.TestVal = res.GetValueForArgument(TestValArg);
            }
            else if (commandName == "session-clear")
            {
                var idArg = res.CommandResult.Children.OfType<ArgumentResult>().FirstOrDefault();
                cliContext.SessionId = idArg?.GetValueOrDefault<string>();
            }
            else if (commandName.StartsWith("ui-"))
            {
                cliContext.UiMode = commandName.Substring(3); // "repl", "simple", "edit", "old"
                // Check if there was a positional file argument
                var fileResult = res.CommandResult.Children.OfType<ArgumentResult>().FirstOrDefault(a => a.Argument.Name == "file");
                if (fileResult != null)
                {
                    var input = fileResult.GetValueOrDefault<string?>();
                    cliContext.ScriptFile = string.IsNullOrWhiteSpace(input) ? null : new FileInfo(input.Trim('"', '\'', ' '));
                }
            }

            cliContext.SessionId ??= res.FindResultFor(SessionOption) != null ? res.GetValueForOption(SessionOption) : null;

            if (res.FindResultFor(VarOption) != null)
            {
                var varArgs = res.GetValueForOption(VarOption);
                foreach (var arg in varArgs ?? Array.Empty<string>())
                {
                    var parts = arg.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].StartsWith("@") ? parts[0] : "@" + parts[0];
                        cliContext.Variables[key] = ParseValue(parts[1]);
                    }
                }
            }

            context.ExitCode = await handler(cliContext);
        }

        private static object? ParseValue(string raw)
        {
            if (int.TryParse(raw, out var i)) return i;
            if (double.TryParse(raw, out var d)) return d;
            if (bool.TryParse(raw, out var b)) return b;
            if (DateTime.TryParse(raw, out var dt)) return dt;
            return raw.Trim('\'', '\"');
        }

        public static void ShowAdvancedHelp()
        {
            AnsiConsole.Write(new FigletText("ETL-SQL").Centered().Color(Color.DeepSkyBlue1));
            AnsiConsole.Write(new Rule("[yellow]ETL-SQL Engine CLI Subcommands[/]").RuleStyle("grey"));
            Console.WriteLine();
            
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            table.AddColumn("[bold yellow]Command[/]");
            table.AddColumn("[bold white]Description[/]");
            table.AddRow($"run [blue]{Markup.Escape("<script>")}[/]", "Execute an ETL script with options like --perf, --log, --batch-size.");
            table.AddRow($"test [blue]{Markup.Escape("<category>")}[/]", "Run unit or integration tests (e.g., unit).");
            table.AddRow($"encrypt [blue]{Markup.Escape("<string>")}[/]", "Securely encrypt connection strings.");
            table.AddRow("generate", "Generate large scale mock data for performance validation.");
            table.AddRow("ui repl", "Start the JSON-based REPL protocol.");
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\nUse [cyan]ETL-SQL {Markup.Escape("<command>")} --help[/] for details on specific options.");
        }
    }

    // CliContext moved to ETL-SQL.Core/CliContext.cs — available via global using ETL_SQL.Core
}
