using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine.Services;

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
                bool isQueryString = tableName.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);

                if (!isQueryString &&
                    !context.Connections.ContainsKey(tableName) &&
                    !context.LocalSources.ContainsKey(tableName) &&
                    !context.VarContext.TryGetView(tableName, out _))
                {
                    throw new ExecutionException(
                        $"CREATE VISUAL '{stmt.Name}': source temp table '{tableName}' does not exist.",
                        null, stmt.Line, stmt.Column);
                }
            }

            // Register / overwrite visual definition
            bool alreadyExists = context.ReportContext.VisualDefinitions.ContainsKey(stmt.Name);
            if (stmt.Mode == ObjectCreationMode.Create && alreadyExists && !context.InteractiveMode)
            {
                throw new ExecutionException($"Visual '{stmt.Name}' already exists. Use CREATE OR ALTER or DROP VISUAL first.", null, stmt.Line, stmt.Column);
            }

            context.ReportContext.VisualDefinitions[stmt.Name] = stmt;
            new LineageManager(context.LineageTracker).RecordCreateVisualLineage(stmt);

            _logger.Debug("Visual '{VisualName}' ({VisualType}) registered.", stmt.Name, stmt.VisualType);
            context.Log($"Visual '{stmt.Name}' {(alreadyExists ? "updated" : "created")}.");

            if (context.InteractiveMode && context is Evaluator eval)
            {
                eval.OnVisualCreated?.Invoke(stmt);
            }

            return Task.CompletedTask;
        }
    }
}

