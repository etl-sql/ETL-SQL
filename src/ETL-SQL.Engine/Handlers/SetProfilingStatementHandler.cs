using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SET PROFILING ON/OFF statement, enabling or disabling performance metric collection.
/// </summary>
public class SetProfilingStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(SetProfilingStatement);
    /// <summary>Executes the SET PROFILING statement, updating the evaluator's state.</summary>
    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetProfilingStatement)statement;
        if (context is Evaluator eval)
        {
            eval.Telemetry.IsProfiling = stmt.Enabled;
        }
        return Task.CompletedTask;
    }
}
