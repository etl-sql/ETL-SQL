using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// Operator Tooling Phase 3 (P2.3): automatic SQLite schema migrations converge any catalog to HEAD
/// on startup — both a fresh (empty) database and an out-of-date database seeded at the previous
/// release — and the operational metrics snapshot reports the resulting migration status so an
/// operator can confirm the schema is current without shell access.
/// </summary>
[Trait("Category", "Portal")]
public sealed class MigrationConvergenceTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "migration_converge_" + Guid.NewGuid().ToString("N")[..8]);

    public MigrationConvergenceTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    private DbContextOptions<PortalDbContext> Options(string fileName) =>
        new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_scratch, fileName)}")
            .Options;

    private PortalConfig MetricsConfig() => new()
    {
        DatasetRootPath = _scratch,
        SnapshotDirectory = _scratch,
        Resources = new ResourcesConfig()
    };

    [Fact]
    public async Task FreshDatabase_MigratesToHead_AndReportsSchemaUpToDate()
    {
        var options = Options("fresh.db");
        await using var db = new PortalDbContext(options);

        // A brand-new database starts with every migration pending.
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var m = await new OperationalMetricsService(db, MetricsConfig()).GetAsync();
        Assert.True(m.SchemaUpToDate);
        Assert.Equal(0, m.PendingMigrations);
        Assert.True(m.AppliedMigrations > 0);
        Assert.False(string.IsNullOrEmpty(m.LastAppliedMigration));
    }

    [Fact]
    public async Task OutOfDateDatabase_AtPreviousRelease_ConvergesToHeadOnMigrate()
    {
        var options = Options("legacy.db");

        // Bring the catalog to the previous release (the migration immediately before this release's two).
        await using (var db = new PortalDbContext(options))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync("AddDurablePortalExecutionJobs");

            // The catalog is now out of date: this release's migrations are still pending.
            Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());
        }

        // A subsequent startup migrates forward in place and converges to HEAD.
        await using (var db = new PortalDbContext(options))
        {
            await db.Database.MigrateAsync();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            var m = await new OperationalMetricsService(db, MetricsConfig()).GetAsync();
            Assert.True(m.SchemaUpToDate);
            Assert.Equal(0, m.PendingMigrations);
        }
    }
}
