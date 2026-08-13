using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.App;

/// <summary>
/// Two-pass Managed Dedicated upgrade controller. The first executing pass fences schedules and
/// drains durable admissions. Cutover is allowed only after no active or retained runtime remains.
/// </summary>
internal static class SaasTenantUpgradeService
{
    internal const string ReceiptSchema = "etl-sql.saas-tenant-upgrade/v1";
    internal static string CurrentReleaseId =>
        Environment.GetEnvironmentVariable("ETLSQL_MANAGED_RELEASE_ID")?.Trim()
        ?? typeof(SaasTenantUpgradeService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(SaasTenantUpgradeService).Assembly.GetName().Version?.ToString()
        ?? throw new InvalidOperationException("The running ETL-SQL release identity is unavailable.");

    internal sealed record UpgradeAuthority(
        TenantContext TenantContext,
        string TargetRelease,
        int MaxConcurrentJobs,
        int MaxStorageMb,
        int MaxReportSessions);

    internal sealed record UpgradeReceipt(
        string SchemaVersion,
        string OperationId,
        string TenantId,
        string Status,
        string PlatformOperator,
        string AuthorizationReference,
        string Reason,
        DateTimeOffset AuthorizationExpiresUtc,
        string PreviousRelease,
        string TargetRelease,
        int PreviousMaxConcurrentJobs,
        int PreviousMaxStorageMb,
        int PreviousMaxReportSessions,
        int TargetMaxConcurrentJobs,
        int TargetMaxStorageMb,
        int TargetMaxReportSessions,
        string CapacityPoolId,
        IReadOnlyList<string> FencedJobs,
        int CancelledQueuedAdmissions,
        IReadOnlyList<string> BlockingAdmissions,
        DateTimeOffset StartedUtc,
        DateTimeOffset? CompletedUtc,
        string? Failure,
        bool TenantUserImpersonation);

    internal static async Task<int> RunAsync(
        CliContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var authority = ResolveAuthorizedContext(context, EnterprisePolicyRuntime.Current, now);
            var receipt = await UpgradeAsync(
                context, authority, now, context.SaasUpgradeExecute, cancellationToken);
            if (!context.SaasUpgradeExecute)
            {
                logger.WriteLine(
                    $"Upgrade preflight passed for {receipt.TenantId}: {receipt.PreviousRelease} -> " +
                    $"{receipt.TargetRelease}; add --execute to fence and cut over.",
                    ConsoleColor.Yellow);
                return 0;
            }

            if (receipt.Status == "Draining")
            {
                logger.WriteLine(
                    $"Tenant {receipt.TenantId} is fenced and draining; " +
                    $"{receipt.BlockingAdmissions.Count} admission(s) still require completion or reconciliation.",
                    ConsoleColor.Yellow);
                return 2;
            }

            logger.WriteLine(
                $"Tenant {receipt.TenantId} upgraded to {receipt.TargetRelease}; capacity assignment applied.",
                ConsoleColor.Green);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or InvalidDataException or InvalidOperationException
                                   or ArgumentException or JsonException)
        {
            logger.WriteLine($"SaaS tenant upgrade failed: {ex.Message}", ConsoleColor.Red);
            return 1;
        }
    }

    internal static UpgradeAuthority ResolveAuthorizedContext(
        CliContext context,
        EffectiveEnterprisePolicy policy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.IsAvailable || policy.Document?.SaasUpgrade.Enabled != true)
            throw new UnauthorizedAccessException(
                "SaaS upgrade requires an active signed organization-policy authorization.");

        var authorization = policy.Document.SaasUpgrade;
        var grant = PlatformAccessGrant.Issue(
            authorization.TenantId!, authorization.OperatorPrincipal!,
            authorization.AuthorizationReference!, authorization.Reason!,
            authorization.ExpiresUtc!.Value, now);
        var tenantContext = TenantContext.FromPlatformGrant(grant, now);
        tenantContext.RequireTenant(context.SaasTenantId);
        RequireAssertion(context.SaasUpgradeTargetRelease, authorization.TargetRelease, "target release");
        RequireAssertion(context.SaasUpgradeMaxConcurrentJobs, authorization.MaxConcurrentJobs, "maximum concurrent jobs");
        RequireAssertion(context.SaasUpgradeMaxStorageMb, authorization.MaxStorageMb, "maximum storage MiB");
        RequireAssertion(context.SaasUpgradeMaxReportSessions, authorization.MaxReportSessions, "maximum report sessions");
        if (!string.Equals(authorization.TargetRelease, CurrentReleaseId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                "The signed target release does not match the running upgrade binary or host-fixed managed release identity.");
        return new UpgradeAuthority(
            tenantContext, authorization.TargetRelease!, authorization.MaxConcurrentJobs!.Value,
            authorization.MaxStorageMb!.Value, authorization.MaxReportSessions!.Value);
    }

