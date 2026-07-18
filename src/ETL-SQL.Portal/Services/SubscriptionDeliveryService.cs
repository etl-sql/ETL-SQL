using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

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
    Task<(bool Success, string? Error)> RunAsync(string scriptText, string sessionId, CancellationToken ct,
        ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null);
}

public sealed class EngineSubscriptionScriptRunner(IScriptExecutor executor) : ISubscriptionScriptRunner
{
    public async Task<(bool Success, string? Error)> RunAsync(string scriptText, string sessionId, CancellationToken ct,
        ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null)
    {
        var result = await executor.ExecuteTextAsync(scriptText, sessionId, ct, executionIdentity: executionIdentity);
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
///
/// <para><b>Delivery semantics: at-most-once per recipient and scheduler trigger.</b> Every delivery is
/// claimed in the durable <see cref="SubscriptionDelivery"/> ledger keyed on
/// <c>(SubscriptionId, TriggerKey, RecipientKey)</c>; a duplicate trigger (poller re-observation,
/// scheduler double-fire) is suppressed without re-sending that recipient. The portal never records <c>Delivered</c> unless
/// the in-process runner reports success, so it errs toward recording a failure rather than a false
/// success. The one caveat is SMTP itself: a timeout after the SMTP server has already accepted a
/// message can leave the recipient with a copy the portal records as <c>Failed</c> — at the wire
/// that is at-least-once. The ledger makes every attempt and its outcome observable.</para>
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
    /// <summary>Ad-hoc (manual) delivery — each call is a distinct trigger and is never deduped
    /// against another. Scheduled deliveries pass the completion's identity as the trigger key.</summary>
    public Task<SubscriptionDeliveryResult> DeliverAsync(int subscriptionId, CancellationToken ct = default)
        => DeliverAsync(subscriptionId, $"manual:{Guid.NewGuid():N}", ct);

    public async Task<SubscriptionDeliveryResult> DeliverAsync(
        int subscriptionId, string triggerKey, CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions
            .Where(s => s.Id == subscriptionId)
            .Select(s => new { s.Recipients })
            .FirstOrDefaultAsync(ct);
        if (subscription is null)
            return SubscriptionDeliveryResult.Skipped("Subscription no longer exists.");

        var recipients = NormalizeRecipients(subscription.Recipients);
        var results = new List<SubscriptionDeliveryResult>();
        foreach (var recipient in recipients)
        {
            var recipientKey = RecipientKey(recipient.Value);
            var (ledger, claimed) = await TryClaimAsync(
                subscriptionId, triggerKey, recipientKey, recipient.Value, ct);
            if (!claimed)
                continue;

            SubscriptionDeliveryResult result;
            try
            {
                result = recipient.IsValid
                    ? await ExecuteDeliveryAsync(subscriptionId, recipient.Value, ledger.DeliveryId, ct)
                    : SubscriptionDeliveryResult.Failed("Recipient address is invalid.");
            }
            catch (Exception ex)
            {
                // Unknown outcome: record it against this recipient and never re-claim the same
                // trigger. A later, distinct trigger may retry independently.
                result = SubscriptionDeliveryResult.Failed(Sanitize(ex.Message, null));
            }

            ledger.Outcome = result.Outcome.ToString();
            ledger.Detail = result.Reason;
            ledger.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            results.Add(result);
        }

        if (results.Count == 0)
            return SubscriptionDeliveryResult.Skipped(
                "Duplicate trigger — delivery outcomes already exist for every recipient.");

        var tracked = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (tracked is not null)
        {
            if (results.Any(result => result.Outcome == SubscriptionDeliveryOutcome.Delivered))
                tracked.LastSentAt = DateTime.UtcNow;
            if (results.Any(result => result.Outcome == SubscriptionDeliveryOutcome.Failed))
                tracked.FailCount++;
            else if (results.All(result => result.Outcome == SubscriptionDeliveryOutcome.Delivered))
                tracked.FailCount = 0;
            await db.SaveChangesAsync(ct);
        }

        return Aggregate(results);
    }

    /// <summary>Inserts the InProgress ledger row, or reports the claim lost when the trigger is a
    /// duplicate (unique-index race included).</summary>
    private async Task<(SubscriptionDelivery Ledger, bool Claimed)> TryClaimAsync(
        int subscriptionId,
        string triggerKey,
        string recipientKey,
        string recipient,
        CancellationToken ct)
    {
        if (await db.SubscriptionDeliveries
                .AnyAsync(d => d.SubscriptionId == subscriptionId
                    && d.TriggerKey == triggerKey
                    && d.RecipientKey == recipientKey, ct))
            return (null!, false);

        var ledger = new SubscriptionDelivery
        {
            DeliveryId = $"delivery-{Guid.NewGuid():N}",
            SubscriptionId = subscriptionId,
            TriggerKey = triggerKey,
            RecipientKey = recipientKey,
            Outcome = "InProgress",
            Recipients = recipient,
            StartedAt = DateTime.UtcNow
        };
        db.SubscriptionDeliveries.Add(ledger);
        try
        {
            await db.SaveChangesAsync(ct);
            return (ledger, true);
        }
        catch (DbUpdateException)
        {
            db.Entry(ledger).State = EntityState.Detached;
            return (null!, false);
        }
    }

    private async Task<SubscriptionDeliveryResult> ExecuteDeliveryAsync(
        int subscriptionId,
        string recipient,
        string correlationId,
        CancellationToken ct)
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
                sub.Id.ToString(), denial, correlationId);
            log.LogWarning("Subscription {SubscriptionId} delivery denied: {Reason}", sub.Id, denial);
            return SubscriptionDeliveryResult.Denied(denial);
        }

        if (!PortalPathGuard.TryResolveScript(config, sub.Report.ScriptPath, out var reportScriptPath))
            return await RecordFailureAsync(sub,
                recipient, "Report script path is outside the configured script root.", correlationId, ct);
        if (!File.Exists(reportScriptPath))
            return await RecordFailureAsync(
                sub, recipient, "Report script file no longer exists.", correlationId, ct);

        // Row-level security: an identity-sensitive report filters rows per viewer, so it must run
        // under *this recipient's* identity to produce their filtered view. Resolve the recipient
        // email to a portal user; if they are not a known user we cannot filter for them, so fail with
        // a clear reason rather than deliver an empty (fail-closed) report. See Docs/Design/RowLevelSecurity.md.
        ETL_SQL.Core.Governance.ExecutionIdentity? recipientIdentity = null;
        try
        {
            if (ETL_SQL.Core.Governance.RowLevelSecurityScan.ReferencesIdentity(
                    await File.ReadAllTextAsync(reportScriptPath, ct)))
            {
                recipientIdentity = await BuildRecipientIdentityAsync(recipient, ct);
                if (recipientIdentity is null)
                    return await RecordFailureAsync(sub, recipient,
                        "Report uses row-level security; recipient is not a known portal user, so their filtered view cannot be produced.",
                        correlationId, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await RecordFailureAsync(sub, recipient,
                "Report script could not be read for row-level-security evaluation.", correlationId, ct);
        }

        SmtpConnection? smtp = null;
        if (!string.IsNullOrEmpty(sub.SmtpAlias))
        {
            smtp = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Alias == sub.SmtpAlias, ct);
            if (smtp is null)
                return await RecordFailureAsync(sub,
                    recipient, $"SMTP connection '{sub.SmtpAlias}' no longer exists.", correlationId, ct);
        }
        else if (sub.Format != SubscriptionFormat.Link)
        {
            return await RecordFailureAsync(sub,
                recipient, "Subscription has no SMTP alias for attachment delivery.", correlationId, ct);
        }
        else
        {
            return SubscriptionDeliveryResult.Skipped(
                "Link subscription has no SMTP alias — nothing to deliver.");
        }

        var smtpPassword = pwdProtector.Unprotect(smtp.EncryptedPassword);
        if (!string.IsNullOrEmpty(smtp.EncryptedPassword) && smtpPassword is null)
            return await RecordFailureAsync(
                sub, recipient, "SMTP credential could not be resolved.", correlationId, ct);

        string? exportPath = null;
        try
        {
            var script = ComposeDeliveryScript(
                sub, recipient, reportScriptPath, smtp, smtpPassword, out exportPath);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, config.Resources.ExecutionTimeoutSeconds)));

            bool success;
            string? error;
            try
            {
                (success, error) = await runner.RunAsync(
                    script, $"sub-delivery-{sub.Id}-{RecipientKey(recipient)[..12]}", cts.Token, recipientIdentity);
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
                return await RecordFailureAsync(
                    sub, recipient, Sanitize(error, smtpPassword), correlationId, ct);

            // Recipient outcome and its audit record share one commit. The address is represented
            // by a fingerprint in operational records; the delivery ledger retains the address for
            // authorized history views.
            audit.Stage(sub.UserId, "SUBSCRIPTION_DELIVERED", "Subscription",
                sub.Id.ToString(),
                $"Report {sub.ReportId} ({sub.Format}); RecipientKey={RecipientKey(recipient)}",
                correlationId);
            await db.SaveChangesAsync(ct);
            log.LogInformation(
                "Subscription {SubscriptionId} delivered to one recipient.", sub.Id);
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

    /// <summary>
    /// Resolves a subscription recipient email to the portal user's row-level-security identity so an
    /// identity-sensitive report can be delivered as that recipient's own filtered view. Returns null
    /// when the email is not a known portal user (an external recipient we cannot filter for).
    /// </summary>
    private async Task<ETL_SQL.Core.Governance.ExecutionIdentity?> BuildRecipientIdentityAsync(
        string recipientEmail, CancellationToken ct)
    {
        var target = recipientEmail.Trim();

        // Email is stored PII-encrypted (non-deterministic), so it cannot be queried by value.
        // Materialize active users and compare the decrypted email in memory. This is O(users) per
        // resolution — acceptable for low-frequency subscription delivery; a deterministic email-hash
        // index would be the optimization if that ever changes.
        var candidates = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new { u.Id, u.UserName, u.Email })
            .ToListAsync(ct);
        var match = candidates.FirstOrDefault(u =>
            !string.IsNullOrEmpty(u.Email)
            && string.Equals(u.Email, target, StringComparison.OrdinalIgnoreCase));
        if (match is null) return null;

        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           where ur.UserId == match.Id && r.Name != null
                           select r.Name!).ToListAsync(ct);
        var groups = await (from ug in db.UserGroups
                            join g in db.Groups on ug.GroupId equals g.Id
                            where ug.UserId == match.Id
                            select g.Name).ToListAsync(ct);

        var name = match.UserName ?? match.Id.ToString();
        return new ETL_SQL.Core.Governance.ExecutionIdentity
        {
            EffectiveUser = name,
            EffectiveUserId = match.Id,
            RealUser = name,
            IsAdmin = roles.Contains("Admin", StringComparer.OrdinalIgnoreCase),
            AdminBypassesRowLevelSecurity = config.Security.AdminBypassRowLevelSecurity,
            Groups = groups,
            Roles = roles
        };
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
        var permission = await folderPermissions.GetEffectivePermissionAsync(sub.Report.FolderId, groupIds, owner.Id);
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
        string recipient,
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
            sb.AppendLine($"    TO      '{Esc(recipient)}'");
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
            sb.AppendLine($"    TO      '{Esc(recipient)}'");
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
        Subscription sub,
        string recipient,
        string reason,
        string correlationId,
        CancellationToken ct)
    {
        // Redact the recipient and any embedded secret (e.g. an SMTP password echoed back
        // in a transport error) before this reason is audited or logged.
        var safeReason = ETL_SQL.Core.Common.SecretRedactor.Redact(
            reason.Replace(
                recipient,
                "[recipient]",
                StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        audit.Stage(sub.UserId, "SUBSCRIPTION_DELIVERY_FAILED", "Subscription",
            sub.Id.ToString(),
            $"RecipientKey={RecipientKey(recipient)}; {safeReason}",
            correlationId);
        await db.SaveChangesAsync(ct);
        log.LogWarning(
            "Subscription {SubscriptionId} delivery failed for one recipient: {Reason}",
            sub.Id,
            safeReason);
        return SubscriptionDeliveryResult.Failed(safeReason);
    }

    private sealed record NormalizedRecipient(string Value, bool IsValid);

    private static IReadOnlyList<NormalizedRecipient> NormalizeRecipients(string recipients)
    {
        var result = new List<NormalizedRecipient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in recipients.Split(
                     [';', ','],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (MailAddress.TryCreate(raw, out var parsed))
            {
                var normalized = parsed.Address.Trim().ToLowerInvariant();
                if (seen.Add(normalized))
                    result.Add(new(normalized, true));
            }
            else
            {
                var invalid = raw.Trim();
                if (seen.Add(invalid))
                    result.Add(new(invalid, false));
            }
        }
        if (result.Count == 0)
            result.Add(new("(missing)", false));
        return result;
    }

    private static string RecipientKey(string recipient) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipient.ToLowerInvariant())));

    private static SubscriptionDeliveryResult Aggregate(
        IReadOnlyCollection<SubscriptionDeliveryResult> results)
    {
        var delivered = results.Count(result => result.Outcome == SubscriptionDeliveryOutcome.Delivered);
        var failed = results.Count(result => result.Outcome == SubscriptionDeliveryOutcome.Failed);
        var denied = results.Count(result => result.Outcome == SubscriptionDeliveryOutcome.Denied);
        if (failed > 0)
            return SubscriptionDeliveryResult.Failed(
                $"{delivered} recipient(s) delivered; {failed} failed; {denied} denied.");
        if (denied > 0)
            return SubscriptionDeliveryResult.Denied(
                $"{delivered} recipient(s) delivered; {denied} denied.");
        if (delivered > 0)
            return SubscriptionDeliveryResult.Delivered();
        return SubscriptionDeliveryResult.Skipped("No recipient required delivery.");
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

    // The ETL-SQL lexer escapes a quote inside a string literal by doubling it ('' -> '), not with a
    // backslash. A backslash-escaped quote would terminate the string early, so a value containing a
    // single quote (e.g. an SMTP username or report name) must be doubled.
    private static string Esc(string? s) => (s ?? string.Empty).Replace("'", "''");
}
