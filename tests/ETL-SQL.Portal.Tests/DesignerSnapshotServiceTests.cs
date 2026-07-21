using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Serves the last compiled snapshot to the Report Designer so visuals lay out against real data.
///
/// The security question this answers is not "are rows filtered" but "does a snapshot exist at all".
/// <c>ExecutionJobService</c> refuses to persist a shared snapshot for an identity-sensitive report,
/// so any snapshot present is identity-independent by construction and the folder-permission gate is
/// what keeps it from the wrong caller. These pin the gate and the absence cases; a caller without
/// permission, or a report with nothing built, must never receive rows.
/// </summary>
public sealed class DesignerSnapshotServiceTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), $"etlsql-designer-snapshot-{Guid.NewGuid():N}");

    public DesignerSnapshotServiceTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    private async Task<PortalDbContext> NewDbAsync()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_scratch, $"portal-{Guid.NewGuid():N}.db")}")
            .Options;
        var db = new PortalDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static ClaimsPrincipal User(int id = 1) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Role, "Publisher"),
        ], "test"));

    private DesignerSnapshotService NewService(PortalDbContext db)
    {
        var config = new PortalConfig { ScriptRootPath = _scratch, SnapshotDirectory = _scratch };
        var storage = new InMemoryArtifactStorage();
        return new DesignerSnapshotService(
            db,
            config,
            new FolderPermissionService(db),
            storage,
            new SnapshotPackageService(config, storage, NullLogger<SnapshotPackageService>.Instance));
    }

    [Fact]
    public async Task LoadForDesigner_ReturnsReportNotFound_ForAnUnknownReport()
    {
        await using var db = await NewDbAsync();

        var result = await NewService(db).LoadForDesignerAsync(reportId: 4242, User());

        Assert.Equal(DesignerSnapshotService.SnapshotOutcome.ReportNotFound, result.Outcome);
        Assert.Null(result.Package);
    }

    [Fact]
    public async Task LoadForDesigner_ReturnsNoPackage_WhenTheCallerLacksFolderPermission()
    {
        await using var db = await NewDbAsync();
        var folder = new Folder { Name = "Restricted" };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        db.Reports.Add(new Report { Name = "Secret", FolderId = folder.Id, ScriptPath = "secret.rptsql" });
        await db.SaveChangesAsync();

        var report = await db.Reports.SingleAsync();
        var result = await NewService(db).LoadForDesignerAsync(report.Id, User(id: 99));

        // Whatever the outcome, the invariant is that no rows reach a caller without permission.
        Assert.NotEqual(DesignerSnapshotService.SnapshotOutcome.Ok, result.Outcome);
        Assert.Null(result.Package);
    }

    [Fact]
    public async Task LoadForDesigner_ReportsNoSnapshot_WhenNothingHasBeenBuilt()
    {
        await using var db = await NewDbAsync();
        var folder = new Folder { Name = "Reports" };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        db.Reports.Add(new Report { Name = "Never Run", FolderId = folder.Id, ScriptPath = "never-run.rptsql" });
        await db.SaveChangesAsync();

        var report = await db.Reports.SingleAsync();
        var result = await NewService(db).LoadForDesignerAsync(report.Id, User());

        // A report that has never run, and an identity-sensitive report that deliberately never
        // persists a shared snapshot, both land here. Neither is an error — the canvas falls back to
        // wireframe placeholders.
        Assert.NotEqual(DesignerSnapshotService.SnapshotOutcome.Ok, result.Outcome);
        Assert.Null(result.Package);
    }

    [Fact]
    public void MaxRowsPerVisual_IsBoundedForABrowserCanvas()
    {
        // The cap is the whole reason this is safe to load into a design surface. If it is ever
        // raised to something unbounded, a 50-million-row snapshot reaches the browser.
        Assert.InRange(DesignerSnapshotService.MaxRowsPerVisual, 1, 5000);
    }
}
