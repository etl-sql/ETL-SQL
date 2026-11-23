using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
public partial class ExecuteToolStatementHandler(ILogger logger, IToolCatalogProvider? catalog = null, IConfiguration? config = null, ICapabilityTokenIssuer? tokenIssuer = null, IToolProcessFactory? processFactory = null) : IStatementHandler
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

        // Resolve parameters. COMPAT_BREAK: 0.22.0 — the ARGS template is split into arguments
        // before values are substituted in a single pass, and parameter roles/flags are validated
        // against the approved tool contract.
        var argValues = new Dictionary<string, string>();
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
                argValues[kvp.Key] = val?.ToString() ?? string.Empty;
            }
        }

        string? containerImage = null;
        string? containerRuntime = null;
        string? containerMemoryArg = null;
        string? containerCpusArg = null;
        long? containerPidsLimit = null;
        bool isolateNetwork = true;
        string? resolvedNativeCommand = null;
        string? validatedNativeWorkingDir = null;
        string? scratchDir = null;

        if (toolType == "CONTAINER")
        {
            containerImage = GetOptionString(toolDef.Options, "IMAGE");
            if (string.IsNullOrWhiteSpace(containerImage))
                throw new ExecutionException($"Tool '{stmt.ToolAlias}' of type CONTAINER is missing the IMAGE option.", null, stmt.Line, stmt.Column);

            ProcessPolicyRules.EnforceContainerToolImage(context, containerImage, _config);

            isolateNetwork = GetOptionBoolean(toolDef.Options, "ISOLATE_NETWORK") ?? true;
            (containerCpusArg, containerMemoryArg, containerPidsLimit, containerRuntime) =
                ValidateAndResolveContainerLimits(toolDef, isolateNetwork, stmt);
        }
        else
        {
            ValidateNativeIsolationContract(toolDef, context, stmt);
            var ownershipMode = ResolveOwnershipMode(catalog, _config);
            resolvedNativeCommand = ToolPathGuard.ValidateNativeCommandPath(
                command,
                ownershipMode,
                requireExists: _processFactory is ToolProcessFactory,
                verifyWriteBoundary: true);

            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                validatedNativeWorkingDir = ToolPathGuard.ValidateNativeDirectoryPath(
                    workingDir,
                    ownershipMode,
                    requireExists: _processFactory is ToolProcessFactory,
                    verifyWriteBoundary: true);
            }

            var scriptPath = GetOptionString(toolDef.Options, "SCRIPT_PATH");
            if (!string.IsNullOrWhiteSpace(scriptPath))
            {
                ToolPathGuard.ValidateNativeScriptPath(
                    scriptPath,
                    validatedNativeWorkingDir,
                    ownershipMode,
                    requireExists: _processFactory is ToolProcessFactory,
                    verifyWriteBoundary: true);
            }
        }

        var args = ValidateAndBuildArguments(toolDef, toolType == "CONTAINER" ? command : resolvedNativeCommand!, argsTemplate, argValues);

        if (context.IsWhatIf)
        {
            var targetSummary = toolType == "CONTAINER" ? containerImage : resolvedNativeCommand;
            context.Log($"WHAT IF: Would execute tool '{stmt.ToolAlias}' ({toolType}) targeting '{targetSummary}' with {args.Count} argument(s).");
            return;
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
            startInfo.FileName = "docker";
            foreach (var arg in new[] { "run", "-i", "--rm", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true" })
            {
                startInfo.ArgumentList.Add(arg);
            }

            if (!string.IsNullOrWhiteSpace(containerRuntime))
            {
                startInfo.ArgumentList.Add("--runtime");
                startInfo.ArgumentList.Add(containerRuntime);
            }

            // 2. Non-root identity
            var runAsUser = GetOptionString(toolDef.Options, "RUN_AS_USER") ?? "65534:65534";
            startInfo.ArgumentList.Add("--user");
            startInfo.ArgumentList.Add(runAsUser);

            // 3. Container CPU, memory, and PID ceilings
            startInfo.ArgumentList.Add("--cpus");
            startInfo.ArgumentList.Add(containerCpusArg!);
            startInfo.ArgumentList.Add("--memory");
            startInfo.ArgumentList.Add(containerMemoryArg!);
            startInfo.ArgumentList.Add("--memory-swap");
            startInfo.ArgumentList.Add(containerMemoryArg!);
            startInfo.ArgumentList.Add("--pids-limit");
            startInfo.ArgumentList.Add(containerPidsLimit!.Value.ToString(CultureInfo.InvariantCulture));

            // 4. Isolated scratch
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

            startInfo.ArgumentList.Add(containerImage!);

            if (!string.IsNullOrWhiteSpace(command))
            {
                startInfo.ArgumentList.Add(command);
            }

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }
        else
        {
            startInfo.FileName = resolvedNativeCommand!;
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            // Sanitized environment: explicitly clear and do not rely on ambient PATH for binary lookup.
            var path = startInfo.EnvironmentVariables.ContainsKey("PATH") ? startInfo.EnvironmentVariables["PATH"] : null;
            startInfo.EnvironmentVariables.Clear();
            if (path != null) startInfo.EnvironmentVariables["PATH"] = path;

            // Disposable per-invocation scratch root, kept separate from any approved WORKING_DIR.
            scratchDir = Path.Combine(Path.GetTempPath(), "etlsql_tool_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratchDir);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(scratchDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            startInfo.EnvironmentVariables["ETLSQL_SCRATCH_DIR"] = scratchDir;
            startInfo.EnvironmentVariables["TMP"] = scratchDir;
            startInfo.EnvironmentVariables["TEMP"] = scratchDir;
            startInfo.EnvironmentVariables["TMPDIR"] = scratchDir;
            startInfo.WorkingDirectory = validatedNativeWorkingDir ?? scratchDir;
        }

        IDataSource? ownedOutput = null;
        try
        {
            using var process = _processFactory.Create(startInfo);

            // P2 - Immutable Logical Checkpoint Identities
            var argString = string.Join("|", startInfo.ArgumentList);
            var envKeys = startInfo.EnvironmentVariables.Keys.Cast<string>()
                .Where(k => k != "ETLSQL_CAPABILITY_TOKEN"
                    && k != "ETLSQL_IDEMPOTENCY_KEY"
                    && k != "ETLSQL_SCRATCH_DIR"
                    && k != "TMP"
                    && k != "TEMP"
                    && k != "TMPDIR")
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
            if (toolType == "EXECUTABLE"
                && !string.IsNullOrEmpty(scratchDir)
                && !string.Equals(scratchDir, validatedNativeWorkingDir, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (Directory.Exists(scratchDir))
                    {
                        Directory.Delete(scratchDir, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning("Failed to clean up scratch root {Dir}: {Error}", scratchDir, ex.Message);
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

    /// <summary>
    /// Splits the ARGS template into argument tokens honoring single and double quotes (so paths
    /// with spaces stay intact), then performs a single-pass replacement of each <c>{NAME}</c>
    /// placeholder inside each token. Values never introduce new arguments or expand nested
    /// placeholders; an argument left empty is dropped.
    /// </summary>
    internal static List<string> BuildArguments(string template, IReadOnlyDictionary<string, string> values)
    {
        var tokens = TokenizeArgumentTemplate(template);
        var result = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            var replaced = PlaceholderRegex().Replace(token, match =>
            {
                var key = match.Groups[1].Value;
                return values.TryGetValue(key, out var val) ? val : match.Value;
            });
            if (!string.IsNullOrEmpty(replaced))
                result.Add(replaced);
        }

        return result;
    }

    internal static List<string> TokenizeArgumentTemplate(string template)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
            return tokens;

        var current = new System.Text.StringBuilder();
        var inToken = false;
        char quote = '\0';

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];

            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < template.Length && template[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                    continue;
                }

                current.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                inToken = true;
                continue;
            }

            current.Append(c);
            inToken = true;
        }

        if (quote != '\0')
        {
            throw new ExecutionException($"Tool ARGS template contains an unclosed {quote} quote.");
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    internal List<string> ValidateAndBuildArguments(
        ToolDefinition toolDef,
        string command,
        string template,
        IReadOnlyDictionary<string, string> values)
    {
        var allowCallerCodeArgs = GetOptionBoolean(toolDef.Options, "ALLOW_CALLER_CODE_ARGS") ?? false;
        var supportsEndOfOptions = GetOptionBoolean(toolDef.Options, "SUPPORTS_END_OF_OPTIONS") ?? false;
        var maxParamLength = (int)Math.Clamp(
            GetOptionLong(toolDef.Options, "MAX_PARAM_LENGTH")
                ?? _config?.GetValue<int?>("Tools:Limits:MaxParamLength")
                ?? 4096L,
            1, 1_048_576);

        var roles = ParseParamRoles(GetOptionString(toolDef.Options, "PARAM_ROLES"));
        foreach (var (paramName, value) in values)
        {
            ValidateParameterValue(paramName, value, maxParamLength, roles, allowCallerCodeArgs);
        }

        var tokens = TokenizeArgumentTemplate(template);
        var isInterpreter = IsInterpreterCommand(command);
        var hasFixedScriptBeforeParam = false;
        var pastEndOfOptions = false;
        var endOfOptionsInsertIndex = -1;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token == "--")
            {
                pastEndOfOptions = true;
                continue;
            }

            var placeholders = PlaceholderRegex().Matches(token);
            if (placeholders.Count == 0)
            {
                // A '/' token is a fixed script only when it is a multi-segment absolute path
                // (/opt/tools/run.py). cmd and Windows PowerShell take '/' switches of any length
                // (/NoProfile, /Command, /V:ON), and PowerShell runs its first positional argument.
                if (!token.StartsWith('-') && !(token.StartsWith('/') && token.IndexOf('/', 1) < 0))
                    hasFixedScriptBeforeParam = true;
                continue;
            }

            var eqIdx = token.IndexOf('=');
            if (token.StartsWith('-') || token.StartsWith('/'))
            {
                if (eqIdx < 0 || placeholders.Cast<Match>().Any(m => m.Index < eqIdx))
                {
                    throw new ExecutionException(
                        $"Tool '{toolDef.Name}' template token '{token}' cannot substitute parameters into an option switch name.");
                }

                if (isInterpreter && !allowCallerCodeArgs && IsInterpreterExecSwitch(token[..eqIdx]))
                {
                    throw new ExecutionException(
                        $"Tool '{toolDef.Name}' cannot pass caller parameters to interpreter execution switch '{token[..eqIdx]}' without explicit ALLOW_CALLER_CODE_ARGS authorization.");
                }
            }

            var prevToken = i > 0 ? tokens[i - 1] : null;
            var isInterpreterExecSwitch = isInterpreter && prevToken != null && IsInterpreterExecSwitch(prevToken);
            if (isInterpreterExecSwitch && !allowCallerCodeArgs)
            {
                throw new ExecutionException(
                    $"Tool '{toolDef.Name}' cannot pass caller parameters to interpreter execution switch '{prevToken}' without explicit ALLOW_CALLER_CODE_ARGS authorization.");
            }

            var isPurePlaceholder = token.Length > 2
                && token[0] == '{'
                && token[^1] == '}'
                && placeholders.Count == 1;

            if (isInterpreter && isPurePlaceholder && !pastEndOfOptions && !hasFixedScriptBeforeParam && !allowCallerCodeArgs)
            {
                var paramName = placeholders[0].Groups[1].Value;
                if (!IsConstrainedScalarRole(roles, paramName))
                {
                    throw new ExecutionException(
                        $"Tool '{toolDef.Name}' cannot pass caller parameter '{{{paramName}}}' as an interpreter entrypoint/script argument without explicit ALLOW_CALLER_CODE_ARGS authorization.");
                }
            }

            if (supportsEndOfOptions && !pastEndOfOptions && endOfOptionsInsertIndex < 0
                && isPurePlaceholder && (prevToken == null || !IsFlagTakingValue(prevToken)))
            {
                endOfOptionsInsertIndex = i;
            }
        }

        if (endOfOptionsInsertIndex >= 0 && !tokens.Contains("--"))
        {
            tokens.Insert(endOfOptionsInsertIndex, "--");
        }

        var result = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            var replaced = PlaceholderRegex().Replace(token, match =>
            {
                var key = match.Groups[1].Value;
                return values.TryGetValue(key, out var val) ? val : match.Value;
            });
            if (!string.IsNullOrEmpty(replaced))
                result.Add(replaced);
        }

        return result;
    }

    private (string Cpus, string MemoryArg, long PidsLimit, string? Runtime) ValidateAndResolveContainerLimits(
        ToolDefinition toolDef,
        bool isolateNetwork,
        ExecuteToolStatement stmt)
    {
        var profile = GetOptionString(toolDef.Options, "ISOLATION_PROFILE")
            ?? _config?["Tools:Limits:RequiredIsolationProfile"];

        string? hardenedRuntime = null;
        if (!string.IsNullOrWhiteSpace(profile))
        {
            if (profile.Equals("Hardened", StringComparison.OrdinalIgnoreCase)
                || profile.Equals("HardenedSandbox", StringComparison.OrdinalIgnoreCase)
                || profile.Equals("HardenedShared", StringComparison.OrdinalIgnoreCase)
                || profile.Equals("HostileTenant", StringComparison.OrdinalIgnoreCase))
            {
                var configuredRuntime = _config?["Tools:Limits:HardenedContainerRuntime"];
                var mounts = GetOptionString(toolDef.Options, "CAPABILITY_MOUNTS");
                var hasNetworkCapability = (GetOptionString(toolDef.Options, "CAPABILITIES") ?? string.Empty)
                    .Contains("NETWORK", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(configuredRuntime) || !isolateNetwork || hasNetworkCapability || !string.IsNullOrWhiteSpace(mounts))
                {
                    throw new ExecutionException(
                        $"Tool '{stmt.ToolAlias}' claims isolation profile '{profile}', which cannot be enforced without a configured hardened OCI runtime and full network/mount isolation.",
                        null, stmt.Line, stmt.Column);
                }

                hardenedRuntime = configuredRuntime.Trim();
            }
            else if (!profile.Equals("ContainerStandard", StringComparison.OrdinalIgnoreCase)
                && !profile.Equals("IsolatedContainer", StringComparison.OrdinalIgnoreCase))
            {
                throw new ExecutionException(
                    $"Tool '{stmt.ToolAlias}' specifies unsupported container isolation profile '{profile}'.",
                    null, stmt.Line, stmt.Column);
            }
        }

        var defaultMemoryMb = _config?.GetValue<long?>("Tools:Limits:DefaultContainerMemoryMb") ?? 512L;
        var maxMemoryMb = _config?.GetValue<long?>("Tools:Limits:MaxContainerMemoryMb") ?? 4096L;
        var requestedMemoryMb = GetOptionLong(toolDef.Options, "MEMORY_MB") ?? defaultMemoryMb;
        if (toolDef.Options.TryGetValue("MEMORY_MB", out var rawMem) && !long.TryParse(rawMem, out _))
            throw new ExecutionException($"Tool '{stmt.ToolAlias}' has invalid MEMORY_MB '{rawMem}'.", null, stmt.Line, stmt.Column);
        if (requestedMemoryMb <= 0 || requestedMemoryMb > maxMemoryMb)
        {
            throw new ExecutionException(
                $"Tool '{stmt.ToolAlias}' container memory limit {requestedMemoryMb}MB is outside the approved range (1..{maxMemoryMb}MB).",
                null, stmt.Line, stmt.Column);
        }

        var defaultCpus = _config?.GetValue<double?>("Tools:Limits:DefaultContainerCpus") ?? 1.0d;
        var maxCpus = _config?.GetValue<double?>("Tools:Limits:MaxContainerCpus") ?? 4.0d;
        double requestedCpus = defaultCpus;
        if (toolDef.Options.TryGetValue("CPUS", out var rawCpus))
        {
            if (!double.TryParse(rawCpus, NumberStyles.Float, CultureInfo.InvariantCulture, out requestedCpus)
                || !double.IsFinite(requestedCpus))
            {
                throw new ExecutionException($"Tool '{stmt.ToolAlias}' has invalid CPUS value '{rawCpus}'.", null, stmt.Line, stmt.Column);
            }
        }
        if (requestedCpus <= 0 || requestedCpus > maxCpus)
        {
            throw new ExecutionException(
                $"Tool '{stmt.ToolAlias}' container CPU limit {requestedCpus.ToString(CultureInfo.InvariantCulture)} is outside the approved range (0..{maxCpus.ToString(CultureInfo.InvariantCulture)}].",
                null, stmt.Line, stmt.Column);
        }

        var defaultPids = _config?.GetValue<long?>("Tools:Limits:DefaultContainerPidsLimit") ?? 128L;
        var maxPids = _config?.GetValue<long?>("Tools:Limits:MaxContainerPidsLimit") ?? 1024L;
        var requestedPids = GetOptionLong(toolDef.Options, "PIDS_LIMIT") ?? defaultPids;
        if (toolDef.Options.TryGetValue("PIDS_LIMIT", out var rawPids) && !long.TryParse(rawPids, out _))
            throw new ExecutionException($"Tool '{stmt.ToolAlias}' has invalid PIDS_LIMIT '{rawPids}'.", null, stmt.Line, stmt.Column);
        if (requestedPids <= 0 || requestedPids > maxPids)
        {
            throw new ExecutionException(
                $"Tool '{stmt.ToolAlias}' container PID limit {requestedPids} is outside the approved range (1..{maxPids}).",
                null, stmt.Line, stmt.Column);
        }

        return (
            requestedCpus.ToString("0.###", CultureInfo.InvariantCulture),
            $"{requestedMemoryMb.ToString(CultureInfo.InvariantCulture)}m",
            requestedPids,
            hardenedRuntime);
    }

    private void ValidateNativeIsolationContract(ToolDefinition toolDef, IExecutionContext context, ExecuteToolStatement stmt)
    {
        var hasTenant = !string.IsNullOrWhiteSpace(context.StorageCapability?.Tenant?.Tenant.Value)
            || !string.IsNullOrWhiteSpace(context.ExecutionIdentity?.TenantId);
        if (hasTenant && _config?.GetValue<bool>("Tools:AllowNativeToolsForTenants") != true)
        {
            throw new ExecutionException(
                $"Native EXECUTABLE tool '{stmt.ToolAlias}' is restricted to dedicated trusted host execution and cannot run inside multi-tenant contexts.",
                null, stmt.Line, stmt.Column);
        }

        var profile = GetOptionString(toolDef.Options, "ISOLATION_PROFILE")
            ?? _config?["Tools:Limits:RequiredIsolationProfile"];
        if (!string.IsNullOrWhiteSpace(profile)
            && !profile.Equals("TrustedNative", StringComparison.OrdinalIgnoreCase)
            && !profile.Equals("DedicatedTrusted", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExecutionException(
                $"Native EXECUTABLE tool '{stmt.ToolAlias}' cannot satisfy claimed isolation profile '{profile}'.",
                null, stmt.Line, stmt.Column);
        }

        if (toolDef.Options.ContainsKey("MEMORY_MB")
            || toolDef.Options.ContainsKey("CPUS")
            || toolDef.Options.ContainsKey("PIDS_LIMIT")
            || toolDef.Options.ContainsKey("ISOLATE_NETWORK"))
        {
            throw new ExecutionException(
                $"Native EXECUTABLE tool '{stmt.ToolAlias}' declares container resource or network isolation options that native OS execution cannot enforce.",
                null, stmt.Line, stmt.Column);
        }
    }

    private static ToolCatalogOwnershipMode ResolveOwnershipMode(IToolCatalogProvider? catalogProvider, IConfiguration? configuration)
    {
        if (catalogProvider is LocalToolCatalogProvider local)
            return local.OwnershipMode;

        if (Enum.TryParse<ToolCatalogOwnershipMode>(configuration?["Governance:Tools:OwnershipMode"], ignoreCase: true, out var configured))
            return configured;

        return ToolCatalogOwnershipMode.PersonalWorkstation;
    }

    private static Dictionary<string, string> ParseParamRoles(string? raw)
    {
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return roles;

        foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && !string.IsNullOrWhiteSpace(kv[0]) && !string.IsNullOrWhiteSpace(kv[1]))
                roles[kv[0]] = kv[1];
        }

        return roles;
    }

    private static void ValidateParameterValue(
        string paramName,
        string value,
        int maxLength,
        IReadOnlyDictionary<string, string> roles,
        bool allowCallerCodeArgs)
    {
        if (value.Length > maxLength)
        {
            throw new ExecutionException(
                $"Parameter '{paramName}' exceeds maximum allowed length of {maxLength} characters.");
        }

        if (value.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ExecutionException(
                $"Parameter '{paramName}' contains prohibited control characters.");
        }

        var role = roles.TryGetValue(paramName, out var declared) ? declared.Trim() : "DATA";
        if (role.Equals("CODE", StringComparison.OrdinalIgnoreCase)
            || role.Equals("SCRIPT", StringComparison.OrdinalIgnoreCase))
        {
            if (!allowCallerCodeArgs)
            {
                throw new ExecutionException(
                    $"Parameter '{paramName}' has role '{role}', which requires explicit ALLOW_CALLER_CODE_ARGS authorization.");
            }
            return;
        }

        if (role.Equals("NUMBER", StringComparison.OrdinalIgnoreCase))
        {
            if (!SignedNumberRegex().IsMatch(value.Trim()))
                throw new ExecutionException($"Parameter '{paramName}' must be a valid number.");
            return;
        }

        if (role.StartsWith("ENUM(", StringComparison.OrdinalIgnoreCase) && role.EndsWith(')'))
        {
            var inner = role[5..^1];
            var allowed = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowed.Any(item => string.Equals(item, value, StringComparison.Ordinal)))
            {
                throw new ExecutionException(
                    $"Parameter '{paramName}' value '{value}' is not in the approved enum set [{inner}].");
            }
            return;
        }

        if (role.Equals("PATH", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Contains("..", StringComparison.Ordinal))
                throw new ExecutionException($"Path parameter '{paramName}' cannot contain relative parent traversal ('..').");
            if (IsOptionShapedValue(value))
                throw new ExecutionException($"Parameter '{paramName}' cannot supply an option flag ('{value}').");
            return;
        }

        // Default DATA role: preserve legitimate negative numbers and paths, while blocking option flags.
        if (IsOptionShapedValue(value))
        {
            throw new ExecutionException(
                $"Parameter '{paramName}' cannot supply an option switch or flag ('{value}').");
        }
    }

    private static bool IsConstrainedScalarRole(IReadOnlyDictionary<string, string> roles, string paramName)
    {
        if (!roles.TryGetValue(paramName, out var role))
            return false;

        return role.Equals("NUMBER", StringComparison.OrdinalIgnoreCase)
            || (role.StartsWith("ENUM(", StringComparison.OrdinalIgnoreCase) && role.EndsWith(')'));
    }

    private static bool IsOptionShapedValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return false;

        if (trimmed.StartsWith("--", StringComparison.Ordinal))
            return true;

        if (trimmed[0] == '-')
            return !SignedNumberRegex().IsMatch(trimmed);

        if (trimmed[0] == '/' && DosSwitchRegex().IsMatch(trimmed))
            return true;

        return false;
    }

    private static bool IsInterpreterCommand(string command)
    {
        var exe = Path.GetFileNameWithoutExtension(command.Trim());
        return exe.Equals("python", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("python3", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("python2", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("py", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("node", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("nodejs", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("bash", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("sh", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("dash", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("zsh", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("ash", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("ksh", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("ruby", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("perl", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("php", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("lua", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("awk", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("gawk", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("env", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("xargs", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("cscript", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("wscript", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInterpreterExecSwitch(string token) =>
        token.Equals("-c", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-e", StringComparison.OrdinalIgnoreCase)
        || token.Equals("--eval", StringComparison.OrdinalIgnoreCase)
        || token.Equals("--exec", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-exec", StringComparison.OrdinalIgnoreCase)
        || token.Equals("/c", StringComparison.OrdinalIgnoreCase)
        || token.Equals("/k", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-command", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-encodedcommand", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-enc", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-m", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-r", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-f", StringComparison.OrdinalIgnoreCase)
        || token.Equals("--file", StringComparison.OrdinalIgnoreCase);

    private static bool IsFlagTakingValue(string token) =>
        (token.StartsWith('-') || token.StartsWith('/')) && !token.Contains('=');

    [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^-?(?:\d+(?:\.\d+)?|\.\d+)$")]
    private static partial Regex SignedNumberRegex();

    [GeneratedRegex(@"^/[A-Za-z]{1,2}$")]
    private static partial Regex DosSwitchRegex();

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
