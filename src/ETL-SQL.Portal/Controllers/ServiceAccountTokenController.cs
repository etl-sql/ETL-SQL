using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using ETL_SQL.Core.Multitenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/auth/service-token")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class ServiceAccountTokenController(
    PortalDbContext db,
    UserManager<PortalUser> users,
    IPasswordHasher<ServiceAccount> passwordHasher,
    TokenService tokens,
    AuditService audit,
    StudioCapabilityStore studioCapabilities,
    StudioAuthorizationService studio,
    PortalConfig config,
    RequestTenantContextAccessor tenantAccessor) : ControllerBase
{
    private static readonly string DummyHash = new PasswordHasher<ServiceAccount>().HashPassword(null!, "dummy_secret_for_timing_protection");

    [HttpPost]
    public async Task<IActionResult> Exchange(ServiceAccountTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret))
            return Unauthorized(new { error = "invalid_client" });
        var account = await db.ServiceAccounts.Include(value => value.OwnerUser)
            .SingleOrDefaultAsync(value => value.ClientId == request.ClientId, ct);

        bool isValid = account is not null && CanAuthenticate(account);
        var targetAccount = account ?? new ServiceAccount();
        var targetHash = isValid ? account!.SecretHash : DummyHash;

        var verifyResult = passwordHasher.VerifyHashedPassword(targetAccount, targetHash, request.ClientSecret);
        if (!isValid || verifyResult == PasswordVerificationResult.Failed)
            return Unauthorized(new { error = "invalid_client" });

        var activeAccount = account!;
        TenantContext? tenantContext = null;
        if (config.SharedTenancy.Enabled)
        {
            if (!string.Equals(activeAccount.TenantId, activeAccount.OwnerUser.TenantId, StringComparison.Ordinal))
                return Unauthorized(new { error = "invalid_client" });
            try
            {
                tenantContext = TenantContext.FromVerifiedCredential(activeAccount.TenantId);
                tenantAccessor.SetVerifiedCredential(tenantContext);
            }
            catch (ArgumentException)
            {
                return Unauthorized(new { error = "invalid_client" });
            }
        }
        else if (!string.IsNullOrWhiteSpace(config.TenantId))
        {
            tenantContext = TenantContext.FromHostConfiguration(config.TenantId);
        }
        var currentOwnerRoles = await users.GetRolesAsync(activeAccount.OwnerUser);
        var cappedRoles = activeAccount.RoleNames.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Intersect(currentOwnerRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        var scopes = ServiceAccountScopes.Parse(activeAccount.Scopes);
        // Capped by the owner's own capabilities, exactly as roles are: a service account must not
        // outlive or exceed the authority of the person who created it, so an owner who loses
        // SourcePush takes it from every account they own at the next token issue.
        var ownerCapabilities = new HashSet<string>(
            await studioCapabilities.ResolveForUserAsync(activeAccount.OwnerUserId, ct),
            StringComparer.OrdinalIgnoreCase);
        foreach (var capability in studio.EffectiveCapabilitiesForRoles(currentOwnerRoles))
            ownerCapabilities.Add(capability);
        var cappedCapabilities = StudioCapabilityStore.Parse(activeAccount.StudioCapabilities)
            .Where(ownerCapabilities.Contains)
            .ToArray();
        activeAccount.LastUsedAt = DateTime.UtcNow;
        activeAccount.UpdatedAt = DateTime.UtcNow;
        audit.Stage(activeAccount.OwnerUserId, "SERVICE_ACCOUNT_TOKEN_ISSUED", "ServiceAccount", activeAccount.Id,
            $"Scopes={activeAccount.Scopes}", actorType: "ServiceAccount", actorId: activeAccount.Id,
            effectiveScopes: activeAccount.Scopes);
        await db.SaveChangesAsync(ct);

        return Ok(new ServiceAccountTokenResponse(
            tokens.GenerateServiceJwt(activeAccount, cappedRoles, scopes, cappedCapabilities, tenantContext),
            "Bearer", tokens.ServiceTokenLifetimeSeconds));
    }

    private static bool CanAuthenticate(ServiceAccount account) =>
        account.IsEnabled && account.RevokedAt is null
        && (account.ExpiresAt is null || account.ExpiresAt > DateTime.UtcNow)
        && account.OwnerUser.IsActive;
}
