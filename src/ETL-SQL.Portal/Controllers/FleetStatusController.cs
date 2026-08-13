using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Read-only fleet health surface (P2.2). Returns only aggregate operational counts for this
/// environment, gated to the scoped <c>FleetReader</c> role (and Admin). This is the ONLY thing a
/// fleet aggregator credential may reach — it cannot read report data, run scripts, mutate state, or
/// access secrets/keys (see the fleet trust boundary in Departmental_Isolation.md).
/// </summary>
[ApiController]
[Route("api/fleet")]
[Authorize(Roles = "FleetReader,Admin")]
public sealed class FleetStatusController(
    HealthCheckService health,
    ExecutionJobService executions,
    PortalDbContext db,
    IArtifactStorage artifacts,
    PortalConfig config,
    PortalNodeIdentity nodeIdentity,
    DatasetTenantScope tenantScope,
    PortalTenantCatalogScope catalogScope) : ControllerBase
{
    /// <summary>
    /// The read-only Fleet/Operations workspace: every configured environment polled at once, with
    /// divergence findings, grouping, and an upgrade preflight or postflight report.
    ///
    /// The aggregation itself has existed for a while but had nothing to aggregate — no
    /// configuration named the environments, so it was machinery with no way in. This is that way
    /// in. It issues one scoped read-only GET per environment and nothing else: naming an
    /// environment here grants visibility, never authority, and a departmental deployment is not
    /// administered from another one's Portal. An unreachable environment is reported as
    /// unreachable rather than failing the whole view, because a partial outage is exactly when the
    /// view is needed.
    /// </summary>
    [HttpGet("workspace")]
    public async Task<IActionResult> Workspace(
        [FromServices] FleetHealthAggregator aggregator,
        [FromQuery] string mode = "preflight",
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? reachable = null,
        [FromQuery] bool? upgradeReady = null,
        [FromQuery] string groupBy = "none",
        CancellationToken ct = default)
    {
        var configured = config.Fleet.Environments
            .Where(environment => !string.IsNullOrWhiteSpace(environment.Name)
                && Uri.TryCreate(environment.BaseUrl, UriKind.Absolute, out _))
            .ToList();

        if (configured.Count == 0)
        {
            // An empty workspace and a misconfigured one look identical unless one says so.
            return Ok(new
            {
                configured = false,
                environments = Array.Empty<object>(),
                message = "No fleet environments are configured. Add them under Portal:Fleet:Environments "
                    + "with a FleetReader-scoped token for each."
            });
        }

        if (!Enum.TryParse<FleetUpgradeReportMode>(mode, ignoreCase: true, out var reportMode))
            return BadRequest(new { error = "mode must be 'preflight' or 'postflight'." });
        if (!Enum.TryParse<FleetGroupBy>(groupBy, ignoreCase: true, out var grouping))
            return BadRequest(new { error = $"groupBy must be one of: {string.Join(", ", Enum.GetNames<FleetGroupBy>())}." });

        var descriptors = configured.Select(environment => new FleetEnvironmentDescriptor(
            environment.Name, new Uri(environment.BaseUrl), environment.BearerToken));

        var report = await aggregator.AggregateAsync(
            descriptors,
            new FleetViewOptions(search, status, reachable, upgradeReady, GroupBy: grouping),
            ct);

        return Ok(new
        {
            configured = true,
            // Presence only: the per-environment tokens are credentials, not status.
            credentialsConfigured = configured.Count(environment => !string.IsNullOrWhiteSpace(environment.BearerToken)),
            report,
            upgrade = FleetHealthAggregator.BuildUpgradeReport(report, reportMode)
        });
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var report = await health.CheckHealthAsync(ct);
        var (queued, running) = executions.GetWorkloadCounts();

        var failedRefreshes = await catalogScope.Reports.CountAsync(r => r.LastRefreshStatus == "Failed", ct);
        var outboxPending = await db.AuditOutboxMessages.CountAsync(
            x => x.TenantId == tenantScope.TenantId && x.Status == "Pending", ct);
        var outboxFailed = await db.AuditOutboxMessages.CountAsync(
            x => x.TenantId == tenantScope.TenantId && x.Status == "Failed", ct);

        var storage = await ProbeStorageAsync(ct);
        var inventory = await BuildInventoryAsync(report, storage, ct);

        return Ok(new FleetEnvironmentStatus(
            Environment: Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default",
            Status: report.Status.ToString(),
            QueueDepth: queued,
            ActiveExecutions: running,
            FailedRefreshes: failedRefreshes,
            AuditOutboxPending: outboxPending,
            AuditOutboxFailed: outboxFailed,
            Storage: storage,
            CapturedAtUtc: DateTime.UtcNow,
            SecurityEvents: SecurityEventRuntime.GetDiagnostics(),
            Inventory: inventory));
    }

    private async Task<string> ProbeStorageAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await foreach (var _ in artifacts
                .EnumerateAsync(ArtifactArea.Snapshots, prefix: null, recursive: false, timeout.Token)
                .WithCancellation(timeout.Token))
            {
                break;
            }
            return "ok";
        }
        catch
        {
            return "unavailable";
        }
    }

    private async Task<FleetNodeInventory> BuildInventoryAsync(
        HealthReport report,
        string storage,
        CancellationToken ct)
    {
        var environment = Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default";
        var policy = EnterprisePolicyRuntime.Current;
        var status = new EnterpriseEnrollmentStore().GetStatus();
        var enrollment = status.Enrollment;

        var appliedMigrations = new List<string>();
        var pendingMigrations = new List<string>();
        try
        {
            appliedMigrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        }
        catch
        {
            pendingMigrations.Add("migration_status_unavailable");
        }

        var schemas = new FleetSchemaVersions(
            EnterpriseEnrollmentDocument.CurrentSchemaVersion,
            SignedOrganizationPolicyEnvelope.CurrentSchemaVersion,
            OrganizationPolicyDocument.CurrentSchemaVersion,
            SecurityEventContract.CurrentSchemaVersion,
            appliedMigrations.LastOrDefault(),
            appliedMigrations.Count,
            pendingMigrations.Count);

        var policyHash = ComputePolicyHash(policy.Document);
        var policyInventory = new FleetPolicyInventory(
            policy.IsEnrolled,
            policy.IsAvailable,
            policy.Status,
            policy.PolicyVersion,
            policyHash,
            policy.IssuedAtUtc,
            policy.ExpiresAtUtc,
            policy.LoadedAtUtc,
            enrollment is not null,
            !string.IsNullOrWhiteSpace(enrollment?.ClientCertificateThumbprint),
            TryGetClientCertificateExpiry(enrollment?.ClientCertificateThumbprint),
            policy.ConfigurationValues.Count);

        var providers = new FleetRuntimeProviders(
            NormalizeProvider(config.Database.Provider, db.Database.ProviderName),
            string.IsNullOrWhiteSpace(config.Storage.Provider) ? "Local" : config.Storage.Provider);

        var readiness = BuildUpgradeReadiness(report, storage, policy, pendingMigrations.Count);
        return new FleetNodeInventory(
            environment,
            nodeIdentity.NodeId,
            typeof(FleetStatusController).Assembly.GetName().Version?.ToString() ?? "unknown",
            schemas,
            policyInventory,
            providers,
            ComputeConfigurationFingerprint(environment, providers, schemas, policy),
            readiness,
            BuildCompatibilityMetadata(schemas, providers),
            BuildMigrationState());
    }

    private static FleetUpgradeReadiness BuildUpgradeReadiness(
        HealthReport report,
        string storage,
        EffectiveEnterprisePolicy policy,
        int pendingMigrations)
    {
        var findings = new List<string>();
        if (report.Status == HealthStatus.Unhealthy)
            findings.Add("portal-health-unhealthy");
        if (pendingMigrations > 0)
            findings.Add("portal-schema-has-pending-migrations");
        if (!string.Equals(storage, "ok", StringComparison.OrdinalIgnoreCase))
            findings.Add("artifact-storage-unavailable");
        if (policy.IsEnrolled && !policy.IsAvailable)
            findings.Add("enterprise-policy-unavailable");
        if (policy.ExpiresAtUtc is { } expires && expires <= DateTimeOffset.UtcNow.AddHours(1))
            findings.Add("enterprise-policy-expires-within-one-hour");

        return new FleetUpgradeReadiness(findings.Count == 0, findings);
    }

    private static string NormalizeProvider(string configured, string? efProvider)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        if (efProvider?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            return "Postgres";
        return "Sqlite";
    }

    private static string ComputeConfigurationFingerprint(
        string environment,
        FleetRuntimeProviders providers,
        FleetSchemaVersions schemas,
        EffectiveEnterprisePolicy policy)
    {
        var payload = JsonSerializer.Serialize(new
        {
            environment,
            providers,
            schemas.Enrollment,
            schemas.PolicyEnvelope,
            schemas.PolicyPayload,
            schemas.SecurityEvent,
            policy.PolicyVersion,
            governedKeys = policy.ConfigurationValues.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
        });
        return Hash(payload);
    }

    private static FleetCompatibilityMetadata BuildCompatibilityMetadata(
        FleetSchemaVersions schemas,
        FleetRuntimeProviders providers)
    {
        var portalVersion = typeof(FleetStatusController).Assembly.GetName().Version?.ToString() ?? "unknown";
        var reportingVersion = typeof(ETL_SQL.Reporting.ReportManifest).Assembly.GetName().Version?.ToString() ?? "unknown";
        return new FleetCompatibilityMetadata(
            MetadataVersion: "1.0",
            CompatibilityWindow: "N-1 rolling when all nodes report ready and no pending migrations",
            RollingUpgradeSequence: new[]
            {
                "preflight-readiness",
                "drain-node",
                "deploy-binaries",
                "single-owner-database-migration",
                "health-verification",
                "restore-traffic",
                "postflight-readiness",
                "rollback-decision"
            },
            Components: new[]
            {
                new FleetComponentCompatibility("portal", portalVersion, "http-api", "N-1", true, "ready"),
                new FleetComponentCompatibility("engine", LanguageMetadata.EngineVersion, "etlsql-runtime", "N-1", true, "ready"),
                new FleetComponentCompatibility("reporting", reportingVersion, "report-manifest", "N-1", true, "ready"),
                new FleetComponentCompatibility("portal-database", providers.PortalDatabase,
                    $"ef-migrations:last={schemas.LastAppliedPortalMigration ?? "none"};pending={schemas.PendingPortalMigrations}",
                    "expand/migrate/contract", true,
                    schemas.PendingPortalMigrations == 0 ? "ready" : "pending-migrations"),
                new FleetComponentCompatibility("artifact-storage", providers.ArtifactStorage, "snapshot-packages", "N-1", true, "ready"),
                new FleetComponentCompatibility("enterprise-enrollment", schemas.Enrollment, "bootstrap", "1.0", true, "ready"),
                new FleetComponentCompatibility("policy-envelope", schemas.PolicyEnvelope, "signed-policy-envelope", "1.0", true, "ready"),
                new FleetComponentCompatibility("policy-payload", schemas.PolicyPayload, "organization-policy", "1.0", true, "ready"),
                new FleetComponentCompatibility("security-event-collector", schemas.SecurityEvent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "security-event-schema", "1", false, "ready"),
                new FleetComponentCompatibility("connectors", LanguageMetadata.EngineVersion,
                    $"connector-token-count={LanguageMetadata.ConnectorTypes.Count}", "N-1", false, "ready"),
                new FleetComponentCompatibility("plugins", "not-advertised", "external-extension-contract", "none", false, "not-reported")
            });
    }

    private static FleetMigrationState BuildMigrationState()
    {
        var status = PortalDatabaseMigrationLock.CurrentStatus;
        return new FleetMigrationState(
            status.State,
            status.OwnerNodeId,
            status.Provider,
            status.LockKind,
            status.LockKey,
            status.StartedAtUtc,
            status.AcquiredAtUtc,
            status.CompletedAtUtc,
            status.UpdatedAtUtc,
            status.PendingMigrations,
            status.Error);
    }

    private static string? ComputePolicyHash(OrganizationPolicyDocument? document) =>
        document is null ? null : Hash(JsonSerializer.Serialize(document));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static DateTimeOffset? TryGetClientCertificateExpiry(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            return null;

        var normalized = thumbprint.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly);
                foreach (var cert in store.Certificates)
                {
                    using (cert)
                    {
                        if (string.Equals(cert.Thumbprint, normalized, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), normalized,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return new DateTimeOffset(cert.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                        }
                    }
                }
            }
            catch
            {
                // Fleet inventory reports absence rather than leaking certificate-store errors.
            }
        }

        return null;
    }
}
