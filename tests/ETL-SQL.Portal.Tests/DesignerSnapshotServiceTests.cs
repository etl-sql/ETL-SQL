using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
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

    private DesignerSnapshotService NewService(PortalDbContext db) => NewServices(db).Service;

    private TestServices NewServices(PortalDbContext db)
    {
        var config = new PortalConfig { ScriptRootPath = _scratch, SnapshotDirectory = _scratch };
        var storage = new InMemoryArtifactStorage();
        var packages = new SnapshotPackageService(config, storage, NullLogger<SnapshotPackageService>.Instance);
        var service = new DesignerSnapshotService(
            db,
            config,
            new FolderPermissionService(db),
            storage,
            packages);
        return new TestServices(service, packages, config);
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
    public async Task LoadForDesigner_ReturnsInlineRows_WithHonestSamplingMetadata()
    {
        await using var db = await NewDbAsync();
        var services = NewServices(db);
        var builtAt = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
        var report = await CreateReportSnapshotAsync(
            db,
            services,
            "inline.etlsnap",
            builtAt,
            CreateManifest("InlineSales", 600));

        var result = await services.Service.LoadForDesignerAsync(report.Id, User());

        Assert.Equal(DesignerSnapshotService.SnapshotOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Package);
        Assert.Equal("Snapshot Report", result.Package!.ReportName);
        Assert.Equal(builtAt, result.Package.BuiltAt);
        Assert.True(result.Package.Metadata.IsSampled);
        Assert.Equal(600, result.Package.Metadata.TotalRows);
        Assert.Equal(DesignerSnapshotService.MaxRowsPerVisual, result.Package.Metadata.ReturnedRows);
        Assert.False(result.Package.Metadata.RlsEnforced);
        Assert.Equal(new[] { "CustomerId", "Amount" }, result.Package.Columns["InlineSales"]);
        Assert.Equal("customer-00000", result.Package.SampleRows["InlineSales"][0][0]);
        Assert.Equal("499", result.Package.SampleRows["InlineSales"][^1][1]);
    }

    [Fact]
    public async Task LoadForDesigner_HonorsDirectReportAcl()
    {
        await using var db = await NewDbAsync();
        var services = NewServices(db);
        var report = await CreateReportSnapshotAsync(
            db,
            services,
            "direct-acl.etlsnap",
            new DateTime(2026, 7, 22, 12, 30, 0, DateTimeKind.Utc),
            CreateManifest("DirectAcl", 1));
        db.Users.Add(new PortalUser
        {
            Id = 99,
            UserName = "direct-reader",
            NormalizedUserName = "DIRECT-READER",
            Email = "direct-reader@example.invalid"
        });
        db.ReportAcls.Add(new ReportAcl
        {
            ReportId = report.Id,
            UserId = 99,
            Permission = FolderPermission.Read
        });
        await db.SaveChangesAsync();

        var result = await services.Service.LoadForDesignerAsync(report.Id, User(id: 99));

        Assert.Equal(DesignerSnapshotService.SnapshotOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Package);
    }

    [Fact]
    public async Task LoadForDesigner_ReturnsArrowBackedRows_WithHonestSamplingMetadata()
    {
        await using var db = await NewDbAsync();
        var services = NewServices(db);
        var report = await CreateReportSnapshotAsync(
            db,
            services,
            "arrow.etlsnap",
            new DateTime(2026, 7, 22, 13, 0, 0, DateTimeKind.Utc),
            CreateManifest("ArrowSales", SnapshotPackageService.ArrowRowThreshold));

        var result = await services.Service.LoadForDesignerAsync(report.Id, User());

        Assert.Equal(DesignerSnapshotService.SnapshotOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Package);
        Assert.True(result.Package!.Metadata.IsSampled);
        Assert.Equal(SnapshotPackageService.ArrowRowThreshold, result.Package.Metadata.TotalRows);
        Assert.Equal(DesignerSnapshotService.MaxRowsPerVisual, result.Package.Metadata.ReturnedRows);
        Assert.Equal(new[] { "CustomerId", "Amount" }, result.Package.Columns["ArrowSales"]);
        Assert.Equal("customer-00000", result.Package.SampleRows["ArrowSales"][0][0]);
        Assert.Equal("499", result.Package.SampleRows["ArrowSales"][^1][1]);
    }

    [Fact]
    public void MaxRowsPerVisual_IsBoundedForABrowserCanvas()
    {
        // The cap is the whole reason this is safe to load into a design surface. If it is ever
        // raised to something unbounded, a 50-million-row snapshot reaches the browser.
        Assert.InRange(DesignerSnapshotService.MaxRowsPerVisual, 1, 5000);
    }

    private async Task<Report> CreateReportSnapshotAsync(
        PortalDbContext db,
        TestServices services,
        string key,
        DateTime builtAt,
        ReportManifest manifest)
    {
        await services.Packages.SaveAsync(manifest, key);

        var folder = new Folder { Name = "Reports", Path = "/reports", OwnerId = 1 };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var report = new Report
        {
            Name = "Snapshot Report",
            FolderId = folder.Id,
            ScriptPath = "snapshot.rptsql",
            CreatedBy = 1
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        db.ReportSnapshots.Add(new ReportSnapshot
        {
            ReportId = report.Id,
            ManifestPath = Path.Combine(services.Config.SnapshotDirectory, key),
            BuiltAt = builtAt,
            BuiltBy = 1
        });
        await db.SaveChangesAsync();
        return report;
    }

    [Fact]
    public async Task LoadForDesigner_PopulatesVisualSvgs_WhenVisualHasNativeSvg()
    {
        await using var db = await NewDbAsync();
        var services = NewServices(db);
        var manifest = new ReportManifest
        {
            Source = "snapshot.rptsql",
            Title = "Snapshot Report",
            Visuals =
            {
                new VisualManifest
                {
                    Name = "ChartVisual",
                    VisualType = "BAR",
                    NativeSvg = "<svg viewBox=\"0 0 600 350\"><rect x=\"10\" y=\"10\" width=\"100\" height=\"100\"/></svg>",
                    Columns = ["Category", "Value"],
                    Rows = [new List<string?> { "A", "10" }]
                }
            }
        };

        var report = await CreateReportSnapshotAsync(
            db,
            services,
            "chart-svg.etlsnap",
            new DateTime(2026, 7, 22, 14, 0, 0, DateTimeKind.Utc),
            manifest);

        var result = await services.Service.LoadForDesignerAsync(report.Id, User());

        Assert.Equal(DesignerSnapshotService.SnapshotOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Package);
        Assert.NotNull(result.Package!.VisualSvgs);
        Assert.True(result.Package.VisualSvgs.ContainsKey("ChartVisual"));
        Assert.Contains("<svg", result.Package.VisualSvgs["ChartVisual"], StringComparison.Ordinal);
    }

    private static ReportManifest CreateManifest(string visualName, int rowCount) => new()
    {
        Source = "snapshot.rptsql",
        Title = "Snapshot Report",
        Visuals =
        {
            new VisualManifest
            {
                Name = visualName,
                VisualType = "TABLE",
                Columns = ["CustomerId", "Amount"],
                Rows = Enumerable.Range(0, rowCount)
                    .Select(i => new List<string?> { $"customer-{i:D5}", i.ToString() })
                    .ToList()
            }
        }
    };

    private sealed record TestServices(
        DesignerSnapshotService Service,
        SnapshotPackageService Packages,
        PortalConfig Config);
}
