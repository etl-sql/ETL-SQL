using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.ReportPortal.Data;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Background service that polls the Orchestrator's JobHistory SQLite table every 60 seconds.
/// When a dataset-refresh job completes, it invalidates the snapshot and queues a re-execution.
/// If the Orchestrator DB is unreachable the portal continues in degraded mode (cached snapshots only).
/// </summary>
public class OrchestratorPollerService(
    OrchestratorDbLocator dbLocator,
    IServiceScopeFactory  scopes,
    ExecutionJobService   jobs,
    ILogger<OrchestratorPollerService> log) : BackgroundService
{
    private DateTime _lastPollTime = DateTime.UtcNow.AddSeconds(-70); // poll covers first 70s on startup

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
            await PollAsync(ct);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        // Find orchestrator DB path from portal DatasetJobs table
        string? orchDbPath;
        int[]   watchedReportIds;

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

            var datasetJobs = await db.DatasetJobs
                .Include(j => j.Report)
                .Where(j => !j.Report.IsDeleted)
                .ToListAsync(ct);

            if (!datasetJobs.Any()) return;

            watchedReportIds = datasetJobs.Select(j => j.ReportId).ToArray();
            // Orchestrator DB path comes from appsettings — for now derive from default location
            orchDbPath = dbLocator.Resolve();
        }
        catch (Exception ex)
        {
            log.LogWarning("OrchestratorPoller: failed to load dataset jobs — {Message}", ex.Message);
            return;
        }

        if (orchDbPath is null || !File.Exists(orchDbPath))
        {
            log.LogDebug("OrchestratorPoller: Orchestrator DB not found at {Path} — degraded mode", orchDbPath);
            return;
        }

        List<(string JobName, DateTime EndTime)> completions;
        try
        {
            completions = await QueryCompletionsAsync(orchDbPath, _lastPollTime, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning("OrchestratorPoller: Orchestrator DB query failed — {Message}", ex.Message);
            return;
        }

        if (!completions.Any())
        {
            _lastPollTime = DateTime.UtcNow;
            return;
        }

        log.LogInformation("OrchestratorPoller: {Count} job completion(s) detected", completions.Count);

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

            foreach (var (jobName, endTime) in completions)
            {
                var datasetJob = await db.DatasetJobs
                    .Include(j => j.Report)
                    .FirstOrDefaultAsync(j => j.OrchestratorJobName == jobName, ct);

                if (datasetJob is null) continue;

                log.LogInformation("OrchestratorPoller: refreshing report {ReportId} after job {JobName}",
                    datasetJob.ReportId, jobName);

                datasetJob.LastRefreshedAt = endTime;
                await db.SaveChangesAsync(ct);

                // Queue re-execution (system user id = 0)
                jobs.EnqueueRefresh(datasetJob.ReportId, userId: 0, datasetJob.Report.ScriptPath);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "OrchestratorPoller: error processing completions");
        }

        _lastPollTime = DateTime.UtcNow;
    }

    private static async Task<List<(string JobName, DateTime EndTime)>> QueryCompletionsAsync(
        string dbPath, DateTime since, CancellationToken ct)
    {
        var results = new List<(string, DateTime)>();
        var cs = $"Data Source={dbPath};Mode=ReadOnly";

        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT JobName, EndTime FROM JobHistory
            WHERE Status = 'COMPLETED'
              AND EndTime > $since
            ORDER BY EndTime ASC
            """;
        cmd.Parameters.AddWithValue("$since", since.ToString("o"));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name    = reader.GetString(0);
            var endRaw  = reader.GetString(1);
            if (DateTime.TryParse(endRaw, out var endTime))
                results.Add((name, endTime));
        }

        return results;
    }

}
