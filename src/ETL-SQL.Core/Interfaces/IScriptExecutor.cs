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
    /// <summary>Executes the given script text and returns the result.</summary>
    Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, CancellationToken cancellationToken = default);
}

/// <summary>Lightweight result returned by IScriptExecutor.</summary>
public record ScriptExecutionResult(bool Success, long RowsProcessed, string? ErrorMessage = null);
