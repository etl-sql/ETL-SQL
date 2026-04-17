using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles CREATE CONTAINER statements (Phase 9.3 Report-SQL).
    /// Registers the container definition in session context for the ManifestBuilder.
    /// </summary>
    public class CreateContainerStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreateContainerStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateContainerStatement)statement;
            context.ContainerDefinitions[stmt.Name] = stmt;
            _logger.Debug("Container '{ContainerName}' registered.", stmt.Name);
            context.Log($"Container '{stmt.Name}' registered.");
            return Task.CompletedTask;
        }
    }
}
