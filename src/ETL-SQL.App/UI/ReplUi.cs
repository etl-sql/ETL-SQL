using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.UI
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

        private static readonly JsonSerializerOptions _deserializeOptions =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public ReplUi(CliContext ctx)
        {
            _ctx = ctx;
        }

        public async Task RunAsync()
        {
            try
            {
                // Set up the persistent evaluator
                _evaluator = Program.ServiceProvider.GetRequiredService<Evaluator>();
                _evaluator.BatchSize = _ctx.BatchSize;
                _evaluator.IsVerbose = _ctx.IsVerbose;
                _evaluator.RedirectOutput = true;
                _evaluator.SessionId = _ctx.SessionId;

                // Route engine log messages to the IDE as JSON on stdout.
                // Suppress the raw Console.WriteLine path so only JSON appears on stdout.
                ETL_SQL.Common.Logger.SuppressConsole = true;
                ETL_SQL.Common.Logger.OnMessage = (msg, color) =>
                {
                    var level = color == ConsoleColor.Red ? "error"
                              : color == ConsoleColor.Yellow ? "warning"
                              : "info";
                    WriteJson(new { type = "message", level, text = msg });
                };

                // Signal ready — the IDE will now send run commands on stdin.
                WriteJson(new { type = "status", status = "ready" });

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
                            await ExecuteScript(cmd.Script ?? "");
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

                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                var parser = new Parser(tokens, source);
                var script = parser.Parse();

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

                // Intercept result sets and stream them to the IDE.
                // isFirst=true because each OnResultSet call is a complete, independent result set.
                var originalOnResultSet = _evaluator.OnResultSet;
                _evaluator.OnResultSet = (table) =>
                {
                    WriteJson(new
                    {
                        type = "results",
                        isFirst = true,
                        columns = table.ColumnNames,
                        rows = table.Rows.Select(r => r.Columns)
                    });
                };

                await _evaluator.Evaluate(script);

                _evaluator.OnResultSet = originalOnResultSet;

                WriteJson(new { type = "done", exitCode = 0 });
            }
            catch (Exception ex)
            {
                WriteJson(new { type = "message", level = "error", text = ex.Message });
                WriteJson(new { type = "done", exitCode = 1 });
            }
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
        }
    }
}
