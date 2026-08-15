using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Multitenancy;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.App
{
    /// <summary>
    /// Operator Tooling Phase 2: packages portal/orchestrator database state, content, and
    /// configuration into a portable backup, and restores/validates it.
    ///
    /// Split custody (P1.5): the backup is produced as <b>two</b> archives so a single leaked artifact
    /// can neither read nor decrypt the data. The <i>data</i> archive holds the databases (whose SMTP/
    /// API secrets are Data-Protection-encrypted) and the encrypted dataset caches, plus a config copy
    /// with all secret values stripped out. The <i>keys</i> archive holds the Data Protection key ring
    /// and the stripped secrets (dataset at-rest key, JWT secret, etc.). Neither archive alone can
    /// decrypt the other's protected material.
    /// </summary>
    internal static class BackupRestoreService
    {
        private const string DataManifestName = "backup-manifest.json";
        private const string KeysManifestName = "keys-manifest.json";
        private const string SecretsName = "secrets.json";
        private const string DpKeyRingDirName = ".portal-keys";
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        // ── Backup ────────────────────────────────────────────────────────────────

        internal static async Task<int> BackupAsync(CliContext ctx, ILogger logger)
        {
            var baseDirectory = AppContext.BaseDirectory;
            string? appSettingsPath = null;
            string? tenantId = null;
            IConfiguration config;
            if (!string.IsNullOrWhiteSpace(ctx.BackupTenantRoot))
            {
                var dedicated = await ResolveDedicatedBoundaryAsync(ctx.BackupTenantRoot);
                baseDirectory = dedicated.ConfigDirectory;
                appSettingsPath = dedicated.ConfigPath;
                tenantId = dedicated.TenantId;
                config = new ConfigurationBuilder().AddJsonFile(appSettingsPath).Build();
            }
            else
            {
                config = Program.ServiceProvider.GetService<IConfiguration>()
                    ?? new ConfigurationBuilder().Build();
            }
            var outputDir = string.IsNullOrWhiteSpace(ctx.BackupOutputDir)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(ctx.BackupOutputDir.Trim('"', '\'', ' '));

            int exitCode;
            try
            {
                exitCode = await BackupCoreAsync(
                    config, baseDirectory, outputDir, logger, appSettingsPath, tenantId);
            }
            catch
            {
                await RecordBackupOutcomeAsync(exitCode: 1, logger);
                throw;
            }

            await RecordBackupOutcomeAsync(exitCode, logger);
            return exitCode;
        }

        /// <summary>
        /// Records the backup outcome under job-state name 'admin-backup' so the Portal's native
        /// backup-report admin service can alert on failed, missing, or stale backups without the
        /// two-step scheduler wiring the samples used. Best-effort: recording never changes the
        /// backup's exit code.
        /// </summary>
        private static async Task RecordBackupOutcomeAsync(int exitCode, ILogger logger)
        {
            try
            {
                var store = Program.ServiceProvider?.GetService<ETL_SQL.Core.Data.IJobHistoryStore>();
                if (store == null) return;
                await store.InitializeAsync();
                await store.SetHostStateAsync("backup", "last_backup_status", exitCode == 0 ? "success" : "failed");
                await store.SetHostStateAsync("backup", "last_backup_at", DateTime.UtcNow.ToString("o"));
                await store.SetHostStateAsync("backup", "last_backup_exit_code", exitCode.ToString());
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Note: the backup outcome could not be recorded for the backup-report service: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        /// <summary>
        /// Records a restore or validation outcome under job-state name 'admin-restore', mirroring
        /// <see cref="RecordBackupOutcomeAsync"/>.
        ///
        /// A backup nobody has ever restored is a hope, not a recovery plan — so the Portal has to be
        /// able to show when the archive was last proven readable, not only when one was last
        /// written. Custody and the restore itself stay out here on the host; only the evidence that
        /// a drill happened travels. Best-effort: recording never changes the command's exit code.
        /// </summary>
        private static async Task RecordRestoreOutcomeAsync(
            int exitCode, bool validateOnly, int problemCount, ILogger logger)
        {
            try
            {
                var store = Program.ServiceProvider?.GetService<ETL_SQL.Core.Data.IJobHistoryStore>();
                if (store == null) return;
                await store.InitializeAsync();
                await store.SetHostStateAsync("restore", "last_restore_mode", validateOnly ? "validate" : "restore");
                await store.SetHostStateAsync("restore", "last_restore_status", exitCode == 0 ? "success" : "failed");
                await store.SetHostStateAsync("restore", "last_restore_at", DateTime.UtcNow.ToString("o"));
                await store.SetHostStateAsync("restore", "last_restore_exit_code", exitCode.ToString());
                await store.SetHostStateAsync("restore", "last_restore_problems", problemCount.ToString());
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Note: the restore outcome could not be recorded: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        /// <summary>Testable backup core: explicit config, install/base directory, and output directory.</summary>
        internal static async Task<int> BackupCoreAsync(
            IConfiguration config,
            string baseDir,
            string outputDir,
            ILogger logger,
            string? appSettingsPath = null,
            string? tenantId = null)
        {
            await CreateDirectoryAsync(outputDir);

            var backupId = Guid.NewGuid().ToString("N");
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var archiveId = $"{stamp}-{backupId}";
            var dataZip = Path.Combine(outputDir, $"etl-sql-backup-{archiveId}.zip");
            var keysZip = Path.Combine(outputDir, $"etl-sql-keys-{archiveId}.zip");

            // Stage under a per-user secure root with owner-only (0700) permissions so the staged key
            // ring and plaintext secrets are never readable by other local users during the backup.
            var staging = Path.Combine(await SecureTempRootAsync(), $"etl-sql-backup-{backupId}");
            var dataStage = Path.Combine(staging, "data");
            var keysStage = Path.Combine(staging, "keys");
            await CreateDirectoryAsync(staging);
            RestrictToOwner(staging, isDirectory: true);
            await CreateDirectoryAsync(dataStage);
            await CreateDirectoryAsync(keysStage);

            try
            {
                var portalDb = Resolve(config["Portal:DatabasePath"] ?? "./portal.db", baseDir);
                var orchDb = Resolve(
                    config["Portal:Orchestrator:DatabasePath"]
                    ?? config["Orchestrator:DatabasePath"]
                    ?? config["Orchestrator:HistoryDbPath"]
                    ?? "./etlsql.db", baseDir);

                if (!string.IsNullOrWhiteSpace(tenantId))
                    await ValidateDedicatedDatabaseTenantAsync(portalDb, orchDb, tenantId);

                var files = new List<BackupFile>();

                // Databases (copy the .db plus -wal/-shm sidecars so a cold copy is consistent).
                files.AddRange(await CopySqliteSetAsync(portalDb, Path.Combine(dataStage, "db"), "portal.db", dataStage));
                files.AddRange(await CopySqliteSetAsync(orchDb, Path.Combine(dataStage, "db"), "etlsql.db", dataStage));

                // Content directories.
                files.AddRange(await CopyTreeAsync(Resolve(config["Portal:SnapshotDirectory"] ?? "./Snapshots", baseDir), Path.Combine(dataStage, "content", "snapshots"), dataStage));
                files.AddRange(await CopyTreeAsync(Resolve(config["Portal:ScriptRootPath"] ?? "./Reports", baseDir), Path.Combine(dataStage, "content", "reports"), dataStage));
                files.AddRange(await CopyTreeAsync(Resolve(config["Portal:DatasetRootPath"] ?? "./data/datasets", baseDir), Path.Combine(dataStage, "content", "datasets"), dataStage));
                files.AddRange(await CopyTreeAsync(Resolve(config["Portal:MapRootPath"] ?? "./data/maps", baseDir), Path.Combine(dataStage, "content", "maps"), dataStage));

                // Config, with secrets split out into the keys archive.
                var (strippedConfig, secrets) = await SplitConfigSecretsAsync(
                    appSettingsPath ?? Path.Combine(baseDir, "appsettings.json"));
                await File.WriteAllTextAsync(Path.Combine(dataStage, "appsettings.json"), strippedConfig);
                files.Add(await DescribeAsync(Path.Combine(dataStage, "appsettings.json"), dataStage));

                // ── Keys archive: Data Protection key ring + the stripped secrets ──────
                var dpRing = Resolve(
                    config["Portal:Storage:KeyRingPath"]
                    ?? Path.Combine(Path.GetDirectoryName(portalDb) ?? baseDir, DpKeyRingDirName),
                    baseDir);
                int dpKeyFiles = (await CopyTreeAsync(dpRing, Path.Combine(keysStage, DpKeyRingDirName), keysStage)).Count;
                var secretsPath = Path.Combine(keysStage, SecretsName);
                await File.WriteAllTextAsync(secretsPath,
                    new JsonObject(secrets.Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value))).ToJsonString(JsonOpts));
                RestrictToOwner(secretsPath, isDirectory: false);

                var atRestVersion = config["Portal:Dataset:AtRestKeyVersion"];
                var keysManifest = new JsonObject
                {
                    ["backupId"] = backupId,
                    ["tenantId"] = tenantId,
                    ["createdUtc"] = DateTime.UtcNow.ToString("o"),
                    ["atRestKeyVersion"] = atRestVersion,
                    ["secretCount"] = secrets.Count,
                    ["dataProtectionKeyFiles"] = dpKeyFiles,
                };
                await File.WriteAllTextAsync(Path.Combine(keysStage, KeysManifestName), keysManifest.ToJsonString(JsonOpts));

                // ── Data manifest ─────────────────────────────────────────────────────
                var manifest = new JsonObject
                {
                    ["backupId"] = backupId,
                    ["tenantId"] = tenantId,
                    ["createdUtc"] = DateTime.UtcNow.ToString("o"),
                    ["appVersion"] = AppVersion(),
                    ["atRestKeyVersion"] = atRestVersion,
                    ["catalogMigration"] = ReadCatalogMigration(portalDb),
                    ["keysArchive"] = Path.GetFileName(keysZip),
                    ["files"] = new JsonArray(files.Select(f => (JsonNode)new JsonObject
                    {
                        ["path"] = f.RelativePath,
                        ["sha256"] = f.Sha256,
                        ["bytes"] = f.Bytes,
                    }).ToArray()),
                };
                await File.WriteAllTextAsync(Path.Combine(dataStage, DataManifestName), manifest.ToJsonString(JsonOpts));

                await DeleteFileIfExistsAsync(dataZip);
                await DeleteFileIfExistsAsync(keysZip);
                await CreateZipFromDirectoryAsync(dataStage, dataZip);
                await CreateZipFromDirectoryAsync(keysStage, keysZip);
                // The output archives carry sensitive material (the data archive holds the databases;
                // the keys archive holds plaintext secrets) — restrict both to the owner.
                RestrictToOwner(dataZip, isDirectory: false);
                RestrictToOwner(keysZip, isDirectory: false);

                logger.WriteLine($"Backup complete (backup id {backupId}).", ConsoleColor.Green);
                logger.WriteLine($"  Data archive: {dataZip}", ConsoleColor.Gray);
                logger.WriteLine($"  Keys archive: {keysZip}", ConsoleColor.Gray);
                logger.WriteLine("Store the two archives in SEPARATE custody — the data archive cannot be", ConsoleColor.Yellow);
                logger.WriteLine("decrypted without the keys archive. Keep both to restore.", ConsoleColor.Yellow);
                return 0;
            }
            catch (Exception ex)
            {
                logger.Error("Backup failed.", ex);
                return 1;
            }
            finally
            {
                TryDeleteDir(staging);
            }
        }

        private sealed record DedicatedBoundary(
            string TenantId,
            string ConfigPath,
            string ConfigDirectory);

        private static async Task<DedicatedBoundary> ResolveDedicatedBoundaryAsync(string tenantRoot)
        {
            var root = Path.GetFullPath(tenantRoot.Trim('"', '\'', ' '))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                == root)
            {
                throw new ArgumentException("A filesystem root cannot be used as a tenant boundary.");
            }

            var manifestPath = Path.Combine(root, "tenant-manifest.json");
            var configPath = Path.Combine(root, "config", "appsettings.tenant.json");
            if (!File.Exists(manifestPath) || !File.Exists(configPath))
                throw new InvalidDataException(
                    "--tenant-root must contain tenant-manifest.json and config/appsettings.tenant.json.");

            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))?.AsObject()
                ?? throw new InvalidDataException("The tenant boundary manifest is unreadable.");
            var config = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject()
                ?? throw new InvalidDataException("The tenant boundary configuration is unreadable.");
            var manifestTenant = TenantId.FromTrustedSource((string?)manifest["tenantId"]).Value;
            var configuredTenant = TenantId.FromTrustedSource(
                (string?)config["saasTenant"]?["tenantId"]
                ?? (string?)config["SaasTenant"]?["TenantId"]).Value;
            if (!string.Equals(manifestTenant, configuredTenant, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Tenant boundary manifest and host-fixed configuration identify different tenants.");

            return new DedicatedBoundary(manifestTenant, configPath, Path.GetDirectoryName(configPath)!);
        }

        private static async Task ValidateDedicatedDatabaseTenantAsync(
            string portalDb,
            string orchestratorDb,
            string expectedTenant)
        {
            await RejectForeignTenantRowsAsync(orchestratorDb, "Jobs", expectedTenant);
            await RejectForeignTenantRowsAsync(portalDb, "SharedTenantResources", expectedTenant);
        }

        private static async Task RejectForeignTenantRowsAsync(
            string databasePath,
            string table,
            string expectedTenant)
        {
            if (!File.Exists(databasePath))
                return;

            await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
            await connection.OpenAsync();
            await using var tableExists = connection.CreateCommand();
            tableExists.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table;";
            tableExists.Parameters.AddWithValue("@table", table);
            if (Convert.ToInt64(await tableExists.ExecuteScalarAsync()) == 0)
                return;

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT DISTINCT TenantId FROM [{table}] WHERE TenantId IS NOT NULL AND TRIM(TenantId) <> '';";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var actual = TenantId.FromTrustedSource(reader.GetString(0)).Value;
                if (!string.Equals(actual, expectedTenant, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Dedicated backup refused: {table} contains state for tenant '{actual}', not host tenant '{expectedTenant}'.");
            }
        }

        // ── Restore / validate ──────────────────────────────────────────────────────

        internal static async Task<int> RestoreAsync(CliContext ctx, ILogger logger)
        {
            var (exitCode, problemCount) = await RestoreCoreAsync(ctx, logger);
            await RecordRestoreOutcomeAsync(exitCode, ctx.RestoreValidateOnly, problemCount, logger);
            return exitCode;
        }

        /// <summary>Restore/validate proper. Returns the exit code and how many validation problems
        /// were found, so <see cref="RestoreAsync"/> can record drill evidence around it.</summary>
        private static async Task<(int ExitCode, int Problems)> RestoreCoreAsync(CliContext ctx, ILogger logger)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(ctx.RestoreFrom) || string.IsNullOrWhiteSpace(ctx.RestoreKeys))
            {
                logger.WriteLine("Both --from <data.zip> and --keys <keys.zip> are required.", ConsoleColor.Red);
                return (1, 0);
            }

            var dataZip = Path.GetFullPath(ctx.RestoreFrom.Trim('"', '\'', ' '));
            var keysZip = Path.GetFullPath(ctx.RestoreKeys.Trim('"', '\'', ' '));
            if (!File.Exists(dataZip)) { logger.WriteLine($"Data archive not found: {dataZip}", ConsoleColor.Red); return (1, 0); }
            if (!File.Exists(keysZip)) { logger.WriteLine($"Keys archive not found: {keysZip}", ConsoleColor.Red); return (1, 0); }

            // Extract under a per-user secure root with owner-only (0700) permissions; the keys archive
            // expands to plaintext secrets + the key ring, which must not be readable by other users.
            var work = Path.Combine(await SecureTempRootAsync(), $"etl-sql-restore-{Guid.NewGuid():N}");
            var dataExtract = Path.Combine(work, "data");
            var keysExtract = Path.Combine(work, "keys");

            try
            {
                await CreateDirectoryAsync(work);
                RestrictToOwner(work, isDirectory: true);
                await ExtractZipToDirectoryAsync(dataZip, dataExtract);
                await ExtractZipToDirectoryAsync(keysZip, keysExtract);

                problems = await ValidateAsync(
                    dataExtract, keysExtract, logger, ctx.RestoreExpectedTenant);
                await WriteRecoveryReportIfRequestedAsync(
                    ctx,
                    dataExtract,
                    keysExtract,
                    problems,
                    targetDirectory: ctx.RestoreValidateOnly ? null : ctx.RestoreTo,
                    restored: false,
                    logger);
                if (problems.Count > 0)
                {
                    logger.WriteLine($"Validation FAILED ({problems.Count} problem(s)):", ConsoleColor.Red);
                    foreach (var p in problems) logger.WriteLine($"  - {p}", ConsoleColor.Yellow);
                    return (1, problems.Count);
                }

                logger.WriteLine("Validation passed: archive integrity, key versions, and app-version compatibility OK.", ConsoleColor.Green);

                if (ctx.RestoreValidateOnly)
                {
                    logger.WriteLine("--validate specified: no files were written.", ConsoleColor.Gray);
                    return (0, 0);
                }

                if (string.IsNullOrWhiteSpace(ctx.RestoreTo))
                {
                    logger.WriteLine("--to <dir> is required to perform a restore (omit it only with --validate).", ConsoleColor.Red);
                    return (1, 0);
                }

                var target = Path.GetFullPath(ctx.RestoreTo.Trim('"', '\'', ' '));
                if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                {
                    logger.WriteLine(
                        "Restore target must be empty; recovery never merges an archive with existing state.",
                        ConsoleColor.Red);
                    return (1, 0);
                }
                await CreateDirectoryAsync(target);

                // Materialize the restored layout: databases at the root, content under their dirs,
                // the DP key ring beside the database, and secrets merged back into appsettings.json.
                // The yielded manifest entries are not needed on restore (archiveRoot is irrelevant).
                await CopyTreeAsync(Path.Combine(dataExtract, "db"), target, target);
                await CopyTreeAsync(Path.Combine(dataExtract, "content", "snapshots"), Path.Combine(target, "Snapshots"), target);
                await CopyTreeAsync(Path.Combine(dataExtract, "content", "reports"), Path.Combine(target, "Reports"), target);
                await CopyTreeAsync(Path.Combine(dataExtract, "content", "datasets"), Path.Combine(target, "data", "datasets"), target);
                await CopyTreeAsync(Path.Combine(dataExtract, "content", "maps"), Path.Combine(target, "data", "maps"), target);
                await CopyTreeAsync(Path.Combine(keysExtract, DpKeyRingDirName), Path.Combine(target, DpKeyRingDirName), target);

                var restoredConfig = Path.Combine(target, "appsettings.json");
                await MergeConfigAsync(
                    Path.Combine(dataExtract, "appsettings.json"),
                    Path.Combine(keysExtract, SecretsName),
                    restoredConfig);
                // The merged config has the plaintext secrets re-injected — restrict it to the owner.
                RestrictToOwner(restoredConfig, isDirectory: false);
                // Likewise the restored Data Protection key ring.
                RestrictToOwner(Path.Combine(target, DpKeyRingDirName), isDirectory: true);
                if (!string.IsNullOrWhiteSpace(ctx.RestoreExpectedTenant))
                    await FenceRestoredTenantWorkAsync(target);
                await WriteRecoveryReportIfRequestedAsync(
                    ctx,
                    dataExtract,
                    keysExtract,
                    problems,
                    target,
                    restored: true,
                    logger);

                logger.WriteLine($"Restore complete into: {target}", ConsoleColor.Green);
                logger.WriteLine("Next steps:", ConsoleColor.Cyan);
                logger.WriteLine("  1. Point the portal/orchestrator at this directory (or copy it into place).", ConsoleColor.Gray);
                logger.WriteLine("  2. Start the portal — pending migrations apply automatically on startup.", ConsoleColor.Gray);
                logger.WriteLine("  3. Dataset caches referenced by ABSOLUTE path in the catalog must be restored to", ConsoleColor.Gray);
                logger.WriteLine("     their original DatasetRootPath, or re-materialized (see admin guide §6.5).", ConsoleColor.Gray);
                return (0, 0);
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Restore failed: {ex.Message}", ConsoleColor.Red);
                return (1, problems.Count);
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task WriteRecoveryReportIfRequestedAsync(
            CliContext ctx,
            string dataExtract,
            string keysExtract,
            IReadOnlyList<string> problems,
            string? targetDirectory,
            bool restored,
            ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(ctx.RestoreReport))
                return;

            var reportPath = Path.GetFullPath(ctx.RestoreReport.Trim('"', '\'', ' '));
            var report = await BuildRecoveryReportAsync(dataExtract, keysExtract, problems, targetDirectory, restored);
            var reportDir = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(reportDir))
                await CreateDirectoryAsync(reportDir);
            await File.WriteAllTextAsync(reportPath, report.ToJsonString(JsonOpts));
            RestrictToOwner(reportPath, isDirectory: false);
            logger.WriteLine($"Recovery report: {reportPath}", ConsoleColor.Gray);
        }

        internal static async Task<JsonObject> BuildRecoveryReportAsync(
            string dataExtract,
            string keysExtract,
            IReadOnlyList<string> problems,
            string? targetDirectory,
            bool restored)
        {
            var manifest = await ReadJsonObjectOrNullAsync(Path.Combine(dataExtract, DataManifestName));
            var keysManifest = await ReadJsonObjectOrNullAsync(Path.Combine(keysExtract, KeysManifestName));
            var createdUtcText = (string?)manifest?["createdUtc"];
            DateTimeOffset? createdUtc = DateTimeOffset.TryParse(createdUtcText, out var parsedCreated)
                ? parsedCreated
                : null;
            var generatedUtc = DateTimeOffset.UtcNow;
            long? achievedRpoSeconds = createdUtc is null
                ? null
                : Math.Max(0, (long)(generatedUtc - createdUtc.Value).TotalSeconds);

            var fileCount = manifest?["files"] is JsonArray files ? files.Count : 0;
            var fileBytes = manifest?["files"] is JsonArray fileArray
                ? fileArray.OfType<JsonObject>().Sum(file => (long?)file["bytes"] ?? 0L)
                : 0L;

            var actions = new JsonArray();
            actions.Add("Start Portal and Orchestrator with the restored configuration only after reviewing environment-specific secrets and endpoints.");
            actions.Add("Verify /healthz, /health, /metrics, service-account login, audit/security delivery, and scheduled job recovery.");
            actions.Add("Re-enroll restored machines and rotate client credentials if this is a clone, replacement host, or cross-environment restore.");
            actions.Add("Verify dataset cache paths or re-materialize datasets when absolute cache paths changed.");

            return new JsonObject
            {
                ["schemaVersion"] = "1.0",
                ["generatedUtc"] = generatedUtc.ToString("o"),
                ["operation"] = restored ? "restore" : "validate",
                ["status"] = problems.Count == 0 ? "Pass" : "Fail",
                ["backupId"] = (string?)manifest?["backupId"],
                ["keysBackupId"] = (string?)keysManifest?["backupId"],
                ["tenantId"] = (string?)manifest?["tenantId"],
                ["backupCreatedUtc"] = createdUtcText,
                ["appVersion"] = (string?)manifest?["appVersion"],
                ["catalogMigration"] = (string?)manifest?["catalogMigration"],
                ["atRestKeyVersion"] = (string?)manifest?["atRestKeyVersion"],
                ["targetDirectory"] = targetDirectory,
                ["restored"] = restored,
                ["achievedRpoSeconds"] = achievedRpoSeconds,
                ["achievedRtoSeconds"] = null,
                ["dataLossWindowSeconds"] = achievedRpoSeconds,
                ["fileCount"] = fileCount,
                ["fileBytes"] = fileBytes,
                ["missingDependencies"] = new JsonArray(problems.Select(p => (JsonNode)p).ToArray()),
                ["operatorActions"] = actions
            };
        }

        private static async Task<JsonObject?> ReadJsonObjectOrNullAsync(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                return JsonNode.Parse(await File.ReadAllTextAsync(path))?.AsObject();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Returns a list of validation problems; empty means the backup is restorable.</summary>
        private static async Task<List<string>> ValidateAsync(
            string dataExtract,
            string keysExtract,
            ILogger logger,
            string? expectedTenant = null)
        {
            var problems = new List<string>();

            var manifestPath = Path.Combine(dataExtract, DataManifestName);
            var keysManifestPath = Path.Combine(keysExtract, KeysManifestName);
            if (!File.Exists(manifestPath)) { problems.Add($"{DataManifestName} missing from data archive."); return problems; }
            if (!File.Exists(keysManifestPath)) { problems.Add($"{KeysManifestName} missing from keys archive."); return problems; }

            JsonObject manifest, keysManifest;
            try { manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject(); }
            catch (Exception ex) { problems.Add($"data manifest unreadable: {ex.Message}"); return problems; }
            try { keysManifest = JsonNode.Parse(await File.ReadAllTextAsync(keysManifestPath))!.AsObject(); }
            catch (Exception ex) { problems.Add($"keys manifest unreadable: {ex.Message}"); return problems; }

            // Paired archives must share a backup id.
            var dataId = (string?)manifest["backupId"];
            var keysId = (string?)keysManifest["backupId"];
            if (string.IsNullOrEmpty(dataId) || dataId != keysId)
                problems.Add($"backup id mismatch: data='{dataId}' keys='{keysId}' (archives are not a matching pair).");

            var dataTenant = (string?)manifest["tenantId"];
            var keysTenant = (string?)keysManifest["tenantId"];
            if (!string.Equals(dataTenant, keysTenant, StringComparison.Ordinal))
                problems.Add(
                    $"tenant mismatch: data='{dataTenant}' keys='{keysTenant}' (archives are not a matching tenant pair).");
            if (!string.IsNullOrWhiteSpace(dataTenant))
            {
                if (string.IsNullOrWhiteSpace(expectedTenant))
                {
                    problems.Add(
                        "Managed Dedicated archive requires --expected-tenant from the recovery environment.");
                }
                else
                {
                    try
                    {
                        var archiveTenant = TenantId.FromTrustedSource(dataTenant).Value;
                        var targetTenant = TenantId.FromTrustedSource(expectedTenant).Value;
                        if (!string.Equals(archiveTenant, targetTenant, StringComparison.Ordinal))
                            problems.Add(
                                $"tenant authority mismatch: archive='{archiveTenant}' expected='{targetTenant}'.");
                    }
                    catch (ArgumentException ex)
                    {
                        problems.Add($"invalid tenant identity: {ex.Message}");
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(expectedTenant))
            {
                problems.Add(
                    "An unscoped backup cannot be restored as a Managed Dedicated tenant archive.");
            }

            // App-version compatibility: never restore a backup made by a newer release.
            var backupVersion = (string?)manifest["appVersion"];
            if (Version.TryParse(backupVersion, out var bv) && Version.TryParse(AppVersion(), out var cur) && bv > cur)
                problems.Add($"backup app version {bv} is newer than this binary {cur}; upgrade before restoring.");

            // Key-version coverage: the data's at-rest key version must be present in the keys archive.
            var dataAtRest = (string?)manifest["atRestKeyVersion"];
            var keysAtRest = (string?)keysManifest["atRestKeyVersion"];
            if (!string.IsNullOrEmpty(dataAtRest) && dataAtRest != keysAtRest)
                problems.Add($"at-rest key version mismatch: data expects '{dataAtRest}', keys archive has '{keysAtRest}'.");

            // File integrity: every listed file is present with a matching checksum.
            if (manifest["files"] is JsonArray fileArr)
            {
                var rootResolved = Path.GetFullPath(dataExtract);
                foreach (var node in fileArr.OfType<JsonObject>())
                {
                    var rel = (string?)node["path"];
                    var expected = (string?)node["sha256"];
                    if (string.IsNullOrEmpty(rel)) continue;
                    var full = Path.GetFullPath(Path.Combine(dataExtract, rel));
                    // Reject a tampered manifest whose path escapes the extracted archive root, so the
                    // integrity check can never be coerced into reading a file outside the backup.
                    if (!full.StartsWith(rootResolved + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    {
                        problems.Add($"unsafe manifest path (escapes archive root): {rel}");
                        continue;
                    }
                    if (!File.Exists(full)) { problems.Add($"missing file: {rel}"); continue; }
                    var actual = await Sha256Async(full);
                    if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        problems.Add($"checksum mismatch: {rel}");
                }
            }

            logger.WriteLine(
                $"Backup id {dataId}; created {(string?)manifest["createdUtc"]}; app version {backupVersion}; " +
                $"catalog migration {(string?)manifest["catalogMigration"] ?? "(unknown)"}.", ConsoleColor.Gray);
            return problems;
        }

        private static async Task FenceRestoredTenantWorkAsync(string target)
        {
            var databasePath = Path.Combine(target, "etlsql.db");
            if (!File.Exists(databasePath))
                return;

            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            if (await SqliteTableExistsAsync(connection, transaction, "Jobs"))
            {
                await using var jobs = connection.CreateCommand();
                jobs.Transaction = (SqliteTransaction)transaction;
                jobs.CommandText = """
                    UPDATE Jobs
                       SET IsEnabled = 0,
                           LeaseOwner = NULL,
                           LeaseExpiresAt = NULL,
                           LeaseFenceToken = LeaseFenceToken + 1;
                    """;
                await jobs.ExecuteNonQueryAsync();
            }

            if (await SqliteTableExistsAsync(connection, transaction, "SandboxAdmissions"))
            {
                await using var admissions = connection.CreateCommand();
                admissions.Transaction = (SqliteTransaction)transaction;
                admissions.CommandText = """
                    UPDATE SandboxAdmissions
                       SET State = CASE WHEN State = 'Active' THEN 'Retained' ELSE 'Cancelled' END,
                           LeaseOwner = NULL,
                           LeaseExpiresUtc = NULL,
                           FenceToken = FenceToken + 1,
                           UpdatedUtc = @now,
                           ReconciliationReason = 'Restored archive requires environment reconciliation'
                     WHERE State IN ('Queued', 'Active');
                    """;
                admissions.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
                await admissions.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
            SqliteConnection.ClearAllPools();
        }

        private static async Task<bool> SqliteTableExistsAsync(
            SqliteConnection connection,
            System.Data.Common.DbTransaction transaction,
            string table)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table;";
            command.Parameters.AddWithValue("@table", table);
            return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
        }

        // ── Config secret split / merge ──────────────────────────────────────────────

        /// <summary>
        /// Splits secret values out of an appsettings.json: returns the config text with secrets blanked,
        /// and a path→value map (dotted JSON path) of the removed secrets for the keys archive.
        /// </summary>
        internal static (string StrippedConfig, Dictionary<string, JsonNode?> Secrets) SplitConfigSecrets(string appSettingsPath)
            => SplitConfigSecretsAsync(appSettingsPath).GetAwaiter().GetResult();

        internal static async Task<(string StrippedConfig, Dictionary<string, JsonNode?> Secrets)> SplitConfigSecretsAsync(string appSettingsPath)
        {
            var secrets = new Dictionary<string, JsonNode?>();
            if (!File.Exists(appSettingsPath))
                return ("{}", secrets);

            var root = JsonNode.Parse(await File.ReadAllTextAsync(appSettingsPath));
            if (root != null) Strip(root, "", secrets);
            return (root?.ToJsonString(JsonOpts) ?? "{}", secrets);

            static void Strip(JsonNode node, string path, Dictionary<string, JsonNode?> sink)
            {
                if (node is not JsonObject obj) return;
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[key];
                    var childPath = path.Length == 0 ? key : $"{path}.{key}";
                    if (SupportBundleBuilder.IsSecretKey(key))
                    {
                        // Capture the original (string/array/object) and blank a string in place.
                        if (child is JsonValue v && v.TryGetValue<string>(out var s))
                        {
                            if (string.IsNullOrEmpty(s)) continue; // nothing to move
                            sink[childPath] = s;
                            obj[key] = "";
                        }
                        else if (child is JsonArray || child is JsonObject)
                        {
                            sink[childPath] = child!.DeepClone();
                            obj[key] = child is JsonArray ? new JsonArray() : new JsonObject();
                        }
                    }
                    else if (child is JsonObject)
                    {
                        Strip(child, childPath, sink);
                    }
                }
            }
        }

        private static async Task MergeConfigAsync(string strippedConfigPath, string secretsPath, string outPath)
        {
            var root = File.Exists(strippedConfigPath)
                ? JsonNode.Parse(await File.ReadAllTextAsync(strippedConfigPath))
                : new JsonObject();
            root ??= new JsonObject();

            if (File.Exists(secretsPath))
            {
                var secrets = JsonNode.Parse(await File.ReadAllTextAsync(secretsPath))?.AsObject();
                if (secrets != null)
                    foreach (var kv in secrets)
                        SetByPath(root.AsObject(), kv.Key, kv.Value?.DeepClone());
            }

            await File.WriteAllTextAsync(outPath, root.ToJsonString(JsonOpts));

            static void SetByPath(JsonObject root, string dottedPath, JsonNode? value)
            {
                var parts = dottedPath.Split('.');
                var cur = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (cur[parts[i]] is not JsonObject next)
                    {
                        next = new JsonObject();
                        cur[parts[i]] = next;
                    }
                    cur = next;
                }
                cur[parts[^1]] = value;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private readonly record struct BackupFile(string RelativePath, string Sha256, long Bytes);

        private static string Resolve(string p, string baseDir) =>
            Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p));

        private static string AppVersion() =>
            typeof(BackupRestoreService).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        /// <summary>Copies a SQLite db plus its -wal/-shm sidecars into <paramref name="destDir"/>.</summary>
        private static IEnumerable<BackupFile> CopySqliteSet(string dbPath, string destDir, string destName, string archiveRoot)
            => CopySqliteSetAsync(dbPath, destDir, destName, archiveRoot).GetAwaiter().GetResult();

        private static async Task<List<BackupFile>> CopySqliteSetAsync(string dbPath, string destDir, string destName, string archiveRoot)
        {
            var files = new List<BackupFile>();
            foreach (var (suffix, name) in new[] { ("", destName), ("-wal", destName + "-wal"), ("-shm", destName + "-shm") })
            {
                var src = dbPath + suffix;
                if (!File.Exists(src)) continue;
                await CreateDirectoryAsync(destDir);
                var dest = Path.Combine(destDir, name);
                await CopyFileAsync(src, dest);
                files.Add(await DescribeAsync(dest, archiveRoot));
            }

            return files;
        }

        /// <summary>Recursively copies a directory tree; yields a manifest entry per file. No-op if absent.</summary>
        private static IEnumerable<BackupFile> CopyTree(string sourceDir, string destDir, string archiveRoot)
            => CopyTreeAsync(sourceDir, destDir, archiveRoot).GetAwaiter().GetResult();

        private static async Task<List<BackupFile>> CopyTreeAsync(string sourceDir, string destDir, string archiveRoot)
        {
            var files = new List<BackupFile>();
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir)) return files;
            var root = Path.GetFullPath(sourceDir);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file);
                var dest = Path.Combine(destDir, rel);
                await CreateDirectoryAsync(Path.GetDirectoryName(dest)!);
                await CopyFileAsync(file, dest);
                files.Add(await DescribeAsync(dest, archiveRoot));
            }

            return files;
        }

        /// <summary>A manifest entry whose path is recorded relative to the archive root, slash-normalized.</summary>
        private static BackupFile Describe(string fullPath, string archiveRoot)
        {
            var fi = new FileInfo(fullPath);
            return new BackupFile(RelativeForArchive(fullPath, archiveRoot), Sha256(fullPath), fi.Length);
        }

        private static async Task<BackupFile> DescribeAsync(string fullPath, string archiveRoot)
        {
            var fi = new FileInfo(fullPath);
            return new BackupFile(RelativeForArchive(fullPath, archiveRoot), await Sha256Async(fullPath), fi.Length);
        }

        private static string RelativeForArchive(string fullPath, string archiveRoot) =>
            Path.GetRelativePath(archiveRoot, fullPath).Replace('\\', '/');

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        private static async Task<string> Sha256Async(string path)
        {
            using var sha = SHA256.Create();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Convert.ToHexString(await sha.ComputeHashAsync(stream));
        }

        private static Task CreateDirectoryAsync(string path) =>
            Task.Run(() => Directory.CreateDirectory(path));

        private static async Task CopyFileAsync(string source, string destination)
        {
            await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destinationStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await sourceStream.CopyToAsync(destinationStream);
        }

        private static Task DeleteFileIfExistsAsync(string path) =>
            Task.Run(() =>
            {
                if (File.Exists(path)) File.Delete(path);
            });

        private static Task CreateZipFromDirectoryAsync(string sourceDirectory, string destinationArchive) =>
            Task.Run(() => ZipFile.CreateFromDirectory(sourceDirectory, destinationArchive, CompressionLevel.Optimal, includeBaseDirectory: false));

        private static Task ExtractZipToDirectoryAsync(string sourceArchive, string destinationDirectory) =>
            Task.Run(() => ZipFile.ExtractToDirectory(sourceArchive, destinationDirectory));

        /// <summary>Reads the last applied EF migration id from a portal SQLite db (read-only).</summary>
        private static string? ReadCatalogMigration(string portalDbPath)
        {
            if (!File.Exists(portalDbPath)) return null;
            try
            {
                using var conn = new SqliteConnection(
                    new SqliteConnectionStringBuilder { DataSource = portalDbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1";
                return cmd.ExecuteScalar() as string;
            }
            catch
            {
                return null; // no history table / unreadable — recorded as unknown
            }
        }

        /// <summary>
        /// A per-user staging root (under LocalApplicationData), restricted to the owner, preferred over
        /// the shared system temp directory for staging credential-bearing backup artifacts.
        /// </summary>
        private static string SecureTempRoot()
            => SecureTempRootAsync().GetAwaiter().GetResult();

        private static async Task<string> SecureTempRootAsync()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = string.IsNullOrWhiteSpace(local)
                ? Path.Combine(Path.GetTempPath(), "ETL-SQL", "tmp")
                : Path.Combine(local, "ETL-SQL", "tmp");
            await CreateDirectoryAsync(root);
            RestrictToOwner(root, isDirectory: true);
            return root;
        }

        /// <summary>
        /// Restricts a file/directory to owner-only access (0600/0700) on Unix. No-op on Windows, where
        /// LocalApplicationData and the chosen output paths already inherit user-scoped ACLs.
        /// </summary>
        private static void RestrictToOwner(string path, bool isDirectory)
        {
            if (OperatingSystem.IsWindows()) return;
            try
            {
                var mode = isDirectory
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite;
                File.SetUnixFileMode(path, mode);
            }
            catch { /* best-effort hardening; never fail the operation over a chmod */ }
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
