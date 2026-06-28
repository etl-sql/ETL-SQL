using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/auth/service-token")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class ServiceAccountTokenController(
    PortalDbContext db,
    UserManager<PortalUser> users,
    IPasswordHasher<ServiceAccount> passwordHasher,
    TokenService tokens,
    AuditService audit) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Exchange(ServiceAccountTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret))
            return Unauthorized(new { error = "invalid_client" });
        var account = await db.ServiceAccounts.Include(value => value.OwnerUser)
            .SingleOrDefaultAsync(value => value.ClientId == request.ClientId, ct);
        if (account is null || !CanAuthenticate(account)
            || passwordHasher.VerifyHashedPassword(account, account.SecretHash, request.ClientSecret)
                == PasswordVerificationResult.Failed)
            return Unauthorized(new { error = "invalid_client" });

        var currentOwnerRoles = await users.GetRolesAsync(account.OwnerUser);
        var cappedRoles = account.RoleNames.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Intersect(currentOwnerRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        var scopes = ServiceAccountScopes.Parse(account.Scopes);
        account.LastUsedAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;
        audit.Stage(account.OwnerUserId, "SERVICE_ACCOUNT_TOKEN_ISSUED", "ServiceAccount", account.Id,
            $"Scopes={account.Scopes}", actorType: "ServiceAccount", actorId: account.Id,
            effectiveScopes: account.Scopes);
        await db.SaveChangesAsync(ct);

        return Ok(new ServiceAccountTokenResponse(
            tokens.GenerateServiceJwt(account, cappedRoles, scopes), "Bearer", tokens.ServiceTokenLifetimeSeconds));
    }

    private static bool CanAuthenticate(ServiceAccount account) =>
        account.IsEnabled && account.RevokedAt is null
        && (account.ExpiresAt is null || account.ExpiresAt > DateTime.UtcNow)
        && account.OwnerUser.IsActive;
}
