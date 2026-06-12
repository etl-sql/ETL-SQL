using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;

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

            if (context.CurrentScriptPath != null &&
                BundleUri.TryParse(context.CurrentScriptPath, out var currentUri) &&
                currentUri != null &&
                !BundleUri.TryParse(scriptPath, out _))
            {
                scriptPath = BundleUri.CombineRelative(currentUri, scriptPath);
            }

            string source;
            string currentPathForContext;
            if (BundleUri.TryParse(scriptPath, out var uri) && uri != null)
            {
                var store = context.ServiceProvider.GetService<IBundleStore>()
                    ?? throw new ExecutionException("RUN SCRIPT orch:// failed: no bundle store is registered.");
                var version = uri.Version ?? (await store.GetLatestVersionAsync(uri.BundleName))?.Version
                    ?? throw new ExecutionException($"RUN SCRIPT orch:// failed: bundle '{uri.BundleName}' was not found.");
                var file = await store.GetFileAsync(uri.BundleName, version, uri.Path)
                    ?? throw new ExecutionException($"RUN SCRIPT orch:// failed: script '{uri.Path}' was not found in bundle '{uri.BundleName}' version {version}.");
                source = file.Content;
                currentPathForContext = uri.ToPinnedString(version);
            }
            else
            {
                scriptPath = context.ResolvePath(scriptPath);

                _logger.Debug("Running sub-script: {ScriptPath}", scriptPath);

                if (!File.Exists(scriptPath))
                    throw new ExecutionException($"Script file not found: {scriptPath}");

                source = await File.ReadAllTextAsync(scriptPath);
                currentPathForContext = Path.GetFullPath(scriptPath);
            }

            string? oldPath = context.CurrentScriptPath;
            context.CurrentScriptPath = currentPathForContext;

            var tokens = new Lexer(source).Tokenize();
            var script = new Parser(tokens).Parse();

            var localVars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var localMetadata = new Dictionary<string, VariableMetadata>(StringComparer.OrdinalIgnoreCase);

            // Pass parameters from calling script
            foreach (var param in stmt.Parameters)
            {
                var val = await context.EvaluateValue(param.Value, new Row());
                localVars[param.Name] = val;
                localMetadata[param.Name] = new VariableMetadata { IsSensitive = true, IsInput = true, IsOutput = param.IsOutput };
            }

            context.VarContext.PushScope(localVars, localMetadata);
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
                var allVars = context.VarContext.GetVariablesWithMetadata(v => true);
                var allMetadata = context.VarContext.CurrentMetadata;
                context.VarContext.PopScope();

                // 1. Map back variables passed as parameters (by identifier)
                foreach (var param in stmt.Parameters)
                {
                    if (allVars.TryGetValue(param.Name, out var result))
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
                            if (!context.VarContext.ContainsVariable(targetVar))
                                context.VarContext.DeclareVariable(targetVar, result.Value, new VariableMetadata { IsDeclared = true });
                            else
                                context.VarContext.SetVariable(targetVar, result.Value);
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
                            if (!context.VarContext.ContainsVariable(kvp.Key))
                                context.VarContext.DeclareVariable(kvp.Key, val.Value, new VariableMetadata { IsDeclared = true });
                            else
                                context.VarContext.SetVariable(kvp.Key, val.Value);
                        }
                    }
                }
                context.CurrentRecursiveDepth--;
                context.CurrentScriptPath = oldPath;
            }
        }
    }
}

