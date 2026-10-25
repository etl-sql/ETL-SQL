using ETL_SQL.Portal.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
[Trait("CompatBreak", "0.22")]
public class AlertInlineDeliveryMigrationTests
{
    [Fact]
    public async Task SqliteUpgradePreservesAlertDefinitionsAndNotificationAttachments()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        await AlertInlineDeliveryMigrationContract.VerifyUpgradeAsync(
            new DbContextOptionsBuilder<PortalDbContext>().UseSqlite(connection).Options);
    }
}

[Trait("Category", "Integration")]
[Trait("CompatBreak", "0.22")]
public class AlertInlineDeliveryPostgresMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task PostgresUpgradePreservesAlertDefinitionsAndNotificationAttachments()
    {
        await AlertInlineDeliveryMigrationContract.VerifyUpgradeAsync(
            new DbContextOptionsBuilder<PortalDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), options => options.MigrationsAssembly("ETL-SQL.Portal.Migrations.Postgres"))
                .Options);
    }
}

internal static class AlertInlineDeliveryMigrationContract
{
    internal static async Task VerifyUpgradeAsync(DbContextOptions<PortalDbContext> options)
    {
        await using var db = new PortalDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.EndsWith("_RemoveAlertInlineDelivery", migrations[^1], StringComparison.Ordinal);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[^2], CancellationToken.None);
        var oldColumns = await ColumnsAsync(db);
        Assert.Contains("recipient", oldColumns);
        Assert.Contains("smtp_alias", oldColumns);

        var owner = new PortalUser { UserName = "alert-owner", IsActive = true };
        db.Users.Add(owner);
        await db.SaveChangesAsync(CancellationToken.None);
        var folder = new Folder { Name = "Finance", Path = "/Finance", OwnerId = owner.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync(CancellationToken.None);
        var report = new Report { FolderId = folder.Id, Name = "Revenue", ScriptPath = "revenue.rptsql", CreatedBy = owner.Id };
        db.Reports.Add(report);
        await db.SaveChangesAsync(CancellationToken.None);
        var alert = new ReportAlert
        {
            ReportId = report.Id,
            OwnerId = owner.Id,
            Name = "RevenueFloor",
            VisualName = "Revenue",
            Operator = "<",
            Threshold = 100m,
            Description = "Existing alert",
            Notifications = [new AlertNotification { OrchestratorAlias = "operations", NotificationName = "FinanceOps" }]
        };
        db.ReportAlerts.Add(alert);
        await db.SaveChangesAsync(CancellationToken.None);
        var alertId = alert.Id;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE report_alerts SET recipient = {"retired@example.invalid"}, smtp_alias = {"retired-smtp"} WHERE id = {alertId}",
            CancellationToken.None);

        await migrator.MigrateAsync(cancellationToken: CancellationToken.None);
        await migrator.MigrateAsync(cancellationToken: CancellationToken.None);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(CancellationToken.None));
        var columns = await ColumnsAsync(db);
        Assert.DoesNotContain("recipient", columns);
        Assert.DoesNotContain("smtp_alias", columns);
        db.ChangeTracker.Clear();
        var retained = await db.ReportAlerts.Include(value => value.Notifications).SingleAsync(CancellationToken.None);
        Assert.Equal(alertId, retained.Id);
        Assert.Equal(report.Id, retained.ReportId);
        Assert.Equal(owner.Id, retained.OwnerId);
        Assert.Equal("RevenueFloor", retained.Name);
        Assert.Equal("Revenue", retained.VisualName);
        Assert.Equal("<", retained.Operator);
        Assert.Equal(100m, retained.Threshold);
        Assert.Equal("Existing alert", retained.Description);
        var notification = Assert.Single(retained.Notifications);
        Assert.Equal("operations", notification.OrchestratorAlias);
        Assert.Equal("FinanceOps", notification.NotificationName);

        retained.Threshold = 200m;
        retained.Notifications.Add(new AlertNotification { OrchestratorAlias = "operations", NotificationName = "AuditOps" });
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        var updated = await db.ReportAlerts.Include(value => value.Notifications).SingleAsync(CancellationToken.None);
        Assert.Equal(200m, updated.Threshold);
        Assert.Equal(new[] { "AuditOps", "FinanceOps" }, updated.Notifications.Select(value => value.NotificationName).Order().ToArray());
    }

    private static async Task<string[]> ColumnsAsync(PortalDbContext db)
    {
        await db.Database.OpenConnectionAsync(CancellationToken.None);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = db.Database.IsSqlite()
                ? "PRAGMA table_info(report_alerts)"
                : "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'report_alerts'";
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            var columns = new List<string>();
            while (await reader.ReadAsync(CancellationToken.None))
                columns.Add(reader.GetString(db.Database.IsSqlite() ? 1 : 0));
            return columns.ToArray();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
