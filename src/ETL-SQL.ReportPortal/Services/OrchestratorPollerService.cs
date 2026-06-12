using System.Globalization;
using ETL_SQL.ReportPortal.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Background service that polls the Orchestrator's JobHistory SQLite table every
/// Portal:Orchestrator:PollIntervalSeconds (default 60).
/// Dataset-refresh completions invalidate the snapshot and queue a re-execution. Subscription
/// trigger completions are routed through the trusted delivery executor.
/// If the Orchestrator DB is unreachable the portal continues in degraded mode (cached snapshots only).
/// </summary>
public class OrchestratorPollerService(
    OrchestratorDbLocator dbLocator,
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
        var orchDbPath = dbLocator.Resolve();

        if (orchDbPath is null || !File.Exists(orchDbPath))
        {
            log.LogDebug("OrchestratorPoller: Orchestrator DB not found at {Path} — degraded mode", orchDbPath);
            return;
        }

        List<(string JobName, DateTime EndTime)> completions;
        var pollUpperBound = DateTime.UtcNow;
        try
        {
            completions = await QueryCompletionsAsync(
                orchDbPath, _lastPollTime, pollUpperBound, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning("OrchestratorPoller: Orchestrator DB query failed — {Message}", ex.Message);
            return;
        }

        if (!completions.Any())
        {
            _lastPollTime = pollUpperBound;
            return;
        }

        log.LogInformation("OrchestratorPoller: {Count} job completion(s) detected", completions.Count);

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
                break;
            }
        }
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

        var delivery = services.GetRequiredService<SubscriptionDeliveryService>();
        var result = await delivery.DeliverAsync(subscriptionId, ct);

        // Every terminal delivery decision consumes this scheduler completion. Transient retry and
        // unknown-outcome semantics require a durable delivery ledger and are tracked separately.
        sub.LastTriggeredAt = endTime;
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "OrchestratorPoller: subscription {SubscriptionId} completed with outcome {Outcome}",
            subscriptionId, result.Outcome);
    }

    private static async Task<List<(string JobName, DateTime EndTime)>> QueryCompletionsAsync(
        string dbPath, DateTime since, DateTime through, CancellationToken ct)
    {
        var results = new List<(string, DateTime)>();
        var cs = $"Data Source={dbPath};Mode=ReadOnly";

        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT JobName, EndTime FROM JobHistory
            WHERE Status = 'SUCCESS'
              AND julianday(EndTime) > julianday($since)
              AND julianday(EndTime) <= julianday($through)
            ORDER BY EndTime ASC
            """;
        cmd.Parameters.AddWithValue("$since", since.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$through", through.ToString("o", CultureInfo.InvariantCulture));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            var endRaw = reader.GetString(1);
            if (DateTime.TryParse(
                    endRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var endTime))
                results.Add((name, endTime));
        }

        return results;
    }

}
