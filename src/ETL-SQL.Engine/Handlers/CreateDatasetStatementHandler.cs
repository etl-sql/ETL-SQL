using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles CREATE DATASET statements (Phase 9A Report-SQL).
    /// Executes the source SELECT and materialises the result into a named temp table,
    /// equivalent to SELECT ... INTO #tableName.
    /// Encryption and scheduled refresh are validated here; execution is deferred to
    /// ReportBuilder (Phase 9B) where the SnapshotStore manages persistence.
    /// </summary>
    public class CreateDatasetStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreateDatasetStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateDatasetStatement)statement;

            switch (stmt.EncryptionMode)
            {
                case DatasetEncryptionMode.Password when string.IsNullOrWhiteSpace(stmt.EncryptionPassword):
                    throw new ExecutionException(
                        $"CREATE DATASET '{stmt.TempTableName}': ENCRYPT = PASSWORD requires PASSWORD = '...' to be specified.",
                        null, stmt.Line, stmt.Column);
                case DatasetEncryptionMode.KeyFile when string.IsNullOrWhiteSpace(stmt.KeyFile):
                    throw new ExecutionException(
                        $"CREATE DATASET '{stmt.TempTableName}': ENCRYPT = KEYFILE requires KEYFILE = '...' to be specified.",
                        null, stmt.Line, stmt.Column);
            }

            // Materialise the source query into the named temp table via SELECT INTO
            var src = stmt.SourceQuery;
            var selectInto = new SelectStatement(
                src.Columns, new TableReference(stmt.TempTableName), src.FromTable,
                src.Joins, src.WhereClause, src.GroupBy, src.HavingClause, src.OrderBy)
            {
                IsDistinct   = src.IsDistinct,
                TopCount     = src.TopCount,
                IsTopPercent = src.IsTopPercent,
                WithTies     = src.WithTies,
                LimitCount   = src.LimitCount,
                Offset       = src.Offset,
                ForClause    = src.ForClause,
                Ctes         = src.Ctes,
                IsRecursive  = src.IsRecursive,
                GroupingSet  = src.GroupingSet
            };

            _logger.Debug("Materialising dataset '{TempTableName}'...", stmt.TempTableName);
            await context.EvaluateStatement(selectInto);

            // Register AST node so ManifestBuilder / DashboardService can access refresh metadata
            if (context is IReportContext rc)
                rc.DatasetDefinitions[stmt.TempTableName] = stmt;

            var intervalNote = string.IsNullOrWhiteSpace(stmt.RefreshInterval)
                ? string.Empty
                : $" (refresh every {stmt.RefreshInterval})";
            context.Log($"Dataset '{stmt.TempTableName}' created{intervalNote}.");
        }
    }
}
