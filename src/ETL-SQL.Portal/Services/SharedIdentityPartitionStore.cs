using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Tenant-qualified identity persistence used by the Shared OIDC bridge. All selectors are applied
/// beneath controller code; numeric user/group identifiers remain assertions inside this scope.
/// </summary>
public sealed class SharedIdentityPartitionStore(
    PortalDbContext db,
    PortalConfig config,
    TenantContext tenantContext)
{
    private readonly string _tenantId = RequireTenant(config, tenantContext);

    public string TenantId => _tenantId;

    public async Task<PortalUser> AddFederatedUserAsync(
        string issuer,
        OidcIdentity identity,
        string normalizedUserName,
        CancellationToken ct = default)
    {
        var user = new PortalUser
        {
            TenantId = _tenantId,
            UserName = identity.Username,
            NormalizedUserName = normalizedUserName,
            Email = identity.Email,
            NormalizedEmail = identity.Email?.ToUpperInvariant(),
            IsActive = true,
            MustChangePassword = false,
            Provider = OidcUserProvisioningService.ProviderName,
            ExternalIssuer = SharedIdentityAuthorityService.NormalizeIssuer(issuer),
            ExternalSubject = identity.Subject,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateFederatedProfileAsync(
        PortalUser user,
        OidcIdentity identity,
        string normalizedUserName,
        CancellationToken ct = default)
    {
        RequireOwnedUser(user);
        if (!string.Equals(user.UserName, identity.Username, StringComparison.Ordinal)
            && await FindByNormalizedNameAsync(normalizedUserName, ct) is null)
        {
            user.UserName = identity.Username;
            user.NormalizedUserName = normalizedUserName;
        }
        if (!string.IsNullOrEmpty(identity.Email)
            && !string.Equals(user.Email, identity.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = identity.Email;
            user.NormalizedEmail = identity.Email.ToUpperInvariant();
        }
    }

    public Task<PortalUser?> FindFederatedUserAsync(
        string issuer,
        string subject,
        CancellationToken ct = default)
    {
        var normalizedIssuer = SharedIdentityAuthorityService.NormalizeIssuer(issuer);
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("A federated subject is required.", nameof(subject));
        return db.Users.SingleOrDefaultAsync(x =>
            x.TenantId == _tenantId
            && x.Provider == OidcUserProvisioningService.ProviderName
            && x.ExternalIssuer == normalizedIssuer
            && x.ExternalSubject == subject, ct);
    }

    public Task<PortalUser?> FindByNormalizedNameAsync(
        string normalizedName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("A normalized username is required.", nameof(normalizedName));
        return db.Users.SingleOrDefaultAsync(x =>
            x.TenantId == _tenantId && x.NormalizedUserName == normalizedName, ct);
    }

    public Task<List<Group>> ListProviderGroupsAsync(
        string provider,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("An identity provider is required.", nameof(provider));
        return db.Groups.Where(x => x.TenantId == _tenantId && x.Provider == provider).ToListAsync(ct);
    }

    public Task<List<UserGroup>> ListProviderMembershipsAsync(
        int userId,
        string provider,
        CancellationToken ct = default) =>
        db.UserGroups.Where(x => x.TenantId == _tenantId
            && x.UserId == userId
            && x.Group.TenantId == _tenantId
            && x.Group.Provider == provider).ToListAsync(ct);

    public void RemoveMemberships(IEnumerable<UserGroup> memberships)
    {
        var rows = memberships.ToList();
        if (rows.Any(x => x.TenantId != _tenantId))
            throw new UnauthorizedAccessException("Membership removal crossed the server-derived tenant.");
        db.UserGroups.RemoveRange(rows);
    }

    public async Task AddMembershipAsync(int userId, int groupId, CancellationToken ct = default)
    {
        var owned = await db.Users.AnyAsync(x => x.Id == userId && x.TenantId == _tenantId, ct)
            && await db.Groups.AnyAsync(x => x.Id == groupId && x.TenantId == _tenantId, ct);
        if (!owned)
            throw new UnauthorizedAccessException(
                "User and group membership must belong to the server-derived tenant.");
        if (!await db.UserGroups.AnyAsync(x =>
                x.TenantId == _tenantId && x.UserId == userId && x.GroupId == groupId, ct))
        {
            db.UserGroups.Add(new UserGroup
            {
                TenantId = _tenantId,
                UserId = userId,
                GroupId = groupId
            });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task AddRefreshTokenAsync(
        int userId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(x => x.Id == userId && x.TenantId == _tenantId, ct))
            throw new UnauthorizedAccessException(
                "A refresh session cannot be attached to another tenant's user.");
        db.RefreshTokens.Add(new RefreshToken
        {
            TenantId = _tenantId,
            UserId = userId,
            Token = tokenHash,
            ExpiresAt = expiresAt
        });
        await db.SaveChangesAsync(ct);
    }

    private static string RequireTenant(PortalConfig config, TenantContext context)
    {
        if (!config.SharedTenancy.Enabled)
            throw new InvalidOperationException("Shared identity partitions require Shared tenancy mode.");
        if (context.Origin != TenantContextOrigin.VerifiedCredential)
            throw new UnauthorizedAccessException(
                "Shared identity persistence requires a verified tenant credential.");
        return context.Tenant.Value;
    }

    private void RequireOwnedUser(PortalUser user)
    {
        if (user.TenantId != _tenantId)
            throw new UnauthorizedAccessException("The user does not belong to the server-derived tenant.");
    }
}

public sealed class SharedIdentityPartitionStoreFactory(PortalDbContext db, PortalConfig config)
{
    public SharedIdentityPartitionStore Create(TenantContext tenantContext) => new(db, config, tenantContext);
}
