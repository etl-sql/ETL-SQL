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
        // The child sleeps far longer than the timeout so the duration assertion below has a wide
        // discriminating gap. At a 10s child with an <8s bound there were only ~2s of headroom, and
        // under full-suite CPU contention process spawn, kill and teardown exceeded it — the test
        // failed while the timeout logic was working correctly.
        var (exePath, argumentsTemplate) = SleepCommand(seconds: 120);

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
            // Proves the run ended on its own timeout/cancellation rather than the child finishing.
            // Anything well under the 120s child is conclusive, so the bound is generous enough to
            // survive a CPU-starved full-suite run without weakening what it asserts.
            Assert.True((DateTime.UtcNow - started).TotalSeconds < 60, // flaky-time-bound-ok: 60s is far below the 120s child sleep being ruled out
                "The run should have ended on timeout/cancellation, not by the child completing.");
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
        // See the note in the timeout test above: a child that outlives the cancellation by a wide
        // margin is what makes the duration assertion meaningful under load.
        var (exePath, argumentsTemplate) = SleepCommand(seconds: 120);

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
            // Proves the run ended on its own timeout/cancellation rather than the child finishing.
            // Anything well under the 120s child is conclusive, so the bound is generous enough to
            // survive a CPU-starved full-suite run without weakening what it asserts.
            Assert.True((DateTime.UtcNow - started).TotalSeconds < 60, // flaky-time-bound-ok: 60s is far below the 120s child sleep being ruled out
                "The run should have ended on timeout/cancellation, not by the child completing.");
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
        // Only accept a host whose directory also carries the App's runtime dependencies.
        // Since the App declares <RuntimeIdentifiers>, a `dotnet test --runtime <rid>` build
        // (used by the enterprise-hardening lane) emits the full App under net10.0/<rid>/, and
        // the apphost copied next to the test assembly is a framework-dependent stub missing
        // transitive deps (e.g. Microsoft.Extensions.DependencyInjection.Abstractions). Spawning
        // that stub crashes the child at startup, so skip incomplete deployments.
        foreach (var dir in CandidateAppHostDirectories())
        {
            foreach (var name in new[] { "ETL-SQL.exe", "ETL-SQL" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate) && HasRuntimeDependencies(dir))
                    return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ETL-SQL.exe" : "ETL-SQL");
    }

    // A spawnable apphost must have its transitive framework deps sitting beside it.
    private static bool HasRuntimeDependencies(string dir) =>
        File.Exists(Path.Combine(dir, "Microsoft.Extensions.DependencyInjection.Abstractions.dll"));

    private static IEnumerable<string> CandidateAppHostDirectories()
    {
        // The App builds with the same config/rid/artifacts tail as this test project, so derive the
        // App's output directory by swapping the project name in this assembly's path. One transform
        // covers Debug/Release, RID subdirectories, and `dotnet test --artifacts-path`:
        //   artifacts:    <root>/bin/ETL-SQL.Tests/<cfg>/     -> <root>/bin/ETL-SQL.App/<cfg>/
        //   conventional: <root>/tests/ETL-SQL.Tests/bin/<Cfg>/net10.0[/<rid>]/
        //                 -> <root>/src/ETL-SQL.App/bin/<Cfg>/net10.0[/<rid>]/
        // (The apphost copied beside the test assembly is a stub whose transitive deps land under
        // refs/, not beside the exe, so spawning it crashes the child — HasRuntimeDependencies and
        // these App-output candidates steer FindAppHost to a complete deployment instead.)
        var testDir = AppContext.BaseDirectory.Replace('\\', '/').TrimEnd('/');
        var appDir = testDir.Replace("ETL-SQL.Tests", "ETL-SQL.App");
        yield return appDir.Replace("/tests/", "/src/");
        yield return appDir;

        // Fallback: walk up looking for the conventional App output under either configuration.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var appOutput = Path.Combine(
                    current.FullName, "src", "ETL-SQL.App", "bin", cfg, "net10.0");
                if (Directory.Exists(appOutput))
                {
                    foreach (var ridDir in Directory.GetDirectories(appOutput))
                        yield return ridDir;
                    yield return appOutput;
                }
            }

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