    internal static async Task<UpgradeReceipt> UpgradeAsync(
        CliContext context,
        UpgradeAuthority authority,
        DateTimeOffset now,
        bool execute = true,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, Task>? cutoverFault = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authority);
        authority.TenantContext.RequireActivePlatformGrant(now);
        if (string.IsNullOrWhiteSpace(context.SaasUpgradeTenantRoot))
            throw new ArgumentException("--tenant-root is required.");

        var tenant = authority.TenantContext.RequireTenant(context.SaasTenantId).Value;
        var root = Path.GetFullPath(context.SaasUpgradeTenantRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Tenant boundary not found: {root}");
        await using var operationLock = AcquireOperationLock(root);
        var manifestPath = SaasTenantOnboardingService.ResolveTenantPath(root, "tenant-manifest.json");
        var configPath = SaasTenantOnboardingService.ResolveTenantPath(root, "config/appsettings.tenant.json");
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
        var config = await ReadConfigAsync(configPath, cancellationToken);
        ValidateBoundaryIdentity(manifest, config, tenant);

        var resources = RequireObject(RequireObject(config, "saasTenant"), "resources");
        var previousJobs = RequirePositiveInt(resources, "maxConcurrentJobs");
        var previousStorage = RequirePositiveInt(resources, "maxStorageMb");
        var previousReports = RequirePositiveInt(resources, "maxReportSessions");
        var poolId = (string?)resources["sandboxPoolId"] ?? DedicatedPoolId(tenant);
        if (!string.Equals(poolId, DedicatedPoolId(tenant), StringComparison.Ordinal))
            throw new InvalidDataException("The tenant capacity pool is not the canonical Dedicated pool.");
        var deployment = GetOrCreateObject(RequireObject(config, "saasTenant"), "deployment");
        var previousRelease = (string?)deployment["activeRelease"] ?? "unversioned";
        var grant = authority.TenantContext.Grant!;
        var operationId = OperationId(grant.AuthorizationReference);
        var receiptPath = SaasTenantOnboardingService.ResolveTenantPath(
            root, $"queues/audit/tenant-upgrade-{operationId}.json");
        var rollbackRoot = SaasTenantOnboardingService.ResolveTenantPath(
            root, $"imports/upgrades/{operationId}");

        var existing = await TryReadReceiptAsync(receiptPath, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status is not ("Draining" or "Cutover" or "Failed" or "Completed"))
                throw new InvalidDataException($"The existing upgrade receipt has invalid status '{existing.Status}'.");
            if (!string.Equals(existing.TenantId, tenant, StringComparison.Ordinal)
                || !string.Equals(existing.PlatformOperator, grant.OperatorPrincipal, StringComparison.Ordinal)
                || !string.Equals(existing.AuthorizationReference, grant.AuthorizationReference, StringComparison.Ordinal)
                || !string.Equals(existing.Reason, grant.Reason, StringComparison.Ordinal)
                || existing.AuthorizationExpiresUtc != grant.ExpiresUtc
                || !string.Equals(existing.TargetRelease, authority.TargetRelease, StringComparison.Ordinal)
                || existing.TargetMaxConcurrentJobs != authority.MaxConcurrentJobs
                || existing.TargetMaxStorageMb != authority.MaxStorageMb
                || existing.TargetMaxReportSessions != authority.MaxReportSessions)
                throw new InvalidDataException("The authorization reference was already used for a different upgrade.");
            if (existing.Status == "Completed")
            {
                if (!string.Equals(previousRelease, authority.TargetRelease, StringComparison.Ordinal)
                    || previousJobs != authority.MaxConcurrentJobs
                    || previousStorage != authority.MaxStorageMb
                    || previousReports != authority.MaxReportSessions)
                    throw new InvalidDataException(
                        "The completed upgrade receipt no longer matches the tenant's active assignment.");
                return existing;
            }
        }

