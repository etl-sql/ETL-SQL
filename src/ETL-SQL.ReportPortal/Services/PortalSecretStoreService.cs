using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.ReportPortal.Data;

namespace ETL_SQL.ReportPortal.Services;

public sealed record PortalSecretSummary(
    string Name,
    bool Disabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int? CreatedByUserId,
    int? UpdatedByUserId,
    long Version);

/// <summary>
/// Portal-managed encrypted secret store for SME/HA deployments that do not operate an external vault.
/// Plaintext values are accepted only on write/verify/resolve paths and are never returned by list APIs.
/// </summary>
public sealed class PortalSecretStoreService
{
    private const string ProtectedPrefix = "dp:";
    private readonly PortalDbContext db;
    private readonly IDataProtector protector;

    public PortalSecretStoreService(PortalDbContext db, IDataProtectionProvider dataProtection)
    {
        this.db = db;
        protector = dataProtection.CreateProtector("ETL_SQL.Portal.SecretStore.v1");
    }

    public async Task StoreAsync(
        string name,
        string value,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        name = NormalizeName(name);
        ArgumentException.ThrowIfNullOrEmpty(value);

        var now = DateTime.UtcNow;
        var encrypted = Protect(value);
        var existing = await db.PortalSecrets
            .SingleOrDefaultAsync(secret => secret.Name == name, cancellationToken);

        if (existing == null)
        {
            db.PortalSecrets.Add(new PortalSecret
            {
                Name = name,
                EncryptedValue = encrypted,
                Disabled = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                Version = 1
            });
        }
        else
        {
            existing.EncryptedValue = encrypted;
            existing.Disabled = false;
            existing.UpdatedAtUtc = now;
            existing.UpdatedByUserId = userId;
            existing.Version++;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        name = NormalizeName(name);
        var secret = await db.PortalSecrets
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Name == name, cancellationToken)
            ?? throw new InvalidOperationException($"Portal secret '{name}' does not exist.");

        if (secret.Disabled)
            throw new InvalidOperationException($"Portal secret '{name}' is disabled.");

        return Unprotect(secret.EncryptedValue);
    }

    public async Task<bool> VerifyAsync(string name, CancellationToken cancellationToken = default)
    {
        _ = await ResolveAsync(name, cancellationToken);
        return true;
    }

    public async Task DisableAsync(
        string name,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        name = NormalizeName(name);
        var secret = await db.PortalSecrets
            .SingleOrDefaultAsync(item => item.Name == name, cancellationToken)
            ?? throw new InvalidOperationException($"Portal secret '{name}' does not exist.");

        if (!secret.Disabled)
        {
            secret.Disabled = true;
            secret.UpdatedAtUtc = DateTime.UtcNow;
            secret.UpdatedByUserId = userId;
            secret.Version++;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        name = NormalizeName(name);
        var secret = await db.PortalSecrets
            .SingleOrDefaultAsync(item => item.Name == name, cancellationToken)
            ?? throw new InvalidOperationException($"Portal secret '{name}' does not exist.");

        db.PortalSecrets.Remove(secret);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PortalSecretSummary>> ListAsync(CancellationToken cancellationToken = default)
        => await db.PortalSecrets
            .AsNoTracking()
            .OrderBy(secret => secret.Name)
            .Select(secret => new PortalSecretSummary(
                secret.Name,
                secret.Disabled,
                secret.CreatedAtUtc,
                secret.UpdatedAtUtc,
                secret.CreatedByUserId,
                secret.UpdatedByUserId,
                secret.Version))
            .ToListAsync(cancellationToken);

    private string Protect(string value) => ProtectedPrefix + protector.Protect(value);

    private string Unprotect(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted) || !encrypted.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Portal secret payload is not encrypted with the expected store format.");

        try
        {
            return protector.Unprotect(encrypted[ProtectedPrefix.Length..]);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Portal secret payload cannot be decrypted with the configured Portal key ring.", ex);
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Secret name is required.", nameof(name));

        return name.Trim();
    }
}
