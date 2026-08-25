using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Services;

public sealed class WorkloadIdentityException(string code, Exception? innerException = null) : Exception(code, innerException)
{
    public string Code { get; } = code;
}

public sealed record ValidatedWorkloadIdentity(
    WorkloadIdentityBindingConfig Binding,
    string TokenId,
    DateTime ValidFromUtc,
    DateTime ValidToUtc);

public interface IWorkloadIdentityFederationService
{
    Task<ValidatedWorkloadIdentity> ValidateAsync(
        string assertion, string audience, string resource, string operation,
        string? approvalReference, CancellationToken ct = default);
}

public interface IWorkloadIdentityApprovalService
{
    string Issue(WorkloadIdentityBindingConfig binding, int approvedByUserId);
    Task ValidateAsync(WorkloadIdentityBindingConfig binding, string? token, CancellationToken ct);
}

public interface IWorkloadIdentitySigningKeyProvider
{
    Task<IEnumerable<SecurityKey>> GetAsync(WorkloadIdentityBindingConfig binding, CancellationToken ct);
}

/// <summary>Durable one-use assertion registry shared by every Portal node.</summary>
public interface IWorkloadIdentityReplayStore
{
    Task<bool> TryUseAsync(string tenantId, string bindingId, string tokenId,
        DateTime expiresUtc, CancellationToken ct);
}

public sealed class WorkloadIdentityReplayCache(PortalDbContext db, TimeProvider timeProvider)
    : IWorkloadIdentityReplayStore
{
    public async Task<bool> TryUseAsync(
        string tenantId, string bindingId, string tokenId, DateTime expiresUtc, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await db.WorkloadIdentityReplays.Where(value => value.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        var row = new WorkloadIdentityReplay
        {
            TenantId = tenantId,
            BindingId = bindingId,
            TokenIdHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenId))),
            ExpiresAt = expiresUtc,
            UsedAt = now
        };
        db.WorkloadIdentityReplays.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            return false;
        }
    }
}

public sealed class WorkloadIdentityFederationService(
    PortalConfig config,
    IWorkloadIdentitySigningKeyProvider signingKeys,
    IWorkloadIdentityReplayStore replay,
    IWorkloadIdentityApprovalService approvals,
    TimeProvider timeProvider) : IWorkloadIdentityFederationService
{
    public async Task<ValidatedWorkloadIdentity> ValidateAsync(
        string assertion, string audience, string resource, string operation,
        string? approvalReference, CancellationToken ct = default)
    {
        var policy = config.Identity.WorkloadIdentity;
        if (!policy.Enabled || string.IsNullOrWhiteSpace(assertion))
            throw new WorkloadIdentityException("invalid_workload_assertion");

        JsonWebToken untrusted;
        try { untrusted = new JsonWebToken(assertion); }
        catch (Exception) { throw new WorkloadIdentityException("invalid_workload_assertion"); }

        var candidates = policy.Bindings.Where(binding =>
            binding.Enabled
            && FixedEquals(binding.Issuer, untrusted.Issuer)
            && FixedEquals(binding.Subject, untrusted.Subject)
            && FixedEquals(binding.Audience, audience)
            && FixedEquals(binding.Resource, resource)
            && binding.Operations.Contains(operation, StringComparer.Ordinal)
            && untrusted.Audiences.Contains(binding.Audience, StringComparer.Ordinal))
            .ToArray();
        if (candidates.Length != 1)
            throw new WorkloadIdentityException("workload_policy_denied");

        var binding = candidates[0];
        ValidateProvider(binding);
        var keys = await signingKeys.GetAsync(binding, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var skew = TimeSpan.FromSeconds(Math.Clamp(policy.ClockSkewSeconds, 0, 120));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(assertion, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = binding.Issuer,
            ValidateAudience = true,
            ValidAudience = binding.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = skew
        });
        if (!result.IsValid)
            throw new WorkloadIdentityException("invalid_workload_assertion", result.Exception);

        var identity = result.ClaimsIdentity;
        var tokenId = identity.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var issuedAt = ReadEpoch(identity, JwtRegisteredClaimNames.Iat);
        var expires = ReadEpoch(identity, JwtRegisteredClaimNames.Exp);
        var maxLifetime = TimeSpan.FromSeconds(Math.Clamp(policy.MaximumAssertionLifetimeSeconds, 60, 600));
        if (string.IsNullOrWhiteSpace(tokenId) || issuedAt is null || expires is null
            || issuedAt > now + skew || expires <= now - skew || expires - issuedAt > maxLifetime)
            throw new WorkloadIdentityException("invalid_workload_lifetime");
        if (!await replay.TryUseAsync(binding.TenantId, binding.Id, tokenId, expires.Value, ct))
            throw new WorkloadIdentityException("workload_replay_rejected");
        await approvals.ValidateAsync(binding, approvalReference, ct);

        return new(binding, tokenId, issuedAt.Value, expires.Value);
    }

    private static void ValidateProvider(WorkloadIdentityBindingConfig binding)
    {
        if (!Uri.TryCreate(binding.Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme != Uri.UriSchemeHttps)
            throw new WorkloadIdentityException("invalid_workload_issuer");
        var valid = binding.Provider.ToLowerInvariant() switch
        {
            "github" => binding.Issuer == "https://token.actions.githubusercontent.com",
            "gitlab" => true,
            "azure_devops" => issuer.Host == "vstoken.dev.azure.com",
            "private_key_jwt" => true,
            _ => false
        };
        if (!valid) throw new WorkloadIdentityException("unsupported_workload_provider");
        if (string.IsNullOrWhiteSpace(binding.Id) || string.IsNullOrWhiteSpace(binding.TenantId)
            || string.IsNullOrWhiteSpace(binding.ServiceAccountClientId)
            || binding.Operations.Length == 0)
            throw new WorkloadIdentityException("invalid_workload_policy");
    }

    private static DateTime? ReadEpoch(ClaimsIdentity identity, string name) =>
        long.TryParse(identity.FindFirst(name)?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;

    private static bool FixedEquals(string left, string right)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(left ?? "");
        var b = System.Text.Encoding.UTF8.GetBytes(right ?? "");
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}

public sealed class WorkloadIdentitySigningKeyProvider(IHttpClientFactory clients)
    : IWorkloadIdentitySigningKeyProvider
{
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _discovery =
        new(StringComparer.Ordinal);

    public async Task<IEnumerable<SecurityKey>> GetAsync(
        WorkloadIdentityBindingConfig binding, CancellationToken ct)
    {
        if (string.Equals(binding.Provider, "private_key_jwt", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(binding.PublicKeyPem))
                throw new WorkloadIdentityException("workload_key_not_configured");
            try
            {
                var rsa = RSA.Create();
                rsa.ImportFromPem(binding.PublicKeyPem);
                return [new RsaSecurityKey(rsa)];
            }
            catch (CryptographicException)
            {
                throw new WorkloadIdentityException("workload_key_not_configured");
            }
        }

        var manager = _discovery.GetOrAdd(binding.Issuer, issuer =>
            new ConfigurationManager<OpenIdConnectConfiguration>(
                issuer.TrimEnd('/') + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(clients.CreateClient(nameof(WorkloadIdentityFederationService)))
                { RequireHttps = true }));
        try { return (await manager.GetConfigurationAsync(ct)).SigningKeys; }
        catch (Exception) { throw new WorkloadIdentityException("workload_issuer_unavailable"); }
    }
}
