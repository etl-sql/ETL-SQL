using System.Text.Json;
using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Seeds an SMTP entry in the governed connection catalog, which replaced the bespoke
/// <c>SmtpConnections</c> table.
/// </summary>
/// <remarks>
/// The password is seeded as a <c>SECRET:</c> reference rather than ciphertext. That is the point
/// of the catalog — it stores references, and delivery copies the reference into the generated
/// script for the engine to resolve — so a test seeding a literal or an encrypted value would be
/// asserting against a shape the catalog rejects on write.
/// </remarks>
internal static class SmtpCatalogSeed
{
    public static PortalSharedConnection Add(
        PortalDbContext db,
        string alias,
        string host = "smtp.test.local",
        int port = 2525,
        string? username = null,
        string? defaultFrom = "portal@test.local",
        bool useSsl = false,
        string? passwordSecretRef = "SECRET:smtp_password")
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HOST"] = host,
            ["PORT"] = port.ToString(),
            ["USE_SSL"] = useSsl ? "true" : "false"
        };
        if (!string.IsNullOrWhiteSpace(username)) options["USERNAME"] = username;
        if (!string.IsNullOrWhiteSpace(defaultFrom)) options["DEFAULT_FROM"] = defaultFrom;
        if (!string.IsNullOrWhiteSpace(passwordSecretRef)) options["PASSWORD"] = passwordSecretRef;

        var entry = new PortalSharedConnection
        {
            Alias = alias,
            ConnectorType = "SMTP",
            OptionsJson = JsonSerializer.Serialize(options),
            SensitiveFieldsCsv = "PASSWORD"
        };
        db.PortalSharedConnections.Add(entry);
        return entry;
    }
}
