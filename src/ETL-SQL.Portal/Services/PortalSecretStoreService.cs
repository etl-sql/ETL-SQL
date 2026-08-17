using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Security;
using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed record PortalSecretSummary(
    string Name,
    bool Disabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int? CreatedByUserId,
    int? UpdatedByUserId,
    long Version);

/// <summary>Result of a decrypt-probe over every stored secret; values are never surfaced.</summary>
public sealed record SecretKeyRingCheckResult(
    int SecretCount,
    int FailedCount,
    string? FirstFailedName,
    string? FirstFailureReason);

/// <summary>
/// Portal-managed encrypted secret store for SME/HA deployments that do not operate an external vault.
/// Plaintext values are accepted only on write/verify/resolve paths and are never returned by list APIs.
/// </summary>
public sealed class PortalSecretStoreService
{
    private const string ProtectedPrefix = "dp:";
    private const string ProviderPrefix = "km1:";
    private readonly PortalDbContext db;
    private readonly IDataProtector protector;
    private readonly PortalConfig? config;
    private readonly IKeyMaterialProvider? keyProvider;
    private readonly string tenantScope;

    public PortalSecretStoreService(
        PortalDbContext db,
        IDataProtectionProvider dataProtection,
        PortalConfig? config = null,
        IKeyMaterialProvider? keyProvider = null,
        TenantContext? tenantContext = null)
    {
        this.db = db;
        protector = dataProtection.CreateProtector("ETL_SQL.Portal.SecretStore.v1");
        this.config = config;
        this.keyProvider = keyProvider;
        tenantScope = ResolveTenantScope(tenantContext, config);
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
        var encrypted = await ProtectAsync(value, cancellationToken);
        var existing = await db.PortalSecrets
            .SingleOrDefaultAsync(secret => secret.TenantId == TenantScope && secret.Name == name, cancellationToken);

        if (existing == null)
        {
            db.PortalSecrets.Add(new PortalSecret
            {
                TenantId = TenantScope,
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
            .SingleOrDefaultAsync(item => item.TenantId == TenantScope && item.Name == name, cancellationToken)
            ?? throw new InvalidOperationException($"Portal secret '{name}' does not exist.");

        if (secret.Disabled)
            throw new InvalidOperationException($"Portal secret '{name}' is disabled.");

        return await UnprotectAsync(secret.EncryptedValue, cancellationToken);
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
            .SingleOrDefaultAsync(item => item.TenantId == TenantScope && item.Name == name, cancellationToken)
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

    public async Task EnableAsync(
        string name,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        name = NormalizeName(name);
        var secret = await db.PortalSecrets
            .SingleOrDefaultAsync(item => item.TenantId == TenantScope && item.Name == name, cancellationToken)
            ?? throw new InvalidOperationException($"Portal secret '{name}' does not exist.");

        if (secret.Disabled)
        {
            secret.Disabled = false;
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
            .SingleOrDefaultAsync(item => item.TenantId == TenantScope && item.Name == name, cancellationToken)
            ?? throw new InvalidOperationException($"Portal secret '{name}' does not exist.");

        db.PortalSecrets.Remove(secret);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SecretLifecycleStatus> GetStatusAsync(string name, CancellationToken cancellationToken = default)
    {
        name = NormalizeName(name);
        var disabled = await db.PortalSecrets
            .AsNoTracking()
            .Where(item => item.TenantId == TenantScope && item.Name == name)
            .Select(item => (bool?)item.Disabled)
            .SingleOrDefaultAsync(cancellationToken);

        return disabled switch
        {
            null => SecretLifecycleStatus.NotFound,
            true => SecretLifecycleStatus.Disabled,
            false => SecretLifecycleStatus.Active
        };
    }

    /// <summary>
    /// Proves every stored secret (including disabled ones) is decryptable with this node's key
    /// ring, without surfacing any value. Used by the key-ring health check on HA nodes and by
    /// verify-all after a backup/restore.
    /// </summary>
    public async Task<SecretKeyRingCheckResult> CheckKeyRingAsync(CancellationToken cancellationToken = default)
    {
        var secrets = await db.PortalSecrets
            .AsNoTracking()
            .Where(secret => secret.TenantId == TenantScope)
            .OrderBy(secret => secret.Name)
            .Select(secret => new { secret.Name, secret.EncryptedValue })
            .ToListAsync(cancellationToken);

        var failed = 0;
        string? firstFailedName = null;
        string? firstFailureReason = null;
        foreach (var secret in secrets)
        {
            try
            {
                _ = await UnprotectAsync(secret.EncryptedValue, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                failed++;
                firstFailedName ??= secret.Name;
                firstFailureReason ??= ex.Message;
            }
        }

        return new SecretKeyRingCheckResult(secrets.Count, failed, firstFailedName, firstFailureReason);
    }

    public async Task<IReadOnlyList<PortalSecretSummary>> ListAsync(CancellationToken cancellationToken = default)
        => await db.PortalSecrets
            .AsNoTracking()
            .Where(secret => secret.TenantId == TenantScope)
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

    private async Task<string> ProtectAsync(string value, CancellationToken cancellationToken)
    {
        if (keyProvider is null) return ProtectedPrefix + protector.Protect(value);

        using var lease = await keyProvider.ResolveAsync(
            new KeyMaterialRequest(KeyScope, KeyPurpose.Credential), cancellationToken);
        var key = SHA256.HashData(lease.Bytes.Span);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var version = Encoding.UTF8.GetBytes(lease.Descriptor.Version);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, version);
        return $"{ProviderPrefix}{lease.Descriptor.Version}:" +
            Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
    }

    private async Task<string> UnprotectAsync(string encrypted, CancellationToken cancellationToken)
    {
        if (encrypted.StartsWith(ProviderPrefix, StringComparison.Ordinal))
        {
            if (keyProvider is null)
                throw new InvalidOperationException(
                    "Portal secret payload requires the configured key-material provider.");
            var separator = encrypted.IndexOf(':', ProviderPrefix.Length);
            if (separator <= ProviderPrefix.Length)
                throw new InvalidOperationException("Portal secret payload has an invalid provider envelope.");
            var version = encrypted[ProviderPrefix.Length..separator];
            byte[] payload;
            try { payload = Convert.FromBase64String(encrypted[(separator + 1)..]); }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Portal secret payload has an invalid provider envelope.", ex);
            }
            if (payload.Length < 28)
                throw new InvalidOperationException("Portal secret payload has an invalid provider envelope.");
            using var lease = await keyProvider.ResolveAsync(
                new KeyMaterialRequest(KeyScope, KeyPurpose.Credential, version), cancellationToken);
            var plaintext = new byte[payload.Length - 28];
            try
            {
                using var aes = new AesGcm(SHA256.HashData(lease.Bytes.Span), 16);
                aes.Decrypt(payload[..12], payload[28..], payload[12..28], plaintext,
                    Encoding.UTF8.GetBytes(version));
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Portal secret payload cannot be decrypted with the configured credential key.", ex);
            }
        }

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

    private string TenantScope => tenantScope;

    private static string ResolveTenantScope(TenantContext? context, PortalConfig? config)
    {
        if (context is not null) return context.Tenant.Value;
        if (config?.SharedTenancy.Enabled == true)
            throw new UnauthorizedAccessException(
                "Shared secret-store access requires a server-verified tenant context.");
        return string.IsNullOrWhiteSpace(config?.TenantId) ? "portal-host" : config.TenantId;
    }

    private string KeyScope => TenantScope;

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Secret name is required.", nameof(name));

        name = name.Trim();
        SecretNameValidator.Validate(name);
        return name;
    }
}
