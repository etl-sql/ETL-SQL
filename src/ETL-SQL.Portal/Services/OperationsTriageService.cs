using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Models;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Builds the cross-job triage board: what failed, what is still running, and what should have run
/// and did not.
///
/// Reads the shared job-history store directly rather than proxying the Orchestrator's HTTP API,
/// which is how every other Portal job-history read already works (failure digest, capacity report,
/// operations posture). Besides avoiding a hop, it means the board still answers when the
/// Orchestrator service is down — which is exactly when an operator is triaging.
/// </summary>
public sealed class OperationsTriageService(IJobHistoryStore jobHistory)
{
    /// <summary>Row cap for the history read; also the value that decides <c>Truncated</c>.</summary>
    private const int HistoryReadLimit = 1000;

    /// <summary>
    /// How late an occurrence must be before it is reported missed. The scheduler advances
    /// <c>NextRun</c> as it claims work, so a small amount of lateness is normal under load and
    /// reporting it would train operators to ignore the list.
    /// </summary>
    public const int DefaultGraceMinutes = 5;

    public async Task<TriageBoardDto> BuildAsync(
        int lookbackHours = 24,
        int graceMinutes = DefaultGraceMinutes,
        CancellationToken cancellationToken = default)
    {
        lookbackHours = Math.Clamp(lookbackHours, 1, 720);
        graceMinutes = Math.Clamp(graceMinutes, 0, 1440);

        // JobHistory timestamps are written with DateTime.Now and parsed back as absolute instants
        // in local kind, so the window is computed in local time to match. See
        // SQLiteJobHistoryStore.GetCompletedHistoryAsync for the storage convention.
        var now = DateTime.Now;
        var since = now.AddHours(-lookbackHours);

        var history = (await jobHistory.GetHistoryAsync(null, HistoryReadLimit)).ToList();
        cancellationToken.ThrowIfCancellationRequested();

        var truncated = history.Count >= HistoryReadLimit;
        var inWindow = history.Where(h => h.StartTime >= since).ToList();

        var running = inWindow
            .Where(IsRunning)
            .OrderByDescending(h => h.StartTime)
            .Select(ToRun)
            .ToList();

        var failures = inWindow.Where(IsFailure).ToList();

        var incidents = failures
            .GroupBy(h => RunFailureSignature.Normalize(h.ErrorMessage), StringComparer.Ordinal)
            .Select(group =>
            {
                var runs = group.OrderByDescending(h => h.StartTime).Select(ToRun).ToList();
                return new TriageIncidentDto(
                    group.Key,
                    RunFailureSignature.SampleFor(group.Select(h => h.ErrorMessage)),
                    runs.Count,
                    group.Select(h => h.JobName)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                         .ToList(),
                    group.Min(h => h.StartTime),
                    group.Max(h => h.StartTime),
                    runs);
            })
            // Most recent incident first: triage starts from what is breaking now, not from
            // whatever happens to have the largest count.
            .OrderByDescending(i => i.LastSeen)
            .ToList();

        var missed = await BuildMissedAsync(now, graceMinutes, cancellationToken);

        return new TriageBoardDto(
            now,
            lookbackHours,
            failures.Count,
            incidents.Count,
            running.Count,
            missed.Count,
            incidents,
            running,
            missed,
            truncated);
    }

    private async Task<List<TriageMissedJobDto>> BuildMissedAsync(
        DateTime now, int graceMinutes, CancellationToken cancellationToken)
    {
        var cutoff = now.AddMinutes(-graceMinutes);
        var jobs = await jobHistory.GetAllJobsAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return jobs
            // A null NextRun means "due now" to the scheduler rather than "overdue" (it is a derived
            // display value that starts null), so an unclaimed null is not evidence of a miss.
            .Where(j => j.IsEnabled && j.NextRun.HasValue && j.NextRun.Value < cutoff)
            .Select(j => new TriageMissedJobDto(
                j.Name,
                j.DisplayName,
                j.NextRun!.Value,
                Math.Round((now - j.NextRun!.Value).TotalMinutes, 1),
                j.LastRun))
            .OrderByDescending(m => m.OverdueMinutes)
            .ToList();
    }

    /// <summary>
    /// Terminal and not successful. INTERRUPTED counts — a run the engine could not finish is a
    /// failure an operator must see. Matches <see cref="FailureDigestAdminService"/> so the digest
    /// email and the board never disagree about what broke.
    /// </summary>
    private static bool IsFailure(JobHistoryEntry h) =>
        !IsRunning(h)
        && !string.Equals(h.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase);

    private static bool IsRunning(JobHistoryEntry h) =>
        string.Equals(h.Status, "RUNNING", StringComparison.OrdinalIgnoreCase) && h.EndTime is null;

    private static TriageRunDto ToRun(JobHistoryEntry h) => new(
        h.Id, h.JobName, h.StartTime, h.EndTime, h.Status, h.ErrorMessage,
        h.RowsProcessed, h.RowsQuarantined, h.RowsWarned, h.DataQualityFailures,
        h.ScriptHashAtRunTime, h.HashMatched);
}
