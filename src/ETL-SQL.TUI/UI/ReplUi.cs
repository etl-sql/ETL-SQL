using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// A JSON-based REPL for background execution, used by IDE extensions to maintain state.
    /// Reads JSON commands from stdin; writes JSON events to stdout.
    /// Stderr is reserved for raw diagnostic lines (non-JSON) visible in the IDE output channel.
    ///
    /// Protocol:
    ///   stdin  ← { "action": "run",  "script": "..." }
    ///   stdin  ← { "action": "exit" }
    ///   stdout → { "type": "status",  "status": "ready" }
    ///   stdout → { "type": "message", "level": "info|warning|error", "text": "..." }
    ///   stdout → { "type": "results", "columns": [...], "rows": [{col: val, ...}, ...] }
    ///   stdout → { "type": "done",    "exitCode": 0|1 }
    /// </summary>
    public class ReplUi
    {
        private readonly CliContext _ctx;
        private Evaluator? _evaluator;
        private ETL_SQL.Data.DataTable? _lastResult;
        private CancellationTokenSource? _currentCts;
        private readonly object _execLock = new();

        private readonly IServiceProvider _serviceProvider;
        private readonly JsonSerializerOptions _deserializeOptions =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public ReplUi(CliContext ctx, IServiceProvider serviceProvider)
        {
            _ctx = ctx;
            _serviceProvider = serviceProvider;
        }

        public async Task RunAsync()
        {
            try
            {
                // Set up the persistent evaluator
                _evaluator = _serviceProvider.GetRequiredService<Evaluator>();
                _evaluator.BatchSize = _ctx.BatchSize;
                _evaluator.IsVerbose = _ctx.IsVerbose;
                _evaluator.RedirectOutput = true;
                _evaluator.SessionId = _ctx.SessionId;
                _evaluator.DisplayExecuteTree = true;
                _evaluator.Telemetry.IsProfiling = true;
                
                // Inject CLI variables as input parameters
                foreach (var v in _ctx.Variables)
                {
                    _evaluator.DeclareVariable(v.Key, v.Value, new VariableMetadata { IsInput = true, IsDeclared = false });
                }

                // Route engine log messages to the IDE as JSON on stdout.
                // We subscribe to the DI-injected ILogger which handles all modernized handlers.
                var logger = _serviceProvider.GetRequiredService<ETL_SQL.Common.ILogger>();
                logger.SuppressConsole = true; // Stop raw stdout writes to prevent blocking and double-output in IDE
                logger.OnMessage += (msg, sid, color) =>
                {
                    if (sid != null && sid != _ctx.SessionId) return;

                    var level = color == ConsoleColor.Red ? "error"
                              : color == ConsoleColor.Yellow ? "warning"
                              : "info";
                    WriteJson(new { type = "message", level, text = msg });
                };

                _evaluator.OnVisualCreated = (stmt) =>
                {
                    WriteJson(new { type = "visual", data = stmt });
                };

                // Signal ready — the IDE will now send run commands on stdin.
                WriteJson(new { 
                    type = "status", 
                    status = "ready", 
                    buildId = "DIAGNOSTIC-2026-04-10-03-00",
                    pid = System.Diagnostics.Process.GetCurrentProcess().Id
                });

                Task? activeExecutionTask = null;

                while (true)
                {
                    Console.Error.WriteLine("[TRACE] Engine about to ReadLine (sync)...");
                    var line = Console.In.ReadLine();
                    Console.Error.WriteLine($"[TRACE] Engine ReadLine returned ({line?.Length ?? -1} chars): {line ?? "NULL"}");
                    
                    if (line == null) 
                    {
                        Console.Error.WriteLine("[TRACE] stdin reached end of stream (null).");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    Console.Error.WriteLine($"[TRACE] Received REPL line ({line.Length} chars): {line}");

                    try
                    {
                        var cmd = JsonSerializer.Deserialize<ReplCommand>(line, _deserializeOptions);
                        if (cmd == null) continue;

                        if (cmd.Action == "ping")
                        {
                            WriteJson(new { type = "pong" });
                            continue;
                        }

                        if (cmd.Action == "exit")
                        {
                            lock (_execLock) _currentCts?.Cancel();
                            break;
                        }

                        if (cmd.Action == "cancel")
                        {
                            lock (_execLock)
                            {
                                if (_currentCts != null)
                                {
                                    _currentCts.Cancel();
                                    WriteJson(new { type = "message", level = "warning", text = "Execution cancellation requested." });
                                }
                                else
                                {
                                    WriteJson(new { type = "message", level = "info", text = "No active execution to cancel." });
                                }
                            }
                        }
                        else if (cmd.Action == "rollback")
                        {
                            if (_evaluator != null)
                            {
                                await _evaluator.RollbackAllTransactions();
                                WriteJson(new { type = "message", level = "warning", text = "All active transactions rolled back." });
                            }
                        }
                        else if (cmd.Action == "ping")
                        {
                            WriteJson(new { type = "pong" });
                            continue;
                        }
                        else if (cmd.Action == "run")
                        {
                            if (activeExecutionTask != null && !activeExecutionTask.IsCompleted)
                            {
                                WriteJson(new { type = "message", level = "warning", text = "Another script is already running. Please wait or cancel." });
                                continue;
                            }

                            if (cmd.WorkspaceRoot != null)
                                _evaluator!.WorkingDirectory = cmd.WorkspaceRoot;
                            if (cmd.ScriptPath != null)
                                _evaluator!.CurrentScriptPath = cmd.ScriptPath;
                            
                            _evaluator!.InteractiveMode = cmd.InteractiveMode;

                            Console.Error.WriteLine($"[TRACE] Starting execution of script ({cmd.Script?.Length} chars) - Interactive: {cmd.InteractiveMode}");
                            activeExecutionTask = ExecuteScript(cmd.Script ?? "");
                            await activeExecutionTask;
                        }
                        else if (cmd.Action == "export")
                        {
                            await HandleExport(cmd);
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteJson(new { type = "message", level = "error", text = ex.Message });
                    }
                }
            }
            catch (Exception ex)
            {
                WriteJson(new { type = "message", level = "error", text = $"REPL Startup Error: {ex.Message}" });
                Console.Error.WriteLine($"FATAL: REPL failed to start. {ex}");
                Console.Error.Flush();
            }
        }

        private async Task ExecuteScript(string source)
        {
            try
            {
                _evaluator!.Telemetry.RowsProcessed = 0;
                _evaluator.Telemetry.TotalSpilledBytes = 0;
                _evaluator.Messages.Clear();

                var originalOnResultSet = _evaluator.OnResultSet;
                _evaluator.OnResultSet = (table) =>
                {
                    _lastResult = table;
                    WriteJson(new
                    {
                        type = "results",
                        isFirst = true,
                        columns = table.ColumnNames,
                        rows = table.Rows.Select(r => r.Columns)
                    });
                };

                _evaluator.OnMessage = (diag) =>
                {
                    WriteJson(new
                    {
                        type = "message",
                        level = diag.Severity.ToString().ToLower(),
                        text = diag.Message,
                        line = diag.Line,
                        column = diag.Column
                    });
                };

                var lexTime = Stopwatch.StartNew();
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                lexTime.Stop();

                var parseTime = Stopwatch.StartNew();
                var parser = new Parser(tokens, source);
                var script = parser.Parse();
                parseTime.Stop();

                if (script.Diagnostics.Count > 0)
                {
                    foreach (var diag in script.Diagnostics)
                    {
                        WriteJson(new
                        {
                            type = "message",
                            level = diag.Severity.ToString().ToLower(),
                            text = $"({diag.Line},{diag.Column}): {diag.Message}"
                        });
                    }
                    if (script.Diagnostics.Exists(d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error))
                    {
                        WriteJson(new { type = "done", exitCode = 1 });
                        return;
                    }
                }

                // Initialize telemetry timing
                var execTime = Stopwatch.StartNew();
                
                lock (_execLock)
                {
                    _currentCts = new CancellationTokenSource();
                }
                
                using var treeCts = new CancellationTokenSource();

                // Start heartbeat for real-time graphical progress (10Hz)
                var tree = _evaluator.Telemetry.ExecutionTree;
                var heartbeatTask = Task.Run(async () =>
                {
                    while (!treeCts.Token.IsCancellationRequested)
                    {
                        try 
                        {
                            WriteJson(new { type = "progress", data = tree.ToSnapshot() });
                            await Task.Delay(100, treeCts.Token);
                        }
                        catch (TaskCanceledException) { break; }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[HEARTBEAT_ERROR] {ex.Message}");
                        }
                    }
                });

                try 
                {
                    Console.Error.WriteLine("[TRACE] Evaluator.Evaluate START");
                    await _evaluator.Evaluate(script, _currentCts.Token);
                    Console.Error.WriteLine("[TRACE] Evaluator.Evaluate END");
                }
                finally
                {
                    treeCts.Cancel();
                    await heartbeatTask;
                    var elapsed = execTime.ElapsedMilliseconds;
                    _evaluator.Telemetry.LastExecutionTimeMs = elapsed;
                    _evaluator.LastLexTimeMs = lexTime.ElapsedMilliseconds;
                    _evaluator.LastParseTimeMs = parseTime.ElapsedMilliseconds;
                    _evaluator.OnResultSet = originalOnResultSet;
                    _evaluator.OnMessage = null;
                    
                    lock (_execLock)
                    {
                        _currentCts?.Dispose();
                        _currentCts = null;
                    }
                }

                // Final status ensures we see the completed nodes
                WriteJson(new { type = "progress", data = tree.ToSnapshot() });

                // Emit final performance metrics for the IDE dashboard
                double memUsageMb = Math.Round((double)GC.GetTotalMemory(false) / (1024 * 1024), 2);
                double rowsPerSec = execTime.Elapsed.TotalSeconds > 0 
                    ? Math.Round(_evaluator.Telemetry.RowsProcessed / execTime.Elapsed.TotalSeconds, 0) 
                    : _evaluator.Telemetry.RowsProcessed;

                WriteJson(new { 
                    type = "performance", 
                    metrics = new {
                        lexerMs = lexTime.ElapsedMilliseconds,
                        parserMs = parseTime.ElapsedMilliseconds,
                        executionMs = execTime.ElapsedMilliseconds,
                        memoryMb = memUsageMb,
                        rowsProcessed = _evaluator.Telemetry.RowsProcessed,
                        rowsPerSecond = rowsPerSec,
                        statements = _evaluator.Telemetry.ProfileMetrics.Select(m => {
                            string sqlClean = m.Sql?.Trim() ?? "";
                            string type = sqlClean.Length > 0 ? sqlClean.Split(' ', 2)[0].ToUpper() : "UNKNOWN";
                            return new {
                                type = type,
                                count = 1,
                                totalMs = m.DurationMs
                            };
                        }).ToList()
                    }
                });

                WriteJson(new { type = "message", level = "info", text = "Finalizing execution..." });

                // Emit current variable state for the IDE Variable Explorer (User Params only)
                var userVars = _evaluator.Variables
                    .Where(kvp => kvp.Key.StartsWith("@") && !kvp.Key.StartsWith("@@"))
                    .Select(kvp => new {
                        name = kvp.Key,
                        value = kvp.Value?.ToString() ?? "NULL",
                        type = kvp.Value?.GetType().Name ?? "Object"
                    }).ToList();

                WriteJson(new { type = "variables", data = userVars });

                // Emit cell-level lineage
                EmitCellLineage();

                WriteJson(new { type = "done", exitCode = 0 });
            }
            catch (Exception ex)
            {
                WriteJson(new { type = "message", level = "error", text = ex.Message });
                WriteJson(new { type = "done", exitCode = 1 });
            }
        }

        private void EmitCellLineage()
        {
            if (_evaluator == null) return;
            var entries = _evaluator.LineageTracker.GetFullLineage().ToList();
            if (entries.Count == 0) return;

            // Simple mermaid graph generation
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("graph LR");
            var nodes = new HashSet<string>();
            
            foreach (var entry in entries)
            {
                foreach (var src in entry.SourceTables)
                {
                    string srcId = SanitizeMermaidId(src);
                    string targetId = SanitizeMermaidId(entry.TargetTable);
                    sb.AppendLine($"  {srcId} --> {targetId}");
                    nodes.Add(srcId);
                    nodes.Add(targetId);
                }
            }

            if (nodes.Count > 0)
            {
                WriteJson(new { type = "lineage", mermaid = sb.ToString() });
            }
        }

        private string SanitizeMermaidId(string id)
        {
            return id.Replace(".", "_").Replace("#", "Temp_").Replace("@", "Var_").Replace(" ", "_");
        }

        private async Task HandleExport(ReplCommand cmd)
        {
            if (_lastResult == null)
            {
                WriteJson(new { type = "message", level = "error", text = "No results available to export. Run a script first." });
                return;
            }

            if (string.IsNullOrEmpty(cmd.Path))
            {
                WriteJson(new { type = "message", level = "error", text = "Export path is required." });
                return;
            }

            try
            {
                if (cmd.Format?.ToLower() == "csv")
                {
                    await ExportToCsv(_lastResult, cmd.Path);
                    WriteJson(new { type = "message", level = "info", text = $"Successfully exported results to {cmd.Path}" });
                }
                else
                {
                    WriteJson(new { type = "message", level = "error", text = $"Unsupported export format: {cmd.Format ?? "null"}" });
                }
            }
            catch (Exception ex)
            {
                WriteJson(new { type = "message", level = "error", text = $"Export failed: {ex.Message}" });
            }
        }

        private async Task ExportToCsv(ETL_SQL.Data.DataTable table, string path)
        {
            using var writer = new System.IO.StreamWriter(path, false, System.Text.Encoding.UTF8);
            
            // Header
            await writer.WriteLineAsync(string.Join(",", table.ColumnNames.Select(EscapeCsv)));

            // Rows
            foreach (var row in table.Rows)
            {
                var values = table.ColumnNames.Select(col => EscapeCsv(row[col]?.ToString() ?? ""));
                await writer.WriteLineAsync(string.Join(",", values));
            }
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        private static readonly object _writeLock = new();
        /// <summary>Serializes <paramref name="obj"/> as a single JSON line on stdout.</summary>
        private static void WriteJson(object obj)
        {
            lock (_writeLock)
            {
                Console.WriteLine(JsonSerializer.Serialize(obj));
                Console.Out.Flush();
            }
        }

        public class ReplCommand
        {
            public string Action { get; set; } = "run";
            public string? Script { get; set; }
            public string? Path { get; set; }
            public string? Format { get; set; }
            public string? ScriptPath { get; set; }
            public string? WorkspaceRoot { get; set; }
            public bool InteractiveMode { get; set; }
        }
    }
}
