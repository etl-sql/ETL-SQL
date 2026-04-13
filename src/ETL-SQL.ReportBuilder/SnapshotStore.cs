using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.ReportBuilder
{
    /// <summary>
    /// Persists and loads a <see cref="ReportManifest"/> snapshot to/from disk.
    ///
    /// Snapshot files are plain JSON, stored alongside the .rptsql script as
    /// <c>&lt;scriptName&gt;.snapshot.json</c> by default.
    ///
    /// Used by <c>etl-sql-report refresh</c> to determine staleness and update
    /// the <see cref="DatasetManifest.LastRefresh"/> timestamps.
    /// </summary>
    public class SnapshotStore
    {
        private static readonly SemaphoreSlim _readLock = new(1, 1);
        private static readonly SemaphoreSlim _writeLock = new(1, 1);
        private static int _readerCount = 0;

        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented        = true,
            PropertyNamingPolicy = null
        };

        /// <summary>Derives the default snapshot path from a script path.</summary>
        public static string DefaultPath(string scriptPath) =>
            Path.ChangeExtension(scriptPath, null) + ".snapshot.json";

        /// <summary>Serialises the manifest to a JSON file atomically.</summary>
        public async Task SaveAsync(ReportManifest manifest, string outputPath)
        {
            await _writeLock.WaitAsync();
            var tmpPath = outputPath + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(manifest, _opts);
                await File.WriteAllTextAsync(tmpPath, json);
                
                // Atomic rename/replace
                if (File.Exists(outputPath)) File.Delete(outputPath);
                File.Move(tmpPath, outputPath);
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { /* ignore cleanup errors */ }
                }
                _writeLock.Release();
            }
        }


        /// <summary>
        /// Deserialises a previously saved manifest. Returns null if the file does not exist.
        /// Supports parallel reads while blocking for writes.
        /// </summary>
        public async Task<ReportManifest?> LoadAsync(string snapshotPath)
        {
            await _readLock.WaitAsync();
            if (++_readerCount == 1) await _writeLock.WaitAsync();
            _readLock.Release();

            try
            {
                if (!File.Exists(snapshotPath)) return null;
                var json = await File.ReadAllTextAsync(snapshotPath);
                return JsonSerializer.Deserialize<ReportManifest>(json, _opts);
            }
            finally
            {
                await _readLock.WaitAsync();
                if (--_readerCount == 0) _writeLock.Release();
                _readLock.Release();
            }
        }

        /// <summary>
        /// Checks whether the snapshot is stale relative to the script's last-write time.
        /// Returns true if the snapshot is older than the script file or the TTL has elapsed.
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
    }
}
