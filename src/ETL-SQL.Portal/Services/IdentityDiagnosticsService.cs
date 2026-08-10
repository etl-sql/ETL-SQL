using ETL_SQL.Core.Common;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Answers the questions an operator has when federated login misbehaves, without reading logs and
/// without exposing a secret: is the provider reachable, do its group claims map to anything, are
/// federated users landing in groups, and — if the provider goes away — can anyone still administer
/// this Portal.
///
/// Break-glass readiness is the one worth having before you need it. An estate that federates every
/// account, including every administrator, is one identity-provider outage away from nobody being
/// able to fix the identity provider's configuration.
/// </summary>
public sealed class IdentityDiagnosticsService(
    PortalDbContext db,
    PortalConfig config,
    IOidcDiscoveryProvider discovery,
    ILogger<IdentityDiagnosticsService> log,
    RequestTenantContextAccessor tenantAccessor)
{
    private string TenantId => config.SharedTenancy.Enabled
        ? tenantAccessor.RequireCurrent().Tenant.Value
        : string.IsNullOrWhiteSpace(config.TenantId) ? "portal-host" : config.TenantId;

    public async Task<IdentityDiagnosticsDto> BuildAsync(CancellationToken ct = default)
    {
        var oidc = config.Identity.Oidc;
        var ldap = config.Identity.Ldap;

        return new IdentityDiagnosticsDto(
            config.Identity.Provider,
            await BuildOidcAsync(ct),
            new IdentityLdapDiagnosticsDto(
                ldap.Enabled,
                ldap.Server,
                ldap.Port,
                ldap.UseSsl,
                ldap.AllowSelfSignedCertificates,
                ldap.Domain,
                ldap.BaseDn,
                ServiceUserConfigured: !string.IsNullOrWhiteSpace(ldap.ServiceUser),
                ServicePasswordConfigured: !string.IsNullOrWhiteSpace(ldap.ServicePassword),
                RoleMappingCount: ldap.RoleMappings.Count),
            await BuildGroupMappingsAsync(ct),
            await BuildSyncHealthAsync(ct),
            await BuildBreakGlassAsync(ct));
    }

    /// <summary>
    /// Resolves claim values against the configured group mappings without a sign-in, so a mapping
    /// can be checked before someone discovers it is wrong by not having the access they expected.
    /// </summary>
    public async Task<GroupMappingTestResultDto> TestGroupMappingAsync(
        IEnumerable<string> claimValues, CancellationToken ct = default)
    {
        var requested = claimValues
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mappings = await BuildGroupMappingsAsync(ct);
        var matched = mappings
            .Where(mapping => requested.Contains(mapping.ClaimValue, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var matchedValues = matched.Select(m => m.ClaimValue).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new GroupMappingTestResultDto(
            matched,
            [.. requested.Where(value => !matchedValues.Contains(value))
                .OrderBy(value => value, StringComparer.Ordinal)]);
    }

    private async Task<IdentityOidcDiagnosticsDto> BuildOidcAsync(CancellationToken ct)
    {
        var oidc = config.Identity.Oidc;
        var errors = OidcConfigValidationService.Validate(oidc).ToArray();

        if (!oidc.Enabled)
        {
            return new IdentityOidcDiagnosticsDto(
                false, oidc.Authority, oidc.ClientId,
                ClientSecretConfigured: !string.IsNullOrEmpty(oidc.ClientSecret),
                oidc.Scopes, oidc.GroupClaimTypes, errors,
                DiscoveryReachable: null, null, null, null);
        }

        try
        {
            var configuration = await discovery.GetConfigurationAsync(ct);
            return new IdentityOidcDiagnosticsDto(
                true, oidc.Authority, oidc.ClientId,
                ClientSecretConfigured: !string.IsNullOrEmpty(oidc.ClientSecret),
                oidc.Scopes, oidc.GroupClaimTypes, errors,
                DiscoveryReachable: true,
                configuration.Issuer,
                configuration.SigningKeys.Count,
                DiscoveryError: null);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "OIDC discovery probe failed during identity diagnostics.");
            return new IdentityOidcDiagnosticsDto(
                true, oidc.Authority, oidc.ClientId,
                ClientSecretConfigured: !string.IsNullOrEmpty(oidc.ClientSecret),
                oidc.Scopes, oidc.GroupClaimTypes, errors,
                DiscoveryReachable: false, null, null,
                // Redacted: a failing probe can echo a URL carrying a token or secret.
                SecretRedactor.Redact(ex.Message));
        }
    }

    private async Task<IReadOnlyList<IdentityGroupMappingDto>> BuildGroupMappingsAsync(CancellationToken ct)
    {
        var groups = await db.Groups
            .Where(group => group.TenantId == TenantId && group.Provider != "Local")
            .Select(group => new
            {
                group.Id,
                group.Name,
                group.AdGroup,
                MemberCount = group.UserGroups.Count(membership => membership.TenantId == TenantId)
            })
            .ToListAsync(ct);

        return
        [
            .. groups
                .Select(group => new IdentityGroupMappingDto(
                    group.Id,
                    group.Name,
                    // Matches OidcUserProvisioningService.SyncGroupsAsync: AD name when set, else the
                    // portal name. Stating it removes the guesswork from a mapping that never fires.
                    string.IsNullOrEmpty(group.AdGroup) ? group.Name : group.AdGroup!,
                    group.MemberCount))
                .OrderBy(mapping => mapping.GroupName, StringComparer.Ordinal)
        ];
    }

    private async Task<IdentitySyncHealthDto> BuildSyncHealthAsync(CancellationToken ct)
    {
        var federated = await db.Users
            .Where(user => user.TenantId == TenantId && user.Provider != "Local")
            .ToListAsync(ct);
        var federatedIds = federated.Select(user => user.Id).ToList();

        var withMappedGroup = await db.UserGroups
            .Where(membership => membership.TenantId == TenantId
                && federatedIds.Contains(membership.UserId)
                && membership.Group.TenantId == TenantId
                && membership.Group.Provider != "Local")
            .Select(membership => membership.UserId)
            .Distinct()
            .CountAsync(ct);

        return new IdentitySyncHealthDto(
            federated.Count,
            federated.Count(user => user.IsActive),
            federated.Count - withMappedGroup,
            await db.Groups.CountAsync(
                group => group.TenantId == TenantId && group.Provider != "Local", ct));
    }

    private async Task<IdentityBreakGlassDto> BuildBreakGlassAsync(CancellationToken ct)
    {
        var localAdmins = await db.UserRoles
            .Join(db.Roles, membership => membership.RoleId, role => role.Id,
                (membership, role) => new { membership.UserId, role.Name })
            .Where(entry => entry.Name == "Admin")
            .Join(db.Users, entry => entry.UserId, user => user.Id, (entry, user) => user)
            .Where(user => user.TenantId == TenantId && user.IsActive && user.Provider == "Local")
            .Select(user => user.UserName!)
            .OrderBy(name => name)
            .ToListAsync(ct);

        return new IdentityBreakGlassDto(
            localAdmins.Count > 0,
            localAdmins,
            localAdmins.Count > 0
                ? $"{localAdmins.Count} active local administrator account(s) can sign in without the "
                  + "identity provider."
                : "No active local administrator exists. If the identity provider becomes unreachable "
                  + "or misconfigured, nobody can sign in to correct it.");
    }
}
