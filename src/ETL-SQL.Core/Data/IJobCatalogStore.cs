using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Data;

/// <summary>What a job runs: a portal report refresh, or a plain script file.</summary>
public enum JobTargetKind
{
    Script,
    Report
}

/// <summary>When an attached notification fires.</summary>
/// <remarks>
/// <c>Completion</c> is the union of the other two. Attaching it alongside <c>Success</c> or
/// <c>Failure</c> for the same job and notification is therefore always a mistake and is rejected at
/// link time rather than silently delivering twice.
/// </remarks>
public enum NotificationTrigger
{
    Success,
    Failure,
    Completion
}

/// <summary>
/// A named trigger. Independent of any job — one schedule may drive many jobs, which is the whole
/// point of separating it from the job in the first place.
/// </summary>
/// <param name="Name">Identity. Unique per orchestrator, case-insensitive, never renamed.</param>
/// <param name="Cron">Standard five-field cron. Minute granularity; see <c>CronSchedule</c>.</param>
/// <param name="TimeZone">
/// Resolved at creation and stored, so editing the configured default cannot silently move a
/// schedule that already exists.
/// </param>
public sealed record ScheduleDefinition(
    string Name,
    string Cron,
    string TimeZone,
    bool IsEnabled = true,
    string? DisplayName = null,
    string? Description = null,
    string? Options = null,
    string? CreatedBy = null,
    string? ModifiedBy = null,
    long Version = 1);

/// <summary>
/// A named delivery destination. Holds a connection <em>alias</em> and never a credential: the alias
/// resolves through normal connection and <c>SECRET:</c> resolution on the host that dispatches.
/// </summary>
public sealed record NotificationDefinition(
    string Name,
    string ConnectionName,
    string? Recipient = null,
    bool IsEnabled = true,
    string? DisplayName = null,
    string? Description = null,
    string? Options = null,
    string? CreatedBy = null,
    string? ModifiedBy = null,
    long Version = 1);

/// <summary>
/// One job↔schedule attachment. Run state lives here rather than on the job, so two schedules on one
/// job stay distinguishable in operations — which is the defect this whole model exists to fix.
/// </summary>
public sealed record JobScheduleLink(
    string JobName,
    string ScheduleName,
    DateTime? LastRun = null,
    DateTime? NextRun = null);

/// <summary>One job↔notification attachment, qualified by the outcome that fires it.</summary>
public sealed record JobNotificationLink(
    string JobName,
    string NotificationName,
    NotificationTrigger Trigger);

/// <summary>
/// The Orchestrator's catalog of schedules, notifications, and their attachments to jobs. The
/// Orchestrator is the system of record for all three; the Portal is a client that keeps only the
/// report→job association it needs.
/// </summary>
/// <remarks>
/// Every mutation here is <b>idempotent</b>, because ETL-SQL's configuration is code: an exported
/// script must converge when replayed rather than failing on the second run. Adding a link that
/// exists and removing one that does not are both no-ops that report what happened, never errors.
/// </remarks>
public interface IJobCatalogStore
{
    // ── Schedules ─────────────────────────────────────────────────────────────

    Task SaveScheduleAsync(ScheduleDefinition schedule);
    Task<ScheduleDefinition?> GetScheduleAsync(string name);
    Task<IReadOnlyList<ScheduleDefinition>> GetSchedulesAsync(int limit = 1000, int offset = 0);

    /// <summary>
    /// Deletes a schedule. <b>Restricts</b> when jobs are still attached: cascading would silently
    /// unschedule unrelated jobs, which is exactly the surprise a shared object must not spring.
    /// </summary>
    /// <returns>
    /// The names of the jobs blocking the delete, empty when the delete succeeded or the schedule
    /// did not exist.
    /// </returns>
    Task<IReadOnlyList<string>> DeleteScheduleAsync(string name);

    Task<bool> SetScheduleEnabledAsync(string name, bool isEnabled);

    // ── Notifications ─────────────────────────────────────────────────────────

    Task SaveNotificationAsync(NotificationDefinition notification);
    Task<NotificationDefinition?> GetNotificationAsync(string name);
    Task<IReadOnlyList<NotificationDefinition>> GetNotificationsAsync(int limit = 1000, int offset = 0);

    /// <summary>Deletes a notification. <b>Restricts</b> when jobs are still attached.</summary>
    /// <returns>The names of the jobs blocking the delete; empty on success.</returns>
    Task<IReadOnlyList<string>> DeleteNotificationAsync(string name);

    Task<bool> SetNotificationEnabledAsync(string name, bool isEnabled);

    // ── Attachments ───────────────────────────────────────────────────────────

    /// <summary>
    /// Attaches a schedule to a job, seeding the link's <c>NextRun</c>. Idempotent: re-attaching an
    /// existing link leaves its run state alone and returns <c>false</c>.
    /// </summary>
    /// <returns><c>true</c> when a new link was created.</returns>
    Task<bool> AddJobScheduleAsync(string jobName, string scheduleName, DateTime? nextRun);

    /// <summary>Detaches a schedule. Idempotent; <c>false</c> when there was nothing to remove.</summary>
    Task<bool> RemoveJobScheduleAsync(string jobName, string scheduleName);

    Task<IReadOnlyList<JobScheduleLink>> GetJobSchedulesAsync(string? jobName = null);

    /// <summary>Records a fired occurrence against one link, and arms the next.</summary>
    Task UpdateJobScheduleRunAsync(string jobName, string scheduleName, DateTime lastRun, DateTime? nextRun);

    /// <summary>
    /// Attaches a notification to a job for one outcome. Idempotent; <c>false</c> when the link
    /// already existed.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The requested trigger overlaps one already attached for the same job and notification —
    /// <c>COMPLETION</c> together with <c>SUCCESS</c> or <c>FAILURE</c> would deliver twice.
    /// </exception>
    Task<bool> AddJobNotificationAsync(string jobName, string notificationName, NotificationTrigger trigger);

    /// <summary>Detaches a notification. Idempotent; <c>false</c> when there was nothing to remove.</summary>
    Task<bool> RemoveJobNotificationAsync(string jobName, string notificationName, NotificationTrigger trigger);

    Task<IReadOnlyList<JobNotificationLink>> GetJobNotificationsAsync(string? jobName = null);
}
