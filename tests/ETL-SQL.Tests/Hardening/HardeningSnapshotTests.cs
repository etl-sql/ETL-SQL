using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.ReportBuilder;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class SnapshotSafetyTests : IDisposable
    {
        private readonly string _testDir;

        public SnapshotSafetyTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-SnapshotTests-" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, true); } catch { }
            }
        }

        [Fact]
        public async Task ConcurrentReadWrite_HighLoad_EnsuresIntegrity()
        {
            var store = new SnapshotStore();
            var snapshotPath = Path.Combine(_testDir, "stress.snapshot.json");
            var manifest = new ReportManifest { Source = "Source", BuiltAt = DateTime.UtcNow };

            // Initial save
            await store.SaveAsync(manifest, snapshotPath);

            int readerCount = 30;
            int writerCount = 10;
            int iterationsPerTask = 20;

            var tasks = new List<Task>();

            // Reader tasks
            for (int i = 0; i < readerCount; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < iterationsPerTask; j++)
                    {
                        var loaded = await store.LoadAsync(snapshotPath);
                        Assert.NotNull(loaded);
                        Assert.Equal("Source", loaded.Source);
                        await Task.Yield();
                    }
                }));
            }

            // Writer tasks
            for (int i = 0; i < writerCount; i++)
            {
                int writerId = i;
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < iterationsPerTask; j++)
                    {
                        var m = new ReportManifest { Source = "Source", BuiltAt = DateTime.UtcNow };
                        // We can't easily verify the row count if multiple writers are racing,
                        // but we can verify the JSON never corrupts.
                        await store.SaveAsync(m, snapshotPath);
                        await Task.Yield();
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }

        [Fact]
        public async Task AtomicReplace_CrashSimulation_LeavesOriginalIntact()
        {
            var store = new SnapshotStore();
            var snapshotPath = Path.Combine(_testDir, "atomic.snapshot.json");
            var originalManifest = new ReportManifest { Source = "Original", BuiltAt = DateTime.UtcNow.AddMinutes(-10) };

            await store.SaveAsync(originalManifest, snapshotPath);

            // Verify original exists
            var loaded = await store.LoadAsync(snapshotPath);
            Assert.Equal("Original", loaded.Source);

            // Simulate a crashed write by leaving a corrupt file at the old fixed .tmp path.
            // Each SaveAsync now uses a unique GUID-based temp path, so the corrupt file
            // is NOT touched by a subsequent save — it is cleaned up by CleanupOrphanedSnapshots.
            var corruptTmpPath = snapshotPath + ".tmp";
            File.WriteAllText(corruptTmpPath, "CORRUPT PARTIAL JSON");

            // Load should still return Original because the corrupt tmp file isn't the real file
            loaded = await store.LoadAsync(snapshotPath);
            Assert.Equal("Original", loaded.Source);

            // A new save succeeds and leaves the original intact until the atomic move.
            var newManifest = new ReportManifest { Source = "New", BuiltAt = DateTime.UtcNow };
            await store.SaveAsync(newManifest, snapshotPath);

            loaded = await store.LoadAsync(snapshotPath);
            Assert.Equal("New", loaded.Source);

            // The corrupt file from the simulated crash is NOT cleaned by SaveAsync (it uses a
            // unique temp path now). CleanupOrphanedSnapshots handles leftover temp files.
            SnapshotStore.CleanupOrphanedSnapshots(_testDir);
            Assert.False(File.Exists(corruptTmpPath));
        }

        [Fact]
        public void CleanupOrphanedSnapshots_RemovesTmpFiles()
        {
            var tmp1 = Path.Combine(_testDir, "a.snapshot.json.tmp");
            var tmp2 = Path.Combine(_testDir, "b.snapshot.json.tmp");
            var real = Path.Combine(_testDir, "c.snapshot.json");

            File.WriteAllText(tmp1, "junk");
            File.WriteAllText(tmp2, "junk");
            File.WriteAllText(real, "{}");

            SnapshotStore.CleanupOrphanedSnapshots(_testDir);

            Assert.False(File.Exists(tmp1));
            Assert.False(File.Exists(tmp2));
            Assert.True(File.Exists(real));
        }
    }
}
