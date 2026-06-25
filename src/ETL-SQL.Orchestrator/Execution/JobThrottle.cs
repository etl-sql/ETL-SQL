using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// Enforces a configurable cap on concurrently running jobs across ALL orchestrator
    /// processes on the same machine. Slot counts are persisted in the shared SQLite DB
    /// so two orchestrator instances cannot collectively exceed MaxConcurrentJobs.
    ///
    /// Within-process queuing: <see cref="AcquireAsync"/> polls the DB every 500 ms
    /// until a slot becomes available (or cancellation is requested).
    ///
    /// Crash safety: on each acquire attempt, slots owned by processes that no longer
    /// exist are purged automatically so a crashed peer never permanently blocks a slot.
    /// </summary>
    public class JobThrottle : IDisposable
    {
        private readonly int _maxConcurrent;
        private readonly string _connectionString;
        private readonly ILogger<JobThrottle> _logger;
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

            var dbPath = configuration["Orchestrator:DatabasePath"];
            _connectionString = $"Data Source={dbPath ?? SQLiteJobHistoryStore.DefaultDbPath()}";
            _logger = logger;
            _logger.LogInformation("JobThrottle initialized: max_concurrent_jobs={Max} (cross-process, pid={Pid})",
                _maxConcurrent, _pid);
        }

        private async Task EnsureTableAsync()
        {
            if (_tableReady) return;
            await _initLock.WaitAsync();
            try
            {
                if (_tableReady) return;
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ThrottleSlots (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        ProcessId   INTEGER NOT NULL,
                        JobName     TEXT    NOT NULL,
                        AcquiredAt  TEXT    NOT NULL,
                        MachineName TEXT    DEFAULT ''
                    );";
                await cmd.ExecuteNonQueryAsync();

                // Add MachineName column to existing tables for backwards compatibility
                try
                {
                    cmd.CommandText = "ALTER TABLE ThrottleSlots ADD COLUMN MachineName TEXT DEFAULT '';";
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
                {
                    // Column already exists, ignore
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
                    await Task.Delay(500, ct);
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
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // BEGIN EXCLUSIVE locks the DB file for the duration of this check+insert,
            // preventing another process from racing through the same window.
            using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                await PurgeStaleSlotsAsync(conn, tx);

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
                insertCmd.CommandText = @"
                    INSERT INTO ThrottleSlots (ProcessId, JobName, AcquiredAt, MachineName)
                    VALUES (@pid, @job, @at, @machine);
                    SELECT last_insert_rowid();";
                insertCmd.Parameters.AddWithValue("@pid", _pid);
                insertCmd.Parameters.AddWithValue("@job", jobName);
                insertCmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("O"));
                insertCmd.Parameters.AddWithValue("@machine", Environment.MachineName);
                var id = Convert.ToInt64(await insertCmd.ExecuteScalarAsync()!);

                tx.Commit();
                return id;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 5 /* SQLITE_BUSY */)
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

        private static async Task PurgeStaleSlotsAsync(SqliteConnection conn, SqliteTransaction tx)
        {
            var pids = new List<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT DISTINCT ProcessId FROM ThrottleSlots WHERE ProcessId != @own AND (MachineName = @machine OR MachineName IS NULL OR MachineName = '');";
                cmd.Parameters.AddWithValue("@own", _pid);
                cmd.Parameters.AddWithValue("@machine", Environment.MachineName);
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) pids.Add(r.GetInt32(0));
            }

            foreach (var pid in pids)
            {
                bool alive;
                try { Process.GetProcessById(pid); alive = true; }
                catch (ArgumentException) { alive = false; }

                if (!alive)
                {
                    using var del = conn.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM ThrottleSlots WHERE ProcessId = @pid AND (MachineName = @machine OR MachineName IS NULL OR MachineName = '');";
                    del.Parameters.AddWithValue("@pid", pid);
                    del.Parameters.AddWithValue("@machine", Environment.MachineName);
                    await del.ExecuteNonQueryAsync();
                    // Note: no logging inside the exclusive lock to keep it short
                }
            }
        }

        private async Task ReleaseAsync(string jobName, long slotId)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM ThrottleSlots WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", slotId);
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
            private bool _disposed;

            public Slot(JobThrottle owner, string jobName, long slotId)
            {
                _owner = owner;
                _jobName = jobName;
                _slotId = slotId;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
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
    }

    public record JobThrottleMetrics(int ActiveJobs, int QueuedJobs, int MaxJobs, int AvailableSlots);
}
