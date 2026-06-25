using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Storage;
using ETL_SQL.Reporting;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public sealed class SnapshotPackageServiceTests
{
    [Fact]
    public async Task SaveAsync_WritesEncryptedPackage_AndLoadsManifest()
    {
        var storage = new InMemoryArtifactStorage();
        var config = new PortalConfig
        {
            Dataset = new DatasetConfig
            {
                AtRestKey = HostedPortalFactory.DefaultAtRestKey,
                AtRestKeyVersion = "v1"
            }
        };
        var service = new SnapshotPackageService(
            config,
            storage,
            NullLogger<SnapshotPackageService>.Instance);

        var manifest = CreateManifest("secret-customer-id-42");

        await service.SaveAsync(manifest, "report_1_job.etlsnap");

        var raw = await storage.ReadAllBytesAsync(ArtifactArea.Snapshots, "report_1_job.etlsnap");
        Assert.DoesNotContain("secret-customer-id-42", Encoding.UTF8.GetString(raw));

        var loaded = await service.LoadAsync("report_1_job.etlsnap");
        Assert.NotNull(loaded);
        Assert.Equal("secret-customer-id-42", loaded!.Visuals[0].Rows[0][0]);
    }

    [Fact]
    public async Task StartAsync_MigratesLegacySnapshotRows_AndDeletesPlaintextArtifact()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();

        var legacyKey = "legacy.snapshot.json";
        var legacyManifest = CreateManifest("legacy-secret-value");
        var legacyJson = JsonSerializer.Serialize(legacyManifest);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
            var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactStorage>();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            await artifacts.WriteAllTextAsync(ArtifactArea.Snapshots, legacyKey, legacyJson);

            var folder = new Folder { Name = "snapshots", Path = "/snapshots", OwnerId = 1 };
            var report = new Report
            {
                Folder = folder,
                Name = "Legacy Snapshot",
                ScriptPath = "legacy.rptsql",
                CreatedBy = 1
            };
            db.Add(report);
            await db.SaveChangesAsync();
            db.ReportSnapshots.Add(new ReportSnapshot
            {
                ReportId = report.Id,
                ManifestPath = Path.Combine(config.SnapshotDirectory, legacyKey),
                BuiltAt = DateTime.UtcNow,
                BuiltBy = 1
            });
            await db.SaveChangesAsync();
        }

        var migration = new SnapshotMigrationService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SnapshotMigrationService>.Instance);
        await migration.StartAsync(CancellationToken.None);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactStorage>();
            var packages = scope.ServiceProvider.GetRequiredService<SnapshotPackageService>();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var snapshot = await db.ReportSnapshots.SingleAsync();
            var packageKey = PortalPathGuard.ToSnapshotKey(
                scope.ServiceProvider.GetRequiredService<PortalConfig>(),
                snapshot.ManifestPath);

            Assert.NotNull(packageKey);
            Assert.EndsWith(".etlsnap", packageKey);
            Assert.False(await artifacts.ExistsAsync(ArtifactArea.Snapshots, legacyKey));
            Assert.True(await artifacts.ExistsAsync(ArtifactArea.Snapshots, packageKey!));

            var raw = await artifacts.ReadAllBytesAsync(ArtifactArea.Snapshots, packageKey!);
            Assert.DoesNotContain("legacy-secret-value", Encoding.UTF8.GetString(raw));

            var loaded = await packages.LoadAsync(packageKey!);
            Assert.Equal("legacy-secret-value", loaded!.Visuals[0].Rows[0][0]);
        }
    }

    [Fact]
    public async Task SaveAsync_StoresLargeVisualRowsAsArrow_AndRehydratesForReaders()
    {
        var storage = new InMemoryArtifactStorage();
        var config = new PortalConfig
        {
            Dataset = new DatasetConfig
            {
                AtRestKey = HostedPortalFactory.DefaultAtRestKey,
                AtRestKeyVersion = "v1"
            }
        };
        var service = new SnapshotPackageService(
            config,
            storage,
            NullLogger<SnapshotPackageService>.Instance);
        var manifest = CreateLargeManifest();

        await service.SaveAsync(manifest, "large.etlsnap");

        var entries = await service.ListPackageEntriesForTestsAsync("large.etlsnap");
        Assert.Contains("layout.json", entries);
        Assert.Contains(entries, e => e.StartsWith("tables/", StringComparison.Ordinal) && e.EndsWith(".arrow", StringComparison.Ordinal));

        var storedLayout = await service.ReadStoredLayoutJsonForTestsAsync("large.etlsnap");
        Assert.DoesNotContain("customer-09999", storedLayout);

        var loaded = await service.LoadAsync("large.etlsnap");
        Assert.NotNull(loaded);
        Assert.Equal(SnapshotPackageService.ArrowRowThreshold, loaded!.Visuals[0].Rows.Count);
        Assert.Equal("customer-00000", loaded.Visuals[0].Rows[0][0]);
        Assert.Equal("9999", loaded.Visuals[0].Rows[^1][1]);
        Assert.Equal("small-inline", loaded.Visuals[1].Rows[0][0]);
    }

    [Fact]
    public async Task LoadLightweightLayoutJsonAsync_UsesRowSource_ForArrowBackedVisuals()
    {
        var storage = new InMemoryArtifactStorage();
        var config = new PortalConfig
        {
            Dataset = new DatasetConfig
            {
                AtRestKey = HostedPortalFactory.DefaultAtRestKey,
                AtRestKeyVersion = "v1"
            }
        };
        var service = new SnapshotPackageService(
            config,
            storage,
            NullLogger<SnapshotPackageService>.Instance);
        var manifest = CreateLargeManifest();

        await service.SaveAsync(manifest, "large.etlsnap");

        var json = await service.LoadLightweightLayoutJsonAsync(
            "large.etlsnap",
            visualIndex => $"/snapshot/rows/{visualIndex}",
            visualIndex => $"/snapshot/rows/{visualIndex}.arrow");
        var lightweight = JsonSerializer.Deserialize<ReportManifest>(json);

        Assert.NotNull(lightweight);
        var large = lightweight!.Visuals[0];
        Assert.Empty(large.Rows);
        Assert.NotNull(large.RowsSource);
        Assert.Equal("json", large.RowsSource!.Format);
        Assert.Equal("/snapshot/rows/0", large.RowsSource.Url);
        Assert.Equal("/snapshot/rows/0.arrow", large.RowsSource.ArrowUrl);
        Assert.Equal(SnapshotPackageService.ArrowRowThreshold, large.RowsSource.RowCount);
        Assert.Equal(new[] { "CustomerId", "Amount" }, large.RowsSource.Columns);

        var small = lightweight.Visuals[1];
        Assert.Null(small.RowsSource);
        Assert.Equal("small-inline", small.Rows[0][0]);
    }

    [Fact]
    public async Task LoadRowsAsync_ReturnsRequestedArrowBackedRows_AndInlineRows()
    {
        var storage = new InMemoryArtifactStorage();
        var config = new PortalConfig
        {
            Dataset = new DatasetConfig
            {
                AtRestKey = HostedPortalFactory.DefaultAtRestKey,
                AtRestKeyVersion = "v1"
            }
        };
        var service = new SnapshotPackageService(
            config,
            storage,
            NullLogger<SnapshotPackageService>.Instance);
        var manifest = CreateLargeManifest();

        await service.SaveAsync(manifest, "large.etlsnap");

        var largeRows = await service.LoadRowsAsync("large.etlsnap", 0);
        Assert.NotNull(largeRows);
        Assert.Equal(SnapshotPackageService.ArrowRowThreshold, largeRows!.Rows.Count);
        Assert.Equal("customer-00000", largeRows.Rows[0][0]);
        Assert.Equal("9999", largeRows.Rows[^1][1]);

        var inlineRows = await service.LoadRowsAsync("large.etlsnap", 1);
        Assert.NotNull(inlineRows);
        Assert.Equal("small-inline", inlineRows!.Rows[0][0]);

        var arrow = await service.LoadArrowTableAsync("large.etlsnap", 0);
        Assert.NotNull(arrow);
        Assert.NotEmpty(arrow!);
        Assert.Null(await service.LoadArrowTableAsync("large.etlsnap", 1));
    }

    private static ReportManifest CreateManifest(string secretValue) => new()
    {
        Source = "secret.rptsql",
        Title = "Sensitive Report",
        Visuals =
        {
            new VisualManifest
            {
                Name = "SensitiveTable",
                VisualType = "TABLE",
                Columns = ["CustomerId"],
                Rows = [[secretValue]]
            }
        }
    };

    private static ReportManifest CreateLargeManifest() => new()
    {
        Source = "large.rptsql",
        Title = "Large Sensitive Report",
        Visuals =
        {
            new VisualManifest
            {
                Name = "LargeTable",
                VisualType = "TABLE",
                Columns = ["CustomerId", "Amount"],
                Rows = Enumerable.Range(0, SnapshotPackageService.ArrowRowThreshold)
                    .Select(i => new List<string?> { $"customer-{i:D5}", i.ToString() })
                    .ToList()
            },
            new VisualManifest
            {
                Name = "SmallTable",
                VisualType = "TABLE",
                Columns = ["Value"],
                Rows = [["small-inline"]]
            }
        }
    };
}
