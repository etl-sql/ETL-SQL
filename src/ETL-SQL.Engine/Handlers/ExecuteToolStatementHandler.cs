using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles EXECUTE TOOL statements.
/// Runs a registered tool process, streams data to it via stdin (JSON Lines),
/// and collects results from stdout (JSON Lines).
/// </summary>
public class ExecuteToolStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(ExecuteToolStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ExecuteToolStatement)statement;
        
        if (!context.ReportContext.ToolDefinitions.TryGetValue(stmt.ToolAlias, out var toolDef))
        {
            throw new ExecutionException($"Tool '{stmt.ToolAlias}' is not defined or registered.", null, stmt.Line, stmt.Column);
        }

        var toolType = toolDef.ToolType.ToUpperInvariant();
        if (toolType != "EXECUTABLE" && toolType != "CONTAINER")
        {
            throw new ExecutionException($"Tool '{stmt.ToolAlias}' is of unsupported type {toolType}.", null, stmt.Line, stmt.Column);
        }

        var command = GetOptionString(toolDef.Options, "COMMAND");
        if (string.IsNullOrWhiteSpace(command))
            throw new ExecutionException($"Tool '{stmt.ToolAlias}' is missing the COMMAND option.", null, stmt.Line, stmt.Column);

        var argsTemplate = GetOptionString(toolDef.Options, "ARGS") ?? string.Empty;
        var workingDir = GetOptionString(toolDef.Options, "WORKING_DIR") ?? string.Empty;
        var timeoutSecs = GetOptionLong(toolDef.Options, "TIMEOUT") ?? 60L;

        // Resolve parameters
        var args = argsTemplate;
        if (stmt.Parameters != null)
        {
            var evaluator = (Evaluator)context;
            foreach (var kvp in stmt.Parameters)
            {
                var val = await evaluator.ExpressionEvaluator.Evaluate(kvp.Value, Row.Empty);
                args = args.Replace($"{{{kvp.Key}}}", val?.ToString() ?? string.Empty);
            }
        }

        context.Log($"Executing tool '{stmt.ToolAlias}'...");
        
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (toolType == "CONTAINER")
        {
            var image = GetOptionString(toolDef.Options, "IMAGE");
            if (string.IsNullOrWhiteSpace(image))
                throw new ExecutionException($"Tool '{stmt.ToolAlias}' of type CONTAINER is missing the IMAGE option.", null, stmt.Line, stmt.Column);
            
            var isolateNetwork = GetOptionBoolean(toolDef.Options, "ISOLATE_NETWORK") ?? true;

            startInfo.FileName = "docker";
            foreach (var arg in new[] { "run", "-i", "--rm", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true" })
            {
                startInfo.ArgumentList.Add(arg);
            }
            if (isolateNetwork)
            {
                startInfo.ArgumentList.Add("--network");
                startInfo.ArgumentList.Add("none");
            }
            
            var mounts = GetOptionString(toolDef.Options, "CAPABILITY_MOUNTS");
            if (!string.IsNullOrWhiteSpace(mounts))
            {
                foreach (var m in mounts.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    startInfo.ArgumentList.Add("-v");
                    startInfo.ArgumentList.Add(m.Trim());
                }
            }
            
            var secrets = GetOptionString(toolDef.Options, "CAPABILITY_SECRETS");
            if (!string.IsNullOrWhiteSpace(secrets) && stmt.Parameters != null)
            {
                var evaluator = (Evaluator)context;
                foreach (var s in secrets.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var secretKey = s.Trim();
                    if (stmt.Parameters.TryGetValue(secretKey, out var paramExpr))
                    {
                        var val = await evaluator.ExpressionEvaluator.Evaluate(paramExpr, Row.Empty, decryptSensitive: true);
                        var strVal = val?.ToString() ?? string.Empty;
                        
                        startInfo.ArgumentList.Add("-e");
                        startInfo.ArgumentList.Add($"{secretKey}={strVal}");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                startInfo.ArgumentList.Add("-w");
                startInfo.ArgumentList.Add(workingDir);
            }

            startInfo.ArgumentList.Add(image);
            
            if (!string.IsNullOrWhiteSpace(command))
            {
                startInfo.ArgumentList.Add(command);
            }
            
            if (!string.IsNullOrWhiteSpace(args))
            {
                var splitArgs = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach(var arg in splitArgs) 
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }
        }
        else
        {
            startInfo.FileName = command;
            startInfo.Arguments = args;
            startInfo.WorkingDirectory = workingDir;
        }

        using var process = new Process { StartInfo = startInfo };
        
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new ExecutionException($"Failed to start tool '{stmt.ToolAlias}': {ex.Message}", null, stmt.Line, stmt.Column);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSecs));
        
        var inputTask = StreamInputAsync(stmt.SourceTable, process.StandardInput, context, cts.Token);
        var outputTask = StreamOutputAsync(stmt.TargetTable, process.StandardOutput, context, stmt.ExpectedSchema, cts.Token);
        
        var errorOutput = new System.Text.StringBuilder();
        var errorTask = Task.Run(async () =>
        {
            var line = await process.StandardError.ReadLineAsync(cts.Token);
            while (line != null)
            {
                errorOutput.AppendLine(line);
                line = await process.StandardError.ReadLineAsync(cts.Token);
            }
        }, cts.Token);

        IDataSource? stagedOutput = null;
        try
        {
            await Task.WhenAll(inputTask, outputTask, errorTask);
            stagedOutput = await outputTask;
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);

            var actor = context.ExecutionIdentity?.RealUser ?? context.ExecutionPolicy?.Actor ?? "system";
            var effective = context.ExecutionIdentity?.EffectiveUser ?? context.ExecutionPolicy?.Actor ?? actor;
            
            SecurityEventRuntime.Emit(SecurityEventContract.Create(
                SecurityEventSeverity.Error,
                SecurityEventType.ResourceLimitViolation,
                actor,
                effective,
                $"Tool:{stmt.ToolAlias}",
                SecurityEventDecision.Failed,
                $"Tool execution timed out after {timeoutSecs} seconds.") with
            {
                ScriptHash = context.ExecutionPolicy?.ScriptHash,
                JobId = context.ExecutionPolicy?.JobId,
                CorrelationId = context.ExecutionPolicy?.CorrelationId,
                PolicyVersion = context.ExecutionPolicy?.PolicyVersion,
                PolicyHash = context.ExecutionPolicy?.PolicyHash
            });

            throw new ExecutionException($"Tool '{stmt.ToolAlias}' execution timed out after {timeoutSecs} seconds.", null, stmt.Line, stmt.Column);
        }

        if (process.ExitCode != 0)
        {
            var errStr = errorOutput.ToString();
            var actor = context.ExecutionIdentity?.RealUser ?? context.ExecutionPolicy?.Actor ?? "system";
            var effective = context.ExecutionIdentity?.EffectiveUser ?? context.ExecutionPolicy?.Actor ?? actor;

            SecurityEventRuntime.Emit(SecurityEventContract.Create(
                SecurityEventSeverity.Error,
                SecurityEventType.OperationDenied,
                actor,
                effective,
                $"Tool:{stmt.ToolAlias}",
                SecurityEventDecision.Failed,
                $"Tool execution failed with exit code {process.ExitCode}. Error: {errStr}") with
            {
                ScriptHash = context.ExecutionPolicy?.ScriptHash,
                JobId = context.ExecutionPolicy?.JobId,
                CorrelationId = context.ExecutionPolicy?.CorrelationId,
                PolicyVersion = context.ExecutionPolicy?.PolicyVersion,
                PolicyHash = context.ExecutionPolicy?.PolicyHash
            });

            throw new ExecutionException($"Tool '{stmt.ToolAlias}' failed with exit code {process.ExitCode}. Error: {errStr}", null, stmt.Line, stmt.Column);
        }

        if (stmt.TargetTable != null && stagedOutput != null)
        {
            var targetName = stmt.TargetTable.TableName;
            if (targetName.StartsWith("#"))
            {
                context.Connections[targetName] = stagedOutput;
            }
            else
            {
                if (context.Connections.TryGetValue(targetName, out var realTarget))
                {
                    await foreach (var batch in stagedOutput.ReadBatches(1000, cts.Token))
                    {
                        await realTarget.WriteBatches(new[] { batch }.ToAsyncEnumerable(), append: true, cts.Token);
                    }
                }
            }

            if (stmt.SourceTable != null)
            {
                context.LineageContext.LineageTracker.Record(
                    targetName,
                    new[] { stmt.SourceTable.TableName },
                    "EXECUTE_TOOL",
                    null, null, null, null,
                    stmt.Line, stmt.Column, stmt.Line, stmt.Column,
                    null,
                    TransformationKind.Unknown,
                    $"EXECUTE TOOL {stmt.ToolAlias}"
                );
            }
        }

        context.Log($"Tool '{stmt.ToolAlias}' execution completed successfully.");
    }

    private string? GetOptionString(Dictionary<string, Expression>? options, string key)
    {
        if (options == null || !options.TryGetValue(key, out var expr)) return null;
        if (expr is LiteralExpression lit) return lit.Value?.ToString();
        return null; // Ignore dynamic options for now
    }

    private long? GetOptionLong(Dictionary<string, Expression>? options, string key)
    {
        if (options == null || !options.TryGetValue(key, out var expr)) return null;
        if (expr is LiteralExpression lit && lit.Value is long l) return l;
        if (expr is LiteralExpression litInt && litInt.Value is int i) return i;
        return null;
    }

    private bool? GetOptionBoolean(Dictionary<string, Expression>? options, string key)
    {
        if (options == null || !options.TryGetValue(key, out var expr)) return null;
        if (expr is LiteralExpression lit && lit.Value is bool b) return b;
        if (expr is LiteralExpression litStr && bool.TryParse(litStr.Value?.ToString(), out var parsed)) return parsed;
        return null;
    }

    private async Task StreamInputAsync(TableReference? sourceTable, StreamWriter stdin, IExecutionContext context, CancellationToken token)
    {
        if (sourceTable == null)
        {
            stdin.Close();
            return;
        }

        var sourceName = sourceTable.TableName;
        if (!context.Connections.TryGetValue(sourceName, out var sourceDs))
            throw new ExecutionException($"Source table '{sourceName}' not found.");

        var columns = await sourceDs.GetColumnsAsync(token);
        var colList = columns.ToList();

        await foreach (var batch in sourceDs.ReadBatches(1000, token))
        {
            foreach (var row in batch.Rows)
            {
                var dict = new Dictionary<string, object?>();
                for (int i = 0; i < colList.Count; i++)
                {
                    dict[colList[i]] = row[i];
                }
                var json = JsonSerializer.Serialize(dict);
                await stdin.WriteLineAsync(json.AsMemory(), token);
            }
        }
        stdin.Close();
    }

    private async Task<IDataSource?> StreamOutputAsync(TableReference? targetTable, StreamReader stdout, IExecutionContext context, List<ExpectedSchemaColumn>? expectedSchema, CancellationToken token)
    {
        if (targetTable == null)
        {
            // Just consume and discard output if not requested
            while (await stdout.ReadLineAsync(token) != null) { }
            return null;
        }

        var targetName = targetTable.TableName;
        var cols = expectedSchema?.Select(x => x.ColumnName).ToList() ?? new List<string>();
        
        IDataSource? targetDs = null;
        var mem = new InMemoryDataSource();
        var evaluator = (Evaluator)context;
        mem.Validator = evaluator;
        mem.ExecutionContext = context;
        mem.MaxInMemoryBatches = evaluator.MaxInMemoryBatches;
        var colDefs = expectedSchema?.Select(x => new ColumnDefinition(x.ColumnName, x.DataType, false, null, null)).ToList() ?? new List<ColumnDefinition>();
        mem.SetSchema(colDefs);
        
        targetDs = mem;

        var schema = new TableSchema(cols);
        var batch = new DataTable();
        batch.SetColumns(cols);

        string? line;
        while ((line = await stdout.ReadLineAsync(token)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(line);
                var row = new Row(schema);
                
                if (dict != null)
                {
                    for (int i = 0; i < cols.Count; i++)
                    {
                        var colName = cols[i];
                        if (dict.TryGetValue(colName, out var val))
                        {
                            if (val is JsonElement je)
                            {
                                row[i] = je.ValueKind switch
                                {
                                    JsonValueKind.String => je.GetString(),
                                    JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                                    JsonValueKind.True => true,
                                    JsonValueKind.False => false,
                                    _ => je.ToString()
                                };
                            }
                            else
                            {
                                row[i] = val;
                            }
                        }
                    }
                }
                batch.Rows.Add(row);

                if (batch.Rows.Count >= 1000)
                {
                    await WriteBatchAsync(targetDs, batch, token);
                    batch = new DataTable();
                    batch.SetColumns(cols);
                }
            }
            catch (JsonException ex)
            {
                _logger.Warning("Failed to parse tool output JSON: {Error}", ex.Message);
            }
        }

        if (batch.Rows.Count > 0)
        {
            await WriteBatchAsync(targetDs, batch, token);
        }
        return targetDs;
    }

    private async Task WriteBatchAsync(IDataSource targetDs, DataTable batch, CancellationToken token)
    {
        var batches = new[] { batch }.ToAsyncEnumerable();
        await targetDs.WriteBatches(batches, append: true, token);
    }
}
