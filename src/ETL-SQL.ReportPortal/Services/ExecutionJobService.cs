using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Storage;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Reporting;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public enum JobStatus { Pending, Running, Completed, Failed, Cancelled }

public record ExecutionJob(
    string Id,
    int ReportId,
    int UserId,
    bool IsAdministrator = false,
    bool TrustedDatasetExecution = false)
{
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ManifestPath { get; set; }
    public string? Error { get; set; }
    public string DatasetCallerContext => TrustedDatasetExecution
        ? "IsAdmin=true"
        : IsAdministrator
            ? $"UserId={UserId};IsAdmin=true"
            : $"UserId={UserId}";
}

/// <summary>
/// Manages async report-execution jobs.
/// Each job runs the .rptsql script via DashboardService, saves the manifest to artifact storage,
/// and stores the ManifestPath in ReportSnapshots.
/// Concurrency is capped by MaxConcurrentReportExecutions.
/// </summary>
public class ExecutionJobService : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, ExecutionJob> _jobs = new();
    private readonly ConcurrentDictionary<int, string> _activeRefreshes = new();
    private readonly SemaphoreSlim _gate;

    /// <summary>Per-user concurrency limiters (workload fairness, P2.6). Keyed by user id; one
    /// gate per user with <c>MaxConcurrentExecutionsPerUser</c> permits.</summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _userGates = new();
    private readonly PortalConfig _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExecutionJobService> _log;
    private readonly SessionCache _sessions;
    private readonly IJobChannel _channel;
    private readonly IArtifactStorage _artifacts;
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public ExecutionJobService(
        PortalConfig config,
        IServiceScopeFactory scopeFactory,
        ILogger<ExecutionJobService> log,
        SessionCache sessions,
        IJobChannel channel,
        IArtifactStorage? artifacts = null)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _log = log;
        _sessions = sessions;
        _channel = channel;
        _artifacts = artifacts ?? CreateDefaultArtifactStorage(config);
        _gate = new SemaphoreSlim(config.Resources.MaxConcurrentReportExecutions,
                                       config.Resources.MaxConcurrentReportExecutions);
    }

    /// <summary>Terminal jobs stay queryable for this window, then are evicted so the
    /// in-memory job table cannot grow without bound on a long-running portal.</summary>
    internal static readonly TimeSpan CompletedJobRetention = TimeSpan.FromHours(24);

    public async Task<ExecutionJob?> GetAsync(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
            return job;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<PortalDbContext>();
        var stored = db is null ? null : await db.PortalExecutionJobs.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == jobId);
        return stored is null ? null : FromEntity(stored);
    }

    private void EvictExpiredJobs()
    {
        var cutoff = DateTime.UtcNow - CompletedJobRetention;
        foreach (var (id, job) in _jobs)
        {
            if (job.CompletedAt is { } completedAt && completedAt < cutoff)
                _jobs.TryRemove(id, out _);
        }
    }

    /// <summary>Returns the in-progress jobId for a report if a refresh is already running.</summary>
    public async Task<string?> GetActiveRefreshJobIdAsync(int reportId)
    {
        if (_activeRefreshes.TryGetValue(reportId, out var id))
            return id;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<PortalDbContext>();
        return db is null
            ? null
            : await db.PortalExecutionJobs.AsNoTracking()
                .Where(value => value.ReportId == reportId
                    && value.Kind == "Refresh"
                    && (value.Status == "Pending" || value.Status == "Running"))
                .Select(value => value.Id)
                .FirstOrDefaultAsync();
    }

    /// <summary>Queues a new execution job and starts it in the background.</summary>
    public async Task<string> EnqueueExecutionAsync(int reportId, int userId, string scriptPath,
        Dictionary<string, string>? parameters = null,
        bool isAdministrator = false)
    {
        EvictExpiredJobs();
        var jobId = Guid.NewGuid().ToString("N");
        var job = new ExecutionJob(jobId, reportId, userId, IsAdministrator: isAdministrator);
        _jobs[jobId] = job;
        await PersistNewJobAsync(job, "Execution");

        _ = RunJobAsync(job, scriptPath, parameters, CancellationToken.None);
        return jobId;
    }

    /// <summary>
    /// Enqueues a refresh job for a report. Debounced — returns the existing jobId if
    /// a refresh is already in flight for this report.
    /// </summary>
    public async Task<string> EnqueueRefreshAsync(
        int reportId,
        int userId,
        string scriptPath,
        bool isAdministrator = false,
        bool trustedDatasetExecution = false)
    {
        EvictExpiredJobs();
        var existingPersisted = await GetActiveRefreshJobIdAsync(reportId);
        if (existingPersisted is not null)
            return existingPersisted;

        var jobId = Guid.NewGuid().ToString("N");
        while (!_activeRefreshes.TryAdd(reportId, jobId))
        {
            // The in-flight refresh can complete between the failed TryAdd and this read;
            // fall through and retry the claim instead of throwing on a missing key.
            if (_activeRefreshes.TryGetValue(reportId, out var existingJobId))
                return existingJobId;
        }

        var job = new ExecutionJob(
            jobId,
            reportId,
            userId,
            IsAdministrator: isAdministrator,
            TrustedDatasetExecution: trustedDatasetExecution);
        _jobs[jobId] = job;

        if (!await TryPersistRefreshJobAsync(job))
        {
            _jobs.TryRemove(jobId, out _);
            _activeRefreshes.TryRemove(new KeyValuePair<int, string>(reportId, jobId));
            return await GetActiveRefreshJobIdAsync(reportId)
                ?? throw new InvalidOperationException("The active refresh claim could not be resolved.");
        }

        _ = RunJobAsync(job, scriptPath, parameters: null, CancellationToken.None);
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

        // Workload fairness (P2.6): a non-admin holds at most MaxConcurrentExecutionsPerUser of the
        // shared slots. Acquire the per-user slot FIRST and without holding a global permit, so a
        // capped user queues without blocking the shared pool. Administrators are exempt.
        var userGate = job.IsAdministrator ? null : GetUserGate(job.UserId);
        try
        {
            if (userGate is not null)
                await userGate.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                await _gate.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                userGate?.Release();
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out while queued — no global permit was retained, so only the job
            // bookkeeping needs unwinding (the refresh debounce must clear).
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            job.Error = "Execution timed out while waiting for an execution slot";
            _activeRefreshes.TryRemove(new KeyValuePair<int, string>(job.ReportId, job.Id));
            await PersistJobAsync(job);
            await UpdateReportRefreshStatusAsync(job, "Cancelled", job.Error);
            _log.LogWarning("Execution job {JobId} cancelled while queued for an execution slot", job.Id);
            return;
        }

        try
        {
            job.Status = JobStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            await PersistJobAsync(job);
            await UpdateReportRefreshStatusAsync(job, "Running", null);
            _log.LogInformation("Execution job {JobId} started for report {ReportId}", job.Id, job.ReportId);

            if (!PortalPathGuard.TryResolveScript(_config, scriptPath, out var resolvedScriptPath))
                throw new UnauthorizedAccessException("Report script path is outside the configured script root.");
            scriptPath = resolvedScriptPath;

            var manifestKey = $"report_{job.ReportId}_{job.Id}.snapshot.json";
            var manifestPath = manifestKey;
            if (PortalPathGuard.ToSnapshotKey(_config, manifestKey) != manifestKey)
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
                    Label = $"Report {job.ReportId} Execution",
                    SessionId = job.Id,
                    Metadata = new Dictionary<string, string>
                    {
                        { "ReportId", job.ReportId.ToString() },
                        { "IsReport", "true" }
                    }
                }, cts.Token);

                // Poll for completion
                while (true)
                {
                    var status = await _channel.GetStatusAsync(remoteJobId, cts.Token);
                    if (status.Status == JobRunStatus.Completed)
                    {
                        if (!string.IsNullOrWhiteSpace(status.ReportManifestJson))
                        {
                            var manifest = System.Text.Json.JsonSerializer.Deserialize<ReportManifest>(
                                status.ReportManifestJson)
                                ?? throw new InvalidOperationException("Remote orchestrator returned an invalid report manifest.");
                            await SaveSnapshotManifestAsync(manifest, manifestKey, cts.Token);
                        }
                        else if (!await _artifacts.ExistsAsync(ArtifactArea.Snapshots, manifestKey, cts.Token))
                        {
                            throw new InvalidOperationException(
                                "Remote orchestrator completed the report without returning or writing a snapshot manifest.");
                        }
                        break;
                    }
                    if (status.Status == JobRunStatus.Failed) throw new Exception(status.ErrorMessage ?? "Remote job failed.");
                    if (status.Status == JobRunStatus.Cancelled) throw new OperationCanceledException();
                    await Task.Delay(1000, cts.Token);
                }

                // Orchestrator saved it to the shared volume; path is deterministic and already root-checked.
            }
            else
            {
                // Use an independent DashboardService for snapshots (not the session cache).
                // Interactive execution and user-triggered refresh retain the caller identity.
                // Only the orchestrator poller explicitly creates trusted scheduled refreshes.
                var dashboardTimeout = TimeSpan.FromSeconds(Math.Max(1, _config.Resources.ExecutionTimeoutSeconds));
                await using var svc = new ETL_SQL.ReportHosting.DashboardService(
                    scriptPath,
                    _scopeFactory,
                    dashboardTimeout,
                    job.DatasetCallerContext,
                    job.ReportId,
                    _config.Dataset.AtRestKey);

                if (parameters is { Count: > 0 })
                    await svc.SetParametersAsync(parameters.Select(kv => (kv.Key, kv.Value)));

                var manifest = await svc.RebuildAsync().WaitAsync(cts.Token);
                await PersistReportLineageAsync(job, scriptPath, svc.CurrentLineageTracker);

                await SaveSnapshotManifestAsync(manifest, manifestKey, cts.Token);
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
                ReportId = job.ReportId,
                ManifestPath = manifestPath,
                BuiltAt = DateTime.UtcNow,
                BuiltBy = job.UserId,
                ParametersJson = parameters is { Count: > 0 }
                    ? System.Text.Json.JsonSerializer.Serialize(parameters)
                    : null,
                ScriptHashAtRunTime = runTimeHash,
                HashMatched = hashMatched
            });

            // Update ScriptLastModified on the report
            if (report is not null && System.IO.File.Exists(scriptPath))
            {
                report.ScriptLastModified = System.IO.File.GetLastWriteTimeUtc(scriptPath);
                report.LastRefreshCompletedAt = DateTime.UtcNow;
                report.LastRefreshStatus = "Completed";
                report.LastRefreshError = null;
                report.LastRefreshDurationMs = job.StartedAt is null
                    ? null
                    : (long)(report.LastRefreshCompletedAt.Value - job.StartedAt.Value).TotalMilliseconds;
            }

            await db.SaveChangesAsync();

            try
            {
                await PruneSnapshotsAsync(db, job.ReportId);
            }
            catch (Exception ex)
            {
                // Retention is best-effort; never fail a completed execution over it.
                _log.LogWarning(ex, "Snapshot pruning failed for report {ReportId}", job.ReportId);
            }

            // Invalidate sessions so next parameter interaction picks up fresh data
            await _sessions.InvalidateReportAsync(job.ReportId);

            job.Status = JobStatus.Completed;
            job.ManifestPath = manifestPath;
            job.CompletedAt = DateTime.UtcNow;
            await PersistJobAsync(job);
            _log.LogInformation("Execution job {JobId} completed", job.Id);

        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            job.Error = "Execution timed out or was cancelled";
            await PersistJobAsync(job);
            await UpdateReportRefreshStatusAsync(job, "Cancelled", job.Error);
            _log.LogWarning("Execution job {JobId} cancelled/timed out", job.Id);
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.Error = ex.Message;
            await PersistJobAsync(job);
            await UpdateReportRefreshStatusAsync(job, "Failed", job.Error);
            _log.LogError(ex, "Execution job {JobId} failed: {Message}. StackTrace: {Stack}",
                job.Id, ex.Message, ex.StackTrace);
        }
        finally
        {
            _gate.Release();
            userGate?.Release();
            _activeRefreshes.TryRemove(new KeyValuePair<int, string>(job.ReportId, job.Id));
        }
    }

    private SemaphoreSlim GetUserGate(int userId)
    {
        var limit = Math.Max(1, _config.Resources.MaxConcurrentExecutionsPerUser);
        return _userGates.GetOrAdd(userId, _ => new SemaphoreSlim(limit, limit));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<PortalDbContext>();
        if (db is null)
            return;

        var now = DateTime.UtcNow;
        var interrupted = await db.PortalExecutionJobs
            .Where(value => value.Status == "Pending" || value.Status == "Running")
            .ToListAsync(cancellationToken);
        foreach (var job in interrupted)
        {
            job.Status = "Cancelled";
            job.CompletedAt = now;
            job.Error = "Portal execution was interrupted by a process restart.";
        }

        var interruptedReportIds = interrupted.Select(value => value.ReportId).Distinct().ToList();
        if (interruptedReportIds.Count > 0)
        {
            var reports = await db.Reports
                .Where(value => interruptedReportIds.Contains(value.Id)
                    && value.LastRefreshStatus == "Running")
                .ToListAsync(cancellationToken);
            foreach (var report in reports)
            {
                report.LastRefreshStatus = "Cancelled";
                report.LastRefreshCompletedAt = now;
                report.LastRefreshError = "Portal execution was interrupted by a process restart.";
                report.LastRefreshDurationMs = report.LastRefreshStartedAt is null
                    ? null
                    : (long)(now - report.LastRefreshStartedAt.Value).TotalMilliseconds;
            }
        }

        var cutoff = now - CompletedJobRetention;
        await db.PortalExecutionJobs
            .Where(value => value.CompletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private async Task PersistNewJobAsync(ExecutionJob job, string kind)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<PortalDbContext>();
        if (db is null)
            return;

        db.PortalExecutionJobs.Add(ToEntity(job, kind));
        await db.SaveChangesAsync();
    }

    private async Task<bool> TryPersistRefreshJobAsync(ExecutionJob job)
    {
        try
        {
            await PersistNewJobAsync(job, "Refresh");
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task PersistJobAsync(ExecutionJob job)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetService<PortalDbContext>();
            if (db is null)
                return;

            var stored = await db.PortalExecutionJobs.FindAsync(job.Id);
            if (stored is null)
                return;

            stored.Status = job.Status.ToString();
            stored.StartedAt = job.StartedAt;
            stored.CompletedAt = job.CompletedAt;
            stored.ManifestPath = job.ManifestPath;
            stored.Error = job.Error;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist execution job {JobId} status", job.Id);
        }
    }

    private static PortalExecutionJob ToEntity(ExecutionJob job, string kind) => new()
    {
        Id = job.Id,
        ReportId = job.ReportId,
        UserId = job.UserId,
        Kind = kind,
        Status = job.Status.ToString(),
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        ManifestPath = job.ManifestPath,
        Error = job.Error
    };

    private static ExecutionJob FromEntity(PortalExecutionJob stored)
    {
        var job = new ExecutionJob(stored.Id, stored.ReportId, stored.UserId)
        {
            Status = Enum.TryParse<JobStatus>(stored.Status, out var status) ? status : JobStatus.Failed,
            CreatedAt = stored.CreatedAt,
            StartedAt = stored.StartedAt,
            CompletedAt = stored.CompletedAt,
            ManifestPath = stored.ManifestPath,
            Error = stored.Error
        };
        return job;
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

    /// <summary>Keeps the newest <see cref="ResourcesConfig.SnapshotRetentionPerReport"/>
    /// snapshots for the report; older rows and their manifest files are removed. File
    /// deletion is restricted to names the path guard resolves inside the snapshot directory.</summary>
    internal async Task PruneSnapshotsAsync(PortalDbContext db, int reportId)
    {
        var keep = Math.Max(1, _config.Resources.SnapshotRetentionPerReport);
        var stale = await db.ReportSnapshots
            .Where(s => s.ReportId == reportId)
            .OrderByDescending(s => s.BuiltAt)
            .Skip(keep)
            .ToListAsync();
        if (stale.Count == 0) return;

        db.ReportSnapshots.RemoveRange(stale);
        await db.SaveChangesAsync();

        foreach (var snapshot in stale)
        {
            try
            {
                var key = PortalPathGuard.ToSnapshotKey(_config, snapshot.ManifestPath);
                if (key is not null)
                    await _artifacts.DeleteAsync(ArtifactArea.Snapshots, key);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to delete pruned snapshot manifest {ManifestPath}",
                    snapshot.ManifestPath);
            }
        }

        _log.LogDebug("Pruned {Count} snapshots for report {ReportId}", stale.Count, reportId);
    }

    private async Task UpdateReportRefreshStatusAsync(ExecutionJob job, string status, string? error)
    {
        // Status reporting must never take down the execution path: a transient DB failure
        // (e.g. SQLite busy) here would otherwise leak the concurrency gate or strand the job.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.FindAsync(job.ReportId);
            if (report is null) return;

            if (string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase))
            {
                report.LastRefreshStartedAt = job.StartedAt ?? DateTime.UtcNow;
                report.LastRefreshCompletedAt = null;
                report.LastRefreshDurationMs = null;
            }
            else
            {
                report.LastRefreshCompletedAt = job.CompletedAt ?? DateTime.UtcNow;
                report.LastRefreshDurationMs = job.StartedAt is null
                    ? null
                    : (long)(report.LastRefreshCompletedAt.Value - job.StartedAt.Value).TotalMilliseconds;
            }

            report.LastRefreshStatus = status;
            report.LastRefreshError = error;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Failed to record refresh status '{Status}' for report {ReportId} (job {JobId})",
                status, job.ReportId, job.Id);
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        foreach (var gate in _userGates.Values)
            gate.Dispose();
    }

    private Task SaveSnapshotManifestAsync(ReportManifest manifest, string manifestKey, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(manifest, SnapshotJsonOptions);
        return _artifacts.WriteAllTextAsync(ArtifactArea.Snapshots, manifestKey, json, ct: ct);
    }

    private static IArtifactStorage CreateDefaultArtifactStorage(PortalConfig config) =>
        new LocalArtifactStorage(new Dictionary<ArtifactArea, string>
        {
            [ArtifactArea.Scripts] = config.ScriptRootPath,
            [ArtifactArea.Snapshots] = config.SnapshotDirectory,
            [ArtifactArea.Maps] = config.MapRootPath,
            [ArtifactArea.Datasets] = config.DatasetRootPath,
            [ArtifactArea.Keys] = string.IsNullOrWhiteSpace(config.Storage.KeyRingPath)
                ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath))!, ".portal-keys")
                : Path.GetFullPath(config.Storage.KeyRingPath)
        });
}
