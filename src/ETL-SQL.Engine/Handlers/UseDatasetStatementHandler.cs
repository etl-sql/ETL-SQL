using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles USE DATASET &amp;name — loads a portal-registered dataset into the calling
    /// script's temp-table namespace.
    ///
    /// Portal mode: resolves the dataset by its globally unique name in the registry (folder-
    /// independent). Access is enforced by the registry's ACL gate. If the cache is fresh
    /// (LastRefresh + TTL &gt; now), loads from Parquet; otherwise re-materialises the source
    /// query, writes a new Parquet file, and updates the registry.
    ///
    /// Non-portal mode: the dataset must already be defined in the current script via
    /// CREATE DATASET — USE DATASET is a no-op since the temp table already exists.
    /// </summary>
    public class UseDatasetStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(UseDatasetStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (UseDatasetStatement)statement;

            var registry   = context is Evaluator e ? e.DatasetRegistry : null;

            if (registry == null)
            {
                // Non-portal: the dataset should already be in the execution context
                if (context is IReportContext rc && rc.DatasetDefinitions.ContainsKey(stmt.DatasetName))
                {
                    _logger.Debug("USE DATASET '{Name}': dataset already loaded in script context.", stmt.DatasetName);
                    return;
                }
                throw new ExecutionException(
                    $"USE DATASET '{stmt.DatasetName}': dataset not found in the current script. " +
                    "In non-portal mode, CREATE DATASET must precede USE DATASET in the same script.",
                    null, stmt.Line, stmt.Column);
            }

            // Registry enforces the dataset ACL against the executing user (CanReadAsync): a PRIVATE
            // dataset the caller cannot read resolves to null, surfacing as the "not found" error
            // below (existence is not leaked). PUBLIC datasets resolve by global name from any folder.
            var callerCtx = (context as Evaluator)?.DatasetCallerContext ?? "";
            var existing  = await registry.Lookup(stmt.DatasetName, callerCtx);
            if (existing == null)
                throw new ExecutionException(
                    $"USE DATASET '{stmt.DatasetName}': dataset not found in the portal registry. " +
                    "Run CREATE DATASET first.",
                    null, stmt.Line, stmt.Column);

            if (IsFreshEnough(existing))
            {
                _logger.Debug(
                    "USE DATASET '{Name}': loading from Parquet cache (last refresh: {Time}).",
                    stmt.DatasetName, existing.LastRefresh);
                await LoadFromParquet(existing.ParquetFilePath, stmt.DatasetName, context);
            }
            else
            {
                _logger.Debug(
                    "USE DATASET '{Name}': cache is stale — re-materialising from source query.",
                    stmt.DatasetName);
                await RematerialiseAndRefresh(stmt.DatasetName, existing, registry, context);
            }

            if (context is IReportContext reportCtx)
            {
                // Synthesise a minimal CreateDatasetStatement to satisfy ManifestBuilder lookups
                reportCtx.DatasetDefinitions[stmt.DatasetName] = new CreateDatasetStatement
                {
                    TempTableName = stmt.DatasetName,
                    SourceQuery   = new NoOpStatement(),
                    Line          = stmt.Line,
                    Column        = stmt.Column
                };
            }

            context.Log($"Dataset '{stmt.DatasetName}' loaded into temp-table namespace.");
        }

        private static bool IsFreshEnough(DatasetMetadata existing)
        {
            if (string.IsNullOrWhiteSpace(existing.ParquetFilePath)
                || !File.Exists(existing.ParquetFilePath)
                || !existing.LastRefresh.HasValue)
                return false;

            if (string.IsNullOrWhiteSpace(existing.Ttl)) return true; // no TTL = always fresh

            var ttl = ParseDuration(existing.Ttl);
            return ttl.HasValue && existing.LastRefresh.Value + ttl.Value > DateTime.UtcNow;
        }

        private async Task LoadFromParquet(string parquetPath, string datasetName, IExecutionContext context)
        {
            var connAlias = $"__ds_load_{Guid.NewGuid():N}__";

            var connStmt = new CreateConnectionStatement(
                connAlias, "PARQUET",
                new LiteralExpression(parquetPath, TokenType.STRING_LITERAL),
                new System.Collections.Generic.Dictionary<string, Expression>
                {
                    ["ENCRYPT"] = new LiteralExpression("MACHINE", TokenType.STRING_LITERAL)
                });

            var selectStmt = new SelectStatement(
                new List<SelectColumn> { new(new IdentifierExpression("*"), null, null) },
                new TableReference(datasetName),
                new TableReference("FILE", null, null, connAlias),
                new List<JoinClause>(),
                null);

            await context.EvaluateStatement(connStmt);
            await context.EvaluateStatement(selectStmt);
        }

        private async Task RematerialiseAndRefresh(
            string datasetName, DatasetMetadata existing,
            IDatasetRegistry registry,
            IExecutionContext context)
        {
            // Parse and re-execute the stored source SQL
            var tokens     = new Lexer(existing.SourceQuery).Tokenize();
            var parser     = new Parser(tokens);
            var sourceStmt = new StatementParser(parser).ParseStatement();

            Statement selectInto;
            if (sourceStmt is SelectStatement sel)
                selectInto = sel with { IntoTable = new TableReference(datasetName) };
            else
                selectInto = new SelectStatement(
                    new List<SelectColumn> { new(new IdentifierExpression("*"), null, null) },
                    new TableReference(datasetName),
                    new TableReference("SUBQUERY", null, null, null, "_src", sourceStmt),
                    new List<JoinClause>(),
                    null);

            await context.EvaluateStatement(selectInto);
            var rowCount = context.Telemetry.LastStatementRowsProcessed;

            // Write refreshed Parquet
            var parquetPath = registry.BuildDatasetFilePath(existing.Id, datasetName);
            var connAlias   = $"__ds_write_{Guid.NewGuid():N}__";

            var connStmt = new CreateConnectionStatement(
                connAlias, "PARQUET",
                new LiteralExpression(parquetPath, TokenType.STRING_LITERAL),
                new System.Collections.Generic.Dictionary<string, Expression>
                {
                    ["COMPRESSION"] = new LiteralExpression("SNAPPY", TokenType.STRING_LITERAL),
                    ["ENCRYPT"]     = new LiteralExpression("MACHINE", TokenType.STRING_LITERAL)
                });

            var insertStmt = new InsertStatement(
                new TableReference("FILE", null, null, connAlias),
                new SelectStatement(
                    new List<SelectColumn> { new(new IdentifierExpression("*"), null, null) },
                    null,
                    new TableReference(datasetName),
                    new List<JoinClause>(),
                    null));

            await context.EvaluateStatement(connStmt);
            await context.EvaluateStatement(insertStmt);

            existing.ParquetFilePath = parquetPath;
            existing.LastRefresh     = DateTime.UtcNow;
            existing.RowCount        = rowCount;
            await registry.RegisterOrUpdate(existing);
        }

        private static TimeSpan? ParseDuration(string? duration)
        {
            if (string.IsNullOrWhiteSpace(duration)) return null;
            var m = Regex.Match(duration.Trim(), @"^(\d+)([smhd])$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            int v = int.Parse(m.Groups[1].Value);
            return m.Groups[2].Value.ToUpperInvariant() switch
            {
                "S" => TimeSpan.FromSeconds(v),
                "M" => TimeSpan.FromMinutes(v),
                "H" => TimeSpan.FromHours(v),
                "D" => TimeSpan.FromDays(v),
                _   => null
            };
        }
    }
}
