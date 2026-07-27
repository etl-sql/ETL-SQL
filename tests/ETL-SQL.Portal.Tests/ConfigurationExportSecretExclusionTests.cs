using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// P1.8: the configuration export must never leak secret or ephemeral security material — password
/// hashes, encrypted credentials, refresh tokens, or share/embed capability tokens. Real secret
/// values are seeded directly, then the generated bootstrap is scanned to prove none appear, while
/// the credential is still represented by a ${...} placeholder for substitution at import.
/// </summary>
[Trait("Category", "Portal")]
public sealed class ConfigurationExportSecretExclusionTests
{
    private const string SmtpCiphertextMarker = "SMTP-CIPHERTEXT-MARKER-9f3a";
    private const string PasswordHashMarker = "PASSWORD-HASH-MARKER-7c21";
    private const string RefreshTokenMarker = "REFRESH-TOKEN-MARKER-be40";
    private const string ShareTokenMarker = "SHARE-TOKEN-MARKER-13dd";
    private const string EmbedTokenMarker = "EMBED-TOKEN-MARKER-aa52";

    [Fact]
    public async Task Export_ExcludesAllSecretAndSecurityArtifacts()
    {
        using var factory = new PortalWebFactory();
        _ = factory.CreateClient(); // trigger migrations + first-run seed
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        // A user carrying a (marker) password hash.
        var user = new PortalUser
        {
            UserName = $"secret_user_{suffix}",
            Email = $"secret_{suffix}@test.local",
            IsActive = true,
            PasswordHash = PasswordHashMarker
        };
        db.Users.Add(user);

        // An SMTP connection in the governed catalog. It holds a SECRET: reference rather than a
        // credential, so there is no ciphertext for the export to leak — the exposure this case
        // used to guard has been designed out rather than merely tested for.
        SmtpCatalogSeed.Add(db, $"secret_smtp_{suffix}", port: 587, username: "mailer",
            defaultFrom: "reports@test.local", useSsl: true);

        var folder = new Folder { Name = $"sf_{suffix}", Path = $"/sf_{suffix}", OwnerId = user.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var report = new Report
        {
            FolderId = folder.Id,
            Name = $"Secret Report {suffix}",
            ScriptPath = $"reports/secret_{suffix}.rptsql",
            ScriptLastModified = DateTime.UtcNow,
            CreatedBy = user.Id
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        // Ephemeral security capabilities — none of these are configuration.
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = RefreshTokenMarker,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        db.ReportShareLinks.Add(new ReportShareLink
        {
            ReportId = report.Id,
            CreatedBy = user.Id,
            Token = ShareTokenMarker,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        db.ReportEmbedTokens.Add(new ReportEmbedToken
        {
            ReportId = report.Id,
            CreatedBy = user.Id,
            Name = "Wallboard",
            Token = EmbedTokenMarker,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var exporter = scope.ServiceProvider.GetRequiredService<ConfigurationExportService>();
        var export = await exporter.GenerateAsync();
        var script = export.Script;

        // Not one byte of secret or capability material may appear in the export.
        foreach (var marker in new[]
                 {
                     PasswordHashMarker,
                     RefreshTokenMarker, ShareTokenMarker, EmbedTokenMarker
                 })
            Assert.DoesNotContain(marker, script);

        // SmtpCiphertextMarker is no longer among them: the catalog stores no credential to leak.
        // The positive form of that guarantee — a reference is exported, never a value.
        Assert.Contains("PASSWORD = 'SECRET:", script);

        // Connections need no substitution placeholder: the catalog holds a SECRET: reference, so
        // the exported statement is already value-free and replayable as-is. Placeholders remain
        // for the things that really do hold values, such as user passwords below.

        // The user is emitted with a password placeholder, never the hash.
        Assert.Contains($"CREATE USER 'secret_user_{suffix}'", script);
        Assert.Contains($"PORTAL_USER_SECRET_USER_{suffix.ToUpperInvariant()}_PASSWORD", script);
        Assert.DoesNotContain("SigningCertThumbprint", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("policy-signing certificate thumbprints and private keys", script,
            StringComparison.OrdinalIgnoreCase);
    }
}
