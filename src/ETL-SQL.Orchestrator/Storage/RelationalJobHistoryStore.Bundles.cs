using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Orchestrator.Storage;

public partial class RelationalJobHistoryStore
{
    internal const string DefaultLegacyBundleTenant = "default";

    // ── IBundleStore (untenanted compatibility facade -> UnboundTenantSentinel) ──────────

    public Task<BundleVersionInfo> PublishBundleAsync(BundlePublishRequest request) =>
        PublishBundleCoreAsync(UnboundTenantSentinel, null, request);

    public Task<BundleVersionInfo?> GetLatestVersionAsync(string bundleName) =>
        GetLatestVersionCoreAsync(UnboundTenantSentinel, bundleName);

    public Task<BundleVersionInfo?> GetVersionAsync(string bundleName, int version) =>
        GetVersionCoreAsync(UnboundTenantSentinel, bundleName, version);

    public Task<BundleFileInfo?> GetFileAsync(string bundleName, int version, string virtualPath) =>
        GetFileCoreAsync(UnboundTenantSentinel, bundleName, version, virtualPath);

    public Task<IEnumerable<BundleVersionInfo>> GetBundlesAsync() =>
        GetBundlesCoreAsync(UnboundTenantSentinel);

    public Task<IEnumerable<BundleVersionInfo>> GetVersionsAsync(string bundleName) =>
        GetVersionsCoreAsync(UnboundTenantSentinel, bundleName);

    public Task<IEnumerable<BundleFileInfo>> GetFilesAsync(string bundleName, int version) =>
        GetFilesCoreAsync(UnboundTenantSentinel, bundleName, version);

    public Task<IEnumerable<BundleDependencyInfo>> GetDependenciesAsync(string bundleName, int version) =>
        GetDependenciesCoreAsync(UnboundTenantSentinel, bundleName, version);

    // ── ITenantBundleStore (tenant-scoped contract) ─────────────────────────────────────

    public Task<BundleVersionInfo> PublishBundleAsync(TenantContext tenant, BundlePublishRequest request)
    {
        RequireRuntimeTenant(tenant);
        return PublishBundleCoreAsync(tenant.Tenant.Value, tenant, request);
    }

    public Task<BundleVersionInfo?> GetLatestVersionAsync(TenantContext tenant, string bundleName)
    {
        RequireRuntimeTenant(tenant);
        return GetLatestVersionCoreAsync(tenant.Tenant.Value, bundleName);
    }

    public Task<BundleVersionInfo?> GetVersionAsync(TenantContext tenant, string bundleName, int version)
    {
        RequireRuntimeTenant(tenant);
        return GetVersionCoreAsync(tenant.Tenant.Value, bundleName, version);
    }

    public Task<BundleFileInfo?> GetFileAsync(TenantContext tenant, string bundleName, int version, string virtualPath)
    {
        RequireRuntimeTenant(tenant);
        return GetFileCoreAsync(tenant.Tenant.Value, bundleName, version, virtualPath);
    }

    public Task<IEnumerable<BundleVersionInfo>> GetBundlesAsync(TenantContext tenant)
    {
        RequireRuntimeTenant(tenant);
        return GetBundlesCoreAsync(tenant.Tenant.Value);
    }

    public Task<IEnumerable<BundleVersionInfo>> GetVersionsAsync(TenantContext tenant, string bundleName)
    {
        RequireRuntimeTenant(tenant);
        return GetVersionsCoreAsync(tenant.Tenant.Value, bundleName);
    }

    public Task<IEnumerable<BundleFileInfo>> GetFilesAsync(TenantContext tenant, string bundleName, int version)
    {
        RequireRuntimeTenant(tenant);
        return GetFilesCoreAsync(tenant.Tenant.Value, bundleName, version);
    }

    public Task<IEnumerable<BundleDependencyInfo>> GetDependenciesAsync(TenantContext tenant, string bundleName, int version)
    {
        RequireRuntimeTenant(tenant);
        return GetDependenciesCoreAsync(tenant.Tenant.Value, bundleName, version);
    }

