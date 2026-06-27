using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
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
        private readonly IOrchestratorStoreDialect _dialect;
        private readonly ILogger<JobThrottle> _logger;
        private readonly int _pollInitialDelayMs;
        private readonly int _pollMaxDelayMs;
        private readonly double _pollJitterRatio;
        private readonly TimeSpan _slotLease;
        private readonly TimeSpan _slotHeartbeat;
        private static readonly int _pid = Environment.ProcessId;

        private int _activeJobs;
        private int _queuedJobs;
        private bool _tableReady;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public JobThrottle(IOptions<JobThrottleOptions> options, ILogger<JobThrottle> logger)
            : this(options, logger, new ConfigurationBuilder().Build())
        {
        }

        public JobThrottle(IOptions<JobThrottleOptions> options, ILogger<JobThrottle> logger, IConfiguration configuration)
        {
            _maxConcurrent = options.Value.MaxConcurrentJobs > 0
                ? options.Value.MaxConcurrentJobs
                : Math.Max(1, Environment.ProcessorCount / 2);
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
                var dbPath = configuration["Orchestrator:DatabasePath"] ?? SQLiteJobHistoryStore.DefaultDbPath();
                _dialect = new SqliteOrchestratorDialect($"Data Source={dbPath}");
            }
            _logger = logger;
            _logger.LogInformation("JobThrottle initialized: max_concurrent_jobs={Max}, poll={Initial}-{MaxDelay}ms, jitter={Jitter:P0} (cross-process, pid={Pid})",
                _maxConcurrent, _pollInitialDelayMs, _pollMaxDelayMs, _pollJitterRatio, _pid);
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
                    CREATE TABLE IF NOT EXISTS ThrottleSlots (
                        Id          {_dialect.AutoIncrementPrimaryKey},
                        ProcessId   INTEGER NOT NULL,
                        JobName     TEXT    NOT NULL,
                        AcquiredAt  TEXT    NOT NULL,
                        MachineName TEXT    DEFAULT ''
                    );";
                await cmd.ExecuteNonQueryAsync();

                var columns = await _dialect.GetColumnNamesAsync(conn, "ThrottleSlots");
                if (!columns.Contains("MachineName"))
                {
                    cmd.CommandText = "ALTER TABLE ThrottleSlots ADD COLUMN MachineName TEXT DEFAULT '';";
                    await cmd.ExecuteNonQueryAsync();
                }

                _tableReady = true;
            }
            finally { _initLock.Release(); }
        }

        /// <summary>
        /// Acquires a concurrency slot, waiting until one is available across all processes.
        /// Returns a disposable that releases the slot on disposal.
        /// </summary>
        public async Task<IDisposable> AcquireAsync(string jobName, CancellationToken ct = default)
        {
            await EnsureTableAsync();
            Interlocked.Increment(ref _queuedJobs);
            var failedAttempts = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    long? slotId = await TryClaimSlotAsync(jobName);
                    if (slotId.HasValue)
                    {
                        Interlocked.Decrement(ref _queuedJobs);
                        var active = Interlocked.Increment(ref _activeJobs);
                        _logger.LogInformation("Job {JobName} started (active={Active}/{Cap}, pid={Pid})",
                            jobName, active, _maxConcurrent, _pid);
                        return new Slot(this, jobName, slotId.Value);
                    }

                    _logger.LogDebug("Job {JobName} waiting for slot (queued={Queued}, cap={Cap})",
                        jobName, _queuedJobs, _maxConcurrent);
                    await Task.Delay(CalculatePollDelay(failedAttempts++), ct);
                }

                ct.ThrowIfCancellationRequested();
                throw new OperationCanceledException(ct);
            }
            catch
            {
                Interlocked.Decrement(ref _queuedJobs);
                throw;
            }
        }

        private async Task<long?> TryClaimSlotAsync(string jobName)
        {
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
                countCmd.CommandText = "SELECT COUNT(*) FROM ThrottleSlots;";
                var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                if (count >= _maxConcurrent)
                {
                    tx.Rollback();
                    return null;
                }

                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = _dialect.InsertReturningId(@"
                    INSERT INTO ThrottleSlots (ProcessId, JobName, AcquiredAt, MachineName)
                    VALUES (@pid, @job, @at, @machine)", "Id");
                insertCmd.AddParam("@pid", _pid);
                insertCmd.AddParam("@job", jobName);
                insertCmd.AddParam("@at", DateTime.UtcNow.ToString("O"));
                insertCmd.AddParam("@machine", Environment.MachineName);
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
                expired.CommandText = "DELETE FROM ThrottleSlots WHERE MachineName != @machine AND AcquiredAt < @cutoff;";
                expired.AddParam("@machine", Environment.MachineName);
                expired.AddParam("@cutoff", DateTime.UtcNow.Subtract(_slotLease).ToString("O"));
                await expired.ExecuteNonQueryAsync();
            }

            var pids = new List<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT ProcessId FROM ThrottleSlots WHERE ProcessId != @own AND (MachineName = @machine OR MachineName IS NULL OR MachineName = '');";
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
                    del.CommandText = "DELETE FROM ThrottleSlots WHERE ProcessId = @pid AND (MachineName = @machine OR MachineName IS NULL OR MachineName = '');";
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

        private async Task ReleaseAsync(string jobName, long slotId)
        {
            try
            {
                using var conn = _dialect.CreateConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM ThrottleSlots WHERE Id = @id;";
                cmd.AddParam("@id", slotId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release throttle slot {SlotId} for job {JobName}", slotId, jobName);
            }
            finally
            {
                var active = Interlocked.Decrement(ref _activeJobs);
                _logger.LogInformation("Job {JobName} released slot (active={Active}/{Cap})", jobName, active, _maxConcurrent);
            }
        }

        private async Task RenewSlotAsync(long slotId, CancellationToken cancellationToken)
        {
            using var conn = _dialect.CreateConnection();
            await conn.OpenAsync(cancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ThrottleSlots SET AcquiredAt = @at WHERE Id = @id;";
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

        private sealed class Slot : IDisposable
        {
            private readonly JobThrottle _owner;
            private readonly string _jobName;
            private readonly long _slotId;
            private readonly CancellationTokenSource _heartbeatCancellation = new();
            private bool _disposed;

            public Slot(JobThrottle owner, string jobName, long slotId)
            {
                _owner = owner;
                _jobName = jobName;
                _slotId = slotId;
                _ = HeartbeatAsync();
            }

            private async Task HeartbeatAsync()
            {
                try
                {
                    while (!_heartbeatCancellation.IsCancellationRequested)
                    {
                        await Task.Delay(_owner._slotHeartbeat, _heartbeatCancellation.Token);
                        await _owner.RenewSlotAsync(_slotId, _heartbeatCancellation.Token);
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
                if (_disposed) return;
                _disposed = true;
                _heartbeatCancellation.Cancel();
                _ = _owner.ReleaseAsync(_jobName, _slotId);
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
