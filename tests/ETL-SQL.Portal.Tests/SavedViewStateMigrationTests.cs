using ETL_SQL.Core.Reporting;
using ETL_SQL.Portal.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
[Trait("CompatBreak", "0.22")]
public class SavedViewStateMigrationTests
{
    [Fact]
    public async Task SqliteUpgradePreservesSavedStateAndBackfillsFlatMaps()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        await SavedViewStateMigrationContract.VerifyAsync(new DbContextOptionsBuilder<PortalDbContext>().UseSqlite(connection).Options);
    }
}

[Trait("Category", "Integration")]
[Trait("CompatBreak", "0.22")]
public class SavedViewStatePostgresMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task PostgresUpgradePreservesSavedStateAndBackfillsFlatMaps()
    {
        await SavedViewStateMigrationContract.VerifyAsync(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), provider => provider.MigrationsAssembly("ETL-SQL.Portal.Migrations.Postgres")).Options);
    }
}

internal static class SavedViewStateMigrationContract
{
    internal static async Task VerifyAsync(DbContextOptions<PortalDbContext> options)
    {
        await using var db = new PortalDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.EndsWith("_ConsolidateSavedViewState", migrations[^1], StringComparison.Ordinal);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[^2], CancellationToken.None);
        var user = new PortalUser { UserName = "view-owner", IsActive = true };
        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken.None);
        var folder = new Folder { Name = "Views", Path = "/Views", OwnerId = user.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync(CancellationToken.None);
        var report = new Report { FolderId = folder.Id, Name = "Sales", ScriptPath = "sales.rptsql", CreatedBy = user.Id };
        db.Reports.Add(report);
        await db.SaveChangesAsync(CancellationToken.None);
        const string stateJson = """{"schemaVersion":1,"activePage":"Detail","parameters":{"@year":2026,"@live":true},"visible":{"Filters":false},"collapsed":{"Notes":true}}""";
        var canonical = new SavedReportView { ReportId = report.Id, UserId = user.Id, Name = "Canonical", StateJson = stateJson, ScriptHash = "original-hash", IsDefault = true };
        var flat = new SavedReportView { ReportId = report.Id, UserId = user.Id, Name = "Flat", ScriptHash = "flat-hash" };
        var empty = new SavedReportView { ReportId = report.Id, UserId = user.Id, Name = "Empty" };
        flat.CreatedAt = flat.UpdatedAt = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        db.SavedReportViews.AddRange(canonical, flat, empty);
        await db.SaveChangesAsync(CancellationToken.None);
        var createdAt = flat.CreatedAt;
        var updatedAt = flat.UpdatedAt;
        // The canonical envelope is authoritative even if abandoned flat columns are unreadable.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE saved_report_views SET parameters_json = {"invalid ignored blob"} WHERE id = {canonical.Id}", CancellationToken.None);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE saved_report_views SET parameters_json = {"not json"} WHERE id = {flat.Id}", CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(cancellationToken: CancellationToken.None));
        Assert.Contains("parameters_json", await ColumnsAsync(db));
        Assert.Contains("filters_json", await ColumnsAsync(db));
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync(CancellationToken.None));

        const string parameters = """{"Code":"001","year":"2026","live":"true","Empty":"","none":null,"region":"West","é":"中文🌿"}""";
        const string filters = """{"@Region":"East","category":"Hardware","@É":"override 中文🌿"}""";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE saved_report_views SET parameters_json = {parameters}, filters_json = {filters} WHERE id = {flat.Id}", CancellationToken.None);
        await migrator.MigrateAsync(cancellationToken: CancellationToken.None);
        await migrator.MigrateAsync(cancellationToken: CancellationToken.None);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(CancellationToken.None));
        Assert.DoesNotContain("parameters_json", await ColumnsAsync(db));
        Assert.DoesNotContain("filters_json", await ColumnsAsync(db));
        db.ChangeTracker.Clear();
        var rows = await db.SavedReportViews.OrderBy(value => value.Id).ToListAsync(CancellationToken.None);
        Assert.Equal(3, rows.Count);
        Assert.Equal(stateJson, rows[0].StateJson);
        Assert.Equal(canonical.Id, rows[0].Id);
        Assert.True(rows[0].IsDefault);
        var retained = rows.Single(value => value.Id == flat.Id);
        Assert.Equal(user.Id, retained.UserId);
        Assert.Equal(report.Id, retained.ReportId);
        Assert.Equal(createdAt, retained.CreatedAt);
        Assert.Equal(updatedAt, retained.UpdatedAt);
        var state = ResolvedReportState.FromJson(retained.StateJson);
        Assert.Equal("flat-hash", state.ScriptHash);
        Assert.Equal(8, state.Parameters.Count);
        Assert.Equal("001", state.Parameters["@Code"].StringValue);
        Assert.Equal("2026", state.Parameters["@year"].StringValue);
        Assert.Equal("true", state.Parameters["@live"].StringValue);
        Assert.Equal("", state.Parameters["@Empty"].StringValue);
        Assert.Equal(ReportStateValueKind.Null, state.Parameters["@none"].Kind);
        Assert.Equal("East", state.Parameters["@region"].StringValue);
        Assert.Equal("Hardware", state.Parameters["@category"].StringValue);
        Assert.Equal("override 中文🌿", state.Parameters["@é"].StringValue);
        Assert.Empty(ResolvedReportState.FromJson(rows.Single(value => value.Id == empty.Id).StateJson).Parameters);
        retained.StateJson = """{"schemaVersion":1,"parameters":{"@Code":"002"}}""";
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.Equal("002", ResolvedReportState.FromJson((await db.SavedReportViews.SingleAsync(value => value.Id == flat.Id, CancellationToken.None)).StateJson).Parameters["@Code"].StringValue);
    }

    private static async Task<string[]> ColumnsAsync(PortalDbContext db)
    {
        await db.Database.OpenConnectionAsync(CancellationToken.None);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = db.Database.IsSqlite() ? "PRAGMA table_info(saved_report_views)"
                : "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'saved_report_views'";
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            var columns = new List<string>();
            while (await reader.ReadAsync(CancellationToken.None)) columns.Add(reader.GetString(db.Database.IsSqlite() ? 1 : 0));
            return columns.ToArray();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
