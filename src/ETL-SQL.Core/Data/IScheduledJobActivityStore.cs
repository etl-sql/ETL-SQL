using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Data;

[JsonConverter(typeof(JsonStringEnumConverter<ScheduledJobRunState>))]
public enum ScheduledJobRunState
{
    Idle,
    Queued,
    Running
}

/// <summary>Activity for one current job identity; outcomes are distinct-job membership, not run counts.</summary>
public sealed record ScheduledJobActivity(
    JobDefinition Job,
    ScheduledJobRunState RunState,
    bool CompletedToday,
    bool FailedToday);

public sealed record ScheduledJobActivityCounts(int Active, int Queued, int CompletedToday, int FailedToday);

public sealed record ScheduledJobActivityPage(
    DateTimeOffset AsOfUtc,
    DateTimeOffset DayStartUtc,
    ScheduledJobActivityCounts Counts,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<ScheduledJobActivity> Items);

/// <summary>Lease-fenced runtime state and one tenant's current jobs with UTC-day outcome membership.</summary>
public interface IScheduledJobActivityStore
{
    /// <summary>
    /// Reads a consistent job/history projection. Active states require an unexpired execution lease.
    /// SUCCESS and FAILURE/BLOCKED outcomes use [UTC midnight, asOfUtc], matched by immutable JobId.
    /// </summary>
    Task<IReadOnlyList<ScheduledJobActivity>> GetScheduledJobActivityAsync(
        string? tenantId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default);

    /// <summary>Changes state only for the same owner, fence token, and unexpired lease.</summary>
    Task<bool> TrySetScheduledJobRunStateAsync(
        JobId jobId, string owner, long fenceToken, ScheduledJobRunState state,
        CancellationToken cancellationToken = default);
}
