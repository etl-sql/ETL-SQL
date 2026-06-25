using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the EXECUTE statement for procedures or sub-scripts, supporting parameter passing and scope management.
    /// </summary>
    public class ExecuteStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(ExecuteStatement);

        public ExecuteStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the procedure or sub-script, managing its execution scope.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExecuteStatement)statement;

            // Check if it's a script file
            if (stmt.ProcedureName.EndsWith(".etlsql", StringComparison.OrdinalIgnoreCase) ||
                stmt.ProcedureName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteScript(stmt, context);
                return;
            }

            _logger.Debug("Executing procedure {ProcedureName}", stmt.ProcedureName);
            var args = new List<(string? Name, object? Value)>();
            foreach (var param in stmt.Parameters)
            {
                var val = await context.EvaluateValue(param.Expression, new Row());
                args.Add((param.Name, val));
            }

            await context.EvaluateProcedure(stmt.ProcedureName, args);
        }

        private async Task ExecuteScript(ExecuteStatement stmt, IExecutionContext context)
        {
            var scriptPath = context.ResolvePath(stmt.ProcedureName);

            _logger.Debug("Running sub-script: {ScriptPath}", scriptPath);

            if (!File.Exists(scriptPath))
                throw new ExecutionException($"Script file not found: {scriptPath}");

            var source = await File.ReadAllTextAsync(scriptPath);
            var tokens = new Lexer(source).Tokenize();
            var script = new ETL_SQL.Core.Parser.Parser(tokens).Parse();

            var previousScriptPath = context.CurrentScriptPath;
            var localVars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var localMetadata = new Dictionary<string, VariableMetadata>(StringComparer.OrdinalIgnoreCase);

            // Pass parameters from calling script
            foreach (var param in stmt.Parameters)
            {
                var val = await context.EvaluateValue(param.Expression, new Row());
                string? varName = param.Name;

                if (varName == null && param.Expression is VariableExpression varExpr)
                {
                    varName = varExpr.Name;
                }

                if (varName != null)
                {
                    localVars[varName] = val;
                    if (param.IsOutput || param.IsInput)
                    {
                        localMetadata[varName] = new VariableMetadata
                        {
                            IsOutput = param.IsOutput,
                            IsInput = param.IsInput,
                            IsDeclared = false // Marked as injected parameter, not yet declared in sub-script
                        };
                    }
                }
            }

            context.VarContext.PushScope(localVars, localMetadata);
            context.CurrentScriptPath = scriptPath;
            try
            {
                await context.Evaluate(script);
            }
            catch (ReturnException)
            {
                // Scripts can return early
            }
            finally
            {
                try
                {
                    var outputs = context.VarContext.GetVariablesWithMetadata(v => v.IsOutput);
                    context.VarContext.PopScope();

                    foreach (var kvp in outputs)
                    {
                        context.VarContext.SetVariable(kvp.Key, kvp.Value.Value);
                    }
                }
                finally
                {
                    context.CurrentScriptPath = previousScriptPath;
                }
            }
        }
    }
}

