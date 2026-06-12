using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles CREATE VIEW / ALTER VIEW / CREATE OR ALTER VIEW for session-scoped query aliases.
    /// </summary>
    public class CreateViewStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(CreateViewStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateViewStatement)statement;
            var exists = context.VarContext.TryGetView(stmt.ViewName, out _);

            if (stmt.ViewName.StartsWith("#") || stmt.ViewName.StartsWith("&") || stmt.ViewName.StartsWith("@"))
                throw new ExecutionException($"View {stmt.ViewName} cannot use a temporary table, dataset, or variable prefix.");

            if (stmt.Mode == ObjectCreationMode.Alter && !exists)
                throw new ExecutionException($"View {stmt.ViewName} does not exist.");
            if (stmt.Mode == ObjectCreationMode.Create && exists)
                throw new ExecutionException($"View {stmt.ViewName} already exists.");
            if (!exists && HasConflictingObject(context, stmt.ViewName))
                throw new ExecutionException($"Cannot create view {stmt.ViewName}: another object with that name already exists.");

            context.VarContext.SetView(stmt.ViewName, stmt);
            return Task.CompletedTask;
        }

        private static bool HasConflictingObject(IExecutionContext context, string name)
        {
            if (context.Connections.ContainsKey(name)) return true;
            if (context.LocalSources.ContainsKey(name)) return true;
            if (context.VarContext.TryGetProcedure(name, out _)) return true;
            if (context.VarContext.TryGetFunction(name, out _)) return true;
            if (context.ReportContext.DatasetDefinitions.ContainsKey(name)) return true;
            return false;
        }
    }
}
