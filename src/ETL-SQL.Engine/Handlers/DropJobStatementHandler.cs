using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the DROP JOB statement, removing a scheduled ETL-SQL script job.
/// </summary>
public class DropJobStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(DropJobStatement);
    private readonly IJobHistoryStore _store;

    public DropJobStatementHandler(IJobHistoryStore store)
    {
        _store = store;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (DropJobStatement)statement;

        if (context.IsWhatIf)
        {
            context.Log($"WHAT IF: Would drop job '{stmt.Name}'.", ConsoleColor.Yellow);
            return;
        }

        try
        {
            // DeleteJobAsync deletes both the job and its history entries.
            await _store.DeleteJobAsync(stmt.Name);
            CatalogStatementSupport.AuditMutation(
                context,
                "DROP_JOB",
                $"JOB:{stmt.Name}",
                $"Job '{stmt.Name}' dropped.");
            context.Log($"Job '{stmt.Name}' dropped successfully.", ConsoleColor.Yellow);
        }
        catch (Exception ex)
        {
            if (!stmt.IfExists)
            {
                throw new ExecutionException($"Failed to drop job '{stmt.Name}': {ex.Message}", null, stmt.Line, stmt.Column);
            }
        }
    }
}
