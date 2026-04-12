using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.App;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.ReportBuilder;

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
                _         => UnknownCommand(args[0])
            };
        }

        // ── build ─────────────────────────────────────────────────────────────

        private static async Task<int> BuildCommand(string[] args)
        {
            string? scriptPath = null;
            string? outputPath = null;
            string  format     = "md";

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
                    default:
                        if (!args[i].StartsWith("-")) scriptPath = args[i];
                        break;
                }
            }

            if (scriptPath == null) { Console.Error.WriteLine("error: no script path specified."); PrintUsage(); return 1; }
            if (!File.Exists(scriptPath)) { Console.Error.WriteLine($"error: script file not found: {scriptPath}"); return 1; }

            var (evaluator, err) = await EvaluateScriptFile(scriptPath);
            if (evaluator == null) { Console.Error.WriteLine($"error: {err}"); return 2; }

            var builder  = new ManifestBuilder(evaluator);
            var manifest = await builder.BuildAsync(scriptPath);

            if (outputPath == null)
                outputPath = Path.ChangeExtension(scriptPath, null) + (format == "json" ? ".report.json" : ".report.md");

            if (format == "json")
            {
                await File.WriteAllTextAsync(outputPath,
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
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

            var (evaluator, err) = await EvaluateScriptFile(scriptPath);
            if (evaluator == null) { Console.Error.WriteLine($"error: {err}"); return 2; }

            var manifest     = await new ManifestBuilder(evaluator).BuildAsync(scriptPath);
            var snapshotPath = SnapshotStore.DefaultPath(scriptPath);
            await new SnapshotStore().SaveAsync(manifest, snapshotPath);

            Console.WriteLine($"Snapshot refreshed: {snapshotPath}");
            Console.WriteLine($"Datasets: {manifest.Datasets.Count}");
            return 0;
        }

        // ── shared evaluation ─────────────────────────────────────────────────

        /// <summary>
        /// Lex, parse, and evaluate a .rptsql script file.
        /// Returns the evaluator (with populated VisualDefinitions etc.) on success,
        /// or (null, error-message) on failure.
        /// </summary>
        private static async Task<(Evaluator? evaluator, string? error)> EvaluateScriptFile(string scriptPath)
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
            for (int i = 1; i < args.Length; i++)
                if (!args[i].StartsWith("-")) scriptPath = args[i];

            if (scriptPath == null) { Console.Error.WriteLine("error: no script path specified."); PrintUsage(); return 1; }
            if (!File.Exists(scriptPath)) { Console.Error.WriteLine($"error: script file not found: {scriptPath}"); return 1; }

            // Resolve the ReportPlayer project for dotnet run (development mode)
            // In production, the ReportPlayer exe lives alongside this binary.
            var selfDir       = AppContext.BaseDirectory;
            var playerExe     = Path.Combine(selfDir, "ETL-SQL.ReportPlayer.exe");
            var playerDll     = Path.Combine(selfDir, "ETL-SQL.ReportPlayer.dll");
            var usePlayerExe  = File.Exists(playerExe) || File.Exists(playerDll);

            string exe;
            string exeArgs;
            if (usePlayerExe && File.Exists(playerDll))
            {
                exe     = "dotnet";
                exeArgs = $"\"{playerDll}\" \"{Path.GetFullPath(scriptPath)}\"";
            }
            else if (usePlayerExe && File.Exists(playerExe))
            {
                exe     = playerExe;
                exeArgs = $"\"{Path.GetFullPath(scriptPath)}\"";
            }
            else
            {
                // Development: find project path relative to this source tree
                var slnDir     = FindSolutionDir(selfDir) ?? selfDir;
                var projectDir = Path.Combine(slnDir, "src", "ETL-SQL.ReportPlayer");
                if (!Directory.Exists(projectDir))
                {
                    Console.Error.WriteLine($"error: Cannot locate ETL-SQL.ReportPlayer. Publish etl-sql-report alongside it, or run ETL-SQL.ReportPlayer directly.");
                    return 1;
                }
                exe     = "dotnet";
                exeArgs = $"run --project \"{projectDir}\" -- \"{Path.GetFullPath(scriptPath)}\"";
            }

            Console.WriteLine($"Starting ReportPlayer for: {scriptPath}");
            Console.WriteLine("Dashboard will be available at http://localhost:5200");

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
            Console.WriteLine("  etl-sql-report build <script.rptsql> [--output <file>] [--format md|json]");
            Console.WriteLine("  etl-sql-report refresh <script.rptsql>");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --output, -o   Output file path (defaults to <script>.report.md|json).");
            Console.WriteLine("  --format, -f   Output format: md (default) or json.");
        }
    }
}
