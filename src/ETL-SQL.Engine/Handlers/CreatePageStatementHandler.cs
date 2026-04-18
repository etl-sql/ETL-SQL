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

            // Validate that every visual slot references a known visual or container definition
            foreach (var (slot, visualName) in stmt.SlotMap)
            {
                bool isVisual = context.VisualDefinitions.ContainsKey(visualName);
                bool isContainer = context.ContainerDefinitions.ContainsKey(visualName);

                if (!isVisual && !isContainer)
                {
                    throw new ExecutionException(
                        $"CREATE PAGE '{stmt.Name}': slot '{slot}' references visual or container '{visualName}' which has not been defined.",
                        null, stmt.Line, stmt.Column);
                }
            }

            // Register / overwrite page definition
            context.PageDefinitions[stmt.Name] = stmt;

            // Declare page parameters with their defaults if not already in scope.
            // This ensures visuals whose SOURCE queries reference @param can execute
            // during ManifestBuilder.FetchVisualDataAsync even before the user has
            // interacted with any slicer.
            foreach (var param in stmt.Parameters)
            {
                var varName = param.Name.StartsWith('@') ? param.Name : '@' + param.Name;
                if (!context.Variables.ContainsKey(varName))
                    context.DeclareVariable(varName, param.DefaultValue);
            }

            _logger.Debug("Page '{PageName}' registered with {SlotCount} visual slot(s).", stmt.Name, stmt.SlotMap.Count);
            context.Log($"Page '{stmt.Name}' created.");

            return Task.CompletedTask;
        }
    }
}
