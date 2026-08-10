using ETL_SQL.Core.Security;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

public sealed class PortalSecretStoreServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "etl-sql-portal-secrets-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProviderBackedStoreUsesCredentialPurposeAndReadsLegacyDataProtectionRows()
    {
        await using var db = NewDb();
        var dataProtection = new EphemeralDataProtectionProvider();
        var legacy = new PortalSecretStoreService(
            db, dataProtection, new PortalConfig { TenantId = "tenant-alpha" });
        await legacy.StoreAsync("legacy_secret", "legacy-value");

        var descriptor = new KeyMaterialDescriptor(
            "vault", "credential-alpha", "tenant-alpha", KeyPurpose.Credential, "v1");
        var keys = new ResolvedKeyMaterialProvider("vault",
            [(descriptor, Enumerable.Repeat((byte)77, 32).ToArray())]);
        var providerStore = new PortalSecretStoreService(
            db, dataProtection, new PortalConfig { TenantId = "tenant-alpha" }, keys);
        await providerStore.StoreAsync("provider_secret", "provider-value");

        var stored = await db.PortalSecrets.SingleAsync(secret => secret.Name == "provider_secret");
        Assert.StartsWith("km1:v1:", stored.EncryptedValue, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-value", stored.EncryptedValue, StringComparison.Ordinal);
        Assert.Equal("provider-value", await providerStore.ResolveAsync("provider_secret"));
        Assert.Equal("legacy-value", await providerStore.ResolveAsync("legacy_secret"));

        var datasetOnly = new ResolvedKeyMaterialProvider("vault",
        [
            (descriptor with { KeyId = "dataset-alpha", Purpose = KeyPurpose.Dataset },
             Enumerable.Repeat((byte)77, 32).ToArray())
        ]);
        var wrongPurpose = new PortalSecretStoreService(
            db, dataProtection, new PortalConfig { TenantId = "tenant-alpha" }, datasetOnly);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => wrongPurpose.ResolveAsync("provider_secret"));
    }

    [Fact]
    public async Task StoreResolveAndListNeverExposePlaintext()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await service.StoreAsync(" sales_db_password ", "p@ssw0rd", userId: 7);

        var stored = await db.PortalSecrets.SingleAsync();
        Assert.Equal("sales_db_password", stored.Name);
        Assert.StartsWith("dp:", stored.EncryptedValue, StringComparison.Ordinal);
        Assert.DoesNotContain("p@ssw0rd", stored.EncryptedValue);
        Assert.Equal(7, stored.CreatedByUserId);
        Assert.Equal(7, stored.UpdatedByUserId);

        Assert.Equal("p@ssw0rd", await service.ResolveAsync("sales_db_password"));

        var summary = Assert.Single(await service.ListAsync());
        Assert.Equal("sales_db_password", summary.Name);
        Assert.False(summary.Disabled);
        Assert.Equal(1, summary.Version);
    }

    [Fact]
    public async Task StoreExistingSecretRotatesAndReenables()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await service.StoreAsync("db_password", "first", userId: 1);
        await service.DisableAsync("db_password", userId: 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync("db_password"));

        await service.StoreAsync("db_password", "second", userId: 3);

        Assert.Equal("second", await service.ResolveAsync("db_password"));
        var stored = await db.PortalSecrets.SingleAsync();
        Assert.False(stored.Disabled);
        Assert.Equal(3, stored.Version);
        Assert.Equal(1, stored.CreatedByUserId);
        Assert.Equal(3, stored.UpdatedByUserId);
    }

    [Fact]
    public async Task DeleteRemovesSecretAndMissingLookupFailsClosed()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await service.StoreAsync("db_password", "value");
        await service.DeleteAsync("db_password");

        Assert.Empty(await service.ListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync("db_password"));
    }

    [Fact]
    public async Task WrongKeyRingCannotResolveStoredSecret()
    {
        await using var db = NewDb();
        await NewService(db, new EphemeralDataProtectionProvider()).StoreAsync("db_password", "value");

        var differentProvider = new EphemeralDataProtectionProvider();
        var readService = NewService(db, differentProvider);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => readService.ResolveAsync("db_password"));
        Assert.Contains("cannot be decrypted", error.Message);
    }

    [Fact]
    public async Task UnencryptedPayloadFailsClosed()
    {
        await using var db = NewDb();
        db.PortalSecrets.Add(new PortalSecret
        {
            Name = "legacy",
            EncryptedValue = "plaintext",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = NewService(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync("legacy"));
        Assert.Contains("not encrypted", error.Message);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch
        {
            // best-effort test cleanup
        }
    }

    private PortalDbContext NewDb()
    {
        Directory.CreateDirectory(root);
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, Guid.NewGuid().ToString("N") + ".db")}")
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static PortalSecretStoreService NewService(
        PortalDbContext db,
        IDataProtectionProvider? provider = null)
        => new(db, provider ?? new EphemeralDataProtectionProvider());
}
