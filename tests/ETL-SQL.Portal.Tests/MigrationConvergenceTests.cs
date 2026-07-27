using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

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
        string previousMigration;
        await using (var probe = new PortalDbContext(options))
        {
            var migrations = probe.Database.GetMigrations().ToList();
            Assert.True(migrations.Count >= 2, "Expected at least two migrations for an N-1 upgrade drill.");
            previousMigration = migrations[^2];
        }

        // Bring the catalog to the previous migration. This keeps the drill current as new migrations are
        // added: N-1 must be able to roll forward to HEAD without a hand-maintained migration name.
        await using (var db = new PortalDbContext(options))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(previousMigration);

            // The catalog is now out of date: HEAD migrations are still pending.
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

    [Fact]
    public async Task PortalMigrations_UpOperationsFollowRollingExpandContract()
    {
        var options = Options("contract.db");
        await using var db = new PortalDbContext(options);
        var provider = db.Database.ProviderName ?? "Microsoft.EntityFrameworkCore.Sqlite";
        var migrations = db.GetInfrastructure().GetRequiredService<IMigrationsAssembly>();

        var violations = new List<string>();
        foreach (var (id, typeInfo) in migrations.Migrations)
        {
            if (PreDeploymentBreakingMigrations.Any(m => id.EndsWith(m, StringComparison.Ordinal)))
                continue;

            var migration = migrations.CreateMigration(typeInfo, provider);
            foreach (var operation in migration.UpOperations)
            {
                if (IsRollingContractViolation(operation, out var reason))
                    violations.Add($"{id}: {operation.GetType().Name} - {reason}");
            }
        }

        Assert.Empty(violations);
        Assert.NotEmpty(migrations.Migrations);
    }

    [Fact]
    public async Task PortalDatabaseMigrationLock_SerializesConcurrentStartupMigrationWork()
    {
        var options = Options("migration-lock.db");
        await using var firstDb = new PortalDbContext(options);
        await using var secondDb = new PortalDbContext(options);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = PortalDatabaseMigrationLock.RunExclusiveAsync(
            firstDb,
            NullLogger.Instance,
            async () =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(5));
            });

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = PortalDatabaseMigrationLock.RunExclusiveAsync(
            secondDb,
            NullLogger.Instance,
            () =>
            {
                secondEntered.SetResult();
                return Task.CompletedTask;
            });

        var early = await Task.WhenAny(secondEntered.Task, Task.Delay(TimeSpan.FromMilliseconds(250)));
        Assert.NotSame(secondEntered.Task, early);

        releaseFirst.SetResult();

        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Migrations deliberately exempted from the rolling-expand contract, by migration-name suffix.
    /// </summary>
    /// <remarks>
    /// The contract protects mixed-version clusters during a rolling deploy. It is waived only
    /// while the product has no deployments to roll — an exemption that stops being available the
    /// moment anyone installs this. Each entry must say what it drops and why deferring it to a
    /// later release was worse than taking the break now.
    /// <para>
    /// This is deliberately a narrow allow-list rather than a relaxed rule: every future drop still
    /// has to fail this test and be argued for explicitly.
    /// </para>
    /// </remarks>
    private static readonly string[] PreDeploymentBreakingMigrations =
    [
        // Drops SmtpConnections, superseded by the governed connection catalog. Deferring would
        // carry a dead table plus the entity, DbSet and model configuration that must stay wired
        // to it — and removing those without the migration makes the model diverge from the schema
        // and SchemaUpToDate report false. No installation exists to roll, so the contract guards
        // nothing here.
        "_DropSmtpConnections",
    ];

    private static bool IsRollingContractViolation(MigrationOperation operation, out string reason)
    {
        reason = operation switch
        {
            DropTableOperation => "drops a table during Up",
            DropColumnOperation => "drops a column during Up",
            RenameTableOperation => "renames a table during Up",
            RenameColumnOperation => "renames a column during Up",
            AlterColumnOperation => "alters an existing column during Up",
            AddColumnOperation add when !add.IsNullable
                && add.DefaultValue is null
                && string.IsNullOrWhiteSpace(add.DefaultValueSql)
                && string.IsNullOrWhiteSpace(add.ComputedColumnSql)
                => "adds a required column without a server/default value",
            _ => ""
        };
        return reason.Length > 0;
    }
}
