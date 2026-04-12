using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// Represents the full results of a script execution.
    /// </summary>
    public class ExecutionResult
    {
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public List<LintResult> LintResults { get; set; } = new();
        /// <summary>Rendered execution tree (Spectre.Console IRenderable) for display.</summary>
        public IRenderable? ExecutionTree { get; set; }
        /// <summary>All result sets produced by the script, in order.</summary>
        public List<IRenderable> ResultsTables { get; set; } = new();
        public long ExecutionTimeMs { get; set; }
        public long RowsProcessed { get; set; }
        public bool Success { get; set; }
        /// <summary>Captured log messages for display in the TUI.</summary>
        public List<string> Messages { get; set; } = new();
        /// <summary>Active connections captured from the engine after execution, used for TUI autocomplete.</summary>
        public Dictionary<string, IDataSource> ActiveConnections { get; set; } = new();
    }

    /// <summary>
    /// Orchestrates the decoupled execution of ETL-SQL scripts.
    /// Used by both the CLI Runner (App) and the Terminal IDE (TUI).
    /// </summary>
    public class ExecutionSession
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly CliContext _ctx;
        
        // Persistent state containers for the IDE
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IDataSource> _persistentConnections = new(StringComparer.OrdinalIgnoreCase);
        private readonly VariableScopeManager _persistentVariables = new();

        /// <summary>
        /// Optional callback fired each time the evaluator appends a node to the
        /// execution tree. Use this for live tree updates in the Terminal IDE.
        /// </summary>
        public Action<string>? OnTreeNodeAdded { get; set; }

        public ExecutionSession(IServiceProvider serviceProvider, CliContext ctx)
        {
            _serviceProvider = serviceProvider;
            _ctx = ctx;
        }

        public async Task<ExecutionResult> ExecuteAsync(string source)
        {
            var result = new ExecutionResult();
            var timer = Stopwatch.StartNew();
            Evaluator? evaluator = null;

            try
            {
                // 1. Lexing
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();

                // 2. Parsing
                var parser = new Parser(tokens, source);
                var script = parser.Parse();
                result.Diagnostics.AddRange(script.Diagnostics);

                if (script.Diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error))
                {
                    result.Success = false;
                    return result;
                }

                // 3. Linting
                var linter = new Linter();
                foreach (var type in typeof(ILintRule).Assembly.GetTypes()
                    .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
                {
                    if (Activator.CreateInstance(type) is ILintRule rule)
                        linter.AddRule(rule);
                }
                var lintResults = await linter.AnalyzeAsync(script, new DefaultLintContext());
                result.LintResults.AddRange(lintResults);

                if (lintResults.Exists(r => r.Severity == LintSeverity.Error))
                {
                    result.Success = false;
                    return result;
                }

                // 4. Execution
                // Resolve using ActivatorUtilities to inject the persistent connection and variable state,
                // overriding the transient behavior so connections live across F5 runs.
                evaluator = ActivatorUtilities.CreateInstance<Evaluator>(
                    _serviceProvider,
                    _persistentConnections,
                    _persistentVariables,
                    new ExecutionTree()
                );
                
                evaluator.BatchSize = _ctx.BatchSize;
                evaluator.IsVerbose = _ctx.IsVerbose;
                evaluator.SessionId = _ctx.SessionId;
                evaluator.IsProfiling = true;

                // Pre-parse security override flags from the script source
                if (source.Contains("### ALLOW_FILE_TYPE_ACCESS", StringComparison.OrdinalIgnoreCase))
                    evaluator.AllowUnknownFileTypes = true;
                if (source.Contains("### ALLOW_GREATER_THAN_100_FILE", StringComparison.OrdinalIgnoreCase))
                    evaluator.AllowLargeFileOperationCount = true;
                if (source.Contains("### ALLOW_RECURSIVE_GREATER_THAN_5_LAYERS", StringComparison.OrdinalIgnoreCase))
                    evaluator.AllowDeepRecursion = true;

                // Wire live tree-node callback
                if (OnTreeNodeAdded != null)
                    evaluator.ExecutionTree.OnNodeAdded = node => OnTreeNodeAdded.Invoke(node.Name);

                // Capture all result sets
                evaluator.OnResultSet = (table) =>
                {
                    var spectreTable = new Table().Border(TableBorder.Rounded);
                    foreach (var col in table.ColumnNames) spectreTable.AddColumn(col);
                    foreach (var row in table.Rows)
                        spectreTable.AddRow(row.Columns.Values.Select(v => v?.ToString() ?? "").ToArray());
                    result.ResultsTables.Add(spectreTable);
                };

                await evaluator.Evaluate(script);

                result.ExecutionTree = new ExecuteTreeVisualizer(evaluator.ExecutionTree).CreateRenderable();
                result.RowsProcessed = evaluator.RowsProcessed;
                result.Messages = evaluator.Messages.ToList();
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new Diagnostic(ex.Message, 0, 0, DiagnosticSeverity.Error));
                result.Success = false;
            }
            finally
            {
                // Capture active connections in the finally block so the UI's connection cache 
                // survives syntax errors
                if (evaluator != null)
                {
                    result.ActiveConnections = evaluator.Connections.ToDictionary(k => k.Key, v => v.Value);
                    
                    // We DO NOT dispose the evaluator if we are persisting connections,
                    // otherwise the ADO.NET objects are closed and autocomplete fails.
                }
                
                timer.Stop();
                result.ExecutionTimeMs = timer.ElapsedMilliseconds;
            }

            return result;
        }
    }

    /// <summary>
    /// IScriptExecutor implementation — thin adapter used by SchedulerService for job execution.
    /// </summary>
    public class ScriptExecutorAdapter : IScriptExecutor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly CliContext _ctx;

        public ScriptExecutorAdapter(IServiceProvider serviceProvider, CliContext ctx)
        {
            _serviceProvider = serviceProvider;
            _ctx = ctx;
        }

        public async Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                var session = new ExecutionSession(_serviceProvider, _ctx);
                var result = await session.ExecuteAsync(scriptText);
                return new ScriptExecutionResult(result.Success, result.RowsProcessed,
                    result.Success ? null : string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            }
            catch (Exception ex)
            {
                return new ScriptExecutionResult(false, 0, ex.Message);
            }
        }
    }
}
