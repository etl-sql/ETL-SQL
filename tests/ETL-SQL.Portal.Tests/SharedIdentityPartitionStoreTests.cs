using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedIdentityPartitionStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "shared_identity_partition_" + Guid.NewGuid().ToString("N"));

    public SharedIdentityPartitionStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task EqualLogicalIdentityRecordsCoexistAndResolveOnlyWithinTenant()
    {
        await using var db = await CreateDbAsync();
        var alphaUser = User("tenant-alpha");
        var betaUser = User("tenant-beta");
        var alphaGroup = Group("tenant-alpha");
        var betaGroup = Group("tenant-beta");
        db.Users.AddRange(alphaUser, betaUser);
        db.Groups.AddRange(alphaGroup, betaGroup);
        db.ServiceAccounts.AddRange(
            Service("tenant-alpha", "client-alpha", alphaUser),
            Service("tenant-beta", "client-beta", betaUser));
        await db.SaveChangesAsync();

        var alpha = Store(db, "tenant-alpha");
        var beta = Store(db, "tenant-beta");
        Assert.Equal(alphaUser.Id, (await alpha.FindFederatedUserAsync(
            "https://idp.test/shared", "equal-subject"))!.Id);
        Assert.Equal(betaUser.Id, (await beta.FindFederatedUserAsync(
            "https://idp.test/shared", "equal-subject"))!.Id);
        Assert.Equal(alphaUser.Id, (await alpha.FindByNormalizedNameAsync("EQUAL.USER"))!.Id);
        Assert.Equal(betaUser.Id, (await beta.FindByNormalizedNameAsync("EQUAL.USER"))!.Id);
        Assert.Equal(alphaGroup.Id, Assert.Single(await alpha.ListProviderGroupsAsync("OIDC")).Id);
        Assert.Equal(betaGroup.Id, Assert.Single(await beta.ListProviderGroupsAsync("OIDC")).Id);
    }

    [Fact]
    public async Task MembershipAndRefreshSessionRejectForeignNumericIds()
    {
        await using var db = await CreateDbAsync();
        var alphaUser = User("tenant-alpha", "alpha");
        var betaUser = User("tenant-beta", "beta");
        var alphaGroup = Group("tenant-alpha", "Alpha Analysts");
        var betaGroup = Group("tenant-beta", "Beta Analysts");
        db.Users.AddRange(alphaUser, betaUser);
        db.Groups.AddRange(alphaGroup, betaGroup);
        await db.SaveChangesAsync();
        var alpha = Store(db, "tenant-alpha");

        await alpha.AddMembershipAsync(alphaUser.Id, alphaGroup.Id);
        var membership = Assert.Single(await db.UserGroups.AsNoTracking().ToListAsync());
        Assert.Equal("tenant-alpha", membership.TenantId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            alpha.AddMembershipAsync(alphaUser.Id, betaGroup.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            alpha.AddMembershipAsync(betaUser.Id, alphaGroup.Id));

        await alpha.AddRefreshTokenAsync(
            alphaUser.Id, "hash-alpha", DateTime.UtcNow.AddHours(1));
        Assert.Equal("tenant-alpha", Assert.Single(await db.RefreshTokens.AsNoTracking().ToListAsync()).TenantId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            alpha.AddRefreshTokenAsync(betaUser.Id, "hash-beta", DateTime.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task SharedPartitionRequiresVerifiedCredentialContext()
    {
        await using var db = await CreateDbAsync();
        var config = SharedConfig();
        Assert.Throws<UnauthorizedAccessException>(() => new SharedIdentityPartitionStore(
            db, config, TenantContext.FromHostConfiguration("tenant-alpha")));

        var now = DateTimeOffset.UtcNow;
        var grant = PlatformAccessGrant.Issue(
            "tenant-alpha", "operator@example.test", "approval", "support",
            now.AddMinutes(5), now);
        Assert.Throws<UnauthorizedAccessException>(() => new SharedIdentityPartitionStore(
            db, config, TenantContext.FromPlatformGrant(grant, now)));
    }

    private static PortalUser User(string tenant, string suffix = "equal") => new()
    {
        TenantId = tenant,
        UserName = suffix == "equal" ? "equal.user" : suffix,
        NormalizedUserName = suffix == "equal" ? "EQUAL.USER" : suffix.ToUpperInvariant(),
        Provider = "OIDC",
        ExternalIssuer = "https://idp.test/shared",
        ExternalSubject = suffix == "equal" ? "equal-subject" : suffix + "-subject",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N")
    };

    private static Group Group(string tenant, string name = "Equal Analysts") => new()
    {
        TenantId = tenant,
        Name = name,
        Provider = "OIDC",
        AdGroup = "equal-analysts"
    };

    private static ServiceAccount Service(
        string tenant, string clientId, PortalUser owner) => new()
    {
        TenantId = tenant,
        ClientId = clientId,
        Name = "equal-service",
        NormalizedName = "EQUAL-SERVICE",
        OwnerUser = owner,
        OwnerUserId = owner.Id,
        SecretHash = "hash"
    };

    private static SharedIdentityPartitionStore Store(PortalDbContext db, string tenant) =>
        new(db, SharedConfig(), TenantContext.FromVerifiedCredential(tenant));

    private async Task<PortalDbContext> CreateDbAsync()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db")}")
            .Options;
        var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static PortalConfig SharedConfig() => new()
    {
        SharedTenancy = new SharedTenancyConfig { Enabled = true }
    };

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
