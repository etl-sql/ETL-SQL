using System;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles DROP SETS [IF EXISTS] !&lt;name&gt;
    /// </summary>
    public class DropSetsStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(DropSetsStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropSetsStatement)statement;
            if (!context.NamedSets.Remove(stmt.Name))
            {
                if (!stmt.IfExists)
                    throw new System.Exception($"Named set '!{stmt.Name}' does not exist.");
            }
            else
            {
                context.Log($"Dropped set !{stmt.Name}.");
            }
            return Task.CompletedTask;
        }
    }
}
