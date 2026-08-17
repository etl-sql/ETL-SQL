using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Security;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class SharedTenantStoreIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"shared-tenant-store-{Guid.NewGuid():N}");

    [Fact]
    public async Task EqualSecretNamesArePartitionedBelowServiceBoundary()
    {
        await using var db = NewDb();
        var dataProtection = new EphemeralDataProtectionProvider();
        var keys = KeyProvider();
        var alpha = new PortalSecretStoreService(db, dataProtection, keyProvider: keys,
            tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        var beta = new PortalSecretStoreService(db, dataProtection, keyProvider: keys,
            tenantContext: TenantContext.FromVerifiedCredential("tenant-beta"));

        await alpha.StoreAsync("warehouse_password", "alpha-secret");
        await beta.StoreAsync("warehouse_password", "beta-secret");

        Assert.Equal("alpha-secret", await alpha.ResolveAsync("warehouse_password"));
        Assert.Equal("beta-secret", await beta.ResolveAsync("warehouse_password"));
        Assert.Equal(2, await db.PortalSecrets.CountAsync());
        var envelopes = await db.PortalSecrets
            .OrderBy(row => row.TenantId)
            .Select(row => row.EncryptedValue)
            .ToListAsync();
        Assert.StartsWith("km1:v1:", envelopes[0], StringComparison.Ordinal);
        Assert.StartsWith("km1:v9:", envelopes[1], StringComparison.Ordinal);
        Assert.Equal(["tenant-alpha", "tenant-beta"],
            await db.PortalSecrets.OrderBy(row => row.TenantId).Select(row => row.TenantId).ToListAsync());
        Assert.Single(await alpha.ListAsync());
        await alpha.DeleteAsync("warehouse_password");
        Assert.Equal("beta-secret", await beta.ResolveAsync("warehouse_password"));
    }

    [Fact]
    public async Task EqualConnectionAliasesAndExportsRemainTenantPartitioned()
    {
        await using var db = NewDb();
        var alpha = new PortalConnectionCatalogService(
            db, TenantContext.FromVerifiedCredential("tenant-alpha"));
        var beta = new PortalConnectionCatalogService(
            db, TenantContext.FromVerifiedCredential("tenant-beta"));

        await alpha.StoreAsync(Connection("warehouse", "alpha.db"));
        await alpha.SaveAsync();
        await beta.StoreAsync(Connection("warehouse", "beta.db"));
        await beta.SaveAsync();

        Assert.Equal("alpha.db", (await alpha.ExportAsync()).Single().Target);
        Assert.Equal("beta.db", (await beta.ExportAsync()).Single().Target);
        Assert.Equal(["warehouse"], await alpha.ListUsableAliasesAsync(null));
        Assert.Equal(2, await db.PortalSharedConnections.CountAsync());

        await alpha.DeleteAsync("warehouse");
        await alpha.SaveAsync();
        Assert.NotNull(await beta.GetDetailAsync("warehouse"));
        Assert.Null(await alpha.GetDetailAsync("warehouse"));
    }

    [Fact]
    public async Task PolicyVersionsWithEqualNamesRemainTenantPartitionedInDatabaseStore()
    {
        await using var db = NewDb();
        using var signer = new RsaPolicyEnvelopeSigner(
            System.Security.Cryptography.RSA.Create(2048));
        var store = new DbPolicyAuthorityStore(db);
        var alpha = new PolicyAuthorityService(store, signer, authorityTenant:
            TenantContext.FromVerifiedCredential("tenant-alpha"));
        var beta = new PolicyAuthorityService(store, signer, authorityTenant:
            TenantContext.FromVerifiedCredential("tenant-beta"));
        var document = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection
            {
                ApprovedRoots = [Path.GetTempPath().TrimEnd('\\', '/')]
            }
        };

        await alpha.PublishAsync(document, "tenant-alpha", "prod", "v1", "alpha-admin",
            null, DateTimeOffset.UtcNow.AddDays(30));
        await beta.PublishAsync(document, "tenant-beta", "prod", "v1", "beta-admin",
            null, DateTimeOffset.UtcNow.AddDays(30));

        Assert.Single(await alpha.ListVersionsAsync("tenant-alpha", "prod"));
        Assert.Single(await beta.ListVersionsAsync("tenant-beta", "prod"));
        Assert.Equal(2, await db.PolicyVersions.CountAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await alpha.ListVersionsAsync("tenant-beta", "prod"));
    }

    [Fact]
    public async Task SharedStoresFailClosedWithoutVerifiedTenantContext()
    {
        await using var db = NewDb();
        var config = new PortalConfig { SharedTenancy = new SharedTenancyConfig { Enabled = true } };
        Assert.Throws<UnauthorizedAccessException>(() => new PortalSecretStoreService(
            db, new EphemeralDataProtectionProvider(), config));
        Assert.Throws<UnauthorizedAccessException>(() =>
            new PortalConnectionCatalogService(db, config: config));
    }

    private static PortalSharedConnectionExport Connection(string alias, string target) =>
        new(alias, "SQLITE", target, new Dictionary<string, string>(), null, false);

    private static ResolvedKeyMaterialProvider KeyProvider()
    {
        static (KeyMaterialDescriptor, byte[]) Entry(string tenant, byte marker) =>
            (new("shared-vault", $"{tenant}-credential", tenant, KeyPurpose.Credential,
                tenant == "tenant-alpha" ? "v1" : "v9"),
                Enumerable.Repeat(marker, 32).ToArray());
        return new ResolvedKeyMaterialProvider("shared-vault",
        [
            Entry("tenant-alpha", 11),
            Entry("tenant-beta", 22)
        ]);
    }

    private PortalDbContext NewDb()
    {
        Directory.CreateDirectory(_root);
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal.db")}")
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
