using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.ReportPortal.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public class ExecutionJobServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"exec_job_svc_{Guid.NewGuid():N}");

    public ExecutionJobServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "scripts"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "snapshots"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "datasets"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Regression for the queued-timeout leak: a job whose timeout fires while it is still
    /// waiting for the concurrency gate must reach a terminal state, clear its refresh
    /// debounce entry, and leave the gate usable — not stay Pending and block the report forever.
    /// </summary>
    [Fact]
    public async Task RefreshTimedOutWhileQueued_ReachesTerminalStateAndFreesGateAndDebounce()
    {
        var scriptPath = Path.Combine(_tempDir, "scripts", "report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'never executed';");

        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(_tempDir, "portal.db"),
            ScriptRootPath = Path.Combine(_tempDir, "scripts"),
            SnapshotDirectory = Path.Combine(_tempDir, "snapshots"),
            DatasetRootPath = Path.Combine(_tempDir, "datasets"),
            Resources = new ResourcesConfig
            {
                MaxConcurrentReportExecutions = 1,
                ExecutionTimeoutSeconds = 1
            }
        };

        var scopeFactory = new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
        // A remote channel that never responds keeps the first job inside the gate until its
        // timeout, forcing the second job to time out while still queued behind the gate.
        var channel = new HttpJobChannelClient(
            new HttpClient(new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        using var service = new ExecutionJobService(
            config, scopeFactory, NullLogger<ExecutionJobService>.Instance, sessions, channel);

        var first = service.EnqueueRefresh(reportId: 1, userId: 7, scriptPath);
        var second = service.EnqueueRefresh(reportId: 2, userId: 7, scriptPath);

        await WaitForTerminalAsync(service, first);
        await WaitForTerminalAsync(service, second);

        Assert.Null(service.GetActiveRefreshJobId(1));
        Assert.Null(service.GetActiveRefreshJobId(2));

        // The debounce entry must be gone (a new job id is issued) and the gate must still
        // have capacity (the new job also runs to a terminal state instead of queueing forever).
        var third = service.EnqueueRefresh(reportId: 1, userId: 7, scriptPath);
        Assert.NotEqual(first, third);
        await WaitForTerminalAsync(service, third);
    }

    /// <summary>
    /// Regression for unbounded job-table growth: terminal jobs older than the retention
    /// window are evicted on the next enqueue, while recent terminal jobs stay queryable.
    /// </summary>
    [Fact]
    public async Task Enqueue_EvictsTerminalJobsPastRetention_KeepsRecentOnes()
    {
        var scriptPath = Path.Combine(_tempDir, "scripts", "report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'never executed';");

        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(_tempDir, "portal.db"),
            ScriptRootPath = Path.Combine(_tempDir, "scripts"),
            SnapshotDirectory = Path.Combine(_tempDir, "snapshots"),
            DatasetRootPath = Path.Combine(_tempDir, "datasets"),
            Resources = new ResourcesConfig
            {
                MaxConcurrentReportExecutions = 1,
                ExecutionTimeoutSeconds = 1
            }
        };

        var scopeFactory = new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
        var channel = new HttpJobChannelClient(
            new HttpClient(new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        using var service = new ExecutionJobService(
            config, scopeFactory, NullLogger<ExecutionJobService>.Instance, sessions, channel);

        var old = service.EnqueueRefresh(reportId: 1, userId: 7, scriptPath);
        await WaitForTerminalAsync(service, old);
        service.Get(old)!.CompletedAt =
            DateTime.UtcNow - ExecutionJobService.CompletedJobRetention - TimeSpan.FromMinutes(1);

        var recent = service.EnqueueRefresh(reportId: 2, userId: 7, scriptPath);
        await WaitForTerminalAsync(service, recent);

        var trigger = service.EnqueueRefresh(reportId: 3, userId: 7, scriptPath);

        Assert.Null(service.Get(old));        // past retention — evicted
        Assert.NotNull(service.Get(recent));  // recent terminal job — still queryable
        await WaitForTerminalAsync(service, trigger);
    }

    private static async Task WaitForTerminalAsync(ExecutionJobService service, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var job = service.Get(jobId);
            Assert.NotNull(job);
            if (job.Status is JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed)
                return;
            await Task.Delay(50);
        }

        Assert.Fail(
            $"Job {jobId} did not reach a terminal state within 15s " +
            $"(status={service.Get(jobId)?.Status}).");
    }

    private sealed class NeverRespondingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
