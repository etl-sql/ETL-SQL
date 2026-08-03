using System.Globalization;
using ETL_SQL.Portal.Data;
using ETL_SQL.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Evaluates report alerts after trusted scheduled refreshes persist a new shared snapshot.
/// Alert delivery is transition-based: an alert notifies only when it enters TRIGGERED.
/// </summary>
public sealed class PortalAlertEvaluationService(
    IServiceScopeFactory scopes,
    PortalConfig config,
    SnapshotPackageService snapshots,
    OrchestratorProxyService orchestrator,
    ILogger<PortalAlertEvaluationService> logger)
{
    public async Task EvaluateScheduledRefreshAsync(
        int reportId,
        string portalJobId,
        string manifestPath,
        DateTime completedAt,
        CancellationToken ct = default)
    {
        var key = PortalPathGuard.ToSnapshotKey(config, manifestPath);
        if (key is null)
        {
            logger.LogWarning(
                "Skipping alert evaluation for report {ReportId}: manifest path is outside the snapshot root.",
                reportId);
            return;
        }

        ReportManifest? manifest;
        try
        {
            manifest = await snapshots.LoadAsync(key, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Skipping alert evaluation for report {ReportId}: failed to load manifest {ManifestPath}.",
                reportId, manifestPath);
            return;
        }

        if (manifest is null) return;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var alerts = await db.ReportAlerts
            .Include(alert => alert.Notifications)
            .Include(alert => alert.Report)
            .Include(alert => alert.Owner)
            .Where(alert => alert.ReportId == reportId && alert.IsActive && !alert.Report.IsDeleted)
            .ToListAsync(ct);
        if (alerts.Count == 0) return;

        alerts = await AuthorizedAlertsAsync(db, alerts, ct);
        if (alerts.Count == 0) return;

        foreach (var alert in alerts)
        {
            var previousState = alert.LastState;
            var value = TryReadVisualValue(manifest, alert.VisualName);
            var isTriggered = value.HasValue && Compare(value.Value, alert.Operator, alert.Threshold);
            var currentState = isTriggered ? "TRIGGERED" : "OK";

            alert.LastCheckedAt = completedAt;
            alert.LastEvaluatedAt = completedAt;
            alert.LastState = currentState;
            if (isTriggered)
                alert.LastTriggeredAt = completedAt;

            if (isTriggered && !string.Equals(previousState, "TRIGGERED", StringComparison.OrdinalIgnoreCase))
            {
                var delivered = await DispatchAlertNotificationsAsync(alert, value, portalJobId, completedAt, ct);
                if (delivered)
                    alert.LastNotifiedAt = completedAt;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Drops alerts whose owner may no longer read the report, mirroring the delivery-time
    /// re-authorization subscriptions already perform.
    ///
    /// An alert notification carries the value that crossed the threshold, so an alert left running
    /// after its author was deactivated or removed from every group keeps pushing report data into a
    /// channel that author chose — a deprovisioning gap that disabling the account did not close.
    /// Unauthorized alerts are skipped whole rather than evaluated-but-not-sent: recording a
    /// TRIGGERED transition nobody was told about would swallow the notification for good if access
    /// were later restored.
    ///
    /// Scope note: like subscription delivery, this resolves folder permission. Report-level ACLs are
    /// not consulted, so the two delivery paths stay consistent with one another.
    /// </summary>
    private async Task<List<ReportAlert>> AuthorizedAlertsAsync(
        PortalDbContext db,
        List<ReportAlert> alerts,
        CancellationToken ct)
    {
        // Constructed over the scoped context rather than resolved: permission resolution is a pure
        // function of this DbContext, and this service already owns the scope.
        var folderPermissions = new FolderPermissionService(db);
        var authorized = new List<ReportAlert>(alerts.Count);

        foreach (var alert in alerts)
        {
            var denial = await DenyReasonAsync(db, folderPermissions, alert, ct);
            if (denial is null)
            {
                authorized.Add(alert);
                continue;
            }

            logger.LogWarning(
                "Alert {AlertName} on report {ReportId} was not evaluated: {Reason}",
                alert.Name, alert.ReportId, denial);
        }

        return authorized;
    }

    private static async Task<string?> DenyReasonAsync(
        PortalDbContext db,
        FolderPermissionService folderPermissions,
        ReportAlert alert,
        CancellationToken ct)
    {
        var owner = alert.Owner
            ?? await db.Users.FirstOrDefaultAsync(u => u.Id == alert.OwnerId, ct);
        if (owner is null)
            return "alert owner no longer exists";
        if (!owner.IsActive)
            return "alert owner is disabled";

        var isAdmin = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(x => x.UserId == owner.Id && x.Name == "Admin", ct);
        if (isAdmin)
            return null;

        var groupIds = new HashSet<int>(await db.UserGroups
            .Where(ug => ug.UserId == owner.Id)
            .Select(ug => ug.GroupId)
            .ToListAsync(ct));
        var permission = await folderPermissions.GetEffectivePermissionAsync(
            alert.Report.FolderId, groupIds, owner.Id);

        return permission is null || permission < FolderPermission.Read
            ? "alert owner no longer has read permission on the report"
            : null;
    }

    private async Task<bool> DispatchAlertNotificationsAsync(
        ReportAlert alert,
        decimal? value,
        string portalJobId,
        DateTime completedAt,
        CancellationToken ct)
    {
        var links = alert.Notifications
            .Where(link => !string.IsNullOrWhiteSpace(link.NotificationName))
            .ToList();
        if (links.Count == 0) return false;

        var delivered = false;
        foreach (var link in links)
        {
            // OrchestratorAlias is persisted for the normalized model. The Portal currently has one
            // configured Orchestrator endpoint, so routing by alias remains a later migration slice.
            var response = await orchestrator.DispatchNotificationAsync(
                link.NotificationName,
                new OrchestratorNotificationDispatchRequest(
                    SourceKind: "ALERT",
                    Title: $"Alert triggered: {alert.Name}",
                    Text: $"Report alert '{alert.Name}' on visual '{alert.VisualName}' evaluated to {FormatValue(value)} {alert.Operator} {alert.Threshold}.",
                    Trigger: "THRESHOLD",
                    Status: "TRIGGERED",
                    JobName: portalJobId,
                    AlertName: alert.Name,
                    ReportId: alert.ReportId.ToString(CultureInfo.InvariantCulture)),
                ct);

            if (response?.IsSuccessStatusCode == true)
                delivered = true;
            else
                logger.LogWarning(
                    "Alert {AlertName}: notification '{Notification}' dispatch returned {StatusCode}.",
                    alert.Name, link.NotificationName, response?.StatusCode.ToString() ?? "offline");
        }

        return delivered;
    }

    private static decimal? TryReadVisualValue(ReportManifest manifest, string visualName)
    {
        var visual = manifest.Visuals.FirstOrDefault(value =>
            string.Equals(value.Name, visualName, StringComparison.OrdinalIgnoreCase));
        if (visual is null) return null;

        foreach (var row in visual.Rows)
        {
            foreach (var cell in row)
            {
                if (decimal.TryParse(
                        cell,
                        NumberStyles.Number | NumberStyles.AllowExponent,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static bool Compare(decimal value, string op, decimal threshold) =>
        op.Trim().ToUpperInvariant() switch
        {
            ">" => value > threshold,
            ">=" => value >= threshold,
            "<" => value < threshold,
            "<=" => value <= threshold,
            "=" or "==" => value == threshold,
            "!=" or "<>" => value != threshold,
            _ => false
        };

    private static string FormatValue(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "<missing>";
}
