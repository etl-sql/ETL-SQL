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
    /// Handles the RUN SCRIPT statement, executing external .etlsql files within a nested scope.
    /// </summary>
    public class RunScriptStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(RunScriptStatement);

        public RunScriptStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the specified script, resolving parameters and managing the script's scope.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            context.CurrentRecursiveDepth++;
            context.IncrementOperationCount(); // Trigger check against limits

            var stmt = (RunScriptStatement)statement;
            
            var pathObj = await context.EvaluateValue(stmt.PathExpression, new Row());
            if (pathObj == null)
                throw new ExecutionException("Script path expression evaluated to null.");
            
            string scriptPath = pathObj.ToString()!;

            _logger.Debug("Running sub-script: {ScriptPath}", scriptPath);

            if (!File.Exists(scriptPath))
                throw new ExecutionException($"Script file not found: {scriptPath}");

            string? oldPath = context.CurrentScriptPath;
            context.CurrentScriptPath = Path.GetFullPath(scriptPath);
            
            var source = await File.ReadAllTextAsync(scriptPath);
            var tokens = new Lexer(source).Tokenize();
            var script = new Parser(tokens).Parse();

            var localVars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var localMetadata = new Dictionary<string, VariableMetadata>(StringComparer.OrdinalIgnoreCase);

            // Pass parameters from calling script
            foreach (var param in stmt.Parameters)
            {
                var val = await context.EvaluateValue(param.Value, new Row());
                localVars[param.Key] = val;
                localMetadata[param.Key] = new VariableMetadata { IsSensitive = true, IsInput = true };
            }

            context.PushScope(localVars, localMetadata);
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
                // Capture all variables from the nested scope BEFORE popping
                var allVars = context.GetVariablesWithMetadata(v => true);
                var allMetadata = context.CurrentMetadata;
                context.PopScope();
                
                // 1. Map back variables passed as parameters (by identifier)
                foreach (var param in stmt.Parameters)
                {
                    if (allVars.TryGetValue(param.Key, out var result))
                    {
                        string? targetVar = null;
                        if (param.Value is IdentifierExpression id)
                        {
                            targetVar = id.Name;
                        }
                        else if (param.Value is VariableExpression varExpr)
                        {
                            targetVar = varExpr.Name;
                        }

                        if (targetVar != null)
                        {
                            if (!context.ContainsVariable(targetVar))
                                context.DeclareVariable(targetVar, result, new VariableMetadata { IsDeclared = true });
                            else
                                context.SetVariable(targetVar, result);
                        }
                    }
                }

                // 2. Map back any variables explicitly marked as IsOutput
                foreach (var kvp in allMetadata)
                {
                    if (kvp.Value.IsOutput)
                    {
                        if (allVars.TryGetValue(kvp.Key, out var val))
                        {
                            if (!context.ContainsVariable(kvp.Key))
                                context.DeclareVariable(kvp.Key, val, new VariableMetadata { IsDeclared = true });
                            else
                                context.SetVariable(kvp.Key, val);
                        }
                    }
                }
                context.CurrentRecursiveDepth--;
                context.CurrentScriptPath = oldPath;
            }
        }
    }
}
