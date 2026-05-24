using System.Collections.Concurrent;
using System.Security.Cryptography;
using ETL_SQL.Reporting;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExecutionJobService> _log;
    private readonly SessionCache _sessions;
    private readonly IJobChannel  _channel;
    public ExecutionJobService(
        PortalConfig config,
        IServiceScopeFactory scopeFactory,
        ILogger<ExecutionJobService> log,
        SessionCache sessions,
        IJobChannel channel)
    {
        _config   = config;
        _scopeFactory = scopeFactory;
        _log      = log;
        _sessions = sessions;
        _channel  = channel;
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
        await UpdateReportRefreshStatusAsync(job, "Running", null);
        _log.LogInformation("Execution job {JobId} started for report {ReportId}", job.Id, job.ReportId);

        try
        {
            if (!PortalPathGuard.TryResolveScript(_config, scriptPath, out var resolvedScriptPath))
                throw new UnauthorizedAccessException("Report script path is outside the configured script root.");
            scriptPath = resolvedScriptPath;

            if (!PortalPathGuard.TryResolveSnapshot(
                    _config,
                    $"report_{job.ReportId}_{job.Id}.snapshot.json",
                    out var manifestPath))
                throw new UnauthorizedAccessException("Snapshot path is outside the configured snapshot directory.");

            // Hash the script file at execution time for integrity tracking
            string? runTimeHash = null;
            bool? hashMatched = null;
            if (System.IO.File.Exists(scriptPath))
            {
                runTimeHash = "sha256:" + Convert.ToHexString(
                    SHA256.HashData(System.IO.File.ReadAllBytes(scriptPath))).ToLowerInvariant();
            }

            if (_channel is HttpJobChannelClient)
            {
                _log.LogInformation("Submitting execution job {JobId} to remote orchestrator", job.Id);
                var scriptText = await System.IO.File.ReadAllTextAsync(scriptPath, cts.Token);
                var remoteJobId = await _channel.SubmitJobAsync(new JobSubmitRequest
                {
                    ScriptText = scriptText,
                    Label      = $"Report {job.ReportId} Execution",
                    SessionId  = job.Id,
                    Metadata   = new Dictionary<string, string>
                    {
                        { "ReportId", job.ReportId.ToString() },
                        { "IsReport", "true" }
                    }
                }, cts.Token);

                // Poll for completion
                while (true)
                {
                    var status = await _channel.GetStatusAsync(remoteJobId, cts.Token);
                    if (status.Status == JobRunStatus.Completed) break;
                    if (status.Status == JobRunStatus.Failed) throw new Exception(status.ErrorMessage ?? "Remote job failed.");
                    if (status.Status == JobRunStatus.Cancelled) throw new OperationCanceledException();
                    await Task.Delay(1000, cts.Token);
                }
                
                // Orchestrator saved it to the shared volume; path is deterministic and already root-checked.
            }
            else
            {
                // Use an independent DashboardService for snapshots (not the session cache)
                var dashboardTimeout = TimeSpan.FromSeconds(Math.Max(1, _config.Resources.ExecutionTimeoutSeconds));
                await using var svc = new ETL_SQL.ReportHosting.DashboardService(scriptPath, _scopeFactory, dashboardTimeout);

                if (parameters is { Count: > 0 })
                    await svc.SetParametersAsync(parameters.Select(kv => (kv.Key, kv.Value)));

                var manifest = await svc.RebuildAsync().WaitAsync(cts.Token);
                await PersistReportLineageAsync(job, scriptPath, svc.CurrentLineageTracker);

                // Save manifest to portal's SnapshotDirectory.
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

                var store = new SnapshotStore();
                await store.SaveAsync(manifest, manifestPath);
            }

            // Persist to DB
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

            // Compare hash against published hash
            var report = await db.Reports.FindAsync(job.ReportId);
            if (report?.PublishedScriptHash is not null && runTimeHash is not null)
            {
                hashMatched = string.Equals(runTimeHash, report.PublishedScriptHash, StringComparison.OrdinalIgnoreCase);
                if (!hashMatched.Value)
                    _log.LogWarning("Script hash mismatch for report {ReportId}. Expected: {Expected}, Got: {Got}",
                        job.ReportId, report.PublishedScriptHash, runTimeHash);
            }

            db.ReportSnapshots.Add(new ReportSnapshot
            {
                ReportId            = job.ReportId,
                ManifestPath        = manifestPath,
                BuiltAt             = DateTime.UtcNow,
                BuiltBy             = job.UserId,
                ParametersJson      = parameters is { Count: > 0 }
                    ? System.Text.Json.JsonSerializer.Serialize(parameters)
                    : null,
                ScriptHashAtRunTime = runTimeHash,
                HashMatched         = hashMatched
            });

            // Update ScriptLastModified on the report
            if (report is not null && System.IO.File.Exists(scriptPath))
            {
                report.ScriptLastModified = System.IO.File.GetLastWriteTimeUtc(scriptPath);
                report.LastRefreshCompletedAt = DateTime.UtcNow;
                report.LastRefreshStatus      = "Completed";
                report.LastRefreshError       = null;
                report.LastRefreshDurationMs  = job.StartedAt is null
                    ? null
                    : (long)(report.LastRefreshCompletedAt.Value - job.StartedAt.Value).TotalMilliseconds;
            }

            await db.SaveChangesAsync();

            // Invalidate sessions so next parameter interaction picks up fresh data
            await _sessions.InvalidateReportAsync(job.ReportId);

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
            await UpdateReportRefreshStatusAsync(job, "Cancelled", job.Error);
            _log.LogWarning("Execution job {JobId} cancelled/timed out", job.Id);
        }
        catch (Exception ex)
        {
            job.Status      = JobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.Error       = ex.Message;
            await UpdateReportRefreshStatusAsync(job, "Failed", job.Error);
            _log.LogError(ex, "Execution job {JobId} failed: {Message}. StackTrace: {Stack}", 
                job.Id, ex.Message, ex.StackTrace);
        }
        finally
        {
            _gate.Release();
            _activeRefreshes.TryRemove(new KeyValuePair<int, string>(job.ReportId, job.Id));
        }
    }

    private async Task PersistReportLineageAsync(ExecutionJob job, string scriptPath, ILineageTracker? tracker)
    {
        var entries = tracker?.GetFullLineage().ToList();
        if (entries is not { Count: > 0 }) return;

        using var scope = _scopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetService<ILineageCatalogStore>();
        if (catalog is null) return;

        await catalog.SaveLineageAsync(
            entries,
            $"report:{job.ReportId}:{job.Id}",
            scriptPath,
            DateTime.UtcNow);
    }

    private async Task UpdateReportRefreshStatusAsync(ExecutionJob job, string status, string? error)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var report = await db.Reports.FindAsync(job.ReportId);
        if (report is null) return;

        if (string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            report.LastRefreshStartedAt   = job.StartedAt ?? DateTime.UtcNow;
            report.LastRefreshCompletedAt = null;
            report.LastRefreshDurationMs  = null;
        }
        else
        {
            report.LastRefreshCompletedAt = job.CompletedAt ?? DateTime.UtcNow;
            report.LastRefreshDurationMs  = job.StartedAt is null
                ? null
                : (long)(report.LastRefreshCompletedAt.Value - job.StartedAt.Value).TotalMilliseconds;
        }

        report.LastRefreshStatus = status;
        report.LastRefreshError  = error;
        await db.SaveChangesAsync();
    }

    public void Dispose() => _gate.Dispose();
}
