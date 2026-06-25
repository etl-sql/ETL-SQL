using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Persists and loads a <see cref="ReportManifest"/> snapshot to/from disk.
    /// 
    /// Rpt-2 Hardening: Uses atomic move operations and path-based async reader-writer locks
    /// to ensure integrity during concurrent dashboard access and background refreshes.
    /// </summary>
    public class SnapshotStore
    {
        // Path-based locking registry to allow parallel processing of different reports
        private static readonly ConcurrentDictionary<string, AsyncReaderWriterLock> _pathLocks = new();

        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null
        };

        /// <summary>Derives the default snapshot path from a script path.</summary>
        public static string DefaultPath(string scriptPath) =>
            Path.ChangeExtension(scriptPath, null) + ".snapshot.json";

        /// <summary>Serialises the manifest to a JSON file atomically.</summary>
        public async Task SaveAsync(ReportManifest manifest, string outputPath)
        {
            var fullPath = Path.GetFullPath(outputPath);
            var lockObj = _pathLocks.GetOrAdd(fullPath, _ => new AsyncReaderWriterLock());

            await lockObj.WriterLock.WaitAsync();
            // Use a per-write unique temp path so concurrent writes from different
            // processes do not overwrite each other's in-progress temp file.
            var tmpPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    bool dirExists = await Task.Run(() => Directory.Exists(dir));
                    if (!dirExists)
                    {
                        await Task.Run(() => Directory.CreateDirectory(dir));
                    }
                }

                var json = JsonSerializer.Serialize(manifest, _opts);
                await File.WriteAllTextAsync(tmpPath, json);

                // Atomic Replace: File.Move with overwrite handles the atomic swap on most filesystems.
                // Last-writer-wins across processes — both produce valid snapshots, so no corruption.
                await Task.Run(() => File.Move(tmpPath, fullPath, overwrite: true));
            }
            finally
            {
                bool tmpExists = await Task.Run(() => File.Exists(tmpPath));
                if (tmpExists)
                {
                    try { await Task.Run(() => File.Delete(tmpPath)); } catch { /* ignore cleanup errors */ }
                }
                lockObj.WriterLock.Release();
            }
        }

        /// <summary>
        /// Deserialises a previously saved manifest. Returns null if the file does not exist.
        /// Supports parallel reads while blocking for writes (per-file).
        /// </summary>
        public async Task<ReportManifest?> LoadAsync(string snapshotPath)
        {
            var fullPath = Path.GetFullPath(snapshotPath);
            var lockObj = _pathLocks.GetOrAdd(fullPath, _ => new AsyncReaderWriterLock());

            await lockObj.EnterReadLockAsync();
            try
            {
                bool exists = await Task.Run(() => File.Exists(fullPath));
                if (!exists) return null;
                var json = await File.ReadAllTextAsync(fullPath);
                return JsonSerializer.Deserialize<ReportManifest>(json, _opts);
            }
            catch (JsonException)
            {
                // If the JSON is corrupt (e.g. partial write during unexpected crash),
                // treat as missing so it can be rebuilt.
                return null;
            }
            finally
            {
                await lockObj.ExitReadLockAsync();
            }
        }

        /// <summary>
        /// Scans for and removes orphaned .tmp files in the specified directory.
        /// </summary>
        public static void CleanupOrphanedSnapshots(string directory)
        {
            if (!Directory.Exists(directory)) return;

            // Match both old *.snapshot.json.tmp and new *.snapshot.json.tmp.<guid> patterns.
            foreach (var file in Directory.GetFiles(directory, "*.snapshot.json.tmp*"))
            {
                try { File.Delete(file); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Checks whether the snapshot is stale relative to the script's last-write time.
        /// </summary>
        public bool IsStale(ReportManifest manifest, string scriptPath, TimeSpan? ttl = null)
        {
            if (File.Exists(scriptPath))
            {
                var scriptWrite = File.GetLastWriteTimeUtc(scriptPath);
                if (manifest.BuiltAt < scriptWrite) return true;
            }

            if (ttl.HasValue && (DateTime.UtcNow - manifest.BuiltAt) > ttl.Value)
                return true;

            return false;
        }

        /// <summary>
        /// Internal implementation of an async-friendly Reader-Writer lock using Nested Semaphores.
        /// avoids the blocking behavior of ReaderWriterLockSlim.
        /// </summary>
        private class AsyncReaderWriterLock
        {
            private readonly SemaphoreSlim _readLock = new(1, 1);
            public readonly SemaphoreSlim WriterLock = new(1, 1);
            private int _readerCount = 0;

            public async Task EnterReadLockAsync()
            {
                await _readLock.WaitAsync();
                if (++_readerCount == 1) await WriterLock.WaitAsync();
                _readLock.Release();
            }

            public async Task ExitReadLockAsync()
            {
                await _readLock.WaitAsync();
                if (--_readerCount == 0) WriterLock.Release();
                _readLock.Release();
            }
        }
    }
}
