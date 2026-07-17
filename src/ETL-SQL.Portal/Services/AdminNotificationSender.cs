using System.Text;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed record AdminNotification(
    string SmtpAlias,
    string? SenderOverride,
    string Recipients,
    string Subject,
    string Body,
    string SessionPrefix);

/// <summary>Delivery seam for admin digests; the default sends email via a stored SMTP connection.</summary>
public interface IAdminNotificationSender
{
    Task<(bool Success, string? Error)> SendAsync(AdminNotification notification, CancellationToken ct);
}

/// <summary>
/// Sends an admin notification by composing a SEND EMAIL script against a stored
/// <see cref="SmtpConnection"/> (selected by alias) and running it in-process, so the SMTP
/// credential never leaves the portal.
/// </summary>
public sealed class SmtpAdminNotificationSender(
    PortalDbContext db,
    SmtpPasswordProtector passwordProtector,
    ISubscriptionScriptRunner runner) : IAdminNotificationSender
{
    public async Task<(bool Success, string? Error)> SendAsync(AdminNotification notification, CancellationToken ct)
    {
        var smtp = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Alias == notification.SmtpAlias, ct);
        if (smtp is null)
            return (false, $"SMTP alias '{notification.SmtpAlias}' does not exist.");

        var password = passwordProtector.Unprotect(smtp.EncryptedPassword);
        if (!string.IsNullOrEmpty(smtp.EncryptedPassword) && password is null)
            return (false, "The SMTP credential could not be resolved with this node's key ring.");

        var fromAddr = !string.IsNullOrWhiteSpace(notification.SenderOverride)
            ? notification.SenderOverride
            : smtp.FromAddress ?? smtp.Username ?? "etlsql@localhost";

        var script = ComposeScript(smtp, password, fromAddr, notification);
        var (success, error) = await runner.RunAsync(script, $"{notification.SessionPrefix}-{Guid.NewGuid():N}", ct);
        if (success)
            return (true, null);

        var safe = ETL_SQL.Core.Common.SecretRedactor.Redact(
            (error ?? "unknown error").Replace(password ?? "\0", "***")) ?? "delivery failed";
        return (false, safe);
    }

    private static string ComposeScript(SmtpConnection smtp, string? password, string fromAddr, AdminNotification n)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CREATE CONNECTION __admin_notify_smtp AS SMTP(");
        sb.AppendLine($"    HOST     = '{Esc(smtp.Host)}',");
        sb.AppendLine($"    PORT     = {smtp.Port},");
        if (!string.IsNullOrEmpty(smtp.Username))
            sb.AppendLine($"    USERNAME = '{Esc(smtp.Username)}',");
        if (!string.IsNullOrEmpty(password))
            sb.AppendLine($"    PASSWORD = '{Esc(password)}',");
        sb.AppendLine($"    USE_SSL  = '{smtp.UseSsl.ToString().ToLowerInvariant()}'");
        sb.AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("SEND EMAIL");
        sb.AppendLine($"    TO      '{Esc(n.Recipients)}'");
        sb.AppendLine($"    FROM    '{Esc(fromAddr)}'");
        sb.AppendLine($"    SUBJECT '{Esc(n.Subject)}'");
        sb.AppendLine($"    BODY    '{Esc(n.Body)}'");
        sb.AppendLine("    AT __admin_notify_smtp;");
        return sb.ToString();
    }

    // The lexer escapes a quote by doubling it ('' -> '); newlines inside a string literal are kept
    // verbatim, so multi-line bodies need no special handling.
    private static string Esc(string? s) => (s ?? string.Empty).Replace("'", "''");
}
