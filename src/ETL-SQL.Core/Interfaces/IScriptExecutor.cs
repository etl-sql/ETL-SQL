using System.Threading;
using System.Threading.Tasks;

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
    string? DataQualityFailures = null);
