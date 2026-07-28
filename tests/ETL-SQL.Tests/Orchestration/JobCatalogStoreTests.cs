using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// The Orchestrator is the system of record for schedules, notifications, and their attachments to
    /// jobs. Two properties matter more than the CRUD and are pinned here: every mutation is
    /// idempotent, because an exported configuration script must converge when replayed; and deletes
    /// of shared objects restrict rather than cascade, because unscheduling three unrelated jobs is
    /// not something a `DROP SCHEDULE` should do quietly.
    /// </summary>
    public class JobCatalogStoreTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SQLiteJobHistoryStore _store;

        public JobCatalogStoreTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"etlsql-catalog-{Guid.NewGuid():N}.db");
            _store = new SQLiteJobHistoryStore(_dbPath);
        }

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch (IOException) { }
        }

        private static ScheduleDefinition Nightly(string name = "NightlyTrigger") =>
            new(name, "0 2 * * *", "America/New_York", DisplayName: "Overnight (2am ET)");

        private static NotificationDefinition OpsAlert(string name = "OpsAlert") =>
            new(name, "local_mail", "ops@example.com");

        private async Task<JobDefinition> SaveJobAsync(string name = "FinanceNightly")
        {
            var job = new JobDefinition(name, "reports/finance.rptsql", 0, "HOUR", null, null, null,
                JobType: JobTargetKind.Report, TargetPath: "folders/Finance", DisplayName: "Finance nightly");
            await _store.SaveJobAsync(job);
            return job;
        }

        // ── Schedules ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task SaveSchedule_RoundTrips()
        {
            await _store.SaveScheduleAsync(Nightly());

            var loaded = await _store.GetScheduleAsync("NightlyTrigger");

            Assert.NotNull(loaded);
            Assert.Equal("0 2 * * *", loaded!.Cron);
            Assert.Equal("America/New_York", loaded.TimeZone);
            Assert.Equal("Overnight (2am ET)", loaded.DisplayName);
            Assert.True(loaded.IsEnabled);
        }

        /// <summary>
        /// The name is the identity and identifiers elsewhere in ETL-SQL are case-insensitive, so the
        /// catalog must not let `financenightly` become a second object beside `FinanceNightly`.
        /// </summary>
        [Fact]
        public async Task Names_AreCaseInsensitive()
        {
            await _store.SaveScheduleAsync(Nightly("NightlyTrigger"));

            Assert.NotNull(await _store.GetScheduleAsync("nightlytrigger"));
            Assert.NotNull(await _store.GetScheduleAsync("NIGHTLYTRIGGER"));

            // A differently-cased save updates the same row rather than creating a second.
            await _store.SaveScheduleAsync(Nightly("nightlytrigger") with { Cron = "0 3 * * *" });
            var all = await _store.GetSchedulesAsync();
            Assert.Single(all);
            Assert.Equal("0 3 * * *", all[0].Cron);
        }

        /// <summary>Replay of an exported script must converge, not fail on the second run.</summary>
        [Fact]
        public async Task SaveSchedule_IsIdempotent_AndBumpsVersion()
        {
            await _store.SaveScheduleAsync(Nightly());
            await _store.SaveScheduleAsync(Nightly() with { Cron = "0 4 * * *" });

            var loaded = await _store.GetScheduleAsync("NightlyTrigger");
            Assert.Equal("0 4 * * *", loaded!.Cron);
            Assert.Equal(2, loaded.Version);
        }

        /// <summary>Attribution records who created the object; a later edit must not rewrite it.</summary>
        [Fact]
        public async Task SaveSchedule_PreservesCreatedBy_AndUpdatesModifiedBy()
        {
            await _store.SaveScheduleAsync(Nightly() with { CreatedBy = "alice", ModifiedBy = "alice" });
            await _store.SaveScheduleAsync(Nightly() with { CreatedBy = "bob", ModifiedBy = "bob" });

            var loaded = await _store.GetScheduleAsync("NightlyTrigger");
            Assert.Equal("alice", loaded!.CreatedBy);
            Assert.Equal("bob", loaded.ModifiedBy);
        }

        [Fact]
        public async Task SetScheduleEnabled_TogglesAndReportsWhetherItMatched()
        {
            await _store.SaveScheduleAsync(Nightly());

            Assert.True(await _store.SetScheduleEnabledAsync("nightlytrigger", false));
            Assert.False((await _store.GetScheduleAsync("NightlyTrigger"))!.IsEnabled);
            Assert.False(await _store.SetScheduleEnabledAsync("no_such_schedule", false));
        }

        // ── Restrict vs cascade ───────────────────────────────────────────────────

        /// <summary>
        /// A schedule is shared. Cascading its delete would silently unschedule every job attached to
        /// it, so the delete fails and names the jobs instead.
        /// </summary>
        [Fact]
        public async Task DeleteSchedule_WhileLinked_IsRestricted_AndNamesTheBlockingJobs()
        {
            await SaveJobAsync("FinanceNightly");
            await SaveJobAsync("SalesNightly");
            await _store.SaveScheduleAsync(Nightly());
            await _store.AddJobScheduleAsync("FinanceNightly", "NightlyTrigger", DateTime.UtcNow);
            await _store.AddJobScheduleAsync("SalesNightly", "NightlyTrigger", DateTime.UtcNow);

            var blockers = await _store.DeleteScheduleAsync("NightlyTrigger");

            Assert.Equal(2, blockers.Count);
            Assert.Contains("FinanceNightly", blockers);
            Assert.Contains("SalesNightly", blockers);
            Assert.NotNull(await _store.GetScheduleAsync("NightlyTrigger"));
        }

        [Fact]
        public async Task DeleteSchedule_WhenUnlinked_Succeeds()
        {
            await _store.SaveScheduleAsync(Nightly());

            Assert.Empty(await _store.DeleteScheduleAsync("NightlyTrigger"));
            Assert.Null(await _store.GetScheduleAsync("NightlyTrigger"));
        }

        [Fact]
        public async Task DeleteNotification_WhileLinked_IsRestricted()
        {
            await SaveJobAsync();
            await _store.SaveNotificationAsync(OpsAlert());
            await _store.AddJobNotificationAsync("FinanceNightly", "OpsAlert", NotificationTrigger.Failure);

            var blockers = await _store.DeleteNotificationAsync("OpsAlert");

            Assert.Equal("FinanceNightly", Assert.Single(blockers));
            Assert.NotNull(await _store.GetNotificationAsync("OpsAlert"));
        }

        /// <summary>
        /// Deleting the job cascades its links — they have no meaning without it — while the shared
        /// schedule and notification survive for the jobs that still use them.
        /// </summary>
        [Fact]
        public async Task DeleteJob_CascadesItsLinks_ButKeepsTheSharedObjects()
        {
            await SaveJobAsync();
            await _store.SaveScheduleAsync(Nightly());
            await _store.SaveNotificationAsync(OpsAlert());
            await _store.AddJobScheduleAsync("FinanceNightly", "NightlyTrigger", DateTime.UtcNow);
            await _store.AddJobNotificationAsync("FinanceNightly", "OpsAlert", NotificationTrigger.Failure);

            await _store.DeleteJobAsync("FinanceNightly");

            Assert.Empty(await _store.GetJobSchedulesAsync("FinanceNightly"));
            Assert.Empty(await _store.GetJobNotificationsAsync("FinanceNightly"));
            Assert.NotNull(await _store.GetScheduleAsync("NightlyTrigger"));
            Assert.NotNull(await _store.GetNotificationAsync("OpsAlert"));

            // The shared objects are now unlinked, so they can be deleted.
            Assert.Empty(await _store.DeleteScheduleAsync("NightlyTrigger"));
        }

        // ── Attachments ───────────────────────────────────────────────────────────

        /// <summary>
        /// The defect this whole model exists to fix: a second schedule on one job must *add* a
        /// trigger, never replace the first.
        /// </summary>
        [Fact]
        public async Task TwoSchedulesOnOneJob_BothSurvive()
        {
            await SaveJobAsync();
            await _store.SaveScheduleAsync(Nightly());
            await _store.SaveScheduleAsync(new ScheduleDefinition("BusinessHours", "*/15 8-18 * * 1-5", "UTC"));

            await _store.AddJobScheduleAsync("FinanceNightly", "NightlyTrigger", DateTime.UtcNow);
            await _store.AddJobScheduleAsync("FinanceNightly", "BusinessHours", DateTime.UtcNow);

            var links = await _store.GetJobSchedulesAsync("FinanceNightly");
            Assert.Equal(2, links.Count);
            Assert.Contains(links, l => l.ScheduleName == "NightlyTrigger");
            Assert.Contains(links, l => l.ScheduleName == "BusinessHours");
        }

        /// <summary>
        /// A replayed script re-issues every ADD. That must be a no-op, and critically must not reset
        /// the run state of a link that is already armed and firing.
        /// </summary>
        [Fact]
        public async Task AddJobSchedule_IsIdempotent_AndPreservesRunState()
        {
            await SaveJobAsync();
            await _store.SaveScheduleAsync(Nightly());
            var armed = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);

            Assert.True(await _store.AddJobScheduleAsync("FinanceNightly", "NightlyTrigger", armed));

            var fired = new DateTime(2026, 8, 1, 6, 0, 5, DateTimeKind.Utc);
            var next = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc);
            await _store.UpdateJobScheduleRunAsync("FinanceNightly", "NightlyTrigger", fired, next);

            // Replay: reports "already there", and leaves the armed state alone.
            Assert.False(await _store.AddJobScheduleAsync("FinanceNightly", "NightlyTrigger", DateTime.UtcNow));

            var link = Assert.Single(await _store.GetJobSchedulesAsync("FinanceNightly"));
            Assert.Equal(fired, link.LastRun);
            Assert.Equal(next, link.NextRun);
        }

        [Fact]
        public async Task RemoveJobSchedule_IsIdempotent()
        {
            await SaveJobAsync();
            await _store.SaveScheduleAsync(Nightly());
            await _store.AddJobScheduleAsync("FinanceNightly", "NightlyTrigger", DateTime.UtcNow);

            Assert.True(await _store.RemoveJobScheduleAsync("financenightly", "nightlytrigger"));
            // Removing what is not there is a no-op, not an error — replay must converge.
            Assert.False(await _store.RemoveJobScheduleAsync("FinanceNightly", "NightlyTrigger"));
        }

        [Fact]
        public async Task AddJobNotification_SupportsSeveralOutcomesOnOneChannel()
        {
            await SaveJobAsync();
            await _store.SaveNotificationAsync(OpsAlert());

            Assert.True(await _store.AddJobNotificationAsync("FinanceNightly", "OpsAlert", NotificationTrigger.Success));
            Assert.True(await _store.AddJobNotificationAsync("FinanceNightly", "OpsAlert", NotificationTrigger.Failure));
            Assert.False(await _store.AddJobNotificationAsync("FinanceNightly", "OpsAlert", NotificationTrigger.Success));

            var links = await _store.GetJobNotificationsAsync("FinanceNightly");
            Assert.Equal(2, links.Count);
        }

        /// <summary>
        /// COMPLETION is the union of SUCCESS and FAILURE, so the pair would deliver twice for one
        /// run. Rejecting it at link time is the only place the mistake is visible — at dispatch it
        /// just looks like duplicate mail.
        /// </summary>
        [Theory]
        [InlineData(NotificationTrigger.Success, NotificationTrigger.Completion)]
        [InlineData(NotificationTrigger.Failure, NotificationTrigger.Completion)]
        [InlineData(NotificationTrigger.Completion, NotificationTrigger.Success)]
        [InlineData(NotificationTrigger.Completion, NotificationTrigger.Failure)]
        public async Task OverlappingTriggers_AreRejected(NotificationTrigger first, NotificationTrigger second)
        {
            await SaveJobAsync();
            await _store.SaveNotificationAsync(OpsAlert());
            await _store.AddJobNotificationAsync("FinanceNightly", "OpsAlert", first);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _store.AddJobNotificationAsync("FinanceNightly", "OpsAlert", second));

            Assert.Contains("COMPLETION covers both", ex.Message, StringComparison.Ordinal);
            Assert.Single(await _store.GetJobNotificationsAsync("FinanceNightly"));
        }

        [Fact]
        public async Task RemoveJobNotification_IsIdempotent()
        {
            await SaveJobAsync();
            await _store.SaveNotificationAsync(OpsAlert());
            await _store.AddJobNotificationAsync("FinanceNightly", "OpsAlert", NotificationTrigger.Failure);

            Assert.True(await _store.RemoveJobNotificationAsync("FinanceNightly", "OpsAlert", NotificationTrigger.Failure));
            Assert.False(await _store.RemoveJobNotificationAsync("FinanceNightly", "OpsAlert", NotificationTrigger.Failure));
            // A different outcome was never attached, so removing it is also a no-op.
            Assert.False(await _store.RemoveJobNotificationAsync("FinanceNightly", "OpsAlert", NotificationTrigger.Success));
        }

        // ── Job columns ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Job_RoundTripsTargetAndPresentationColumns()
        {
            await _store.SaveJobAsync(new JobDefinition(
                "SalesRefresh", "reports/sales.rptsql", 0, "HOUR", null, null, null,
                JobType: JobTargetKind.Report,
                TargetPath: "folders/Sales",
                DisplayName: "Sales — half-hourly",
                Description: "Rebuilds the sales dashboard cache",
                CreatedBy: "alice"));

            var loaded = await _store.GetJobAsync("salesrefresh");

            Assert.NotNull(loaded);
            Assert.Equal(JobTargetKind.Report, loaded!.JobType);
            Assert.Equal("folders/Sales", loaded.TargetPath);
            Assert.Equal("Sales — half-hourly", loaded.DisplayName);
            Assert.Equal("alice", loaded.CreatedBy);
        }

        /// <summary>A job saved without a target kind is a script job — the pre-existing shape.</summary>
        [Fact]
        public async Task Job_DefaultsToScriptKind()
        {
            await _store.SaveJobAsync(new JobDefinition("Legacy", "pipelines/sync.etlsql", 1, "HOUR", null, null, null));

            var loaded = await _store.GetJobAsync("Legacy");

            Assert.Equal(JobTargetKind.Script, loaded!.JobType);
            Assert.Null(loaded.TargetPath);
        }
    }
}