    public async Task<IReadOnlyList<BundleVersionInfo>> GetUnassignedDefaultBundlesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT bundle_name, version, entry_path, content_hash, published_at, publisher, description
            FROM bundle_versions
            WHERE tenant_id = @defaultTenant OR tenant_id = @unboundTenant
            ORDER BY bundle_name COLLATE NOCASE, version DESC;";
        cmd.AddParam("@defaultTenant", DefaultLegacyBundleTenant);
        cmd.AddParam("@unboundTenant", UnboundTenantSentinel);
        var list = await ReadBundleVersionsAsync(cmd, cancellationToken);
        return list.ToList();
    }

    public async Task<int> ReassignDefaultBundlesAsync(
        TenantContext targetTenant,
        string? bundleName = null,
        bool includeUnbound = false,
        CancellationToken cancellationToken = default)
    {
        RequireMigrationTargetTenant(targetTenant);
        var targetTenantId = targetTenant.Tenant.Value;

        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            var candidates = new List<(string SourceTenant, string BundleName, int Version)>();
            using (var selectCmd = connection.CreateCommand())
            {
                selectCmd.Transaction = transaction;
                var sql = @"
                    SELECT tenant_id, bundle_name, version
                    FROM bundle_versions
                    WHERE (tenant_id = @defaultTenant" + (includeUnbound ? " OR tenant_id = @unboundTenant" : "") + @")";
                if (!string.IsNullOrWhiteSpace(bundleName))
                {
                    sql += " AND bundle_name = @bundle COLLATE NOCASE";
                    selectCmd.AddParam("@bundle", bundleName.Trim());
                }
                sql += " ORDER BY bundle_name COLLATE NOCASE, version;";
                selectCmd.CommandText = sql;
                selectCmd.AddParam("@defaultTenant", DefaultLegacyBundleTenant);
                if (includeUnbound)
                    selectCmd.AddParam("@unboundTenant", UnboundTenantSentinel);

                using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    candidates.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
            }

            if (candidates.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return 0;
            }

            foreach (var (sourceTenant, candidateBundle, version) in candidates)
            {
                if (string.Equals(sourceTenant, targetTenantId, StringComparison.Ordinal))
                    continue;

                if (await TargetVersionExistsAsync(connection, transaction, targetTenantId, candidateBundle, version, cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"Cannot reassign legacy bundle '{candidateBundle}' version {version} to tenant '{targetTenantId}' because that version already exists in the target tenant.");
                }
            }

            var movedVersions = 0;
            foreach (var group in candidates
                .Where(c => !string.Equals(c.SourceTenant, targetTenantId, StringComparison.Ordinal))
                .GroupBy(c => (c.SourceTenant, c.BundleName)))
            {
                await MoveBundleRowsAsync(
                    connection,
                    transaction,
                    group.Key.SourceTenant,
                    group.Key.BundleName,
                    targetTenantId,
                    cancellationToken);
                movedVersions += group.Count();
            }

            await transaction.CommitAsync(cancellationToken);
            return movedVersions;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<int> MigrateLegacyDefaultBundlesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await MigrateDefaultBundlesByJobOwnershipAsync(connection, cancellationToken);
    }

    internal async Task<int> MigrateDefaultBundlesByJobOwnershipAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        var defaultBundles = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DISTINCT bundle_name
                FROM bundle_versions
                WHERE tenant_id = @defaultTenant;";
            cmd.AddParam("@defaultTenant", DefaultLegacyBundleTenant);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                defaultBundles.Add(reader.GetString(0));
        }

        if (defaultBundles.Count == 0)
            return 0;

        var ownersByBundle = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        using (var jobsCmd = connection.CreateCommand())
        {
            jobsCmd.CommandText = @"
                SELECT tenant_id, target_path, script
                FROM jobs
                WHERE tenant_id IS NOT NULL AND tenant_id <> '' AND tenant_id <> @defaultTenant;";
            jobsCmd.AddParam("@defaultTenant", DefaultLegacyBundleTenant);
            using var reader = await jobsCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var jobTenant = reader.GetString(0);
                var targetPath = reader.IsDBNull(1) ? null : reader.GetString(1);
                var script = reader.IsDBNull(2) ? null : reader.GetString(2);

                foreach (var referencedBundle in ExtractReferencedBundles(targetPath, script))
                {
                    if (!ownersByBundle.TryGetValue(referencedBundle, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        ownersByBundle[referencedBundle] = set;
                    }
                    set.Add(jobTenant);
                }
            }
        }

        var migrated = 0;
        foreach (var bundleName in defaultBundles)
        {
            if (!ownersByBundle.TryGetValue(bundleName, out var owners) || owners.Count != 1)
                continue;

            var ownerTenant = owners.Single();
            if (!TenantId.TryParse(ownerTenant, out _))
                continue;

            using var transaction = connection.BeginTransaction();
            try
            {
                var versions = new List<int>();
                using (var verCmd = connection.CreateCommand())
                {
                    verCmd.Transaction = transaction;
                    verCmd.CommandText = @"
                        SELECT version
                        FROM bundle_versions
                        WHERE tenant_id = @defaultTenant AND bundle_name = @bundle;";
                    verCmd.AddParam("@defaultTenant", DefaultLegacyBundleTenant);
                    verCmd.AddParam("@bundle", bundleName);
                    using var reader = await verCmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                        versions.Add(reader.GetInt32(0));
                }

                var hasCollision = false;
                foreach (var version in versions)
                {
                    if (await TargetVersionExistsAsync(connection, transaction, ownerTenant, bundleName, version, cancellationToken))
                    {
                        hasCollision = true;
                        break;
                    }
                }

                if (hasCollision || versions.Count == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                await MoveBundleRowsAsync(
                    connection,
                    transaction,
                    DefaultLegacyBundleTenant,
                    bundleName,
                    ownerTenant,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                migrated += versions.Count;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return migrated;
    }

    // ── Shared Tenant-Filtered Bundle Operations ────────────────────────────────────────

    private async Task<BundleVersionInfo> PublishBundleCoreAsync(
        string tenantId,
        TenantContext? tenantForLineage,
        BundlePublishRequest request)
    {
        await EnsureInitializedAsync();
        var latest = await GetLatestVersionCoreAsync(tenantId, request.BundleName);
        if (latest != null && string.Equals(latest.ContentHash, request.ContentHash, StringComparison.OrdinalIgnoreCase))
            return latest;

        var nextVersion = (latest?.Version ?? 0) + 1;
        var publishedAt = DateTime.Now;

        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO bundle_versions
                        (tenant_id, bundle_name, version, entry_path, content_hash, published_at, publisher, description, encryption_mode, encryption_metadata)
                    VALUES
                        (@tenant, @bundle, @version, @entry, @hash, @published, @publisher, @description, @mode, @metadata);";
                cmd.AddParam("@tenant", tenantId);
                cmd.AddParam("@bundle", request.BundleName);
                cmd.AddParam("@version", nextVersion);
                cmd.AddParam("@entry", NormalizeVirtualPath(request.EntryPath));
                cmd.AddParam("@hash", request.ContentHash);
                cmd.AddParam("@published", publishedAt.ToString("O"));
                cmd.AddParam("@publisher", (object?)request.Publisher ?? DBNull.Value);
                cmd.AddParam("@description", (object?)request.Description ?? DBNull.Value);
                cmd.AddParam("@mode", request.EncryptionMode);
                cmd.AddParam("@metadata", (object?)request.EncryptionMetadata ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var file in request.Files)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO bundle_files
                        (tenant_id, bundle_name, version, virtual_path, content, content_hash, size_bytes, content_type)
                    VALUES
                        (@tenant, @bundle, @version, @path, @content, @hash, @size, @type);";
                cmd.AddParam("@tenant", tenantId);
                cmd.AddParam("@bundle", request.BundleName);
                cmd.AddParam("@version", nextVersion);
                cmd.AddParam("@path", NormalizeVirtualPath(file.VirtualPath));
                cmd.AddParam("@content", file.Content);
                cmd.AddParam("@hash", file.ContentHash);
                cmd.AddParam("@size", file.SizeBytes);
                cmd.AddParam("@type", file.ContentType);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var dep in request.Dependencies)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO bundle_dependencies
                        (tenant_id, bundle_name, version, from_path, to_path)
                    VALUES
                        (@tenant, @bundle, @version, @from, @to)
                    ON CONFLICT DO NOTHING;";
                cmd.AddParam("@tenant", tenantId);
                cmd.AddParam("@bundle", request.BundleName);
                cmd.AddParam("@version", nextVersion);
                cmd.AddParam("@from", NormalizeVirtualPath(dep.FromPath));
                cmd.AddParam("@to", NormalizeVirtualPath(dep.ToPath));
                await cmd.ExecuteNonQueryAsync();
            }

            using (var gcFiles = connection.CreateCommand())
            {
                gcFiles.Transaction = transaction;
                gcFiles.CommandText = @"
                    DELETE FROM bundle_files
                    WHERE tenant_id = @tenant
                      AND bundle_name = @bundle COLLATE NOCASE
                      AND version NOT IN (
                          SELECT version FROM bundle_versions
                          WHERE tenant_id = @tenant AND bundle_name = @bundle COLLATE NOCASE
                          ORDER BY version DESC
                          LIMIT 5
                      );";
                gcFiles.AddParam("@tenant", tenantId);
                gcFiles.AddParam("@bundle", request.BundleName);
                await gcFiles.ExecuteNonQueryAsync();
            }

            using (var gcDeps = connection.CreateCommand())
            {
                gcDeps.Transaction = transaction;
                gcDeps.CommandText = @"
                    DELETE FROM bundle_dependencies
                    WHERE tenant_id = @tenant
                      AND bundle_name = @bundle COLLATE NOCASE
                      AND version NOT IN (
                          SELECT version FROM bundle_versions
                          WHERE tenant_id = @tenant AND bundle_name = @bundle COLLATE NOCASE
                          ORDER BY version DESC
                          LIMIT 5
                      );";
                gcDeps.AddParam("@tenant", tenantId);
                gcDeps.AddParam("@bundle", request.BundleName);
                await gcDeps.ExecuteNonQueryAsync();
            }

            using (var gcVersions = connection.CreateCommand())
            {
                gcVersions.Transaction = transaction;
                gcVersions.CommandText = @"
                    DELETE FROM bundle_versions
                    WHERE tenant_id = @tenant
                      AND bundle_name = @bundle COLLATE NOCASE
                      AND version NOT IN (
                          SELECT version FROM bundle_versions
                          WHERE tenant_id = @tenant AND bundle_name = @bundle COLLATE NOCASE
                          ORDER BY version DESC
                          LIMIT 5
                      );";
                gcVersions.AddParam("@tenant", tenantId);
                gcVersions.AddParam("@bundle", request.BundleName);
                await gcVersions.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        var versionInfo = new BundleVersionInfo(
            request.BundleName,
            nextVersion,
            NormalizeVirtualPath(request.EntryPath),
            request.ContentHash,
            publishedAt,
            request.Publisher,
            request.Description);
        var lineage = BuildBundleLineage(request, versionInfo);
        if (lineage.Count > 0)
        {
            var jobName = $"bundle:{versionInfo.BundleName}@{versionInfo.Version}";
            var scriptPath = $"orch://{versionInfo.BundleName}@{versionInfo.Version}/{versionInfo.EntryPath}";
            if (tenantForLineage is not null)
                await SaveLineageAsync(tenantForLineage, lineage, jobName, scriptPath, versionInfo.PublishedAt);
            else
                await SaveLineageAsync(lineage, jobName, scriptPath, versionInfo.PublishedAt);
        }

        return versionInfo;
    }

    private async Task<BundleVersionInfo?> GetLatestVersionCoreAsync(string tenantId, string bundleName)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT bundle_name, version, entry_path, content_hash, published_at, publisher, description
            FROM bundle_versions
            WHERE tenant_id = @tenant AND bundle_name = @bundle COLLATE NOCASE
            ORDER BY version DESC
            LIMIT 1;";
        cmd.AddParam("@tenant", tenantId);
        cmd.AddParam("@bundle", bundleName);
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadBundleVersion(reader) : null;
    }

    private async Task<BundleVersionInfo?> GetVersionCoreAsync(string tenantId, string bundleName, int version)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT bundle_name, version, entry_path, content_hash, published_at, publisher, description
            FROM bundle_versions
            WHERE tenant_id = @tenant AND bundle_name = @bundle COLLATE NOCASE AND version = @version
            LIMIT 1;";
        cmd.AddParam("@tenant", tenantId);
        cmd.AddParam("@bundle", bundleName);
        cmd.AddParam("@version", version);
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadBundleVersion(reader) : null;
    }

    private async Task<BundleFileInfo?> GetFileCoreAsync(string tenantId, string bundleName, int version, string virtualPath)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT bundle_name, version, virtual_path, content, content_hash, size_bytes, content_type
            FROM bundle_files
            WHERE tenant_id = @tenant
              AND bundle_name = @bundle COLLATE NOCASE
              AND version = @version
              AND virtual_path = @path COLLATE NOCASE
            LIMIT 1;";
        cmd.AddParam("@tenant", tenantId);
        cmd.AddParam("@bundle", bundleName);
        cmd.AddParam("@version", version);
        cmd.AddParam("@path", NormalizeVirtualPath(virtualPath));
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadBundleFile(reader) : null;
    }

    private async Task<IEnumerable<BundleVersionInfo>> GetBundlesCoreAsync(string tenantId)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT bv.bundle_name, bv.version, bv.entry_path, bv.content_hash, bv.published_at, bv.publisher, bv.description
            FROM bundle_versions bv
            INNER JOIN (
                SELECT tenant_id, bundle_name, MAX(version) AS max_version
                FROM bundle_versions
                WHERE tenant_id = @tenant
                GROUP BY tenant_id, bundle_name
            ) latest ON latest.tenant_id = bv.tenant_id
                    AND latest.bundle_name = bv.bundle_name
                    AND latest.max_version = bv.version
            WHERE bv.tenant_id = @tenant
            ORDER BY bv.bundle_name COLLATE NOCASE;";
        cmd.AddParam("@tenant", tenantId);
        return await ReadBundleVersionsAsync(cmd);
    }

    private async Task<IEnumerable<BundleVersionInfo>> GetVersionsCoreAsync(string tenantId, string bundleName)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT bundle_name, version, entry_path, content_hash, published_at, publisher, description
            FROM bundle_versions
            WHERE tenant_id = @tenant AND bundle_name = @bundle COLLATE NOCASE
            ORDER BY version DESC;";
        cmd.AddParam("@tenant", tenantId);
        cmd.AddParam("@bundle", bundleName);
        return await ReadBundleVersionsAsync(cmd);
    }

    private async Task<IEnumerable<BundleFileInfo>> GetFilesCoreAsync(string tenantId, string bundleName, int version)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT bundle_name, version, virtual_path, content, content_hash, size_bytes, content_type
            FROM bundle_files
            WHERE tenant_id = @tenant AND bundle_name = @bundle COLLATE NOCASE AND version = @version
            ORDER BY virtual_path COLLATE NOCASE;";
        cmd.AddParam("@tenant", tenantId);
        cmd.AddParam("@bundle", bundleName);
        cmd.AddParam("@version", version);
        var files = new List<BundleFileInfo>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) files.Add(ReadBundleFile(reader));
        return files;
    }

    private async Task<IEnumerable<BundleDependencyInfo>> GetDependenciesCoreAsync(string tenantId, string bundleName, int version)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT bundle_name, version, from_path, to_path
            FROM bundle_dependencies
            WHERE tenant_id = @tenant AND bundle_name = @bundle COLLATE NOCASE AND version = @version
            ORDER BY from_path COLLATE NOCASE, to_path COLLATE NOCASE;";
        cmd.AddParam("@tenant", tenantId);
        cmd.AddParam("@bundle", bundleName);
        cmd.AddParam("@version", version);
        var deps = new List<BundleDependencyInfo>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            deps.Add(new BundleDependencyInfo(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        return deps;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    private static async Task<bool> TargetVersionExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string targetTenantId,
        string bundleName,
        int version,
        CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            SELECT 1
            FROM bundle_versions
            WHERE tenant_id = @target AND bundle_name = @bundle COLLATE NOCASE AND version = @version
            LIMIT 1;";
        cmd.AddParam("@target", targetTenantId);
        cmd.AddParam("@bundle", bundleName);
        cmd.AddParam("@version", version);
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is not null and not DBNull;
    }

    private static async Task MoveBundleRowsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sourceTenant,
        string bundleName,
        string targetTenant,
        CancellationToken cancellationToken)
    {
        // Insert the target parent rows before repointing bundle_files / bundle_dependencies so
        // composite foreign keys referencing bundle_versions(tenant_id, bundle_name, version)
        // stay satisfied on both SQLite and PostgreSQL without ON UPDATE CASCADE.
        using (var copyParent = connection.CreateCommand())
        {
            copyParent.Transaction = transaction;
            copyParent.CommandText = @"
                INSERT INTO bundle_versions
                    (tenant_id, bundle_name, version, entry_path, content_hash, published_at, publisher, description, encryption_mode, encryption_metadata)
                SELECT @target, bundle_name, version, entry_path, content_hash, published_at, publisher, description, encryption_mode, encryption_metadata
                FROM bundle_versions
                WHERE tenant_id = @source AND bundle_name = @bundle;";
            copyParent.AddParam("@target", targetTenant);
            copyParent.AddParam("@source", sourceTenant);
            copyParent.AddParam("@bundle", bundleName);
            await copyParent.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var moveFiles = connection.CreateCommand())
        {
            moveFiles.Transaction = transaction;
            moveFiles.CommandText = @"
                UPDATE bundle_files
                SET tenant_id = @target
                WHERE tenant_id = @source AND bundle_name = @bundle;";
            moveFiles.AddParam("@target", targetTenant);
            moveFiles.AddParam("@source", sourceTenant);
            moveFiles.AddParam("@bundle", bundleName);
            await moveFiles.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var moveDeps = connection.CreateCommand())
        {
            moveDeps.Transaction = transaction;
            moveDeps.CommandText = @"
                UPDATE bundle_dependencies
                SET tenant_id = @target
                WHERE tenant_id = @source AND bundle_name = @bundle;";
            moveDeps.AddParam("@target", targetTenant);
            moveDeps.AddParam("@source", sourceTenant);
            moveDeps.AddParam("@bundle", bundleName);
            await moveDeps.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var deleteOldParent = connection.CreateCommand())
        {
            deleteOldParent.Transaction = transaction;
            deleteOldParent.CommandText = @"
                DELETE FROM bundle_versions
                WHERE tenant_id = @source AND bundle_name = @bundle;";
            deleteOldParent.AddParam("@source", sourceTenant);
            deleteOldParent.AddParam("@bundle", bundleName);
            await deleteOldParent.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IEnumerable<string> ExtractReferencedBundles(string? targetPath, string? script)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(targetPath)
            && BundleUri.TryParse(targetPath, out var targetUri)
            && targetUri is not null)
        {
            seen.Add(targetUri.BundleName);
        }

        if (string.IsNullOrWhiteSpace(script)
            || !script.Contains("orch://", StringComparison.OrdinalIgnoreCase))
        {
            return seen;
        }

        try
        {
            foreach (var token in new Lexer(script).Tokenize())
            {
                if (token.Type == TokenType.STRING_LITERAL
                    && BundleUri.TryParse(token.Value, out var uri)
                    && uri is not null)
                {
                    seen.Add(uri.BundleName);
                }
            }
        }
        catch
        {
            // Ignore malformed legacy script text; leave ambiguous bundles for operator reassignment.
        }

        return seen;
    }

    private static void RequireMigrationTargetTenant(TenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Origin == TenantContextOrigin.PlatformAuthorization)
        {
            tenant.RequireActivePlatformGrant(DateTimeOffset.UtcNow);
            return;
        }

        if (tenant.Origin is not (TenantContextOrigin.HostFixed or TenantContextOrigin.VerifiedCredential))
        {
            throw new UnauthorizedAccessException(
                "Bundle reassignment requires host-fixed, verified-credential, or active platform tenant authority.");
        }
    }
}
