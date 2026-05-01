using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.App;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.ReportBuilder;
using ETL_SQL.ReportBuilder.Renderers;
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
                "build"   => await BuildCommand(args),
                "refresh" => await RefreshCommand(args),
                "serve"   => await ServeCommand(args),
                "print"   => await PrintCommand(args),
                _         => UnknownCommand(args[0])
            };
        }

        // ── build ─────────────────────────────────────────────────────────────

        private static async Task<int> BuildCommand(string[] args)
        {
            string? scriptPath = null;
            string? outputPath = null;
            string  format     = "md";
            var     parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--output": case "-o":
                        outputPath = i + 1 < args.Length ? args[++i] : null;
                        break;
                    case "--format": case "-f":
                        format = i + 1 < args.Length ? args[++i].ToLowerInvariant() : "md";
                        break;
                    case "--parameter": case "-p":
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

            var (evaluator, err) = await EvaluateScriptFile(scriptPath, parameters);
            if (evaluator == null) { Console.Error.WriteLine($"error: {err}"); return 2; }

            var builder  = new ManifestBuilder(evaluator);
            var manifest = await builder.BuildAsync(scriptPath);

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
                var pdfBytes = new PdfExporter().Export(manifest);
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

            var manifest     = await new ManifestBuilder(evaluator).BuildAsync(scriptPath);
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
                else if (!args[i].StartsWith("-")) scriptPath = args[i];
            }

            if (scriptPath == null) { Console.Error.WriteLine("error: no script path specified."); return 1; }
            if (!File.Exists(scriptPath)) { Console.Error.WriteLine($"error: script file not found: {scriptPath}"); return 1; }

            AnsiConsole.MarkupLine($"[bold blue]ETL-SQL Report Printer[/]");
            AnsiConsole.MarkupLine($"[grey]Executing {Path.GetFileName(scriptPath)}...[/]");

            var (evaluator, err) = await EvaluateScriptFile(scriptPath, parameters);
            if (evaluator == null) { AnsiConsole.MarkupLine($"[red]error: {err}[/]"); return 2; }

            var manifest = await new ManifestBuilder(evaluator).BuildAsync(scriptPath);
            
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

            var lexer  = new Lexer(scriptText);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, scriptText);
            var script = parser.Parse();

            var provider  = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();
            evaluator.RedirectOutput = true;

            // Inject parameters before evaluation
            if (parameters != null)
            {
                foreach (var kv in parameters)
                {
                    string name = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                    evaluator.DeclareVariable(name, kv.Value);
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
            string? scriptPath   = null;
            string? manifestPath = null;

            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == "--manifest" || args[i] == "-m") && i + 1 < args.Length)
                    manifestPath = args[++i];
                else if (!args[i].StartsWith("-"))
                    scriptPath = args[i];
            }

            bool multiMode = manifestPath != null;

            if (!multiMode && scriptPath == null)
            {
                Console.Error.WriteLine("error: no script path or --manifest specified.");
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
                ? $"--manifest \"{Path.GetFullPath(manifestPath!)}\""
                : $"\"{Path.GetFullPath(scriptPath!)}\"";

            // Resolve the ReportPlayer project for dotnet run (development mode)
            var selfDir      = AppContext.BaseDirectory;
            var playerExe    = Path.Combine(selfDir, "ETL-SQL.ReportPlayer.exe");
            var playerDll    = Path.Combine(selfDir, "ETL-SQL.ReportPlayer.dll");
            var usePlayerExe = File.Exists(playerExe) || File.Exists(playerDll);

            string exe;
            string exeArgs;
            if (usePlayerExe && File.Exists(playerDll))
            {
                exe     = "dotnet";
                exeArgs = $"\"{playerDll}\" {playerArg}";
            }
            else if (usePlayerExe && File.Exists(playerExe))
            {
                exe     = playerExe;
                exeArgs = playerArg;
            }
            else
            {
                var slnDir     = FindSolutionDir(selfDir) ?? selfDir;
                var projectDir = Path.Combine(slnDir, "src", "ETL-SQL.ReportPlayer");
                if (!Directory.Exists(projectDir))
                {
                    Console.Error.WriteLine($"error: Cannot locate ETL-SQL.ReportPlayer.");
                    return 1;
                }
                exe     = "dotnet";
                exeArgs = $"run --project \"{projectDir}\" -- {playerArg}";
            }

            if (multiMode)
            {
                Console.WriteLine($"Starting ReportPlayer with manifest: {manifestPath}");
                Console.WriteLine("Catalog will be available at http://localhost:5200");
            }
            else
            {
                Console.WriteLine($"Starting ReportPlayer for: {scriptPath}");
                Console.WriteLine("Dashboard will be available at http://localhost:5200");
            }

            var psi = new ProcessStartInfo(exe, exeArgs) { UseShellExecute = false };
            using var proc = Process.Start(psi);
            if (proc == null) { Console.Error.WriteLine("error: Failed to start ReportPlayer."); return 1; }

            // Open browser after a short delay
            await Task.Delay(2500);
            try { Process.Start(new ProcessStartInfo("http://localhost:5200") { UseShellExecute = true }); }
            catch { /* browser open is best-effort */ }

            await proc.WaitForExitAsync();
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
            Console.WriteLine("  etl-sql-report build   <script.rptsql> [--output <file>] [--format md|json|pdf] [--parameter @p=v]");
            Console.WriteLine("  etl-sql-report refresh <script.rptsql>");
            Console.WriteLine("  etl-sql-report serve   <script.rptsql>");
            Console.WriteLine("  etl-sql-report print   <script.rptsql> [--parameter @p=v]");
            Console.WriteLine("  etl-sql-report serve   --manifest reports.json");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --output, -o      Output file path (defaults to <script>.report.md|json|pdf).");
            Console.WriteLine("  --format, -f      Output format: md (default), json, or pdf.");
            Console.WriteLine("  --parameter, -p   Pass a variable to the script (e.g. -p @region=West).");
            Console.WriteLine("  --manifest, -m    Path to reports.json for multi-report hosting.");
        }
    }
}
