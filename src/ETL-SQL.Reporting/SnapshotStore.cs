using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Storage;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Services;

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

        /// <summary>Derives a partitioned default snapshot path from a script path.</summary>
        public static string DefaultPath(string scriptPath)
        {
            var fullScriptPath = Path.GetFullPath(scriptPath);
            var scriptDir = Path.GetDirectoryName(fullScriptPath) ?? "";
            var snapshotName = Path.GetFileNameWithoutExtension(fullScriptPath) + ".etlsnap";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullScriptPath)))
                .ToLowerInvariant();
            return Path.Combine(scriptDir, ".etlsnap", hash[..2], hash[2..4], snapshotName);
        }

        /// <summary>Serialises the manifest to a JSON file atomically.</summary>
        public async Task SaveAsync(ReportManifest manifest, string outputPath)
        {
            var fullPath = Path.GetFullPath(outputPath);
            var lockObj = _pathLocks.GetOrAdd(fullPath, _ => new AsyncReaderWriterLock());

            await lockObj.WriterLock.WaitAsync();
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

                if (fullPath.EndsWith(".etlsnap", StringComparison.OrdinalIgnoreCase))
                {
                    var directory = dir ?? "";
                    var filename = Path.GetFileName(fullPath);
                    var storage = new FileSystemArtifactStorage(new Dictionary<ArtifactArea, string> {
                        { ArtifactArea.Snapshots, directory }
                    });
                    var service = new SnapshotPackageService(
                        new PortalConfig(),
                        storage,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SnapshotPackageService>.Instance);
                    await service.SaveAsync(manifest, filename);
                }
                else
                {
                    // Use a per-write unique temp path so concurrent writes from different
                    // processes do not overwrite each other's in-progress temp file.
                    var tmpPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
                    try
                    {
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
                    }
                }
            }
            finally
            {
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
                if (!exists)
                {
                    // If .etlsnap requested but only legacy .snapshot.json or .json exists, load legacy
                    if (fullPath.EndsWith(".etlsnap", StringComparison.OrdinalIgnoreCase))
                    {
                        var legacyPackagePath = LegacyFlatSnapshotPath(fullPath);
                        if (legacyPackagePath is not null && await Task.Run(() => File.Exists(legacyPackagePath)))
                        {
                            var directory = Path.GetDirectoryName(legacyPackagePath) ?? "";
                            var filename = Path.GetFileName(legacyPackagePath);
                            var storage = new FileSystemArtifactStorage(new Dictionary<ArtifactArea, string> {
                                { ArtifactArea.Snapshots, directory }
                            });
                            var service = new SnapshotPackageService(
                                new PortalConfig(),
                                storage,
                                Microsoft.Extensions.Logging.Abstractions.NullLogger<SnapshotPackageService>.Instance);
                            return await service.LoadAsync(filename);
                        }

                        var legacyPath = Path.ChangeExtension(fullPath, ".snapshot.json");
                        if (await Task.Run(() => File.Exists(legacyPath)))
                        {
                            var json = await File.ReadAllTextAsync(legacyPath);
                            return JsonSerializer.Deserialize<ReportManifest>(json, _opts);
                        }

                        var jsonPath = Path.ChangeExtension(fullPath, ".json");
                        if (await Task.Run(() => File.Exists(jsonPath)))
                        {
                            var json = await File.ReadAllTextAsync(jsonPath);
                            return JsonSerializer.Deserialize<ReportManifest>(json, _opts);
                        }

                        if (legacyPackagePath is not null)
                        {
                            legacyPath = Path.ChangeExtension(legacyPackagePath, ".snapshot.json");
                            if (await Task.Run(() => File.Exists(legacyPath)))
                            {
                                var json = await File.ReadAllTextAsync(legacyPath);
                                return JsonSerializer.Deserialize<ReportManifest>(json, _opts);
                            }

                            jsonPath = Path.ChangeExtension(legacyPackagePath, ".json");
                            if (await Task.Run(() => File.Exists(jsonPath)))
                            {
                                var json = await File.ReadAllTextAsync(jsonPath);
                                return JsonSerializer.Deserialize<ReportManifest>(json, _opts);
                            }
                        }
                    }
                    return null;
                }

                if (fullPath.EndsWith(".etlsnap", StringComparison.OrdinalIgnoreCase))
                {
                    var directory = Path.GetDirectoryName(fullPath) ?? "";
                    var filename = Path.GetFileName(fullPath);
                    var storage = new FileSystemArtifactStorage(new Dictionary<ArtifactArea, string> {
                        { ArtifactArea.Snapshots, directory }
                    });
                    var service = new SnapshotPackageService(
                        new PortalConfig(),
                        storage,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SnapshotPackageService>.Instance);
                    return await service.LoadAsync(filename);
                }
                else
                {
                    var json = await File.ReadAllTextAsync(fullPath);
                    return JsonSerializer.Deserialize<ReportManifest>(json, _opts);
                }
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

            // Match both old *.snapshot.json.tmp and new unique temp-file patterns under flat
            // or partitioned snapshot directories.
            foreach (var file in Directory.EnumerateFiles(directory, "*.snapshot.json.tmp*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); } catch { /* ignore */ }
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.etlsnap.tmp*", SearchOption.AllDirectories))
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

        private static string? LegacyFlatSnapshotPath(string partitionedPath)
        {
            var leafDir = Directory.GetParent(partitionedPath);
            var secondPartition = leafDir?.Parent;
            var firstPartition = secondPartition?.Parent;
            var root = firstPartition?.Parent;
            return root is not null
                && string.Equals(firstPartition!.Name, ".etlsnap", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(root.FullName, Path.GetFileName(partitionedPath))
                : null;
        }
    }
}
