using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed record ImpactConsumer(string Type, string Name, string? Detail, DateTime? LastUsedAtUtc, long? UseCount);

public sealed record ImpactReport(string Reference, int ConsumerCount, IReadOnlyList<ImpactConsumer> Consumers);

/// <summary>
/// Answers "what breaks if I disable or delete this?" for shared connections and secrets: scans
/// published report scripts, subscription job scripts, and orchestrator job scripts for the
/// SHARED:alias / SECRET:name token, includes catalog entries that reference a secret, and (for
/// shared connections) lists per-consumer usage recorded at resolution time. Text-based and
/// best-effort by design: a scan failure on one source never hides the others.
/// </summary>
public sealed class ReferenceImpactService(
    PortalDbContext db,
    PortalConfig portalConfig,
    PortalTenantJobEvidenceStore jobHistory,
    ILogger<ReferenceImpactService> logger,
    DatasetTenantScope? tenantScope = null,
    PortalTenantCatalogScope? catalogScope = null)
{
    private readonly DatasetTenantScope _tenantScope = tenantScope ?? new DatasetTenantScope(portalConfig);
    private IQueryable<Report> Reports => catalogScope?.Reports ?? db.Reports;
    private IQueryable<Subscription> Subscriptions => catalogScope?.Subscriptions ?? db.Subscriptions;
    private const int MaxScriptBytes = 1024 * 1024;

    public async Task<ImpactReport> ForSharedConnectionAsync(string alias, CancellationToken ct = default)
    {
        var reference = $"SHARED:{alias}";
        var consumers = await ScanScriptsAsync(reference, ct);

        var entity = await db.PortalSharedConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Alias == alias, ct);
        if (entity != null)
        {
            var usages = await db.SharedConnectionUsages
                .AsNoTracking()
                .Where(u => u.SharedConnectionId == entity.Id)
                .OrderByDescending(u => u.LastUsedAtUtc)
                .ToListAsync(ct);
            consumers.AddRange(usages.Select(u =>
                new ImpactConsumer("Consumer", u.ConsumerUser, "Recorded at SHARED: resolution", u.LastUsedAtUtc, u.UseCount)));
        }

        var alertLinks = await db.AlertNotifications
            .AsNoTracking()
            .Include(n => n.Alert)
                .ThenInclude(a => a.Report)
                    .ThenInclude(r => r.Folder)
            .Where(n => n.OrchestratorAlias == alias && !n.Alert.Report.IsDeleted)
            .OrderBy(n => n.Alert.Report.Folder.Path)
            .ThenBy(n => n.Alert.Report.Name)
            .ThenBy(n => n.Alert.Name)
            .ThenBy(n => n.NotificationName)
            .ToListAsync(ct);
        consumers.AddRange(alertLinks.Select(n =>
            new ImpactConsumer(
                "AlertNotification",
                n.Alert.Name,
                $"{n.Alert.Report.Folder.Path}/{n.Alert.Report.Name} → {n.NotificationName}",
                n.Alert.UpdatedAt,
                null)));

        var reportJobLinks = await db.ReportJobLinks
            .AsNoTracking()
            .Include(l => l.Report)
                .ThenInclude(r => r.Folder)
            .Where(l => l.OrchestratorAlias == alias && !l.Report.IsDeleted)
            .OrderBy(l => l.Report.Folder.Path)
            .ThenBy(l => l.Report.Name)
            .ThenBy(l => l.JobName)
            .ToListAsync(ct);
        consumers.AddRange(reportJobLinks.Select(l =>
            new ImpactConsumer(
                "ReportJobLink",
                l.JobName,
                $"{l.Report.Folder.Path}/{l.Report.Name}",
                l.LastRefreshedAt ?? l.UpdatedAt,
                null)));

        return new ImpactReport(reference, consumers.Count, consumers);
    }

    public async Task<ImpactReport> ForSecretAsync(string name, CancellationToken ct = default)
    {
        var reference = $"SECRET:{name}";
        var consumers = await ScanScriptsAsync(reference, ct);

        // Shared connection entries referencing the secret (options/target are decrypted by the
        // PII converter on materialization, so the token check must happen in memory).
        var entries = await db.PortalSharedConnections.AsNoTracking().ToListAsync(ct);
        foreach (var entry in entries)
        {
            if (ContainsReference(entry.OptionsJson, reference) || ContainsReference(entry.Target, reference))
                consumers.Add(new ImpactConsumer("SharedConnection", entry.Alias, entry.ConnectorType, entry.LastUsedAtUtc, null));
        }

        return new ImpactReport(reference, consumers.Count, consumers);
    }

    private async Task<List<ImpactConsumer>> ScanScriptsAsync(string reference, CancellationToken ct)
    {
        var consumers = new List<ImpactConsumer>();

        var reports = await Reports
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .Join(
                db.Users.AsNoTracking().Where(user => user.TenantId == _tenantScope.TenantId),
                report => report.CreatedBy,
                user => user.Id,
                (report, _) => report)
            .Select(r => new { r.Id, r.Name, r.ScriptPath })
            .ToListAsync(ct);
        foreach (var report in reports)
        {
            if (PortalPathGuard.TryResolveScript(
                    portalConfig, _tenantScope.TenantId, report.ScriptPath, out var resolved)
                && ContainsReference(await ReadBoundedAsync(resolved, ct), reference))
            {
                consumers.Add(new ImpactConsumer("Report", report.Name, report.ScriptPath, null, null));
            }
        }

        var subscriptions = await Subscriptions
            .AsNoTracking()
            .Where(s => s.ScriptPath != null && s.User.TenantId == _tenantScope.TenantId)
            .Select(s => new { s.Id, s.ReportId, s.ScriptPath })
            .ToListAsync(ct);
        foreach (var subscription in subscriptions)
        {
            if (PortalPathGuard.TryResolveScript(
                    portalConfig, _tenantScope.TenantId, subscription.ScriptPath!, out var resolved)
                && ContainsReference(await ReadBoundedAsync(resolved, ct), reference))
            {
                consumers.Add(new ImpactConsumer(
                    "Subscription", $"Subscription #{subscription.Id}", $"Report {subscription.ReportId}", null, null));
            }
        }

        try
        {
            foreach (var job in await jobHistory.GetAllJobsAsync())
            {
                // JobDefinition.Script is a path for scheduled jobs; scan the file when it exists
                // and fall back to the raw value so inline definitions are covered too.
                var content = File.Exists(job.Script) ? await ReadBoundedAsync(job.Script, ct) : job.Script;
                if (ContainsReference(content, reference))
                    consumers.Add(new ImpactConsumer("ScheduledJob", job.Name, job.Script, job.LastRun, null));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impact scan could not enumerate orchestrator jobs.");
        }

        return consumers;
    }

    private async Task<string?> ReadBoundedAsync(string path, CancellationToken ct)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxScriptBytes)
                return null;
            return await File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Impact scan could not read {Path}.", path);
            return null;
        }
    }

    // The token must not be followed by another name character, so SHARED:db does not match SHARED:db2.
    private static bool ContainsReference(string? text, string reference)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var index = 0;
        while ((index = text.IndexOf(reference, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = index + reference.Length;
            if (end >= text.Length || !IsNameChar(text[end]))
                return true;
            index = end;
        }

        return false;
    }

    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '.' or '-';
}
