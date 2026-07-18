using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// IScriptExecutor implementation that executes ETL-SQL scripts by spawning
    /// <c>ETL-SQL.exe run &lt;script-file&gt; --json</c> as a child process.
    ///
    /// Benefits over in-process execution:
    ///   - Memory isolation — a runaway job cannot corrupt the Orchestrator's heap
    ///   - Kill granularity — individual jobs can be cancelled via Process.Kill()
    ///   - Config isolation — each job inherits a fresh environment
    ///   - Supports running jobs compiled against a different engine version
    ///
    /// The process exit code signals success (0) or failure (non-zero).
    /// With <c>--json</c> the last line of stdout is a JSON result envelope.
    /// </summary>
    public class ProcessJobExecutor : IScriptExecutor
    {
        private readonly ProcessJobExecutorOptions _options;
        private readonly ChildProcessTracker _tracker;
        private readonly ILogger<ProcessJobExecutor> _logger;
        private static readonly object CleanupLock = new();
        private static DateTime _lastTempScriptCleanupUtc = DateTime.MinValue;
        private static readonly ConcurrentDictionary<string, WarmRunnerPool> WarmRunnerPools = new(StringComparer.OrdinalIgnoreCase);

        public ProcessJobExecutor(
            IOptions<ProcessJobExecutorOptions> options,
            ChildProcessTracker tracker,
            ILogger<ProcessJobExecutor> logger)
        {
            _options = options.Value;
            _tracker = tracker;
            _logger = logger;
            CleanupOldTempScripts(force: true);
        }

        internal static void ClearWarmRunnerPoolsForTests()
        {
            foreach (var pool in WarmRunnerPools.Values)
                pool.Dispose();

            WarmRunnerPools.Clear();
        }

        public async Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, string? sessionId = null, CancellationToken cancellationToken = default, string? jobName = null, long queueWaitMs = 0, ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null)
        {
            // Out-of-process execution does not carry a row-level-security identity across the process
            // boundary; identity-sensitive scripts therefore fail closed in this path. Subscription
            // per-recipient delivery uses the in-process ScriptExecutorAdapter. See Docs/Design/RowLevelSecurity.md.
            CleanupOldTempScripts(force: false);

            // Write script to a temp file — ETL-SQL.exe run expects a file path
            var tempFile = Path.Combine(Path.GetTempPath(), $"etlsql-job-{Guid.NewGuid():N}.etlsql");
            try
            {
                await File.WriteAllTextAsync(tempFile, scriptText, Encoding.UTF8, cancellationToken);
                if (_options.UseWarmRunner)
                {
                    try
                    {
                        return await RunWarmProcessAsync(tempFile, sessionId, cancellationToken, jobName, queueWaitMs);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Warm job runner failed; falling back to one-shot process execution.");
                    }
                }

                return await RunProcessAsync(tempFile, sessionId, cancellationToken, queueWaitMs);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best effort */ }
            }
        }

        private void CleanupOldTempScripts(bool force)
        {
            var now = DateTime.UtcNow;
            lock (CleanupLock)
            {
                if (!force && now - _lastTempScriptCleanupUtc < TimeSpan.FromHours(1))
                    return;

                _lastTempScriptCleanupUtc = now;
            }

            try
            {
                var cutoff = now - TimeSpan.FromHours(24);
                foreach (var path in Directory.EnumerateFiles(Path.GetTempPath(), "etlsql-job-*.etlsql", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var lastWrite = File.GetLastWriteTimeUtc(path);
                        if (lastWrite < cutoff)
                            File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Unable to delete stale Orchestrator job temp script {Path}", path);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to enumerate stale Orchestrator job temp scripts.");
            }
        }

        private async Task<ScriptExecutionResult> RunProcessAsync(string scriptFile, string? sessionId, CancellationToken ct, long queueWaitMs = 0)
        {
            var exePath = ResolveExecutablePath();
            var argList = BuildArguments(scriptFile, sessionId);

            _logger.LogInformation("Spawning job process: {Exe} {Args}", exePath, ETL_SQL.Core.Common.LogSanitizer.Clean(string.Join(' ', argList)));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (_options.TimeoutSeconds > 0)
                cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            // Pass arguments through ArgumentList (not a single concatenated string) so the
            // runtime quotes/escapes each token. This prevents argument injection from a
            // hostile sessionId (e.g. one containing a quote) smuggling extra CLI flags into
            // the child ETL-SQL process.
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Directory.GetCurrentDirectory()
            };
            if (queueWaitMs > 0)
            {
                psi.EnvironmentVariables["ETLSQL_QUEUE_WAIT_MS"] = queueWaitMs.ToString();
            }
            foreach (var arg in argList)
                psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _tracker.Register(process.Id, scriptFile);
            _logger.LogDebug("Job process PID={Pid} started for {Script}", process.Id, scriptFile);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Job process PID={Pid} timed out or was cancelled — killing.", process.Id);
                try { process.CancelOutputRead(); } catch { }
                try { process.CancelErrorRead(); } catch { }
                try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
                try { process.WaitForExit(2000); } catch { }
                return new ScriptExecutionResult(false, 0, "Job execution was cancelled or timed out.");
            }
            finally
            {
                _tracker.Unregister(process.Id);
            }

            long peakMemory = 0;
            double cpuSeconds = 0;
            try
            {
                // Capture metrics before disposing
                peakMemory = process.PeakWorkingSet64;
                cpuSeconds = process.TotalProcessorTime.TotalSeconds;
            }
            catch { /* Process object might be in a state where these are unavailable */ }

            var exitCode = process.ExitCode;
            _logger.LogInformation("Job process PID={Pid} exited with code {ExitCode}. CPU: {Cpu}s, Peak RAM: {Mem} bytes",
                process.Id, exitCode, cpuSeconds, peakMemory);

            // Many CLIs write progress info to stderr even on success; only escalate to Error
            // when the process also exited non-zero, otherwise log at Info to avoid alert noise.
            if (stderr.Length > 0)
            {
                if (exitCode != 0)
                    _logger.LogError("Job process PID={Pid} stderr (exit {ExitCode}): {Stderr}", process.Id, exitCode, stderr.ToString().Trim());
                else
                    _logger.LogInformation("Job process PID={Pid} stderr (exit 0): {Stderr}", process.Id, stderr.ToString().Trim());
            }

            return ParseResult(exitCode, stdout.ToString(), stderr.ToString(), peakMemory, cpuSeconds);
        }

        private async Task<ScriptExecutionResult> RunWarmProcessAsync(
            string scriptFile,
            string? sessionId,
            CancellationToken ct,
            string? jobName,
            long queueWaitMs)
        {
            var exePath = ResolveExecutablePath();
            var key = $"{Path.GetFullPath(exePath)}|{Math.Max(1, _options.WarmRunnerPoolSize)}|{Math.Max(1, _options.WarmRunnerStartupTimeoutSeconds)}";
            var pool = WarmRunnerPools.GetOrAdd(key, _ => new WarmRunnerPool(
                exePath,
                Math.Max(1, _options.WarmRunnerPoolSize),
                TimeSpan.FromSeconds(Math.Max(1, _options.WarmRunnerStartupTimeoutSeconds)),
                _tracker,
                _logger));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (_options.TimeoutSeconds > 0)
                cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            try
            {
                return await pool.ExecuteAsync(
                    new WarmRunnerRequest(
                        Guid.NewGuid().ToString("N"),
                        scriptFile,
                        sessionId,
                        jobName,
                        queueWaitMs,
                        _options.WarmRunnerBatchSize),
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                return new ScriptExecutionResult(false, 0, "Job execution was cancelled or timed out.");
            }
        }

        private ScriptExecutionResult ParseResult(int exitCode, string stdout, string stderr, long peakMemory, double cpuSeconds)
        {
            // ETL-SQL.exe with --json writes a JSON envelope as the LAST non-empty line
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (!line.StartsWith("{")) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    bool success;
                    if (root.TryGetProperty("success", out var s))
                    {
                        success = s.GetBoolean();
                    }
                    else if (root.TryGetProperty("type", out var type) &&
                             string.Equals(type.GetString(), "done", StringComparison.OrdinalIgnoreCase) &&
                             root.TryGetProperty("exitCode", out var doneExitCode))
                    {
                        success = doneExitCode.GetInt32() == 0;
                    }
                    else
                    {
                        success = exitCode == 0;
                    }

                    long rows = root.TryGetProperty("rowsProcessed", out var r) ? r.GetInt64() : 0;
                    string? error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                    string? session = root.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;

                    return new ScriptExecutionResult(success, rows, error, peakMemory, cpuSeconds, session);
                }
                catch (JsonException)
                {
                    // Line looked like JSON but wasn't valid — keep searching upward
                }
            }

            // No parseable JSON envelope — fall back to exit code
            if (exitCode == 0)
                return new ScriptExecutionResult(true, 0, null, peakMemory, cpuSeconds);

            var stdoutText = LogSanitizer.Clean(stdout.Trim());
            var stderrText = LogSanitizer.Clean(stderr.Trim());
            var message = new StringBuilder($"Process exited with code {exitCode}.");
            if (!string.IsNullOrWhiteSpace(stdoutText))
                message.Append(" Stdout: ").Append(stdoutText);
            if (!string.IsNullOrWhiteSpace(stderrText))
                message.Append(" Stderr: ").Append(stderrText);

            return new ScriptExecutionResult(false, 0, message.ToString(), peakMemory, cpuSeconds);
        }

        private string ResolveExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(_options.ExecutablePath) && File.Exists(_options.ExecutablePath))
                return _options.ExecutablePath;

            // Auto-discover: look for ETL-SQL.exe / ETL-SQL next to the current assembly
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var name in new[] { "ETL-SQL.exe", "ETL-SQL", "etl-sql.exe", "etl-sql" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }

            throw new InvalidOperationException(
                $"Could not locate ETL-SQL executable. Set 'Jobs:ExecutablePath' in appsettings.json. " +
                $"Searched in: {dir}");
        }

        private List<string> BuildArguments(string scriptFile, string? sessionId)
        {
            if (!string.IsNullOrWhiteSpace(_options.ArgumentsTemplate))
            {
                // Each whitespace-separated template token becomes one argument; placeholders
                // are substituted as whole values so a tokenised {SessionId} can never expand
                // into multiple arguments. Operators should not add their own quoting —
                // ArgumentList handles escaping.
                var result = new List<string>();
                foreach (var token in _options.ArgumentsTemplate.Split(
                             (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    result.Add(token
                        .Replace("{ScriptFile}", scriptFile, StringComparison.Ordinal)
                        .Replace("{SessionId}", sessionId ?? string.Empty, StringComparison.Ordinal));
                }
                return result;
            }

            var args = new List<string> { "run", scriptFile, "--json" };
            if (!string.IsNullOrEmpty(sessionId))
            {
                args.Add("--session");
                args.Add(sessionId);
            }
            return args;
        }
    }

    public class ProcessJobExecutorOptions
    {
        /// <summary>Full path to ETL-SQL.exe. If empty, auto-discovery is attempted.</summary>
        public string? ExecutablePath { get; set; }
        /// <summary>Optional process argument template. Supports {ScriptFile} and {SessionId}; defaults to run "{ScriptFile}" --json.</summary>
        public string? ArgumentsTemplate { get; set; }
        /// <summary>Maximum seconds a job process may run before it is killed. 0 = unlimited.</summary>
        public int TimeoutSeconds { get; set; } = 3600;
        /// <summary>When true, SchedulerService uses ProcessJobExecutor instead of ScriptExecutorAdapter.</summary>
        public bool UseProcessSpawning { get; set; } = false;
        /// <summary>When true, ProcessJobExecutor reuses warm ETL-SQL runner processes instead of launching a fresh process per job.</summary>
        public bool UseWarmRunner { get; set; } = false;
        /// <summary>Maximum warm runner processes kept for concurrent job execution.</summary>
        public int WarmRunnerPoolSize { get; set; } = 2;
        /// <summary>Seconds to wait for a newly spawned warm runner to publish its ready handshake.</summary>
        public int WarmRunnerStartupTimeoutSeconds { get; set; } = 10;
        /// <summary>Batch size passed to warm runner execution sessions. Values less than 1 use the engine default.</summary>
        public int WarmRunnerBatchSize { get; set; } = 10000;
    }

    internal sealed class WarmRunnerPool : IDisposable
    {
        private readonly string _exePath;
        private readonly TimeSpan _startupTimeout;
        private readonly ChildProcessTracker _tracker;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _slots;
        private readonly ConcurrentBag<WarmRunnerClient> _idle = new();
        private readonly ConcurrentDictionary<WarmRunnerClient, byte> _all = new();

        public WarmRunnerPool(
            string exePath,
            int poolSize,
            TimeSpan startupTimeout,
            ChildProcessTracker tracker,
            ILogger logger)
        {
            _exePath = exePath;
            _startupTimeout = startupTimeout;
            _tracker = tracker;
            _logger = logger;
            _slots = new SemaphoreSlim(Math.Max(1, poolSize));
        }

        public async Task<ScriptExecutionResult> ExecuteAsync(WarmRunnerRequest request, CancellationToken ct)
        {
            await _slots.WaitAsync(ct);
            WarmRunnerClient? client = null;
            try
            {
                client = await RentAsync(ct);
                _tracker.Register(client.ProcessId, request.ScriptFile);
                try
                {
                    return await client.ExecuteAsync(request, ct);
                }
                finally
                {
                    _tracker.Unregister(client.ProcessId);
                }
            }
            catch (OperationCanceledException)
            {
                if (client != null)
                {
                    client.Kill();
                    if (_all.TryRemove(client, out _))
                    {
                        client.Dispose();
                    }
                }
                throw;
            }
            catch
            {
                if (client != null)
                {
                    client.Kill();
                    if (_all.TryRemove(client, out _))
                    {
                        client.Dispose();
                    }
                }
                throw;
            }
            finally
            {
                if (client is { IsUsable: true })
                    _idle.Add(client);
                _slots.Release();
            }
        }

        private async Task<WarmRunnerClient> RentAsync(CancellationToken ct)
        {
            while (_idle.TryTake(out var client))
            {
                if (client.IsUsable)
                    return client;

                if (_all.TryRemove(client, out _))
                {
                    client.Dispose();
                }
            }

            var started = await WarmRunnerClient.StartAsync(_exePath, _startupTimeout, _logger, ct);
            _all[started] = 0;
            return started;
        }

        public void Dispose()
        {
            foreach (var client in _all.Keys)
            {
                client.Kill();
                client.Dispose();
            }
            _all.Clear();

            _slots.Dispose();
        }
    }

    internal sealed class WarmRunnerClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly Process _process;
        private readonly ILogger _logger;
        private readonly StringBuilder _stderr = new();
        private bool _killed;

        private WarmRunnerClient(Process process, ILogger logger, StringBuilder? stderr = null)
        {
            _process = process;
            _logger = logger;
            if (stderr != null)
                _stderr.Append(stderr.ToString());
        }

        public int ProcessId => _process.Id;
        public bool IsUsable => !_killed && !_process.HasExited;

        public static async Task<WarmRunnerClient> StartAsync(string exePath, TimeSpan startupTimeout, ILogger logger, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Directory.GetCurrentDirectory()
            };
            psi.ArgumentList.Add("runner");

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                var clean = LogSanitizer.Clean(e.Data);
                stderr.AppendLine(clean);
                logger.LogDebug("Warm runner PID={Pid} stderr: {Line}", SafeProcessId(process), clean);
            };

            process.Start();
            process.BeginErrorReadLine();

            using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            startupCts.CancelAfter(startupTimeout);
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(startupCts.Token);
                if (line == null)
                {
                    var detail = stderr.Length == 0
                        ? string.Empty
                        : $" Stderr: {stderr.ToString().Trim()}";
                    throw new InvalidOperationException($"Warm runner exited before ready handshake.{detail}");
                }

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var type) &&
                    string.Equals(type.GetString(), "ready", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Warm job runner PID={Pid} is ready.", process.Id);
                    return new WarmRunnerClient(process, logger, stderr);
                }
            }
        }

        public async Task<ScriptExecutionResult> ExecuteAsync(WarmRunnerRequest request, CancellationToken ct)
        {
            if (!IsUsable)
                throw new InvalidOperationException("Warm runner is not running.");

            var payload = JsonSerializer.Serialize(request, JsonOptions);
            await _process.StandardInput.WriteLineAsync(payload.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);

            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct);
                if (line == null)
                {
                    var detail = _stderr.Length == 0
                        ? string.Empty
                        : $" Stderr: {_stderr.ToString().Trim()}";
                    throw new InvalidOperationException($"Warm runner exited before returning a result.{detail}");
                }

                WarmRunnerResponse? response;
                try
                {
                    response = JsonSerializer.Deserialize<WarmRunnerResponse>(line, JsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException("Warm runner returned invalid JSON.", ex);
                }

                if (response == null ||
                    !string.Equals(response.Type, "result", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(response.Id, request.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return new ScriptExecutionResult(
                    response.Success,
                    response.RowsProcessed,
                    response.ErrorMessage,
                    response.PeakMemoryBytes,
                    response.CpuTimeSeconds,
                    response.SessionId);
            }
        }

        public void Kill()
        {
            _killed = true;
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch { }
        }

        public void Dispose()
        {
            try { _process.Dispose(); } catch { }
        }

        private static int SafeProcessId(Process process)
        {
            try { return process.Id; } catch { return -1; }
        }
    }

    internal sealed record WarmRunnerRequest(
        string Id,
        string ScriptFile,
        string? SessionId,
        string? JobName,
        long QueueWaitMs,
        int BatchSize);

    internal sealed record WarmRunnerResponse(
        string Type,
        string Id,
        bool Success,
        long RowsProcessed,
        string? ErrorMessage,
        long PeakMemoryBytes,
        double CpuTimeSeconds,
        string? SessionId);
}
