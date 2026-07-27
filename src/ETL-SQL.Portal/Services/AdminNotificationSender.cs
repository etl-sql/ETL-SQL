using System.Text;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Services;

public sealed record AdminNotification(
    string SmtpAlias,
    string? SenderOverride,
    string Recipients,
    string Subject,
    string Body,
    string SessionPrefix);

/// <summary>Delivery seam for admin digests; the default sends email via a cataloged SMTP connection.</summary>
public interface IAdminNotificationSender
{
    Task<(bool Success, string? Error)> SendAsync(AdminNotification notification, CancellationToken ct);
}

/// <summary>
/// Sends an admin notification by composing a SEND EMAIL script against a governed SMTP connection
/// from the Portal catalog (selected by alias) and running it in-process.
/// </summary>
/// <remarks>
/// The credential is never materialised in the Portal process. Catalog options hold
/// <c>SECRET:name</c> references rather than values, and those references are copied verbatim into
/// the generated script for the engine to resolve at connection time. That is why this type no
/// longer takes a <c>SmtpPasswordProtector</c>: there is no ciphertext for it to unprotect, and
/// decrypting here would put a plaintext password in Portal memory for no benefit.
/// </remarks>
public sealed class SmtpAdminNotificationSender(
    PortalConnectionCatalogService catalog,
    ISubscriptionScriptRunner runner) : IAdminNotificationSender
{
    public async Task<(bool Success, string? Error)> SendAsync(AdminNotification notification, CancellationToken ct)
    {
        SharedConnectionDefinition definition;
        try
        {
            definition = await catalog.ResolveDefinitionAsync(notification.SmtpAlias, identity: null, ct);
        }
        catch (KeyNotFoundException)
        {
            return (false, $"SMTP alias '{notification.SmtpAlias}' does not exist in the connection catalog.");
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }

        if (!definition.ConnectorType.Equals("SMTP", StringComparison.OrdinalIgnoreCase))
            return (false,
                $"Connection '{notification.SmtpAlias}' is a {definition.ConnectorType} connection, not SMTP.");

        var options = new Dictionary<string, string>(definition.Options, StringComparer.OrdinalIgnoreCase);

        var fromAddr = !string.IsNullOrWhiteSpace(notification.SenderOverride)
            ? notification.SenderOverride
            : Option(options, "DEFAULT_FROM") ?? Option(options, "USERNAME") ?? "etlsql@localhost";

        var script = ComposeScript(options, fromAddr, notification);
        var (success, error) = await runner.RunAsync(script, $"{notification.SessionPrefix}-{Guid.NewGuid():N}", ct);
        if (success)
            return (true, null);

        return (false, ETL_SQL.Core.Common.SecretRedactor.Redact(error ?? "unknown error") ?? "delivery failed");
    }

    private static string ComposeScript(
        IReadOnlyDictionary<string, string> options, string fromAddr, AdminNotification n)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CREATE CONNECTION __admin_notify_smtp AS SMTP(");

        var lines = new List<string>();
        foreach (var key in new[] { "HOST", "PORT", "USERNAME", "PASSWORD", "USE_SSL" })
        {
            if (Option(options, key) is not { } value) continue;
            // PORT is numeric in the connector's option set; everything else is a quoted literal,
            // and a SECRET:name reference is a quoted literal the engine resolves on connect.
            lines.Add(key == "PORT"
                ? $"    {key} = {value}"
                : $"    {key} = '{Esc(value)}'");
        }

        sb.AppendLine(string.Join("," + Environment.NewLine, lines));
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

    private static string? Option(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    // The lexer escapes a quote by doubling it ('' -> '); newlines inside a string literal are kept
    // verbatim, so multi-line bodies need no special handling.
    private static string Esc(string? s) => (s ?? string.Empty).Replace("'", "''");
}
