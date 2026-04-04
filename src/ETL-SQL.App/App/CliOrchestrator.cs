using System;
using System.IO;
using System.CommandLine;
using System.CommandLine.Invocation;
using Spectre.Console;
using ETL_SQL.Common;

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
        
        private static readonly Argument<string> RunScriptArg = new("script", "The ETL-SQL script to execute.");
        private static readonly Argument<string> UiModeArg = new("mode", () => "edit", "UI mode: edit, simple, silent, or verbose");
        private static readonly Argument<string?> UiScriptArg = new("script", () => null, "Optional script to load initially.");
        private static readonly Argument<string> EncryptValueArg = new("value", "The string to encrypt.");
        private static readonly Argument<string> TestValArg = new("testVal", () => "unit", "Test category: unit, integration, etc.");

        public static RootCommand BuildRootCommand(Func<CliContext, Task<int>> handler)
        {
            var rootCommand = new RootCommand("ETL-SQL Engine - Modern Data Integration Tool");

            // 1. RUN Command
            var runCommand = new Command("run", "Execute an ETL-SQL script")
            {
                RunScriptArg,
                BatchSizeOption, PerfOption, VerboseOption, LogOption, SilentOption, PreviewOption, JsonOption, PageOption
            };
            runCommand.SetHandler(async (context) => await Dispatch(context, "run", handler));

            // 2. UI Command
            var uiCommand = new Command("ui", "Launch the advanced console UI")
            {
                UiModeArg,
                UiScriptArg,
                EstimateOption,
                LogOption
            };
            uiCommand.SetHandler(async (context) => await Dispatch(context, "ui", handler));

            // 3. TEST Command
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

            rootCommand.AddCommand(runCommand);
            rootCommand.AddCommand(uiCommand);
            rootCommand.AddCommand(testCommand);
            rootCommand.AddCommand(encryptCommand);
            rootCommand.AddCommand(generateCommand);

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
                EnablePaging = res.FindResultFor(PageOption) != null && res.GetValueForOption(PageOption)
            };

            if (commandName == "run")
            {
                var input = res.GetValueForArgument(RunScriptArg);
                cliContext.ScriptFile = string.IsNullOrWhiteSpace(input) ? null : new FileInfo(input.Trim('"', '\'', ' '));
            }
            else if (commandName == "ui")
            {
                cliContext.UiMode = res.GetValueForArgument(UiModeArg);
                var input = res.GetValueForArgument(UiScriptArg);
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

            context.ExitCode = await handler(cliContext);
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
            table.AddRow($"ui [blue]{Markup.Escape("edit|simple|silent|verbose")}[/] [blue]{Markup.Escape("[script]")}[/]", "Launch interactive editor, simple REPL, or headless logging modes.");
            table.AddRow($"test [blue]{Markup.Escape("<category>")}[/]", "Run unit or integration tests (e.g., unit).");
            table.AddRow($"encrypt [blue]{Markup.Escape("<string>")}[/]", "Securely encrypt connection strings.");
            table.AddRow("generate", "Generate large scale mock data for performance validation.");
            
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\nUse [cyan]ETL-SQL {Markup.Escape("<command>")} --help[/] for details on specific options.");
        }
    }

    public class CliContext
    {
        public string Command { get; set; } = "run";
        public FileInfo? ScriptFile { get; set; }
        public bool IsPerfMode { get; set; }
        public int BatchSize { get; set; }
        public bool IsGenerateMode => Command == "generate";
        public bool IsLogMode { get; set; }
        public string? LogPath { get; set; }
        public bool IsSilentMode { get; set; }
        public string? UiMode { get; set; }
        public int EstimatedRows { get; set; }
        public bool IsVerbose { get; set; }
        public string? TestVal { get; set; }
        public bool IsTestMode => Command == "test";
        public string? PreviewVal { get; set; }
        public string? DocsVal { get; set; }
        public string? Password { get; set; }
        public string? EncryptValue { get; set; }
        public bool IsJsonMode { get; set; }
        public bool EnablePaging { get; set; }
    }
}
