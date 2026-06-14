using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Reporting;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace ETL_SQL.App
{
    public class EngineRunner
    {
        public static async Task<int> Run(CliContext ctx)
        {
            var logger = Program.ServiceProvider.GetRequiredService<ILogger>();
            var loggerService = Program.ServiceProvider.GetRequiredService<ILoggerService>();
            var registry = Program.ServiceProvider.GetRequiredService<IConnectorRegistry>();

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

            if (ctx.Command == "gen-script")
            {
                if (string.IsNullOrEmpty(ctx.SpecSchema) || string.IsNullOrEmpty(ctx.SpecOutput))
                {
                    logger.WriteLine("Both --schema (-s) and --output (-o) options are required for the gen-script command.", ConsoleColor.Red);
                    return 1;
                }
                return await PipelineGenerator.Generate(ctx.SpecSchema, ctx.SpecOutput, logger);
            }

            if (ctx.Command == "extract-spec")
            {
                if (string.IsNullOrEmpty(ctx.ExtractInput) || string.IsNullOrEmpty(ctx.ExtractOutput))
                {
                    logger.WriteLine("Both --input (-i) and --output (-o) options are required for the extract-spec command.", ConsoleColor.Red);
                    return 1;
                }
                return SpecExtractor.Extract(ctx.ExtractInput, ctx.ExtractOutput, logger);
            }

            if (ctx.Command == "notices")
            {
                ShowThirdPartyNotices(logger);
                return 0;
            }

            if (ctx.Command == "serve")
            {
                return await ServeReport(ctx, logger);
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
                return await RunDoctor(ctx, logger);
            }

            if (ctx.Command == "admin-support-bundle")
            {
                return await SupportBundleBuilder.RunAsync(ctx, logger);
            }

            if (ctx.Command == "init")
            {
                return await InitScaffolder.RunAsync(ctx, logger);
            }

            if (ctx.Command == "config-setup-jwt")
            {
                return await RunSetupJwt(logger, ctx.UpdateConfig);
            }

            if (ctx.Command == "purge")
            {
                return RunPurge(ctx);
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

                var engineLogger = logger as LoggerService;
                if (engineLogger != null)
                {
                    engineLogger.IsSilent = ctx.IsSilentMode;
                    engineLogger.IsVerbose = ctx.IsVerbose;
                }

                if (ctx.IsLogMode)
                {
                    // Read script-log config from the DI container's IConfiguration (graceful defaults)
                    var config = Program.ServiceProvider.GetService<IConfiguration>();
                    string scriptLogDir = config?["Logging:ScriptLog:Directory"] ?? "logs/scripts";
                    int scriptRetention = int.TryParse(config?["Logging:ScriptLog:DefaultRetentionDays"], out var sr) ? sr : 30;
                    int scriptSizeLimitMb = int.TryParse(config?["Logging:ScriptLog:FileSizeLimitMb"], out var ss) ? ss : 10;

                    // --log-path override still respected
                    if (!string.IsNullOrWhiteSpace(ctx.LogPath))
                        scriptLogDir = ctx.LogPath;

                    loggerService?.InitializeScriptLogger(ctx.ScriptFile.Name, scriptLogDir, scriptRetention, scriptSizeLimitMb);
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
                        foreach (var err in errors)
                        {
                            Console.Error.WriteLine($"Syntax Error at line {err.Line}, col {err.Column}: {err.Message}");
                        }
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { type = "done", exitCode = 1, uri = ctx.ScriptFile.FullName }));
                    }
                    else
                    {
                        logger.WriteLine("Parsing failed:", ConsoleColor.Red);
                        foreach (var err in errors)
                        {
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

                var lintInfos = lintResults.Where(r => r.Severity == LintSeverity.Info).ToList();
                if (lintInfos.Any() && !ctx.IsSilentMode)
                {
                    foreach (var i in lintInfos)
                    {
                        logger.WriteLine($"  - Linter Info: {i.Message} (Line {i.LineNumber}, Col {i.ColumnNumber})", ConsoleColor.Cyan);
                    }
                }

                var lintWarnings = lintResults.Where(r => r.Severity == LintSeverity.Warning).ToList();
                if (lintWarnings.Any() && !ctx.IsSilentMode)
                {
                    foreach (var w in lintWarnings)
                    {
                        logger.WriteLine($"  - Linter Warning: {w.Message} (Line {w.LineNumber}, Col {w.ColumnNumber})", ConsoleColor.Yellow);
                    }
                }

                var lintErrors = lintResults.Where(r => r.Severity == LintSeverity.Error).ToList();
                if (lintErrors.Any())
                {
                    if (ctx.IsJsonMode)
                    {
                        foreach (var err in lintErrors)
                        {
                            Console.Error.WriteLine($"Linter Error at line {err.LineNumber}, col {err.ColumnNumber}: {err.Message}");
                        }
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { type = "done", exitCode = 1, uri = ctx.ScriptFile.FullName }));
                    }
                    else
                    {
                        logger.WriteLine("Linting failed:", ConsoleColor.Red);
                        foreach (var err in lintErrors)
                        {
                            logger.WriteLine($"  - Line {err.LineNumber}, Col {err.ColumnNumber}: {err.Message}", ConsoleColor.Yellow);
                        }
                    }
                    return 1;
                }

                var engineConfig = Program.ServiceProvider.GetRequiredService<IConfiguration>();
                bool auditAdHoc = engineConfig.GetValue<bool>("Engine:AuditAdHocRuns");
                long auditHistoryId = -1L;
                string? runTimeHash = null;
                IJobHistoryStore? historyStore = null;

                try
                {
                    logger.WriteLine("Execution phase...");
                    await using var evaluator = Program.ServiceProvider.GetRequiredService<Evaluator>();
                    evaluator.BatchSize = ctx.BatchSize;
                    evaluator.IsVerbose = ctx.IsVerbose;
                    logger.IsVerbose = ctx.IsVerbose;
                    evaluator.MasterPassword = ctx.Password;
                    evaluator.SessionId = ctx.SessionId;
                    evaluator.CurrentScriptPath = ctx.ScriptFile.FullName;

                    if (System.IO.File.Exists(ctx.ScriptFile.FullName))
                    {
                        var bytes = await System.IO.File.ReadAllBytesAsync(ctx.ScriptFile.FullName);
                        runTimeHash = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                    }

                    if (auditAdHoc)
                    {
                        historyStore = Program.ServiceProvider.GetService<IJobHistoryStore>();
                        if (historyStore != null)
                        {
                            try
                            {
                                var jobName = Path.GetFileName(ctx.ScriptFile.FullName);
                                auditHistoryId = await historyStore.LogJobStartAsync(jobName);
                            }
                            catch (Exception ex)
                            {
                                logger.WriteLine($"[history catalog] Failed to log execution start: {ex.Message}", ConsoleColor.DarkYellow);
                            }
                        }
                    }

                    // Security Hardening: Register the script directory as an approved safe zone for overrides
                    var scriptDir = Path.GetDirectoryName(ctx.ScriptFile.FullName);
                    if (!string.IsNullOrEmpty(scriptDir))
                    {
                        // Rpt-2: Cleanup orphaned snapshot temp files from previous failed runs
                        ETL_SQL.Reporting.SnapshotStore.CleanupOrphanedSnapshots(scriptDir);

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

                    if (ctx.Resume && string.IsNullOrEmpty(ctx.SessionId))
                    {
                        logger.WriteLine("Error: --resume requires --session to be specified.", ConsoleColor.Red);
                        return 1;
                    }

                    if (!string.IsNullOrEmpty(ctx.SessionId))
                    {
                        evaluator.IsPersistentSession = true;
                        evaluator.SessionId = ctx.SessionId;
                        evaluator.SessionRoot = sessionManager.SessionRoot;

                        if (ctx.Resume)
                        {
                            var state = await sessionManager.LoadSession(ctx.SessionId);
                            if (state != null)
                            {
                                logger.WriteLine($"Restoring session {ctx.SessionId}...", ConsoleColor.Cyan);
                                await evaluator.LoadSessionState(state);
                                evaluator.IsResuming = true;
                            }
                            else
                            {
                                logger.WriteLine($"Error: --resume specified but no saved session found for '{ctx.SessionId}'. Run without --resume to start fresh.", ConsoleColor.Red);
                                return 1;
                            }
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
                        var tree = evaluator.Telemetry.ExecutionTree;
                        var visualizer = new ExecuteTreeVisualizer(tree);
                        treeCts = new CancellationTokenSource();
                        treeRenderTask = visualizer.RenderLiveAsync(treeCts.Token);

                        // If we are showing the tree, we might want to suppress some logs to avoid flickering
                        // but let's keep it simple for now as requested.
                    }
                    else if (ctx.IsJsonMode)
                    {
                        // In JSON mode, we emit "progress" packets for the VS Code extension
                        var tree = evaluator.Telemetry.ExecutionTree;
                        treeCts = new CancellationTokenSource();

                        evaluator.Telemetry.IsProfiling = true;
                        evaluator.DisplayExecuteTree = true;

                        // Initial clear signal for the VS Code extension
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            type = "clear",
                            uri = ctx.ScriptFile.FullName,
                            target = "all"
                        }));

                        // Initial flush to ensure the root node is visible immediately
                        var initialSnapshot = new
                        {
                            type = "progress",
                            uri = ctx.ScriptFile.FullName,
                            data = tree.ToSnapshot()
                        };
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(initialSnapshot));

                        treeRenderTask = Task.Run(async () =>
                        {
                            while (!treeCts.Token.IsCancellationRequested)
                            {
                                var snapshot = new
                                {
                                    type = "progress",
                                    uri = ctx.ScriptFile.FullName,
                                    data = tree.ToSnapshot()
                                };
                                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(snapshot));

                                // Emit variables state
                                var vars = new
                                {
                                    type = "variables",
                                    uri = ctx.ScriptFile.FullName,
                                    data = evaluator.VarContext.GetVariablesWithMetadata().Select(kv => new
                                    {
                                        name = kv.Key,
                                        value = kv.Value.Value?.ToString() ?? "null",
                                        type = kv.Value.Value?.GetType().Name ?? "null"
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
                        if (treeRenderTask != null)
                        {
                            try { await treeRenderTask; } catch (TaskCanceledException) { /* Expected */ }
                        }

                        // Final flush for fast scripts in JSON mode
                        if (ctx.IsJsonMode && !ctx.IsSilentMode)
                        {
                            var finalSnapshot = new
                            {
                                type = "progress",
                                uri = ctx.ScriptFile.FullName,
                                data = evaluator.Telemetry.ExecutionTree.ToSnapshot()
                            };
                            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(finalSnapshot));

                            // Final variables flush
                            var finalVars = new
                            {
                                type = "variables",
                                uri = ctx.ScriptFile.FullName,
                                data = evaluator.VarContext.GetVariablesWithMetadata().Select(kv => new
                                {
                                    name = kv.Key,
                                    value = kv.Value.Value?.ToString() ?? "null",
                                    type = kv.Value.Value?.GetType().Name ?? "null"
                                }).ToList()
                            };
                            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(finalVars));

                            // Emit performance telemetry
                            if (evaluator.Telemetry.IsProfiling)
                            {
                                var perf = new
                                {
                                    type = "performance",
                                    uri = ctx.ScriptFile.FullName,
                                    data = new
                                    {
                                        totalDurationMs = execTime.ElapsedMilliseconds,
                                        statements = evaluator.Telemetry.ProfileMetrics.Select(m => new
                                        {
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

                    if (!string.IsNullOrEmpty(ctx.SessionId) && evaluator.IsPersistentSession)
                    {
                        logger.WriteLine($"Saving session {ctx.SessionId}...", ConsoleColor.Cyan);
                        await sessionManager.SaveSession(ctx.SessionId, evaluator, source);
                    }

                    if (ctx.IsPerfMode || evaluator.Telemetry.IsProfiling)
                    {
                        var memUsageBytes = GC.GetTotalMemory(false) - startMem;
                        double memUsageMb = Math.Round((double)memUsageBytes / (1024 * 1024), 2);
                        if (memUsageMb < 0) memUsageMb = 0;
                        double rowsPerSec = execTime.Elapsed.TotalSeconds > 0 ? Math.Round(evaluator.Telemetry.RowsProcessed / execTime.Elapsed.TotalSeconds, 0) : evaluator.Telemetry.RowsProcessed;

                        if (ctx.IsJsonMode)
                        {
                            var perfPacket = new
                            {
                                type = "performance",
                                uri = ctx.ScriptFile.FullName,
                                metrics = new
                                {
                                    lexerMs = lexTime.ElapsedMilliseconds,
                                    parserMs = parseTime.ElapsedMilliseconds,
                                    executionMs = execTime.ElapsedMilliseconds,
                                    memoryMb = memUsageMb,
                                    spilledMb = Math.Round((double)evaluator.Telemetry.TotalSpilledBytes / (1024 * 1024), 2),
                                    partitions = evaluator.Telemetry.PartitionsCount,
                                    maxRecursion = evaluator.MaxRecursiveDepth,
                                    rowsProcessed = evaluator.Telemetry.RowsProcessed,
                                    rowsPerSecond = rowsPerSec,
                                    statements = evaluator.Telemetry.ProfileMetrics.Select(m => new
                                    {
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
                        else if (!ctx.IsSilentMode)
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
                            table.AddRow("Total Rows Processed", evaluator.Telemetry.RowsProcessed.ToString("N0"));
                            table.AddRow("Throughput (Rows/s)", rowsPerSec.ToString("N0"));
                            table.AddRow("Approx. RAM Peak", $"{memUsageMb} MB");

                            if (evaluator.Telemetry.TotalSpilledBytes > 0)
                                table.AddRow("Disk Spilled", $"[yellow]{Math.Round((double)evaluator.Telemetry.TotalSpilledBytes / (1024 * 1024), 2)} MB[/]");

                            if (evaluator.Telemetry.PartitionsCount > 0)
                                table.AddRow("Partitions Used", evaluator.Telemetry.PartitionsCount.ToString());

                            if (evaluator.MaxRecursiveDepth > 0)
                                table.AddRow("Max Recursion Depth", evaluator.MaxRecursiveDepth.ToString());

                            AnsiConsole.Write(table);
                            Console.WriteLine();
                        }
                    }
                    else if (!ctx.IsJsonMode)
                    {
                        logger.WriteLine($"Execution finished in {execTime.ElapsedMilliseconds}ms.", ConsoleColor.Green);
                        logger.WriteLine($"Rows affected: {evaluator.Telemetry.RowsProcessed}");
                    }

                    if (evaluator.DockerManager.HasActiveContainers)
                    {
                        if (ctx.IsJsonMode)
                        {
                            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                            {
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

                    if (ctx.IsJsonMode)
                    {
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { type = "done", exitCode = 0, uri = ctx.ScriptFile.FullName }));
                    }

                    if (auditAdHoc && historyStore != null && auditHistoryId != -1L)
                    {
                        try
                        {
                            var totalCpu = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
                            await historyStore.LogJobEndAsync(
                                auditHistoryId,
                                "COMPLETED",
                                null,
                                evaluator.Telemetry.RowsProcessed,
                                Process.GetCurrentProcess().PeakWorkingSet64,
                                totalCpu,
                                runTimeHash,
                                true);
                        }
                        catch (Exception ex)
                        {
                            logger.WriteLine($"[history catalog] Failed to log execution end: {ex.Message}", ConsoleColor.DarkYellow);
                        }
                    }

                    if (evaluator.LineageEnabled)
                    {
                        try
                        {
                            var lineage = evaluator.LineageTracker.GetFullLineage().ToList();
                            if (lineage.Count > 0)
                            {
                                var lineageCatalog = Program.ServiceProvider.GetService<ILineageCatalogStore>();
                                if (lineageCatalog != null)
                                {
                                    var jobName = Path.GetFileName(ctx.ScriptFile.FullName);
                                    await lineageCatalog.SaveLineageAsync(lineage, jobName, ctx.ScriptFile.FullName, DateTime.UtcNow);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.WriteLine($"[lineage catalog] Failed to persist lineage: {ex.Message}", ConsoleColor.DarkYellow);
                        }
                    }

                    return 0;
                }
                catch (ExecutionException ex)
                {
                    if (ctx.IsJsonMode)
                    {
                        Console.Error.WriteLine($"Execution Error at line {ex.Line}, col {ex.Column}: {ex.Message} (Code: {ex.ErrorNumber})");
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { type = "done", exitCode = 1, uri = ctx.ScriptFile.FullName }));
                    }
                    else
                    {
                        logger.WriteLine($"Execution Error: {ex.Message}", ConsoleColor.Red);
                        logger.WriteLine($"  - Line: {ex.Line}, Column: {ex.Column}", ConsoleColor.Yellow);
                        if (ex.ErrorNumber > 0) logger.WriteLine($"  - Error Number: {ex.ErrorNumber}", ConsoleColor.Yellow);
                    }

                    if (auditAdHoc && historyStore != null && auditHistoryId != -1L)
                    {
                        try
                        {
                            await historyStore.LogJobEndAsync(
                                auditHistoryId,
                                "FAILED",
                                ex.Message,
                                0,
                                Process.GetCurrentProcess().PeakWorkingSet64,
                                0,
                                runTimeHash,
                                false);
                        }
                        catch { }
                    }

                    return 1;
                }
                catch (Exception ex)
                {
                    if (ctx.IsJsonMode)
                    {
                        Console.Error.WriteLine($"Fatal Error: {ex.Message}");
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { type = "done", exitCode = 1, uri = ctx.ScriptFile.FullName }));
                    }
                    else
                    {
                        logger.WriteLine($"Fatal Error: {ex.Message}", ConsoleColor.Red);
                        if (ctx.IsVerbose) logger.WriteLine(ex.StackTrace ?? "", ConsoleColor.DarkGray);
                    }

                    if (auditAdHoc && historyStore != null && auditHistoryId != -1L)
                    {
                        try
                        {
                            await historyStore.LogJobEndAsync(
                                auditHistoryId,
                                "FAILED",
                                ex.Message,
                                0,
                                Process.GetCurrentProcess().PeakWorkingSet64,
                                0,
                                runTimeHash,
                                false);
                        }
                        catch { }
                    }

                    return 1;
                }
            }

            logger.WriteLine($"Unknown command: {ctx.Command}", ConsoleColor.Red);
            return 1;
        }

        internal static async Task<int> RunDoctor(CliContext ctx, ILogger logger)
        {
            bool isJson = ctx.IsJsonMode;
            bool isStrict = ctx.DoctorStrict;
            bool isFull = string.Equals(ctx.DoctorProfile, "full", StringComparison.OrdinalIgnoreCase);

            var previousLoggerSuppressConsole = logger.SuppressConsole;
            var previousLoggerJsonMode = logger.IsJsonMode;
            var previousResultSuppressOutput = ResultFormatter.SuppressOutput;
            if (isJson)
            {
                logger.SuppressConsole = true;
                logger.IsJsonMode = true;
                ResultFormatter.SuppressOutput = true;
            }

            if (!isJson)
            {
                AnsiConsole.Write(new FigletText("ETL-SQL Doctor").Centered().Color(Color.DeepSkyBlue1));
                AnsiConsole.Write(new Rule(isFull ? "[yellow]System Health Check (full)[/]" : "[yellow]System Health Check (quick)[/]").RuleStyle("grey"));
                Console.WriteLine();
            }

            var checks = new List<(string Name, string Detail, string Status)>();

            // 1. OS & Runtime
            var os = Environment.OSVersion.ToString();
            var runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            checks.Add(("Operating System", os, "OK"));
            checks.Add((".NET Runtime", runtime, "OK"));

            // 2. Base directory write permissions
            bool canWrite = false;
            try
            {
                var testPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".doctor_test.tmp");
                await File.WriteAllTextAsync(testPath, "test");
                File.Delete(testPath);
                canWrite = true;
            }
            catch { }
            checks.Add(("Base Directory Write", canWrite ? AppDomain.CurrentDomain.BaseDirectory : "Denied", canWrite ? "OK" : "FAIL"));

            // 3. Temp directory write permissions
            bool canWriteTemp = false;
            try
            {
                var tmpPath = Path.Combine(Path.GetTempPath(), $".etlsql_doctor_{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(tmpPath, "test");
                File.Delete(tmpPath);
                canWriteTemp = true;
            }
            catch { }
            checks.Add(("Temp Directory Write", canWriteTemp ? Path.GetTempPath() : "Denied", canWriteTemp ? "OK" : "FAIL"));

            // 4. Available disk space (warn if under 500 MB on the base drive)
            string diskStatus = "OK";
            string diskDetail = "Unknown";
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory) ?? "/");
                var freeMb = drive.AvailableFreeSpace / (1024 * 1024);
                diskDetail = $"{freeMb:N0} MB free on {drive.Name}";
                diskStatus = freeMb < 500 ? "WARN" : "OK";
            }
            catch (Exception ex) { diskDetail = ex.Message; diskStatus = "WARN"; }
            checks.Add(("Disk Space", diskDetail, diskStatus));

            // 5. ODBC Driver Manager
            string odbcStatus = "OK";
            string odbcDetail = "Found";
            try
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    var odbcDll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "odbc32.dll");
                    odbcDetail = File.Exists(odbcDll) ? "odbc32.dll present" : "odbc32.dll not found";
                    if (!File.Exists(odbcDll)) odbcStatus = "WARN";
                }
                else
                {
                    odbcDetail = File.Exists("/etc/odbcinst.ini") ? "odbcinst.ini present" : "odbcinst.ini not found";
                    if (!File.Exists("/etc/odbcinst.ini")) odbcStatus = "WARN";
                }
            }
            catch { odbcDetail = "Check skipped (non-critical)"; odbcStatus = "WARN"; }
            checks.Add(("ODBC Driver Manager", odbcDetail, odbcStatus));

            // 6. AppSettings.json present
            var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            bool hasAppSettings = File.Exists(appSettingsPath);
            checks.Add(("appsettings.json", hasAppSettings ? appSettingsPath : "Not found", hasAppSettings ? "OK" : "WARN"));

            // 7. Security configuration
            var config = Program.ServiceProvider.GetRequiredService<IConfiguration>();
            var authHosts = config.GetSection("Security:AuthorizedHosts").Get<string[]>() ?? Array.Empty<string>();
            checks.Add(("Authorized Hosts", $"{authHosts.Length} defined", authHosts.Length > 0 ? "OK" : "WARN"));

            // 8. Registered connectors
            try
            {
                var registry = Program.ServiceProvider.GetRequiredService<IConnectorRegistry>();
                var connectorNames = registry.GetRegisteredNames();
                checks.Add(("Registered Connectors", string.Join(", ", connectorNames), "OK"));
            }
            catch (Exception ex)
            {
                checks.Add(("Registered Connectors", $"Registry unavailable: {ex.Message}", "WARN"));
            }

            // 9. Orchestrator SQLite history DB
            string dbStatus = "OK";
            string dbDetail = "Not configured";
            try
            {
                var dbPath = config["Orchestrator:HistoryDbPath"];
                if (!string.IsNullOrEmpty(dbPath))
                {
                    dbDetail = dbPath;
                    dbStatus = File.Exists(dbPath) ? "OK" : "WARN";
                    if (!File.Exists(dbPath)) dbDetail += " (not yet created — will be created on first run)";
                }
                else
                {
                    dbStatus = "WARN";
                    dbDetail = "Orchestrator:HistoryDbPath not set";
                }
            }
            catch (Exception ex) { dbDetail = ex.Message; dbStatus = "WARN"; }
            checks.Add(("Orchestrator History DB", dbDetail, dbStatus));

            // 10. Log directory write access (quick)
            var appLogDir = config["Logging:AppLog:Directory"] ?? "logs/app";
            var scriptLogDir = config["Logging:ScriptLog:Directory"] ?? "logs/scripts";
            foreach (var (label, dir) in new[] { ("App Log Dir", appLogDir), ("Script Log Dir", scriptLogDir) })
            {
                bool canWriteDir = false;
                string dirDetail = dir;
                try
                {
                    Directory.CreateDirectory(dir);
                    var probe = Path.Combine(dir, $".doctor_{Guid.NewGuid():N}.tmp");
                    await File.WriteAllTextAsync(probe, "test");
                    File.Delete(probe);
                    canWriteDir = true;
                }
                catch { }
                checks.Add((label, dirDetail, canWriteDir ? "OK" : "WARN"));
            }

            // ── Full profile checks ──────────────────────────────────────────────
            if (isFull)
            {
                // 11. Parser/lexer smoke
                string parseStatus = "OK";
                string parseDetail = "SELECT 1 AS n parsed successfully";
                try
                {
                    var smokeScript = new Parser(new Lexer("SELECT 1 AS n;").Tokenize()).Parse();
                    if (smokeScript.Statements.Count == 0)
                    {
                        parseStatus = "FAIL";
                        parseDetail = "Parser returned 0 statements";
                    }
                }
                catch (Exception ex) { parseStatus = "FAIL"; parseDetail = ex.Message; }
                checks.Add(("Parser Smoke", parseDetail, parseStatus));

                // 12. Engine execution smoke (MOCKDB)
                string engineStatus = "OK";
                string engineDetail = "SELECT * FROM MOCKDB executed successfully";
                try
                {
                    var smokeEval = Program.ServiceProvider.GetRequiredService<Evaluator>();
                    smokeEval.SecurityService.IsTestMode = true;
                    var smokeScript = new Parser(new Lexer(
                        "CREATE CONNECTION _doctor_mock AS MOCKDB(); SELECT * FROM _doctor_mock.Users;").Tokenize()).Parse();
                    await smokeEval.Evaluate(smokeScript);
                    engineDetail = $"MOCKDB query returned {smokeEval.LastResult?.Rows.Count ?? 0} row(s)";
                }
                catch (Exception ex) { engineStatus = "FAIL"; engineDetail = ex.Message; }
                checks.Add(("Engine Smoke (MOCKDB)", engineDetail, engineStatus));

                // 13. ENC: encrypt/decrypt round-trip
                string encStatus = "OK";
                string encDetail = "ENC: round-trip verified";
                try
                {
                    var plaintext = "doctor-smoke-" + Guid.NewGuid().ToString("N");
                    var tempPass = "doctor-temp-key";
                    var cipher = CryptoUtils.Encrypt(plaintext, tempPass);
                    var restored = CryptoUtils.Decrypt(cipher, tempPass);
                    if (restored != plaintext)
                    {
                        encStatus = "FAIL";
                        encDetail = "Decrypted value did not match original";
                    }
                }
                catch (Exception ex) { encStatus = "FAIL"; encDetail = ex.Message; }
                checks.Add(("ENC: Round-Trip", encDetail, encStatus));

                // 14. Linter smoke
                string lintStatus = "OK";
                string lintDetail = "No linter errors on SELECT 1";
                try
                {
                    var lintScript = new Parser(new Lexer("SELECT 1 AS n;").Tokenize()).Parse();
                    var linter = ETL_SQL.Analysis.Linting.LinterFactory.CreateWithAllRules(Program.ServiceProvider);
                    var lintContext = new ETL_SQL.Analysis.Linting.DefaultLintContext { DocumentUri = "doctor-smoke" };
                    var lintResults = await linter.AnalyzeAsync(lintScript, lintContext);
                    var errors = lintResults.Count(r => r.Severity == ETL_SQL.Analysis.Linting.LintSeverity.Error);
                    if (errors > 0)
                    {
                        lintStatus = "FAIL";
                        lintDetail = $"Linter returned {errors} error(s) on simple SELECT";
                    }
                }
                catch (Exception ex) { lintStatus = "FAIL"; lintDetail = ex.Message; }
                checks.Add(("Linter Smoke", lintDetail, lintStatus));

                // 15. Security guardrail smoke
                string secGuardStatus = "OK";
                string secGuardDetail = "Restricted system path correctly rejected";
                try
                {
                    var secSmoke = new SecurityService(NullLogger.Instance);
                    secSmoke.IsTestMode = false;
                    bool threwOnBlocked = false;
                    var blockedSamplePath = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "test.dll")
                        : "/etc/passwd";
                    try { secSmoke.ValidatePath(blockedSamplePath); }
                    catch (SecurityException) { threwOnBlocked = true; }

                    if (!threwOnBlocked)
                    {
                        secGuardStatus = "FAIL";
                        secGuardDetail = $"Guardrail did not reject: {blockedSamplePath}";
                    }
                }
                catch (Exception ex) { secGuardStatus = "FAIL"; secGuardDetail = ex.Message; }
                checks.Add(("Security Guardrail Smoke", secGuardDetail, secGuardStatus));

                // 16. Report build smoke
                string rptBuildStatus = "OK";
                string rptBuildDetail = "Report manifest build produced 1 page and 1 visual";
                string? rptSmokePath = null;
                try
                {
                    var rptSource = @"
SET REPORT TITLE = 'Doctor Report Smoke';
SELECT 'Health' AS Label, 1 AS Value INTO #DoctorReportSmoke;

CREATE VISUAL DoctorHealth AS CARD (
    SOURCE = (SELECT Label, Value FROM #DoctorReportSmoke),
    MAPPINGS (VALUE = Value, LABEL = Label)
);

CREATE PAGE Main AS DASHBOARD (
    STRUCTURE = 'A',
    MAP('A' = DoctorHealth)
);
";
                    rptSmokePath = Path.Combine(Path.GetTempPath(), $"etl_sql_doctor_report_{Guid.NewGuid():N}.rptsql");
                    await File.WriteAllTextAsync(rptSmokePath, rptSource);

                    var rptScript = new Parser(new Lexer(rptSource).Tokenize(), rptSource).Parse();
                    await using var rptEval = Program.ServiceProvider.GetRequiredService<Evaluator>();
                    rptEval.SecurityService.IsTestMode = true;
                    rptEval.CurrentScriptPath = rptSmokePath;
                    await rptEval.Evaluate(rptScript);

                    var manifest = await new ManifestBuilder(rptEval).BuildAsync(rptSmokePath);
                    if (!string.IsNullOrWhiteSpace(manifest.Error))
                    {
                        rptBuildStatus = "FAIL";
                        rptBuildDetail = manifest.Error;
                    }
                    else if (manifest.Pages.Count != 1 || manifest.Visuals.Count != 1)
                    {
                        rptBuildStatus = "FAIL";
                        rptBuildDetail = $"Expected 1 page/1 visual, got {manifest.Pages.Count} page(s)/{manifest.Visuals.Count} visual(s)";
                    }
                    else if (manifest.Visuals[0].Rows.Count != 1)
                    {
                        rptBuildStatus = "FAIL";
                        rptBuildDetail = $"Expected 1 visual row, got {manifest.Visuals[0].Rows.Count}";
                    }
                }
                catch (Exception ex) { rptBuildStatus = "FAIL"; rptBuildDetail = ex.Message; }
                finally
                {
                    if (!string.IsNullOrEmpty(rptSmokePath) && File.Exists(rptSmokePath))
                        File.Delete(rptSmokePath);
                }
                checks.Add(("Report Build Smoke", rptBuildDetail, rptBuildStatus));

                // 17. Report PDF export smoke
                string pdfStatus = "OK";
                string pdfDetail = "PDF exporter produced a valid PDF payload";
                try
                {
                    var pdfManifest = new ReportManifest
                    {
                        Source = "doctor-pdf-smoke",
                        Title = "Doctor PDF Smoke"
                    };
                    pdfManifest.Visuals.Add(new VisualManifest
                    {
                        Name = "DoctorHealth",
                        VisualType = "CARD",
                        Columns = new List<string> { "Value" },
                        Rows = new List<List<string?>> { new() { "1" } }
                    });
                    pdfManifest.Pages.Add(new PageManifest
                    {
                        Name = "Main",
                        Structure = "A",
                        SlotMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["A"] = "DoctorHealth"
                        }
                    });

                    var pdfBytes = new PdfExporter().Export(pdfManifest);
                    if (pdfBytes.Length < 5
                        || pdfBytes[0] != (byte)'%'
                        || pdfBytes[1] != (byte)'P'
                        || pdfBytes[2] != (byte)'D'
                        || pdfBytes[3] != (byte)'F'
                        || pdfBytes[4] != (byte)'-')
                    {
                        pdfStatus = "FAIL";
                        pdfDetail = $"PDF exporter returned {pdfBytes.Length} byte(s) without a PDF header";
                    }
                }
                catch (Exception ex) { pdfStatus = "FAIL"; pdfDetail = ex.Message; }
                checks.Add(("Report PDF Export", pdfDetail, pdfStatus));

                // 18. Optional render/runtime capabilities
                var graphvizRequired = bool.TryParse(config["Doctor:RequireGraphviz"], out var requireGraphviz) && requireGraphviz;
                var graphvizPath = FindExecutableOnPath("dot");
                checks.Add(("Graphviz (optional)",
                    graphvizPath != null
                        ? $"dot found at {graphvizPath}"
                        : graphvizRequired
                            ? "Doctor:RequireGraphviz=true but dot was not found in PATH"
                            : "dot not found; Graphviz-dependent exports are unavailable",
                    graphvizPath != null ? "OK" : graphvizRequired ? "WARN" : "OK"));

                var browserRequired = bool.TryParse(config["Doctor:RequireBrowser"], out var requireBrowser) && requireBrowser;
                var browserPath = FindExecutableOnPath("msedge", "chrome", "chromium", "chromium-browser", "google-chrome");
                checks.Add(("Browser Runtime (optional)",
                    browserPath != null
                        ? $"Browser runtime found at {browserPath}"
                        : browserRequired
                            ? "Doctor:RequireBrowser=true but no supported browser runtime was found in PATH"
                            : "No external browser runtime required for built-in report export",
                    browserPath != null ? "OK" : browserRequired ? "WARN" : "OK"));

                // 19. Shared runtime asset drift (source context only)
                string driftStatus = "OK";
                string driftDetail = "Not running from a source checkout — skipped";
                var syncScript = FindRepoFile(Path.Combine("scripts", "sync-assets.ps1"));
                if (syncScript != null)
                {
                    try
                    {
                        var pwshExe = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "pwsh.exe" : "pwsh";
                        var psi = new ProcessStartInfo
                        {
                            FileName = pwshExe,
                            Arguments = $"-NoProfile -NonInteractive -File \"{syncScript}\" -Check",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                            await proc.WaitForExitAsync(cts.Token);
                            driftDetail = proc.ExitCode == 0 ? "Runtime assets in sync" : "Asset drift detected — run sync-assets.ps1 to fix";
                            driftStatus = proc.ExitCode == 0 ? "OK" : "WARN";
                        }
                        else { driftDetail = "Could not start pwsh for drift check"; driftStatus = "WARN"; }
                    }
                    catch (Exception ex) { driftStatus = "WARN"; driftDetail = $"Drift check skipped: {ex.Message}"; }
                }
                checks.Add(("Asset Drift Check", driftDetail, driftStatus));

                // 20. Node.js (optional dependency for extension builds)
                string nodeStatus = "WARN";
                string nodeDetail = "Node.js not found — VS Code extension build unavailable";
                try
                {
                    var nodeExe = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "node.exe" : "node";
                    var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
                    var nodeFound = pathDirs.Any(dir => { try { return File.Exists(Path.Combine(dir, nodeExe)); } catch { return false; } });
                    if (nodeFound) { nodeStatus = "OK"; nodeDetail = "Node.js found in PATH"; }
                }
                catch { }
                checks.Add(("Node.js (optional)", nodeDetail, nodeStatus));

                // 21. Portal database
                string portalDbStatus = "OK";
                string portalDbDetail = "Portal:DatabasePath not configured";
                try
                {
                    var portalDb = config["Portal:DatabasePath"];
                    if (!string.IsNullOrEmpty(portalDb))
                    {
                        portalDbDetail = File.Exists(portalDb)
                            ? $"{portalDb} (initialized)"
                            : $"{portalDb} (not yet created — will be created on first portal run)";
                        if (!File.Exists(portalDb)) portalDbStatus = "WARN";
                    }
                    else { portalDbStatus = "WARN"; }
                }
                catch (Exception ex) { portalDbStatus = "WARN"; portalDbDetail = ex.Message; }
                checks.Add(("Portal Database", portalDbDetail, portalDbStatus));

                // 22. Optional service endpoint checks. These only probe configured endpoints.
                checks.Add(await ProbeHttpEndpointAsync(
                    "Report Portal /health",
                    ResolveHealthUrl(
                        config["Doctor:ReportPortalHealthUrl"],
                        config["Portal:HealthUrl"],
                        config["Portal:BaseUrl"]),
                    "Report Portal health URL not configured"));

                checks.Add(await ProbeHttpEndpointAsync(
                    "Orchestrator /health",
                    ResolveHealthUrl(
                        config["Doctor:OrchestratorHealthUrl"],
                        config["Orchestrator:HealthUrl"],
                        config["Portal:Orchestrator:ApiUrl"],
                        config["Orchestrator:ApiUrl"]),
                    "Orchestrator health URL not configured"));

                checks.Add(await ProbeTcpEndpointAsync(
                    "SMTP Endpoint",
                    config["Doctor:SmtpHost"] ?? config["SMTP:Host"] ?? config["Smtp:Host"],
                    ConfigInt(config, "Doctor:SmtpPort", "SMTP:Port", "Smtp:Port") ?? 25,
                    "SMTP endpoint not configured"));

                checks.Add(await ProbeTcpEndpointAsync(
                    "SFTP Endpoint",
                    config["Doctor:SftpHost"] ?? config["SFTP:Host"] ?? config["Sftp:Host"],
                    ConfigInt(config, "Doctor:SftpPort", "SFTP:Port", "Sftp:Port") ?? 22,
                    "SFTP endpoint not configured"));

                checks.Add(await ProbeHttpEndpointAsync(
                    "Azure Blob Endpoint",
                    config["Doctor:AzureBlobEndpoint"] ?? config["AzureBlob:BlobEndpoint"] ?? config["AzureBlob:Endpoint"],
                    "Azure Blob endpoint not configured"));
            }

            bool hasFailures = checks.Any(c => c.Status == "FAIL");
            bool hasWarnings = checks.Any(c => c.Status == "WARN");

            logger.SuppressConsole = previousLoggerSuppressConsole;
            logger.IsJsonMode = previousLoggerJsonMode;
            ResultFormatter.SuppressOutput = previousResultSuppressOutput;

            if (isJson)
            {
                var result = new System.Text.Json.Nodes.JsonObject
                {
                    ["overall"] = hasFailures ? "FAIL" : hasWarnings ? "WARN" : "OK",
                    ["checks"] = new System.Text.Json.Nodes.JsonArray(
                        checks.Select(c => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                        {
                            ["name"] = c.Name,
                            ["detail"] = c.Detail,
                            ["status"] = c.Status,
                        }).ToArray())
                };
                Console.WriteLine(result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
                table.AddColumn("Check");
                table.AddColumn("Detail");
                table.AddColumn("Status");

                foreach (var (name, detail, status) in checks)
                {
                    var statusMarkup = status switch
                    {
                        "OK" => "[green]OK[/]",
                        "WARN" => "[yellow]WARN[/]",
                        "FAIL" => "[red]FAIL[/]",
                        _ => status,
                    };
                    table.AddRow(name, Markup.Escape(detail), statusMarkup);
                }

                AnsiConsole.Write(table);

                if (hasFailures)
                    AnsiConsole.MarkupLine("\n[red]One or more checks FAILED. ETL-SQL may not function correctly.[/]");
                else if (hasWarnings)
                    AnsiConsole.MarkupLine("\n[yellow]One or more checks produced a WARNING. Review the items above.[/]");
                else
                    AnsiConsole.MarkupLine("\n[green]All checks passed.[/]");

                if (isStrict && (hasFailures || hasWarnings))
                    AnsiConsole.MarkupLine("[yellow]--strict mode: exiting with code 1 due to WARN or FAIL results.[/]");
            }

            return DoctorExitCode(isStrict, hasFailures, hasWarnings);
        }

        public static int DoctorExitCode(bool isStrict, bool hasFailures, bool hasWarnings) =>
            isStrict && (hasFailures || hasWarnings) ? 1 : 0;

        private static void ShowThirdPartyNotices(ILogger logger)
        {
            logger.WriteLine("Third-party notices", ConsoleColor.Cyan);
            logger.WriteLine("Visualizations powered by Apache ECharts. Table views powered by Tabulator. Terminal experience powered by Spectre.Console.");

            var noticesPath = FindRepoFile("THIRD-PARTY-NOTICES.md");
            if (noticesPath is null)
            {
                logger.WriteLine("THIRD-PARTY-NOTICES.md was not found in this installation.", ConsoleColor.Yellow);
                return;
            }

            logger.WriteLine($"Full notices: {noticesPath}", ConsoleColor.Gray);
        }

        private static string? FindRepoFile(string fileName)
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, fileName);
                    if (File.Exists(candidate))
                        return candidate;
                    dir = dir.Parent;
                }
            }

            return null;
        }

        private static string? FindExecutableOnPath(params string[] names)
        {
            var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var pathExts = OperatingSystem.IsWindows()
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                : new[] { string.Empty };

            foreach (var dir in pathDirs)
            {
                foreach (var name in names)
                {
                    var extensions = Path.HasExtension(name) ? new[] { string.Empty } : pathExts;
                    foreach (var ext in extensions)
                    {
                        try
                        {
                            var candidate = Path.Combine(dir, name + ext);
                            if (File.Exists(candidate))
                                return candidate;
                        }
                        catch
                        {
                            // Ignore invalid PATH entries.
                        }
                    }
                }
            }

            return null;
        }

        private static string? ResolveHealthUrl(params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var trimmed = candidate.Trim();
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                    continue;

                if (uri.AbsolutePath.EndsWith("/health", StringComparison.OrdinalIgnoreCase))
                    return uri.ToString();

                return new Uri(uri, "health").ToString();
            }

            return null;
        }

        private static int? ConfigInt(IConfiguration config, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (int.TryParse(config[key], out var value))
                    return value;
            }

            return null;
        }

        private static async Task<(string Name, string Detail, string Status)> ProbeHttpEndpointAsync(
            string name,
            string? url,
            string notConfiguredDetail)
        {
            if (string.IsNullOrWhiteSpace(url))
                return (name, notConfiguredDetail, "OK");

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var response = await client.GetAsync(url);
                var status = (int)response.StatusCode;
                return status < 500
                    ? (name, $"{url} returned HTTP {status}", "OK")
                    : (name, $"{url} returned HTTP {status}", "WARN");
            }
            catch (Exception ex)
            {
                return (name, $"{url} unreachable: {ex.Message}", "WARN");
            }
        }

        private static async Task<(string Name, string Detail, string Status)> ProbeTcpEndpointAsync(
            string name,
            string? host,
            int port,
            string notConfiguredDetail)
        {
            if (string.IsNullOrWhiteSpace(host))
                return (name, notConfiguredDetail, "OK");

            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await client.ConnectAsync(host.Trim(), port, cts.Token);
                return (name, $"{host}:{port} accepted TCP connection", "OK");
            }
            catch (Exception ex)
            {
                return (name, $"{host}:{port} unreachable: {ex.Message}", "WARN");
            }
        }

        private static async Task<int> RunSetupJwt(ILogger logger, bool updateConfig)
        {
            // 1. Generate 256-bit secret
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            var secret = Convert.ToBase64String(bytes);

            // 2. Encrypt using Machine Key
            var machineKey = SecurityService.GetMachineKey();
            var encryptedSecret = CryptoUtils.Encrypt(secret, machineKey);

            // 3. UI - Bold Warning
            AnsiConsole.Write(new FigletText("JWT SETUP").Centered().Color(Color.Yellow));
            AnsiConsole.Write(new Rule("[red bold]CRITICAL SECURITY INFORMATION[/]").RuleStyle("red"));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold red]GENERATED PLAIN-TEXT SECRET:[/]");
            AnsiConsole.MarkupLine($"[bold yellow]{Markup.Escape(secret)}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold white]ENCRYPTED CONFIG VALUE (Machine-Bound):[/]");
            AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(encryptedSecret)}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold red]IMPORTANT:[/] Record the plain-text secret in your password manager. It cannot be recovered from the encrypted value if the machine key changes.");

            if (updateConfig)
            {
                // Update appsettings.json
                try
                {
                    var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                    if (File.Exists(configPath))
                    {
                        var json = File.ReadAllText(configPath);
                        var doc = System.Text.Json.Nodes.JsonNode.Parse(json);
                        if (doc != null)
                        {
                            var portal = doc["Portal"] ?? (doc["Portal"] = new System.Text.Json.Nodes.JsonObject());
                            var jwt = portal["Jwt"] ?? (portal["Jwt"] = new System.Text.Json.Nodes.JsonObject());
                            jwt["Secret"] = encryptedSecret;

                            File.WriteAllText(configPath, doc.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                            AnsiConsole.MarkupLine("[green]SUCCESS:[/] Updated Portal:Jwt:Secret in appsettings.json.");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]WARN:[/] appsettings.json not found in base directory. Skipping auto-update.");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]ERROR:[/] Failed to update appsettings.json: {ex.Message}");
                }
            }

            return 0;
        }

        // ── purge command ──────────────────────────────────────────────────────────

        private static int RunPurge(CliContext ctx)
        {
            var config = Program.ServiceProvider.GetRequiredService<IConfiguration>();
            var targets = DataPurgeService.ResolveTargets(config, AppContext.BaseDirectory);

            // Measure existing targets up front so we can show the user what is at stake.
            var existing = targets
                .Select(t => (Target: t, Bytes: DataPurgeService.MeasureBytes(t),
                              Exists: t.IsDirectory ? Directory.Exists(t.Path) : File.Exists(t.Path)))
                .Where(t => t.Exists)
                .ToList();

            if (existing.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]No ETL-SQL runtime data found. Nothing to purge.[/]");
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            table.AddColumn("Data");
            table.AddColumn("Path");
            table.AddColumn(new TableColumn("Size").RightAligned());
            foreach (var (target, bytes, _) in existing)
                table.AddRow(Markup.Escape(target.Description), Markup.Escape(target.Path), FormatBytes(bytes));

            AnsiConsole.Write(new Rule(ctx.PurgeDryRun
                ? "[yellow]ETL-SQL Data Purge (dry run)[/]"
                : "[red]ETL-SQL Data Purge[/]").RuleStyle("grey"));
            AnsiConsole.Write(table);

            if (ctx.PurgeDryRun)
            {
                AnsiConsole.MarkupLine($"\n[yellow]Dry run:[/] {existing.Count} item(s) would be deleted. Re-run without [cyan]--dry-run[/] to delete.");
                return 0;
            }

            if (!ctx.PurgeYes)
            {
                AnsiConsole.MarkupLine("\n[bold red]This permanently deletes all of the above and cannot be undone.[/]");
                Console.Write("Type 'yes' to confirm: ");
                var answer = Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine("[yellow]Aborted. No data was deleted.[/]");
                    return 1;
                }
            }

            var results = DataPurgeService.Execute(existing.Select(e => e.Target), dryRun: false);
            var deleted = results.Count(r => r.Deleted);
            var failed = results.Where(r => r.Error != null && r.Existed).ToList();
            var freed = results.Where(r => r.Deleted).Sum(r => r.Bytes);

            AnsiConsole.MarkupLine($"\n[green]Deleted {deleted} item(s), freeing {FormatBytes(freed)}.[/]");
            foreach (var f in failed)
                AnsiConsole.MarkupLine($"[yellow]Could not delete[/] {Markup.Escape(f.Target.Path)}: {Markup.Escape(f.Error ?? "unknown error")}");

            return failed.Count > 0 ? 1 : 0;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return unit == 0 ? $"{bytes} B" : $"{size:0.0} {units[unit]}";
        }

        // ── serve command ────────────────────────────────────────────────────────

        private static async Task<int> ServeReport(CliContext ctx, ILogger logger)
        {
            if (ctx.ScriptFile == null && string.IsNullOrWhiteSpace(ctx.ServeManifest))
            {
                logger.WriteLine("Usage: etl-sql serve <script.rptsql>", ConsoleColor.Red);
                logger.WriteLine("       etl-sql serve --manifest reports.json", ConsoleColor.Red);
                return 1;
            }

            var (exe, prefixArgs) = FindReportPlayer();

            // Build the argument list passed to the ReportPlayer process
            var rpArgs = new List<string>(prefixArgs);
            if (ctx.ScriptFile != null)
                rpArgs.Add($"\"{ctx.ScriptFile.FullName}\"");
            if (!string.IsNullOrWhiteSpace(ctx.ServeManifest))
            {
                rpArgs.Add("--manifest");
                rpArgs.Add($"\"{Path.GetFullPath(ctx.ServeManifest)}\"");
            }
            if (ctx.ServePort.HasValue)
            {
                rpArgs.Add("--port");
                rpArgs.Add(ctx.ServePort.Value.ToString());
            }
            if (ctx.ServeNoBrowser)
                rpArgs.Add("--no-browser");

            var psi = new ProcessStartInfo(exe, string.Join(" ", rpArgs))
            {
                UseShellExecute = false,
            };

            logger.WriteLine($"Starting report preview server...", ConsoleColor.Cyan);

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                logger.WriteLine("Failed to start the ReportPlayer process.", ConsoleColor.Red);
                return 1;
            }

            // Forward Ctrl+C so the child shuts down cleanly
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            };

            await proc.WaitForExitAsync();
            return proc.ExitCode;
        }

        private static (string exe, string[] prefixArgs) FindReportPlayer()
        {
            var exeName = OperatingSystem.IsWindows() ? "ETL-SQL.ReportPlayer.exe" : "ETL-SQL.ReportPlayer";

            // 1. Sibling executable (production install — both binaries in same directory)
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? ".";
            var siblingExe = Path.Combine(exeDir, exeName);
            if (File.Exists(siblingExe))
                return (siblingExe, Array.Empty<string>());

            // 2. Dev mode — walk up from CWD to find the solution root, then use `dotnet run`
            var dir = Directory.GetCurrentDirectory();
            while (true)
            {
                if (Directory.GetFiles(dir, "*.slnx").Length > 0 || Directory.GetFiles(dir, "*.sln").Length > 0)
                {
                    var projectPath = Path.Combine(dir, "src", "ETL-SQL.ReportPlayer");
                    if (Directory.Exists(projectPath))
                        return ("dotnet", new[] { "run", "--project", $"\"{projectPath}\"", "--" });
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }

            throw new InvalidOperationException(
                "Could not locate ETL-SQL.ReportPlayer. Run from the solution root directory.");
        }
    }
}
