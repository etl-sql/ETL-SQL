using System.Text;
using ETL_SQL.Core;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public enum SubscriptionDeliveryOutcome { Delivered, Denied, Failed, Skipped }

public sealed record SubscriptionDeliveryResult(SubscriptionDeliveryOutcome Outcome, string? Reason)
{
    public static SubscriptionDeliveryResult Delivered() => new(SubscriptionDeliveryOutcome.Delivered, null);
    public static SubscriptionDeliveryResult Denied(string reason) => new(SubscriptionDeliveryOutcome.Denied, reason);
    public static SubscriptionDeliveryResult Failed(string reason) => new(SubscriptionDeliveryOutcome.Failed, reason);
    public static SubscriptionDeliveryResult Skipped(string reason) => new(SubscriptionDeliveryOutcome.Skipped, reason);
}

/// <summary>
/// Executes a composed delivery script. Abstracted so authorization and script composition
/// are testable without a live SMTP server; the engine implementation always runs in-process
/// so the composed script (which holds the SMTP credential in memory) never leaves the portal.
/// </summary>
public interface ISubscriptionScriptRunner
{
    Task<(bool Success, string? Error)> RunAsync(string scriptText, string sessionId, CancellationToken ct);
}

public sealed class EngineSubscriptionScriptRunner(IScriptExecutor executor) : ISubscriptionScriptRunner
{
    public async Task<(bool Success, string? Error)> RunAsync(string scriptText, string sessionId, CancellationToken ct)
    {
        var result = await executor.ExecuteTextAsync(scriptText, sessionId, ct);
        return (result.Success, result.Success ? null : result.ErrorMessage);
    }
}

