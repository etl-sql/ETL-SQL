using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Practical HA P1.2: proves the Portal runs on PostgreSQL end to end against a real Postgres
/// (Testcontainers, Docker-backed). The provider is selected by config, the PostgreSQL migration set
/// (from ETL-SQL.Portal.Migrations.Postgres) applies cleanly, and entities round-trip.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PortalPostgresProviderTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _pg.StartAsync();

    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    [Fact]
    public async Task PostgresProvider_AppliesMigrationsAndRoundTrips()
    {
        var config = new PortalConfig
        {
            Database = new PortalDatabaseConfig
            {
                Provider = "Postgres",
                ConnectionString = _pg.GetConnectionString()
            }
        };

        var builder = new DbContextOptionsBuilder<PortalDbContext>();
        PortalDatabase.Configure(builder, config);

        await using var db = new PortalDbContext(builder.Options);

        // The PostgreSQL migration set applies against the real database (provider-selected).
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // The applied migration came from the Postgres assembly's set.
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Contains(applied, m => m.EndsWith("InitialCreate"));

        // Round-trip an entity to prove the generated schema is usable.
        db.Groups.Add(new Group { Name = "finance", Description = "Finance team" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await db.Groups.SingleAsync(g => g.Name == "finance");
        Assert.Equal("Finance team", loaded.Description);

        var owner = new PortalUser
        {
            TenantId = "tenant-alpha",
            UserName = "service-owner",
            NormalizedUserName = "SERVICE-OWNER",
            IsActive = true
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        db.ServiceAccounts.Add(new ServiceAccount
        {
            Name = "postgres-runner",
            NormalizedName = "POSTGRES-RUNNER",
            ClientId = "sa_" + Guid.NewGuid().ToString("N"),
            OwnerUserId = owner.Id,
            SecretHash = "one-way-test-hash",
            Scopes = "portal.read"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var account = await db.ServiceAccounts.SingleAsync();
        Assert.Equal(owner.Id, account.OwnerUserId);
        Assert.Equal("portal.read", account.Scopes);

        var betaOwner = new PortalUser
        {
            TenantId = "tenant-beta",
            UserName = "service-owner",
            NormalizedUserName = "SERVICE-OWNER",
            IsActive = true
        };
        db.Users.Add(betaOwner);
        await db.SaveChangesAsync();
        db.Folders.AddRange(
            new Folder
            {
                TenantId = "tenant-alpha", Name = "Shared", Path = "/shared", OwnerId = owner.Id
            },
            new Folder
            {
                TenantId = "tenant-beta", Name = "Shared", Path = "/shared", OwnerId = betaOwner.Id
            });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Folders.CountAsync(folder => folder.Path == "/shared"));

        db.StewardshipSettings.AddRange(
            new StewardshipSettings { TenantId = "tenant-alpha" },
            new StewardshipSettings { TenantId = "tenant-beta" });
        db.StewardshipFindings.AddRange(
            new StewardshipFinding
            {
                TenantId = "tenant-alpha", AssetKey = "same.table", RuleKey = "same-rule"
            },
            new StewardshipFinding
            {
                TenantId = "tenant-beta", AssetKey = "same.table", RuleKey = "same-rule"
            });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.StewardshipSettings.CountAsync());
        Assert.Equal(2, await db.StewardshipFindings.CountAsync());

        const string sharedEventId = "same-audit-event";
        db.AuditOutboxMessages.AddRange(
            AuditOutbox("tenant-alpha", sharedEventId, "Delivered"),
            AuditOutbox("tenant-beta", sharedEventId, "Failed"));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.AuditOutboxMessages
            .CountAsync(row => row.EventId == sharedEventId));
    }

    private static AuditOutboxMessage AuditOutbox(
        string tenantId, string eventId, string status) => new()
    {
        TenantId = tenantId,
        EventId = eventId,
        Action = "POSTGRES_TENANT_COLLISION",
        PayloadJson = "{}",
        Status = status,
        OccurredAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task PostgresProvider_MigrationLock_SerializesConcurrentMigrationWork()
    {
        var config = new PortalConfig
        {
            Database = new PortalDatabaseConfig
            {
                Provider = "Postgres",
                ConnectionString = _pg.GetConnectionString()
            }
        };

        var builder = new DbContextOptionsBuilder<PortalDbContext>();
        PortalDatabase.Configure(builder, config);

        await using var firstDb = new PortalDbContext(builder.Options);
        await using var secondDb = new PortalDbContext(builder.Options);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = PortalDatabaseMigrationLock.RunExclusiveAsync(
            firstDb,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            async () =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(5));
            });

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = PortalDatabaseMigrationLock.RunExclusiveAsync(
            secondDb,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            () =>
            {
                secondEntered.SetResult();
                return Task.CompletedTask;
            });

        var early = await Task.WhenAny(secondEntered.Task, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.NotSame(secondEntered.Task, early);

        releaseFirst.SetResult();

        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
