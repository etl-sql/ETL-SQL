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
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles EXECUTE TOOL statements.
/// Runs a registered tool process, streams data to it via stdin (JSON Lines),
/// and collects results from stdout (JSON Lines).
/// </summary>
public class ExecuteToolStatementHandler(ILogger logger, IToolCatalogProvider? catalog = null, IConfiguration? config = null, ICapabilityTokenIssuer? tokenIssuer = null, IToolProcessFactory? processFactory = null) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    private readonly IConfiguration? _config = config;
    private readonly ICapabilityTokenIssuer? _tokenIssuer = tokenIssuer;
    private readonly IToolProcessFactory _processFactory = processFactory ?? new ToolProcessFactory();
    public Type SupportedStatementType => typeof(ExecuteToolStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ExecuteToolStatement)statement;

        ToolDefinition toolDef;
        if (catalog != null)
        {
            try
            {
                toolDef = await catalog.ResolveAsync(stmt.ToolAlias, context.ExecutionIdentity, context.CancellationToken);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                throw new ExecutionException($"Tool '{stmt.ToolAlias}' could not be resolved from the catalog: {ex.Message}", null, stmt.Line, stmt.Column);
            }
        }
        else
        {
            throw new ExecutionException($"Tool '{stmt.ToolAlias}' cannot be resolved because no ToolCatalogProvider is configured.", null, stmt.Line, stmt.Column);
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
        var secretKeys = (GetOptionString(toolDef.Options, "CAPABILITY_SECRETS") ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Resolve parameters
        var args = argsTemplate;
        if (stmt.Parameters != null)
        {
            var evaluator = (Evaluator)context;
            foreach (var kvp in stmt.Parameters)
            {
                if (secretKeys.Contains(kvp.Key))
                {
                    if (argsTemplate.Contains($"{{{kvp.Key}}}", StringComparison.OrdinalIgnoreCase))
                        throw new ExecutionException("Capability secrets must be passed through the child environment, not ARGS.");
                    continue;
                }
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

            // 1. Pinned images
            if (!image.Contains("@sha256:"))
                throw new ExecutionException($"Tool '{stmt.ToolAlias}' of type CONTAINER must use a pinned image digest (e.g. @sha256:...).", null, stmt.Line, stmt.Column);

            var isolateNetwork = GetOptionBoolean(toolDef.Options, "ISOLATE_NETWORK") ?? true;

            startInfo.FileName = "docker";
            foreach (var arg in new[] { "run", "-i", "--rm", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true" })
            {
                startInfo.ArgumentList.Add(arg);
            }

            // 2. Non-root identity
            var runAsUser = GetOptionString(toolDef.Options, "RUN_AS_USER") ?? "65534:65534";
            startInfo.ArgumentList.Add("--user");
            startInfo.ArgumentList.Add(runAsUser);

            // 3. Isolated scratch
            startInfo.ArgumentList.Add("--tmpfs");
            startInfo.ArgumentList.Add("/tmp:rw,noexec,nosuid,size=65536k");

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

                        AddContainerSecret(startInfo, secretKey, strVal);
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
                foreach (var arg in splitArgs)
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }
        }
        else
        {
            startInfo.FileName = command;
            if (!string.IsNullOrWhiteSpace(args))
            {
                var splitArgs = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var arg in splitArgs)
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }

            // Sanitized environment: explicitly clear and only allowlist PATH
            var path = startInfo.EnvironmentVariables.ContainsKey("PATH") ? startInfo.EnvironmentVariables["PATH"] : null;
            startInfo.EnvironmentVariables.Clear();
            if (path != null) startInfo.EnvironmentVariables["PATH"] = path;

            // Canonical scratch root
            workingDir = Path.Combine(Path.GetTempPath(), "etlsql_tool_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDir);
            startInfo.WorkingDirectory = workingDir;
        }

        IDataSource? ownedOutput = null;
        try
        {
            using var process = _processFactory.Create(startInfo);

            // P2 - Immutable Logical Checkpoint Identities
            var argString = string.Join("|", startInfo.ArgumentList);
            var envKeys = startInfo.EnvironmentVariables.Keys.Cast<string>()
                .Where(k => k != "ETLSQL_CAPABILITY_TOKEN" && k != "ETLSQL_IDEMPOTENCY_KEY")
                .OrderBy(k => k);
            var envString = string.Join("|", envKeys.Select(k => $"{k}={startInfo.EnvironmentVariables[k]}"));
            var policyHash = context.ExecutionPolicy?.PolicyHash ?? "no-policy";
            var identityMaterial = $"{context.SessionId ?? "temp"}_{stmt.ToolAlias}_{stmt.Line}_{toolDef.ToolType}_{policyHash}_{argString}_{envString}";

            string operationId;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(identityMaterial));
                operationId = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            if (context.Ledger != null && stmt.TargetTable == null)
            {
                var existingOp = await context.Ledger.GetStateAsync(operationId);
                if (existingOp?.Status == ETL_SQL.Core.Execution.OperationStatus.Completed)
                {
                    _logger.Info("Tool '{ToolAlias}' already completed successfully in a previous run (Idempotency Key: {OpId}). Skipping.", stmt.ToolAlias, operationId);
                    return;
                }

                await context.Ledger.RecordStartAsync(operationId, "ToolExecution", stmt.ToolAlias);
            }

            try
            {
                if (context.Ledger != null && stmt.TargetTable == null)
                {
                    startInfo.EnvironmentVariables["ETLSQL_IDEMPOTENCY_KEY"] = operationId;
                }

                // P2 - Capability Tokens
                if (_tokenIssuer != null)
                {
                    var hasCapabilities = toolDef.Options?.ContainsKey("CAPABILITIES") == true || (stmt.Parameters?.ContainsKey("CAPABILITIES") == true);
                    if (hasCapabilities)
                    {
                        var policy = context.ExecutionPolicy;
                        var actor = context.ExecutionIdentity?.RealUser ?? context.ExecutionPolicy?.Actor ?? "system";
                        var rawCaps = GetOptionString(toolDef.Options, "CAPABILITIES") ?? string.Empty;

                        var cap = new CapabilityToken
                        {
                            TenantId = context.StorageCapability?.Tenant?.Tenant.Value,
                            Environment = null,
                            ToolDigest = toolDef.ToolType,
                            OperationId = operationId,
                            Actor = actor,
                            RunAttempt = policy?.JobId,
                            PolicyVersion = policy?.PolicyVersion,
                            Nonce = Guid.NewGuid().ToString("N"),
                            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(timeoutSecs / 60.0 + 5), // Bound to timeout
                            AllowNetworkAccess = rawCaps.Contains("NETWORK", StringComparison.OrdinalIgnoreCase),
                            AllowGatewayResources = rawCaps.Contains("GATEWAY", StringComparison.OrdinalIgnoreCase)
                        };

                        startInfo.EnvironmentVariables["ETLSQL_CAPABILITY_TOKEN"] = _tokenIssuer.IssueToken(cap);
                    }
                }

            }
            catch (Exception ex)
            {
                throw new ExecutionException($"Failed to start tool '{stmt.ToolAlias}': {ex.Message}", null, stmt.Line, stmt.Column);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSecs));
            var containerName = toolType == "CONTAINER" ? "etlsql-tool-" + Guid.NewGuid().ToString("N") : null;
            if (containerName != null)
            {
                // The per-attempt runtime name is deliberately excluded from the logical operation hash.
                startInfo.ArgumentList.Insert(1, "--name");
                startInfo.ArgumentList.Insert(2, containerName);
            }
            using var ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            var (stagedOutput, errorOutput) = await RunProcessAsync(process, containerName, stmt, context, ioCancellation, timeoutSecs);
            ownedOutput = stagedOutput;

            if (process.ExitCode != 0)
            {
                if (context.Ledger != null && stmt.TargetTable == null)
                {
                    await context.Ledger.RecordCompletionAsync(operationId, process.ExitCode, errorOutput.ToString());
                }

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

            if (context.Ledger != null && stmt.TargetTable == null)
            {
                await context.Ledger.RecordCompletionAsync(operationId, process.ExitCode, null);
            }

            if (stmt.TargetTable != null && stagedOutput != null)
            {
                var targetName = stmt.TargetTable.TableName;
                if (targetName.StartsWith("#"))
                {
                    context.Connections[targetName] = stagedOutput;
                    ownedOutput = null;
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

            var actorSuccess = context.ExecutionIdentity?.RealUser ?? context.ExecutionPolicy?.Actor ?? "system";
            var effectiveSuccess = context.ExecutionIdentity?.EffectiveUser ?? context.ExecutionPolicy?.Actor ?? actorSuccess;

            SecurityEventRuntime.Emit(SecurityEventContract.Create(
                SecurityEventSeverity.Information,
                SecurityEventType.ExecutionCompleted,
                actorSuccess,
                effectiveSuccess,
                $"Tool:{stmt.ToolAlias}",
                SecurityEventDecision.Allowed,
                $"Tool execution completed successfully without violating resource limits or boundary policy.") with
            {
                ScriptHash = context.ExecutionPolicy?.ScriptHash,
                JobId = context.ExecutionPolicy?.JobId,
                CorrelationId = context.ExecutionPolicy?.CorrelationId,
                PolicyVersion = context.ExecutionPolicy?.PolicyVersion,
                PolicyHash = context.ExecutionPolicy?.PolicyHash
            });

            context.Log($"Tool '{stmt.ToolAlias}' execution completed successfully.");
        }
        finally
        {
            if (ownedOutput != null) await ownedOutput.DisposeAsync();
            if (toolType == "EXECUTABLE" && !string.IsNullOrEmpty(workingDir) && workingDir.StartsWith(Path.GetTempPath()))
            {
                try
                {
                    if (Directory.Exists(workingDir))
                    {
                        Directory.Delete(workingDir, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning("Failed to clean up scratch root {Dir}: {Error}", workingDir, ex.Message);
                }
            }
        }
    }

    private async Task<(IDataSource? Output, string Error)> RunProcessAsync(IToolProcess process, string? containerName,
        ExecuteToolStatement statement, IExecutionContext context, CancellationTokenSource cancellation, long timeoutSeconds)
    {
        var started = false;
        var attempted = false;
        var succeeded = false;
        Task<IDataSource?>? output = null;
        Task[] streams = [];
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            attempted = true;
            process.Start();
            started = true;
            var input = CancelOnFailureAsync(async () =>
            {
                await StreamInputAsync(statement.SourceTable, process.StandardInput, context, cancellation.Token);
                return true;
            }, cancellation);
            output = CancelOnFailureAsync(() => StreamOutputAsync(statement.TargetTable, process.StandardOutput,
                context, statement.ExpectedSchema, cancellation.Token), cancellation);
            var error = CancelOnFailureAsync(() => BoundedToolOutput.CaptureAsync(process.StandardError,
                Math.Max(2, _config?.GetValue<int?>("Tools:Limits:MaxStderrChars") ?? 65536),
                () => _logger.Warning("Tool stderr truncated: tool={Tool} run={Run} correlation={Correlation}",
                    statement.ToolAlias, context.ExecutionPolicy?.JobId, context.ExecutionPolicy?.CorrelationId), cancellation.Token), cancellation);
            streams = [input, output, error];
            await Task.WhenAll(streams).WaitAsync(cancellation.Token);
            await process.WaitForExitAsync(cancellation.Token);
            succeeded = true;
            return (await output, await error);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            EmitToolFailure(statement, context, SecurityEventType.OperationDenied, "Tool execution cancelled by the enclosing run.");
            throw new OperationCanceledException("Tool execution cancelled by the enclosing run.", context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A stream failure also cancels peers. Preserve that failure instead of calling it a timeout.
            var fault = streams.FirstOrDefault(task => task.IsFaulted)?.Exception?.InnerException;
            if (fault != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();
            EmitToolFailure(statement, context, SecurityEventType.ResourceLimitViolation, $"Tool execution timed out after {timeoutSeconds} seconds.");
            throw new ExecutionException($"Tool '{statement.ToolAlias}' execution timed out after {timeoutSeconds} seconds.");
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                if (started)
                {
                    if (!process.HasExited) process.Kill();
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds));
                    await process.WaitForExitAsync(deadline.Token);
                    // Close local pipes as well as cancelling their reads/writes before joining workers.
                    process.StandardInput.Dispose();
                    process.StandardOutput.Dispose();
                    process.StandardError.Dispose();
                    try { await Task.WhenAll(streams).WaitAsync(deadline.Token); }
                    catch (Exception) when (streams.All(task => task.IsCompleted)) { /* Observed stream failures are handled above. */ }
                }
            }
            catch (Exception ex)
            {
                succeeded = false;
                _logger.Error("Tool process teardown failed: tool={Tool} run={Run}", ex, statement.ToolAlias, context.ExecutionPolicy?.JobId);
                throw;
            }
            finally
            {
                try
                {
                    if (attempted && containerName != null) await RemoveContainerAsync(containerName, statement, context);
                }
                catch { succeeded = false; throw; }
                finally
                {
                    if (!succeeded && output?.Status == TaskStatus.RanToCompletion)
                    {
                        var abandonedOutput = await output;
                        if (abandonedOutput != null) await abandonedOutput.DisposeAsync();
                    }
                }
            }
        }
    }

    private int CleanupSeconds => Math.Max(1, _config?.GetValue<int?>("Tools:Limits:CleanupTimeoutSeconds") ?? 30);

    private static async Task<T> CancelOnFailureAsync<T>(Func<Task<T>> action, CancellationTokenSource cancellation)
    {
        try { return await action(); }
        catch { cancellation.Cancel(); throw; }
    }

    private async Task RemoveContainerAsync(string name, ExecuteToolStatement statement, IExecutionContext context)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds));
            // A failed run may not have created a container. Only a successful empty daemon query proves absence.
            await RunDockerCommandAsync(["rm", "--force", name], deadline.Token);
            var remaining = await RunDockerCommandAsync(
                ["container", "ls", "--all", "--filter", $"name=^/{name}$", "--format", "{{.ID}}"], deadline.Token);
            if (remaining.ExitCode != 0 || !string.IsNullOrWhiteSpace(remaining.Output))
                throw new ExecutionException("Docker did not confirm tool container removal.");
        }
        catch (Exception ex)
        {
            _logger.Error("Tool container teardown failed: container={Container} tool={Tool} run={Run} correlation={Correlation}. Remove this container before retrying.",
                ex, name, statement.ToolAlias, context.ExecutionPolicy?.JobId, context.ExecutionPolicy?.CorrelationId);
            EmitToolFailure(statement, context, SecurityEventType.OperationDenied, $"Tool container {name} removal could not be verified; operator cleanup is required.");
            throw new ExecutionException($"Tool container '{name}' removal could not be verified.", ex);
        }
    }

    private async Task<(int ExitCode, string Output)> RunDockerCommandAsync(string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = _processFactory.Create(start);
        process.Start();
        Task<string>? output = null;
        Task<string>? error = null;
        try
        {
            process.StandardInput.Close();
            output = BoundedToolOutput.CaptureAsync(process.StandardOutput, 4096, () => { }, token);
            error = BoundedToolOutput.CaptureAsync(process.StandardError, 4096, () => { }, token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(token)).WaitAsync(token);
            return (process.ExitCode, await output);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds));
            await process.WaitForExitAsync(deadline.Token);
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            if (output != null && error != null)
            {
                try { await Task.WhenAll(output, error).WaitAsync(deadline.Token); }
                catch (Exception) when (output.IsCompleted && error.IsCompleted) { /* Observe cancelled or failed cleanup reads. */ }
            }
        }
    }

    private static void EmitToolFailure(ExecuteToolStatement statement, IExecutionContext context, SecurityEventType type, string message)
    {
        var actor = context.ExecutionIdentity?.RealUser ?? context.ExecutionPolicy?.Actor ?? "system";
        SecurityEventRuntime.Emit(SecurityEventContract.Create(SecurityEventSeverity.Error, type, actor,
            context.ExecutionIdentity?.EffectiveUser ?? actor, $"Tool:{statement.ToolAlias}", SecurityEventDecision.Failed, message) with
        {
            ScriptHash = context.ExecutionPolicy?.ScriptHash,
            JobId = context.ExecutionPolicy?.JobId,
            CorrelationId = context.ExecutionPolicy?.CorrelationId,
            PolicyVersion = context.ExecutionPolicy?.PolicyVersion,
            PolicyHash = context.ExecutionPolicy?.PolicyHash
        });
    }

    private static void AddContainerSecret(ProcessStartInfo startInfo, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || !(char.IsAsciiLetter(name[0]) || name[0] == '_')
            || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ExecutionException("Capability secret names must be valid environment variable names.");

        ETL_SQL.Core.Common.SecretRedactor.RegisterRuntimeSecret(value);
        startInfo.Environment[name] = value;
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(name);
    }

    private string? GetOptionString(IReadOnlyDictionary<string, string>? options, string key)
    {
        if (options == null || !options.TryGetValue(key, out var val)) return null;
        return val;
    }

    private long? GetOptionLong(IReadOnlyDictionary<string, string>? options, string key)
    {
        if (options == null || !options.TryGetValue(key, out var val)) return null;
        if (long.TryParse(val, out var l)) return l;
        return null;
    }

    private bool? GetOptionBoolean(IReadOnlyDictionary<string, string>? options, string key)
    {
        if (options == null || !options.TryGetValue(key, out var val)) return null;
        if (bool.TryParse(val, out var b)) return b;
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
        var lines = BoundedToolOutput.LinesAsync(stdout,
            Math.Max(1, _config?.GetValue<int?>("Tools:Limits:MaxLineChars") ?? 1048576),
            Math.Max(1, _config?.GetValue<long?>("Tools:Limits:MaxBytes") ?? 100 * 1024 * 1024), token);
        if (targetTable == null)
        {
            // Just consume and discard output if not requested
            await foreach (var ignored in lines) { }
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
        try
        {
            var schema = new TableSchema(cols);
            var batch = new DataTable();
            batch.SetColumns(cols);

            int rowCount = 0;
            int MaxRows = Math.Max(1, _config?.GetValue<int?>("Tools:Limits:MaxRows") ?? 1_000_000);

            await foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                rowCount++;
                if (rowCount > MaxRows)
                    throw new ExecutionException($"Tool output exceeded the maximum allowed row count of {MaxRows}.", null, 0, 0);

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
                                object? rawVal = null;
                                if (val is JsonElement je)
                                {
                                    rawVal = je.ValueKind switch
                                    {
                                        JsonValueKind.String => je.GetString(),
                                        // COMPAT_BREAK: 0.20 — preserve exact tool numbers before schema conversion.
                                        JsonValueKind.Number => ReadNumber(je, expectedSchema?[i].DataType),
                                        JsonValueKind.True => true,
                                        JsonValueKind.False => false,
                                        JsonValueKind.Null => null,
                                        _ => je.ToString()
                                    };
                                }
                                else
                                {
                                    rawVal = val;
                                }

                                row[i] = rawVal != null && expectedSchema != null && expectedSchema.Count > i
                                    ? ETL_SQL.Core.Data.TypeConverter.Cast(rawVal, expectedSchema[i].DataType)
                                    : rawVal;
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
                    throw new ExecutionException($"Tool output malformed JSON at row {rowCount}: {ex.Message}", null, 0, 0);
                }
            }

            if (batch.Rows.Count > 0)
            {
                await WriteBatchAsync(targetDs, batch, token);
            }
            return targetDs;
        }
        catch
        {
            await mem.DisposeAsync();
            throw;
        }
    }

    private static object ReadNumber(JsonElement value, string? declaredType)
    {
        var baseType = declaredType?.Split('(')[0].Trim().ToUpperInvariant();
        if (baseType is "FLOAT" or "DOUBLE") return value.GetDouble();
        if (baseType == "REAL") return value.GetSingle();
        if (value.TryGetInt64(out var integer)) return integer;
        return value.GetDecimal();
    }

    private async Task WriteBatchAsync(IDataSource targetDs, DataTable batch, CancellationToken token)
    {
        var batches = new[] { batch }.ToAsyncEnumerable();
        await targetDs.WriteBatches(batches, append: true, token);
    }
}
