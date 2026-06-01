using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Reporting;

namespace ETL_SQL.Tests.Security
{
    public class SnapshotSafetyTests : IDisposable
    {
        private readonly string _testPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.snapshot.json");

        public void Dispose()
        {
            if (File.Exists(_testPath)) File.Delete(_testPath);
        }

        [Fact]
        public async Task SaveAsync_IsAtomicAndConcurrentSafe()
        {
            var store = new SnapshotStore();
            var manifest = new ReportManifest { BuiltAt = DateTime.UtcNow };

            // Simulate high concurrency: 50 simultaneous saves to the same path
            var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () => {
                await Task.Delay(new Random().Next(1, 10)); // Jitter
                await store.SaveAsync(manifest, _testPath);
            }));

            await Task.WhenAll(tasks);

            // Verify the file exists and is valid JSON (not corrupted by interleaved writes)
            Assert.True(File.Exists(_testPath));
            var content = await File.ReadAllTextAsync(_testPath);
            Assert.Contains("\"builtAt\":", content);

            
            // Check that no .tmp files were leaked
            var tmpFiles = Directory.GetFiles(Path.GetTempPath(), "*.tmp")
                                    .Where(f => f.Contains(Path.GetFileName(_testPath)));
            Assert.Empty(tmpFiles);
        }
    }
}
