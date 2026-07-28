using System;
using Cronos;
using ETL_SQL.Engine;

namespace ETL_SQL.Orchestrator.Scheduling
{
    /// <summary>
    /// Cron plus a named timezone — the one schedule representation in ETL-SQL. Parsing, timezone
    /// resolution, and next-occurrence calculation live here so the scheduler, the statement handlers,
    /// and the catalog all agree on what a schedule means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Granularity is minutes.</b> Standard five-field cron is all that is accepted; the retired
    /// <c>EVERY n SECONDS</c> form has no replacement, by decision rather than omission. Cronos can
    /// parse six-field cron, but sub-minute schedules would also be capped by
    /// <c>Scheduler:SleepIntervalSeconds</c> (default 30) — a <c>*/5 * * * * *</c> schedule would fire
    /// every 30 seconds, not every 5, with the discrepancy depending on an unrelated configuration
    /// knob. A six-field expression is rejected with that explanation rather than half-honoured.
    /// </para>
    /// <para>
    /// <b>Timezones resolve through <see cref="RelDateResolver.FindTimeZone"/></b>, the same function
    /// behind the <c>AT TIME ZONE</c> expression and <c>RELDATE</c>. IANA IDs, Windows IDs, and the
    /// documented abbreviations all work, and a scheduler that accepted a different set of spellings
    /// than the rest of the language would be a defect rather than a feature.
    /// </para>
    /// </remarks>
    public static class CronSchedule
    {
        /// <summary>Fallback when neither the statement nor configuration names a zone.</summary>
        public const string DefaultTimeZone = "UTC";

        /// <summary>
        /// Validates a cron expression and timezone, throwing with an actionable message. Called when
        /// a schedule is created or altered — <b>not</b> at first fire, so a typo is a statement error
        /// rather than a schedule that silently never runs.
        /// </summary>
        public static void Validate(string cron, string timeZone)
        {
            _ = Parse(cron);
            _ = ResolveTimeZone(timeZone);
        }

        /// <summary>Parses a five-field cron expression.</summary>
        /// <exception cref="ArgumentException">The expression is empty, six-field, or malformed.</exception>
        public static CronExpression Parse(string cron)
        {
            if (string.IsNullOrWhiteSpace(cron))
                throw new ArgumentException("A schedule requires a cron expression.", nameof(cron));

            var fields = cron.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 6)
                throw new ArgumentException(
                    $"'{cron}' is a six-field cron expression with a seconds field. ETL-SQL schedules " +
                    "are minute-granularity: use five fields. Sub-minute scheduling would also be " +
                    "bounded by Scheduler:SleepIntervalSeconds, so it would not fire at the rate the " +
                    "expression states.",
                    nameof(cron));

            if (fields.Length != 5)
                throw new ArgumentException(
                    $"'{cron}' is not a five-field cron expression (minute hour day month weekday).",
                    nameof(cron));

            try
            {
                return CronExpression.Parse(cron);
            }
            catch (CronFormatException ex)
            {
                throw new ArgumentException($"'{cron}' is not a valid cron expression: {ex.Message}", nameof(cron), ex);
            }
        }

        /// <summary>
        /// Resolves a timezone identifier, accepting every spelling the rest of the language accepts.
        /// </summary>
        /// <exception cref="ArgumentException">The identifier is not a known timezone.</exception>
        public static TimeZoneInfo ResolveTimeZone(string? timeZone)
        {
            var id = string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone.Trim();
            try
            {
                return RelDateResolver.FindTimeZone(id);
            }
            catch (TimeZoneNotFoundException ex)
            {
                throw new ArgumentException($"'{id}' is not a known time zone.", nameof(timeZone), ex);
            }
            catch (InvalidTimeZoneException ex)
            {
                throw new ArgumentException($"Time zone '{id}' is not usable on this host.", nameof(timeZone), ex);
            }
        }

        /// <summary>
        /// The next occurrence strictly after <paramref name="after"/>, in UTC.
        /// </summary>
        /// <remarks>
        /// Daylight saving follows Cronos, adopted deliberately after checking what it actually does:
        /// a local time that does not exist on a spring-forward day fires at the instant the gap
        /// <b>ends</b> (02:00 → 03:00), so a nightly job still runs that night rather than silently
        /// missing one night a year; a local time that occurs twice on a fall-back day fires
        /// <b>once</b>, on the first occurrence. Both are pinned by tests against fixed dates.
        /// </remarks>
        /// <returns>
        /// <c>null</c> when the expression has no further occurrence — possible for a date-bounded
        /// expression such as <c>0 0 30 2 *</c>. A caller must treat that as "never runs again"
        /// rather than "run now".
        /// </returns>
        public static DateTime? GetNextOccurrence(string cron, string? timeZone, DateTimeOffset after)
        {
            var expression = Parse(cron);
            var tz = ResolveTimeZone(timeZone);
            return expression.GetNextOccurrence(after, tz, inclusive: false)?.UtcDateTime;
        }

        /// <summary>The next occurrence strictly after now, in UTC.</summary>
        public static DateTime? GetNextOccurrence(string cron, string? timeZone) =>
            GetNextOccurrence(cron, timeZone, DateTimeOffset.UtcNow);
    }
}
