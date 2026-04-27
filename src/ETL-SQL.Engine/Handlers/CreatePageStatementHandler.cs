using System;
using System.Collections.Generic;
using System.Linq;
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

            // Phase 1: Validate STRUCTURE vs MAP consistency
            var structureKeys = stmt.Structure
                .Split(new[] { '/', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s != ".") // CSS grid empty-cell placeholder
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var key in structureKeys)
            {
                if (!stmt.SlotMap.ContainsKey(key))
                    throw new ExecutionException(
                        $"CREATE PAGE '{stmt.Name}': slot '{key}' is defined in STRUCTURE but is missing from the MAP clause.",
                        null, stmt.Line, stmt.Column);
            }

            foreach (var mapKey in stmt.SlotMap.Keys)
            {
                if (!structureKeys.Contains(mapKey, StringComparer.OrdinalIgnoreCase))
                    throw new ExecutionException(
                        $"CREATE PAGE '{stmt.Name}': slot '{mapKey}' is defined in MAP but does not appear in the STRUCTURE string.",
                        null, stmt.Line, stmt.Column);
            }

            // Phase 2: Validate that every slot references a known visual, container, or button
            foreach (var (slot, visualName) in stmt.SlotMap)
            {
                bool isVisual    = context.VisualDefinitions.ContainsKey(visualName);
                bool isContainer = context.ContainerDefinitions.ContainsKey(visualName);
                bool isButton    = context.ButtonDefinitions.ContainsKey(visualName);

                if (!isVisual && !isContainer && !isButton)
                {
                    throw new ExecutionException(
                        $"CREATE PAGE '{stmt.Name}': slot '{slot}' references '{visualName}' which has not been defined as a visual, container, or button.",
                        null, stmt.Line, stmt.Column);
                }
            }

            // Phase 3: Register / overwrite page definition
            if (stmt.Mode == ObjectCreationMode.Create && context.PageDefinitions.ContainsKey(stmt.Name))
            {
                 throw new ExecutionException($"Page '{stmt.Name}' already exists. Use CREATE OR ALTER or DROP PAGE first.", null, stmt.Line, stmt.Column);
            }

            context.PageDefinitions[stmt.Name] = stmt;


            _logger.Debug("Page '{PageName}' registered with {SlotCount} visual slot(s).", stmt.Name, stmt.SlotMap.Count);
            context.Log($"Page '{stmt.Name}' {(stmt.Mode == ObjectCreationMode.CreateOrAlter ? "updated" : "created")}.");

            return Task.CompletedTask;
        }
    }
}
