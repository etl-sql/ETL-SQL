using System.Security.Claims;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/admin/service-accounts")]
[Authorize(Roles = "Admin")]
public sealed class ServiceAccountsController(
    PortalDbContext db,
    UserManager<PortalUser> users,
    IPasswordHasher<ServiceAccount> passwordHasher,
    AuditService audit,
    PortalConfig config,
    RequestTenantContextAccessor tenantAccessor) : ControllerBase
{
    private string TenantId => config.SharedTenancy.Enabled
        ? tenantAccessor.RequireCurrent().Tenant.Value
        : string.IsNullOrWhiteSpace(config.TenantId) ? "portal-host" : config.TenantId;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsServiceIdentity =>
        User.FindFirstValue(TokenService.IdentityTypeClaim) == TokenService.ServiceIdentityType;

    [HttpGet]
    public async Task<IReadOnlyList<ServiceAccountDto>> List(CancellationToken ct)
    {
        var query = db.ServiceAccounts.AsNoTracking().Where(value => value.TenantId == TenantId);
        if (IsServiceIdentity)
            query = query.Where(value => value.OwnerUserId == CurrentUserId);
        return await query.OrderBy(value => value.Name).Select(value => ToDto(value)).ToListAsync(ct);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateServiceAccountRequest request, CancellationToken ct)
    {
        if (request.Description?.Trim().Length > 500)
            return BadRequest(new { error = "Description must not exceed 500 characters." });
        // Refuse a cross-owner delegation before looking that owner up, so a service identity
        // cannot use validation differences to enumerate active administrator IDs.
        if (IsServiceIdentity && request.OwnerUserId != CurrentUserId) return Forbid();
        var validation = await Validate(request.Name, request.OwnerUserId, request.Scopes, request.Roles, request.ExpiresAt);
        if (validation.Error is not null) return BadRequest(new { error = validation.Error });
        if (IsServiceIdentity && !CanDelegate(request.OwnerUserId, request.Scopes,
                validation.Roles, request.StudioCapabilities ?? []))
            return Forbid();
        var normalizedName = request.Name.Trim().ToUpperInvariant();
        if (await db.ServiceAccounts.AnyAsync(
                value => value.TenantId == TenantId && value.NormalizedName == normalizedName, ct))
            return Conflict(new { error = "A service account with this name already exists." });

        var account = new ServiceAccount
        {
            TenantId = TenantId,
            Name = request.Name.Trim(),
            NormalizedName = normalizedName,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            OwnerUserId = request.OwnerUserId,
            ClientId = ServiceAccountCredentials.NewClientId(),
            Scopes = ServiceAccountScopes.Serialize(request.Scopes),
            RoleNames = string.Join(' ', validation.Roles),
            StudioCapabilities = StudioCapabilityStore.Format(request.StudioCapabilities ?? []),
            ExpiresAt = request.ExpiresAt?.ToUniversalTime()
        };
        var secret = ServiceAccountCredentials.NewSecret();
        account.SecretHash = passwordHasher.HashPassword(account, secret);
        db.ServiceAccounts.Add(account);
        audit.Stage(CurrentUserId, "CREATE_SERVICE_ACCOUNT", "ServiceAccount", account.Id,
            $"Name={account.Name}; Owner={account.OwnerUserId}; Scopes={account.Scopes}");
        await db.SaveChangesAsync(ct);
        return Created($"/api/admin/service-accounts/{account.Id}",
            new ServiceAccountCreatedResponse(ToDto(account), secret));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateServiceAccountRequest request, CancellationToken ct)
    {
        var account = await db.ServiceAccounts.SingleOrDefaultAsync(
            value => value.Id == id && value.TenantId == TenantId, ct);
        if (account is null) return NotFound();
        if (IsServiceIdentity && (!CanManage(account)
                || !CanDelegate(account.OwnerUserId, request.Scopes, Roles(account),
                    request.StudioCapabilities ?? Capabilities(account))))
            return Forbid();
        var concurrency = PrepareMutation(account);
        if (concurrency is not null) return concurrency;
        var scopes = ServiceAccountScopes.Normalize(request.Scopes);
        if (scopes.Length == 0) return BadRequest(new { error = "At least one scope is required." });
        var invalid = scopes.Except(ServiceAccountScopes.Allowed, StringComparer.OrdinalIgnoreCase).ToArray();
        if (invalid.Length > 0) return BadRequest(new { error = $"Unsupported scopes: {string.Join(", ", invalid)}" });
        if (request.ExpiresAt <= DateTime.UtcNow) return BadRequest(new { error = "Expiry must be in the future." });

        account.IsEnabled = request.IsEnabled;
        account.ExpiresAt = request.ExpiresAt?.ToUniversalTime();
        account.Scopes = ServiceAccountScopes.Serialize(scopes);
        if (request.StudioCapabilities is not null)
            account.StudioCapabilities = StudioCapabilityStore.Format(request.StudioCapabilities);
        // A new stamp invalidates outstanding tokens, so a narrowed capability set takes effect now
        // rather than at the end of the current token's life.
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.UpdatedAt = DateTime.UtcNow;
        audit.Stage(CurrentUserId, "UPDATE_SERVICE_ACCOUNT", "ServiceAccount", account.Id,
            $"Enabled={account.IsEnabled}; Scopes={account.Scopes}");
        var conflict = await SaveMutation(account, ct);
        if (conflict is not null) return conflict;
        return Ok(ToDto(account));
    }

    [HttpPost("{id}/rotate-secret")]
    public async Task<IActionResult> Rotate(string id, CancellationToken ct)
    {
        var account = await db.ServiceAccounts.SingleOrDefaultAsync(
            value => value.Id == id && value.TenantId == TenantId, ct);
        if (account is null) return NotFound();
        if (IsServiceIdentity && !CanManage(account)) return Forbid();
        var concurrency = PrepareMutation(account);
        if (concurrency is not null) return concurrency;
        if (account.RevokedAt is not null) return Conflict(new { error = "A revoked service account cannot be rotated." });
        var secret = ServiceAccountCredentials.NewSecret();
        account.SecretHash = passwordHasher.HashPassword(account, secret);
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.UpdatedAt = DateTime.UtcNow;
        audit.Stage(CurrentUserId, "ROTATE_SERVICE_ACCOUNT_SECRET", "ServiceAccount", account.Id);
        var conflict = await SaveMutation(account, ct);
        if (conflict is not null) return conflict;
        return Ok(new ServiceAccountCreatedResponse(ToDto(account), secret));
    }

    [HttpPost("{id}/revoke")]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        var account = await db.ServiceAccounts.SingleOrDefaultAsync(
            value => value.Id == id && value.TenantId == TenantId, ct);
        if (account is null) return NotFound();
        if (IsServiceIdentity && !CanManage(account)) return Forbid();
        var concurrency = PrepareMutation(account);
        if (concurrency is not null) return concurrency;
        if (account.RevokedAt is null)
        {
            account.RevokedAt = DateTime.UtcNow;
            account.IsEnabled = false;
            account.SecurityStamp = Guid.NewGuid().ToString("N");
            account.UpdatedAt = DateTime.UtcNow;
            audit.Stage(CurrentUserId, "REVOKE_SERVICE_ACCOUNT", "ServiceAccount", account.Id);
            var conflict = await SaveMutation(account, ct);
            if (conflict is not null) return conflict;
        }
        return NoContent();
    }

    private IActionResult? PrepareMutation(ServiceAccount account)
    {
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null) return OptimisticConcurrency.MissingVersion(this);
        return OptimisticConcurrency.Prepare(db, account, expectedVersion.Value)
            ? null
            : OptimisticConcurrency.Conflict(this, ToDto(account));
    }

    private async Task<IActionResult?> SaveMutation(ServiceAccount account, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            OptimisticConcurrency.SetETag(Response, account.Version);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await db.ServiceAccounts.AsNoTracking().SingleAsync(
                value => value.Id == account.Id && value.TenantId == TenantId, ct);
            return OptimisticConcurrency.Conflict(this, ToDto(current));
        }
    }

    private async Task<(string? Error, string[] Roles)> Validate(
        string name, int ownerId, IEnumerable<string> scopes, IEnumerable<string> requestedRoles, DateTime? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            return ("Name is required and must not exceed 100 characters.", []);
        var owner = await db.Users.SingleOrDefaultAsync(
            value => value.Id == ownerId && value.TenantId == TenantId);
        if (owner is null || !owner.IsActive) return ("Owner must be an active portal user.", []);
        if (expiresAt <= DateTime.UtcNow) return ("Expiry must be in the future.", []);
        var normalizedScopes = ServiceAccountScopes.Normalize(scopes);
        if (normalizedScopes.Length == 0) return ("At least one scope is required.", []);
        var invalidScopes = normalizedScopes.Except(ServiceAccountScopes.Allowed, StringComparer.OrdinalIgnoreCase).ToArray();
        if (invalidScopes.Length > 0) return ($"Unsupported scopes: {string.Join(", ", invalidScopes)}", []);

        var roles = (requestedRoles ?? []).Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

        // The Admin role was previously refused outright, because an Admin-roled token would have
        // reached all ~60 admin endpoints. It is now permitted solely alongside admin.identity,
        // which confines the token to the enumerated identity routes in AdminIdentityRoutes —
        // backup, export, promotion, key rotation and shutdown stay unreachable. Granting Admin
        // without that scope would restore the unbounded reach and is still refused.
        if (roles.Contains("Admin", StringComparer.OrdinalIgnoreCase)
            && !normalizedScopes.Contains(ServiceAccountScopes.AdminIdentity, StringComparer.OrdinalIgnoreCase))
            return ($"The Admin role may only be granted together with the {ServiceAccountScopes.AdminIdentity} scope.", []);
        var ownerRoles = await users.GetRolesAsync(owner);
        var excessive = roles.Except(ownerRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        return excessive.Length > 0
            ? ($"Roles exceed the owner's current roles: {string.Join(", ", excessive)}", [])
            : (null, roles);
    }

    /// <summary>
    /// A service identity may delegate only its current effective authority, and only beneath the
    /// same human owner. This prevents selecting a stronger administrator as owner, adding a scope
    /// the caller lacks, or rotating a more privileged sibling account to steal its credential.
    /// </summary>
    private bool CanDelegate(
        int ownerUserId, IEnumerable<string> scopes, IEnumerable<string> roles,
        IEnumerable<string> capabilities) =>
        ownerUserId == CurrentUserId
        && IsSubset(scopes, User.FindAll(TokenService.ScopeClaim).Select(value => value.Value))
        && IsSubset(roles, User.FindAll(ClaimTypes.Role).Select(value => value.Value))
        && IsSubset(capabilities,
            User.FindAll(StudioAuthorizationService.CapabilityClaim).Select(value => value.Value));

    private bool CanManage(ServiceAccount account) =>
        CanDelegate(account.OwnerUserId, ServiceAccountScopes.Parse(account.Scopes),
            Roles(account), Capabilities(account));

    private static string[] Roles(ServiceAccount account) =>
        account.RoleNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string[] Capabilities(ServiceAccount account) =>
        [.. StudioCapabilityStore.Parse(account.StudioCapabilities)];

    private static bool IsSubset(IEnumerable<string> requested, IEnumerable<string> granted)
    {
        var grant = granted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requested.Select(value => value.Trim()).Where(value => value.Length > 0)
            .All(grant.Contains);
    }

    private static ServiceAccountDto ToDto(ServiceAccount value) => new(
        value.Id, value.ClientId, value.Name, value.Description, value.OwnerUserId,
        ServiceAccountScopes.Parse(value.Scopes),
        value.RoleNames.Split(' ', StringSplitOptions.RemoveEmptyEntries), value.IsEnabled,
        value.ExpiresAt, value.RevokedAt, value.CreatedAt, value.UpdatedAt, value.LastUsedAt, value.Version,
        [.. StudioCapabilityStore.Parse(value.StudioCapabilities)]);
}
