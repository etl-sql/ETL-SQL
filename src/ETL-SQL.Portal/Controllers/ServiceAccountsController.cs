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
    AuditService audit) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IReadOnlyList<ServiceAccountDto>> List(CancellationToken ct) =>
        await db.ServiceAccounts.AsNoTracking().OrderBy(value => value.Name)
            .Select(value => ToDto(value)).ToListAsync(ct);

    [HttpPost]
    public async Task<IActionResult> Create(CreateServiceAccountRequest request, CancellationToken ct)
    {
        if (request.Description?.Trim().Length > 500)
            return BadRequest(new { error = "Description must not exceed 500 characters." });
        var validation = await Validate(request.Name, request.OwnerUserId, request.Scopes, request.Roles, request.ExpiresAt);
        if (validation.Error is not null) return BadRequest(new { error = validation.Error });
        var normalizedName = request.Name.Trim().ToUpperInvariant();
        if (await db.ServiceAccounts.AnyAsync(value => value.NormalizedName == normalizedName, ct))
            return Conflict(new { error = "A service account with this name already exists." });

        var account = new ServiceAccount
        {
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
        var account = await db.ServiceAccounts.FindAsync([id], ct);
        if (account is null) return NotFound();
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
        var account = await db.ServiceAccounts.FindAsync([id], ct);
        if (account is null) return NotFound();
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
        var account = await db.ServiceAccounts.FindAsync([id], ct);
        if (account is null) return NotFound();
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
            var current = await db.ServiceAccounts.AsNoTracking().SingleAsync(value => value.Id == account.Id, ct);
            return OptimisticConcurrency.Conflict(this, ToDto(current));
        }
    }

    private async Task<(string? Error, string[] Roles)> Validate(
        string name, int ownerId, IEnumerable<string> scopes, IEnumerable<string> requestedRoles, DateTime? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            return ("Name is required and must not exceed 100 characters.", []);
        var owner = await users.FindByIdAsync(ownerId.ToString());
        if (owner is null || !owner.IsActive) return ("Owner must be an active portal user.", []);
        if (expiresAt <= DateTime.UtcNow) return ("Expiry must be in the future.", []);
        var normalizedScopes = ServiceAccountScopes.Normalize(scopes);
        if (normalizedScopes.Length == 0) return ("At least one scope is required.", []);
        var invalidScopes = normalizedScopes.Except(ServiceAccountScopes.Allowed, StringComparer.OrdinalIgnoreCase).ToArray();
        if (invalidScopes.Length > 0) return ($"Unsupported scopes: {string.Join(", ", invalidScopes)}", []);

        var roles = (requestedRoles ?? []).Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        if (roles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
            return ("Service accounts cannot receive the Admin role.", []);
        var ownerRoles = await users.GetRolesAsync(owner);
        var excessive = roles.Except(ownerRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        return excessive.Length > 0
            ? ($"Roles exceed the owner's current roles: {string.Join(", ", excessive)}", [])
            : (null, roles);
    }

    private static ServiceAccountDto ToDto(ServiceAccount value) => new(
        value.Id, value.ClientId, value.Name, value.Description, value.OwnerUserId,
        ServiceAccountScopes.Parse(value.Scopes),
        value.RoleNames.Split(' ', StringSplitOptions.RemoveEmptyEntries), value.IsEnabled,
        value.ExpiresAt, value.RevokedAt, value.CreatedAt, value.UpdatedAt, value.LastUsedAt, value.Version,
        [.. StudioCapabilityStore.Parse(value.StudioCapabilities)]);
}
