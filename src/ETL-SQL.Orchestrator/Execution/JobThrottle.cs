using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>Workload priority/resource class used for admission weighting and short-job capacity reservation.</summary>
    public enum JobWorkloadClass
    {
        Normal = 0,
        Short = 1,
        Heavy = 2
    }
    /// <summary>
    /// Enforces a configurable cap on concurrently running jobs across ALL orchestrator
    /// processes using the configured orchestrator relational store. SQLite coordinates
    /// local processes; PostgreSQL coordinates HA nodes through the shared database.
    ///
    /// Within-process queuing: <see cref="AcquireAsync"/> uses configurable exponential
    /// backoff and jitter until a slot becomes available (or cancellation is requested).
    ///
    /// Crash safety: local dead-process slots are purged, while remote-node slots use
    /// renewable leases so a crashed HA peer cannot permanently block capacity.
    /// </summary>
    public class JobThrottle : IDisposable
    {
        private readonly int _maxConcurrent;
        private readonly int _reservedShortSlots;
        private readonly int _defaultHeavySlotWeight;
        private readonly IOrchestratorStoreDialect _dialect;
        private readonly ILogger<JobThrottle> _logger;
        private readonly int _pollInitialDelayMs;
        private readonly int _pollMaxDelayMs;
        private readonly double _pollJitterRatio;
        private readonly TimeSpan _slotLease;
        private readonly TimeSpan _slotHeartbeat;
        private static readonly int _pid = Environment.ProcessId;
        private static readonly Regex HeavyScriptPattern = new(
            @"\b(JOIN|ORDER\s+BY|GROUP\s+BY|TRANSFORM|SELECT\s+DISTINCT)\b|\bOVER\s*\(|SPILL_THRESHOLD|SPILL_CHUNK|OPERATOR_MEMORY_GRANT|EXTERNAL_SORT_CHUNK_SIZE",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private int _activeJobs;
        private int _queuedJobs;
        private int _activeShortAcquires;
        private bool _tableReady;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly object _waiterLock = new();
        private long _nextWaiterSeq;
        private readonly PriorityQueue<WaiterEntry, (int Priority, long Sequence)> _waiters = new();

        public JobThrottle(IOptions<JobThrottleOptions> options, ILogger<JobThrottle> logger)
            : this(options, logger, new ConfigurationBuilder().Build())
        {
        }

        public JobThrottle(IOptions<JobThrottleOptions> options, ILogger<JobThrottle> logger, IConfiguration configuration)
        {
            _maxConcurrent = options.Value.MaxConcurrentJobs > 0
                ? options.Value.MaxConcurrentJobs
                : Math.Max(1, Environment.ProcessorCount / 2);
            _reservedShortSlots = ResolveReservedShortSlots(options.Value.ReservedShortJobSlots, _maxConcurrent);
            _defaultHeavySlotWeight = Math.Max(1, options.Value.HeavyJobSlotWeight);
            _pollInitialDelayMs = Math.Max(10, options.Value.PollInitialDelayMs);
            _pollMaxDelayMs = Math.Max(_pollInitialDelayMs, options.Value.PollMaxDelayMs);
            _pollJitterRatio = Math.Clamp(options.Value.PollJitterRatio, 0d, 1d);
            _slotLease = TimeSpan.FromSeconds(Math.Max(2, options.Value.SlotLeaseSeconds));
            _slotHeartbeat = TimeSpan.FromSeconds(Math.Clamp(
                options.Value.SlotHeartbeatSeconds, 1, Math.Max(1, (int)_slotLease.TotalSeconds / 2)));

            var provider = ETL_SQL.Common.DatabaseProviderParser.Parse(configuration["Orchestrator:Database:Provider"]);
            if (provider == ETL_SQL.Common.DatabaseProvider.Postgres)
            {
                var connectionString = configuration["Orchestrator:Database:ConnectionString"];
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException(
                        "Orchestrator:Database:Provider=Postgres requires Orchestrator:Database:ConnectionString for shared throttle coordination.");
                _dialect = new NpgsqlOrchestratorDialect(connectionString);
            }
            else
            {
                var configuredDbPath = configuration["Orchestrator:DatabasePath"];
                var dbPath = string.IsNullOrWhiteSpace(configuredDbPath)
                    ? SQLiteJobHistoryStore.DefaultDbPath()
                    : configuredDbPath;
                _dialect = new SqliteOrchestratorDialect($"Data Source={dbPath}");
            }
            _logger = logger;
            _logger.LogInformation(
                "JobThrottle initialized: max_concurrent_jobs={Max}, reserved_short_slots={ShortSlots}, heavy_weight={HeavyWeight}, poll={Initial}-{MaxDelay}ms, jitter={Jitter:P0} (cross-process, pid={Pid})",
                _maxConcurrent, _reservedShortSlots, _defaultHeavySlotWeight, _pollInitialDelayMs, _pollMaxDelayMs, _pollJitterRatio, _pid);
        }

        internal int ReservedShortSlots => _reservedShortSlots;
        internal int MaxHeavySlots => Math.Max(1, _maxConcurrent - _reservedShortSlots);

        /// <summary>
        /// Classifies a job as <see cref="JobWorkloadClass.Short"/>, <see cref="JobWorkloadClass.Normal"/>,
        /// or <see cref="JobWorkloadClass.Heavy"/> using explicit metadata overrides first, then
        /// spilling/sort/join/window/aggregation markers in the script text.
        /// </summary>
        public static JobWorkloadClass ClassifyWorkload(
            string? scriptText,
            IReadOnlyDictionary<string, string>? jobOptions = null)
        {
            if (jobOptions is not null)
            {
                foreach (var key in new[] { "WORKLOAD_CLASS", "WorkloadClass", "PRIORITY", "Priority" })
                {
                    if (!jobOptions.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                        continue;

                    if (raw.Equals("Heavy", StringComparison.OrdinalIgnoreCase)
                        || raw.Equals("Low", StringComparison.OrdinalIgnoreCase))
                        return JobWorkloadClass.Heavy;
                    if (raw.Equals("Short", StringComparison.OrdinalIgnoreCase)
                        || raw.Equals("High", StringComparison.OrdinalIgnoreCase))
                        return JobWorkloadClass.Short;
                    if (raw.Equals("Normal", StringComparison.OrdinalIgnoreCase))
                        return JobWorkloadClass.Normal;
                }
            }

            if (!string.IsNullOrWhiteSpace(scriptText) && HeavyScriptPattern.IsMatch(scriptText))
                return JobWorkloadClass.Heavy;

            return JobWorkloadClass.Short;
        }

        private static int ResolveReservedShortSlots(int configured, int maxConcurrent)
        {
            if (configured == 0 || maxConcurrent <= 1)
                return 0;
            if (configured < 0)
                return 1;
            return Math.Min(configured, maxConcurrent - 1);
        }

        private async Task EnsureTableAsync()
        {
            if (_tableReady) return;
            await _initLock.WaitAsync();
            try
            {
                if (_tableReady) return;
                using var conn = _dialect.CreateConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS throttle_slots (
                        id           {_dialect.AutoIncrementPrimaryKey},
                        process_id   INTEGER NOT NULL,
                        job_name     TEXT    NOT NULL,
                        acquired_at  TEXT    NOT NULL,
                        machine_name TEXT    DEFAULT '',
                        slot_weight  INTEGER NOT NULL DEFAULT 1,
                        is_heavy     INTEGER NOT NULL DEFAULT 0
                    );";
                await cmd.ExecuteNonQueryAsync();

                var columns = await _dialect.GetColumnNamesAsync(conn, "throttle_slots");
                if (!columns.Contains("slot_weight"))
                {
                    using var alterWeight = conn.CreateCommand();
                    alterWeight.CommandText = "ALTER TABLE throttle_slots ADD COLUMN slot_weight INTEGER NOT NULL DEFAULT 1;";
                    await alterWeight.ExecuteNonQueryAsync();
                }

                if (!columns.Contains("is_heavy"))
                {
                    using var alterHeavy = conn.CreateCommand();
                    alterHeavy.CommandText = "ALTER TABLE throttle_slots ADD COLUMN is_heavy INTEGER NOT NULL DEFAULT 0;";
                    await alterHeavy.ExecuteNonQueryAsync();
                }

                _tableReady = true;
            }
            finally { _initLock.Release(); }
        }

        /// <summary>
        /// Acquires a concurrency slot, waiting until one is available across all processes.
        /// The returned disposable also implements IAsyncDisposable. Async owners should await
        /// disposal so the lease heartbeat and database release finish before host shutdown.
        /// </summary>
        public Task<IDisposable> AcquireAsync(string jobName, CancellationToken ct = default) =>
            AcquireAsync(jobName, JobWorkloadClass.Normal, slotWeight: 0, ct);

        /// <summary>Acquires a concurrency slot for the given workload classification.</summary>
        public Task<IDisposable> AcquireAsync(
            string jobName,
            JobWorkloadClass workloadClass,
            CancellationToken ct = default) =>
            AcquireAsync(jobName, workloadClass, slotWeight: 0, ct);

        /// <summary>
        /// Acquires a concurrency slot for the given workload classification and weight, giving waiting
        /// short jobs reserved slot capacity and priority wakeups over queued heavy jobs.
        /// </summary>
        public async Task<IDisposable> AcquireAsync(
            string jobName,
            JobWorkloadClass workloadClass,
            int slotWeight,
            CancellationToken ct = default)
        {
            await EnsureTableAsync();
            int effectiveWeight = slotWeight > 0
                ? slotWeight
                : (workloadClass == JobWorkloadClass.Heavy ? _defaultHeavySlotWeight : 1);
            bool isShort = workloadClass == JobWorkloadClass.Short;

            if (isShort)
                Interlocked.Increment(ref _activeShortAcquires);

            Interlocked.Increment(ref _queuedJobs);
            var failedAttempts = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    long? slotId = await TryClaimSlotAsync(jobName, workloadClass, effectiveWeight);
                    if (slotId.HasValue)
                    {
                        var active = Interlocked.Increment(ref _activeJobs);
                        _logger.LogInformation(
                            "Job {JobName} started (class={WorkloadClass}, weight={Weight}, active={Active}/{Cap}, pid={Pid})",
                            jobName, workloadClass, effectiveWeight, active, _maxConcurrent, _pid);
                        return new Slot(this, jobName, slotId.Value, effectiveWeight);
                    }

                    _logger.LogDebug("Job {JobName} waiting for slot (class={WorkloadClass}, queued={Queued}, cap={Cap})",
                        jobName, workloadClass, _queuedJobs, _maxConcurrent);
                    await WaitForSlotSignalOrDelayAsync(workloadClass, failedAttempts++, ct);
                }

                ct.ThrowIfCancellationRequested();
                throw new OperationCanceledException(ct);
            }
            finally
            {
                Interlocked.Decrement(ref _queuedJobs);
                if (isShort)
                    Interlocked.Decrement(ref _activeShortAcquires);
            }
        }

        private async Task<long?> TryClaimSlotAsync(
            string jobName,
            JobWorkloadClass workloadClass,
            int effectiveWeight)
        {
            bool isHeavy = workloadClass == JobWorkloadClass.Heavy || effectiveWeight > 1;
            if (isHeavy && Volatile.Read(ref _activeShortAcquires) > 0)
                return null;

            using var conn = _dialect.CreateConnection();
            await conn.OpenAsync();

            await PurgeStaleSlotsAsync(conn);

            // BEGIN EXCLUSIVE locks the DB file for the duration of this check+insert,
            // preventing another process from racing through the same window.
            using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                using var countCmd = conn.CreateCommand();
                countCmd.Transaction = tx;
                countCmd.CommandText = @"
                    SELECT
                        COUNT(*),
                        COALESCE(SUM(slot_weight), 0),
                        COALESCE(SUM(CASE WHEN is_heavy = 1 THEN 1 ELSE 0 END), 0)
                    FROM throttle_slots;";
                int totalCount;
                int totalWeight;
                int heavyCount;
                using (var reader = await countCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        totalCount = reader.GetInt32(0);
                        totalWeight = Convert.ToInt32(reader.GetValue(1));
                        heavyCount = Convert.ToInt32(reader.GetValue(2));
                    }
                    else
                    {
                        totalCount = 0;
                        totalWeight = 0;
                        heavyCount = 0;
                    }
                }

                if (totalCount >= _maxConcurrent)
                {
                    tx.Rollback();
                    return null;
                }

                if (isHeavy)
                {
                    if (heavyCount >= MaxHeavySlots
                        || (totalCount > 0 && totalWeight + effectiveWeight > _maxConcurrent))
                    {
                        tx.Rollback();
                        return null;
                    }
                }

                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = _dialect.InsertReturningId(@"
                    INSERT INTO throttle_slots (process_id, job_name, acquired_at, machine_name, slot_weight, is_heavy)
                    VALUES (@pid, @job, @at, @machine, @weight, @heavy)", "id");
                insertCmd.AddParam("@pid", _pid);
                insertCmd.AddParam("@job", jobName);
                insertCmd.AddParam("@at", DateTime.UtcNow.ToString("O"));
                insertCmd.AddParam("@machine", Environment.MachineName);
                insertCmd.AddParam("@weight", effectiveWeight);
                insertCmd.AddParam("@heavy", isHeavy ? 1 : 0);
                var id = Convert.ToInt64(await insertCmd.ExecuteScalarAsync()!);

                tx.Commit();
                return id;
            }
            catch (DbException ex) when (IsRetryableContention(ex))
            {
                // Another process holds an exclusive lock; retry on the next poll tick.
                try { tx.Rollback(); } catch { }
                return null;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        internal TimeSpan CalculatePollDelay(int failedAttempts)
        {
            var exponent = Math.Min(20, Math.Max(0, failedAttempts));
            var delay = Math.Min(_pollMaxDelayMs, _pollInitialDelayMs * Math.Pow(2, exponent));
            var jitter = delay * _pollJitterRatio * ((Random.Shared.NextDouble() * 2d) - 1d);
            return TimeSpan.FromMilliseconds(Math.Clamp(delay + jitter, 1d, _pollMaxDelayMs));
        }

        private async Task PurgeStaleSlotsAsync(DbConnection conn)
        {
            using (var expired = conn.CreateCommand())
            {
                expired.CommandText = "DELETE FROM throttle_slots WHERE machine_name != @machine AND acquired_at < @cutoff;";
                expired.AddParam("@machine", Environment.MachineName);
                expired.AddParam("@cutoff", DateTime.UtcNow.Subtract(_slotLease).ToString("O"));
                await expired.ExecuteNonQueryAsync();
            }

            var pids = new List<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT process_id FROM throttle_slots WHERE process_id != @own AND (machine_name = @machine OR machine_name IS NULL OR machine_name = '');";
                cmd.AddParam("@own", _pid);
                cmd.AddParam("@machine", Environment.MachineName);
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) pids.Add(r.GetInt32(0));
            }

            var deadPids = new List<int>();
            foreach (var pid in pids)
            {
                bool alive;
                try { Process.GetProcessById(pid); alive = true; }
                catch (ArgumentException) { alive = false; }

                if (!alive)
                    deadPids.Add(pid);
            }

            if (deadPids.Count == 0)
                return;

            try
            {
                using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
                foreach (var pid in deadPids)
                {
                    using var del = conn.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM throttle_slots WHERE process_id = @pid AND (machine_name = @machine OR machine_name IS NULL OR machine_name = '');";
                    del.AddParam("@pid", pid);
                    del.AddParam("@machine", Environment.MachineName);
                    await del.ExecuteNonQueryAsync();
                }

                tx.Commit();
            }
            catch (DbException ex) when (IsRetryableContention(ex))
            {
                // Another process is claiming or releasing a slot. The next acquire poll will retry cleanup.
            }
            catch
            {
                throw;
            }
        }

        private async Task WaitForSlotSignalOrDelayAsync(
            JobWorkloadClass workloadClass,
            int failedAttempts,
            CancellationToken ct)
        {
            var delay = CalculatePollDelay(failedAttempts);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = new WaiterEntry(tcs);
            int priority = workloadClass switch
            {
                JobWorkloadClass.Short => 0,
                JobWorkloadClass.Normal => 1,
                _ => 2
            };

            lock (_waiterLock)
            {
                _waiters.Enqueue(entry, (priority, ++_nextWaiterSeq));
            }

            try
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var delayTask = Task.Delay(delay, delayCts.Token);
                var completed = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);
                delayCts.Cancel();
                if (completed == delayTask)
                    await delayTask.ConfigureAwait(false);
                else
                    ct.ThrowIfCancellationRequested();
            }
            finally
            {
                entry.Cancel();
            }
        }

        private void WakeNextWaiters(int count)
        {
            int woken = 0;
            lock (_waiterLock)
            {
                while (woken < count && _waiters.TryDequeue(out var entry, out _))
                {
                    if (entry.TrySignal())
                        woken++;
                }
            }
        }

        private async Task ReleaseAsync(string jobName, long slotId, int slotWeight)
        {
            try
            {
                using var conn = _dialect.CreateConnection();
                await conn.OpenAsync().ConfigureAwait(false);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM throttle_slots WHERE id = @id;";
                cmd.AddParam("@id", slotId);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release throttle slot {SlotId} for job {JobName}", slotId, jobName);
            }
            finally
            {
                var active = Interlocked.Decrement(ref _activeJobs);
                _logger.LogInformation("Job {JobName} released slot (active={Active}/{Cap})", jobName, active, _maxConcurrent);
                WakeNextWaiters(Math.Max(1, slotWeight));
            }
        }

        private async Task RenewSlotAsync(long slotId, CancellationToken cancellationToken)
        {
            using var conn = _dialect.CreateConnection();
            await conn.OpenAsync(cancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE throttle_slots SET acquired_at = @at WHERE id = @id;";
            cmd.AddParam("@at", DateTime.UtcNow.ToString("O"));
            cmd.AddParam("@id", slotId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private static bool IsRetryableContention(DbException exception)
        {
            return exception is SqliteException { SqliteErrorCode: 5 }
                || exception is PostgresException { SqlState: "40001" or "40P01" or "55P03" };
        }

        /// <summary>Current snapshot of resource utilization (local process view).</summary>
        public JobThrottleMetrics GetMetrics() => new(
            ActiveJobs: _activeJobs,
            QueuedJobs: _queuedJobs,
            MaxJobs: _maxConcurrent,
            AvailableSlots: Math.Max(0, _maxConcurrent - _activeJobs));

        public void Dispose() { }

        private sealed class WaiterEntry(TaskCompletionSource<bool> tcs)
        {
            private int _done;

            public bool TrySignal()
            {
                if (Interlocked.Exchange(ref _done, 1) != 0)
                    return false;
                tcs.TrySetResult(true);
                return true;
            }

            public void Cancel() => Interlocked.Exchange(ref _done, 1);
        }

        private sealed class Slot : IDisposable, IAsyncDisposable
        {
            private readonly JobThrottle _owner;
            private readonly string _jobName;
            private readonly long _slotId;
            private readonly int _slotWeight;
            private readonly CancellationTokenSource _heartbeatCancellation = new();
            private readonly Task _heartbeatTask;
            private readonly object _disposeLock = new();
            private Task? _disposalTask;

            public Slot(JobThrottle owner, string jobName, long slotId, int slotWeight)
            {
                _owner = owner;
                _jobName = jobName;
                _slotId = slotId;
                _slotWeight = slotWeight;
                _heartbeatTask = HeartbeatAsync();
            }

            private async Task HeartbeatAsync()
            {
                try
                {
                    while (!_heartbeatCancellation.IsCancellationRequested)
                    {
                        await Task.Delay(_owner._slotHeartbeat, _heartbeatCancellation.Token).ConfigureAwait(false);
                        await _owner.RenewSlotAsync(_slotId, _heartbeatCancellation.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_heartbeatCancellation.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    _owner._logger.LogWarning(ex, "Failed to renew throttle slot {SlotId} for job {JobName}", _slotId, _jobName);
                }
            }

            public void Dispose()
            {
                // IDisposable callers need the same completed release as async owners. All awaits
                // in the disposal path avoid a captured synchronization context.
                DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            public ValueTask DisposeAsync()
            {
                lock (_disposeLock)
                    return new ValueTask(_disposalTask ??= DisposeCoreAsync());
            }

            private async Task DisposeCoreAsync()
            {
                try
                {
                    _heartbeatCancellation.Cancel();
                    await _heartbeatTask.ConfigureAwait(false);
                    await _owner.ReleaseAsync(_jobName, _slotId, _slotWeight).ConfigureAwait(false);
                }
                finally
                {
                    _heartbeatCancellation.Dispose();
                }
            }
        }
    }

    public class JobThrottleOptions
    {
        /// <summary>
        /// Maximum number of jobs that may run concurrently across all orchestrator processes.
        /// 0 = auto (ProcessorCount / 2, minimum 1).
        /// </summary>
        public int MaxConcurrentJobs { get; set; } = 0;

        /// <summary>
        /// Slots reserved so short jobs can run concurrently while heavy spilling jobs occupy the rest
        /// of the pool. -1 = auto (reserves 1 slot when MaxConcurrentJobs >= 2); 0 = disabled.
        /// </summary>
        public int ReservedShortJobSlots { get; set; } = -1;

        /// <summary>Default slot weight charged to heavy jobs when an explicit weight is omitted.</summary>
        public int HeavyJobSlotWeight { get; set; } = 1;

        /// <summary>Delay before the first retry when no slot is available.</summary>
        public int PollInitialDelayMs { get; set; } = 100;

        /// <summary>Maximum delay between slot-claim attempts.</summary>
        public int PollMaxDelayMs { get; set; } = 2000;

        /// <summary>Symmetric random jitter ratio from 0.0 through 1.0.</summary>
        public double PollJitterRatio { get; set; } = 0.2;

        /// <summary>Seconds before an unrenewed remote-node slot is considered abandoned.</summary>
        public int SlotLeaseSeconds { get; set; } = 60;

        /// <summary>Seconds between active slot lease renewals.</summary>
        public int SlotHeartbeatSeconds { get; set; } = 20;
    }

    public record JobThrottleMetrics(int ActiveJobs, int QueuedJobs, int MaxJobs, int AvailableSlots);
}
