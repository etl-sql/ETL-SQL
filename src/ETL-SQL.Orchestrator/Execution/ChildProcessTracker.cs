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
        private readonly ConcurrentDictionary<int, string> _active = new(); // pid → script path

        public ChildProcessTracker(ILogger<ChildProcessTracker> logger, string? persistPath = null)
        {
            _logger = logger;
            _persistPath = persistPath ?? Path.Combine("logs", "child-pids.json");
        }

        /// <summary>
        /// Called at Orchestrator Service startup. Kills any child processes from a previous
        /// run that are still alive (orphan cleanup).
        /// </summary>
        public void CleanupOrphans()
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
                    var p = Process.GetProcessById(entry.Pid);
                    if (!p.HasExited)
                    {
                        _logger.LogWarning("Killing orphaned child process PID={Pid} (script={Script})", entry.Pid, entry.ScriptPath);
                        p.Kill(entireProcessTree: true);
                    }
                }
                catch (ArgumentException)
                {
                    // Process no longer exists — that's fine
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not kill orphan PID={Pid}", entry.Pid);
                }
            }

            // Clear the store after cleanup
            try { File.Delete(_persistPath); } catch { /* best effort */ }
        }

        /// <summary>Records a newly spawned child process.</summary>
        public void Register(int pid, string scriptPath)
        {
            _active[pid] = scriptPath;
            Persist();
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
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_persistPath) ?? "logs");
                var entries = new List<PersistedPid>();
                foreach (var kv in _active)
                    entries.Add(new PersistedPid(kv.Key, kv.Value));

                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(_persistPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist child PID store.");
            }
        }

        private record PersistedPid(int Pid, string ScriptPath);
    }
}
