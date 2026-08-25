using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/auth/workload-token")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class WorkloadIdentityTokenController(
    PortalDbContext db,
    UserManager<PortalUser> users,
    IWorkloadIdentityFederationService federation,
    TokenService tokens,
    AuditService audit,
    StudioCapabilityStore studioCapabilities,
    StudioAuthorizationService studio,
    PortalConfig config,
    RequestTenantContextAccessor tenantAccessor) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Exchange(WorkloadIdentityTokenRequest request, CancellationToken ct)
    {
        ValidatedWorkloadIdentity workload;
        try
        {
            workload = await federation.ValidateAsync(request.SubjectToken, request.Audience,
                request.Resource, request.Operation, request.ApprovalToken, ct);
        }
        catch (WorkloadIdentityException ex)
        {
            await audit.LogAsync(null, "WORKLOAD_IDENTITY_EXCHANGE_DENIED", "WorkloadIdentity", null,
                $"Reason={ex.Code}; Resource={request.Resource}; Operation={request.Operation}",
                actorType: "ExternalWorkload");
            return Unauthorized(new { error = ex.Code });
        }

        var binding = workload.Binding;
        var account = await db.ServiceAccounts.Include(value => value.OwnerUser)
            .SingleOrDefaultAsync(value => value.ClientId == binding.ServiceAccountClientId
                && value.TenantId == binding.TenantId, ct);
        if (account is null || !CanAuthenticate(account)
            || !ServiceAccountScopes.Parse(account.Scopes).Contains(request.Operation, StringComparer.OrdinalIgnoreCase))
        {
            await audit.LogAsync(account?.OwnerUserId, "WORKLOAD_IDENTITY_EXCHANGE_DENIED",
                "ServiceAccount", account?.Id, $"Binding={binding.Id}; Reason=account_authority_denied",
                actorType: "ExternalWorkload", actorId: binding.Id);
            return Unauthorized(new { error = "workload_account_authority_denied" });
        }

        TenantContext? tenantContext;
        try
        {
            tenantContext = config.SharedTenancy.Enabled
                ? TenantContext.FromVerifiedCredential(binding.TenantId)
                : !string.IsNullOrWhiteSpace(config.TenantId)
                    ? TenantContext.FromHostConfiguration(config.TenantId)
                    : null;
            if (config.SharedTenancy.Enabled) tenantAccessor.SetVerifiedCredential(tenantContext!);
        }
        catch (ArgumentException)
        {
            return Unauthorized(new { error = "workload_tenant_denied" });
        }
        if (tenantContext is not null
            && !string.Equals(tenantContext.Tenant.Value, binding.TenantId, StringComparison.Ordinal))
            return Unauthorized(new { error = "workload_tenant_denied" });

        var ownerRoles = await users.GetRolesAsync(account.OwnerUser);
        var roles = account.RoleNames.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Intersect(ownerRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        var ownerCapabilities = new HashSet<string>(
            await studioCapabilities.ResolveForUserAsync(account.OwnerUserId, ct),
            StringComparer.OrdinalIgnoreCase);
        foreach (var capability in studio.EffectiveCapabilitiesForRoles(ownerRoles))
            ownerCapabilities.Add(capability);
        var capabilities = StudioCapabilityStore.Parse(account.StudioCapabilities)
            .Where(ownerCapabilities.Contains).ToArray();

        account.LastUsedAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;
        audit.Stage(account.OwnerUserId, "WORKLOAD_IDENTITY_TOKEN_ISSUED", "ServiceAccount", account.Id,
            $"Binding={binding.Id}; Provider={binding.Provider}; Resource={binding.Resource}; Operation={request.Operation}; Jti={workload.TokenId}",
            actorType: "ExternalWorkload", actorId: binding.Id, effectiveScopes: request.Operation);
        await db.SaveChangesAsync(ct);

        return Ok(new WorkloadIdentityTokenResponse(
            tokens.GenerateServiceJwt(account, roles, [request.Operation], capabilities, tenantContext,
                binding.Id, binding.Resource, request.Operation),
            "Bearer", tokens.ServiceTokenLifetimeSeconds, binding.Id));
    }

    private static bool CanAuthenticate(ServiceAccount account) =>
        account.IsEnabled && account.RevokedAt is null
        && (account.ExpiresAt is null || account.ExpiresAt > DateTime.UtcNow)
        && account.OwnerUser.IsActive;
}
