using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Core;

/// <summary>
/// Abstraction for executing ETL-SQL scripts. Implemented by the engine's
/// ExecutionSession wrapper. Injected into SchedulerService so scheduled job
/// execution is testable without taking a direct dependency on Evaluator.
/// </summary>
public interface IScriptExecutor
{
    /// <summary>
    /// Executes the given script text and returns the result. When <paramref name="executionIdentity"/>
    /// is supplied, it is the row-level-security identity the script runs under (used by per-recipient
    /// subscription delivery); null means non-interactive execution, which fails closed for
    /// identity-sensitive scripts. See Docs/Design/RowLevelSecurity.md.
    /// </summary>
    Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, string? sessionId = null, CancellationToken cancellationToken = default, string? jobName = null, long queueWaitMs = 0, Governance.ExecutionIdentity? executionIdentity = null);

    /// <summary>
    /// Executes with one-run input-variable overrides. The separate overload preserves existing
    /// executors while making the privileged manual-trigger path explicit.
    /// </summary>
    Task<ScriptExecutionResult> ExecuteTextAsync(
        string scriptText,
        string? sessionId,
        CancellationToken cancellationToken,
        string? jobName,
        long queueWaitMs,
        Governance.ExecutionIdentity? executionIdentity,
        IReadOnlyDictionary<string, string> variableOverrides) =>
        throw new NotSupportedException("This script executor does not support variable overrides.");

    /// <summary>
    /// Restores an existing persistent session and resumes at its last completed author-declared
    /// checkpoint. Arbitrary statement offsets are intentionally not part of this contract.
    /// </summary>
    Task<ScriptExecutionResult> ResumeTextAsync(
        string scriptText,
        string sessionId,
        CancellationToken cancellationToken = default,
        string? jobName = null,
        long queueWaitMs = 0,
        Governance.ExecutionIdentity? executionIdentity = null) =>
        throw new NotSupportedException("This script executor does not support named-checkpoint resume.");
}

public record ScriptExecutionResult(
    bool Success,
    long RowsProcessed,
    string? ErrorMessage = null,
    long PeakMemoryBytes = 0,
    double CpuTimeSeconds = 0,
    string? SessionId = null,
    /// <summary>Rows removed from output by an <c>@expect</c> QUARANTINE action during this run.</summary>
    long RowsQuarantined = 0,
    /// <summary>Rows that failed a WARN rule but still reached the target during this run.</summary>
    long RowsWarned = 0,
    /// <summary>Compact per-rule failure counts (<c>column:rule=count;…</c>); counts only, never sample values.</summary>
    string? DataQualityFailures = null,
    /// <summary>Column-level run metrics collected for ASSERT JOB predicates.</summary>
    IReadOnlyList<DataQualityColumnMetric>? DataQualityColumnMetrics = null,
    /// <summary>Structured counts-only rule failures; never contains sample values.</summary>
    IReadOnlyList<DataQualityRuleFailureMetric>? DataQualityRuleFailures = null,
    /// <summary>
    /// Per-statement measurements for the run flight recorder. Statement text is normalized by
    /// <c>StatementMetricsPayload.From</c> before it gets here, and the list is capped — a run is
    /// represented by its failures and its slowest statements, not by every statement it executed.
    /// </summary>
    IReadOnlyList<ETL_SQL.Core.Profiling.StatementMetricsPayload>? StatementMetrics = null);
