using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Common;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// The Portal's online-safe support bundle: diagnostics an operator can collect while the Portal is
/// running, redacted, reviewable before download, and audited.
///
/// The CLI's <c>admin support-bundle</c> stays the recovery path for when the Portal is down — it
/// can read files and configuration this cannot. What it cannot do is be run by someone who only has
/// a browser, which is the common case when a vendor asks for diagnostics.
///
/// Two properties make it safe to expose: it collects <b>counts, versions, and states</b> rather than
/// content — no report data, no dataset rows, no log bodies — and everything textual passes through
/// <see cref="SupportBundleRedactor"/>, the same rules the CLI bundle uses.
/// </summary>
public sealed class PortalSupportBundleService(
    PortalDbContext db,
    PortalConfig config,
    HealthCheckService health,
    PortalNodeIdentity nodeIdentity,
    TimeProvider clock)
{
    public async Task<SupportBundleContentDto> BuildAsync(CancellationToken ct = default)
    {
        var sections = new List<SupportBundleSectionDto>();
        var tenantContext = string.IsNullOrWhiteSpace(config.TenantId)
            ? null
            : ETL_SQL.Core.Multitenancy.TenantContext.FromHostConfiguration(config.TenantId);

        // Health: statuses and durations, never the description of a failing check verbatim —
        // those can quote a connection string.
        var report = await health.CheckHealthAsync(ct);
        sections.Add(Section("health", "Health check results", new JsonObject
        {
            ["status"] = report.Status.ToString(),
            ["entries"] = new JsonArray([.. report.Entries.Select(entry => (JsonNode)new JsonObject
            {
                ["name"] = entry.Key,
                ["status"] = entry.Value.Status.ToString(),
                ["durationMs"] = (int)entry.Value.Duration.TotalMilliseconds,
                ["description"] = SupportBundleRedactor.RedactDiagnosticText(entry.Value.Description)
            })])
        }, volatileCounts: true));

        sections.Add(Section("deployment", "Deployment identity and versions", new JsonObject
        {
            ["nodeId"] = nodeIdentity.NodeId,
            ["tenantId"] = tenantContext?.Tenant.Value,
            ["tenantContextOrigin"] = tenantContext?.Origin.ToString(),
            ["environment"] = Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default",
            ["portalVersion"] = typeof(PortalSupportBundleService).Assembly.GetName().Version?.ToString() ?? "unknown",
            ["dotnetRuntime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = Environment.OSVersion.ToString(),
            ["databaseProvider"] = db.Database.ProviderName ?? "unknown",
            ["storageProvider"] = string.IsNullOrWhiteSpace(config.Storage.Provider) ? "Local" : config.Storage.Provider,
            ["studioMode"] = config.Studio.Mode.ToString()
        }));

        sections.Add(Section("schema", "Database migration state", new JsonObject
        {
            ["applied"] = (await db.Database.GetAppliedMigrationsAsync(ct)).Count(),
            ["pending"] = (await db.Database.GetPendingMigrationsAsync(ct)).Count(),
            ["latestApplied"] = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault()
        }));

        // Catalog shape as counts. "How big is this deployment" answers most support questions and
        // reveals nothing about what is in it.
        sections.Add(Section("catalog", "Catalog size (counts only)", new JsonObject
        {
            ["users"] = await db.Users.CountAsync(ct),
            ["activeUsers"] = await db.Users.CountAsync(user => user.IsActive, ct),
            ["groups"] = await db.Groups.CountAsync(ct),
            ["folders"] = await db.Folders.CountAsync(ct),
            ["reports"] = await db.Reports.CountAsync(report => !report.IsDeleted, ct),
            ["datasets"] = await db.Datasets.CountAsync(ct),
            ["subscriptions"] = await db.Subscriptions.CountAsync(ct),
            ["sharedConnections"] = await db.PortalSharedConnections.CountAsync(ct),
            ["secrets"] = await db.PortalSecrets.CountAsync(ct)
        }, volatileCounts: true));

        sections.Add(Section("auditDelivery", "Audit outbox state", new JsonObject
        {
            ["pending"] = await db.AuditOutboxMessages.CountAsync(message => message.Status == "Pending", ct),
            ["failed"] = await db.AuditOutboxMessages.CountAsync(message => message.Status == "Failed", ct),
            ["delivered"] = await db.AuditOutboxMessages.CountAsync(message => message.Status == "Delivered", ct),
            ["collectorConfigured"] = !string.IsNullOrWhiteSpace(config.Audit.TransportEndpoint)
        }, volatileCounts: true));

        // Configuration travels only as the redacted document, and only the Portal section: the rest
        // of appsettings belongs to hosts this process does not speak for.
        var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        sections.Add(Section("configuration", "Portal configuration (redacted)",
            JsonNode.Parse(SupportBundleRedactor.RedactConfigJson(configJson))!));

        var generatedAt = clock.GetUtcNow().UtcDateTime;
        var content = new SupportBundleContentDto(
            generatedAt,
            sections,
            ContentHash: ComputeHash(sections),
            RedactionNote:
                "Counts, versions and states only — no report data, dataset rows, or log bodies. "
                + "Configuration values are masked by key, and free text has credentials, addresses, "
                + "host paths, and data-shaped rows removed. Review before sending this anywhere.",
            Excluded:
            [
                "Report and dataset contents, and any query results",
                "Log file bodies (the CLI bundle collects those, on the host, for when the Portal is down)",
                "Secret values, key material, and tokens",
                "Audit row detail (counts only)"
            ]);

        return content;
    }

    private static SupportBundleSectionDto Section(
        string key, string title, JsonNode payload, bool volatileCounts = false) =>
        new(key, title, payload, volatileCounts);

    /// <summary>
    /// Identifies what a review was <em>about</em>: the deployment and the disclosure, not every
    /// number in it.
    ///
    /// Volatile sections are excluded deliberately. Reviewing the bundle audits the review, which
    /// moves the audit-outbox counts the bundle reports — so hashing everything would make each
    /// review stale the instant it was made, and the acknowledgement check would degrade into noise
    /// an operator learns to bypass. The question worth answering is "is this the same deployment,
    /// with the same configuration and the same things left out, that I approved?"; a counter
    /// ticking is not a reason to refuse the download.
    /// </summary>
    private static string ComputeHash(IEnumerable<SupportBundleSectionDto> sections)
    {
        var payload = string.Join("\n", sections
            .Where(section => !section.VolatileCounts)
            .Select(section => $"{section.Key}:{section.Payload.ToJsonString()}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16].ToLowerInvariant();
    }
}
