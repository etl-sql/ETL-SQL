using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
using ETL_SQL.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Taking an execution node out of rotation under load. A rolling upgrade needs the gentle path:
/// in-flight reports finish, new ones go elsewhere. Cancelling every running execution to install a
/// release is an outage, not a rollout — that abrupt behaviour belongs to lease *loss*, where the
/// node has actually lost its claim and must stop.
/// </summary>
[Trait("Category", "Portal")]
public class ExecutionNodeDrainTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"exec_drain_{Guid.NewGuid():N}");

    public ExecutionNodeDrainTests()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "scripts"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "snapshots"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "datasets"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task DrainingRefusesNewWorkWhileInFlightExecutionsAreLeftToFinish()
    {
        using var service = Service(out _);
        var scriptPath = await ScriptAsync();

        var running = await service.EnqueueRefreshAsync(reportId: 1, userId: 7, scriptPath);
        await LoadAwareWait.UntilAsync(
            "the first execution to be in flight",
            _ => Task.FromResult(service.InFlightExecutions),
            inFlight => inFlight > 0,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(25),
            inFlight => $"inFlight={inFlight}");

        service.BeginDrain("node is being rolled to 0.18.0");

        var (draining, reason, inFlightAtDrain) = service.DrainState;
        Assert.True(draining);
        Assert.Contains("0.18.0", reason);
        Assert.Equal(1, inFlightAtDrain);

        // New work is refused rather than queued, so a load balancer places it on a node that stays.
        var refused = await Assert.ThrowsAsync<NodeDrainingException>(() =>
            service.EnqueueRefreshAsync(reportId: 2, userId: 7, scriptPath));
        Assert.Contains("not accepting new work", refused.Message);
        await Assert.ThrowsAsync<NodeDrainingException>(() =>
            service.EnqueueExecutionAsync(reportId: 3, userId: 7, scriptPath));

        // The execution that was already running is not cancelled by the drain itself; it is allowed
        // to reach its own terminal state, which is the whole difference from lease loss.
        var job = await service.GetAsync(running);
        Assert.NotNull(job);
        Assert.NotEqual(JobStatus.Cancelled, job!.Status);

        await WaitForTerminalAsync(service, running);
        await LoadAwareWait.UntilAsync(
            "the drain to finish",
            _ => Task.FromResult(service.InFlightExecutions),
            inFlight => inFlight == 0,
            TimeSpan.FromSeconds(45),
            TimeSpan.FromMilliseconds(50),
            inFlight => $"inFlight={inFlight}");
        Assert.Equal(0, service.DrainState.InFlight);
    }

    [Fact]
    public async Task DrainIsIdempotentAndKeepsItsFirstReason()
    {
        using var service = Service(out _);

        service.BeginDrain("first reason");
        service.BeginDrain("second reason");

        Assert.Contains("first reason", service.DrainState.Reason);
        var scriptPath = await ScriptAsync();
        await Assert.ThrowsAsync<NodeDrainingException>(() =>
            service.EnqueueExecutionAsync(reportId: 1, userId: 7, scriptPath));
    }

    [Fact]
    public async Task LeaseLossStillFencesRunningWorkRatherThanDrainingIt()
    {
        using var service = Service(out _);
        var scriptPath = await ScriptAsync();
        var running = await service.EnqueueRefreshAsync(reportId: 1, userId: 7, scriptPath);
        await LoadAwareWait.UntilAsync(
            "the execution to be in flight",
            _ => Task.FromResult(service.InFlightExecutions),
            inFlight => inFlight > 0,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(25),
            inFlight => $"inFlight={inFlight}");

        // A node that has lost its claim must stop, not finish: another node may already be running
        // this work. The two paths must stay distinct.
        await service.OnNodeLeaseLostAsync("node-a", "portal", "heartbeat expired", default);

        await WaitForTerminalAsync(service, running);
        Assert.Equal(JobStatus.Cancelled, (await service.GetAsync(running))!.Status);
    }

    private ExecutionJobService Service(out PortalConfig config)
    {
        config = new PortalConfig
        {
            DatabasePath = Path.Combine(_tempDir, "portal.db"),
            ScriptRootPath = Path.Combine(_tempDir, "scripts"),
            SnapshotDirectory = Path.Combine(_tempDir, "snapshots"),
            DatasetRootPath = Path.Combine(_tempDir, "datasets"),
            Resources = new ResourcesConfig
            {
                MaxConcurrentReportExecutions = 2,
                ExecutionTimeoutSeconds = 20
            }
        };

        var scopeFactory = new ServiceCollection().BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
        // A channel that never answers keeps the execution in flight for the duration of the test,
        // which is what "under load" needs to mean here.
        var channel = new HttpJobChannelClient(
            new HttpClient(new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        return new ExecutionJobService(
            config, scopeFactory, NullLogger<ExecutionJobService>.Instance, sessions, channel);
    }

    private async Task<string> ScriptAsync()
    {
        var scriptPath = Path.Combine(_tempDir, "scripts", "report.rptsql");
        if (!File.Exists(scriptPath))
            await File.WriteAllTextAsync(scriptPath, "PRINT 'held by the stalled channel';");
        return scriptPath;
    }

    private static Task WaitForTerminalAsync(ExecutionJobService service, string jobId) =>
        LoadAwareWait.UntilAsync(
            $"job '{jobId}' to become terminal",
            async _ => await service.GetAsync(jobId),
            job => job?.Status is JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed,
            TimeSpan.FromSeconds(45),
            TimeSpan.FromMilliseconds(50),
            job => $"status={job?.Status.ToString() ?? "<missing>"}");

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
