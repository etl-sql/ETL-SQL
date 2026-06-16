using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Practical HA P1.8: monotonic fencing tokens. A lease acquisition advances the job's fence token;
    /// the durable completion write carries the token and is rejected once a newer owner has acquired the
    /// lease — so a node that resumes after a partition cannot clobber the newer owner's scheduling state.
    /// Cross-provider behavior is also exercised on PostgreSQL in <see cref="OrchestratorPostgresStoreTests"/>.
    /// </summary>
    public sealed class FencingTokenTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fence_{Guid.NewGuid():N}.db");
        private readonly SQLiteJobHistoryStore _store;

        public FencingTokenTests() => _store = new SQLiteJobHistoryStore(_dbPath);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
        }

        private static JobDefinition Job(string name) =>
            new(name, "SELECT 1;", 1, "DAY", "06:00", null, null, true);

        [Fact]
        public async Task Acquire_AdvancesTokenMonotonically_AcrossOwnershipChanges()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(Job("j"));

            var t1 = await _store.AcquireJobLeaseAsync("j", "owner-A", TimeSpan.FromMilliseconds(40));
            Assert.NotNull(t1);

            // A renewal does NOT advance the token.
            Assert.True(await _store.TryRenewJobLeaseAsync("j", "owner-A", TimeSpan.FromMilliseconds(40)));
            Assert.True(await _store.ValidateFenceTokenAsync("j", t1!.Value));

            // After expiry a new owner acquires and the token strictly increases.
            await Task.Delay(120);
            var t2 = await _store.AcquireJobLeaseAsync("j", "owner-B", TimeSpan.FromMinutes(5));
            Assert.NotNull(t2);
            Assert.True(t2!.Value > t1.Value);
        }

        [Fact]
        public async Task StaleToken_IsRejected_AfterNewerAcquisition()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(Job("j"));

            var stale = await _store.AcquireJobLeaseAsync("j", "owner-A", TimeSpan.FromMilliseconds(40));
            Assert.NotNull(stale);

            await Task.Delay(120);
            var fresh = await _store.AcquireJobLeaseAsync("j", "owner-B", TimeSpan.FromMinutes(5));
            Assert.NotNull(fresh);

            // The stale holder's token no longer validates; the fresh one does.
            Assert.False(await _store.ValidateFenceTokenAsync("j", stale!.Value));
            Assert.True(await _store.ValidateFenceTokenAsync("j", fresh!.Value));
        }

        [Fact]
        public async Task FencedCompletionWrite_RejectsStaleWriter_ButAllowsCurrentOwner()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(Job("j"));

            var staleToken = await _store.AcquireJobLeaseAsync("j", "owner-A", TimeSpan.FromMilliseconds(40));
            await Task.Delay(120);
            var freshToken = await _store.AcquireJobLeaseAsync("j", "owner-B", TimeSpan.FromMinutes(5));

            var nextRun = DateTime.UtcNow.AddHours(1);

            // The paused-then-resumed node (stale token) is fenced out: its write lands on zero rows.
            Assert.False(await _store.TryUpdateJobLastRunFencedAsync("j", DateTime.UtcNow, nextRun, staleToken!.Value));

            // The current owner's fenced write succeeds and is persisted.
            Assert.True(await _store.TryUpdateJobLastRunFencedAsync("j", DateTime.UtcNow, nextRun, freshToken!.Value));
            var job = await _store.GetJobAsync("j");
            Assert.NotNull(job!.NextRun);
        }
    }
}
