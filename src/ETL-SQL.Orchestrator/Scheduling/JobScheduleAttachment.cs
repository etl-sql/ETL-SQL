using System;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine.Scheduling;

namespace ETL_SQL.Orchestrator.Scheduling
{
    /// <summary>
    /// Attaching a schedule to a job needs both halves of the model: the catalog holds the link, and
    /// the cron expression decides when it first comes due. Composing them here keeps the invariant
    /// in one place — <b>a link is always armed when it is created</b> — instead of leaving each
    /// caller to remember it.
    /// </summary>
    /// <remarks>
    /// The invariant matters because a null <c>NextRun</c> is not "run now" in this model: a cron
    /// expression can legitimately have no further occurrence, so the scheduler treats a missing
    /// value as dormant. A link created without one would therefore never fire, silently.
    /// </remarks>
    public static class JobScheduleAttachment
    {
        /// <summary>
        /// Attaches <paramref name="scheduleName"/> to <paramref name="jobName"/>, arming the link at
        /// the schedule's next occurrence.
        /// </summary>
        /// <remarks>
        /// The link is armed at the next occurrence rather than immediately: an explicit cron time
        /// means what it says, so attaching a <c>0 2 * * *</c> schedule at 15:00 waits for 02:00
        /// instead of firing on the spot. Trigger the job by hand if a run is wanted now.
        /// </remarks>
        /// <returns><c>true</c> when a new link was created; <c>false</c> when it already existed.</returns>
        /// <exception cref="InvalidOperationException">No such schedule.</exception>
        public static async Task<bool> AttachAsync(
            IJobCatalogStore catalog, string jobName, string scheduleName, DateTimeOffset? asOf = null)
        {
            var schedule = await catalog.GetScheduleAsync(scheduleName)
                ?? throw new InvalidOperationException(
                    $"Schedule '{scheduleName}' does not exist. Create it before attaching it to a job.");

            var nextRun = CronSchedule.GetNextOccurrence(
                schedule.Cron, schedule.TimeZone, asOf ?? DateTimeOffset.UtcNow);

            return await catalog.AddJobScheduleAsync(jobName, schedule.Name, nextRun);
        }
    }
}
