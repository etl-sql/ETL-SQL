using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ETL_SQL.Core;

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
            _logger  = logger;
        }

        public async Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, CancellationToken cancellationToken = default)
        {
            // Write script to a temp file — ETL-SQL.exe run expects a file path
            var tempFile = Path.Combine(Path.GetTempPath(), $"etlsql-job-{Guid.NewGuid():N}.etlsql");
            try
            {
                await File.WriteAllTextAsync(tempFile, scriptText, Encoding.UTF8, cancellationToken);
                return await RunProcessAsync(tempFile, cancellationToken);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best effort */ }
            }
        }

        private async Task<ScriptExecutionResult> RunProcessAsync(string scriptFile, CancellationToken ct)
        {
            var exePath = ResolveExecutablePath();
            var args    = $"run \"{scriptFile}\" --json";

            _logger.LogInformation("Spawning job process: {Exe} {Args}", exePath, args);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (_options.TimeoutSeconds > 0)
                cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var psi = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                WorkingDirectory       = Path.GetDirectoryName(exePath) ?? Directory.GetCurrentDirectory()
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

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
                try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
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

            if (stderr.Length > 0)
                _logger.LogWarning("Job process stderr: {Stderr}", stderr.ToString().Trim());

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

                    bool   success  = root.TryGetProperty("success",       out var s) && s.GetBoolean();
                    long   rows     = root.TryGetProperty("rowsProcessed", out var r) ? r.GetInt64() : 0;
                    string? error   = root.TryGetProperty("error",         out var e) ? e.GetString() : null;

                    return new ScriptExecutionResult(success, rows, error, peakMemory, cpuSeconds);
                }
                catch (JsonException)
                {
                    // Line looked like JSON but wasn't valid — keep searching upward
                }
            }

            // No parseable JSON envelope — fall back to exit code
            return exitCode == 0
                ? new ScriptExecutionResult(true,  0, null, peakMemory, cpuSeconds)
                : new ScriptExecutionResult(false, 0, $"Process exited with code {exitCode}. Stdout: {stdout.Trim()}", peakMemory, cpuSeconds);
        }

        private string ResolveExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(_options.ExecutablePath) && File.Exists(_options.ExecutablePath))
                return _options.ExecutablePath;

            // Auto-discover: look for ETL-SQL.exe / ETL-SQL next to the current assembly
            var dir  = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var name in new[] { "ETL-SQL.exe", "ETL-SQL", "etl-sql.exe", "etl-sql" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }

            throw new InvalidOperationException(
                $"Could not locate ETL-SQL executable. Set 'Jobs:ExecutablePath' in appsettings.json. " +
                $"Searched in: {dir}");
        }
    }

    public class ProcessJobExecutorOptions
    {
        /// <summary>Full path to ETL-SQL.exe. If empty, auto-discovery is attempted.</summary>
        public string? ExecutablePath { get; set; }
        /// <summary>Maximum seconds a job process may run before it is killed. 0 = unlimited.</summary>
        public int TimeoutSeconds     { get; set; } = 3600;
        /// <summary>When true, SchedulerService uses ProcessJobExecutor instead of ScriptExecutorAdapter.</summary>
        public bool UseProcessSpawning { get; set; } = false;
    }
}
