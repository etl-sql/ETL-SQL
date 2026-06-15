using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// Practical HA P1.2: proves the Portal runs on PostgreSQL end to end against a real Postgres
/// (Testcontainers, Docker-backed). The provider is selected by config, the PostgreSQL migration set
/// (from ETL-SQL.ReportPortal.Migrations.Postgres) applies cleanly, and entities round-trip.
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
    }
}