        var history = new SQLiteJobHistoryStore(
            SaasTenantOnboardingService.ResolveTenantPath(root, "databases/orchestrator.db"));
        await history.InitializeAsync();
        if (existing?.Status == "Cutover")
        {
            var manifestBackup = Path.Combine(rollbackRoot, "tenant-manifest.before.json");
            var configBackup = Path.Combine(rollbackRoot, "appsettings.tenant.before.json");
            if (!File.Exists(manifestBackup) || !File.Exists(configBackup))
                throw new InvalidDataException("An interrupted cutover has no complete rollback snapshot.");
            await WriteAtomicAsync(manifestPath, await File.ReadAllBytesAsync(manifestBackup, cancellationToken), cancellationToken);
            await WriteAtomicAsync(configPath, await File.ReadAllBytesAsync(configBackup, cancellationToken), cancellationToken);
            foreach (var name in existing.FencedJobs)
            {
                var job = await history.GetJobAsync(name);
                if (job is not null && !job.IsEnabled)
                    await history.SaveJobAsync(job with { IsEnabled = true, ModifiedBy = grant.OperatorPrincipal });
            }
            existing = existing with
            {
                Status = "Draining",
                CompletedUtc = null,
                Failure = "InterruptedCutoverRecovered"
            };
            await WriteReceiptAsync(receiptPath, existing, cancellationToken);
            manifest = await ReadManifestAsync(manifestPath, cancellationToken);
            config = await ReadConfigAsync(configPath, cancellationToken);
            ValidateBoundaryIdentity(manifest, config, tenant);
            resources = RequireObject(RequireObject(config, "saasTenant"), "resources");
            previousJobs = RequirePositiveInt(resources, "maxConcurrentJobs");
            previousStorage = RequirePositiveInt(resources, "maxStorageMb");
            previousReports = RequirePositiveInt(resources, "maxReportSessions");
            deployment = GetOrCreateObject(RequireObject(config, "saasTenant"), "deployment");
            previousRelease = (string?)deployment["activeRelease"] ?? "unversioned";
        }
        if (existing is not null
            && (!string.Equals(existing.PreviousRelease, previousRelease, StringComparison.Ordinal)
                || existing.PreviousMaxConcurrentJobs != previousJobs
                || existing.PreviousMaxStorageMb != previousStorage
                || existing.PreviousMaxReportSessions != previousReports
                || !string.Equals(existing.CapacityPoolId, poolId, StringComparison.Ordinal)))
            throw new InvalidDataException(
                "The tenant release or capacity assignment changed outside the authorized draining operation.");
        var jobs = (await history.GetAllJobsAsync()).ToArray();
        var fencedJobs = existing?.FencedJobs?.ToArray()
                         ?? jobs.Where(job => job.IsEnabled).Select(job => job.Name)
                             .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var ledger = new RelationalSandboxAdmissionLedger(new SqliteOrchestratorDialect(
            $"Data Source={SaasTenantOnboardingService.ResolveTenantPath(root, "databases/orchestrator.db")}"));
        var openBefore = await ledger.ListOpenAsync(poolId, cancellationToken);
        EnsureAdmissionsBelongToTenant(openBefore, tenant);

        var receipt = new UpgradeReceipt(
            ReceiptSchema, operationId, tenant, execute ? "Draining" : "Preflight",
            grant.OperatorPrincipal, grant.AuthorizationReference, grant.Reason, grant.ExpiresUtc,
            previousRelease, authority.TargetRelease,
            previousJobs, previousStorage, previousReports,
            authority.MaxConcurrentJobs, authority.MaxStorageMb, authority.MaxReportSessions,
            poolId, fencedJobs, existing?.CancelledQueuedAdmissions ?? 0,
            openBefore.Where(row => row.State is SandboxAdmissionState.Active or SandboxAdmissionState.Retained)
                .Select(row => row.AdmissionId).ToArray(),
            existing?.StartedUtc ?? now, null, null, TenantUserImpersonation: false);
        if (!execute)
            return receipt;

