using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace ETL_SQL.ReportBuilder.CLI
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "build" => await BuildCommand(args),
                "refresh" => await RefreshCommand(args),
                "serve" => await ServeCommand(args),
                "print" => await PrintCommand(args),
                _ => UnknownCommand(args[0])
            };
        }

        // ── build ─────────────────────────────────────────────────────────────

        private static async Task<int> BuildCommand(string[] args)
        {
            string? scriptPath = null;
            string? outputPath = null;
            string format = "md";
            bool isInteraction = false;
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var runPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--output":
                    case "-o":
                        outputPath = i + 1 < args.Length ? args[++i] : null;
                        break;
                    case "--format":
                    case "-f":
                        format = i + 1 < args.Length ? args[++i].ToLowerInvariant() : "md";
                        break;
                    case "--interaction":
                        isInteraction = true;
                        break;
                    case "--run-page":
                        if (i + 1 < args.Length)
                        {
                            runPages.Add(args[++i]);
                        }
                        break;
                    case "--parameter":
                    case "-p":
                        if (i + 1 < args.Length)
                        {
                            var pair = args[++i].Split('=', 2);
                            parameters[pair[0]] = pair.Length > 1 ? pair[1] : string.Empty;
                        }
                        break;
                    default:
                        if (!args[i].StartsWith("-")) scriptPath = args[i];
                        break;
                }
            }

            if (scriptPath == null) { Console.Error.WriteLine("error: no script path specified."); PrintUsage(); return 1; }
            if (!File.Exists(scriptPath)) { Console.Error.WriteLine($"error: script file not found: {scriptPath}"); return 1; }

            // In one-shot mode, we evaluate the script.
            // If it's an interaction, we want the "Universe" to be the state with baseline parameters
            // and the "Selection" to be the state with current parameters.
            // However, the CLI doesn't have a "warm" session.
            // So we treat the provided parameters as the interaction values if it's an interaction.
            var (evaluator, err) = await EvaluateScriptFile(scriptPath, isInteraction ? null : parameters);
            if (evaluator == null) { Console.Error.WriteLine($"error: {err}"); return 2; }

            var builder = new ManifestBuilder(evaluator);
            var manifest = await builder.BuildAsync(scriptPath, isInteraction ? parameters : null, runPages: runPages.Count > 0 ? runPages : null);
            manifest.IsInteraction = isInteraction;

            string ext = format switch { "json" => ".report.json", "pdf" => ".report.pdf", _ => ".report.md" };
            if (outputPath == null)
                outputPath = Path.ChangeExtension(scriptPath, null) + ext;

            if (format == "json")
            {
                await File.WriteAllTextAsync(outputPath,
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (format == "pdf")
            {
                var pdfBytes = await new PdfExporter().ExportAsync(manifest);
                await File.WriteAllBytesAsync(outputPath, pdfBytes);
            }
            else
            {
                await File.WriteAllTextAsync(outputPath, new MarkdownRenderer().Render(manifest));
            }

            var snapshotPath = SnapshotStore.DefaultPath(scriptPath);
            await new SnapshotStore().SaveAsync(manifest, snapshotPath);

            Console.WriteLine($"Report written to:  {outputPath}");
            Console.WriteLine($"Snapshot saved to:  {snapshotPath}");
            Console.WriteLine($"Visuals: {manifest.Visuals.Count}  Pages: {manifest.Pages.Count}  Datasets: {manifest.Datasets.Count}");
            return 0;
        }

        // ── refresh ───────────────────────────────────────────────────────────

        private static async Task<int> RefreshCommand(string[] args)
        {
            string? scriptPath = null;
            for (int i = 1; i < args.Length; i++)
                if (!args[i].StartsWith("-")) scriptPath = args[i];

            if (scriptPath == null) { Console.Error.WriteLine("error: no script path specified."); PrintUsage(); return 1; }
            if (!File.Exists(scriptPath)) { Console.Error.WriteLine($"error: script file not found: {scriptPath}"); return 1; }

            var (evaluator, err) = await EvaluateScriptFile(scriptPath, null);
            if (evaluator == null) { Console.Error.WriteLine($"error: {err}"); return 2; }

            var manifest = await new ManifestBuilder(evaluator).BuildAsync(scriptPath);
            var snapshotPath = SnapshotStore.DefaultPath(scriptPath);
            await new SnapshotStore().SaveAsync(manifest, snapshotPath);

            Console.WriteLine($"Snapshot refreshed: {snapshotPath}");
            Console.WriteLine($"Datasets: {manifest.Datasets.Count}");
            return 0;
        }

        // ── print ─────────────────────────────────────────────────────────────

        private static async Task<int> PrintCommand(string[] args)
        {
            string? scriptPath = null;
            string? outputPath = null;
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--parameter" || args[i] == "-p")
                {
                    if (i + 1 < args.Length)
                    {
                        var pair = args[++i].Split('=', 2);
                        parameters[pair[0]] = pair.Length > 1 ? pair[1] : string.Empty;
                    }
                }
                else if (args[i] == "--output" || args[i] == "-o")
                {
                    outputPath = i + 1 < args.Length ? args[++i] : null;
                }
                else if (!args[i].StartsWith("-")) scriptPath = args[i];
            }

            if (scriptPath == null) { Console.Error.WriteLine("error: no script path specified."); return 1; }
            if (!File.Exists(scriptPath)) { Console.Error.WriteLine($"error: script file not found: {scriptPath}"); return 1; }

            if (outputPath == null)
            {
                AnsiConsole.MarkupLine($"[bold blue]ETL-SQL Report Printer[/]");
                AnsiConsole.MarkupLine($"[grey]Executing {Path.GetFileName(scriptPath)}...[/]");
            }

            var (evaluator, err) = await EvaluateScriptFile(scriptPath, parameters);
            if (evaluator == null)
            {
                if (outputPath == null) AnsiConsole.MarkupLine($"[red]error: {err}[/]");
                else await File.WriteAllTextAsync(outputPath, $"error: {err}");
                return 2;
            }

            var manifest = await new ManifestBuilder(evaluator).BuildAsync(scriptPath);

            if (outputPath != null)
            {
                // Capture output to a string and strip ANSI
                var console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Ansi = AnsiSupport.No,
                    ColorSystem = ColorSystemSupport.NoColors,
                    Interactive = InteractionSupport.No,
                    Out = new AnsiConsoleOutput(new StringWriter())
                });

                if (manifest.Pages.Count == 0)
                {
                    foreach (var visual in manifest.Visuals)
                    {
                        console.Write(TerminalRenderer.RenderVisual(visual));
                        console.WriteLine();
                    }
                }
                else
                {
                    foreach (var page in manifest.Pages)
                    {
                        console.Write(TerminalRenderer.RenderPage(page, manifest));
                        console.WriteLine();
                    }
                }

                var text = ((StringWriter)console.Profile.Out.Writer).ToString();
                await File.WriteAllTextAsync(outputPath, text);
                return 0;
            }

            if (manifest.Pages.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No pages defined. Printing all visuals in sequence...[/]");
                foreach (var visual in manifest.Visuals)
                {
                    AnsiConsole.Write(TerminalRenderer.RenderVisual(visual));
                    AnsiConsole.WriteLine();
                }
            }
            else
            {
                foreach (var page in manifest.Pages)
                {
                    AnsiConsole.Write(TerminalRenderer.RenderPage(page, manifest));
                    AnsiConsole.WriteLine();
                }
            }

            return 0;
        }

        // ── shared evaluation ─────────────────────────────────────────────────

        /// <summary>
        /// Lex, parse, and evaluate a .rptsql script file.
        /// Returns the evaluator (with populated VisualDefinitions etc.) on success,
        /// or (null, error-message) on failure.
        /// </summary>
        private static async Task<(Evaluator? evaluator, string? error)> EvaluateScriptFile(string scriptPath, Dictionary<string, string>? parameters = null)
        {
            string scriptText;
            try { scriptText = await File.ReadAllTextAsync(scriptPath); }
            catch (Exception ex) { return (null, $"Cannot read script: {ex.Message}"); }

            var lexer = new Lexer(scriptText);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, scriptText);
            var script = parser.Parse();

            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();
            evaluator.RedirectOutput = true;

            // Inject parameters before evaluation
            if (parameters != null)
            {
                foreach (var kv in parameters)
                {
                    string name = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                    evaluator.DeclareVariable(name, kv.Value, new VariableMetadata { IsDeclared = false, IsInput = true });
                }
            }

            try
            {
                await evaluator.Evaluate(script);
                return (evaluator, null);
            }
            catch (Exception ex)
            {
                return (null, $"Script evaluation failed: {ex.Message}");
            }
        }

        // ── serve ─────────────────────────────────────────────────────────────

        private static async Task<int> ServeCommand(string[] args)
        {
            string? scriptPath = null;
            string? manifestPath = null;
            string? dirPath = null;
            string? openReport = null;

            int? portArg = null;
            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == "--manifest" || args[i] == "-m") && i + 1 < args.Length)
                    manifestPath = args[++i];
                else if (args[i] == "--dir" && i + 1 < args.Length)
                    dirPath = args[++i];
                else if (args[i] == "--open" && i + 1 < args.Length)
                    openReport = args[++i];
                else if ((args[i] == "--port" || args[i] == "-p") && i + 1 < args.Length && int.TryParse(args[++i], out int p))
                    portArg = p;
                else if (!args[i].StartsWith("-"))
                    scriptPath = args[i];
            }

            // Auto-generate manifest if --dir is specified
            string? tempManifest = null;
            if (dirPath != null && manifestPath == null)
            {
                if (!Directory.Exists(dirPath)) { Console.Error.WriteLine($"error: directory not found: {dirPath}"); return 1; }

                var files = Directory.GetFiles(dirPath, "*.rptsql", SearchOption.TopDirectoryOnly);
                var entries = ((IEnumerable<string>)files).Select(f => new
                {
                    Name = Path.GetFileNameWithoutExtension(f),
                    Description = $"Report: {Path.GetFileName(f)}",
                    Path = Path.GetRelativePath(dirPath, f)
                }).ToList();

                tempManifest = Path.Combine(dirPath, $".etl_reports_{Guid.NewGuid():N}.tmp.json");
                File.WriteAllText(tempManifest, JsonSerializer.Serialize(new { reports = entries }, new JsonSerializerOptions { WriteIndented = true }));
                manifestPath = tempManifest;
            }

            bool multiMode = manifestPath != null;

            if (!multiMode && scriptPath == null)
            {
                Console.Error.WriteLine("error: no script path, --dir, or --manifest specified.");
                PrintUsage();
                return 1;
            }
            if (multiMode && !File.Exists(manifestPath!))
            {
                Console.Error.WriteLine($"error: manifest file not found: {manifestPath}");
                return 1;
            }
            if (!multiMode && !File.Exists(scriptPath!))
            {
                Console.Error.WriteLine($"error: script file not found: {scriptPath}");
                return 1;
            }

            // Build the argument string to forward to ReportPlayer
            string playerArg = multiMode
                ? $"--manifest \"{Path.GetFullPath(manifestPath!)}\" --no-browser"
                : $"\"{Path.GetFullPath(scriptPath!)}\" --no-browser";

            if (portArg.HasValue) playerArg += $" --port {portArg.Value}";

            // Resolve the ReportPlayer project (development mode or production publish)
            var selfDir = AppContext.BaseDirectory;
            var playerExe = Path.Combine(selfDir, "ETL-SQL-Player.exe");
            var playerDll = Path.Combine(selfDir, "ETL-SQL-Player.dll");
            var fallbackExe = Path.Combine(selfDir, "ETL-SQL.ReportPlayer.exe");
            var fallbackDll = Path.Combine(selfDir, "ETL-SQL.ReportPlayer.dll");

            string exe;
            string exeArgs;
            if (File.Exists(playerDll))
            {
                exe = "dotnet";
                exeArgs = $"\"{playerDll}\" {playerArg}";
            }
            else if (File.Exists(playerExe))
            {
                exe = playerExe;
                exeArgs = playerArg;
            }
            else if (File.Exists(fallbackDll))
            {
                exe = "dotnet";
                exeArgs = $"\"{fallbackDll}\" {playerArg}";
            }
            else if (File.Exists(fallbackExe))
            {
                exe = fallbackExe;
                exeArgs = playerArg;
            }
            else
            {
                var slnDir = FindSolutionDir(selfDir) ?? selfDir;
                var projectDir = Path.Combine(slnDir, "src", "ETL-SQL.ReportPlayer");
                if (!Directory.Exists(projectDir))
                {
                    Console.Error.WriteLine($"error: Cannot locate ETL-SQL-Player.");
                    return 1;
                }
                exe = "dotnet";
                exeArgs = $"run --project \"{projectDir}\" -- {playerArg}";
            }

            if (multiMode)
            {
                if (tempManifest != null) Console.WriteLine($"Scanning directory: {dirPath}");
                Console.WriteLine($"Starting ReportPlayer with manifest: {manifestPath}");
            }
            else
            {
                Console.WriteLine($"Starting ReportPlayer for: {scriptPath}");
            }

            var psi = new ProcessStartInfo(exe, exeArgs)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = new Process { StartInfo = psi };

            var urlSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                Console.WriteLine(e.Data);
                if (e.Data.StartsWith("REPORT_URL="))
                    urlSource.TrySetResult(e.Data.Substring("REPORT_URL=".Length).Trim());
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) Console.Error.WriteLine(e.Data);
            };

            if (!proc.Start()) { Console.Error.WriteLine("error: Failed to start ReportPlayer."); return 1; }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Wait up to 60s — dotnet run (dev mode) can take 10–30s on first build
            var completed = await Task.WhenAny(urlSource.Task, Task.Delay(60_000));
            var boundUrl = completed == urlSource.Task ? urlSource.Task.Result : null;

            if (boundUrl != null)
            {
                // If --open is specified, navigate to that report
                if (!string.IsNullOrEmpty(openReport))
                {
                    string name = Path.GetFileNameWithoutExtension(openReport);
                    if (!boundUrl.EndsWith("/")) boundUrl += "/";
                    boundUrl += "reports/" + System.Net.WebUtility.UrlEncode(name);
                }

                try
                {
                    Process.Start(new ProcessStartInfo(boundUrl) { UseShellExecute = true });
                }
                catch { /* browser open is best-effort */ }
            }

            await proc.WaitForExitAsync();

            // Cleanup temp manifest
            if (tempManifest != null && File.Exists(tempManifest))
            {
                try { File.Delete(tempManifest); } catch { }
            }

            return proc.ExitCode;
        }

        private static string? FindSolutionDir(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static int UnknownCommand(string cmd)
        {
            Console.Error.WriteLine($"error: unknown command '{cmd}'");
            PrintUsage();
            return 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("etl-sql-report — Report-SQL build tool");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  etl-sql-report build   <script.rptsql> [--output <file>] [--format md|json|pdf] [--parameter @p=v] [--run-page PageName]");
            Console.WriteLine("  etl-sql-report refresh <script.rptsql>");
            Console.WriteLine("  etl-sql-report serve   <script.rptsql> [--port <n>] [--no-browser]");
            Console.WriteLine("  etl-sql-report serve   --manifest reports.json [--port <n>]");
            Console.WriteLine("  etl-sql-report serve   --dir <path> [--open <file.rptsql>] [--port <n>]");
            Console.WriteLine("  etl-sql-report print   <script.rptsql> [--parameter @p=v]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --output, -o      Output file path (defaults to <script>.report.md|json|pdf).");
            Console.WriteLine("  --format, -f      Output format: md (default), json, or pdf.");
            Console.WriteLine("  --parameter, -p   Pass a variable to the script (e.g. -p @region=West).");
            Console.WriteLine("  --run-page        Mark a paginated page as run so AUTO/ON_RUN result visuals load.");
            Console.WriteLine("  --manifest, -m    Path to reports.json for multi-report hosting.");
            Console.WriteLine("  --dir             Host all .rptsql files in the specified directory.");
            Console.WriteLine("  --open            The initial report file to open when using --dir.");
        }
    }
}
