using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Engine.Scheduling;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Which jobs the scheduler picks up, once schedules are attachments rather than columns on the
    /// job. The two paths — legacy interval and cron links — must be disjoint, because a job that
    /// appeared on both would run twice per occurrence.
    /// </summary>
    public class ScheduleDrivenDispatchTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SQLiteJobHistoryStore _store;

        public ScheduleDrivenDispatchTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"etlsql-dispatch-{Guid.NewGuid():N}.db");
            _store = new SQLiteJobHistoryStore(_dbPath);
        }

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch (IOException) { }
        }

        private async Task SaveJobAsync(string name, bool enabled = true) =>
            await _store.SaveJobAsync(new JobDefinition(
                name, "reports/x.rptsql", 1, "HOUR", null, null, null, enabled,
                JobType: JobTargetKind.Report, TargetPath: "folders/X"));

        private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        // ── Due selection ─────────────────────────────────────────────────────────

        [Fact]
        public async Task DueBySchedule_PicksUpAJobWhoseLinkIsDue()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("T", "0 2 * * *", "UTC"));
            await _store.AddJobScheduleAsync("Nightly", "T", Now.AddMinutes(-1));

            var due = await _store.GetJobsDueByScheduleAsync(Now);

            Assert.Equal("Nightly", Assert.Single(due).Name);
        }

        [Fact]
        public async Task DueBySchedule_IgnoresALinkThatIsNotYetDue()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("T", "0 2 * * *", "UTC"));
            await _store.AddJobScheduleAsync("Nightly", "T", Now.AddHours(1));

            Assert.Empty(await _store.GetJobsDueByScheduleAsync(Now));
        }

        /// <summary>
        /// The coalescing rule. Two schedules that come due together are one occurrence of one job,
        /// not two concurrent runs of it — a job still has exactly one execution lease.
        /// </summary>
        [Fact]
        public async Task DueBySchedule_ReturnsAJobOnceWhenSeveralLinksAreDue()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("A", "0 2 * * *", "UTC"));
            await _store.SaveScheduleAsync(new ScheduleDefinition("B", "*/15 * * * *", "UTC"));
            await _store.AddJobScheduleAsync("Nightly", "A", Now.AddMinutes(-1));
            await _store.AddJobScheduleAsync("Nightly", "B", Now.AddMinutes(-1));

            var due = await _store.GetJobsDueByScheduleAsync(Now);

            Assert.Single(due);
        }

        /// <summary>
        /// A null NextRun means "no further occurrence", never "run now". Treating it as due would
        /// spin a dormant job on every scheduler tick.
        /// </summary>
        [Fact]
        public async Task DueBySchedule_TreatsAnUnarmedLinkAsDormant()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("T", "0 2 * * *", "UTC"));
            await _store.AddJobScheduleAsync("Nightly", "T", nextRun: null);

            Assert.Empty(await _store.GetJobsDueByScheduleAsync(Now));
        }

        [Fact]
        public async Task DueBySchedule_SkipsADisabledScheduleOrJob()
        {
            await SaveJobAsync("Nightly");
            await SaveJobAsync("Paused", enabled: false);
            await _store.SaveScheduleAsync(new ScheduleDefinition("T", "0 2 * * *", "UTC"));
            await _store.AddJobScheduleAsync("Nightly", "T", Now.AddMinutes(-1));
            await _store.AddJobScheduleAsync("Paused", "T", Now.AddMinutes(-1));

            Assert.Equal("Nightly", Assert.Single(await _store.GetJobsDueByScheduleAsync(Now)).Name);

            await _store.SetScheduleEnabledAsync("T", false);
            Assert.Empty(await _store.GetJobsDueByScheduleAsync(Now));
        }

        /// <summary>
        /// The legacy interval path must not also claim a cron-scheduled job. Its NextRun column
        /// starts null and null means "due now" there, so without the exclusion every link-scheduled
        /// job would fire on every tick — and would then be running on two schedules at once.
        /// </summary>
        [Fact]
        public async Task LegacyIntervalPath_ExcludesJobsThatHaveScheduleLinks()
        {
            await SaveJobAsync("Linked");
            await SaveJobAsync("Interval");
            await _store.SaveScheduleAsync(new ScheduleDefinition("T", "0 2 * * *", "UTC"));
            await _store.AddJobScheduleAsync("Linked", "T", Now.AddHours(1));

            var legacyDue = await _store.GetDueJobsAsync(DateTime.Now);

            Assert.Equal("Interval", Assert.Single(legacyDue).Name);
        }

        // ── Attachment arms the link ──────────────────────────────────────────────

        [Fact]
        public async Task Attach_ArmsTheLinkAtTheNextOccurrence()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("T", "0 2 * * *", "UTC"));

            // 15:00 UTC — the next 02:00 is the following day, not now.
            var asOf = new DateTimeOffset(2026, 8, 1, 15, 0, 0, TimeSpan.Zero);
            Assert.True(await JobScheduleAttachment.AttachAsync(_store, "Nightly", "T", asOf));

            var link = Assert.Single(await _store.GetJobSchedulesAsync("Nightly"));
            Assert.Equal(new DateTime(2026, 8, 2, 2, 0, 0, DateTimeKind.Utc), link.NextRun);
            Assert.Null(link.LastRun);

            // And it is therefore not due at the moment it was attached.
            Assert.Empty(await _store.GetJobsDueByScheduleAsync(asOf.UtcDateTime));
        }

        [Fact]
        public async Task Attach_RejectsAnUnknownSchedule()
        {
            await SaveJobAsync("Nightly");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => JobScheduleAttachment.AttachAsync(_store, "Nightly", "NoSuchSchedule"));

            Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Attach_ResolvesTheScheduleNameCaseInsensitively()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("NightlyTrigger", "0 2 * * *", "UTC"));

            await JobScheduleAttachment.AttachAsync(_store, "Nightly", "nightlytrigger");

            // The link stores the schedule's canonical name, not the caller's casing.
            var link = Assert.Single(await _store.GetJobSchedulesAsync("Nightly"));
            Assert.Equal("NightlyTrigger", link.ScheduleName);
        }

        // ── Advancing links after a run ───────────────────────────────────────────

        /// <summary>
        /// Built against the real store rather than a mock: what is being verified is the state the
        /// links are left in, which a mock would only restate.
        /// </summary>
        private SchedulerService Scheduler() => new(
            new Mock<IServiceProvider>().Object,
            _store,
            new Mock<ILogger<SchedulerService>>().Object,
            new JobThrottle(Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 4 }),
                new Mock<ILogger<JobThrottle>>().Object),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            new Mock<ISessionStateManager>().Object);

        /// <summary>
        /// One run satisfies every link that was due. Advancing only the earliest would leave the
        /// others due, and the job would re-fire on the very next tick — the coalescing rule undone
        /// one step later.
        /// </summary>
        [Fact]
        public async Task Advance_MarksEveryDueLinkAsFired_NotJustTheEarliest()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("A", "0 2 * * *", "UTC"));
            await _store.SaveScheduleAsync(new ScheduleDefinition("B", "0 3 * * *", "UTC"));
            await _store.AddJobScheduleAsync("Nightly", "A", Now.AddMinutes(-2));
            await _store.AddJobScheduleAsync("Nightly", "B", Now.AddMinutes(-1));
            var job = (await _store.GetJobAsync("Nightly"))!;

            var earliest = await Scheduler().AdvanceScheduleLinksAsync(job, Now);

            var links = (await _store.GetJobSchedulesAsync("Nightly")).ToDictionary(l => l.ScheduleName);
            Assert.Equal(Now, links["A"].LastRun);
            Assert.Equal(Now, links["B"].LastRun);
            Assert.Equal(new DateTime(2026, 8, 2, 2, 0, 0, DateTimeKind.Utc), links["A"].NextRun);
            Assert.Equal(new DateTime(2026, 8, 2, 3, 0, 0, DateTimeKind.Utc), links["B"].NextRun);

            // Neither is due any more, so the job does not immediately re-fire.
            Assert.Empty(await _store.GetJobsDueByScheduleAsync(Now));
            Assert.Equal(links["A"].NextRun, earliest);
        }

        /// <summary>
        /// A link that was not due belongs to a different occurrence and must keep its own arming —
        /// the run it did not cause must not be recorded against it.
        /// </summary>
        [Fact]
        public async Task Advance_LeavesALinkThatWasNotDueUntouched()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("Due", "0 2 * * *", "UTC"));
            await _store.SaveScheduleAsync(new ScheduleDefinition("Later", "0 2 * * *", "UTC"));
            var later = Now.AddHours(5);
            await _store.AddJobScheduleAsync("Nightly", "Due", Now.AddMinutes(-1));
            await _store.AddJobScheduleAsync("Nightly", "Later", later);
            var job = (await _store.GetJobAsync("Nightly"))!;

            await Scheduler().AdvanceScheduleLinksAsync(job, Now);

            var links = (await _store.GetJobSchedulesAsync("Nightly")).ToDictionary(l => l.ScheduleName);
            Assert.Equal(Now, links["Due"].LastRun);
            Assert.Null(links["Later"].LastRun);
            Assert.Equal(later, links["Later"].NextRun);
        }

        /// <summary>An unarmed link is armed rather than left dormant forever.</summary>
        [Fact]
        public async Task Advance_ArmsAnUnarmedLinkWithoutClaimingItRan()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("A", "0 2 * * *", "UTC"));
            await _store.SaveScheduleAsync(new ScheduleDefinition("Unarmed", "0 6 * * *", "UTC"));
            await _store.AddJobScheduleAsync("Nightly", "A", Now.AddMinutes(-1));
            await _store.AddJobScheduleAsync("Nightly", "Unarmed", nextRun: null);
            var job = (await _store.GetJobAsync("Nightly"))!;

            await Scheduler().AdvanceScheduleLinksAsync(job, Now);

            var unarmed = (await _store.GetJobSchedulesAsync("Nightly")).Single(l => l.ScheduleName == "Unarmed");
            Assert.Equal(new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc), unarmed.NextRun);
            Assert.Null(unarmed.LastRun);
        }

        /// <summary>
        /// A disabled schedule cannot make the job due, so it must not be what an operator reads as
        /// the next run — but its link is still advanced, so re-enabling takes effect at once.
        /// </summary>
        [Fact]
        public async Task Advance_ExcludesADisabledScheduleFromTheReportedNextRun()
        {
            await SaveJobAsync("Nightly");
            await _store.SaveScheduleAsync(new ScheduleDefinition("Early", "0 1 * * *", "UTC", IsEnabled: false));
            await _store.SaveScheduleAsync(new ScheduleDefinition("Late", "0 9 * * *", "UTC"));
            await _store.AddJobScheduleAsync("Nightly", "Early", Now.AddMinutes(-1));
            await _store.AddJobScheduleAsync("Nightly", "Late", Now.AddMinutes(-1));
            var job = (await _store.GetJobAsync("Nightly"))!;

            var earliest = await Scheduler().AdvanceScheduleLinksAsync(job, Now);

            // 01:00 is sooner, but that schedule is disabled — the reported next run is 09:00.
            Assert.Equal(new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc), earliest);
            var early = (await _store.GetJobSchedulesAsync("Nightly")).Single(l => l.ScheduleName == "Early");
            Assert.Equal(new DateTime(2026, 8, 2, 1, 0, 0, DateTimeKind.Utc), early.NextRun);
        }

        /// <summary>A job with no links leaves the legacy interval path to answer.</summary>
        [Fact]
        public async Task Advance_ReturnsNullForAJobWithNoLinks()
        {
            await SaveJobAsync("Interval");
            var job = (await _store.GetJobAsync("Interval"))!;

            Assert.Null(await Scheduler().AdvanceScheduleLinksAsync(job, Now));
        }

        /// <summary>
        /// A cron expression with no further occurrence leaves the link dormant rather than armed at
        /// some invented time — and dormant is not due, so the job stops instead of spinning.
        /// </summary>
        [Fact]
        public async Task Advance_LeavesALinkDormantWhenTheCronHasNoFurtherOccurrence()
        {
            await SaveJobAsync("Nightly");
            // 30 February never occurs.
            await _store.SaveScheduleAsync(new ScheduleDefinition("Never", "0 0 30 2 *", "UTC"));
            await _store.AddJobScheduleAsync("Nightly", "Never", Now.AddMinutes(-1));
            var job = (await _store.GetJobAsync("Nightly"))!;

            var earliest = await Scheduler().AdvanceScheduleLinksAsync(job, Now);

            Assert.Null(earliest);
            var link = Assert.Single(await _store.GetJobSchedulesAsync("Nightly"));
            Assert.Null(link.NextRun);
            Assert.Equal(Now, link.LastRun);
            Assert.Empty(await _store.GetJobsDueByScheduleAsync(Now));
        }
    }
}
