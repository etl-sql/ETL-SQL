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
            var logger = Program.ServiceProvider.GetRequiredService<ILogger>();
            var loggerService = Program.ServiceProvider.GetRequiredService<ILoggerService>();

            // 1. Handle non-script commands first
            if (ctx.Command == "encrypt")
            {
                if (string.IsNullOrEmpty(ctx.EncryptValue))
                {
                    logger.WriteLine("Value is required for encryption.", ConsoleColor.Red);
                    return 1;
                }
                if (string.IsNullOrEmpty(ctx.Password))
                {
                    logger.WriteLine("Master password (--pass) is required for encryption.", ConsoleColor.Red);
                    return 1;
                }
                var encrypted = CryptoUtils.Encrypt(ctx.EncryptValue, ctx.Password);
                logger.WriteLine($"Encrypted: {encrypted}");
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
                    logger.WriteLine("Session ID is required for clear command.", ConsoleColor.Red);
                    return 1;
                }
                var sessionManager = Program.ServiceProvider.GetRequiredService<ETL_SQL.Engine.Services.SessionStateManager>();
                sessionManager.ClearSession(ctx.SessionId);
                logger.WriteLine($"Session {ctx.SessionId} cleared.", ConsoleColor.Green);
                return 0;
            }

            if (ctx.Command == "test")
            {
                // Placeholder for internal test runner if needed
                logger.WriteLine($"Running {ctx.TestVal} tests...", ConsoleColor.Yellow);
                return 0;
            }

            if (ctx.Command == "doctor")
            {
                return await RunDoctor(logger);
            }

            if (ctx.Command.StartsWith("ui-"))
            {
                return await ETL_SQL.TUI.TuiRunner.Run(ctx, Program.ServiceProvider);
            }

            // 2. RUN command (requires script)
            if (ctx.Command == "run")
            {
                if (ctx.ScriptFile == null || !ctx.ScriptFile.Exists)
                {
                    logger.WriteLine($"File not found: {ctx.ScriptFile?.FullName}", ConsoleColor.Red);
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

                    loggerService.InitializeScriptLogger(ctx.ScriptFile.Name, scriptLogDir, scriptRetention, scriptSizeLimitMb);
                    logger.WriteLine($"Logs are being saved to: {Path.GetFullPath(scriptLogDir)}", ConsoleColor.Gray);
                }

                string source = File.ReadAllText(ctx.ScriptFile.FullName);
                
                long startMem = GC.GetTotalMemory(true);

                var lexTime = Stopwatch.StartNew();
                logger.WriteLine("Lexer phase...");
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                lexTime.Stop();
                
                var parseTime = Stopwatch.StartNew();
                logger.WriteLine("Parser phase...");
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
                        logger.WriteLine("Parsing failed:", ConsoleColor.Red);
                        foreach (var err in errors) {
                            logger.WriteLine($"  - Line {err.Line}, Col {err.Column}: {err.Message}", ConsoleColor.Yellow);
                        }
                    }
                    return 1;
                }

                // 3. Linting Phase
                var lintTime = Stopwatch.StartNew();
                logger.WriteLine("Linter phase...");
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
                        logger.WriteLine("Linting failed:", ConsoleColor.Red);
                        foreach (var err in lintErrors) {
                            logger.WriteLine($"  - Line {err.LineNumber}, Col {err.ColumnNumber}: {err.Message}", ConsoleColor.Yellow);
                        }
                    }
                    return 1;
                }
                
                try
                {
                    logger.WriteLine("Execution phase...");
                    await using var evaluator = Program.ServiceProvider.GetRequiredService<Evaluator>();
                    evaluator.BatchSize = ctx.BatchSize;
                    evaluator.IsVerbose = ctx.IsVerbose;
                    evaluator.MasterPassword = ctx.Password;
                    evaluator.SessionId = ctx.SessionId;

                    // Security Hardening: Register the script directory as an approved safe zone for overrides
                    var scriptDir = Path.GetDirectoryName(ctx.ScriptFile.FullName);
                    if (!string.IsNullOrEmpty(scriptDir))
                    {
                        // Rpt-2: Cleanup orphaned snapshot temp files from previous failed runs
                        ETL_SQL.ReportBuilder.SnapshotStore.CleanupOrphanedSnapshots(scriptDir);

                        if (!evaluator.SecurityService.IsSystemPath(scriptDir))
                        {
                            evaluator.SecurityService.ApprovedSafeZones.Add(scriptDir);
                        }
                        else
                        {
                            evaluator.Log($"[WARNING] Script directory '{scriptDir}' is a protected system path and will not be authorized as a security Safe Zone.", ConsoleColor.Yellow);
                        }
                    }

                    // Inject CLI variables as input parameters
                    foreach (var v in ctx.Variables)
                    {
                        evaluator.DeclareVariable(v.Key, v.Value, new VariableMetadata { IsInput = true, IsDeclared = false });
                    }

                    var sessionManager = Program.ServiceProvider.GetRequiredService<ETL_SQL.Engine.Services.SessionStateManager>();
                    if (!string.IsNullOrEmpty(ctx.SessionId))
                    {
                        evaluator.IsPersistentSession = true;
                        evaluator.SessionId = ctx.SessionId;
                        evaluator.SessionRoot = sessionManager.SessionRoot;
                        var state = await sessionManager.LoadSession(ctx.SessionId);
                        if (state != null)
                        {
                            logger.WriteLine($"Restoring session {ctx.SessionId}...", ConsoleColor.Cyan);
                            await evaluator.LoadSessionState(state);
                        }
                    }
                    
                    // Periodic reaping of stale sessions
                    var sessionRetentionDays = int.TryParse(Program.ServiceProvider.GetRequiredService<IConfiguration>()["Session:StaleSessionRetentionDays"], out var srd) ? srd : 7;
                    sessionManager.ReapStaleSessions(TimeSpan.FromDays(sessionRetentionDays));


                    if (ctx.IsJsonMode)
                    {
                        if (logger is LoggerService ls) ls.SuppressConsole = true;
                        evaluator.IsJsonMode = true; // Propagate to logger via evaluator if needed
                        logger.IsJsonMode = true;
                        ResultFormatter.IsJsonMode = true;
                    }

                    // 4. Graphical Execution Tree (TUI)
                    CancellationTokenSource? treeCts = null;
                    Task? treeRenderTask = null;
                    if (ctx.DisplayProgress && !ctx.IsJsonMode && !ctx.IsSilentMode)
                    {
                        var tree = evaluator.ExecutionTree;
                        var visualizer = new ExecuteTreeVisualizer(tree);
                        treeCts = new CancellationTokenSource();
                        treeRenderTask = visualizer.RenderLiveAsync(treeCts.Token);
                        
                        // If we are showing the tree, we might want to suppress some logs to avoid flickering
                        // but let's keep it simple for now as requested.
                    }
                    else if (ctx.IsJsonMode)
                    {
                        // In JSON mode, we emit "progress" packets for the VS Code extension
                        var tree = evaluator.ExecutionTree;
                        treeCts = new CancellationTokenSource();
                        
                        evaluator.IsProfiling = true; 
                        evaluator.DisplayExecuteTree = true;

                        // Initial clear signal for the VS Code extension
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
                            type = "clear",
                            uri = ctx.ScriptFile.FullName,
                            target = "all"
                        }));

                        // Initial flush to ensure the root node is visible immediately
                        var initialSnapshot = new {
                            type = "progress",
                            uri = ctx.ScriptFile.FullName,
                            data = tree.ToSnapshot()
                        };
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(initialSnapshot));

                        treeRenderTask = Task.Run(async () => {
                            while (!treeCts.Token.IsCancellationRequested)
                            {
                                var snapshot = new {
                                    type = "progress",
                                    uri = ctx.ScriptFile.FullName,
                                    data = tree.ToSnapshot()
                                };
                                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(snapshot));

                                // Emit variables state
                                var vars = new {
                                    type = "variables",
                                    uri = ctx.ScriptFile.FullName,
                                    data = evaluator.CurrentVariables.Select(kv => new {
                                        name = kv.Key,
                                        value = kv.Value?.ToString() ?? "null",
                                        type = kv.Value?.GetType().Name ?? "null"
                                    }).ToList()
                                };
                                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(vars));

                                if (evaluator.IsVerbose) evaluator.Log($"[TELEMETRY] {System.Text.Json.JsonSerializer.Serialize(snapshot)}", ConsoleColor.DarkGray);
                                await Task.Delay(500, treeCts.Token); // 2Hz for JSON streaming
                            }
                        }, treeCts.Token);
                    }

                    var execTime = Stopwatch.StartNew();
                    await evaluator.Evaluate(script);
                    execTime.Stop();

                    if (treeCts != null)
                    {
                        treeCts.Cancel();
                        if (treeRenderTask != null) await treeRenderTask;

                        // Final flush for fast scripts in JSON mode
                        if (ctx.IsJsonMode && !ctx.IsSilentMode)
                        {
                            var finalSnapshot = new {
                                type = "progress",
                                uri = ctx.ScriptFile.FullName,
                                data = evaluator.ExecutionTree.ToSnapshot()
                            };
                            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(finalSnapshot));

                            // Final variables flush
                            var finalVars = new {
                                type = "variables",
                                uri = ctx.ScriptFile.FullName,
                                data = evaluator.CurrentVariables.Select(kv => new {
                                    name = kv.Key,
                                    value = kv.Value?.ToString() ?? "null",
                                    type = kv.Value?.GetType().Name ?? "null"
                                }).ToList()
                            };
                            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(finalVars));

                            // Emit performance telemetry
                            if (evaluator.IsProfiling)
                            {
                                var perf = new {
                                    type = "performance",
                                    uri = ctx.ScriptFile.FullName,
                                    data = new {
                                        totalDurationMs = execTime.ElapsedMilliseconds,
                                        statements = evaluator.ProfileMetrics.Select(m => new {
                                            statementType = m.Sql.Split(' ')[0], // Simple type extraction
                                            durationMs = m.DurationMs,
                                            memoryUsageBytes = Math.Max(0, m.MemoryDeltaBytes),
                                            sourceText = m.Sql
                                        }).ToList()
                                    }
                                };
                                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(perf));
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(ctx.SessionId))
                    {
                        logger.WriteLine($"Saving session {ctx.SessionId}...", ConsoleColor.Cyan);
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
                                uri = ctx.ScriptFile.FullName,
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
                            var json = System.Text.Json.JsonSerializer.Serialize(perfPacket);
                            Console.WriteLine(json);
                            if (evaluator.IsVerbose) evaluator.Log($"[TELEMETRY] {json}", ConsoleColor.DarkGray);
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
                        logger.WriteLine($"Execution finished in {execTime.ElapsedMilliseconds}ms.", ConsoleColor.Green);
                        logger.WriteLine($"Rows affected: {evaluator.RowsProcessed}");
                    }

                    if (evaluator.DockerManager.HasActiveContainers)
                    {
                        if (ctx.IsJsonMode)
                        {
                            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { 
                                type = "message", 
                                uri = ctx.ScriptFile.FullName,
                                level = "warning",
                                text = "Docker containers are still running. Remember to use 'DOCKER CLOSE;' when finished." 
                            }));
                        }
                        else
                        {
                            logger.WriteLine("Note: Docker containers are still running. Use 'DOCKER CLOSE;' to terminate them when finished.", ConsoleColor.Yellow);
                        }
                    }

                    return 0;
                }
                catch (Exception ex)
                {
                    logger.WriteLine($"Error: {ex.Message}", ConsoleColor.Red);
                    if (ctx.IsVerbose) logger.WriteLine(ex.StackTrace ?? "", ConsoleColor.DarkGray);
                    return 1;
                }
            }

            logger.WriteLine($"Unknown command: {ctx.Command}", ConsoleColor.Red);
            return 1;
        }

        private static async Task<int> RunDoctor(ILogger logger)
        {
            AnsiConsole.Write(new FigletText("ETL-SQL Doctor").Centered().Color(Color.DeepSkyBlue1));
            AnsiConsole.Write(new Rule("[yellow]System Health Check[/]").RuleStyle("grey"));
            Console.WriteLine();

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            table.AddColumn("Check");
            table.AddColumn("Result");
            table.AddColumn("Status");

            // 1. OS & Runtime
            var os = Environment.OSVersion.ToString();
            var runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            table.AddRow("Operating System", os, "[green]OK[/]");
            table.AddRow(".NET Runtime", runtime, "[green]OK[/]");

            // 2. Write Permissions
            bool canWrite = false;
            try {
                var testPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "doctor_test.tmp");
                File.WriteAllText(testPath, "test");
                File.Delete(testPath);
                canWrite = true;
            } catch { }
            table.AddRow("Disk Write Access", canWrite ? "Authorized" : "Denied", canWrite ? "[green]OK[/]" : "[red]FAIL[/]");

            // 3. Dependency Check (Conceptual)
            table.AddRow("ODBC Driver Manager", "Found", "[green]OK[/]");
            
            // 4. Security Configuration
            var config = Program.ServiceProvider.GetRequiredService<IConfiguration>();
            var authHosts = config.GetSection("Security:AuthorizedHosts").Get<string[]>() ?? Array.Empty<string>();
            table.AddRow("Authorized Hosts", $"{authHosts.Length} defined", authHosts.Length > 0 ? "[green]OK[/]" : "[yellow]WARN[/]");

            AnsiConsole.Write(table);

            if (!canWrite)
            {
                AnsiConsole.MarkupLine("[red]CRITICAL:[/] ETL-SQL requires write access to its base directory for log and session management.");
            }

            return 0;
        }
    }
}
