using System.Collections.Concurrent;
using ETL_SQL.ReportBuilder;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public enum JobStatus { Pending, Running, Completed, Failed, Cancelled }

public record ExecutionJob(
    string Id,
    int    ReportId,
    int    UserId)
{
    public JobStatus  Status       { get; set; } = JobStatus.Pending;
    public DateTime   CreatedAt    { get; init; } = DateTime.UtcNow;
    public DateTime?  StartedAt    { get; set; }
    public DateTime?  CompletedAt  { get; set; }
    public string?    ManifestPath { get; set; }
    public string?    Error        { get; set; }
}

/// <summary>
/// Manages async report-execution jobs.
/// Each job runs the .rptsql script via DashboardService, saves the manifest to disk,
/// and stores the ManifestPath in ReportSnapshots.
/// Concurrency is capped by MaxConcurrentReportExecutions.
/// </summary>
public class ExecutionJobService : IDisposable
{
    private readonly ConcurrentDictionary<string, ExecutionJob> _jobs = new();
    private readonly ConcurrentDictionary<int, string>          _activeRefreshes = new();
    private readonly SemaphoreSlim _gate;
    private readonly PortalConfig  _config;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ExecutionJobService> _log;
    private readonly SessionCache _sessions;
    public ExecutionJobService(
        PortalConfig config,
        IServiceScopeFactory scopes,
        ILogger<ExecutionJobService> log,
        SessionCache sessions)
    {
        _config   = config;
        _scopes   = scopes;
        _log      = log;
        _sessions = sessions;
        _gate     = new SemaphoreSlim(config.Resources.MaxConcurrentReportExecutions,
                                       config.Resources.MaxConcurrentReportExecutions);
    }

    public ExecutionJob? Get(string jobId) =>
        _jobs.TryGetValue(jobId, out var j) ? j : null;

    /// <summary>Returns the in-progress jobId for a report if a refresh is already running.</summary>
    public string? GetActiveRefreshJobId(int reportId) =>
        _activeRefreshes.TryGetValue(reportId, out var id) ? id : null;

    /// <summary>Queues a new execution job and starts it in the background.</summary>
    public string EnqueueExecution(int reportId, int userId, string scriptPath,
        Dictionary<string, string>? parameters = null)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var job   = new ExecutionJob(jobId, reportId, userId);
        _jobs[jobId] = job;

        _ = RunJobAsync(job, scriptPath, parameters, CancellationToken.None);
        return jobId;
    }

    /// <summary>
    /// Enqueues a refresh job for a report. Debounced — returns the existing jobId if
    /// a refresh is already in flight for this report.
    /// </summary>
    public string EnqueueRefresh(int reportId, int userId, string scriptPath)
    {
        if (_activeRefreshes.TryGetValue(reportId, out var existing))
            return existing;

        var jobId = EnqueueExecution(reportId, userId, scriptPath);
        _activeRefreshes[reportId] = jobId;
        return jobId;
    }

    private async Task RunJobAsync(
        ExecutionJob job,
        string scriptPath,
        Dictionary<string, string>? parameters,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(_config.Resources.ExecutionTimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        await _gate.WaitAsync(cts.Token).ConfigureAwait(false);
        job.Status    = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        _log.LogInformation("Execution job {JobId} started for report {ReportId}", job.Id, job.ReportId);

        try
        {
            // Use an independent DashboardService for snapshots (not the session cache)
            var svc = new ETL_SQL.ReportPlayer.DashboardService(scriptPath);

            if (parameters is { Count: > 0 })
                await svc.SetParametersAsync(parameters.Select(kv => (kv.Key, kv.Value)));

            var manifest = await svc.RebuildAsync().WaitAsync(cts.Token);

            // Save manifest to portal's SnapshotDirectory
            var snapshotDir = Path.GetFullPath(_config.SnapshotDirectory);
            Directory.CreateDirectory(snapshotDir);
            var manifestPath = Path.Combine(snapshotDir, $"report_{job.ReportId}_{job.Id}.snapshot.json");

            var store = new SnapshotStore();
            await store.SaveAsync(manifest, manifestPath);

            // Persist to DB
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

            db.ReportSnapshots.Add(new ReportSnapshot
            {
                ReportId       = job.ReportId,
                ManifestPath   = manifestPath,
                BuiltAt        = DateTime.UtcNow,
                BuiltBy        = job.UserId,
                ParametersJson = parameters is { Count: > 0 }
                    ? System.Text.Json.JsonSerializer.Serialize(parameters)
                    : null
            });

            // Update ScriptLastModified on the report
            var report = await db.Reports.FindAsync(job.ReportId);
            if (report is not null && System.IO.File.Exists(scriptPath))
                report.ScriptLastModified = System.IO.File.GetLastWriteTimeUtc(scriptPath);

            await db.SaveChangesAsync();

            // Invalidate sessions so next parameter interaction picks up fresh data
            _sessions.InvalidateReport(job.ReportId);

            job.Status       = JobStatus.Completed;
            job.ManifestPath = manifestPath;
            job.CompletedAt  = DateTime.UtcNow;
            _log.LogInformation("Execution job {JobId} completed", job.Id);

        }
        catch (OperationCanceledException)
        {
            job.Status      = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            job.Error       = "Execution timed out or was cancelled";
            _log.LogWarning("Execution job {JobId} cancelled/timed out", job.Id);
        }
        catch (Exception ex)
        {
            job.Status      = JobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.Error       = ex.Message;
            _log.LogError(ex, "Execution job {JobId} failed", job.Id);
        }
        finally
        {
            _gate.Release();
            _activeRefreshes.TryRemove(new KeyValuePair<int, string>(job.ReportId, job.Id));
        }
    }

    public void Dispose() => _gate.Dispose();
}
