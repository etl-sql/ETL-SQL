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

        // An SMTP connection whose stored credential is (marker) ciphertext.
        db.SmtpConnections.Add(new SmtpConnection
        {
            Alias = $"secret_smtp_{suffix}",
            Host = "smtp.test.local",
            Port = 587,
            Username = "mailer",
            EncryptedPassword = SmtpCiphertextMarker,
            FromAddress = "reports@test.local",
            UseSsl = true
        });

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
                     SmtpCiphertextMarker, PasswordHashMarker,
                     RefreshTokenMarker, ShareTokenMarker, EmbedTokenMarker
                 })
            Assert.DoesNotContain(marker, script);

        // The SMTP credential is still represented — as a substitution placeholder, not a value.
        Assert.Contains($"SMTP_SECRET_SMTP_{suffix.ToUpperInvariant()}_PASSWORD", script);
        Assert.Contains(export.RequiredSecrets, s => s.Contains(suffix.ToUpperInvariant()));

        // The user is emitted with a password placeholder, never the hash.
        Assert.Contains($"CREATE USER 'secret_user_{suffix}'", script);
        Assert.Contains($"PORTAL_USER_SECRET_USER_{suffix.ToUpperInvariant()}_PASSWORD", script);
        Assert.DoesNotContain("SigningCertThumbprint", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("policy-signing certificate thumbprints and private keys", script,
            StringComparison.OrdinalIgnoreCase);
    }
}
