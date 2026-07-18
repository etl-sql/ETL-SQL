using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Storage;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Reporting;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public enum JobStatus { Pending, Running, Completed, Failed, Cancelled }

internal enum ExecutionWorkloadKind { Interactive, Refresh }

public record ExecutionJob(
    string Id,
    int ReportId,
    int UserId,
    bool IsAdministrator = false,
    bool TrustedDatasetExecution = false,
    string ActorType = "User",
    string? ActorId = null,
    string? EffectiveScopes = null,
    string? CorrelationId = null)
{
    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>
    /// When set, the report runs under this target user's row-level-security identity while
    /// <see cref="UserId"/> remains the real actor (an administrator). Impersonated runs are always
    /// executed per-request and never persist a shared snapshot. See Docs/Design/RowLevelSecurity.md.
    /// </summary>
    public int? ImpersonatedUserId { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ManifestPath { get; set; }
    public string? Error { get; set; }
    public long RowsProcessed { get; set; }
    public long PeakMemoryBytes { get; set; }
    public double CpuTimeSeconds { get; set; }
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
public class ExecutionJobService : IHostedService, INodeLeaseLossHandler, IDisposable
{
    private readonly ConcurrentDictionary<string, ExecutionJob> _jobs = new();
    private readonly ConcurrentDictionary<int, string> _activeRefreshes = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningJobCancellations = new();
    private readonly ConcurrentDictionary<string, string> _jobCancellationReasons = new();
    private readonly WeightedExecutionAdmission _admission;

    /// <summary>Per-user concurrency limiters (workload fairness, P2.6). Keyed by user id; one
    /// gate per user with <c>MaxConcurrentExecutionsPerUser</c> permits.</summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _userGates = new();

    /// <summary>Per-group concurrency limiters (workload fairness, P2.6). A user in multiple
    /// groups must acquire all group gates in sorted order before consuming a global slot.</summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _groupGates = new();
    private readonly PortalConfig _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExecutionJobService> _log;
    private readonly SessionCache _sessions;
    private readonly IJobChannel _channel;
    private readonly IArtifactStorage _artifacts;
    private readonly SnapshotPackageService _snapshotPackages;
    private readonly INodeCapacityMonitor _capacityMonitor;
    public ExecutionJobService(
        PortalConfig config,
        IServiceScopeFactory scopeFactory,
        ILogger<ExecutionJobService> log,
        SessionCache sessions,
        IJobChannel channel,
        IArtifactStorage? artifacts = null,
        INodeCapacityMonitor? capacityMonitor = null,
        SnapshotPackageService? snapshotPackages = null)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _log = log;
        _sessions = sessions;
        _channel = channel;
        _artifacts = artifacts ?? CreateDefaultArtifactStorage(config);
        _snapshotPackages = snapshotPackages ?? new SnapshotPackageService(
            config,
            _artifacts,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SnapshotPackageService>.Instance);
        _capacityMonitor = capacityMonitor ?? new NodeCapacityMonitor();
        _admission = new WeightedExecutionAdmission(
            config.Resources.MaxConcurrentReportExecutions,
            config.Resources.InteractiveExecutionWeight,
            config.Resources.RefreshExecutionWeight);
    }

    /// <summary>Terminal jobs stay queryable for this window, then are evicted so the
    /// in-memory job table cannot grow without bound on a long-running portal.</summary>
    internal static readonly TimeSpan CompletedJobRetention = TimeSpan.FromHours(24);

    /// <summary>Node-local execution workload snapshot for read-only fleet health (P2.2): how many
    /// jobs are queued (Pending) and actively running on this node. In an HA environment each node
    /// reports its own; the fleet aggregator polls per environment.</summary>
    public (int Queued, int Running) GetWorkloadCounts()
    {
        var queued = 0;
        var running = 0;
        foreach (var job in _jobs.Values)
        {
            if (job.Status == JobStatus.Pending) queued++;
            else if (job.Status == JobStatus.Running) running++;
        }
        return (queued, running);
    }

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

    public async Task<bool> CancelAsync(string jobId, string reason)
    {
        var now = DateTime.UtcNow;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<PortalDbContext>();
        if (db is null)
            return false;

        var stored = await db.PortalExecutionJobs.FindAsync(jobId);
        if (stored is null)
            return false;

        if (stored.Status is "Completed" or "Failed" or "Cancelled")
            return false;

        stored.Status = JobStatus.Cancelled.ToString();
        stored.CompletedAt = now;
        stored.Error = SecretRedactor.Redact(reason);
        await db.SaveChangesAsync();

        if (_jobs.TryGetValue(jobId, out var local))
        {
            local.Status = JobStatus.Cancelled;
            local.CompletedAt = now;
            local.Error = SecretRedactor.Redact(reason);
        }

        if (_runningJobCancellations.TryGetValue(jobId, out var cts))
        {
            _jobCancellationReasons[jobId] = reason;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        if (stored.Kind == "Refresh")
        {
            _activeRefreshes.TryRemove(new KeyValuePair<int, string>(stored.ReportId, stored.Id));
            await UpdateReportRefreshStatusAsync(FromEntity(stored), "Cancelled", reason);
        }
        return true;
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
        bool isAdministrator = false,
        string actorType = "User", string? actorId = null, string? effectiveScopes = null,
        string? correlationId = null, int? impersonatedUserId = null)
    {
        EvictExpiredJobs();
        var jobId = Guid.NewGuid().ToString("N");
        var job = new ExecutionJob(jobId, reportId, userId, IsAdministrator: isAdministrator,
            ActorType: actorType, ActorId: actorId, EffectiveScopes: effectiveScopes,
            CorrelationId: correlationId)
        {
            ImpersonatedUserId = impersonatedUserId
        };
        _jobs[jobId] = job;
        await PersistNewJobAsync(job, "Execution");

        _ = RunJobAsync(job, scriptPath, parameters, ExecutionWorkloadKind.Interactive, CancellationToken.None);
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
        bool trustedDatasetExecution = false,
        string actorType = "User", string? actorId = null, string? effectiveScopes = null,
        string? correlationId = null)
    {
        EvictExpiredJobs();

        // A scheduled/background refresh exists to update the shared snapshot. An identity-sensitive
        // report has no shared snapshot (it runs per viewer and is never cached), so a trusted
        // scheduled refresh would run under a non-interactive identity and produce nothing shareable.
        // Skip it rather than burn an execution slot. Interactive user refreshes still run under the
        // caller's own identity. See Docs/Design/RowLevelSecurity.md.
        if (trustedDatasetExecution && await ScriptReferencesIdentityAsync(scriptPath, CancellationToken.None))
        {
            _log.LogInformation(
                "Report {ReportId} is identity-sensitive; skipping scheduled refresh (no shared snapshot to update).",
                reportId);
            return string.Empty;
        }

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
            TrustedDatasetExecution: trustedDatasetExecution,
            ActorType: actorType,
            ActorId: actorId,
            EffectiveScopes: effectiveScopes,
            CorrelationId: correlationId);
        _jobs[jobId] = job;

        if (!await TryPersistRefreshJobAsync(job))
        {
            _jobs.TryRemove(jobId, out _);
            _activeRefreshes.TryRemove(new KeyValuePair<int, string>(reportId, jobId));
            return await GetActiveRefreshJobIdAsync(reportId)
                ?? throw new InvalidOperationException("The active refresh claim could not be resolved.");
        }

        _ = RunJobAsync(job, scriptPath, parameters: null, ExecutionWorkloadKind.Refresh, CancellationToken.None);
        return jobId;
    }

    private async Task RunJobAsync(
        ExecutionJob job,
        string scriptPath,
        Dictionary<string, string>? parameters,
        ExecutionWorkloadKind workloadKind,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(_config.Resources.ExecutionTimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var activity = PortalObservability.StartExecutionJobActivity(job, workloadKind.ToString());

        // Workload fairness (P2.6): a non-admin holds at most MaxConcurrentExecutionsPerUser of the
        // shared slots. Acquire the per-user slot FIRST and without holding a global permit, so a
        // capped user queues without blocking the shared pool. Administrators are exempt.
        var userGate = job.IsAdministrator ? null : GetUserGate(job.UserId);
        var userGateHeld = false;
        var groupGates = new List<SemaphoreSlim>();
        WeightedExecutionAdmission.Permit? executionPermit = null;
        try
        {
            await WaitForNodeCapacityAsync(job, cts.Token).ConfigureAwait(false);

            if (userGate is not null)
            {
                await userGate.WaitAsync(cts.Token).ConfigureAwait(false);
                userGateHeld = true;
            }

            foreach (var groupId in await GetExecutionGroupIdsAsync(job, cts.Token).ConfigureAwait(false))
            {
                var groupGate = GetGroupGate(groupId);
                await groupGate.WaitAsync(cts.Token).ConfigureAwait(false);
                groupGates.Add(groupGate);
            }

            executionPermit = await _admission.AcquireAsync(workloadKind, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            executionPermit?.Dispose();
            ReleaseGates(groupGates);
            // Only release the per-user gate if WaitAsync actually acquired it — otherwise a job that
            // timed out *while waiting* for the gate would release a permit it never held, corrupting
            // the per-user cap (and letting that user exceed it / starve the queue).
            if (userGateHeld) userGate!.Release();

            // Timed out while queued — no global permit was retained, so only the job
            // bookkeeping needs unwinding (the refresh debounce must clear).
            job.Error = "Execution timed out while waiting for an execution slot";
            job.CompletedAt = DateTime.UtcNow;
            job.Status = JobStatus.Cancelled;
            _activeRefreshes.TryRemove(new KeyValuePair<int, string>(job.ReportId, job.Id));
            await PersistJobAsync(job);
            await UpdateReportRefreshStatusAsync(job, "Cancelled", job.Error);
            PortalObservability.CompleteExecutionJobActivity(activity, job, workloadKind: workloadKind.ToString());
            _log.LogWarning("Execution job {JobId} cancelled while queued for an execution slot", job.Id);
            return;
        }
        catch
        {
            executionPermit?.Dispose();
            ReleaseGates(groupGates);
            if (userGateHeld) userGate!.Release();
            throw;
        }

        Task? cancellationMonitor = null;
        try
        {
            if (await ApplyPersistedCancellationAsync(job.Id, cts).ConfigureAwait(false))
                throw new OperationCanceledException(cts.Token);

            _runningJobCancellations[job.Id] = cts;
            cancellationMonitor = MonitorPersistedCancellationAsync(job.Id, cts);

            job.Status = JobStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            await PersistJobAsync(job);
            await UpdateReportRefreshStatusAsync(job, "Running", null);
            _log.LogInformation("Execution job {JobId} started for report {ReportId}", job.Id, job.ReportId);

            if (!PortalPathGuard.TryResolveScript(_config, scriptPath, out var resolvedScriptPath))
                throw new UnauthorizedAccessException("Report script path is outside the configured script root.");
            scriptPath = resolvedScriptPath;

            var manifestKey = SnapshotPackageService.BuildSnapshotKey(job.ReportId, job.Id);
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
                activity?.SetTag(PortalObservability.Tags.ScriptHash, runTimeHash);
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
                        ApplyExecutionMetrics(job, status);
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
                var process = System.Diagnostics.Process.GetCurrentProcess();
                process.Refresh();
                var startCpuSeconds = process.TotalProcessorTime.TotalSeconds;
                // Use an independent DashboardService for snapshots (not the session cache).
                // Interactive execution and user-triggered refresh retain the caller identity.
                // Only the orchestrator poller explicitly creates trusted scheduled refreshes.
                var dashboardTimeout = TimeSpan.FromSeconds(Math.Max(1, _config.Resources.ExecutionTimeoutSeconds));
                var executionIdentity = await BuildExecutionIdentityAsync(job, cts.Token);
                await using var svc = new ETL_SQL.ReportHosting.DashboardService(
                    scriptPath,
                    _scopeFactory,
                    dashboardTimeout,
                    job.DatasetCallerContext,
                    job.ReportId,
                    _config.Dataset.AtRestKey,
                    executionIdentity);

                if (parameters is { Count: > 0 })
                    await svc.SetParametersAsync(parameters.Select(kv => (kv.Key, kv.Value)));

                var manifest = await svc.RebuildAsync().WaitAsync(cts.Token);
                process.Refresh();
                job.RowsProcessed = manifest.Telemetry?.RowsProcessed ?? 0;
                job.PeakMemoryBytes = process.PeakWorkingSet64;
                job.CpuTimeSeconds = Math.Max(0, process.TotalProcessorTime.TotalSeconds - startCpuSeconds);
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

            // Row-level security: an identity-sensitive report must never persist a shared snapshot,
            // because the view path serves the latest snapshot to any viewer regardless of who built
            // it — which would leak one user's row-filtered result to another. The executor still gets
            // their own result via the per-job manifest artifact. See Docs/Design/RowLevelSecurity.md.
            // Impersonated runs are always per-request previews under a substituted identity and must
            // never become a shared snapshot, regardless of whether the script references identity.
            var identitySensitive = job.ImpersonatedUserId is not null
                || await ScriptReferencesIdentityAsync(scriptPath, cts.Token);
            if (identitySensitive)
            {
                _log.LogInformation(
                    "Report {ReportId} is identity-sensitive; skipping shared snapshot (per-viewer execution only).",
                    job.ReportId);
            }
            else
            {
                db.ReportSnapshots.Add(new ReportSnapshot
                {
                    ReportId = job.ReportId,
                    ManifestPath = manifestPath,
                    BuiltAt = DateTime.UtcNow,
                    BuiltBy = job.UserId,
                    ParametersJson = parameters is { Count: > 0 }
                        ? System.Text.Json.JsonSerializer.Serialize(
                            parameters.ToDictionary(kv => kv.Key, kv => SecretRedactor.MaskIfSensitive(kv.Key, kv.Value)))
                        : null,
                    ScriptHashAtRunTime = runTimeHash,
                    HashMatched = hashMatched
                });
            }

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
            PortalObservability.CompleteExecutionJobActivity(activity, job, workloadKind: workloadKind.ToString(), scriptHash: runTimeHash);
            _log.LogInformation("Execution job {JobId} completed", job.Id);

        }
        catch (OperationCanceledException)
        {
            job.Error = _jobCancellationReasons.TryRemove(job.Id, out var reason)
                ? reason
                : "Execution timed out or was cancelled";
            job.CompletedAt = DateTime.UtcNow;
            job.Status = JobStatus.Cancelled;
            await PersistJobAsync(job);
            await UpdateReportRefreshStatusAsync(job, "Cancelled", job.Error);
            PortalObservability.CompleteExecutionJobActivity(activity, job, workloadKind: workloadKind.ToString());
            _log.LogWarning("Execution job {JobId} cancelled/timed out", job.Id);
        }
        catch (Exception ex)
        {
            job.Error = SecretRedactor.Redact(ex.Message);
            job.CompletedAt = DateTime.UtcNow;
            job.Status = JobStatus.Failed;
            await PersistJobAsync(job);
            await UpdateReportRefreshStatusAsync(job, "Failed", job.Error);
            PortalObservability.CompleteExecutionJobActivity(activity, job, workloadKind: workloadKind.ToString());
            _log.LogError("Execution job {JobId} failed: {Message}. StackTrace: {Stack}",
                job.Id, job.Error, SecretRedactor.Redact(ex.StackTrace));
        }
        finally
        {
            if (!cts.IsCancellationRequested)
                cts.Cancel();
            _runningJobCancellations.TryRemove(job.Id, out _);
            _jobCancellationReasons.TryRemove(job.Id, out _);
            executionPermit?.Dispose();
            ReleaseGates(groupGates);
            if (userGateHeld) userGate!.Release();
            _activeRefreshes.TryRemove(new KeyValuePair<int, string>(job.ReportId, job.Id));
        }

        if (cancellationMonitor is not null)
            await cancellationMonitor.ConfigureAwait(false);
    }

    private async Task MonitorPersistedCancellationAsync(string jobId, CancellationTokenSource cts)
    {
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(250, cts.Token).ConfigureAwait(false);
                if (await ApplyPersistedCancellationAsync(jobId, cts).ConfigureAwait(false))
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // The local run finished or was cancelled through another path.
        }
    }

    private async Task<bool> ApplyPersistedCancellationAsync(string jobId, CancellationTokenSource cts)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<PortalDbContext>();
        if (db is null)
            return false;

        var stored = await db.PortalExecutionJobs.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == jobId, cts.Token)
            .ConfigureAwait(false);
        if (stored?.Status != JobStatus.Cancelled.ToString())
            return false;

        _jobCancellationReasons[jobId] = stored.Error ?? "Execution was cancelled.";
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        return true;
    }

    public Task OnNodeLeaseLostAsync(string nodeId, string role, string reason, CancellationToken ct)
    {
        var cancelled = CancelLocalRunningJobs(
            $"Portal node lease was lost ({nodeId}, role={role}): {reason}");
        if (cancelled > 0)
            _log.LogError("Cancelled {Count} local execution job(s) after node lease loss.", cancelled);
        return Task.CompletedTask;
    }

    internal int CancelLocalRunningJobs(string reason)
    {
        var cancelled = 0;
        foreach (var (jobId, cts) in _runningJobCancellations)
        {
            if (cts.IsCancellationRequested)
                continue;

            _jobCancellationReasons[jobId] = reason;
            try
            {
                cts.Cancel();
                cancelled++;
            }
            catch (ObjectDisposedException)
            {
                _jobCancellationReasons.TryRemove(jobId, out _);
            }
        }

        return cancelled;
    }

    private SemaphoreSlim GetUserGate(int userId)
    {
        var limit = Math.Max(1, _config.Resources.MaxConcurrentExecutionsPerUser);
        return _userGates.GetOrAdd(userId, _ => new SemaphoreSlim(limit, limit));
    }

    private SemaphoreSlim GetGroupGate(int groupId)
    {
        var limit = Math.Max(1, _config.Resources.MaxConcurrentExecutionsPerGroup);
        return _groupGates.GetOrAdd(groupId, _ => new SemaphoreSlim(limit, limit));
    }

    private async Task<IReadOnlyList<int>> GetExecutionGroupIdsAsync(ExecutionJob job, CancellationToken ct)
    {
        if (job.IsAdministrator || _config.Resources.MaxConcurrentExecutionsPerGroup <= 0)
            return [];

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<PortalDbContext>();
        if (db is null)
            return [];

        return await db.UserGroups
            .AsNoTracking()
            .Where(value => value.UserId == job.UserId)
            .Select(value => value.GroupId)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(ct);
    }

    private static void ReleaseGates(List<SemaphoreSlim> gates)
    {
        for (var i = gates.Count - 1; i >= 0; i--)
            gates[i].Release();
        gates.Clear();
    }

    private async Task WaitForNodeCapacityAsync(ExecutionJob job, CancellationToken ct)
    {
        var logged = false;
        while (true)
        {
            var capacity = _capacityMonitor.Capture();
            if (!capacity.IsOverloaded)
                return;

            if (!logged)
            {
                _log.LogWarning(
                    "Execution job {JobId}: waiting because this portal node is overloaded (CPU={Cpu:F1}%, memory={Memory:F1}%).",
                    job.Id, capacity.ProcessCpuPercent, capacity.MemoryLoadPercent);
                logged = true;
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
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
                    && (value.LastRefreshStatus == "Running" || value.LastRefreshStatus == null))
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
            stored.RowsProcessed = job.RowsProcessed;
            stored.PeakMemoryBytes = job.PeakMemoryBytes;
            stored.CpuTimeSeconds = job.CpuTimeSeconds;
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
        ActorType = job.ActorType,
        ActorId = job.ActorId,
        EffectiveScopes = job.EffectiveScopes,
        CorrelationId = job.CorrelationId,
        Kind = kind,
        Status = job.Status.ToString(),
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        ManifestPath = job.ManifestPath,
        Error = job.Error,
        RowsProcessed = job.RowsProcessed,
        PeakMemoryBytes = job.PeakMemoryBytes,
        CpuTimeSeconds = job.CpuTimeSeconds
    };

    private static ExecutionJob FromEntity(PortalExecutionJob stored)
    {
        var job = new ExecutionJob(stored.Id, stored.ReportId, stored.UserId,
            ActorType: stored.ActorType,
            ActorId: stored.ActorId,
            EffectiveScopes: stored.EffectiveScopes,
            CorrelationId: stored.CorrelationId)
        {
            Status = Enum.TryParse<JobStatus>(stored.Status, out var status) ? status : JobStatus.Failed,
            CreatedAt = stored.CreatedAt,
            StartedAt = stored.StartedAt,
            CompletedAt = stored.CompletedAt,
            ManifestPath = stored.ManifestPath,
            Error = stored.Error,
            RowsProcessed = stored.RowsProcessed,
            PeakMemoryBytes = stored.PeakMemoryBytes,
            CpuTimeSeconds = stored.CpuTimeSeconds
        };
        return job;
    }

    private static void ApplyExecutionMetrics(ExecutionJob job, ETL_SQL.Orchestrator.Channels.JobStatusResponse status)
    {
        job.RowsProcessed = status.RowsProcessed;
        job.PeakMemoryBytes = status.PeakMemoryBytes;
        job.CpuTimeSeconds = status.CpuTimeSeconds;
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
        CancelLocalRunningJobs("Execution service is shutting down.");
        foreach (var gate in _userGates.Values)
            gate.Dispose();
    }

    private Task SaveSnapshotManifestAsync(ReportManifest manifest, string manifestKey, CancellationToken ct)
    {
        return _snapshotPackages.SaveAsync(manifest, manifestKey, ct);
    }

    /// <summary>
    /// Does the script reference any row-level-security identity variable/function? Reads the file and
    /// applies the shared conservative scan; on read failure fails safe (treat as identity-sensitive,
    /// so no shared snapshot is produced).
    /// </summary>
    private static async Task<bool> ScriptReferencesIdentityAsync(string scriptPath, CancellationToken ct)
    {
        try
        {
            return ETL_SQL.Core.Governance.RowLevelSecurityScan.ReferencesIdentity(
                await File.ReadAllTextAsync(scriptPath, ct));
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Builds the row-level-security identity for an execution from the job's user (username, roles,
    /// and groups from the portal database). Returns null when the user cannot be resolved, so
    /// identity-sensitive reports fail closed. See Docs/Design/RowLevelSecurity.md.
    /// </summary>
    private async Task<ETL_SQL.Core.Governance.ExecutionIdentity?> BuildExecutionIdentityAsync(
        ExecutionJob job, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

            // Under impersonation the effective identity (whose rows are shown) is the target user;
            // the real actor stays the administrator. Row-level predicates and the admin bypass key
            // off the *effective* (target) identity, so impersonating a non-admin shows that user's
            // restricted view. Permission to run the report was already gated on the real admin.
            var effectiveUserId = job.ImpersonatedUserId ?? job.UserId;

            var effective = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == effectiveUserId, ct);
            if (effective is null) return null;

            var roles = await (from ur in db.UserRoles
                               join r in db.Roles on ur.RoleId equals r.Id
                               where ur.UserId == effectiveUserId && r.Name != null
                               select r.Name!).ToListAsync(ct);
            var groups = await (from ug in db.UserGroups
                                join g in db.Groups on ug.GroupId equals g.Id
                                where ug.UserId == effectiveUserId
                                select g.Name).ToListAsync(ct);

            var effectiveName = effective.UserName ?? effectiveUserId.ToString();
            var realName = effectiveName;
            if (job.ImpersonatedUserId is not null)
            {
                var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == job.UserId, ct);
                realName = actor?.UserName ?? job.UserId.ToString();
            }

            // IsAdmin reflects the effective identity, not the real actor, so an admin impersonating a
            // normal user does not carry admin bypass into the target's view.
            var effectiveIsAdmin = job.ImpersonatedUserId is null
                ? job.IsAdministrator
                : roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);

            return new ETL_SQL.Core.Governance.ExecutionIdentity
            {
                EffectiveUser = effectiveName,
                EffectiveUserId = effectiveUserId,
                RealUser = realName,
                IsAdmin = effectiveIsAdmin,
                AdminBypassesRowLevelSecurity = _config.Security.AdminBypassRowLevelSecurity,
                Groups = groups,
                Roles = roles
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Failed to build execution identity for user {UserId}; identity-sensitive reports will fail closed.",
                job.UserId);
            return null;
        }
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

internal sealed class WeightedExecutionAdmission
{
    private readonly object _sync = new();
    private readonly Queue<Waiter> _interactive = new();
    private readonly Queue<Waiter> _refresh = new();
    private readonly ExecutionWorkloadKind[] _cycle;
    private int _nextCycleIndex;
    private int _available;

    public WeightedExecutionAdmission(int maxConcurrent, int interactiveWeight, int refreshWeight)
    {
        _available = Math.Max(1, maxConcurrent);
        var cycle = new List<ExecutionWorkloadKind>();
        for (var i = 0; i < Math.Max(1, interactiveWeight); i++)
            cycle.Add(ExecutionWorkloadKind.Interactive);
        for (var i = 0; i < Math.Max(1, refreshWeight); i++)
            cycle.Add(ExecutionWorkloadKind.Refresh);
        _cycle = cycle.ToArray();
    }

    public async Task<Permit> AcquireAsync(ExecutionWorkloadKind kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Waiter waiter;
        List<Waiter> grants;

        lock (_sync)
        {
            if (_available > 0 && !HasQueuedWorkLocked())
            {
                _available--;
                return new Permit(this);
            }

            waiter = new Waiter(this);
            QueueFor(kind).Enqueue(waiter);
            grants = DispatchLocked();
        }

        GrantAll(grants);
        using var registration = ct.Register(static state =>
        {
            var tuple = ((WeightedExecutionAdmission Admission, Waiter Waiter, CancellationToken Token))state!;
            tuple.Admission.Cancel(tuple.Waiter, tuple.Token);
        }, (this, waiter, ct));

        return await waiter.Task.ConfigureAwait(false);
    }

    private void Release()
    {
        List<Waiter> grants;
        lock (_sync)
        {
            _available++;
            grants = DispatchLocked();
        }

        GrantAll(grants);
    }

    private void Cancel(Waiter waiter, CancellationToken ct)
    {
        List<Waiter> grants;
        lock (_sync)
        {
            if (waiter.Granted)
                return;

            waiter.Cancelled = true;
            waiter.TrySetCancelled(ct);
            grants = DispatchLocked();
        }

        GrantAll(grants);
    }

    private List<Waiter> DispatchLocked()
    {
        var grants = new List<Waiter>();
        while (_available > 0)
        {
            var waiter = DequeueNextLocked();
            if (waiter is null)
                break;
            if (waiter.Cancelled)
                continue;

            waiter.Granted = true;
            _available--;
            grants.Add(waiter);
        }

        return grants;
    }

    private Waiter? DequeueNextLocked()
    {
        for (var i = 0; i < _cycle.Length; i++)
        {
            var kind = _cycle[_nextCycleIndex];
            _nextCycleIndex = (_nextCycleIndex + 1) % _cycle.Length;
            if (TryDequeueLiveLocked(kind, out var waiter))
                return waiter;
        }

        return TryDequeueLiveLocked(ExecutionWorkloadKind.Interactive, out var interactive)
            ? interactive
            : TryDequeueLiveLocked(ExecutionWorkloadKind.Refresh, out var refresh)
                ? refresh
                : null;
    }

    private bool TryDequeueLiveLocked(ExecutionWorkloadKind kind, out Waiter? waiter)
    {
        var queue = QueueFor(kind);
        while (queue.Count > 0)
        {
            waiter = queue.Dequeue();
            if (!waiter.Cancelled)
                return true;
        }

        waiter = null;
        return false;
    }

    private bool HasQueuedWorkLocked() =>
        _interactive.Any(value => !value.Cancelled)
        || _refresh.Any(value => !value.Cancelled);

    private Queue<Waiter> QueueFor(ExecutionWorkloadKind kind) =>
        kind == ExecutionWorkloadKind.Interactive ? _interactive : _refresh;

    private static void GrantAll(List<Waiter> grants)
    {
        foreach (var waiter in grants)
            waiter.TrySetGranted();
    }

    private sealed class Waiter(WeightedExecutionAdmission owner)
    {
        private readonly TaskCompletionSource<Permit> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; set; }
        public bool Granted { get; set; }
        public Task<Permit> Task => _tcs.Task;
        public void TrySetGranted() => _tcs.TrySetResult(new Permit(owner));
        public void TrySetCancelled(CancellationToken ct) => _tcs.TrySetCanceled(ct);
    }

    public sealed class Permit : IDisposable
    {
        private WeightedExecutionAdmission? _owner;

        internal Permit(WeightedExecutionAdmission owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}
