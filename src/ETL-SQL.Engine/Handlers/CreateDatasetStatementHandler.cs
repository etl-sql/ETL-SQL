using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles CREATE DATASET statements.
    ///
    /// In standalone (non-portal) mode: materialises the source query into a named
    /// temp table (SELECT INTO equivalent) so the rest of the script can use it.
    ///
    /// In portal mode (IDatasetRegistry is available on the Evaluator): also persists
    /// the result as a machine-key-encrypted Parquet file, registers the dataset in
    /// portal.db, writes a sidecar refresh script, and optionally creates a scheduled
    /// refresh job when REFRESH EVERY is specified.  On subsequent executions within
    /// the TTL the Parquet cache is loaded instead of re-running the source query.
    /// </summary>
    public class CreateDatasetStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreateDatasetStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateDatasetStatement)statement;

            // ── 1. Validate encryption credential completeness ─────────────────────
            // (Mode-appropriateness is a lint warning, not a runtime error — users may
            //  deliberately choose PASSWORD/KEYFILE for portable cross-machine datasets.)
            if (stmt.EncryptionMode is DatasetEncryptionMode.Password
                && string.IsNullOrWhiteSpace(stmt.EncryptionPassword))
                throw new ExecutionException(
                    $"CREATE DATASET '{stmt.TempTableName}': ENCRYPT = PASSWORD requires PASSWORD = '...' to be specified.",
                    null, stmt.Line, stmt.Column);

            if (stmt.EncryptionMode is DatasetEncryptionMode.KeyFile
                && string.IsNullOrWhiteSpace(stmt.KeyFile))
                throw new ExecutionException(
                    $"CREATE DATASET '{stmt.TempTableName}': ENCRYPT = KEYFILE requires KEYFILE = '...' to be specified.",
                    null, stmt.Line, stmt.Column);

            // ── 2. Resolve portal registry and folder ──────────────────────────────
            var registry = context is Evaluator e ? e.DatasetRegistry : null;
            var folderPath = Path.GetDirectoryName(context.CurrentScriptPath) ?? "";

            // ── 3. Staleness check — skip source query if cached data is fresh ─────
            if (registry != null)
            {
                var existing = await registry.Lookup(stmt.TempTableName, folderPath, "IsAdmin=true");
                if (IsFreshEnough(existing, stmt.Ttl))
                {
                    _logger.Debug(
                        "Dataset '{Name}' is within TTL (last refresh: {Time}). Loading from Parquet cache.",
                        stmt.TempTableName, existing!.LastRefresh);
                    await LoadFromParquet(existing.ParquetFilePath, stmt.TempTableName, stmt, context);
                    RegisterReportContext(stmt, context);
                    await context.EnsureCatalogMetadataImportedAsync(stmt.SourceQuery.GetSourceTables());
                    new LineageManager(context.LineageTracker).RecordCreateDatasetLineage(stmt);
                    return;
                }
            }

            // ── 4. Materialise source query into temp table ────────────────────────
            await MaterializeSourceQuery(stmt, context);
            var rowCount = context.Telemetry.LastStatementRowsProcessed;

            // ── 5. Portal persistence (Parquet write → registry → refresh job) ──────
            if (registry != null)
                await PersistToPortal(stmt, registry, folderPath, rowCount, context);

            // ── 6. Register AST in report context (for ManifestBuilder) ───────────
            RegisterReportContext(stmt, context);
            await context.EnsureCatalogMetadataImportedAsync(stmt.SourceQuery.GetSourceTables());
            new LineageManager(context.LineageTracker).RecordCreateDatasetLineage(stmt);

            var intervalNote = string.IsNullOrWhiteSpace(stmt.RefreshInterval) ? ""
                : $" (refresh every {stmt.RefreshInterval})";
            context.Log(
                $"Dataset '{stmt.TempTableName}' {(stmt.Mode == ObjectCreationMode.CreateOrAlter ? "updated" : "created")}{intervalNote}.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private static bool IsFreshEnough(DatasetMetadata? existing, string? ttlOverride)
        {
            if (existing == null
                || string.IsNullOrWhiteSpace(existing.ParquetFilePath)
                || !File.Exists(existing.ParquetFilePath)
                || !existing.LastRefresh.HasValue)
                return false;

            TimeSpan? ttl = ttlOverride == null && existing.CachedTtl.HasValue
                ? existing.CachedTtl
                : ParseDuration(ttlOverride ?? existing.Ttl);

            if (!ttl.HasValue) return false;

            return existing.LastRefresh.Value + ttl.Value > DateTime.UtcNow;
        }

        // Intentional ordering: write Parquet first so the registry entry always points to data that exists.
        // If registry update succeeds but CreateRefreshJob fails, the dataset is registered but will not
        // auto-refresh — the operator must re-run the script or create a job manually.  No rollback is
        // attempted because the Parquet file and registry entry are both valid; only the refresh job is missing.
        private async Task PersistToPortal(
            CreateDatasetStatement stmt, IDatasetRegistry registry,
            string folderPath, long rowCount, IExecutionContext context)
        {
            var parquetPath = registry.BuildDatasetFilePath(stmt.TempTableName, folderPath);

            await WriteToParquet(stmt.TempTableName, parquetPath, stmt, context);
            WriteSidecarScript(stmt, parquetPath, context);

            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name           = stmt.TempTableName,
                FolderPath     = folderPath,
                ParquetFilePath = parquetPath,
                SourceQuery    = stmt.SourceQuery.ToSql(),
                AccessLevel    = stmt.AccessLevel,
                EncryptionMode = stmt.EncryptionMode,
                LastRefresh    = DateTime.UtcNow,
                Ttl            = stmt.Ttl,
                CachedTtl      = ParseDuration(stmt.Ttl),
                RefreshInterval = stmt.RefreshInterval,
                RowCount       = rowCount
            });

            if (!string.IsNullOrWhiteSpace(stmt.RefreshInterval))
                await CreateRefreshJob(stmt, parquetPath, context);
        }

        private async Task MaterializeSourceQuery(CreateDatasetStatement stmt, IExecutionContext context)
        {
            // Enforce CREATE vs. CREATE OR ALTER uniqueness
            if (stmt.Mode == ObjectCreationMode.Create
                && context is IReportContext rc
                && rc.DatasetDefinitions.ContainsKey(stmt.TempTableName))
            {
                throw new ExecutionException(
                    $"Dataset '{stmt.TempTableName}' already exists. Use CREATE OR ALTER DATASET or DROP DATASET first.",
                    null, stmt.Line, stmt.Column);
            }

            Statement selectInto;
            if (stmt.SourceQuery is SelectStatement sel)
            {
                selectInto = sel with { IntoTable = new TableReference(stmt.TempTableName) };
            }
            else
            {
                // Wrap non-SELECT source in SELECT * INTO #name FROM (<source>) AS _src
                selectInto = new SelectStatement(
                    new List<SelectColumn> { new(new IdentifierExpression("*"), null, null) },
                    new TableReference(stmt.TempTableName),
                    new TableReference("SUBQUERY", null, null, null, "_src", stmt.SourceQuery),
                    new List<JoinClause>(),
                    null);
            }

            _logger.Debug("Materialising dataset '{Name}'...", stmt.TempTableName);
            await context.EvaluateStatement(selectInto);
        }

        private async Task WriteToParquet(
            string tempTableName, string parquetPath,
            CreateDatasetStatement stmt, IExecutionContext context)
        {
            var connAlias = $"__ds_write_{Guid.NewGuid():N}__";

            var connStmt = new CreateConnectionStatement(
                connAlias, "PARQUET",
                new LiteralExpression(parquetPath, TokenType.STRING_LITERAL),
                BuildParquetOptions(stmt, includeCompression: true));

            var insertStmt = new InsertStatement(
                new TableReference("FILE", null, null, connAlias),
                new SelectStatement(
                    new List<SelectColumn> { new(new IdentifierExpression("*"), null, null) },
                    null,
                    new TableReference(tempTableName),
                    new List<JoinClause>(),
                    null));

            await context.EvaluateStatement(connStmt);
            await context.EvaluateStatement(insertStmt);
        }

        private async Task LoadFromParquet(
            string parquetPath, string tempTableName,
            CreateDatasetStatement stmt, IExecutionContext context)
        {
            var connAlias = $"__ds_load_{Guid.NewGuid():N}__";

            var connStmt = new CreateConnectionStatement(
                connAlias, "PARQUET",
                new LiteralExpression(parquetPath, TokenType.STRING_LITERAL),
                BuildParquetOptions(stmt, includeCompression: false));

            var selectStmt = new SelectStatement(
                new List<SelectColumn> { new(new IdentifierExpression("*"), null, null) },
                new TableReference(tempTableName),
                new TableReference("FILE", null, null, connAlias),
                new List<JoinClause>(),
                null);

            await context.EvaluateStatement(connStmt);
            await context.EvaluateStatement(selectStmt);
        }

        private async Task CreateRefreshJob(
            CreateDatasetStatement stmt, string parquetPath, IExecutionContext context)
        {
            var schedule = ParseRefreshInterval(stmt.RefreshInterval!);
            if (schedule == null)
            {
                _logger.Debug(
                    "Dataset '{Name}': could not parse refresh interval '{Interval}' — skipping job creation.",
                    stmt.TempTableName, stmt.RefreshInterval);
                return;
            }

            var connAlias = $"__ds_{MakeSafeAlias(stmt.TempTableName)}__";

            var connStmt = new CreateConnectionStatement(
                connAlias, "PARQUET",
                new LiteralExpression(parquetPath, TokenType.STRING_LITERAL),
                BuildParquetOptions(stmt, includeCompression: true),
                ObjectCreationMode.CreateOrAlter);

            var insertStmt = new InsertStatement(
                new TableReference("FILE", null, null, connAlias),
                stmt.SourceQuery);

            var jobScript = new BlockStatement(new List<Statement> { connStmt, insertStmt });
            var jobName   = $"__dataset_refresh_{MakeSafeAlias(stmt.TempTableName)}__";

            var jobStmt = new CreateJobStatement(jobName, schedule, jobScript);
            await context.EvaluateStatement(jobStmt);
        }

        private void WriteSidecarScript(CreateDatasetStatement stmt, string parquetPath, IExecutionContext context)
        {
            try
            {
                var sidecarPath = context.ResolvePath(Path.ChangeExtension(parquetPath, ".etlsql"));
                var connAlias   = $"__ds_{MakeSafeAlias(stmt.TempTableName)}__";
                var encLabel    = EncryptLabel(stmt.EncryptionMode);

                // Build WITH clause — include credential if non-machine mode
                var withExtra = stmt.EncryptionMode switch
                {
                    DatasetEncryptionMode.Password => $", PASSWORD = '{stmt.EncryptionPassword}'",
                    DatasetEncryptionMode.KeyFile  => $", KEYFILE = '{stmt.KeyFile}'",
                    _                              => ""
                };

                var content = $"""
                    -- Dataset refresh script for {stmt.TempTableName}
                    -- Auto-generated by the ETL-SQL engine. Do not edit manually.
                    -- Source: {stmt.TempTableName} | Parquet: {parquetPath}
                    CREATE OR ALTER CONNECTION {connAlias} AS PARQUET('{parquetPath}', COMPRESSION = 'SNAPPY', ENCRYPT = '{encLabel}'{withExtra});
                    INSERT INTO {connAlias}.FILE
                    {stmt.SourceQuery.ToSql()};
                    """;

                File.WriteAllText(sidecarPath, content, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Sidecar is non-critical; log and continue
                _logger.Debug("Could not write sidecar script for dataset '{Name}': {Error}",
                    stmt.TempTableName, ex.Message);
            }
        }

        private static void RegisterReportContext(CreateDatasetStatement stmt, IExecutionContext context)
        {
            if (context is IReportContext rc)
                rc.DatasetDefinitions[stmt.TempTableName] = stmt;
        }

        // ── Static utilities ─────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the encryption-related options for a synthetic PARQUET CreateConnectionStatement.
        /// MACHINE → ENCRYPT=MACHINE (no credential needed).
        /// PASSWORD → ENCRYPT=PASSWORD + PASSWORD=value.
        /// KEYFILE  → ENCRYPT=KEYFILE  + KEYFILE=path.
        /// None     → no encryption options added.
        /// </summary>
        private static Dictionary<string, Expression> BuildParquetOptions(
            CreateDatasetStatement stmt, bool includeCompression = true)
        {
            var opts = new Dictionary<string, Expression>();

            if (includeCompression)
                opts["COMPRESSION"] = new LiteralExpression("SNAPPY", TokenType.STRING_LITERAL);

            switch (stmt.EncryptionMode)
            {
                case DatasetEncryptionMode.MachineBound:
                    opts["ENCRYPT"] = new LiteralExpression("MACHINE", TokenType.STRING_LITERAL);
                    break;
                case DatasetEncryptionMode.Password:
                    opts["ENCRYPT"]   = new LiteralExpression("PASSWORD",                   TokenType.STRING_LITERAL);
                    opts["PASSWORD"]  = new LiteralExpression(stmt.EncryptionPassword ?? "", TokenType.STRING_LITERAL);
                    break;
                case DatasetEncryptionMode.KeyFile:
                    opts["ENCRYPT"]  = new LiteralExpression("KEYFILE",          TokenType.STRING_LITERAL);
                    opts["KEYFILE"]  = new LiteralExpression(stmt.KeyFile ?? "", TokenType.STRING_LITERAL);
                    break;
                // DatasetEncryptionMode.None → no ENCRYPT option → Parquet written unencrypted
            }

            return opts;
        }

        private static string EncryptLabel(DatasetEncryptionMode mode) => mode switch
        {
            DatasetEncryptionMode.MachineBound => "MACHINE",
            DatasetEncryptionMode.Password     => "PASSWORD",
            DatasetEncryptionMode.KeyFile      => "KEYFILE",
            _                                  => "OFF"
        };

        private static string MakeSafeAlias(string name) =>
            Regex.Replace(name.TrimStart('&', '#'), @"[^\w]", "_").ToLowerInvariant();

        private static ScheduleInfo? ParseRefreshInterval(string interval)
        {
            var match = Regex.Match(interval.Trim(), @"^(\d+)([smhd])$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            int value = int.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value.ToUpperInvariant() switch
            {
                "S" => "SECOND",
                "M" => "MINUTE",
                "H" => "HOUR",
                "D" => "DAY",
                _   => "MINUTE"
            };
            return new ScheduleInfo(value, unit);
        }

        private static TimeSpan? ParseDuration(string? duration)
        {
            if (string.IsNullOrWhiteSpace(duration)) return null;

            var match = Regex.Match(duration.Trim(), @"^(\d+)([smhd])$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            int value = int.Parse(match.Groups[1].Value);
            return match.Groups[2].Value.ToUpperInvariant() switch
            {
                "S" => TimeSpan.FromSeconds(value),
                "M" => TimeSpan.FromMinutes(value),
                "H" => TimeSpan.FromHours(value),
                "D" => TimeSpan.FromDays(value),
                _   => null
            };
        }
    }
}