        authority.TenantContext.RequireActivePlatformGrant(DateTimeOffset.UtcNow);
        // Persist the complete re-enable set before the first scheduling mutation. A process death
        // after any subset is fenced can then resume without silently leaving formerly enabled jobs off.
        await WriteReceiptAsync(receiptPath, receipt, cancellationToken);
        foreach (var job in jobs.Where(job => job.IsEnabled))
            await history.SaveJobAsync(job with { IsEnabled = false, ModifiedBy = grant.OperatorPrincipal });

        var cancelled = existing?.CancelledQueuedAdmissions ?? 0;
        foreach (var queued in openBefore.Where(row => row.State == SandboxAdmissionState.Queued))
        {
            if (!await ledger.TryCancelQueuedAsync(queued.AdmissionId, cancellationToken)) continue;
            cancelled++;
            await WriteReceiptAsync(receiptPath, receipt with
            {
                CancelledQueuedAdmissions = cancelled
            }, cancellationToken);
        }
        var openAfter = await ledger.ListOpenAsync(poolId, cancellationToken);
        EnsureAdmissionsBelongToTenant(openAfter, tenant);
        var blockers = openAfter
            .Where(row => row.State is SandboxAdmissionState.Active or SandboxAdmissionState.Retained)
            .Select(row => row.AdmissionId).ToArray();
        if (blockers.Length > 0)
        {
            var draining = receipt with
            {
                CancelledQueuedAdmissions = cancelled,
                BlockingAdmissions = blockers
            };
            await WriteReceiptAsync(receiptPath, draining, cancellationToken);
            return draining;
        }

        SaasTenantOnboardingService.EnsureStorageWithinQuota(
            root, checked((long)authority.MaxStorageMb * 1024 * 1024));
        Directory.CreateDirectory(rollbackRoot);
        var originalManifest = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var originalConfig = await File.ReadAllBytesAsync(configPath, cancellationToken);
        await WriteNewIfAbsentAsync(Path.Combine(rollbackRoot, "tenant-manifest.before.json"), originalManifest, cancellationToken);
        await WriteNewIfAbsentAsync(Path.Combine(rollbackRoot, "appsettings.tenant.before.json"), originalConfig, cancellationToken);
        await WriteReceiptAsync(receiptPath, receipt with
        {
            Status = "Cutover",
            CancelledQueuedAdmissions = cancelled,
            BlockingAdmissions = []
        }, cancellationToken);

        try
        {
            resources["maxConcurrentJobs"] = authority.MaxConcurrentJobs;
            resources["maxStorageMb"] = authority.MaxStorageMb;
            resources["maxReportSessions"] = authority.MaxReportSessions;
            resources["sandboxPoolId"] = poolId;
            deployment["activeRelease"] = authority.TargetRelease;
            deployment["previousRelease"] = previousRelease;
            deployment["lastUpgradeOperationId"] = operationId;
            deployment["updatedUtc"] = DateTimeOffset.UtcNow;
            RequireObject(RequireObject(config, "orchestration"), "jobThrottle")["maxConcurrentJobs"] = authority.MaxConcurrentJobs;
            RequireObject(RequireObject(config, "portal"), "resources")["maxConcurrentReportExecutions"] = authority.MaxReportSessions;
            GetOrCreateObject(RequireObject(config, "orchestration"), "sandboxAdmission")["poolCapacities"] =
                new JsonObject { [poolId] = authority.MaxConcurrentJobs };

            var updatedManifest = manifest with
            {
                MaxConcurrentJobs = authority.MaxConcurrentJobs,
                MaxStorageMb = authority.MaxStorageMb,
                MaxReportSessions = authority.MaxReportSessions
            };
            await WriteAtomicAsync(configPath, JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions), cancellationToken);
            if (cutoverFault is not null) await cutoverFault(cancellationToken);
            await WriteAtomicAsync(manifestPath, JsonSerializer.SerializeToUtf8Bytes(updatedManifest, JsonOptions), cancellationToken);

            foreach (var name in fencedJobs)
            {
                var job = await history.GetJobAsync(name);
                if (job is not null && !job.IsEnabled)
                    await history.SaveJobAsync(job with { IsEnabled = true, ModifiedBy = grant.OperatorPrincipal });
            }

