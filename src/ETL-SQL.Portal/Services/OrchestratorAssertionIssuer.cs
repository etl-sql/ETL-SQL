using System.Security.Claims;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>One issued assertion and what a client needs to use and renew it.</summary>
/// <param name="Assertion">The signed token, presented in <c>X-Orchestrator-Identity</c>.</param>
/// <param name="ExpiresAt">
/// When it stops being accepted. Returned so a client can renew ahead of expiry instead of
/// discovering it through a failed call — the assertion is deliberately short-lived, so a client that
/// cannot see the expiry would either re-exchange on every request or fail intermittently.
/// </param>
/// <param name="Scopes">
/// The ceiling carried in the token, echoed back so a caller can tell a permission failure from a
/// scope failure without decoding a token it is not supposed to parse.
/// </param>
public sealed record IssuedOrchestratorAssertion(
    string Assertion,
    DateTimeOffset ExpiresAt,
    string Audience,
    IReadOnlyList<string> Scopes);

/// <summary>
/// Mints Portal-signed Orchestrator identity assertions.
///
/// <para>This is the single place a Portal principal becomes an Orchestrator caller. It exists as a
/// service rather than a method on the proxy because two callers now need it: the proxy, which
/// attaches an assertion to its own outbound calls, and the exchange endpoint, which hands one to a
/// client that will call the Orchestrator directly. Duplicating the resolution would mean two
/// answers to "who is this and what may they do", which is the thing a single control plane exists
/// to prevent.</para>
/// </summary>
public sealed class OrchestratorAssertionIssuer(
    PortalConfig? portalConfig = null,
    PortalDbContext? portalDb = null)
{
    /// <summary>
    /// The assertion for the Portal's own background work — subscription dispatch, refresh,
    /// reconciliation. This is the control plane acting as itself, so it carries the whole scope
    /// ladder: a service caller with no scopes can do nothing, and leaving them off would silently
    /// stop every scheduled delivery.
    /// </summary>
    public string? IssueForBackground()
    {
        var secret = portalConfig?.Orchestrator.IdentitySigningSecret;
        if (string.IsNullOrWhiteSpace(secret)) return null;

        var tenant = portalConfig?.SharedTenancy.Enabled == true
            ? null
            : string.IsNullOrWhiteSpace(portalConfig?.TenantId) ? "portal-host" : portalConfig.TenantId;

        return OrchestratorIdentityAssertion.Create(
            new OrchestratorCaller(
                "service", "portal-background", "Portal background service", ["PortalSystem"], [],
                tenant, [.. ServiceAccountScopes.OrchestratorLadder]),
            secret);
    }

    /// <summary>
    /// The assertion for an authenticated Portal principal, or null when the deployment does not
    /// federate identity or the principal cannot be resolved to one.
    ///
    /// <para>Everything in it is read from the server's own view of the caller — the session's
    /// claims, the group memberships in the Portal database, the tenant bound to the credential.
    /// Nothing is taken from the request body, which is why the exchange endpoint needs no parameters:
    /// a caller cannot ask for an identity, only present one.</para>
    /// </summary>
    public async Task<IssuedOrchestratorAssertion?> IssueForAsync(
        ClaimsPrincipal? user, CancellationToken cancellationToken = default)
    {
        var secret = portalConfig?.Orchestrator.IdentitySigningSecret;
        if (string.IsNullOrWhiteSpace(secret)) return null;
        if (user?.Identity?.IsAuthenticated != true) return null;
        if (portalConfig is null
            || !TenantCredentialBinding.TryResolve(user, portalConfig, out var tenant, out _))
            return null;

        var tenantId = tenant?.Tenant.Value ?? "portal-host";
        // Either spelling of the subject: the token carries `sub`, and whether it arrives mapped to
        // NameIdentifier depends on the handler's inbound claim mapping. Reading only one is how a
        // caller ends up unidentifiable and silently unable to obtain an assertion at all —
        // ServiceAccountScopeMiddleware already checks both for the same reason.
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity.Name;
        var serviceAccountId = user.FindFirstValue(TokenService.ServiceAccountIdClaim);
        var isService = string.Equals(
            user.FindFirstValue(TokenService.IdentityTypeClaim),
            TokenService.ServiceIdentityType,
            StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(isService ? serviceAccountId : id)) return null;

        // Group membership and the subject's own key are read live rather than taken from the token:
        // a group removed since the session began must not still be assertable, and grants resolve
        // against these.
        string[] groupIds = [];
        string[] groupNames = [];
        var subjectId = serviceAccountId;
        if (!isService)
        {
            if (portalDb is null || !int.TryParse(id, out var userId)) return null;

            // The stable key, not the row id. A row id is only stable while the row is; a grant that
            // followed one would be inherited by whoever held the id after a re-provision or a
            // restore. A user with no key yet cannot be asserted at all — falling back to the numeric
            // id would silently reintroduce exactly the identifier this replaces.
            subjectId = await portalDb.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.PrincipalKey)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(subjectId)) return null;

            var groups = await portalDb.UserGroups.AsNoTracking()
                .Where(membership => membership.UserId == userId)
                .Join(portalDb.Groups, membership => membership.GroupId, group => group.Id,
                    (_, group) => new { group.PrincipalKey, group.Name })
                .ToArrayAsync(cancellationToken);

            // A group with no key is skipped rather than substituted: it can hold no grant, and
            // inventing an identifier for it would create one that matches something.
            groupIds = [.. groups
                .Select(group => group.PrincipalKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key!)];
            groupNames = [.. groups.Select(group => group.Name).Where(name => !string.IsNullOrWhiteSpace(name))];
        }

        if (string.IsNullOrWhiteSpace(subjectId)) return null;

        var roles = user.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Scopes cap a service caller only. An interactive user has none: their authority is their
        // roles and their grants, already bounded by the Portal session this is derived from.
        var scopes = isService
            ? user.FindAll(TokenService.ScopeClaim)
                .Select(claim => claim.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        var issuedAt = DateTimeOffset.UtcNow;
        var assertion = OrchestratorIdentityAssertion.Create(
            new OrchestratorCaller(
                isService ? "service" : "user",
                subjectId,
                name ?? subjectId,
                roles,
                groupIds,
                tenantId,
                scopes,
                groupNames),
            secret,
            issuedAt);

        return new IssuedOrchestratorAssertion(
            assertion,
            issuedAt.Add(OrchestratorIdentityAssertion.DefaultLifetime),
            OrchestratorIdentityAssertion.Audience,
            scopes);
    }
}
