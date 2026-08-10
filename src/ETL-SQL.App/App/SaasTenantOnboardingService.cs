using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.App;

/// <summary>
/// Deployment-plane onboarding for one tenant-per-runtime SaaS boundaries. Tenant authority is
/// fixed by host configuration, so application callers cannot select another tenant by id.
/// </summary>
internal static partial class SaasTenantOnboardingService
{
    internal const string ManifestSchema = "etl-sql.saas-tenant-boundary/v1";
    internal sealed record BoundaryPath(string Concern, string RelativePath);
    internal sealed record ArtifactProof(string Path, long SizeBytes, string Sha256);
    internal sealed record Manifest(
        string SchemaVersion,
        string TenantId,
        string SourceProfile,
        DateTimeOffset CreatedUtc,
        bool Activated,
        bool SupportAccessEnabled,
        string SecretNamespace,
        string TelemetryNamespace,
        int MaxConcurrentJobs,
        int MaxStorageMb,
        int MaxReportSessions,
        IReadOnlyList<BoundaryPath> Boundaries,
        IReadOnlyList<ArtifactProof> PortableArtifacts,
        int ImportedJobs,
        int ImportedQualityRuns,
        int ImportedLineageEntries);

    internal static async Task<int> RunAsync(CliContext ctx, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var manifest = await OnboardAsync(ctx, ct);
            var tenantRoot = Path.Combine(Path.GetFullPath(ctx.SaasOutputRoot!), manifest.TenantId);
            logger.WriteLine($"SaaS tenant boundary staged: {tenantRoot}", ConsoleColor.Green);
            logger.WriteLine(
                $"Tenant {manifest.TenantId}: {manifest.PortableArtifacts.Count} artifact(s), " +
                $"{manifest.ImportedJobs} disabled job(s), {manifest.ImportedQualityRuns} quality run(s), " +
                $"{manifest.ImportedLineageEntries} lineage/tag row(s). Activation remains false.",
                ConsoleColor.Cyan);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or InvalidOperationException or ArgumentException)
        {
            logger.WriteLine($"SaaS tenant onboarding failed: {ex.Message}", ConsoleColor.Red);
            return 1;
        }
    }

    internal static async Task<Manifest> OnboardAsync(CliContext ctx, CancellationToken ct = default)
    {
        // One definition of a valid tenant id, shared with the multitenancy contract in Core.
        if (!ETL_SQL.Core.Multitenancy.TenantId.TryParse(ctx.SaasTenantId, out var tenantId))
            throw new ArgumentException("--tenant must contain 3-63 lowercase letters, digits, or hyphens and start/end with a letter or digit.");
        var tenant = tenantId.Value;
        var sourceProfile = ctx.SaasSourceProfile?.Trim();
        if (sourceProfile is not ("Solo" or "Enterprise"))
            throw new ArgumentException("--source-profile must be Solo or Enterprise.");
        if (string.IsNullOrWhiteSpace(ctx.PromotionSource) || string.IsNullOrWhiteSpace(ctx.SaasOutputRoot))
            throw new ArgumentException("--source and --output-root are required.");
        if (ctx.SaasMaxConcurrentJobs < 1 || ctx.SaasMaxStorageMb < 128 || ctx.SaasMaxReportSessions < 1)
            throw new ArgumentException("Resource limits must be positive; storage must be at least 128 MiB.");

        var source = Path.GetFullPath(ctx.PromotionSource);
        var outputRoot = Path.GetFullPath(ctx.SaasOutputRoot);
        if (Path.GetPathRoot(outputRoot)?.TrimEnd(Path.DirectorySeparatorChar) == outputRoot.TrimEnd(Path.DirectorySeparatorChar))
            throw new ArgumentException("A filesystem root cannot be used as the SaaS output root.");
        Directory.CreateDirectory(outputRoot);
        var finalRoot = ResolveTenantPath(outputRoot, tenant);
        if (Directory.Exists(finalRoot) || File.Exists(finalRoot))
            throw new IOException($"Tenant boundary '{tenant}' already exists; onboarding never overwrites it.");

        var preflight = await DeploymentPromotionPreflightService.BuildAsync(source, sourceProfile, "SaaS", ct);
        if (!preflight.Ready)
            throw new InvalidOperationException("Source preflight contains blocking findings; resolve them before tenant onboarding.");

        var staging = ResolveTenantPath(outputRoot, $".onboarding-{tenant}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var paths = CreateBoundaryDirectories(staging);
            var artifacts = new List<ArtifactProof>();
            foreach (var artifact in preflight.PortableArtifacts)
            {
                ct.ThrowIfCancellationRequested();
                var sourcePath = Path.GetFullPath(Path.Combine(source, artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!sourcePath.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Portable artifact escapes source root: {artifact.Path}");
                var destination = ResolveTenantPath(staging,
                    Path.Combine("artifacts", "scripts", artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(sourcePath, destination, overwrite: false);
                var info = new FileInfo(destination);
                artifacts.Add(new(artifact.Path, info.Length, await HashAsync(destination, ct)));
            }

            var importedJobs = 0;
            var importedRuns = 0;
            var importedLineage = 0;
            if (!string.IsNullOrWhiteSpace(ctx.PromotionPackage))
            {
                await using var packageStream = new FileStream(Path.GetFullPath(ctx.PromotionPackage), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                var package = await OrchestratorPromotionPackageService.ReadAsync(packageStream, ct);
                var store = new SQLiteJobHistoryStore(ResolveTenantPath(staging, "databases/orchestrator.db"));
                await store.InitializeAsync();
                var result = await OrchestratorPromotionPackageService.ImportAsync(package, store, store, store,
                    DeploymentPromotionPackageAdminService.ParseBindings(ctx.PromotionBindings), ct);
                importedJobs = result.Jobs;
                importedRuns = result.QualityRuns;
                importedLineage = result.LineageEntries;
                File.Copy(Path.GetFullPath(ctx.PromotionPackage), ResolveTenantPath(staging, "imports/orchestrator-promotion.json"));
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }
            if (!string.IsNullOrWhiteSpace(ctx.SaasPortalBootstrap))
            {
                var bootstrapPath = Path.GetFullPath(ctx.SaasPortalBootstrap);
                var bootstrap = await File.ReadAllTextAsync(bootstrapPath, ct);
                if (RawCredentialRegex().IsMatch(bootstrap))
                    throw new InvalidOperationException("Portal bootstrap contains raw credential material; use export placeholders or SECRET references.");
                File.Copy(bootstrapPath, ResolveTenantPath(staging, "imports/portal-bootstrap.etlsql"));
            }

            await WriteTenantConfigurationAsync(staging, tenant, ctx, ct);
            var manifest = new Manifest(
                ManifestSchema, tenant, sourceProfile, DateTimeOffset.UtcNow, Activated: false,
                SupportAccessEnabled: false, $"tenant/{tenant}", $"etlsql.tenant.{tenant}",
                ctx.SaasMaxConcurrentJobs, ctx.SaasMaxStorageMb, ctx.SaasMaxReportSessions,
                paths, artifacts.OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
                importedJobs, importedRuns, importedLineage);
            await using (var manifestStream = new FileStream(Path.Combine(staging, "tenant-manifest.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, ct);
            EnsureStorageWithinQuota(staging, checked((long)ctx.SaasMaxStorageMb * 1024 * 1024));
            Directory.Move(staging, finalRoot);
            return manifest;
        }
        catch
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static IReadOnlyList<BoundaryPath> CreateBoundaryDirectories(string root)
    {
        var paths = new[]
        {
            new BoundaryPath("Databases", "databases"), new("Artifacts", "artifacts"),
            new("ReportScripts", "artifacts/scripts"), new("ReportDatasets", "artifacts/datasets"),
            new("ReportSnapshots", "artifacts/snapshots"), new("Keys", "keys"),
            new("PortalKeyRing", "keys/portal"), new("DatasetKeys", "keys/datasets"),
            new("Caches", "cache"), new("Queues", "queues"), new("AuditOutbox", "queues/audit"),
            new("SecurityOutbox", "queues/security"), new("Logs", "logs"),
            new("Telemetry", "telemetry"), new("Support", "support"), new("Imports", "imports"),
            new("Configuration", "config")
        };
        foreach (var path in paths) Directory.CreateDirectory(ResolveTenantPath(root, path.RelativePath));
        return paths;
    }

    private static async Task WriteTenantConfigurationAsync(string root, string tenant, CliContext ctx, CancellationToken ct)
    {
        var config = new
        {
            SaasTenant = new
            {
                TenantId = tenant,
                AuthorityMode = "HostFixed",
                SecretNamespace = $"tenant/{tenant}",
                TelemetryNamespace = $"etlsql.tenant.{tenant}",
                SupportAccess = new { Enabled = false, RequiresTenantApproval = true },
                Resources = new
                {
                    MaxConcurrentJobs = ctx.SaasMaxConcurrentJobs,
                    MaxStorageMb = ctx.SaasMaxStorageMb,
                    MaxReportSessions = ctx.SaasMaxReportSessions
                }
            },
            Orchestration = new
            {
                JobThrottle = new { MaxConcurrentJobs = ctx.SaasMaxConcurrentJobs }
            },
            Orchestrator = new
            {
                DatabasePath = "../databases/orchestrator.db",
                Database = new { Provider = "Sqlite", ConnectionString = "" },
                ScriptRoot = "../artifacts/scripts"
            },
            Portal = new
            {
                DatabasePath = "../databases/portal.db",
                Database = new { Provider = "Sqlite", ConnectionString = "" },
                ScriptRootPath = "../artifacts/scripts",
                DatasetRootPath = "../artifacts/datasets",
                SnapshotDirectory = "../artifacts/snapshots",
                Storage = new { Provider = "Local", KeyRingPath = "../keys/portal" },
                Resources = new { MaxConcurrentReportExecutions = ctx.SaasMaxReportSessions }
            },
            Lineage = new { Namespace = $"etlsql.tenant.{tenant}" }
        };
        await using var stream = new FileStream(ResolveTenantPath(root, "config/appsettings.tenant.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, ct);
    }

    internal static string ResolveTenantPath(string tenantRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathFullyQualified(relativePath))
            throw new InvalidDataException("Tenant boundary paths must be relative to the host-fixed tenant root.");

        var root = Path.GetFullPath(tenantRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes the host-fixed tenant boundary: {relativePath}");
        return resolved;
    }

    internal static long EnsureStorageWithinQuota(string tenantRoot, long maxBytes)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        var root = Path.GetFullPath(tenantRoot);
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            total = checked(total + new FileInfo(path).Length);
            if (total > maxBytes)
                throw new InvalidOperationException(
                    $"Tenant storage quota exceeded: {total} bytes staged, limit {maxBytes} bytes.");
        }
        return total;
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    [GeneratedRegex(@"\b(?:PASSWORD|API_KEY|TOKEN|CLIENT_SECRET|PRIVATE_KEY)\s*=\s*'(?!SECRET:|ENC:|ENV\()[^'$][^']*'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RawCredentialRegex();
}
