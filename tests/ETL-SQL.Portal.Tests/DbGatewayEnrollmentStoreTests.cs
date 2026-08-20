using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class DbGatewayEnrollmentStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"gateway-enrollment-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;

    public DbGatewayEnrollmentStoreTests()
    {
        _services = new ServiceCollection()
            .AddDbContext<PortalDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PortalDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task EnrollmentPersistsAcrossStoreInstancesWithoutPersistingTheToken()
    {
        const string token = "one-time-enrollment-token-that-is-never-stored";
        var first = Store();
        var issued = await first.IssueAsync(
            "tenant-alpha", "gateway-1", token, DateTimeOffset.UtcNow.AddMinutes(10));

        var restarted = Store();
        var listed = await restarted.ListByTenantAsync("tenant-alpha");
        var persisted = Assert.Single(listed);
        Assert.Equal(issued.EnrollmentId, persisted.EnrollmentId);
        Assert.Equal(GatewayEnrollmentToken.Hash(token), persisted.TokenHash);
        await using (var stream = new FileStream(
            _dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            Assert.DoesNotContain(token, await reader.ReadToEndAsync(), StringComparison.Ordinal);
        }

        var consumed = await restarted.ConsumeAsync("tenant-alpha", token, new string('A', 64));
        Assert.Equal(GatewayEnrollmentState.Consumed, consumed.State);
        Assert.Equal(new string('A', 64), consumed.WorkloadPublicKeyThumbprint);

        var afterSecondRestart = await Store().FindByGatewayAsync("tenant-alpha", "gateway-1");
        Assert.Equal(GatewayEnrollmentState.Consumed, afterSecondRestart?.State);
    }

    [Fact]
    public async Task ConsumedTokenCannotBeReusedOrPresentedAcrossTenants()
    {
        const string token = "another-one-time-token-with-enough-entropy";
        var store = Store();
        await store.IssueAsync("tenant-alpha", "gateway-1", token, DateTimeOffset.UtcNow.AddMinutes(10));

        await Assert.ThrowsAsync<GatewayEnrollmentException>(() =>
            Store().ConsumeAsync("tenant-beta", token, new string('B', 64)));
        await store.ConsumeAsync("tenant-alpha", token, new string('A', 64));
        await Assert.ThrowsAsync<GatewayEnrollmentException>(() =>
            Store().ConsumeAsync("tenant-alpha", token, new string('B', 64)));
    }

    private DbGatewayEnrollmentStore Store() => new(_services.GetRequiredService<IServiceScopeFactory>());

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch (IOException) { }
        }
    }
}
