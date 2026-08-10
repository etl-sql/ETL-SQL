using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Observability;
using ETL_SQL.Core.Security;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class SnapshotPackageServiceTests
{
    [Fact]
    public async Task ProviderBackedArtifactEncryptionUsesArtifactPurposeAndSafeVersionEnvelope()
    {
        var storage = new InMemoryArtifactStorage();
        var config = new PortalConfig { TenantId = "tenant-alpha" };
        var v1 = new KeyMaterialDescriptor(
            "test-vault", "artifact-alpha", "tenant-alpha", KeyPurpose.Artifact, "v1");
        var writerKeys = new ResolvedKeyMaterialProvider("test-vault",
            [(v1, Enumerable.Repeat((byte)41, 32).ToArray())]);
        var writer = new SnapshotPackageService(
            config, storage, NullLogger<SnapshotPackageService>.Instance, writerKeys);

        await writer.SaveAsync(CreateManifest("provider-secret"), "provider.etlsnap");

        var raw = await storage.ReadAllBytesAsync(ArtifactArea.Snapshots, "provider.etlsnap");
        Assert.DoesNotContain("provider-secret", Encoding.UTF8.GetString(raw));
        Assert.Contains(Encoding.UTF8.GetBytes("v1"), raw);
        Assert.Equal("provider-secret", (await writer.LoadAsync("provider.etlsnap"))!.Visuals[0].Rows[0][0]);

        var datasetOnly = new ResolvedKeyMaterialProvider("test-vault",
        [
            (new KeyMaterialDescriptor(
                "test-vault", "dataset-alpha", "tenant-alpha", KeyPurpose.Dataset, "v1"),
             Enumerable.Repeat((byte)41, 32).ToArray())
        ]);
        var wrongPurpose = new SnapshotPackageService(
            config, storage, NullLogger<SnapshotPackageService>.Instance, datasetOnly);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => wrongPurpose.LoadAsync("provider.etlsnap"));
    }

    [Fact]
    public async Task ProviderBackedArtifactReadsPreviousVersionDuringRotation()
    {
        var storage = new InMemoryArtifactStorage();
        var config = new PortalConfig { TenantId = "tenant-alpha" };
        var previous = new KeyMaterialDescriptor(
            "vault", "artifact-v1", "tenant-alpha", KeyPurpose.Artifact, "v1", IsCurrent: false);
        var current = new KeyMaterialDescriptor(
            "vault", "artifact-v2", "tenant-alpha", KeyPurpose.Artifact, "v2");
        var v1Bytes = Enumerable.Repeat((byte)11, 32).ToArray();
        var writer = new SnapshotPackageService(config, storage,
            NullLogger<SnapshotPackageService>.Instance,
            new ResolvedKeyMaterialProvider("vault", [(previous with { IsCurrent = true }, v1Bytes)]));
        await writer.SaveAsync(CreateManifest("rotation-secret"), "rotation.etlsnap");

        var reader = new SnapshotPackageService(config, storage,
            NullLogger<SnapshotPackageService>.Instance,
            new ResolvedKeyMaterialProvider("vault",
            [
                (previous, v1Bytes),
                (current, Enumerable.Repeat((byte)22, 32).ToArray())
            ]));

        Assert.Equal("rotation-secret", (await reader.LoadAsync("rotation.etlsnap"))!.Visuals[0].Rows[0][0]);
    }

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
    public async Task SaveAsync_WithNoAtRestKey_UsesHostBoundMachineEncryption_AndRoundTrips()
    {
        // When no portal-managed key is configured, snapshots must fall back to host-bound
        // ENCRYPT=MACHINE protection (the same as dataset caches) — NOT a source-public constant key.
        var storage = new InMemoryArtifactStorage();
        var config = new PortalConfig { Dataset = new DatasetConfig { AtRestKey = null } };
        var service = new SnapshotPackageService(
            config,
            storage,
            NullLogger<SnapshotPackageService>.Instance);

        var manifest = CreateManifest("secret-customer-id-42");
        await service.SaveAsync(manifest, "report_1_job.etlsnap");

        var raw = await storage.ReadAllBytesAsync(ArtifactArea.Snapshots, "report_1_job.etlsnap");
        // Payload is encrypted (no plaintext secret) and is NOT the ETLSNAP1 keyed envelope.
        Assert.DoesNotContain("secret-customer-id-42", Encoding.UTF8.GetString(raw));
        Assert.False(raw.AsSpan(0, Math.Min(8, raw.Length)).SequenceEqual("ETLSNAP1"u8));

        // Round-trips on the same host.
        var loaded = await service.LoadAsync("report_1_job.etlsnap");
        Assert.NotNull(loaded);
        Assert.Equal("secret-customer-id-42", loaded!.Visuals[0].Rows[0][0]);
    }

    [Fact]
    public async Task LoadAsync_KeyedPackage_FailsClosed_WhenKeyLaterRemoved()
    {
        // A package written with a configured key must not silently decrypt once the key is removed.
        var storage = new InMemoryArtifactStorage();
        var keyed = new SnapshotPackageService(
            new PortalConfig { Dataset = new DatasetConfig { AtRestKey = HostedPortalFactory.DefaultAtRestKey, AtRestKeyVersion = "v1" } },
            storage,
            NullLogger<SnapshotPackageService>.Instance);
        await keyed.SaveAsync(CreateManifest("secret"), "report_1_job.etlsnap");

        var noKey = new SnapshotPackageService(
            new PortalConfig { Dataset = new DatasetConfig { AtRestKey = null } },
            storage,
            NullLogger<SnapshotPackageService>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => noKey.LoadAsync("report_1_job.etlsnap"));
    }

    [Fact]
    public async Task StartAsync_MigratesLegacySnapshotRows_AndDeletesPlaintextArtifact()
    {
        using var telemetry = new BackgroundTelemetryCapture();
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

        Assert.Contains(telemetry.Activities, activity =>
            activity.OperationName == "background_service.run"
            && Tag(activity, ObservabilityConventions.Tags.ServiceName) == "snapshot-migration"
            && Tag(activity, BackgroundServiceObservability.OperationTag) == "startup_migration"
            && Tag(activity, ObservabilityConventions.Tags.Status) == "success"
            && Tag(activity, ObservabilityConventions.Tags.RowsProcessed) == "1");
        Assert.Contains(telemetry.Measurements, measurement =>
            measurement.Name == "etlsql.background_service.run.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.ServiceName, "snapshot-migration")
            && HasTag(measurement.Tags, BackgroundServiceObservability.OperationTag, "startup_migration")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "success"));
        Assert.DoesNotContain(telemetry.Measurements, measurement => measurement.Tags.Any(tag =>
            tag.Value is string value && value.Contains("legacy-secret-value", StringComparison.OrdinalIgnoreCase)));
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

    private sealed class BackgroundTelemetryCapture : IDisposable
    {
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;

        public List<Activity> Activities { get; } = [];
        public List<(string Name, double Value, Dictionary<string, object?> Tags)> Measurements { get; } = [];

        public BackgroundTelemetryCapture()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == BackgroundServiceObservability.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => Activities.Add(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == BackgroundServiceObservability.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Measurements.Add((instrument.Name, value, ToDictionary(tags))));
            _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Measurements.Add((instrument.Name, value, ToDictionary(tags))));
            _meterListener.Start();
        }

        public void Dispose()
        {
            _activityListener.Dispose();
            _meterListener.Dispose();
        }
    }

    private static string? Tag(Activity activity, string key)
    {
        var value = activity.TagObjects.FirstOrDefault(t => t.Key == key).Value;
        return value?.ToString();
    }

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>();
        foreach (var tag in tags)
            result[tag.Key] = tag.Value;
        return result;
    }

    private static bool HasTag(Dictionary<string, object?> tags, string key, object value) =>
        tags.TryGetValue(key, out var actual) && Equals(actual, value);
}