            var completed = receipt with
            {
                Status = "Completed",
                CancelledQueuedAdmissions = cancelled,
                BlockingAdmissions = [],
                CompletedUtc = DateTimeOffset.UtcNow
            };
            await WriteReceiptAsync(receiptPath, completed, cancellationToken);
            return completed;
        }
        catch (Exception ex)
        {
            await WriteAtomicAsync(configPath, originalConfig, CancellationToken.None);
            await WriteAtomicAsync(manifestPath, originalManifest, CancellationToken.None);
            foreach (var name in fencedJobs)
            {
                var job = await history.GetJobAsync(name);
                if (job is not null && !job.IsEnabled)
                    await history.SaveJobAsync(job with { IsEnabled = true, ModifiedBy = grant.OperatorPrincipal });
            }
            var failed = receipt with
            {
                Status = "Failed",
                CancelledQueuedAdmissions = cancelled,
                Failure = ex.GetType().Name,
                CompletedUtc = DateTimeOffset.UtcNow
            };
            await WriteReceiptAsync(receiptPath, failed, CancellationToken.None);
            throw;
        }
    }

    private static void RequireAssertion<T>(T? actual, T? authorized, string name)
    {
        if (actual is null || !EqualityComparer<T>.Default.Equals(actual, authorized))
            throw new UnauthorizedAccessException($"The asserted {name} does not match signed upgrade authority.");
    }

    private static string DedicatedPoolId(string tenant) => $"dedicated-{tenant}";

    private static string OperationId(string authorizationReference) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(authorizationReference)))[..24];

    private static async Task<SaasTenantOnboardingService.Manifest> ReadManifestAsync(
        string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("The target is not a provisioned Managed Dedicated boundary.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return await JsonSerializer.DeserializeAsync<SaasTenantOnboardingService.Manifest>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("Tenant manifest is unreadable.");
    }

    private static async Task<JsonObject> ReadConfigAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("The target is not a provisioned Managed Dedicated boundary.");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))?.AsObject()
               ?? throw new InvalidDataException("Tenant configuration is unreadable.");
    }

    private static void ValidateBoundaryIdentity(
        SaasTenantOnboardingService.Manifest manifest, JsonObject config, string tenant)
    {
        if (!string.Equals(manifest.SchemaVersion, SaasTenantOnboardingService.ManifestSchema, StringComparison.Ordinal)
            || !string.Equals(manifest.TenantId, tenant, StringComparison.Ordinal)
            || !string.Equals((string?)RequireObject(config, "saasTenant")["tenantId"], tenant, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                "Signed tenant authority does not match the boundary manifest and host configuration.");
    }

    private static JsonObject RequireObject(JsonObject parent, string name) =>
        parent[name] as JsonObject ?? throw new InvalidDataException($"Tenant configuration is missing '{name}'.");

    private static JsonObject GetOrCreateObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing) return existing;
        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static int RequirePositiveInt(JsonObject parent, string name)
    {
        var value = (int?)parent[name];
        return value > 0 ? value.Value : throw new InvalidDataException($"Tenant configuration has invalid '{name}'.");
    }

    private static void EnsureAdmissionsBelongToTenant(
        IReadOnlyList<SandboxAdmissionLedgerEntry> entries, string tenant)
    {
        if (entries.Any(entry => !string.Equals(entry.TenantId, tenant, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException("The Dedicated capacity pool contains foreign tenant authority.");
    }

    private static async Task<UpgradeReceipt?> TryReadReceiptAsync(
        string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return await JsonSerializer.DeserializeAsync<UpgradeReceipt>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("The existing upgrade receipt is unreadable.");
    }

    private static Task WriteReceiptAsync(
        string path, UpgradeReceipt receipt, CancellationToken cancellationToken) =>
        WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions), cancellationToken);

    private static async Task WriteNewAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(content, cancellationToken);
    }

    private static async Task WriteNewIfAbsentAsync(
        string path, byte[] content, CancellationToken cancellationToken)
    {
        if (File.Exists(path)) return;
        await WriteNewAsync(path, content, cancellationToken);
    }

    private static async Task WriteAtomicAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await WriteNewAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static FileStream AcquireOperationLock(string root)
    {
        var path = SaasTenantOnboardingService.ResolveTenantPath(root, "queues/tenant-upgrade.lock");
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.WriteThrough);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                "Another tenant upgrade operation currently owns the boundary lock.", ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
