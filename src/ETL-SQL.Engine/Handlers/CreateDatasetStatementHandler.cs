using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
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
            Statement selectInto;

            if (src is SelectStatement sel)
            {
                // Optimization: direct SELECT INTO if it's a plain SELECT
                selectInto = sel with { IntoTable = new TableReference(stmt.TempTableName) };
            }
            else
            {
                // Wrap in SELECT * INTO #table FROM ( <query> ) AS src
                selectInto = new SelectStatement(
                    new List<SelectColumn> { new SelectColumn(new IdentifierExpression("*"), null, null) },
                    new TableReference(stmt.TempTableName),
                    new TableReference("SUBQUERY", null, null, null, "src", src),
                    new List<JoinClause>(),
                    null);
            }

            _logger.Debug("Materialising dataset '{TempTableName}'...", stmt.TempTableName);
            await context.EvaluateStatement(selectInto);

            // Register AST node so ManifestBuilder / DashboardService can access refresh metadata
            if (context is IReportContext rc)
            {
                if (stmt.Mode == ObjectCreationMode.Create && rc.DatasetDefinitions.ContainsKey(stmt.TempTableName))
                {
                    throw new ExecutionException($"Dataset '{stmt.TempTableName}' already exists. Use CREATE OR ALTER or DROP DATASET first.", null, stmt.Line, stmt.Column);
                }
                rc.DatasetDefinitions[stmt.TempTableName] = stmt;
            }

            // Phase 10 Integration: Register in persistent IDatasetRegistry if available
            if (context is Evaluator e && e.DatasetRegistry != null)
            {
                _logger.Debug("Registering dataset '{TempTableName}' in persistent registry...", stmt.TempTableName);
                
                var metadata = new ETL_SQL.Core.Data.DatasetMetadata
                {
                    Name = stmt.TempTableName,
                    SourceQuery = stmt.SourceQuery.ToSql(),
                    RefreshInterval = stmt.RefreshInterval,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    FolderPath = Path.GetDirectoryName(context.CurrentScriptPath) ?? ""
                };

                // Attempt to capture row count if it was just materialized
                if (context.Telemetry.LastStatementRowsProcessed > 0)
                {
                    metadata.RowCount = context.Telemetry.LastStatementRowsProcessed;
                }

                await e.DatasetRegistry.RegisterOrUpdate(metadata);
            }

            var intervalNote = string.IsNullOrWhiteSpace(stmt.RefreshInterval)
                ? string.Empty
                : $" (refresh every {stmt.RefreshInterval})";
            context.Log($"Dataset '{stmt.TempTableName}' {(stmt.Mode == ObjectCreationMode.CreateOrAlter ? "updated" : "created")}{intervalNote}.");
        }
    }
}