/// <summary>
/// The trusted subscription executor (TODO P0.1/P0.2). Persisted job scripts are credential-free
/// triggers; this service performs the actual export + email delivery in-process:
/// it reloads the subscription owner, active state, report state, and current folder permission
/// immediately before delivery, composes the export/SMTP script in memory only (the SMTP
/// credential is decrypted per delivery and never written to disk), and records the outcome.
/// A denied delivery is recorded without report data and is not treated as a transient failure.
/// </summary>
public class SubscriptionDeliveryService(
    PortalDbContext db,
    PortalConfig config,
    SmtpPasswordProtector pwdProtector,
    FolderPermissionService folderPermissions,
    AuditService audit,
    ISubscriptionScriptRunner runner,
    ILogger<SubscriptionDeliveryService> log)
{
    public async Task<SubscriptionDeliveryResult> DeliverAsync(int subscriptionId, CancellationToken ct = default)
    {
        var sub = await db.Subscriptions
            .Include(s => s.Report)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (sub is null)
            return SubscriptionDeliveryResult.Skipped("Subscription no longer exists.");

        // P0.2: authorization is evaluated at the moment work executes, not at creation time.
        var denial = await AuthorizeAsync(sub, ct);
        if (denial is not null)
        {
            await audit.LogAsync(sub.UserId, "SUBSCRIPTION_DELIVERY_DENIED", "Subscription",
                sub.Id.ToString(), denial);
            log.LogWarning("Subscription {SubscriptionId} delivery denied: {Reason}", sub.Id, denial);
            return SubscriptionDeliveryResult.Denied(denial);
        }

        if (!PortalPathGuard.TryResolveScript(config, sub.Report.ScriptPath, out var reportScriptPath))
            return await RecordFailureAsync(sub,
                "Report script path is outside the configured script root.", ct);
        if (!File.Exists(reportScriptPath))
            return await RecordFailureAsync(sub, "Report script file no longer exists.", ct);

        SmtpConnection? smtp = null;
        if (!string.IsNullOrEmpty(sub.SmtpAlias))
        {
            smtp = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Alias == sub.SmtpAlias, ct);
            if (smtp is null)
                return await RecordFailureAsync(sub,
                    $"SMTP connection '{sub.SmtpAlias}' no longer exists.", ct);
        }
        else if (sub.Format != SubscriptionFormat.Link)
        {
            return await RecordFailureAsync(sub,
                "Subscription has no SMTP alias for attachment delivery.", ct);
        }
        else
        {
            return SubscriptionDeliveryResult.Skipped(
                "Link subscription has no SMTP alias — nothing to deliver.");
        }

        var smtpPassword = pwdProtector.Unprotect(smtp.EncryptedPassword);
        if (!string.IsNullOrEmpty(smtp.EncryptedPassword) && smtpPassword is null)
            return await RecordFailureAsync(sub, "SMTP credential could not be resolved.", ct);

        string? exportPath = null;
        try
        {
            var script = ComposeDeliveryScript(sub, reportScriptPath, smtp, smtpPassword, out exportPath);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, config.Resources.ExecutionTimeoutSeconds)));

            bool success;
            string? error;
            try
            {
                (success, error) = await runner.RunAsync(script, $"sub-delivery-{sub.Id}", cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                (success, error) = (false, "Delivery timed out.");
            }
            catch (Exception ex)
            {
                (success, error) = (false, ex.Message);
            }

            if (!success)
                return await RecordFailureAsync(sub, Sanitize(error, smtpPassword), ct);

            sub.LastSentAt = DateTime.UtcNow;
            sub.FailCount = 0;
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(sub.UserId, "SUBSCRIPTION_DELIVERED", "Subscription",
                sub.Id.ToString(), $"Report {sub.ReportId} ({sub.Format}) to {sub.Recipients}");
            log.LogInformation("Subscription {SubscriptionId} delivered to {Recipients}",
                sub.Id, sub.Recipients);
            return SubscriptionDeliveryResult.Delivered();
        }
        finally
        {
            if (exportPath is not null)
            {
                try { if (File.Exists(exportPath)) File.Delete(exportPath); }
                catch { /* best effort */ }
            }
        }
    }

    // ── Delivery-time authorization (P0.2) ────────────────────────────────────

    private async Task<string?> AuthorizeAsync(Subscription sub, CancellationToken ct)
    {
        if (!sub.IsActive)
            return "Subscription is disabled.";

        var owner = sub.User
            ?? await db.Users.FirstOrDefaultAsync(u => u.Id == sub.UserId, ct);
        if (owner is null)
            return "Subscription owner no longer exists.";
        if (!owner.IsActive)
            return "Subscription owner is disabled.";

        if (sub.Report is null || sub.Report.IsDeleted)
            return "Report no longer exists.";

        if (await IsAdminAsync(owner.Id, ct))
            return null;

        var groupIds = new HashSet<int>(await db.UserGroups
            .Where(ug => ug.UserId == owner.Id)
            .Select(ug => ug.GroupId)
            .ToListAsync(ct));
        var permission = await folderPermissions.GetEffectivePermissionAsync(sub.Report.FolderId, groupIds);
        if (permission is null || permission < FolderPermission.Read)
            return "Subscription owner no longer has read permission on the report.";

        return null;
    }

    private Task<bool> IsAdminAsync(int userId, CancellationToken ct) =>
        db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(x => x.UserId == userId && x.Name == "Admin", ct);

    // ── In-memory delivery script (P0.1) ──────────────────────────────────────

    private static string ComposeDeliveryScript(
        Subscription sub,
        string reportScriptPath,
        SmtpConnection smtp,
        string? smtpPassword,
        out string? exportPath)
    {
        var fromAddr = smtp.FromAddress ?? smtp.Username ?? "etlsql@localhost";
        var sb = new StringBuilder();

        var parameters = DeserializeParams(sub.ParametersJson);
        if (parameters is { Count: > 0 })
        {
            // RELDATE values are stored as-is and resolved fresh by the engine on each run.
            foreach (var (k, v) in parameters)
            {
                var varName = k.StartsWith('@') ? k : "@" + k;
                sb.AppendLine($"DECLARE {varName} STRING = '{Esc(v)}';");
            }
            sb.AppendLine();
        }

        if (sub.Format != SubscriptionFormat.Link)
        {
            var (ext, formatName) = sub.Format switch
            {
                SubscriptionFormat.CSV => ("csv", "CSV"),
                SubscriptionFormat.Markdown => ("md", "MARKDOWN"),
                _ => ("pdf", "PDF")
            };
            exportPath = Path.Combine(Path.GetTempPath(), $"sub_{sub.Id}_{Guid.NewGuid():N}.{ext}");

            sb.AppendLine($"EXPORT REPORT '{reportScriptPath.Replace("\\", "/")}' FORMAT {formatName} TO '{exportPath.Replace("\\", "/")}';");
            sb.AppendLine();
            AppendSmtpConnection(sb, smtp, smtpPassword);
            sb.AppendLine("SEND EMAIL");
            sb.AppendLine($"    TO      '{Esc(sub.Recipients)}'");
            sb.AppendLine($"    FROM    '{Esc(fromAddr)}'");
            sb.AppendLine($"    SUBJECT 'Report: {Esc(sub.Report.Name)}'");
            sb.AppendLine($"    BODY    'Please find the attached report: {Esc(sub.Report.Name)}.'");
            sb.AppendLine($"    ATTACH '{exportPath.Replace("\\", "/")}'");
            sb.AppendLine("    AT __sub_smtp;");
        }
        else
        {
            exportPath = null;
            var portalUrl = $"{{portal_url}}/index.html#report/{sub.ReportId}";
            AppendSmtpConnection(sb, smtp, smtpPassword);
            sb.AppendLine("SEND EMAIL");
            sb.AppendLine($"    TO      '{Esc(sub.Recipients)}'");
            sb.AppendLine($"    FROM    '{Esc(fromAddr)}'");
            sb.AppendLine($"    SUBJECT 'Report ready: {Esc(sub.Report.Name)}'");
            sb.AppendLine($"    BODY    'Your report is ready. View it here: {portalUrl}'");
            sb.AppendLine("    AT __sub_smtp;");
        }

        return sb.ToString();
    }

    private static void AppendSmtpConnection(StringBuilder sb, SmtpConnection smtp, string? password)
    {
        sb.AppendLine("CREATE CONNECTION __sub_smtp AS SMTP(");
        sb.AppendLine($"    HOST     = '{Esc(smtp.Host)}',");
        sb.AppendLine($"    PORT     = {smtp.Port},");
        if (!string.IsNullOrEmpty(smtp.Username))
            sb.AppendLine($"    USERNAME = '{Esc(smtp.Username)}',");
        if (!string.IsNullOrEmpty(password))
            sb.AppendLine($"    PASSWORD = '{Esc(password)}',");
        sb.AppendLine($"    USE_SSL  = '{smtp.UseSsl.ToString().ToLower()}'");
        sb.AppendLine(");");
        sb.AppendLine();
    }

    // ── Outcome recording ─────────────────────────────────────────────────────

    private async Task<SubscriptionDeliveryResult> RecordFailureAsync(
        Subscription sub, string reason, CancellationToken ct)
    {
        sub.FailCount++;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(sub.UserId, "SUBSCRIPTION_DELIVERY_FAILED", "Subscription",
            sub.Id.ToString(), reason);
        log.LogWarning("Subscription {SubscriptionId} delivery failed: {Reason}", sub.Id, reason);
        return SubscriptionDeliveryResult.Failed(reason);
    }

    /// <summary>The persisted failure detail must never echo the SMTP credential.</summary>
    private static string Sanitize(string? error, string? smtpPassword)
    {
        var message = string.IsNullOrWhiteSpace(error) ? "Delivery failed." : error;
        if (!string.IsNullOrEmpty(smtpPassword))
            message = message.Replace(smtpPassword, "***");
        return message.Length > 1000 ? message[..1000] : message;
    }

    private static Dictionary<string, string>? DeserializeParams(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch { return null; }
    }

    private static string Esc(string? s) => (s ?? string.Empty).Replace("'", "\\'");
}
