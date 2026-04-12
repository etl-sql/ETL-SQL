using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles CREATE VISUAL statements (Phase 9A Report-SQL).
    /// Validates the source reference and registers the visual definition in session context
    /// so that ReportBuilder (Phase 9B) can query it.
    /// </summary>
    public class CreateVisualStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreateVisualStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateVisualStatement)statement;

            // Validate temp-table source reference
            if (!stmt.Source.IsInlineSelect && stmt.Source.TempTableName != null)
            {
                var tableName = stmt.Source.TempTableName;
                if (!context.Connections.ContainsKey(tableName) &&
                    !context.LocalSources.ContainsKey(tableName))
                {
                    throw new ExecutionException(
                        $"CREATE VISUAL '{stmt.Name}': source temp table '{tableName}' does not exist.",
                        null, stmt.Line, stmt.Column);
                }
            }

            // Register / overwrite visual definition
            context.VisualDefinitions[stmt.Name] = stmt;

            _logger.Debug($"Visual '{stmt.Name}' ({stmt.VisualType}) registered.");
            context.Log($"Visual '{stmt.Name}' created.");

            return Task.CompletedTask;
        }
    }
}
