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
                _evaluator.IsProfiling = true;

                // Route engine log messages to the IDE as JSON on stdout.
                // We subscribe to the DI-injected ILogger which handles all modernized handlers.
                var logger = _serviceProvider.GetRequiredService<ETL_SQL.Common.ILogger>();
                logger.OnMessage += (msg, color) =>
                {
                    var level = color == ConsoleColor.Red ? "error"
                              : color == ConsoleColor.Yellow ? "warning"
                              : "info";
                    WriteJson(new { type = "message", level, text = msg });
                };

                // Signal ready — the IDE will now send run commands on stdin.
                WriteJson(new { type = "status", status = "ready", buildId = "DIAGNOSTIC-2026-04-10-02-00" });

                while (true)
                {
                    var line = await Console.In.ReadLineAsync();
                    if (line == null) break;                  // stdin closed
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var cmd = JsonSerializer.Deserialize<ReplCommand>(line, _deserializeOptions);
                        if (cmd == null) continue;

                        if (cmd.Action == "exit") break;
                        if (cmd.Action == "run")
                        {
                            if (cmd.WorkspaceRoot != null)
                                _evaluator!.WorkingDirectory = cmd.WorkspaceRoot;
                            if (cmd.ScriptPath != null)
                                _evaluator!.CurrentScriptPath = cmd.ScriptPath;
                            await ExecuteScript(cmd.Script ?? "");
                        }
                        if (cmd.Action == "export")
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
                _evaluator!.RowsProcessed = 0;
                _evaluator.TotalSpilledBytes = 0;
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
                using var treeCts = new CancellationTokenSource();

                // Start heartbeat for real-time graphical progress (10Hz)
                var tree = _evaluator.ExecutionTree;
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
                    await _evaluator.Evaluate(script);
                }
                finally
                {
                    treeCts.Cancel();
                    await heartbeatTask;
                    var elapsed = execTime.ElapsedMilliseconds;
                    _evaluator.LastExecTimeMs = elapsed;
                    _evaluator.LastLexTimeMs = lexTime.ElapsedMilliseconds;
                    _evaluator.LastParseTimeMs = parseTime.ElapsedMilliseconds;
                    _evaluator.OnResultSet = originalOnResultSet;
                }

                // Final status ensures we see the completed nodes
                WriteJson(new { type = "progress", data = tree.ToSnapshot() });

                // Emit final performance metrics for the IDE dashboard
                double memUsageMb = Math.Round((double)GC.GetTotalMemory(false) / (1024 * 1024), 2);
                double rowsPerSec = execTime.Elapsed.TotalSeconds > 0 
                    ? Math.Round(_evaluator.RowsProcessed / execTime.Elapsed.TotalSeconds, 0) 
                    : _evaluator.RowsProcessed;

                WriteJson(new { 
                    type = "performance", 
                    metrics = new {
                        lexerMs = lexTime.ElapsedMilliseconds,
                        parserMs = parseTime.ElapsedMilliseconds,
                        executionMs = execTime.ElapsedMilliseconds,
                        memoryMb = memUsageMb,
                        rowsProcessed = _evaluator.RowsProcessed,
                        rowsPerSecond = rowsPerSec,
                        statements = _evaluator.ProfileMetrics.Select(m => {
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

                WriteJson(new { type = "done", exitCode = 0 });
            }
            catch (Exception ex)
            {
                WriteJson(new { type = "message", level = "error", text = ex.Message });
                WriteJson(new { type = "done", exitCode = 1 });
            }
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

        /// <summary>Serializes <paramref name="obj"/> as a single JSON line on stdout.</summary>
        private static void WriteJson(object obj)
        {
            Console.WriteLine(JsonSerializer.Serialize(obj));
            Console.Out.Flush();
        }

        private class ReplCommand
        {
            public string Action { get; set; } = "run";
            public string? Script { get; set; }
            public string? Path { get; set; }
            public string? Format { get; set; }
            public string? ScriptPath { get; set; }
            public string? WorkspaceRoot { get; set; }
        }
    }
}
