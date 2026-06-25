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
}
