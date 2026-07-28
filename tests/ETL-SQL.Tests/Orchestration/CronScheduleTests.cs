using System;
using ETL_SQL.Engine.Scheduling;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Cron plus a named timezone is the one schedule representation in ETL-SQL. The daylight-saving
    /// cases are pinned against fixed dates rather than left to the library's discretion, because
    /// "the job did not run that night" is not something to discover in production.
    /// </summary>
    public class CronScheduleTests
    {
        // ── Parsing ───────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("0 2 * * *")]
        [InlineData("*/15 8-18 * * 1-5")]
        [InlineData("0 0 1 * *")]
        public void Parse_AcceptsFiveFieldExpressions(string cron) => CronSchedule.Parse(cron);

        /// <summary>
        /// Six-field cron parses in Cronos but is refused here. Sub-minute scheduling would be capped
        /// by Scheduler:SleepIntervalSeconds (default 30), so the expression would not fire at the
        /// rate it states — a discrepancy governed by an unrelated configuration knob. Refusing is
        /// honest; half-honouring is not.
        /// </summary>
        [Fact]
        public void Parse_RejectsSixFieldExpressions_AndSaysWhy()
        {
            var ex = Assert.Throws<ArgumentException>(() => CronSchedule.Parse("*/5 * * * * *"));

            Assert.Contains("six-field", ex.Message, StringComparison.Ordinal);
            Assert.Contains("minute-granularity", ex.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a cron")]
        [InlineData("0 2 * *")]
        public void Parse_RejectsMalformedExpressions(string cron) =>
            Assert.Throws<ArgumentException>(() => CronSchedule.Parse(cron));

        // ── Timezones ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolution goes through the same function as the AT TIME ZONE expression and RELDATE, so
        /// the scheduler accepts exactly the spellings the rest of the language documents. A
        /// scheduler with its own timezone vocabulary would be a defect.
        /// </summary>
        [Theory]
        [InlineData("UTC")]
        [InlineData("America/New_York")]
        [InlineData("Eastern Standard Time")]
        [InlineData("EST")]
        public void ResolveTimeZone_AcceptsEverySpellingTheLanguageAccepts(string id) =>
            Assert.NotNull(CronSchedule.ResolveTimeZone(id));

        [Fact]
        public void ResolveTimeZone_DefaultsToUtcWhenUnspecified()
        {
            Assert.Equal(CronSchedule.ResolveTimeZone("UTC"), CronSchedule.ResolveTimeZone(null));
            Assert.Equal(CronSchedule.ResolveTimeZone("UTC"), CronSchedule.ResolveTimeZone("  "));
        }

        [Fact]
        public void ResolveTimeZone_RejectsUnknownIdentifiers()
        {
            var ex = Assert.Throws<ArgumentException>(() => CronSchedule.ResolveTimeZone("Mars/Olympus_Mons"));
            Assert.Contains("not a known time zone", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Validation runs when a schedule is written, not at first fire, so a typo is a statement
        /// error rather than a schedule that silently never runs correctly.
        /// </summary>
        [Fact]
        public void Validate_CatchesBothHalves()
        {
            Assert.Throws<ArgumentException>(() => CronSchedule.Validate("nonsense", "UTC"));
            Assert.Throws<ArgumentException>(() => CronSchedule.Validate("0 2 * * *", "Mars/Olympus_Mons"));
            CronSchedule.Validate("0 2 * * *", "America/Chicago");
        }

        // ── Occurrences ───────────────────────────────────────────────────────────

        [Fact]
        public void GetNextOccurrence_IsRelativeToTheNamedZone_NotTheHost()
        {
            // 02:00 in Chicago on a summer date is CDT (UTC-5), so 07:00 UTC.
            var after = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

            var next = CronSchedule.GetNextOccurrence("0 2 * * *", "America/Chicago", after);

            Assert.Equal(new DateTime(2026, 7, 2, 7, 0, 0, DateTimeKind.Utc), next);
        }

        /// <summary>
        /// 2026-03-08 is US spring-forward: 02:00 local never happens in New York, because the clock
        /// jumps 02:00 → 03:00. The occurrence fires at the instant the gap ends rather than being
        /// dropped, so a nightly job still runs that night — which is what an operator wants, and is
        /// why this is Cronos's behaviour adopted rather than overridden.
        /// </summary>
        [Fact]
        public void GetNextOccurrence_FiresAtTheEndOfAGapWhenTheLocalTimeDoesNotExist()
        {
            var after = new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero);

            var next = CronSchedule.GetNextOccurrence("0 2 * * *", "America/New_York", after);

            // 03:00 EDT on the transition day — the moment the skipped interval ends — is 07:00 UTC.
            Assert.Equal(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc), next);
        }

        /// <summary>
        /// 2026-11-01 is US fall-back: 01:00 local occurs twice in New York. A daily 01:00 schedule
        /// must fire once, not twice.
        /// </summary>
        [Fact]
        public void GetNextOccurrence_FiresOnceWhenALocalTimeRepeats()
        {
            var after = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

            var first = CronSchedule.GetNextOccurrence("0 1 * * *", "America/New_York", after);
            Assert.NotNull(first);

            // The first 01:00 is EDT (UTC-4) → 05:00 UTC. The next occurrence is the following day,
            // not the repeated 01:00 EST at 06:00 UTC.
            Assert.Equal(new DateTime(2026, 11, 1, 5, 0, 0, DateTimeKind.Utc), first);

            var second = CronSchedule.GetNextOccurrence("0 1 * * *", "America/New_York", new DateTimeOffset(first!.Value, TimeSpan.Zero));
            Assert.Equal(new DateTime(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc), second);
        }

        /// <summary>
        /// A date-bounded expression can have no further occurrence. The caller must treat null as
        /// "never runs again" rather than "run now" — a null NextRun means due-immediately elsewhere
        /// in the scheduler, so conflating the two would fire a dead schedule continuously.
        /// </summary>
        [Fact]
        public void GetNextOccurrence_ReturnsNullWhenTheExpressionCanNeverFireAgain()
        {
            // 30 February never occurs.
            var next = CronSchedule.GetNextOccurrence("0 0 30 2 *", "UTC", DateTimeOffset.UtcNow);

            Assert.Null(next);
        }

        [Fact]
        public void GetNextOccurrence_IsStrictlyAfterTheGivenInstant()
        {
            var exactly = new DateTimeOffset(2026, 7, 1, 2, 0, 0, TimeSpan.Zero);

            var next = CronSchedule.GetNextOccurrence("0 2 * * *", "UTC", exactly);

            Assert.Equal(new DateTime(2026, 7, 2, 2, 0, 0, DateTimeKind.Utc), next);
        }
    }
}
