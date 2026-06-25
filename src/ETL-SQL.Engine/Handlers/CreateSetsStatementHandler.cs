using System;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles CREATE SETS !&lt;name&gt; — stores a named collection of variable assignments.
/// </summary>
public class CreateSetsStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(CreateSetsStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateSetsStatement)statement;
        context.NamedSets[stmt.Name] = new NamedSet(stmt.Assignments, stmt.WithPrompt);
        context.Log($"Created set !{stmt.Name} with {stmt.Assignments.Count} assignment(s).");
        return Task.CompletedTask;
    }
}
