using System.Globalization;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed record SharedIdentityAuthorityDefinition(
    string PortalHost,
    string LoginDomain,
    string Issuer,
    string ClientId,
    string? ClientSecretReference,
    bool Enabled = true);

public sealed record SharedIdentityAuthorityBinding(
    string AuthorityId,
    string TenantId,
    string PortalHost,
    string LoginDomain,
    string Issuer,
    string ClientId,
    bool ClientSecretConfigured,
    long Version);

/// <summary>Tenant-scoped administration of shared identity-authority rows.</summary>
public sealed class SharedIdentityAuthorityService(
    PortalDbContext db,
    PortalConfig config,
    RequestTenantContextAccessor tenantAccessor)
{
    private readonly string _tenantId = RequireSharedTenant(config, tenantAccessor.RequireCurrent());

    public async Task<SharedIdentityAuthorityBinding> SetAsync(
        string authorityId,
        SharedIdentityAuthorityDefinition definition,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authorityId) || authorityId.Length > 64)
            throw new ArgumentException("A stable authority id of at most 64 characters is required.", nameof(authorityId));
        ArgumentNullException.ThrowIfNull(definition);

        var portalHost = NormalizeDomain(definition.PortalHost, nameof(definition.PortalHost));
        var loginDomain = NormalizeDomain(definition.LoginDomain, nameof(definition.LoginDomain));
        var issuer = NormalizeIssuer(definition.Issuer);
        var clientId = Require(definition.ClientId, nameof(definition.ClientId), 512);
        var secretReference = NormalizeSecretReference(definition.ClientSecretReference);

        var row = await db.SharedIdentityAuthorities.SingleOrDefaultAsync(
            x => x.TenantId == _tenantId && x.AuthorityId == authorityId, ct);
        if (row is null)
        {
            // Do not turn a foreign authority id into an update selector or disclose who owns it.
            if (await db.SharedIdentityAuthorities.AnyAsync(x => x.AuthorityId == authorityId, ct))
                throw new InvalidOperationException("The identity authority id is unavailable.");
            row = new SharedIdentityAuthority
            {
                AuthorityId = authorityId,
                TenantId = _tenantId,
                CreatedAtUtc = DateTime.UtcNow,
                Version = 0
            };
            db.SharedIdentityAuthorities.Add(row);
        }

        row.PortalHost = portalHost;
        row.LoginDomain = loginDomain;
        row.Issuer = issuer;
        row.ClientId = clientId;
        row.ClientSecretReference = secretReference;
        row.Enabled = definition.Enabled;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.Version++;
        await db.SaveChangesAsync(ct);
        return ToBinding(row);
    }

    public async Task<IReadOnlyList<SharedIdentityAuthorityBinding>> ListAsync(CancellationToken ct = default) =>
        await db.SharedIdentityAuthorities.AsNoTracking()
            .Where(x => x.TenantId == _tenantId)
            .OrderBy(x => x.PortalHost)
            .Select(x => new SharedIdentityAuthorityBinding(
                x.AuthorityId, x.TenantId, x.PortalHost, x.LoginDomain,
                x.Issuer, x.ClientId, x.ClientSecretReference != null, x.Version))
            .ToListAsync(ct);

    public async Task DisableAsync(string authorityId, CancellationToken ct = default)
    {
        var row = await db.SharedIdentityAuthorities.SingleOrDefaultAsync(
            x => x.TenantId == _tenantId && x.AuthorityId == authorityId, ct)
            ?? throw new KeyNotFoundException("Identity authority was not found in this tenant.");
        row.Enabled = false;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.Version++;
        await db.SaveChangesAsync(ct);
    }

    private static string RequireSharedTenant(PortalConfig config, TenantContext context)
    {
        if (!config.SharedTenancy.Enabled)
            throw new InvalidOperationException("Shared identity authorities require Shared tenancy mode.");
        if (context.Origin == TenantContextOrigin.PlatformAuthorization)
            throw new UnauthorizedAccessException(
                "Platform authorization cannot administer a tenant identity authority as a tenant user.");
        return context.Tenant.Value;
    }

    internal static string NormalizeDomain(string value, string parameter)
    {
        var normalized = Require(value, parameter, 253).TrimEnd('.');
        try
        {
            normalized = new IdnMapping().GetAscii(normalized).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Identity domains must be valid DNS names.", parameter);
        }
        if (Uri.CheckHostName(normalized) != UriHostNameType.Dns)
            throw new ArgumentException("Identity domains must be valid DNS names.", parameter);
        return normalized;
    }

    internal static string NormalizeIssuer(string value)
    {
        var text = Require(value, nameof(value), 2048).TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "The identity issuer must be an absolute HTTPS URI without credentials, query, or fragment.",
                nameof(value));
        }
        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)
            .TrimEnd('/');
    }

    private static string? NormalizeSecretReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var reference = value.Trim();
        if (!reference.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "OIDC client credentials must be stored as SECRET:name references.", nameof(value));
        SecretNameValidator.Validate(reference["SECRET:".Length..]);
        return "SECRET:" + reference["SECRET:".Length..];
    }

    private static string Require(string value, string parameter, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new ArgumentException($"A non-empty value of at most {maxLength} characters is required.", parameter);
        return value.Trim();
    }

    private static SharedIdentityAuthorityBinding ToBinding(SharedIdentityAuthority x) => new(
        x.AuthorityId, x.TenantId, x.PortalHost, x.LoginDomain,
        x.Issuer, x.ClientId, x.ClientSecretReference != null, x.Version);
}

