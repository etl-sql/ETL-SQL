using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles CREATE PAGE statements (Phase 9A Report-SQL).
    /// Validates that every visual referenced in the slot map has been registered,
    /// then stores the page definition in session context for ReportBuilder (Phase 9B).
    /// </summary>
    public class CreatePageStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreatePageStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreatePageStatement)statement;

            // Validate that every visual slot references a known visual definition
            foreach (var (slot, visualName) in stmt.SlotMap)
            {
                if (!context.VisualDefinitions.ContainsKey(visualName))
                {
                    throw new ExecutionException(
                        $"CREATE PAGE '{stmt.Name}': slot '{slot}' references visual '{visualName}' which has not been defined.",
                        null, stmt.Line, stmt.Column);
                }
            }

            // Register / overwrite page definition
            context.PageDefinitions[stmt.Name] = stmt;

            _logger.Debug($"Page '{stmt.Name}' registered with {stmt.SlotMap.Count} visual slot(s).");
            context.Log($"Page '{stmt.Name}' created.");

            return Task.CompletedTask;
        }
    }
}
