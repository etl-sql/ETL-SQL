using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using ETL_SQL.Core.Spill;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// Tracks the PIDs of all child processes spawned by <see cref="ProcessJobExecutor"/>.
    ///
    /// Persistence: PIDs are written to a JSON file (<c>logs/child-pids.json</c>) on every
    /// register/unregister call. On startup, <see cref="CleanupOrphans"/> reads the file
    /// and kills any processes that are still running from a previous (crashed) Orchestrator
    /// session.
    ///
    /// Thread-safety: all operations are protected by a ConcurrentDictionary.
    /// </summary>
    public class ChildProcessTracker : IDisposable
    {
        private readonly string _persistPath;
        private readonly ILogger<ChildProcessTracker> _logger;
        private readonly ConcurrentDictionary<int, PersistedPid> _active = new();
        private readonly ITrackedChildProcessFactory _processes;
        private readonly object _storeLock = new();
        private readonly object _sweepLock = new();
        private readonly string _owner = $"{Environment.MachineName}/{Environment.UserDomainName}/{Environment.UserName}";
        private long _lastSweepUtcTicks;
        private int _sweepPending;
        private int _sweepInFlight;
        private int _sweepExecutionCount;
        private bool _timerArmed;
        private bool _disposed;
        private Timer? _coalesceTimer;

        /// <summary>Minimum interval between spill-directory sweeps triggered by exiting child processes.</summary>
        public TimeSpan SweepCoalesceInterval { get; set; } = TimeSpan.FromSeconds(5);

        internal string? SpillParentOverride { get; set; }
        internal int SweepExecutionCount => Volatile.Read(ref _sweepExecutionCount);

        public ChildProcessTracker(ILogger<ChildProcessTracker> logger, string? persistPath = null,
            ITrackedChildProcessFactory? processes = null, TimeSpan? sweepCoalesceInterval = null)
        {
            _logger = logger;
            _persistPath = Path.GetFullPath(persistPath ?? Path.Combine("logs", "child-pids.json"));
            _processes = processes ?? new TrackedChildProcessFactory();
            if (sweepCoalesceInterval.HasValue)
                SweepCoalesceInterval = sweepCoalesceInterval.Value;
        }

        /// <summary>
        /// Called at Orchestrator Service startup. Kills any child processes from a previous
        /// run that are still alive (orphan cleanup).
        /// </summary>
        public void CleanupOrphans()
        {
            try { KillOrphans(); }
            finally
            {
                // After the kill: a root belongs to a dead owner only once that owner is dead.
                ExecuteSpillSweep(forceTimestampUpdate: true);
            }
        }

        private void KillOrphans()
        {
            if (!File.Exists(_persistPath)) return;

            List<PersistedPid>? entries;
            try
            {
                var json = File.ReadAllText(_persistPath);
                entries = JsonSerializer.Deserialize<List<PersistedPid>>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read child PID store at {Path} — skipping orphan cleanup.", _persistPath);
                return;
            }

            if (entries == null || entries.Count == 0) return;

            _logger.LogInformation("Checking {Count} potentially orphaned child processes from previous run.", entries.Count);

            foreach (var entry in entries)
            {
                try
                {
                    if (entry.Identity == null || entry.Owner != _owner)
                    {
                        _logger.LogWarning("Skipping stale child metadata PID={Pid}: missing creation identity or different service owner.", entry.Pid);
                        continue;
                    }
                    using var p = _processes.Open(entry.Pid);
                    if (!p.HasExited)
                    {
                        var identity = p.Identity;
                        if (identity.StartedUtcTicks != entry.Identity.StartedUtcTicks
                            || !string.Equals(identity.ExecutablePath, entry.Identity.ExecutablePath,
                                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        {
                            _logger.LogWarning("Skipping stale child metadata PID={Pid}: creation time or executable changed.", entry.Pid);
                            continue;
                        }
                        _logger.LogWarning("Killing orphaned child process PID={Pid} (script={Script})", entry.Pid, entry.ScriptPath);
                        p.Kill();
                    }
                }
                catch (ArgumentException)
                {
                    // Process no longer exists — that's fine
                }
                catch (Exception ex)
                {
                    _active[entry.Pid] = entry;
                    _logger.LogWarning(ex, "Could not kill orphan PID={Pid}", entry.Pid);
                }
            }

            // Retain failed cleanup records for the next restart.
            Persist();
        }

        /// <summary>Records a newly spawned child process.</summary>
        public void Register(int pid, string scriptPath, long reservedMemoryBytes = 0, bool backedByBufferManager = false)
        {
            try
            {
                using var process = _processes.Open(pid);
                if (process.HasExited) return;
                // Ownership is established by the executor's registration of its newly spawned
                // child, bound to this service account and the exact creation/executable identity.
                _active[pid] = new PersistedPid(
                    pid,
                    scriptPath,
                    process.Identity,
                    _owner,
                    Math.Max(0L, reservedMemoryBytes),
                    backedByBufferManager);
                Persist();
            }
            catch (ArgumentException) { /* Child exited before registration. */ }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not identify child PID={Pid}; no unsafe PID-only record will be persisted.", pid);
            }
        }

        /// <summary>Removes a child process that has exited normally.</summary>
        public void Unregister(int pid)
        {
            _active.TryRemove(pid, out _);
            Persist();
            // A child that crashed or was killed cannot delete its own spill root; coalesce
            // sweeps during exit bursts so unregistering workers do not repeatedly walk %TEMP%.
            RequestCoalescedSpillSweep();
        }

        /// <summary>Returns the number of currently tracked active child processes.</summary>
        public int ActiveCount => _active.Count;

        /// <summary>
        /// Returns total memory reserved or held across all live tracked child workers.
        /// </summary>
        public long GetAggregateWorkerMemoryBytes() => ComputeWorkerMemoryBytes(unaccountedOnly: false);

        /// <summary>
        /// Returns worker memory not already reserved in the host's <see cref="BufferManager"/>
        /// (plus any live working-set excess beyond a worker's reservation).
        /// </summary>
        public long GetUnaccountedWorkerMemoryBytes() => ComputeWorkerMemoryBytes(unaccountedOnly: true);

        private long ComputeWorkerMemoryBytes(bool unaccountedOnly)
        {
            long total = 0;
            foreach (var entry in _active.Values)
            {
                long workingSet = 0;
                try
                {
                    if (entry.Identity is null)
                        continue;

                    using var process = _processes.Open(entry.Pid);
                    if (process.HasExited)
                        continue;

                    var identity = process.Identity;
                    if (identity.StartedUtcTicks != entry.Identity.StartedUtcTicks
                        || !string.Equals(identity.ExecutablePath, entry.Identity.ExecutablePath,
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        continue;
                    }

                    workingSet = Math.Max(0L, process.WorkingSetBytes);
                }
                catch
                {
                    continue;
                }

                long contribution;
                if (unaccountedOnly && entry.BackedByBufferManager)
                {
                    contribution = Math.Max(0L, workingSet - entry.ReservedMemoryBytes);
                }
                else
                {
                    contribution = Math.Max(entry.ReservedMemoryBytes, workingSet);
                }

                total = checked(total + contribution);
            }

            return total;
        }

        private void Persist()
        {
            lock (_storeLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_persistPath) ?? "logs");
                    var entries = new List<PersistedPid>();
                    foreach (var kv in _active)
                        entries.Add(kv.Value);

                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = false });
                    var temporaryPath = _persistPath + ".tmp";
                    File.WriteAllText(temporaryPath, json);
                    File.Move(temporaryPath, _persistPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist child PID store.");
                }
            }
        }

        private void RequestCoalescedSpillSweep()
        {
            var interval = SweepCoalesceInterval;
            if (interval <= TimeSpan.Zero)
            {
                ExecuteSpillSweep(forceTimestampUpdate: true);
                return;
            }

            long nowTicks = DateTime.UtcNow.Ticks;
            bool runNow = false;
            lock (_sweepLock)
            {
                if (_disposed)
                    return;

                long elapsedTicks = nowTicks - _lastSweepUtcTicks;
                if ((_lastSweepUtcTicks == 0 || elapsedTicks >= interval.Ticks)
                    && Volatile.Read(ref _sweepInFlight) == 0)
                {
                    _lastSweepUtcTicks = nowTicks;
                    Volatile.Write(ref _sweepPending, 0);
                    runNow = true;
                }
                else
                {
                    Volatile.Write(ref _sweepPending, 1);
                    if (!_timerArmed)
                    {
                        var delay = _lastSweepUtcTicks == 0 || elapsedTicks >= interval.Ticks
                            ? TimeSpan.FromMilliseconds(50)
                            : TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond, interval.Ticks - elapsedTicks));
                        _coalesceTimer ??= new Timer(OnCoalesceTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                        _coalesceTimer.Change(delay, Timeout.InfiniteTimeSpan);
                        _timerArmed = true;
                    }
                }
            }

            if (runNow)
                ExecuteSpillSweep(forceTimestampUpdate: false);
        }

        private void OnCoalesceTimer(object? _)
        {
            lock (_sweepLock)
            {
                _timerArmed = false;
                if (_disposed || Interlocked.Exchange(ref _sweepPending, 0) == 0)
                    return;
                _lastSweepUtcTicks = DateTime.UtcNow.Ticks;
            }

            ExecuteSpillSweep(forceTimestampUpdate: false);
        }

        internal void FlushCoalescedSweep()
        {
            bool hadPending;
            lock (_sweepLock)
            {
                _timerArmed = false;
                _coalesceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                hadPending = Interlocked.Exchange(ref _sweepPending, 0) != 0;
                if (hadPending)
                    _lastSweepUtcTicks = DateTime.UtcNow.Ticks;
            }

            if (hadPending)
                ExecuteSpillSweep(forceTimestampUpdate: false);
        }

        private void ExecuteSpillSweep(bool forceTimestampUpdate)
        {
            if (Interlocked.CompareExchange(ref _sweepInFlight, 1, 0) != 0)
            {
                Volatile.Write(ref _sweepPending, 1);
                return;
            }

            try
            {
                if (forceTimestampUpdate)
                {
                    lock (_sweepLock)
                        _lastSweepUtcTicks = DateTime.UtcNow.Ticks;
                }

                SweepTempSpillDirectories();
                Interlocked.Increment(ref _sweepExecutionCount);
            }
            finally
            {
                Interlocked.Exchange(ref _sweepInFlight, 0);
            }
        }

        private void SweepTempSpillDirectories() =>
            SweepSpillRoots(
                SpillParentOverride ?? Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill"),
                TimeSpan.FromHours(24),
                _logger);

        /// <summary>
        /// Removes disposable spill roots under <paramref name="spillParent"/> whose owning process is
        /// gone (see <see cref="SpillRootOwnership"/>), and roots older than <paramref name="maxAge"/>
        /// that carry no owner marker. Returns the number of roots removed.
        /// </summary>
        public static int SweepSpillRoots(string spillParent, TimeSpan maxAge, ILogger logger)
        {
            if (!Directory.Exists(spillParent)) return 0;
            var pruned = 0;
            try
            {
                var cutoff = DateTime.UtcNow - maxAge;
                var trustMarkers = SpillRootOwnership.ParentIsPrivate(spillParent);
                if (!trustMarkers)
                    logger.LogWarning(
                        "Spill directory {Path} is writable by other users; its owner markers are ignored and only roots older than {MaxAge} are removed.",
                        spillParent, maxAge);
                foreach (var dir in Directory.GetDirectories(spillParent))
                {
                    try
                    {
                        // Never follow a link out of the spill directory.
                        if (new DirectoryInfo(dir).LinkTarget is not null) continue;
                        // A live owner keeps its root whatever its age: a long job is not an orphan.
                        var marked = trustMarkers && File.Exists(SpillRootOwnership.MarkerPath(dir));
                        var remove = marked
                            ? SpillRootOwnership.IsAbandoned(dir)
                            : Directory.GetLastWriteTimeUtc(dir) < cutoff;
                        if (!remove) continue;
                        Directory.Delete(dir, true);
                        SpillRootOwnership.Release(dir);
                        pruned++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogDebug(ex, "Failed to delete orphaned temporary spill directory: {Path}", dir);
                    }
                }

                // A marker whose root is already gone is left by a cleanup interrupted between the two deletes.
                foreach (var marker in trustMarkers ? Directory.GetFiles(spillParent, "*" + SpillRootOwnership.MarkerSuffix) : [])
                {
                    var root = marker[..^SpillRootOwnership.MarkerSuffix.Length];
                    if (!Directory.Exists(root)) SpillRootOwnership.Release(root);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Error sweeping temporary spill directories.");
            }
            if (pruned > 0)
                logger.LogInformation("Removed {Count} orphaned temporary spill directories from {Path}.", pruned, spillParent);
            return pruned;
        }

        public void Dispose()
        {
            lock (_sweepLock)
            {
                if (_disposed) return;
                _disposed = true;
                _timerArmed = false;
                _sweepPending = 0;
                _coalesceTimer?.Dispose();
                _coalesceTimer = null;
            }
        }

        private record PersistedPid(
            int Pid,
            string ScriptPath,
            ChildProcessIdentity? Identity = null,
            string? Owner = null,
            long ReservedMemoryBytes = 0,
            bool BackedByBufferManager = false);
    }
}
