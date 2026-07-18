using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public class ProcessJobExecutorChaosTests
{
    [Fact]
    public async Task WarmRunner_ExecutesMultipleJobs_AndClearsActiveProcessTracking()
    {
        var tempDir = NewTempDir();
        var pidStore = Path.Combine(tempDir, "child-pids.json");
        var tracker = new ChildProcessTracker(new Mock<ILogger<ChildProcessTracker>>().Object, pidStore);
        var exePath = FindAppHost();

        Assert.True(File.Exists(exePath), $"Expected ETL-SQL apphost at {exePath}");

        var executor = new ProcessJobExecutor(
            Options.Create(new ProcessJobExecutorOptions
            {
                ExecutablePath = exePath,
                TimeoutSeconds = 30,
                UseWarmRunner = true,
                WarmRunnerPoolSize = 1,
                WarmRunnerStartupTimeoutSeconds = 20
            }),
            tracker,
            new Mock<ILogger<ProcessJobExecutor>>().Object);

        try
        {
            var first = await executor.ExecuteTextAsync("SELECT 1 AS Val;", sessionId: "warm-test-1", jobName: "WarmRunnerOne");
            var second = await executor.ExecuteTextAsync("SELECT 2 AS Val;", sessionId: "warm-test-2", jobName: "WarmRunnerTwo");

            Assert.True(first.Success, first.ErrorMessage);
            Assert.True(second.Success, second.ErrorMessage);
            Assert.Equal("warm-test-1", first.SessionId);
            Assert.Equal("warm-test-2", second.SessionId);
            Assert.Equal(0, tracker.ActiveCount);
        }
        finally
        {
            ProcessJobExecutor.ClearWarmRunnerPoolsForTests();
            TryDelete(tempDir);
        }
    }

    [Fact]
    public async Task Timeout_KillsChildProcess_AndClearsActiveProcessTracking()
    {
        var tempDir = NewTempDir();
        var pidStore = Path.Combine(tempDir, "child-pids.json");
        var tracker = new ChildProcessTracker(new Mock<ILogger<ChildProcessTracker>>().Object, pidStore);
        var (exePath, argumentsTemplate) = SleepCommand(seconds: 10);

        var executor = new ProcessJobExecutor(
            Options.Create(new ProcessJobExecutorOptions
            {
                ExecutablePath = exePath,
                ArgumentsTemplate = argumentsTemplate,
                TimeoutSeconds = 1
            }),
            tracker,
            new Mock<ILogger<ProcessJobExecutor>>().Object);

        try
        {
            var started = DateTime.UtcNow;
            var result = await executor.ExecuteTextAsync("WAITFOR DELAY '00:00:10';", jobName: "TimeoutChaosJob");

            Assert.False(result.Success);
            Assert.Contains("cancelled or timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True((DateTime.UtcNow - started).TotalSeconds < 8);
            Assert.Equal(0, tracker.ActiveCount);
            Assert.False(File.Exists(pidStore) && File.ReadAllText(pidStore).Contains("etlsql-job-", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    [Fact]
    public async Task CallerCancellation_KillsChildProcess_AndClearsActiveProcessTracking()
    {
        var tempDir = NewTempDir();
        var pidStore = Path.Combine(tempDir, "child-pids.json");
        var tracker = new ChildProcessTracker(new Mock<ILogger<ChildProcessTracker>>().Object, pidStore);
        var (exePath, argumentsTemplate) = SleepCommand(seconds: 10);

        var executor = new ProcessJobExecutor(
            Options.Create(new ProcessJobExecutorOptions
            {
                ExecutablePath = exePath,
                ArgumentsTemplate = argumentsTemplate,
                TimeoutSeconds = 0
            }),
            tracker,
            new Mock<ILogger<ProcessJobExecutor>>().Object);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var started = DateTime.UtcNow;
            var result = await executor.ExecuteTextAsync(
                "WAITFOR DELAY '00:00:10';",
                cancellationToken: cts.Token,
                jobName: "CallerCancelledChaosJob");

            Assert.False(result.Success);
            Assert.Contains("cancelled or timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True((DateTime.UtcNow - started).TotalSeconds < 8);
            Assert.Equal(0, tracker.ActiveCount);
            Assert.False(File.Exists(pidStore) && File.ReadAllText(pidStore).Contains("etlsql-job-", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    [Fact]
    public async Task CleanupOrphans_KillsPersistedChildProcess_FromPreviousRun()
    {
        var tempDir = NewTempDir();
        var pidStore = Path.Combine(tempDir, "child-pids.json");
        var (exePath, arguments) = SleepCommand(seconds: 30);
        using var child = StartProcess(exePath, arguments);

        try
        {
            var firstTracker = new ChildProcessTracker(new Mock<ILogger<ChildProcessTracker>>().Object, pidStore);
            firstTracker.Register(child.Id, "orphaned-chaos-script.etlsql");

            Assert.Equal(1, firstTracker.ActiveCount);
            Assert.True(File.Exists(pidStore));

            var restartTracker = new ChildProcessTracker(new Mock<ILogger<ChildProcessTracker>>().Object, pidStore);
            restartTracker.CleanupOrphans();

            await WaitForExitAsync(child, timeoutSeconds: 10);
            Assert.True(child.HasExited);
            Assert.False(File.Exists(pidStore));
        }
        finally
        {
            if (!child.HasExited)
            {
                try { child.Kill(entireProcessTree: true); } catch { }
            }
            TryDelete(tempDir);
        }
    }

    private static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etlsql-process-chaos-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static Process StartProcess(string exePath, string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(exePath, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        return process;
    }

    private static (string ExePath, string Arguments) SleepCommand(int seconds)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var exe = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            return (exe, $"/c ping -n {seconds + 1} 127.0.0.1 > nul");
        }

        return ("/bin/sleep", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string FindAppHost()
    {
        foreach (var dir in CandidateAppHostDirectories())
        {
            foreach (var name in new[] { "ETL-SQL.exe", "ETL-SQL" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ETL-SQL.exe" : "ETL-SQL");
    }

    private static IEnumerable<string> CandidateAppHostDirectories()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var appOutput = Path.Combine(
                current.FullName,
                "src",
                "ETL-SQL.App",
                "bin",
                "Debug",
                "net10.0");
            if (Directory.Exists(appOutput))
                yield return appOutput;

            current = current.Parent;
        }

        yield return AppContext.BaseDirectory;
    }

    private static async Task WaitForExitAsync(Process process, int timeoutSeconds)
    {
        var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        var exited = await Task.WhenAny(process.WaitForExitAsync(), timeout);
        Assert.NotSame(timeout, exited);
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
