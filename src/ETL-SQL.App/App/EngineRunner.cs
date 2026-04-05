using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.UI;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;

namespace ETL_SQL.App
{
    public class EngineRunner
    {
        public static async Task<int> Run(CliContext ctx)
        {
            // 1. Handle non-script commands first
            if (ctx.Command == "encrypt")
            {
                if (string.IsNullOrEmpty(ctx.EncryptValue))
                {
                    Logger.WriteLine("Value is required for encryption.", ConsoleColor.Red);
                    return 1;
                }
                if (string.IsNullOrEmpty(ctx.Password))
                {
                    Logger.WriteLine("Master password (--pass) is required for encryption.", ConsoleColor.Red);
                    return 1;
                }
                var encrypted = CryptoUtils.Encrypt(ctx.EncryptValue, ctx.Password);
                Logger.WriteLine($"Encrypted: {encrypted}");
                return 0;
            }

            if (ctx.Command == "generate")
            {
                DataGenerator.Generate(ctx.EstimatedRows);
                return 0;
            }

            if (ctx.Command == "session-clear")
            {
                if (string.IsNullOrEmpty(ctx.SessionId))
                {
                    Logger.WriteLine("Session ID is required for clear command.", ConsoleColor.Red);
                    return 1;
                }
                var sessionManager = Program.ServiceProvider.GetRequiredService<ETL_SQL.Engine.Services.SessionStateManager>();
                sessionManager.ClearSession(ctx.SessionId);
                Logger.WriteLine($"Session {ctx.SessionId} cleared.", ConsoleColor.Green);
                return 0;
            }

            if (ctx.Command == "test")
            {
                // Placeholder for internal test runner if needed
                Logger.WriteLine($"Running {ctx.TestVal} tests...", ConsoleColor.Yellow);
                return 0;
            }

            if (ctx.Command == "ui")
            {
                if (ctx.UiMode == "simple")
                {
                    var simpleUi = new ETL_SQL.UI.SimpleUi(ctx);
                    await simpleUi.RunAsync();
                    return 0;
                }
                else if (ctx.UiMode == "verbose" || ctx.UiMode == "silent")
                {
                    ctx.Command = "run";
                    ctx.IsVerbose = ctx.UiMode == "verbose";
                    ctx.IsSilentMode = ctx.UiMode == "silent";
                    // Fall through to RUN command below
                }
                else
                {
                    // UI mode: edit (default)
                    var editor = new ConsoleEditor(ctx.ScriptFile?.FullName ?? "", new Dictionary<string, IDataSource>());
                    await editor.InitializeAsync();
                    await editor.Run();
                    return 0;
                }
            }

            // 2. RUN command (requires script)
            if (ctx.Command == "run")
            {
                if (ctx.ScriptFile == null || !ctx.ScriptFile.Exists)
                {
                    Logger.WriteLine($"File not found: {ctx.ScriptFile?.FullName}", ConsoleColor.Red);
                    return 1;
                }

                if (ctx.IsLogMode)
                {
                    // Read script-log config from the DI container's IConfiguration (graceful defaults)
                    var config     = Program.ServiceProvider.GetService<IConfiguration>();
                    string scriptLogDir      = config?["Logging:ScriptLog:Directory"]          ?? "logs/scripts";
                    int    scriptRetention   = int.TryParse(config?["Logging:ScriptLog:DefaultRetentionDays"], out var sr) ? sr : 30;
                    int    scriptSizeLimitMb = int.TryParse(config?["Logging:ScriptLog:FileSizeLimitMb"],      out var ss) ? ss : 10;

                    // --log-path override still respected
                    if (!string.IsNullOrWhiteSpace(ctx.LogPath))
                        scriptLogDir = ctx.LogPath;

                    Logger.InitializeScriptLogger(ctx.ScriptFile.Name, scriptLogDir, scriptRetention, scriptSizeLimitMb);
                    Logger.WriteLine($"Logs are being saved to: {Path.GetFullPath(scriptLogDir)}", ConsoleColor.Gray);
                }

                string source = File.ReadAllText(ctx.ScriptFile.FullName);
                
                long startMem = GC.GetTotalMemory(true);

                var lexTime = Stopwatch.StartNew();
                Logger.WriteLine("Lexer phase...");
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                lexTime.Stop();
                
                var parseTime = Stopwatch.StartNew();
                Logger.WriteLine("Parser phase...");
                var parser = new Parser(tokens, source);
                var script = parser.Parse();
                parseTime.Stop();

                var errors = script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (errors.Any())
                {
                    if (ctx.IsJsonMode)
                    {
                        // Output the error messages to stderr for the extension to capture
                        foreach (var err in errors) {
                            Console.Error.WriteLine($"Syntax Error at line {err.Line}, col {err.Column}: {err.Message}");
                        }
                    } 
                    else 
                    {
                        Logger.WriteLine("Parsing failed:", ConsoleColor.Red);
                        foreach (var err in errors) {
                            Logger.WriteLine($"  - Line {err.Line}, Col {err.Column}: {err.Message}", ConsoleColor.Yellow);
                        }
                    }
                    return 1;
                }

                // 3. Linting Phase
                var lintTime = Stopwatch.StartNew();
                Logger.WriteLine("Linter phase...");
                var linter = new Linter();
                foreach (var type in typeof(ILintRule).Assembly.GetTypes()
                    .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
                {
                    if (Activator.CreateInstance(type) is ILintRule rule)
                        linter.AddRule(rule);
                }

                var lintResults = await linter.AnalyzeAsync(script, new DefaultLintContext { DocumentUri = ctx.ScriptFile.FullName });
                lintTime.Stop();

                var lintErrors = lintResults.Where(r => r.Severity == LintSeverity.Error).ToList();
                if (lintErrors.Any())
                {
                    if (ctx.IsJsonMode)
                    {
                        foreach (var err in lintErrors) {
                            Console.Error.WriteLine($"Linter Error at line {err.LineNumber}, col {err.ColumnNumber}: {err.Message}");
                        }
                    } 
                    else 
                    {
                        Logger.WriteLine("Linting failed:", ConsoleColor.Red);
                        foreach (var err in lintErrors) {
                            Logger.WriteLine($"  - Line {err.LineNumber}, Col {err.ColumnNumber}: {err.Message}", ConsoleColor.Yellow);
                        }
                    }
                    return 1;
                }
                
                try
                {
                    Logger.WriteLine("Execution phase...");
                    await using var evaluator = Program.ServiceProvider.GetRequiredService<Evaluator>();
                    evaluator.BatchSize = ctx.BatchSize;
                    evaluator.IsVerbose = ctx.IsVerbose;
                    evaluator.MasterPassword = ctx.Password;
                    evaluator.SessionId = ctx.SessionId;

                    var sessionManager = Program.ServiceProvider.GetRequiredService<ETL_SQL.Engine.Services.SessionStateManager>();
                    if (!string.IsNullOrEmpty(ctx.SessionId))
                    {
                        var state = await sessionManager.LoadSession(ctx.SessionId, ctx.Password);
                        if (state != null)
                        {
                            Logger.WriteLine($"Restoring session {ctx.SessionId}...", ConsoleColor.Cyan);
                            await evaluator.LoadSessionState(state);
                        }
                    }
                    
                    // Periodic reaping of stale sessions
                    sessionManager.ReapStaleSessions(TimeSpan.FromDays(7));

                    if (ctx.IsJsonMode)
                    {
                        Logger.SuppressConsole = true;
                        ResultFormatter.IsJsonMode = true;
                    }

                    var execTime = Stopwatch.StartNew();
                    await evaluator.Evaluate(script);
                    execTime.Stop();

                    if (!string.IsNullOrEmpty(ctx.SessionId))
                    {
                        Logger.WriteLine($"Saving session {ctx.SessionId}...", ConsoleColor.Cyan);
                        await sessionManager.SaveSession(ctx.SessionId, evaluator, source);
                    }

                    if (ctx.IsPerfMode || evaluator.IsProfiling)
                    {
                        var memUsageBytes = GC.GetTotalMemory(false) - startMem;
                        double memUsageMb = Math.Round((double)memUsageBytes / (1024 * 1024), 2);
                        if (memUsageMb < 0) memUsageMb = 0;
                        double rowsPerSec = execTime.Elapsed.TotalSeconds > 0 ? Math.Round(evaluator.RowsProcessed / execTime.Elapsed.TotalSeconds, 0) : evaluator.RowsProcessed;

                        if (ctx.IsJsonMode)
                        {
                            var perfPacket = new {
                                type = "performance",
                                metrics = new {
                                    lexerMs = lexTime.ElapsedMilliseconds,
                                    parserMs = parseTime.ElapsedMilliseconds,
                                    executionMs = execTime.ElapsedMilliseconds,
                                    memoryMb = memUsageMb,
                                    spilledMb = Math.Round((double)evaluator.TotalSpilledBytes / (1024 * 1024), 2),
                                    partitions = evaluator.PartitionsCount,
                                    maxRecursion = evaluator.MaxRecursiveDepth,
                                    rowsProcessed = evaluator.RowsProcessed,
                                    rowsPerSecond = rowsPerSec,
                                    statements = evaluator.ProfileMetrics.Select(m => new {
                                        sql = m.Sql,
                                        durationMs = m.DurationMs,
                                        rows = m.RowsProcessed
                                    }).ToList()
                                }
                            };
                            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(perfPacket));
                        }
                        else
                        {
                            AnsiConsole.Write(new Rule("[yellow]Performance Metrics[/]").RuleStyle("grey"));
                            
                            var chart = new BreakdownChart()
                                .Width(60)
                                .AddItem("Execution", execTime.ElapsedMilliseconds, Color.Green)
                                .AddItem("Parser", parseTime.ElapsedMilliseconds, Color.Orange1)
                                .AddItem("Lexer", lexTime.ElapsedMilliseconds, Color.Blue);

                            AnsiConsole.Write(chart);

                            var table = new Table().Border(TableBorder.Rounded);
                            table.AddColumn("Metric");
                            table.AddColumn("Value");
                            table.AddRow("Total Rows Processed", evaluator.RowsProcessed.ToString("N0"));
                            table.AddRow("Throughput (Rows/s)", rowsPerSec.ToString("N0"));
                            table.AddRow("Approx. RAM Peak", $"{memUsageMb} MB");
                            
                            if (evaluator.TotalSpilledBytes > 0)
                                table.AddRow("Disk Spilled", $"[yellow]{Math.Round((double)evaluator.TotalSpilledBytes / (1024 * 1024), 2)} MB[/]");
                            
                            if (evaluator.PartitionsCount > 0)
                                table.AddRow("Partitions Used", evaluator.PartitionsCount.ToString());

                            if (evaluator.MaxRecursiveDepth > 0)
                                table.AddRow("Max Recursion Depth", evaluator.MaxRecursiveDepth.ToString());
                            
                            AnsiConsole.Write(table);
                            Console.WriteLine();
                        }
                    }
                    else if (!ctx.IsJsonMode)
                    {
                        Logger.WriteLine($"Execution finished in {execTime.ElapsedMilliseconds}ms.", ConsoleColor.Green);
                        Logger.WriteLine($"Rows affected: {evaluator.RowsProcessed}");
                    }

                    if (evaluator.DockerManager.HasActiveContainers)
                    {
                        if (ctx.IsJsonMode)
                        {
                            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { 
                                type = "message", 
                                level = "warning",
                                text = "Docker containers are still running. Remember to use 'DOCKER CLOSE;' when finished." 
                            }));
                        }
                        else
                        {
                            Logger.WriteLine("Note: Docker containers are still running. Use 'DOCKER CLOSE;' to terminate them when finished.", ConsoleColor.Yellow);
                        }
                    }

                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Error: {ex.Message}", ConsoleColor.Red);
                    if (ctx.IsVerbose) Logger.WriteLine(ex.StackTrace ?? "", ConsoleColor.DarkGray);
                    return 1;
                }
            }

            Logger.WriteLine($"Unknown command: {ctx.Command}", ConsoleColor.Red);
            return 1;
        }
    }
}
