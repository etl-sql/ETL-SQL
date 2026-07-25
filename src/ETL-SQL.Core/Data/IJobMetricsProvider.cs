using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Data;

/// <summary>
/// One completed run's recorded metrics, as read back for <c>HISTORICAL</c> baselines.
/// </summary>
public sealed record JobRunMetrics(
    long RowsProcessed,
    long RowsQuarantined,
    long RowsWarned);

/// <summary>
/// Narrow Engine→Orchestrator seam giving <c>ASSERT JOB … WITHIN … OF HISTORICAL</c> access to
/// previous runs' recorded metrics. Statement handlers live in the engine and only see
/// <see cref="IExecutionContext"/>; the job-history store lives in the orchestrator. This
/// interface is the whole contract between them.
///
/// It is deliberately absent (null on the context) in pure-engine and CLI contexts: there,
/// <c>HISTORICAL</c> predicates fail with a clear message, while every collector-backed predicate
/// (<c>NULL_PERCENT</c>, <c>QUARANTINE_PERCENT</c>, plain <c>ROW_COUNT</c> compares) still works.
/// </summary>
public interface IJobMetricsProvider
{
    /// <summary>
    /// Returns the most recent successfully completed runs of <paramref name="jobName"/>, newest
    /// first, capped at <paramref name="limit"/>. Runs that failed or are still in flight are
    /// excluded — a failed run's metrics are not a baseline.
    /// </summary>
    Task<IReadOnlyList<JobRunMetrics>> GetRecentRunMetricsAsync(
        string jobName, int limit, CancellationToken cancellationToken = default);
}
