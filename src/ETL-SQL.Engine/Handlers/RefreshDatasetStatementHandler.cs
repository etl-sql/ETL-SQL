using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles REFRESH DATASET &amp;name — forces re-execution of the stored source query,
    /// re-writes the Parquet file, and updates LastRefresh in the portal registry.
    ///
    /// Requires portal mode (IDatasetRegistry available). In non-portal mode the statement
    /// throws an ExecutionException since there is no persistence layer to refresh.
    /// </summary>
    public class RefreshDatasetStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(RefreshDatasetStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (RefreshDatasetStatement)statement;

            var registry = context is Evaluator e ? e.DatasetRegistry : null;
            if (registry == null)
                throw new ExecutionException(
                    $"REFRESH DATASET '{stmt.DatasetName}' requires portal mode. " +
                    "Datasets can only be refreshed when a DatasetRegistry is available.",
                    null, stmt.Line, stmt.Column);

            var callerCtx  = (context as Evaluator)?.DatasetCallerContext ?? "";
            var existing   = await registry.Lookup(stmt.DatasetName, callerCtx);
            if (existing == null)
                throw new ExecutionException(
                    $"REFRESH DATASET '{stmt.DatasetName}': dataset not found in the portal registry. " +
                    "Run CREATE DATASET first.",
                    null, stmt.Line, stmt.Column);

            if (string.IsNullOrWhiteSpace(existing.SourceQuery))
                throw new ExecutionException(
                    $"REFRESH DATASET '{stmt.DatasetName}': no source query stored in registry. " +
                    "The dataset may have been created by an older version of the engine.",
                    null, stmt.Line, stmt.Column);

            _logger.Debug("REFRESH DATASET '{Name}': re-materialising from source query...", stmt.DatasetName);

            // ── 1. Parse and re-execute the stored source SQL ─────────────────
            var tokens     = new Lexer(existing.SourceQuery).Tokenize();
            var parser     = new Parser(tokens);
            var sourceStmt = new StatementParser(parser).ParseStatement();

            Statement selectInto;
            if (sourceStmt is SelectStatement sel)
                selectInto = sel with { IntoTable = new TableReference(stmt.DatasetName) };
            else
                selectInto = new SelectStatement(
                    new List<SelectColumn> { new(new IdentifierExpression("*"), null, null) },
                    new TableReference(stmt.DatasetName),
                    new TableReference("SUBQUERY", null, null, null, "_src", sourceStmt),
                    new List<JoinClause>(),
                    null);

            await context.EvaluateStatement(selectInto);
            var rowCount = context.Telemetry.LastStatementRowsProcessed;

            // ── 2. Re-write Parquet with machine-bound encryption ─────────────
            var parquetPath = registry.BuildDatasetFilePath(existing.Id, stmt.DatasetName);
            var connAlias   = $"__ds_write_{Guid.NewGuid():N}__";

            var connStmt = new CreateConnectionStatement(
                connAlias, "PARQUET",
                new LiteralExpression(parquetPath, TokenType.STRING_LITERAL),
                new Dictionary<string, Expression>
                {
                    ["COMPRESSION"] = new LiteralExpression("SNAPPY", TokenType.STRING_LITERAL),
                    ["ENCRYPT"]     = new LiteralExpression("MACHINE", TokenType.STRING_LITERAL)
                });

            var insertStmt = new InsertStatement(
                new TableReference("FILE", null, null, connAlias),
                new SelectStatement(
                    new List<SelectColumn> { new(new IdentifierExpression("*"), null, null) },
                    null,
                    new TableReference(stmt.DatasetName),
                    new List<JoinClause>(),
                    null));

            await context.EvaluateStatement(connStmt);
            await context.EvaluateStatement(insertStmt);

            // ── 3. Update registry ────────────────────────────────────────────
            existing.ParquetFilePath = parquetPath;
            existing.LastRefresh     = DateTime.UtcNow;
            existing.RowCount        = rowCount;
            await registry.RegisterOrUpdate(existing);

            context.Log($"Dataset '{stmt.DatasetName}' refreshed ({rowCount:N0} rows).");
        }
    }
}
