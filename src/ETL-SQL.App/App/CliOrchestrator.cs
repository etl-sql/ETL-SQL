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
        private static readonly Option<int> BatchSizeOption = new("--batch-size", new[] { "-b" })
        {
            Description = "The size of data chunks to process in memory.",
            DefaultValueFactory = _ => 10000
        };
        private static readonly Option<bool> PerfOption = new("--perf", new[] { "-p" })
        {
            Description = "Display performance metrics after execution."
        };
        private static readonly Option<bool> VerboseOption = new("--verbose", new[] { "-v" })
        {
            Description = "Print detailed execution tracking."
        };
        private static readonly Option<string?> LogOption = new("--log", new[] { "-l" })
        {
            Description = "Enable logging. Optional: specify path/directory.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> SilentOption = new("--silent", new[] { "-s" })
        {
            Description = "Remove all console messages."
        };
        private static readonly Option<string?> PreviewOption = new("--preview", new[] { "-pr" })
        {
            Description = "Preview top N results (e.g. 20, 100, *)",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<int> EstimateOption = new("--estimate", new[] { "-e" })
        {
            Description = "Estimated total rows for progress UI.",
            DefaultValueFactory = _ => 1000000
        };
        private static readonly Option<string> PassOption = new("--pass", Array.Empty<string>())
        {
            Description = "Master password for encryption."
        };
        private static readonly Option<bool> JsonOption = new("--json", Array.Empty<string>())
        {
            Description = "Output results and messages in structured JSON format."
        };
        private static readonly Option<bool> PageOption = new("--page", new[] { "-pa" })
        {
            Description = "Pause and page between multiple result sets in the console."
        };
        private static readonly Option<string?> SessionOption = new("--session", Array.Empty<string>())
        {
            Description = "Enable session persistence with the specified session ID.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string[]> VarOption = new("--var", new[] { "-d" })
        {
            Description = "Inject a variable into the script (e.g. @Name=Value).",
            AllowMultipleArgumentsPerToken = true
        };
        private static readonly Option<bool> ProgressOption = new("--progress", new[] { "-g" })
        {
            Description = "Display real-time graphical execution progress."
        };
        private static readonly Option<bool> ResumeOption = new("--resume", Array.Empty<string>())
        {
            Description = "Resume execution of a persistent session from the last successfully completed checkpoint."
        };
        private static readonly Option<bool> UpdateJwtOption = new("--update", Array.Empty<string>())
        {
            Description = "Update the local appsettings.json file with the new secret."
        };
        
        private static readonly Argument<string> RunScriptArg = new("script")
        {
            Description = "The ETL-SQL script to execute."
        };
        private static readonly Argument<string> EncryptValueArg = new("value")
        {
            Description = "The string to encrypt."
        };
        private static readonly Argument<string> TestValArg = new("testVal")
        {
            Description = "Test category: unit, integration, etc.",
            DefaultValueFactory = _ => "unit"
        };

        private static readonly Argument<string?> ServeScriptArg = new("script")
        {
            Description = "The .rptsql file to serve (omit if using --manifest)",
            Arity = ArgumentArity.ZeroOrOne
        };
        private static readonly Option<string?> ServeManifestOption = new("--manifest", new[] { "-m" })
        {
            Description = "Serve multiple reports defined in a JSON manifest",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<int?> ServePortOption = new("--port", new[] { "-p" })
        {
            Description = "Port to listen on (default: auto-assigned ephemeral port)",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> ServeNoBrowserOption = new("--no-browser", Array.Empty<string>())
        {
            Description = "Do not automatically open the browser on start"
        };
        private static readonly Option<bool> DoctorStrictOption = new("--strict", Array.Empty<string>())
        {
            Description = "Exit with code 1 if any check produces a WARN or FAIL result."
        };
        private static readonly Option<string> DoctorProfileOption = new("--profile", Array.Empty<string>())
        {
            Description = "Check depth: 'quick' (fast local checks) or 'full' (adds engine, report, asset, runtime, and configured service probes).",
            DefaultValueFactory = _ => "quick"
        };
        private static readonly Option<bool> PurgeDryRunOption = new("--dry-run", Array.Empty<string>())
        {
            Description = "List the data that would be removed without deleting anything."
        };
        private static readonly Option<bool> PurgeYesOption = new("--yes", new[] { "-y" })
        {
            Description = "Skip the confirmation prompt (for scripts and installers)."
        };
        private static readonly Option<string?> SpecSchemaOption = new("--schema", new[] { "-s" })
        {
            Description = "Path to the input JSON schema specification file.",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> SpecOutputOption = new("--output", new[] { "-o" })
        {
            Description = "Destination path for the generated ETL-SQL script.",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> ExtractInputOption = new("--input", new[] { "-i" })
        {
            Description = "Path to the input large PDF specification file.",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> ExtractOutputOption = new("--output", new[] { "-o" })
        {
            Description = "Destination path for the extracted trimmed PDF file.",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => null
        };

        public static RootCommand BuildRootCommand(Func<CliContext, Task<int>> handler)
        {
            var rootCommand = new RootCommand("ETL-SQL Engine - Modern Data Integration Tool");

            // 1. RUN Command
            var runCommand = new Command("run", "Execute an ETL-SQL script")
            {
                RunScriptArg,
                BatchSizeOption, PerfOption, VerboseOption, LogOption, SilentOption, PreviewOption, JsonOption, PageOption, SessionOption, VarOption, ProgressOption, ResumeOption
            };
            runCommand.SetAction(context => Dispatch(context, "run", handler));

            // 2. TEST Command
            var testCommand = new Command("test", "Run internal diagnostics or unit tests")
            {
                TestValArg
            };
            testCommand.SetAction(context => Dispatch(context, "test", handler));

            // 4. ENCRYPT Command
            var encryptCommand = new Command("encrypt", "Utility to encrypt a string for secure connections")
            {
                EncryptValueArg,
                PassOption
            };
            encryptCommand.SetAction(context => Dispatch(context, "encrypt", handler));

            var generateCommand = new Command("generate", "Generate mock data for testing projects")
            {
                EstimateOption
            };
            generateCommand.SetAction(context => Dispatch(context, "generate", handler));

            var noticesCommand = new Command("notices", "Show third-party notices and dependency credits");
            noticesCommand.SetAction(context => Dispatch(context, "notices", handler));

            // 5. SESSION Command
            var sessionCommand = new Command("session", "Manage ad-hoc execution sessions");
            var clearSubcommand = new Command("clear", "Clear a session state")
            {
                new Argument<string>("id") { Description = "The session ID to clear" }
            };
            clearSubcommand.SetAction(context => Dispatch(context, "session-clear", handler));
            sessionCommand.Add(clearSubcommand);

            // 6. UI Command (for REPL and windowed mode)
            var uiCommand = new Command("ui", "Interactive user interface commands");
            var replSubcommand = new Command("repl", "Start the JSON-based REPL protocol for IDE integration")
            {
                BatchSizeOption, PerfOption, VerboseOption, LogOption, JsonOption, SessionOption, VarOption
            };
            replSubcommand.SetAction(context => Dispatch(context, "ui-repl", handler));

            var simpleSubcommand = new Command("simple", "Start the simple interactive menu UI")
            {
                BatchSizeOption, VerboseOption
            };
            simpleSubcommand.SetAction(context => Dispatch(context, "ui-simple", handler));

            var editSubcommand = new Command("edit", "Start the modern windowed Terminal IDE (default)")
            {
                new Argument<string?>("file") { Description = "Optional file to pre-load", Arity = ArgumentArity.ZeroOrOne },
                BatchSizeOption, VerboseOption, SessionOption
            };
            editSubcommand.SetAction(context => Dispatch(context, "ui-edit", handler));

            var oldSubcommand = new Command("old", "Start the legacy Spectre-based console editor")
            {
                new Argument<string?>("file") { Description = "Optional file to pre-load", Arity = ArgumentArity.ZeroOrOne },
                BatchSizeOption, VerboseOption
            };
            oldSubcommand.SetAction(context => Dispatch(context, "ui-old", handler));

            uiCommand.Add(replSubcommand);
            uiCommand.Add(simpleSubcommand);
            uiCommand.Add(editSubcommand);
            uiCommand.Add(oldSubcommand);
            
            // 7. DOCTOR Command (Health Check)
            var doctorCommand = new Command("doctor", "Perform a system health check to verify the environment")
            {
                DoctorStrictOption,
                DoctorProfileOption,
                JsonOption,
            };
            doctorCommand.SetAction(context => Dispatch(context, "doctor", handler));

            // 8. CONFIG Command
            var configCommand = new Command("config", "Manage application configuration");
            var setupJwtSubcommand = new Command("setup-jwt", "Generate a secure JWT secret and update appsettings.json")
            {
                UpdateJwtOption
            };
            setupJwtSubcommand.SetAction(context => Dispatch(context, "config-setup-jwt", handler));
            configCommand.Add(setupJwtSubcommand);

            // 9. SERVE Command — start live preview server for a Report-SQL script
            var serveCommand = new Command("serve", "Start a live preview server for a Report-SQL script")
            {
                ServeScriptArg,
                ServeManifestOption,
                ServePortOption,
                ServeNoBrowserOption,
            };
            serveCommand.SetAction(context => Dispatch(context, "serve", handler));

            // 10. PURGE Command — delete all runtime data (cross-platform "delete all data")
            var purgeCommand = new Command("purge", "Delete all ETL-SQL runtime data (reports, snapshots, databases, logs, sessions)")
            {
                PurgeDryRunOption,
                PurgeYesOption,
            };
            purgeCommand.SetAction(context => Dispatch(context, "purge", handler));

            // 11. GEN-SCRIPT Command — compile specification JSON to ETL-SQL script template
            var genScriptCommand = new Command("gen-script", "Compile a schema JSON specification into a validated ETL-SQL script template")
            {
                SpecSchemaOption,
                SpecOutputOption
            };
            genScriptCommand.SetAction(context => Dispatch(context, "gen-script", handler));

            // 12. EXTRACT-SPEC Command — trim large PDF specifications to data dictionary pages
            var extractSpecCommand = new Command("extract-spec", "Extract data dictionary / schema pages from a large PDF specification")
            {
                ExtractInputOption,
                ExtractOutputOption
            };
            extractSpecCommand.SetAction(context => Dispatch(context, "extract-spec", handler));

            rootCommand.Add(runCommand);
            rootCommand.Add(testCommand);
            rootCommand.Add(encryptCommand);
            rootCommand.Add(generateCommand);
            rootCommand.Add(noticesCommand);
            rootCommand.Add(sessionCommand);
            rootCommand.Add(uiCommand);
            rootCommand.Add(doctorCommand);
            rootCommand.Add(configCommand);
            rootCommand.Add(serveCommand);
            rootCommand.Add(purgeCommand);
            rootCommand.Add(genScriptCommand);
            rootCommand.Add(extractSpecCommand);

            return rootCommand;
        }

        private static async Task<int> Dispatch(ParseResult res, string commandName, Func<CliContext, Task<int>> handler)
        {
            var cliContext = new CliContext
            {
                Command = commandName,
                BatchSize = res.GetValue(BatchSizeOption),
                IsPerfMode = res.GetValue(PerfOption),
                IsVerbose = res.GetValue(VerboseOption),
                IsSilentMode = res.GetValue(SilentOption),
                EstimatedRows = res.GetValue(EstimateOption),
                PreviewVal = res.GetValue(PreviewOption),
                Password = res.GetValue(PassOption) ?? Environment.GetEnvironmentVariable("ETL_SQL_MASTER_PASSWORD"),
                LogPath = res.GetResult(LogOption)?.GetValueOrDefault<string?>() ?? "logs/",
                IsLogMode = res.GetResult(LogOption) != null,
                IsJsonMode = res.GetValue(JsonOption),
                EnablePaging = res.GetValue(PageOption),
                DisplayProgress = res.GetValue(ProgressOption)
            };

            if (commandName == "run")
            {
                var input = res.GetValue(RunScriptArg);
                cliContext.ScriptFile = string.IsNullOrWhiteSpace(input) ? null : new FileInfo(input.Trim('"', '\'', ' '));
            }
            else if (commandName == "encrypt")
            {
                cliContext.EncryptValue = res.GetValue(EncryptValueArg);
            }
            else if (commandName == "test")
            {
                cliContext.TestVal = res.GetValue(TestValArg);
            }
            else if (commandName == "session-clear")
            {
                var idArg = res.CommandResult.Children.OfType<ArgumentResult>().FirstOrDefault();
                var sid = idArg?.GetValueOrDefault<string>();
                if (sid != null) cliContext.SessionId = sid;
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
            else if (commandName == "config-setup-jwt")
            {
                cliContext.UpdateConfig = res.GetValue(UpdateJwtOption);
            }
            else if (commandName == "serve")
            {
                var scriptInput = res.GetValue(ServeScriptArg);
                if (!string.IsNullOrWhiteSpace(scriptInput))
                    cliContext.ScriptFile = new FileInfo(scriptInput.Trim('"', '\'', ' '));
                cliContext.ServeManifest  = res.GetValue(ServeManifestOption);
                cliContext.ServePort      = res.GetValue(ServePortOption);
                cliContext.ServeNoBrowser = res.GetValue(ServeNoBrowserOption);
            }
            else if (commandName == "doctor")
            {
                cliContext.DoctorStrict = res.GetValue(DoctorStrictOption);
                cliContext.DoctorProfile = res.GetValue(DoctorProfileOption) ?? "quick";
            }
            else if (commandName == "purge")
            {
                cliContext.PurgeDryRun = res.GetValue(PurgeDryRunOption);
                cliContext.PurgeYes = res.GetValue(PurgeYesOption);
            }
            else if (commandName == "gen-script")
            {
                cliContext.SpecSchema = res.GetValue(SpecSchemaOption);
                cliContext.SpecOutput = res.GetValue(SpecOutputOption);
            }
            else if (commandName == "extract-spec")
            {
                cliContext.ExtractInput = res.GetValue(ExtractInputOption);
                cliContext.ExtractOutput = res.GetValue(ExtractOutputOption);
            }

            var sessionOptVal = res.GetValue(SessionOption);
            if (sessionOptVal != null) cliContext.SessionId = sessionOptVal;

            if (res.GetResult(ResumeOption) != null)
            {
                cliContext.Resume = res.GetValue(ResumeOption);
            }

            if (res.GetResult(VarOption) != null)
            {
                var varArgs = res.GetValue(VarOption);
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

            return await handler(cliContext);
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
            table.AddRow($"serve [blue]{Markup.Escape("<script.rptsql>")}[/]", "Start a live preview server for a Report-SQL script (opens browser automatically).");
            table.AddRow($"test [blue]{Markup.Escape("<category>")}[/]", "Run unit or integration tests (e.g., unit).");
            table.AddRow($"encrypt [blue]{Markup.Escape("<string>")}[/]", "Securely encrypt connection strings.");
            table.AddRow("generate", "Generate large scale mock data for performance validation.");
            table.AddRow("notices", "Show third-party notices and dependency credits.");
            table.AddRow("config setup-jwt", "Generate a secure 256-bit JWT secret.");
            table.AddRow("purge", "Delete all runtime data (reports, snapshots, DBs, logs, sessions). Use --dry-run to preview.");
            table.AddRow($"gen-script [blue]-s <json> -o <etlsql>[/]", "Compile a schema JSON specification into an ETL-SQL script template.");
            table.AddRow($"extract-spec [blue]-i <pdf> -o <pdf>[/]", "Extract data dictionary / schema pages from a large PDF specification.");
            table.AddRow("ui repl", "Start the JSON-based REPL protocol.");
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\nUse [cyan]ETL-SQL {Markup.Escape("<command>")} --help[/] for details on specific options.");
        }
    }

    // CliContext moved to ETL-SQL.Core/CliContext.cs — available via global using ETL_SQL.Core
}