/// <summary>
/// Cross-tenant anonymous discovery boundary. It accepts only the routed request host, returns one
/// enabled exact match, and binds the tenant only after the OIDC library has validated the issuer.
/// There is intentionally no lookup by tenant, issuer, authority id, or caller-selected domain.
/// </summary>
public sealed class SharedIdentityAuthorityResolver(PortalDbContext db, PortalConfig config)
{
    public async Task<SharedIdentityAuthorityBinding?> ResolveForRequestAsync(
        HttpRequest request,
        CancellationToken ct = default)
    {
        if (!config.SharedTenancy.Enabled)
            throw new InvalidOperationException("Dynamic identity discovery requires Shared tenancy mode.");
        ArgumentNullException.ThrowIfNull(request);
        var host = SharedIdentityAuthorityService.NormalizeDomain(request.Host.Host, "requestHost");
        return await db.SharedIdentityAuthorities.AsNoTracking()
            .Where(x => x.Enabled && x.PortalHost == host)
            .Select(x => new SharedIdentityAuthorityBinding(
                x.AuthorityId, x.TenantId, x.PortalHost, x.LoginDomain,
                x.Issuer, x.ClientId, x.ClientSecretReference != null, x.Version))
            .SingleOrDefaultAsync(ct);
    }

    internal async Task<SharedIdentityAuthorityBinding?> ResolveProtectedFlowAsync(
        string authorityId,
        string portalHost,
        long version,
        CancellationToken ct)
    {
        var normalizedHost = SharedIdentityAuthorityService.NormalizeDomain(portalHost, nameof(portalHost));
        return await db.SharedIdentityAuthorities.AsNoTracking()
            .Where(x => x.Enabled
                && x.AuthorityId == authorityId
                && x.PortalHost == normalizedHost
                && x.Version == version)
            .Select(x => new SharedIdentityAuthorityBinding(
                x.AuthorityId, x.TenantId, x.PortalHost, x.LoginDomain,
                x.Issuer, x.ClientId, x.ClientSecretReference != null, x.Version))
            .SingleOrDefaultAsync(ct);
    }

    internal async Task<string?> ResolveClientSecretReferenceAsync(
        SharedIdentityAuthorityBinding authority,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return await db.SharedIdentityAuthorities.AsNoTracking()
            .Where(x => x.Enabled
                && x.AuthorityId == authority.AuthorityId
                && x.TenantId == authority.TenantId
                && x.PortalHost == authority.PortalHost
                && x.Issuer == authority.Issuer
                && x.Version == authority.Version)
            .Select(x => x.ClientSecretReference)
            .SingleOrDefaultAsync(ct);
    }

    public TenantContext BindValidatedIssuer(
        SharedIdentityAuthorityBinding authority,
        string validatedIssuer)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var issuer = SharedIdentityAuthorityService.NormalizeIssuer(validatedIssuer);
        if (!string.Equals(issuer, authority.Issuer, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                "The validated identity issuer does not match the server-routed tenant authority.");
        return TenantContext.FromVerifiedCredential(authority.TenantId);
    }
}
