using ETL_SQL.Common;
using ETL_SQL.Core.Observability;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Background service that polls the configured Orchestrator JobHistory store every
/// Portal:Orchestrator:PollIntervalSeconds (default 60).
/// Dataset-refresh completions invalidate the snapshot and queue a re-execution. Subscription
/// trigger completions are routed through the trusted delivery executor.
/// If the Orchestrator DB is unreachable the portal continues in degraded mode (cached snapshots only).
/// </summary>
public class OrchestratorPollerService(
    OrchestratorDbLocator dbLocator,
    IOrchestratorStoreFactory storeFactory,
    IServiceScopeFactory scopes,
    ExecutionJobService jobs,
    PortalConfig config,
    ILogger<OrchestratorPollerService> log) : BackgroundService
{
    private DateTime _lastPollTime = DateTime.UtcNow.AddSeconds(-70); // poll covers first 70s on startup

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, config.Orchestrator.PollIntervalSeconds));
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);
            await PollAsync(ct);
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun("portal", "orchestrator-poller", "poll");
        var orchDbPath = dbLocator.Resolve();

        if (storeFactory.Provider == DatabaseProvider.Sqlite
            && (orchDbPath is null || !File.Exists(orchDbPath)))
        {
            log.LogDebug("OrchestratorPoller: Orchestrator DB not found at {Path} — degraded mode", orchDbPath);
            CompletePoll(activity, sw, "degraded");
            return;
        }

        List<(string JobName, DateTime EndTime)> completions;
        var pollUpperBound = DateTime.UtcNow;
        try
        {
            var store = storeFactory.Create(orchDbPath);
            await store.InitializeAsync();
            const int pageSize = 1000;
            var completedRows = new List<ETL_SQL.Core.Data.JobHistoryEntry>();
            for (var offset = 0; ; offset += pageSize)
            {
                var page = (await store.GetCompletedHistoryAsync(
                    _lastPollTime, pollUpperBound, pageSize, offset)).ToList();
                completedRows.AddRange(page);
                if (page.Count < pageSize) break;
            }

            completions = completedRows
                .Where(entry => string.Equals(entry.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                .Select(entry => (entry.JobName, EndTime: entry.EndTime!.Value.ToUniversalTime()))
                .ToList();
        }
        catch (Exception ex)
        {
            log.LogWarning("OrchestratorPoller: Orchestrator DB query failed — {Message}", ex.Message);
            CompletePoll(activity, sw, "degraded");
            return;
        }

        if (!completions.Any())
        {
            _lastPollTime = pollUpperBound;
            CompletePoll(activity, sw, "idle");
            return;
        }

        log.LogInformation("OrchestratorPoller: {Count} job completion(s) detected", completions.Count);

        var failed = false;
        foreach (var (jobName, endTime) in completions)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

                if (SubscriptionOrchestration.TryParseSubscriptionId(jobName, out var subscriptionId))
                {
                    await ProcessSubscriptionCompletionAsync(
                        scope.ServiceProvider, db, subscriptionId, endTime, ct);
                    _lastPollTime = endTime;
                    continue;
                }

                var datasetJob = await db.DatasetJobs
                    .Include(j => j.Report)
                    .FirstOrDefaultAsync(j => j.OrchestratorJobName == jobName, ct);

                if (datasetJob is null)
                {
                    _lastPollTime = endTime;
                    continue;
                }

                log.LogInformation("OrchestratorPoller: refreshing report {ReportId} after job {JobName}",
                    datasetJob.ReportId, jobName);

                datasetJob.LastRefreshedAt = endTime;
                await db.SaveChangesAsync(ct);

                // The poller is the sole trusted dataset execution path. Interactive execution and
                // user-triggered refreshes retain their real UserId caller context.
                await jobs.EnqueueRefreshAsync(
                    datasetJob.ReportId,
                    userId: 0,
                    scriptPath: datasetJob.Report.ScriptPath,
                    trustedDatasetExecution: true);

                _lastPollTime = endTime;
            }
            catch (Exception ex)
            {
                log.LogError(ex,
                    "OrchestratorPoller: error processing completion for job {JobName}", jobName);
                failed = true;
                break;
            }
        }

        CompletePoll(activity, sw, failed ? "failure" : "success");
    }

    private async Task ProcessSubscriptionCompletionAsync(
        IServiceProvider services,
        PortalDbContext db,
        int subscriptionId,
        DateTime endTime,
        CancellationToken ct)
    {
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (sub is null || sub.LastTriggeredAt >= endTime)
            return;

        log.LogInformation(
            "OrchestratorPoller: delivering subscription {SubscriptionId}", subscriptionId);

        // The completion's EndTime is the durable trigger key: the delivery ledger guarantees
        // at-most-once delivery per completion even if this completion is observed twice.
        var delivery = services.GetRequiredService<SubscriptionDeliveryService>();
        var result = await delivery.DeliverAsync(subscriptionId, endTime.ToString("o"), ct);

        sub.LastTriggeredAt = endTime;
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "OrchestratorPoller: subscription {SubscriptionId} completed with outcome {Outcome}",
            subscriptionId, result.Outcome);
    }

    private static void CompletePoll(System.Diagnostics.Activity? activity, System.Diagnostics.Stopwatch sw, string status)
    {
        sw.Stop();
        BackgroundServiceObservability.CompleteRun(
            activity,
            "portal",
            "orchestrator-poller",
            "poll",
            status,
            sw.ElapsedMilliseconds);
    }
}
