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
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Rendering;

using ETL_SQL.UI;

namespace ETL_SQL.App
{
    /// <summary>
    /// Represents the full results of a script execution.
    /// </summary>
    public class ExecutionResult
    {
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public List<LintResult> LintResults { get; set; } = new();
        public IRenderable? ExecutionTree { get; set; }
        /// <summary>All result sets produced by the script, in order.</summary>
        public List<IRenderable> ResultsTables { get; set; } = new();
        public long ExecutionTimeMs { get; set; }
        public long RowsProcessed { get; set; }
        public bool Success { get; set; }
    }

    /// <summary>
    /// Orchestrates the decoupled execution of ETL-SQL scripts.
    /// Used by both the CLI Runner and the Terminal IDE.
    /// </summary>
    public class ExecutionSession
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly CliContext _ctx;

        public ExecutionSession(IServiceProvider serviceProvider, CliContext ctx)
        {
            _serviceProvider = serviceProvider;
            _ctx = ctx;
        }

        public async Task<ExecutionResult> ExecuteAsync(string source)
        {
            var result = new ExecutionResult();
            var timer = Stopwatch.StartNew();

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
                await using var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
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
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new Diagnostic(ex.Message, 0, 0, DiagnosticSeverity.Error));
                result.Success = false;
            }
            finally
            {
                timer.Stop();
                result.ExecutionTimeMs = timer.ElapsedMilliseconds;
            }

            return result;
        }
    }
}
