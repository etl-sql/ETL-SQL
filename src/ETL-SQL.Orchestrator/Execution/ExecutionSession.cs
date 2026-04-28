using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// Orchestrates the decoupled execution of ETL-SQL scripts.
    /// Used by both the CLI Runner (App) and the Terminal IDE (TUI).
    /// Maintains persistent connection and variable state across multiple executions
    /// so that connections created in one F5 run survive into the next.
    /// </summary>
    public class ExecutionSession : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly CliContext _ctx;

        // Persistent state containers for the IDE — survive across multiple ExecuteAsync calls
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IDataSource> _persistentConnections
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly VariableScopeManager _persistentVariables = new();

        private Evaluator? _lastEvaluator;
        private bool _disposed;

        /// <summary>
        /// Optional callback fired each time the evaluator appends a node to the
        /// execution tree. Use this for live tree updates in the Terminal IDE.
        /// </summary>
        public Action<string>? OnTreeNodeAdded { get; set; }

        public ExecutionSession(IServiceProvider serviceProvider, CliContext ctx, ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _ctx = ctx;
            _logger = logger;
        }

        public async Task<ExecutionResult> ExecuteAsync(string source, CancellationToken cancellationToken = default)
        {
            var result = new ExecutionResult();
            var timer = Stopwatch.StartNew();
            Evaluator? evaluator = null;

            _logger.Info("Starting execution session {SessionId}", _ctx.SessionId);
            
            try
            {
                // 1. Lex
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();

                // 2. Parse
                var parser = new Parser(tokens, source);
                var script = parser.Parse();
                result.Diagnostics.AddRange(script.Diagnostics);

                if (script.Diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error))
                {
                    _logger.Warning("Parse failed with {ErrorCount} errors and {WarningCount} warnings", 
                        script.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
                        script.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning));
                    result.Success = false;
                    return result;
                }

                // 3. Lint
                var lintResults = await LinterFactory.CreateWithAllRules()
                    .AnalyzeAsync(script, new DefaultLintContext());
                result.LintResults.AddRange(lintResults);

                if (lintResults.Exists(r => r.Severity == LintSeverity.Error))
                {
                    _logger.Warning("Linting failed with {ErrorCount} errors and {InfoCount} infos",
                        lintResults.Count(r => r.Severity == LintSeverity.Error || r.Severity == LintSeverity.Warning),
                        lintResults.Count(r => r.Severity == LintSeverity.Info));
                    result.Success = false;
                    return result;
                }

                _logger.Debug("Linting passed with {FindingCount} minor findings", lintResults.Count);

                // 4. Execute
                // ActivatorUtilities injects persistent connection + variable state so that
                // connections survive across F5 runs in the IDE.
                evaluator = ActivatorUtilities.CreateInstance<Evaluator>(
                    _serviceProvider,
                    _persistentConnections,
                    _persistentVariables,
                    new ExecutionTree()
                );

                evaluator.BatchSize  = _ctx.BatchSize;
                evaluator.IsVerbose  = _ctx.IsVerbose;
                evaluator.SessionId  = _ctx.SessionId;
                evaluator.Telemetry.IsProfiling = true;
                evaluator.RedirectOutput = true;

                if (OnTreeNodeAdded != null)
                    evaluator.Telemetry.ExecutionTree.OnNodeAdded = node => OnTreeNodeAdded.Invoke(node.Name);

                // Raw DataTables — rendering is the TUI's responsibility (CQ-S2)
                evaluator.OnResultSet = table => result.ResultsTables.Add(table);

                await evaluator.Evaluate(script, cancellationToken);

                result.ExecutionTree  = evaluator.Telemetry.ExecutionTree;
                result.RowsProcessed  = evaluator.Telemetry.RowsProcessed;
                result.Messages       = evaluator.Messages.ToList();
                result.Success        = true;
                _lastEvaluator        = evaluator;
            }
            catch (Exception ex)
            {
                _logger.Error("Execution failed: {ErrorMessage}", ex, ex.Message);
                result.Diagnostics.Add(new Diagnostic(ex.Message, 0, 0, DiagnosticSeverity.Error));
                result.Success = false;
            }
            finally
            {
                // Capture active connections even on failure so the TUI's autocomplete cache stays live
                if (evaluator != null)
                    result.ActiveConnections = evaluator.Connections.ToDictionary(k => k.Key, v => v.Value);

                timer.Stop();
                result.ExecutionTimeMs = timer.ElapsedMilliseconds;
                _logger.Info("Execution completed in {DurationMs}ms. Success: {Success}", result.ExecutionTimeMs, result.Success);
            }

            return result;
        }

        /// <summary>
        /// Disposes the last evaluator and releases any open ADO.NET connections.
        /// Call this when the IDE session ends (e.g., user exits the TUI).
        /// Note: do NOT call between F5 runs — connections are intentionally persistent.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (_lastEvaluator is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (_lastEvaluator is IDisposable disposable)
                disposable.Dispose();

            foreach (var ds in _persistentConnections.Values)
            {
                if (ds is IAsyncDisposable ads) await ads.DisposeAsync();
                else if (ds is IDisposable d) d.Dispose();
            }
            _persistentConnections.Clear();
        }
    }
}
