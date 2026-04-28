using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DROP CONNECTION statement, removing registered connections from the execution context.
    /// </summary>
    public class DropConnectionStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(DropConnectionStatement);

        /// <summary>Executes the DROP CONNECTION statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropConnectionStatement)statement;
            var connections = context.DataContext.Connections;

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would drop connection {stmt.ConnectionName}", ConsoleColor.Yellow);
                return;
            }

            if (connections.TryGetValue(stmt.ConnectionName, out var ds))
            {
                await ds.DisposeAsync();
                connections.Remove(stmt.ConnectionName);
                _logger.WriteLine($"Connection {stmt.ConnectionName} dropped.", ConsoleColor.Yellow);
            }
            else if (!stmt.IfExists)
            {
                throw new ExecutionException($"Connection not found: {stmt.ConnectionName}");
            }
        }
    }
}
