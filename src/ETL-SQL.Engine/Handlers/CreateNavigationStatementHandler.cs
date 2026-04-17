using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles CREATE NAVIGATION statements (Phase 9.3 Report-SQL).
    /// Registers the navigation definition in session context for the ManifestBuilder.
    /// </summary>
    public class CreateNavigationStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreateNavigationStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateNavigationStatement)statement;
            context.NavigationDefinitions[stmt.Name] = stmt;
            _logger.Debug("Navigation '{NavigationName}' registered.", stmt.Name);
            context.Log($"Navigation '{stmt.Name}' registered.");
            return Task.CompletedTask;
        }
    }
}
