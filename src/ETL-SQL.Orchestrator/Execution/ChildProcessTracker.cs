using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
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
    public class ChildProcessTracker
    {
        private readonly string _persistPath;
        private readonly ILogger<ChildProcessTracker> _logger;
        private readonly ConcurrentDictionary<int, PersistedPid> _active = new();
        private readonly ITrackedChildProcessFactory _processes;
        private readonly object _storeLock = new();
        private readonly string _owner = $"{Environment.MachineName}/{Environment.UserDomainName}/{Environment.UserName}";

        public ChildProcessTracker(ILogger<ChildProcessTracker> logger, string? persistPath = null,
            ITrackedChildProcessFactory? processes = null)
        {
            _logger = logger;
            _persistPath = Path.GetFullPath(persistPath ?? Path.Combine("logs", "child-pids.json"));
            _processes = processes ?? new TrackedChildProcessFactory();
        }

        /// <summary>
        /// Called at Orchestrator Service startup. Kills any child processes from a previous
        /// run that are still alive (orphan cleanup).
        /// </summary>
        public void CleanupOrphans()
        {
            SweepTempSpillDirectories();

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
        public void Register(int pid, string scriptPath)
        {
            try
            {
                using var process = _processes.Open(pid);
                if (process.HasExited) return;
                // Ownership is established by the executor's registration of its newly spawned
                // child, bound to this service account and the exact creation/executable identity.
                _active[pid] = new PersistedPid(pid, scriptPath, process.Identity, _owner);
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
        }

        /// <summary>Returns the number of currently tracked active child processes.</summary>
        public int ActiveCount => _active.Count;

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

        private void SweepTempSpillDirectories()
        {
            try
            {
                var tempSpillRoot = Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill");
                if (Directory.Exists(tempSpillRoot))
                {
                    _logger.LogInformation("Sweeping orphaned temporary spill directories in {Path}...", tempSpillRoot);
                    var directories = Directory.GetDirectories(tempSpillRoot);
                    var cutoff = DateTime.UtcNow.AddHours(-24);
                    int prunedCount = 0;

                    foreach (var dir in directories)
                    {
                        try
                        {
                            var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                            if (lastWrite < cutoff)
                            {
                                Directory.Delete(dir, true);
                                prunedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to delete orphaned temporary spill directory: {Path}", dir);
                        }
                    }

                    if (prunedCount > 0)
                    {
                        _logger.LogInformation("Pruned {Count} stale temporary spill directories.", prunedCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error sweeping temporary spill directories.");
            }
        }

        private record PersistedPid(int Pid, string ScriptPath, ChildProcessIdentity? Identity = null, string? Owner = null);
    }
}
