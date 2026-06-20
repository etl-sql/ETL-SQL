using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
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

        public ProcessJobExecutor(
            IOptions<ProcessJobExecutorOptions> options,
            ChildProcessTracker tracker,
            ILogger<ProcessJobExecutor> logger)
        {
            _options = options.Value;
            _tracker = tracker;
            _logger = logger;
        }

        public async Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, string? sessionId = null, CancellationToken cancellationToken = default, string? jobName = null)
        {
            // Write script to a temp file — ETL-SQL.exe run expects a file path
            var tempFile = Path.Combine(Path.GetTempPath(), $"etlsql-job-{Guid.NewGuid():N}.etlsql");
            try
            {
                await File.WriteAllTextAsync(tempFile, scriptText, Encoding.UTF8, cancellationToken);
                return await RunProcessAsync(tempFile, sessionId, cancellationToken);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best effort */ }
            }
        }

        private async Task<ScriptExecutionResult> RunProcessAsync(string scriptFile, string? sessionId, CancellationToken ct)
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

            return ParseResult(exitCode, stdout.ToString(), peakMemory, cpuSeconds);
        }

        private ScriptExecutionResult ParseResult(int exitCode, string stdout, long peakMemory, double cpuSeconds)
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
            return exitCode == 0
                ? new ScriptExecutionResult(true, 0, null, peakMemory, cpuSeconds)
                : new ScriptExecutionResult(false, 0, $"Process exited with code {exitCode}. Stdout: {stdout.Trim()}", peakMemory, cpuSeconds);
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
    }
}
