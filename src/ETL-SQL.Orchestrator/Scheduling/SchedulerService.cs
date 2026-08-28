using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Scheduling;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Scheduling
{
    public enum ManualTriggerResult
    {
        Accepted,
        JobNotFound,
        AlreadyRunning
    }

    public enum ResumeTriggerStatus
    {
        Accepted,
        RunNotFound,
        JobNotFound,
        NotResumable,
        AlreadyRunning
    }

    public sealed record ResumeTriggerResult(
        ResumeTriggerStatus Status,
        string? CheckpointLabel = null,
        string? Reason = null);

    /// <summary>
    /// Background service that manages the scheduling and execution of automated ETL-SQL jobs.
    /// Concurrency is limited by <see cref="JobThrottle"/> — jobs beyond the cap are queued
    /// and executed as slots become available.
    /// </summary>
    public class SchedulerService : IJobManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IJobHistoryStore _store;
        private readonly ILogger<SchedulerService> _logger;
        private readonly IConfiguration _configuration;
        private readonly JobThrottle _throttle;
        private readonly ETL_SQL.Core.Execution.ISessionStateManager _sessionManager;
        private readonly INodeCapacityMonitor _capacityMonitor;
        private readonly ITenantMeteringLedger? _meteringLedger;
        private CancellationTokenSource? _cts;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, CancellationTokenSource> _runningJobs = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _scheduledJobStarts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Identifies this scheduler instance as a lease owner (P1.1). Unique per process
        /// start so a restarted instance never silently inherits its previous leases.</summary>
        private readonly string _leaseOwnerId =
            $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        public SchedulerService(IServiceProvider serviceProvider, IJobHistoryStore store,
            ILogger<SchedulerService> logger, JobThrottle throttle, IConfiguration configuration,
            ETL_SQL.Core.Execution.ISessionStateManager sessionManager,
            INodeCapacityMonitor? capacityMonitor = null,
            ITenantMeteringLedger? meteringLedger = null)
        {
            _serviceProvider = serviceProvider;
            _store = store;
            _logger = logger;
            _throttle = throttle;
            _configuration = configuration;
            _sessionManager = sessionManager;
            _capacityMonitor = capacityMonitor ?? new NodeCapacityMonitor();
            _meteringLedger = meteringLedger;
        }

        /// <summary>Process-unique owner id for the maintenance lease, matching the heartbeat's form.</summary>
        private readonly string _maintenanceOwner =
            $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        /// <summary>Lease name for the periodic history/metrics maintenance pass.</summary>
        internal const string MaintenanceLockName = "orchestrator:history-maintenance";

        /// <summary>Returns a snapshot of current concurrency metrics.</summary>
        public JobThrottleMetrics GetMetrics() => _throttle.GetMetrics();

        private DateTime _lastMetricsLog = DateTime.MinValue;
        private DateTime _lastSessionReap = DateTime.MinValue;
        private DateTime _lastHistoryPrune = DateTime.MinValue;
        private Task? _runTask;

        /// <summary>Starts the background scheduler loop.</summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            _lastMetricsLog = DateTime.Now;
            _lastSessionReap = DateTime.Now;
            _lastHistoryPrune = DateTime.Now;
            _runTask = Task.Run(() => RunAsync(_cts.Token));
            _ = _runTask.ContinueWith(t =>
                _logger.LogError(t.Exception, "Scheduler background task terminated unexpectedly."),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _runTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ae)
            {
                ae.Handle(ex => ex is OperationCanceledException || ex is TaskCanceledException);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for scheduler background task to terminate.");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            _cts?.Cancel();
            try
            {
                if (_runTask != null)
                {
                    await _runTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for scheduler background task to terminate.");
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            _logger.LogInformation("Scheduler service started.");

            try
            {
                await _store.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize job history store.");
                return;
            }

            // Startup recovery: a previous crash may have left RUNNING rows with no completion write.
            // Reconcile any older than the max job runtime to INTERRUPTED so they are not stuck RUNNING
            // forever (unprunable and invisible to failure reporting). Self-healing per the store contract.
            try
            {
                int maxRuntimeHours = _configuration.GetValue<int>("Orchestrator:MaxJobRuntimeHours", 24);
                if (maxRuntimeHours > 0)
                {
                    int reconciled = await _store.ReconcileStaleRunningAsync(TimeSpan.FromHours(maxRuntimeHours));
                    if (reconciled > 0)
                        _logger.LogWarning("Marked {Count} orphaned RUNNING job-history row(s) as INTERRUPTED on startup.", reconciled);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Startup reconciliation of orphaned RUNNING job-history rows failed.");
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var dueJobs = _store is IJobScheduleQueryStore scheduleQueryStore
                        ? await scheduleQueryStore.GetDueJobsAsync(now)
                        : (await _store.GetActiveJobsAsync()).Where(job => job.NextRun == null || job.NextRun <= now);

                    // Cron-scheduled jobs come from their schedule links; the query above covers only
                    // jobs with no schedule attached. The two sets are disjoint by construction, so
                    // there is nothing to de-duplicate between them.
                    if (_store is IJobCatalogStore catalog)
                        dueJobs = dueJobs.Concat(await catalog.GetJobsDueByScheduleAsync(DateTime.UtcNow));

                    foreach (var job in dueJobs)
                    {
                        if (!_scheduledJobStarts.TryAdd(job.Name, 0))
                        {
                            continue;
                        }

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ExecuteJobAsync(job);
                            }
                            finally
                            {
                                _scheduledJobStarts.TryRemove(job.Name, out _);
                            }
                        }, CancellationToken.None);
                    }

                    // Resolve intervals from configuration with safe defaults
                    int metricsIntervalSeconds = _configuration.GetValue<int>("Scheduler:MetricsIntervalSeconds", 60);
                    int sleepIntervalSeconds = _configuration.GetValue<int>("Scheduler:SleepIntervalSeconds", 30);

                    // 8B-1: Periodic metrics emission
                    if (now - _lastMetricsLog >= TimeSpan.FromSeconds(metricsIntervalSeconds))
                    {
                        var metrics = GetMetrics();
                        _logger.LogInformation("Orchestrator Metrics: ActiveJobs={Active}, QueuedJobs={Queued}, MaxConcurrent={Max}, AvailableSlots={Slots}",
                            metrics.ActiveJobs, metrics.QueuedJobs, metrics.MaxJobs, metrics.AvailableSlots);
                        _lastMetricsLog = now;
                    }

                    // 8B-2: Periodic session reaping
                    int reapIntervalMinutes = _configuration.GetValue<int>("Scheduler:SessionReapIntervalMinutes", 60);
                    if (now - _lastSessionReap >= TimeSpan.FromMinutes(reapIntervalMinutes))
                    {
                        int retentionDays = _configuration.GetValue<int>("Session:StaleSessionRetentionDays", 7);
                        _logger.LogInformation("Orchestrator: Executing periodic session reap (stale_days={Days})", retentionDays);
                        _sessionManager.ReapStaleSessions(TimeSpan.FromDays(retentionDays));
                        _lastSessionReap = now;
                    }

                    // Periodic history maintenance. Parent-history and statement-detail retention
                    // are independent: a deployment may keep run summaries indefinitely while
                    // pruning the much denser statement timeline.
                    int historyPruneIntervalMinutes = _configuration.GetValue<int>("Scheduler:HistoryPruneIntervalMinutes", 360);
                    int historyRetentionDays = _configuration.GetValue<int>("Orchestrator:JobHistoryRetentionDays", 30);
                    if (now - _lastHistoryPrune >= TimeSpan.FromMinutes(historyPruneIntervalMinutes))
                    {
                        // Reconcile orphaned/hung RUNNING rows first so they become prunable and visible
                        // to failure reporting, then prune old terminal rows.
                        int maxRuntimeHours = _configuration.GetValue<int>("Orchestrator:MaxJobRuntimeHours", 24);
                        // Roll-up and pruning rewrite shared state. Behind a load balancer every
                        // node reaches this on its own timer, so without a lease they run
                        // concurrently against one database — duplicating roll-up work and racing
                        // each other's deletes. A node that loses the race skips this cycle rather
                        // than waiting: the next pass is minutes away, and blocking a scheduler
                        // loop to do maintenance twice is worse than not doing it now.
                        var maintenanceLocks = _store as IClusterLockStore;
                        var maintenanceLease = TimeSpan.FromMinutes(
                            _configuration.GetValue<int>("Scheduler:MaintenanceLeaseMinutes", 15));
                        var holdsMaintenanceLease = maintenanceLocks is null
                            || await maintenanceLocks.TryAcquireLockAsync(
                                MaintenanceLockName, _maintenanceOwner, maintenanceLease);

                        if (!holdsMaintenanceLease)
                        {
                            _logger.LogDebug(
                                "Skipping history maintenance this cycle; another node holds '{Lock}'.",
                                MaintenanceLockName);
                        }
                        else
                            try
                            {
                                if (maxRuntimeHours > 0)
                                {
                                    int reconciled = await _store.ReconcileStaleRunningAsync(TimeSpan.FromHours(maxRuntimeHours));
                                    if (reconciled > 0)
                                        _logger.LogWarning("Marked {Count} RUNNING job-history row(s) exceeding the max runtime ({Hours}h) as INTERRUPTED.", reconciled, maxRuntimeHours);
                                }

                                // Roll up BEFORE pruning raw rows, so daily trend captures rows about to
                                // age out. Daily summaries are retained far longer than raw history/samples.
                                var metricsStore = _store as IHostMetricsStore;
                                int rollupRetentionDays = _configuration.GetValue<int>("Orchestrator:HistoryRollupRetentionDays", 400);
                                await _store.RollUpJobHistoryAsync();
                                if (metricsStore != null) await metricsStore.RollUpHostMetricsAsync();
                                if (rollupRetentionDays > 0)
                                {
                                    await _store.PruneJobHistoryDailyAsync(TimeSpan.FromDays(rollupRetentionDays));
                                    if (metricsStore != null) await metricsStore.PruneHostMetricsDailyAsync(TimeSpan.FromDays(rollupRetentionDays));
                                }

                                if (historyRetentionDays > 0)
                                {
                                    int pruned = await _store.PruneHistoryAsync(TimeSpan.FromDays(historyRetentionDays));
                                    if (pruned > 0)
                                        _logger.LogInformation("Orchestrator: pruned {Count} job-history row(s) older than {Days} day(s).", pruned, historyRetentionDays);
                                }

                                int successfulStatementRetentionDays = _configuration.GetValue<int>(
                                    "Orchestrator:SuccessfulStatementMetricsRetentionDays", 7);
                                int failedStatementRetentionDays = _configuration.GetValue<int>(
                                    "Orchestrator:FailedStatementMetricsRetentionDays", 30);
                                if (successfulStatementRetentionDays > 0 && failedStatementRetentionDays > 0)
                                {
                                    int prunedStatements = await _store.PruneStatementMetricsAsync(
                                        TimeSpan.FromDays(successfulStatementRetentionDays),
                                        TimeSpan.FromDays(failedStatementRetentionDays));
                                    if (prunedStatements > 0)
                                    {
                                        _logger.LogInformation(
                                            "Orchestrator: pruned {Count} statement-metric row(s) using success={SuccessDays}d and failure={FailureDays}d retention.",
                                            prunedStatements,
                                            successfulStatementRetentionDays,
                                            failedStatementRetentionDays);
                                    }
                                }

                                // Host-metrics samples are dense; retain them shorter than job history and
                                // rely on the roll-up for long-term trend. Same store implements both.
                                int hostMetricsRetentionDays = _configuration.GetValue<int>("Orchestrator:HostMetricsRetentionDays", 14);
                                if (hostMetricsRetentionDays > 0 && metricsStore != null)
                                {
                                    int prunedMetrics = await metricsStore.PruneHostMetricsAsync(TimeSpan.FromDays(hostMetricsRetentionDays));
                                    if (prunedMetrics > 0)
                                        _logger.LogInformation("Orchestrator: pruned {Count} host-metrics sample(s) older than {Days} day(s).", prunedMetrics, hostMetricsRetentionDays);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Job-history maintenance failed; will retry next cycle.");
                            }
                            finally
                            {
                                // Released rather than left to expire, so the next cycle is not blocked
                                // for the remainder of the lease if this node finishes early.
                                if (maintenanceLocks is not null)
                                {
                                    try { await maintenanceLocks.ReleaseLockAsync(MaintenanceLockName, _maintenanceOwner); }
                                    catch (Exception releaseEx)
                                    {
                                        _logger.LogDebug(releaseEx,
                                            "Could not release '{Lock}'; it will expire on its lease.",
                                            MaintenanceLockName);
                                    }
                                }
                            }
                        _lastHistoryPrune = now;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(sleepIntervalSeconds), ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in scheduler loop.");
                    // Safety sleep on error to avoid tight loops
                    await Task.Delay(_configuration.GetValue<int>("Scheduler:ErrorSleepMs", 5000), ct);
                }
            }
            _logger.LogInformation("Scheduler service stopped.");
        }

        /// <summary>Enqueues an immediate out-of-schedule execution for an existing job.</summary>
        public async Task<bool> TriggerJobAsync(JobId jobId) =>
            await TriggerJobWithOverridesAsync(jobId, null) != ManualTriggerResult.JobNotFound;

        /// <summary>
        /// Enqueues a one-run manual execution with optional input-variable overrides. Addressed by
        /// identity: the caller has already resolved and authorized a specific job, and re-resolving
        /// its name here could land on a different tenant's job of the same name.
        /// </summary>
        public async Task<ManualTriggerResult> TriggerJobWithOverridesAsync(
            JobId jobId,
            IReadOnlyDictionary<string, string>? variableOverrides)
        {
            var job = await _store.GetJobByIdAsync(jobId);
            if (job == null) return ManualTriggerResult.JobNotFound;

            // The request object belongs to the HTTP scope. Copy it before the detached task so a
            // caller cannot mutate values while the job waits for a throttle slot or retry.
            IReadOnlyDictionary<string, string>? capturedOverrides = variableOverrides is { Count: > 0 }
                ? new Dictionary<string, string>(variableOverrides, StringComparer.OrdinalIgnoreCase)
                : null;

            // Same start guard as the scheduling loop: one execution of a job at a time. A
            // trigger racing an in-flight run coalesces with it instead of starting a duplicate.
            if (!_scheduledJobStarts.TryAdd(job.Name, 0))
            {
                _logger.LogInformation(
                    "Job {JobName}: manual trigger rejected because an execution is already in progress.",
                    job.Name);
                return ManualTriggerResult.AlreadyRunning;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteJobAsync(job, capturedOverrides);
                }
                finally
                {
                    _scheduledJobStarts.TryRemove(job.Name, out _);
                }
            }, CancellationToken.None);
            return ManualTriggerResult.Accepted;
        }

        /// <summary>Resumes one failed persistent run from its last completed named checkpoint.</summary>
        public async Task<ResumeTriggerResult> ResumeJobAsync(long historyId)
        {
            var history = await _store.GetHistoryEntryAsync(historyId);
            if (history is null)
                return new(ResumeTriggerStatus.RunNotFound, Reason: "Run history was not found.");

            if (!IsResumeFailureStatus(history.Status))
                return new(ResumeTriggerStatus.NotResumable,
                    Reason: "Only failed or cancelled runs can be resumed.");
            if (string.IsNullOrWhiteSpace(history.SessionId))
                return new(ResumeTriggerStatus.NotResumable,
                    Reason: "This run was not a persistent session.");
            if (string.IsNullOrWhiteSpace(history.CheckpointLabel))
                return new(ResumeTriggerStatus.NotResumable,
                    Reason: "This run never reached an author-declared checkpoint.");

            var state = await _sessionManager.LoadSession(history.SessionId);
            if (state is null)
                return new(ResumeTriggerStatus.NotResumable,
                    history.CheckpointLabel,
                    "The saved checkpoint has expired or is unavailable on this deployment.");
            if (!TryReadCheckpointLabel(state, out var persistedLabel) ||
                !string.Equals(persistedLabel, history.CheckpointLabel, StringComparison.OrdinalIgnoreCase))
                return new(ResumeTriggerStatus.NotResumable,
                    history.CheckpointLabel,
                    "The saved session no longer contains the recorded checkpoint.");

            // By id, not by the name the run recorded: a resume must return to the same job, never
            // to whatever object happens to hold that name now.
            var job = history.JobId.IsAssigned
                ? await _store.GetJobByIdAsync(history.JobId)
                : null;
            if (job is null)
                return new(ResumeTriggerStatus.JobNotFound,
                    history.CheckpointLabel,
                    "The saved job no longer exists.");

            try
            {
                var parsed = new Parser(new Lexer(job.Script).Tokenize(), job.Script).Parse();
                if (parsed.Diagnostics.Any(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error))
                    return new(ResumeTriggerStatus.NotResumable,
                        history.CheckpointLabel,
                        "The current saved script has parse errors and cannot be resumed.");
                if (!parsed.Statements.OfType<SectionLabelStatement>().Any(label =>
                        label.IsTopLevel && label.LabelName.Equals(
                            history.CheckpointLabel, StringComparison.OrdinalIgnoreCase)))
                    return new(ResumeTriggerStatus.NotResumable,
                        history.CheckpointLabel,
                        "The current saved script no longer contains the recorded checkpoint label.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job {JobName}: could not validate resume checkpoint {Checkpoint}.",
                    job.Name, history.CheckpointLabel);
                return new(ResumeTriggerStatus.NotResumable,
                    history.CheckpointLabel,
                    "The current saved script could not be validated for checkpoint resume.");
            }

            if (!_scheduledJobStarts.TryAdd(job.Name, 0))
                return new(ResumeTriggerStatus.AlreadyRunning,
                    history.CheckpointLabel,
                    "The job already has an execution in progress.");

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteJobAsync(
                        job, initialSessionId: history.SessionId, resumeFromCheckpoint: true);
                }
                finally
                {
                    _scheduledJobStarts.TryRemove(job.Name, out _);
                }
            }, CancellationToken.None);

            return new(ResumeTriggerStatus.Accepted, history.CheckpointLabel);
        }

        /// <summary>Kills a running job instance by its HistoryId.</summary>
        public bool KillJob(long historyId)
        {
            if (_runningJobs.TryGetValue(historyId, out var cts))
            {
                cts.Cancel();
                _logger.LogInformation("Cancelled job history record {HistoryId}.", historyId);
                return true;
            }
            _logger.LogWarning("Attempted to kill job {HistoryId} but it was not found as a running job.", historyId);
            return false;
        }

        private async Task ExecuteJobAsync(
            JobDefinition job,
            IReadOnlyDictionary<string, string>? variableOverrides = null,
            string? initialSessionId = null,
            bool resumeFromCheckpoint = false)
        {
            var capacity = _capacityMonitor.Capture();
            if (capacity.IsOverloaded)
            {
                _logger.LogWarning(
                    "Job {JobName}: skipping lease claim because this node is overloaded (CPU={Cpu:F1}%, memory={Memory:F1}%).",
                    job.Name, capacity.ProcessCpuPercent, capacity.MemoryLoadPercent);
                return;
            }

            // P1.1: claim the per-job execution lease before doing anything observable. This is the
            // single choke point for both scheduled and manually triggered runs — another scheduler
            // instance holding the lease means this occurrence is already being executed elsewhere.
            var leaseDuration = TimeSpan.FromSeconds(
                Math.Max(30, _configuration.GetValue<int>("Scheduler:JobLeaseSeconds", 600)));

            long? fenceToken;
            try
            {
                fenceToken = await _store.AcquireJobLeaseAsync(job.Id, _leaseOwnerId, leaseDuration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobName}: could not acquire the execution lease.", job.Name);
                return;
            }

            if (fenceToken is null)
            {
                _logger.LogDebug("Job {JobName} is leased by another scheduler instance — skipping.", job.Name);
                return;
            }

            try
            {
                await ExecuteLeasedJobAsync(
                    job, leaseDuration, fenceToken.Value, variableOverrides,
                    initialSessionId, resumeFromCheckpoint);
            }
            finally
            {
                try
                {
                    await _store.ReleaseJobLeaseAsync(job.Id, _leaseOwnerId);
                }
                catch (Exception ex)
                {
                    // An unreleased lease self-heals at expiry; never let release failure surface.
                    _logger.LogWarning(ex, "Job {JobName}: failed to release the execution lease.", job.Name);
                }
            }
        }

        private async Task ExecuteLeasedJobAsync(
            JobDefinition job,
            TimeSpan leaseDuration,
            long fenceToken,
            IReadOnlyDictionary<string, string>? variableOverrides,
            string? initialSessionId,
            bool resumeFromCheckpoint)
        {
            _logger.LogInformation("Job runner: {JobName} starting execution cycle (MaxRetries={Max}).", job.Name, job.MaxRetries);

            var currentHash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(job.Script))).ToLowerInvariant();

            bool? hashMatched = null;
            if (job.ScriptHash is not null)
            {
                hashMatched = string.Equals(currentHash, job.ScriptHash, StringComparison.OrdinalIgnoreCase);
                if (!hashMatched.Value)
                {
                    var mismatchMsg = $"Script hash mismatch for job '{job.Name}'. Expected: {job.ScriptHash}, Got: {currentHash}";
                    _logger.LogWarning(mismatchMsg);
                    if (job.HashPolicy.Equals("Block", StringComparison.OrdinalIgnoreCase))
                    {
                        long blockedId = 0;
                        try { blockedId = await _store.LogJobStartAsync(job.Id); } catch { }
                        var blockedSw = System.Diagnostics.Stopwatch.StartNew();
                        using var blockedActivity = SchedulerObservability.StartScheduledJobActivity(blockedId, currentHash, attempt: 0);
                        if (blockedId > 0)
                            await _store.LogJobEndAsync(blockedId, "BLOCKED", mismatchMsg,
                                scriptHashAtRunTime: currentHash, hashMatched: false);
                        blockedSw.Stop();
                        SchedulerObservability.CompleteScheduledJobActivity(
                            blockedActivity, "BLOCKED", blockedSw.ElapsedMilliseconds, 0, 0, 0);
                        // A blocked run still consumed its occurrence: advance the schedule so the job
                        // does not re-fire immediately and block again on every tick.
                        var blockedNextRun = await AdvanceScheduleLinksAsync(job, DateTime.UtcNow) ?? CalculateNextRun(job);
                        try { await _store.TryUpdateJobLastRunFencedAsync(job.Id, DateTime.Now, blockedNextRun, fenceToken); } catch { }
                        return;
                    }
                }
            }

            string sessionId = string.IsNullOrWhiteSpace(initialSessionId)
                ? $"job-{Guid.NewGuid():N}"
                : initialSessionId;
            int maxAttempts = Math.Max(1, job.MaxRetries + 1);
            ScriptExecutionResult? lastResult = null;
            var finalStatus = "FAILURE";

            using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? default);
            long lastHistoryId = 0;

            // Renew the lease while the job runs (retries with backoff can far outlive the lease
            // duration). Losing the lease cancels the run: another instance may now own the job.
            var leaseHeartbeat = StartLeaseHeartbeat(job.Id, job.Name, leaseDuration, cycleCts);

            try
            {
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    if (lastHistoryId > 0)
                    {
                        _runningJobs.TryRemove(lastHistoryId, out _);
                    }

                    long historyId = 0;
                    try
                    {
                        historyId = await _store.LogJobStartAsync(job.Id);
                        lastHistoryId = historyId;
                        if (historyId > 0) _runningJobs[historyId] = cycleCts;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to log job start for {JobName}.", job.Name);
                    }

                    var attemptSw = System.Diagnostics.Stopwatch.StartNew();
                    using var attemptActivity = SchedulerObservability.StartScheduledJobActivity(historyId, currentHash, attempt);
                    var attemptStatus = "FAILURE";
                    long attemptRows = 0;
                    long attemptPeakMemory = 0;
                    double attemptCpuSeconds = 0;
                    long attemptQueueWaitMs = 0;
                    var usedSandbox = false;
                    try
                    {
                        var throttleSw = System.Diagnostics.Stopwatch.StartNew();
                        using var slot = await _throttle.AcquireAsync(job.Name, cycleCts.Token);
                        throttleSw.Stop();
                        var queueWaitMs = throttleSw.ElapsedMilliseconds;
                        attemptQueueWaitMs = queueWaitMs;
                        _logger.LogInformation("Job {JobName} acquired throttle slot. Queue wait: {QueueWaitMs} ms.", job.Name, queueWaitMs);
                        using var scope = _serviceProvider.CreateScope();
                        var sandboxExecutor = scope.ServiceProvider.GetService<ISandboxScheduledJobExecutor>();
                        if (sandboxExecutor is not null)
                        {
                            usedSandbox = true;
                            lastResult = await sandboxExecutor.ExecuteAsync(
                                job, historyId, attempt, sessionId, resumeFromCheckpoint, queueWaitMs,
                                variableOverrides, cycleCts.Token);
                        }
                        else
                        {
                            var executor = scope.ServiceProvider.GetRequiredService<IScriptExecutor>();
                            lastResult = resumeFromCheckpoint
                                ? await executor.ResumeTextAsync(
                                    job.Script, sessionId, cycleCts.Token, job.Name, queueWaitMs)
                                : variableOverrides is { Count: > 0 }
                                ? await executor.ExecuteTextAsync(
                                    job.Script, sessionId, cycleCts.Token, job.Name, queueWaitMs,
                                    executionIdentity: null, variableOverrides: variableOverrides)
                                : await executor.ExecuteTextAsync(
                                    job.Script, sessionId, cycleCts.Token, job.Name, queueWaitMs);
                        }
                        sessionId = lastResult.SessionId ?? sessionId;

                        if (lastResult.Success)
                        {
                            _logger.LogInformation("Job {JobName} finished successfully on attempt {Attempt}. (RAM: {Mem} bytes, CPU: {Cpu}s)",
                                job.Name, attempt, lastResult.PeakMemoryBytes, lastResult.CpuTimeSeconds);
                            attemptStatus = "SUCCESS";
                            attemptRows = lastResult.RowsProcessed;
                            attemptPeakMemory = lastResult.PeakMemoryBytes;
                            attemptCpuSeconds = lastResult.CpuTimeSeconds;
                            finalStatus = "SUCCESS";

                            if (historyId > 0)
                            {
                                await _store.LogJobEndAsync(historyId, "SUCCESS", rowsProcessed: lastResult.RowsProcessed,
                                    peakMemoryBytes: lastResult.PeakMemoryBytes, cpuTimeSeconds: lastResult.CpuTimeSeconds,
                                    scriptHashAtRunTime: currentHash, hashMatched: hashMatched,
                                    rowsQuarantined: lastResult.RowsQuarantined, rowsWarned: lastResult.RowsWarned,
                                    dataQualityFailures: lastResult.DataQualityFailures);
                                await _store.SaveJobColumnMetricsAsync(historyId, lastResult.DataQualityColumnMetrics ?? []);
                                await _store.SaveJobDataQualityFailuresAsync(historyId, lastResult.DataQualityRuleFailures ?? []);
                                await _store.SaveJobStatementMetricsAsync(historyId, lastResult.StatementMetrics ?? []);
                                await PersistResumeMetadataAsync(historyId, sessionId);
                            }

                            break; // Done
                        }
                        else
                        {
                            var safeError = SecretRedactor.Redact(lastResult.ErrorMessage);
                            _logger.LogWarning("Job {JobName} finished with failure on attempt {Attempt}/{Max}: {Error}",
                                job.Name, attempt, maxAttempts, safeError);
                            attemptStatus = "FAILURE";
                            attemptRows = lastResult.RowsProcessed;
                            attemptPeakMemory = lastResult.PeakMemoryBytes;
                            attemptCpuSeconds = lastResult.CpuTimeSeconds;
                            finalStatus = "FAILURE";

                            if (historyId > 0)
                            {
                                await _store.LogJobEndAsync(historyId, "FAILURE", safeError,
                                    peakMemoryBytes: lastResult.PeakMemoryBytes, cpuTimeSeconds: lastResult.CpuTimeSeconds,
                                    scriptHashAtRunTime: currentHash, hashMatched: hashMatched,
                                    rowsQuarantined: lastResult.RowsQuarantined, rowsWarned: lastResult.RowsWarned,
                                    dataQualityFailures: lastResult.DataQualityFailures);
                                await _store.SaveJobColumnMetricsAsync(historyId, lastResult.DataQualityColumnMetrics ?? []);
                                await _store.SaveJobDataQualityFailuresAsync(historyId, lastResult.DataQualityRuleFailures ?? []);
                                await _store.SaveJobStatementMetricsAsync(historyId, lastResult.StatementMetrics ?? []);
                                await PersistResumeMetadataAsync(historyId, sessionId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing job {JobName} on attempt {Attempt}.", job.Name, attempt);
                        if (historyId > 0)
                        {
                            await _store.LogJobEndAsync(historyId, "FAILURE", SecretRedactor.Redact(ex.Message),
                                scriptHashAtRunTime: currentHash, hashMatched: hashMatched);
                            await PersistResumeMetadataAsync(historyId, sessionId);
                        }
                        lastResult = new ScriptExecutionResult(false, 0, SecretRedactor.Redact(ex.Message));
                        finalStatus = "FAILURE";
                    }
                    finally
                    {
                        attemptSw.Stop();
                        SchedulerObservability.CompleteScheduledJobActivity(
                            attemptActivity,
                            attemptStatus,
                            attemptSw.ElapsedMilliseconds,
                            attemptRows,
                            attemptPeakMemory,
                            attemptCpuSeconds,
                            attemptQueueWaitMs,
                            attempt);
                        if (historyId > 0 && !string.IsNullOrWhiteSpace(job.TenantId))
                        {
                            try
                            {
                                await _store.SaveTenantUsageAsync(new TenantUsageRecord(
                                    0,
                                    job.TenantId,
                                    historyId,
                                    job.JobType.ToString(),
                                    attemptStatus,
                                    attemptRows,
                                    attemptPeakMemory,
                                    attemptCpuSeconds,
                                    attemptSw.ElapsedMilliseconds,
                                    DateTime.UtcNow));
                            }
                            catch (Exception ex)
                            {
                                // Metering is evidence, never execution authority. A ledger outage
                                // cannot turn a completed tenant workload into a retry or duplicate write.
                                _logger.LogError(ex,
                                    "Failed to persist tenant usage for job {JobName}, history {HistoryId}.",
                                    job.Name, historyId);
                            }

                            if (_meteringLedger is not null)
                            {
                                try
                                {
                                    var configuredTenant = _configuration["Orchestrator:TenantId"];
                                    var tenant = string.IsNullOrWhiteSpace(configuredTenant)
                                        ? ETL_SQL.Core.Multitenancy.TenantContext.FromVerifiedCredential(job.TenantId)
                                        : ETL_SQL.Core.Multitenancy.TenantContext.FromHostConfiguration(configuredTenant);
                                    tenant.RequireTenant(job.TenantId);
                                    var statements = lastResult?.StatementMetrics ?? [];
                                    await _meteringLedger.AppendAsync(tenant, new TenantMeteringEvent
                                    {
                                        SourceEventId = $"history-{historyId}",
                                        Source = TenantMeteringSource.Scheduler,
                                        WorkloadClass = job.JobType == JobTargetKind.Report
                                            ? TenantWorkloadClass.Report
                                            : TenantWorkloadClass.Script,
                                        ConnectorClass = TenantConnectorClass.None,
                                        Status = attemptStatus switch
                                        {
                                            "SUCCESS" => TenantMeteringStatus.Succeeded,
                                            "CANCELLED" => TenantMeteringStatus.Cancelled,
                                            _ => TenantMeteringStatus.Failed
                                        },
                                        Rows = attemptRows,
                                        SandboxCpuMilliseconds = usedSandbox
                                            ? ToMilliseconds(attemptCpuSeconds)
                                            : 0,
                                        SandboxPeakMemoryBytes = usedSandbox ? attemptPeakMemory : 0,
                                        SandboxIoReadBytes = usedSandbox
                                            ? statements.Sum(metric => Math.Max(0, metric.SpillReadBytes))
                                            : 0,
                                        SandboxIoWriteBytes = usedSandbox
                                            ? statements.Sum(metric => Math.Max(0, metric.SpilledBytes))
                                            : 0,
                                        ConcurrencyUnits = 1,
                                        DurationMilliseconds = attemptSw.ElapsedMilliseconds,
                                        RecordedAtUtc = DateTimeOffset.UtcNow
                                    }, CancellationToken.None);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex,
                                        "Failed to append counts-only tenant metering for job {JobName}, history {HistoryId}.",
                                        job.Name, historyId);
                                }
                            }
                        }
                    }

                    if (lastResult?.RetryAllowed == false)
                    {
                        _logger.LogError(
                            "Job {JobName} will not be retried because its failure has an ambiguous external write outcome.",
                            job.Name);
                        break;
                    }

                    if (attempt < maxAttempts)
                    {
                        int backoffSeconds = (int)Math.Pow(2, attempt - 1) * job.RetryDelaySeconds;
                        backoffSeconds = Math.Min(backoffSeconds, 3600); // Cap at 1 hour

                        _logger.LogInformation("Job {JobName} failed. Retrying in {Delay}s (Backoff). Session: {SessionId}",
                            job.Name, backoffSeconds, sessionId);

                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cycleCts.Token);
                        }
                        catch (TaskCanceledException) { break; }
                    }
                }
            }
            finally
            {
                if (lastHistoryId > 0)
                {
                    _runningJobs.TryRemove(lastHistoryId, out _);
                }

                if (!cycleCts.IsCancellationRequested)
                    cycleCts.Cancel();
                await leaseHeartbeat;
            }

            var notificationDispatch = _serviceProvider.GetService<NotificationDispatchService>();
            if (notificationDispatch is not null)
            {
                await notificationDispatch.DispatchJobNotificationsAsync(
                    job, finalStatus, lastHistoryId, lastResult, _cts?.Token ?? default);
            }

            var nextRun = await AdvanceScheduleLinksAsync(job, DateTime.UtcNow) ?? CalculateNextRun(job);
            try
            {
                // Fenced write: if this node was paused past its lease and another instance took over the
                // job (advancing the fence token), this update matches zero rows and we skip rescheduling
                // rather than overwrite the new owner's state (P1.8).
                if (!await _store.TryUpdateJobLastRunFencedAsync(job.Id, DateTime.Now, nextRun, fenceToken))
                    _logger.LogWarning(
                        "Job {JobName}: skipped the last-run/next-run update — the execution lease was reclaimed " +
                        "by another instance (fenced out, token {Token}).", job.Name, fenceToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update last run info for {JobName}.", job.Name);
            }

            await QuarantineIfRepeatedlyFailingAsync(job);
        }

        private static long ToMilliseconds(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds <= 0) return 0;
            var value = seconds * 1000d;
            return value >= long.MaxValue ? long.MaxValue : checked((long)Math.Round(value));
        }

        private async Task PersistResumeMetadataAsync(long historyId, string? sessionId)
        {
            if (historyId <= 0 || string.IsNullOrWhiteSpace(sessionId)) return;
            var state = await _sessionManager.LoadSession(sessionId);
            if (state is null || !TryReadCheckpointLabel(state, out var checkpointLabel)) return;
            await _store.UpdateJobResumeMetadataAsync(historyId, sessionId, checkpointLabel);
        }

        private static bool TryReadCheckpointLabel(SessionState state, out string checkpointLabel)
        {
            checkpointLabel = string.Empty;
            if (!state.GlobalVariables.TryGetValue("@_LAST_CHECKPOINT_LABEL", out var value)) return false;
            checkpointLabel = value?.ToString()?.Trim() ?? string.Empty;
            return checkpointLabel.Length > 0;
        }

        private static bool IsResumeFailureStatus(string status) =>
            status.Equals("FAILURE", StringComparison.OrdinalIgnoreCase)
            || status.Equals("FAILED", StringComparison.OrdinalIgnoreCase)
            || status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);

        private async Task QuarantineIfRepeatedlyFailingAsync(JobDefinition job)
        {
            var threshold = _configuration.GetValue<int>("Scheduler:QuarantineFailureThreshold", 5);
            if (threshold <= 0)
                return;

            IReadOnlyList<JobHistoryEntry> recent;
            try
            {
                recent = (await _store.GetHistoryAsync(job.Id, threshold)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job {JobName}: failed to evaluate quarantine policy.", job.Name);
                return;
            }

            if (recent.Count < threshold
                || recent.Any(entry => !IsFailureStatus(entry.Status)))
                return;

            var reason = $"Job quarantined after {threshold} consecutive failures.";
            try
            {
                await _store.SaveJobAsync(job with { IsEnabled = false, NextRun = null });
                var quarantineId = await _store.LogJobStartAsync(job.Id);
                await _store.LogJobEndAsync(quarantineId, "QUARANTINED", reason);
                _logger.LogError("Job {JobName}: {Reason}", job.Name, reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job {JobName}: failed to quarantine repeatedly failing job.", job.Name);
            }
        }

        private static bool IsFailureStatus(string status) =>
            status.Equals("FAILURE", StringComparison.OrdinalIgnoreCase)
            || status.Equals("FAILED", StringComparison.OrdinalIgnoreCase);

        private Task StartLeaseHeartbeat(JobId jobId, string jobName, TimeSpan leaseDuration, CancellationTokenSource cycleCts)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(5, leaseDuration.TotalSeconds / 3));
            return Task.Run(async () =>
            {
                while (!cycleCts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(interval, cycleCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        if (!await _store.TryRenewJobLeaseAsync(jobId, _leaseOwnerId, leaseDuration))
                        {
                            _logger.LogWarning(
                                "Job {JobName}: execution lease was lost (expired and reclaimed) — cancelling this run to avoid a duplicate execution.",
                                jobName);
                            cycleCts.Cancel();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        // A transient store failure must not kill a healthy run; at worst the lease
                        // lapses, which degrades to the pre-lease at-least-once behavior.
                        _logger.LogWarning(ex, "Job {JobName}: lease renewal failed transiently.", jobName);
                    }
                }
            }, CancellationToken.None);
        }

        // Scheduling is deliberately local wall-clock: AtTime means "at HH:mm on this machine"
        // and stored LastRun/NextRun values are local. Because the next run is always computed
        // forward from 'now' after a fire, a DST fall-back hour cannot double-run a job; a
        // spring-forward NextRun inside the skipped hour fires when the clock jumps past it.
        // Do not switch this to UTC piecemeal — persisted rows would be reinterpreted at upgrade.
        private DateTime CalculateNextRun(JobDefinition job)
        {
            var now = DateTime.Now;
            var interval = job.Interval;
            var unit = job.Unit.ToUpper();

            DateTime next = now;
            switch (unit)
            {
                case "SECOND": next = now.AddSeconds(interval); break;
                case "MINUTE": next = now.AddMinutes(interval); break;
                case "HOUR": next = now.AddHours(interval); break;
                case "DAY": next = now.AddDays(interval); break;
                case "WEEK": next = now.AddDays(interval * 7); break;
                case "MONTH": next = now.AddMonths(interval); break;
                default: next = now.AddHours(1); break;
            }

            if (!string.IsNullOrEmpty(job.AtTime) && TimeSpan.TryParse(job.AtTime, out var atTime))
            {
                if (unit == "DAY")
                {
                    next = next.Date.Add(atTime);
                    if (next <= now) next = next.AddDays(1);
                }
            }

            return next;
        }

        /// <summary>
        /// Advances a cron-scheduled job's links after a run and returns the earliest next occurrence
        /// across them, or <c>null</c> when the job has no links (leaving the legacy interval path to
        /// answer instead).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every link that was due is marked as having fired, not just the earliest one. One run
        /// satisfies all of them — that is what coalescing means — so leaving the others due would
        /// re-fire the job on the very next tick.
        /// </para>
        /// <para>
        /// The value returned is written to <c>Jobs.NextRun</c>, which for a cron-scheduled job is a
        /// derived display value: the links are the schedule of record.
        /// </para>
        /// </remarks>
        internal async Task<DateTime?> AdvanceScheduleLinksAsync(JobDefinition job, DateTime ranAtUtc)
        {
            if (_store is not IJobCatalogStore catalog) return null;

            IReadOnlyList<JobScheduleLink> links;
            try
            {
                links = await catalog.GetJobSchedulesAsync(job.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobName}: could not read schedule links to advance them.", job.Name);
                return null;
            }

            if (links.Count == 0) return null;

            DateTime? earliest = null;
            foreach (var link in links)
            {
                var schedule = await catalog.GetScheduleByIdAsync(link.ScheduleId);
                if (schedule is null)
                {
                    _logger.LogWarning(
                        "Job {JobName}: schedule '{Schedule}' is attached but no longer exists — the link is dead " +
                        "and the job will not run on it.", job.Name, link.ScheduleName);
                    continue;
                }

                DateTime? next;
                try
                {
                    next = CronSchedule.GetNextOccurrence(schedule.Cron, schedule.TimeZone, new DateTimeOffset(ranAtUtc, TimeSpan.Zero));
                }
                catch (ArgumentException ex)
                {
                    // Stored expressions are validated on write, so this means the row was edited out
                    // of band. Report it and leave the link alone rather than arming it with a guess.
                    _logger.LogError(ex, "Job {JobName}: schedule '{Schedule}' has an unusable cron expression.",
                        job.Name, link.ScheduleName);
                    continue;
                }

                if (next is null)
                    _logger.LogWarning(
                        "Job {JobName}: schedule '{Schedule}' ('{Cron}') has no further occurrence; the link is now " +
                        "dormant.", job.Name, link.ScheduleName, schedule.Cron);

                var wasDue = link.NextRun is not null && link.NextRun <= ranAtUtc;
                DateTime? armed;
                if (wasDue)
                {
                    await catalog.UpdateJobScheduleRunAsync(link.JobId, link.ScheduleId, ranAtUtc, next);
                    armed = next;
                }
                else if (link.NextRun is null)
                {
                    // Not due — it had nothing armed at all. Arm it without claiming it ran.
                    await catalog.ArmJobScheduleAsync(link.JobId, link.ScheduleId, next);
                    armed = next;
                }
                else
                {
                    // Not due and already armed: this run came from a sibling link, so leave it be.
                    armed = link.NextRun;
                }

                // A disabled schedule cannot make the job due, so it must not contribute to the
                // next-run figure an operator reads — but its link is still advanced above, so
                // re-enabling it takes effect immediately rather than after one wasted cycle.
                if (!schedule.IsEnabled) continue;

                if (armed is not null && (earliest is null || armed < earliest))
                    earliest = armed;
            }

            return earliest;
        }
    }
}
