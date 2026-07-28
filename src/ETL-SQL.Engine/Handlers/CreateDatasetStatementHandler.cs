using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles CREATE DATASET statements.
///
/// In standalone (non-portal) mode: materialises the source query into a named
/// temp table (SELECT INTO equivalent) so the rest of the script can use it.
///
/// In portal mode (IDatasetRegistry is available on the Evaluator): also persists
/// the result as an encrypted Parquet file, registers the dataset in portal.db,
/// and loads a still-valid TTL cache instead of re-running the source query.
/// </summary>
public class CreateDatasetStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(CreateDatasetStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateDatasetStatement)statement;

        // ── 1. Resolve portal registry and folder ──────────────────────────────
        var registry = context is Evaluator e ? e.DatasetRegistry : null;
        var folderPath = Path.GetDirectoryName(context.CurrentScriptPath) ?? "";

        // ── 2. Validate encryption credential completeness (non-portal only) ────
        // In a portal the at-rest cache always uses the portal key, so an ENCRYPT=PASSWORD|KEYFILE
        // clause is a transport credential that is ignored at rest (see EXPORT/PUBLISH DATASET);
        // a missing credential is harmless. In non-portal mode the clause is honored at write time,
        // so the credential must be complete.
        if (registry == null)
        {
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
        }

        // ── 3. Staleness check — skip source query if cached data is fresh ─────
        if (registry != null)
        {
            var callerCtx = (context as Evaluator)?.DatasetCallerContext ?? "";

            // Redefining an existing dataset (CREATE OR ALTER) is a write — restrict to editor/owner
            // (admin/scheduled jobs pass). A brand-new CREATE is unaffected.
            if (stmt.Mode == ObjectCreationMode.CreateOrAlter
                && await registry.Exists(stmt.TempTableName)
                && !await registry.CanEditAsync(stmt.TempTableName, callerCtx))
                throw new ExecutionException(
                    $"CREATE OR ALTER DATASET '{stmt.TempTableName}' requires editor or owner permission.",
                    null, stmt.Line, stmt.Column);

            var existing = await registry.Lookup(stmt.TempTableName, callerCtx);
            if (IsFreshEnough(existing, stmt.Ttl))
            {
                await context.EnsureCatalogMetadataImportedAsync(stmt.SourceQuery.GetSourceTables());
                new LineageManager(context.LineageTracker).RecordSelectIntoLineage(
                    stmt.SourceQuery,
                    new TableReference(stmt.TempTableName),
                    context);
                TagGovernanceRuntimePolicy.EnforceDatasetPublish(
                    stmt.TempTableName,
                    stmt.AccessLevel,
                    TagGovernanceRuntimePolicy.CollectDatasetTags(stmt, context),
                    stmt.Line,
                    stmt.Column);

                _logger.Debug(
                    "Dataset '{Name}' is within TTL (last refresh: {Time}). Loading from Parquet cache.",
                    stmt.TempTableName, existing!.LastRefresh);
                await LoadFromParquet(
                    existing.ParquetFilePath,
                    stmt.TempTableName,
                    stmt,
                    context,
                    existing.AtRestDecryptionKey);
                RegisterReportContext(stmt, context);
                new LineageManager(context.LineageTracker).RecordCreateDatasetLineage(stmt);
                return;
            }
        }

        // ── 4. Materialise source query into temp table ────────────────────────
        await MaterializeSourceQuery(stmt, context);
        var rowCount = context.Telemetry.LastStatementRowsProcessed;

        // ── 5. Portal persistence (Parquet write → registry → refresh job) ──────
        if (registry != null)
        {
            TagGovernanceRuntimePolicy.EnforceDatasetPublish(
                stmt.TempTableName,
                stmt.AccessLevel,
                TagGovernanceRuntimePolicy.CollectDatasetTags(stmt, context),
                stmt.Line,
                stmt.Column);

            await PersistToPortal(stmt, registry, folderPath, rowCount, context);
        }

        // ── 6. Register AST in report context (for ManifestBuilder) ───────────
        RegisterReportContext(stmt, context);
        await context.EnsureCatalogMetadataImportedAsync(stmt.SourceQuery.GetSourceTables());
        new LineageManager(context.LineageTracker).RecordCreateDatasetLineage(stmt);

        context.Log(
            $"Dataset '{stmt.TempTableName}' {(stmt.Mode == ObjectCreationMode.CreateOrAlter ? "updated" : "created")}.");
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

    private async Task PersistToPortal(
        CreateDatasetStatement stmt, IDatasetRegistry registry,
        string folderPath, long rowCount, IExecutionContext context)
    {
        var callerCtx = (context as Evaluator)?.DatasetCallerContext ?? "";
        var existing = await registry.Lookup(stmt.TempTableName, callerCtx);
        var metadata = new DatasetMetadata
        {
            Id = existing?.Id ?? 0,
            Name = stmt.TempTableName,
            FolderPath = folderPath,
            OwningReportId = (context as Evaluator)?.DatasetOwningReportId,
            CreatedBy = existing?.CreatedBy,
            ParquetFilePath = existing?.ParquetFilePath ?? "",
            SourceQuery = stmt.SourceQuery.ToSql(),
            AccessLevel = stmt.AccessLevel,
            EncryptionMode = stmt.EncryptionMode,
            LastRefresh = DateTime.UtcNow,
            Ttl = stmt.Ttl,
            CachedTtl = ParseDuration(stmt.Ttl),
            RefreshInterval = stmt.RefreshInterval,
            RowCount = rowCount
        };

        var allocatedNewRow = existing == null;
        var id = existing?.Id ?? await registry.RegisterOrUpdate(metadata);
        var parquetPath = registry.BuildDatasetFilePath(id, stmt.TempTableName);
        using var fileTransaction = DatasetFileTransaction.Create(parquetPath);

        try
        {
            await WriteToParquet(
                stmt.TempTableName,
                fileTransaction.StagingPath,
                stmt,
                context);
            fileTransaction.Commit();

            metadata.Id = id;
            metadata.ParquetFilePath = parquetPath;
            await registry.RegisterOrUpdate(metadata);
            fileTransaction.Complete();
        }
        catch
        {
            if (allocatedNewRow)
            {
                try
                {
                    await registry.Delete(stmt.TempTableName);
                }
                catch
                {
                    // Preserve the write failure. Startup reconciliation removes an interrupted
                    // allocation that still has no valid managed cache.
                }
            }
            throw;
        }

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
            BuildParquetOptions(stmt, includeCompression: true, (context as Evaluator)?.DatasetAtRestKey));

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
        CreateDatasetStatement stmt, IExecutionContext context,
        string? resolvedAtRestKey = null)
    {
        var connAlias = $"__ds_load_{Guid.NewGuid():N}__";

        var connStmt = new CreateConnectionStatement(
            connAlias, "PARQUET",
            new LiteralExpression(parquetPath, TokenType.STRING_LITERAL),
            BuildParquetOptions(
                stmt,
                includeCompression: false,
                resolvedAtRestKey ?? (context as Evaluator)?.DatasetAtRestKey));

        var selectStmt = new SelectStatement(
            new List<SelectColumn> { new(new IdentifierExpression("*"), null, null) },
            new TableReference(tempTableName),
            new TableReference("FILE", null, null, connAlias),
            new List<JoinClause>(),
            null);

        await context.EvaluateStatement(connStmt);
        await context.EvaluateStatement(selectStmt);
    }

    private static void RegisterReportContext(CreateDatasetStatement stmt, IExecutionContext context)
    {
        if (context is IReportContext rc)
            rc.DatasetDefinitions[stmt.TempTableName] = stmt;
    }

    // ── Static utilities ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the encryption-related options for a synthetic PARQUET CreateConnectionStatement.
    /// In a portal (an at-rest key is configured) the cache ALWAYS uses the portal at-rest key
    /// (ENCRYPT=PASSWORD), regardless of the statement's ENCRYPT clause — that clause is a transport
    /// credential, ignored at rest (use EXPORT/PUBLISH DATASET to move data). In non-portal mode the
    /// statement's mode is honored: MACHINE → host-bound; PASSWORD/KEYFILE → that credential; None → plain.
    /// </summary>
    private static Dictionary<string, Expression> BuildParquetOptions(
        CreateDatasetStatement stmt, bool includeCompression, string? atRestKey)
    {
        var opts = new Dictionary<string, Expression>();

        if (includeCompression)
            opts["COMPRESSION"] = new LiteralExpression("SNAPPY", TokenType.STRING_LITERAL);

        // Portal at rest: always the portal key, ignoring the statement's transport ENCRYPT clause.
        if (!string.IsNullOrWhiteSpace(atRestKey))
        {
            DatasetAtRestOptions.Apply(opts, atRestKey);
            return opts;
        }

        switch (stmt.EncryptionMode)
        {
            case DatasetEncryptionMode.MachineBound:
                DatasetAtRestOptions.Apply(opts, null);   // host MACHINE (no portal key)
                break;
            case DatasetEncryptionMode.Password:
                opts["ENCRYPT"] = new LiteralExpression("PASSWORD", TokenType.STRING_LITERAL);
                opts["PASSWORD"] = new LiteralExpression(stmt.EncryptionPassword ?? "", TokenType.STRING_LITERAL);
                break;
            case DatasetEncryptionMode.KeyFile:
                opts["ENCRYPT"] = new LiteralExpression("KEYFILE", TokenType.STRING_LITERAL);
                opts["KEYFILE"] = new LiteralExpression(stmt.KeyFile ?? "", TokenType.STRING_LITERAL);
                break;
                // DatasetEncryptionMode.None → no ENCRYPT option → Parquet written unencrypted
        }

        return opts;
    }

    private static string MakeSafeAlias(string name) =>
        Regex.Replace(name.TrimStart('&', '#'), @"[^\w]", "_").ToLowerInvariant();

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
            _ => null
        };
    }
}
