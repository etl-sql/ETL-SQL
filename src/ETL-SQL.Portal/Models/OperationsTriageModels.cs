namespace ETL_SQL.Portal.Models;

/// <summary>One run shown in the triage board. A projection of the durable job-history row.</summary>
public sealed record TriageRunDto(
    long Id,
    string JobName,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    string? ErrorMessage,
    long RowsProcessed,
    long RowsQuarantined,
    long RowsWarned,
    string? DataQualityFailures,
    string? ScriptHashAtRunTime,
    bool? HashMatched);

/// <summary>
/// Failed runs that share a normalized error signature, presented as one incident. One bad source
/// database at 03:00 produces a failure per dependent job; without grouping an operator reads the
/// same outage N times and has to infer the common cause themselves.
/// </summary>
public sealed record TriageIncidentDto(
    string Signature,
    string SampleError,
    int FailureCount,
    IReadOnlyList<string> JobNames,
    DateTime FirstSeen,
    DateTime LastSeen,
    IReadOnlyList<TriageRunDto> Runs);

/// <summary>
/// An enabled job whose scheduled occurrence has passed without the scheduler claiming it. A missed
/// run writes no history row, so it is invisible to any failure-driven view — this is the silent
/// 03:00 miss that a "failed jobs" list alone will never surface.
/// </summary>
public sealed record TriageMissedJobDto(
    string JobName,
    string? DisplayName,
    DateTime DueAt,
    double OverdueMinutes,
    DateTime? LastRun);

/// <summary>The whole triage board for one lookback window.</summary>
/// <param name="Truncated">
/// True when the history read hit its row cap, so counts are a floor rather than a total. Surfaced
/// because a silently clipped board is worse than an obviously partial one.
/// </param>
public sealed record TriageBoardDto(
    DateTime GeneratedAt,
    int LookbackHours,
    int FailureCount,
    int IncidentCount,
    int RunningCount,
    int MissedCount,
    IReadOnlyList<TriageIncidentDto> Incidents,
    IReadOnlyList<TriageRunDto> Running,
    IReadOnlyList<TriageMissedJobDto> Missed,
    bool Truncated);

/// <summary>Bulk re-run request from the triage board.</summary>
public sealed record TriageRerunRequest(IReadOnlyList<string> JobNames);

/// <summary>Per-job outcome of a bulk re-run, so a partial failure is legible rather than fatal.</summary>
public sealed record TriageRerunResultDto(string JobName, bool Triggered, string? Error);

/// <summary>Result envelope for a bulk re-run.</summary>
public sealed record TriageRerunResponseDto(
    int Requested,
    int Triggered,
    IReadOnlyList<TriageRerunResultDto> Results);
