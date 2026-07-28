using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;
using ETL_SQL.Core.Storage;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// P2.4 fault injection and recovery: reconciliation is idempotent and preserves the last
/// known-good state, tolerates a file it cannot delete (a busy/locked file) without crashing, and
/// the Orchestrator poller degrades safely when its database is unavailable. It also covers
/// disk-pressure-style snapshot write failures at the storage boundary. Deterministic, fast-lane
/// scenarios; non-deterministic faults (true volume exhaustion, network partition, clock skew)
/// belong to a separate chaos/integration harness and are tracked as residual.
/// </summary>
[Trait("Category", "Portal")]
public sealed class FaultInjectionRecoveryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fault_recovery_" + Guid.NewGuid().ToString("N")[..8]);

    public FaultInjectionRecoveryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private (PortalDbContext Db, PortalConfig Config, string DatasetRoot) NewDatasetDb()
    {
        var datasetRoot = Path.Combine(_root, "datasets");
        Directory.CreateDirectory(datasetRoot);
        var config = new PortalConfig { DatasetRootPath = datasetRoot };
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal.db")}")
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();
        return (db, config, datasetRoot);
    }

    private static Dataset ManagedRow(string name, string parquetPath) => new()
    {
        Name = name,
        FolderPath = "/",
        ParquetFilePath = parquetPath,
        AccessLevel = DatasetAccessLevel.Private
    };

    /// <summary>
    /// Reconciliation removes the crash artifacts once, then a second pass is a no-op that still
    /// preserves the referenced (last known-good) cache and its catalog row.
    /// </summary>
    [Fact]
    public async Task DatasetReconcile_IsIdempotent_AndPreservesLastKnownGood()
    {
        var (db, config, datasetRoot) = NewDatasetDb();
        await using var _ = db;

        var goodPath = Path.Combine(datasetRoot, "good_1.parquet");
        var orphanPath = Path.Combine(datasetRoot, "orphan_998.parquet");
        var stagingPath = Path.Combine(datasetRoot, ".good_1.parquet.tmp-crash");
        await File.WriteAllTextAsync(goodPath, "good");
        await File.WriteAllTextAsync(orphanPath, "orphan");
        await File.WriteAllTextAsync(stagingPath, "half-written");

        db.Datasets.AddRange(
            ManagedRow("#good", goodPath),
            ManagedRow("#missing", Path.Combine(datasetRoot, "missing_2.parquet")));
        await db.SaveChangesAsync();

        await DatasetStorageMaintenance.ReconcileAsync(db, config, NullLogger.Instance, deepOrphanScan: true);

        // First pass: artifacts gone, last known-good intact.
        Assert.True(File.Exists(goodPath));
        Assert.False(File.Exists(orphanPath));
        Assert.False(File.Exists(stagingPath));
        Assert.Equal(1, await db.Datasets.CountAsync());
        Assert.True(await db.Datasets.AnyAsync(d => d.Name == "#good"));

        // Second pass: a true no-op — no throw, nothing removed, good cache and row preserved.
        var goodWriteTime = File.GetLastWriteTimeUtc(goodPath);
        await DatasetStorageMaintenance.ReconcileAsync(db, config, NullLogger.Instance, deepOrphanScan: true);
        Assert.True(File.Exists(goodPath));
        Assert.Equal(goodWriteTime, File.GetLastWriteTimeUtc(goodPath));
        Assert.Equal(1, await db.Datasets.CountAsync());
    }

    /// <summary>
    /// A file the reconciler cannot delete (held open, deny-share) must not abort the sweep: the
    /// referenced cache survives and reconciliation completes without throwing.
    /// </summary>
    [Fact]
    public async Task DatasetReconcile_ToleratesHeldOpenFile_WithoutCrashing()
    {
        var (db, config, datasetRoot) = NewDatasetDb();
        await using var _ = db;

        var goodPath = Path.Combine(datasetRoot, "good_1.parquet");
        var lockedOrphan = Path.Combine(datasetRoot, "locked_997.parquet");
        await File.WriteAllTextAsync(goodPath, "good");
        await File.WriteAllTextAsync(lockedOrphan, "locked");
        db.Datasets.Add(ManagedRow("#good", goodPath));
        await db.SaveChangesAsync();

        // Hold the orphan open with no share: on Windows this blocks deletion; the sweep must
        // log-and-continue rather than throw.
        using (var hold = new FileStream(lockedOrphan, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = await Record.ExceptionAsync(() =>
                DatasetStorageMaintenance.ReconcileAsync(db, config, NullLogger.Instance, deepOrphanScan: true));
            Assert.Null(ex);
        }

        // The referenced cache is never affected by a fault elsewhere in the sweep.
        Assert.True(File.Exists(goodPath));
        Assert.True(await db.Datasets.AnyAsync(d => d.Name == "#good"));
    }

    /// <summary>
    /// When the Orchestrator database is unreadable (here, a corrupt/non-SQLite file) the poller
    /// degrades to cached-only mode: a poll completes without throwing rather than failing the
    /// background loop. The corrupt file is resolved first, so the global default DB is not consulted.
    /// </summary>
    [Fact]
    public async Task OrchestratorPoller_DegradesWhenOrchestratorDbUnreadable()
    {
        var stoppedActivities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BackgroundServiceObservability.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stoppedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BackgroundServiceObservability.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.Start();

        using var factory = new PortalWebFactory();
        _ = factory.CreateClient(); // build the host, apply migrations

        var corruptOrchDb = Path.Combine(_root, "corrupt-orch.db");
        await File.WriteAllTextAsync(corruptOrchDb, "this is not a sqlite database");
        var degradedConfig = new PortalConfig
        {
            DatabasePath = Path.Combine(_root, "portal.db"),
            Orchestrator = new OrchestratorConfig { DatabasePath = corruptOrchDb }
        };

        var poller = ActivatorUtilities.CreateInstance<OrchestratorPollerService>(
            factory.Services, new OrchestratorDbLocator(degradedConfig));
        var ex = await Record.ExceptionAsync(() => poller.PollAsync(CancellationToken.None));
        Assert.Null(ex);

        Assert.Contains(stoppedActivities, activity =>
            activity.OperationName == "background_service.run"
            && Tag(activity, ObservabilityConventions.Tags.Component) == "portal"
            && Tag(activity, ObservabilityConventions.Tags.WorkloadKind) == "background"
            && Tag(activity, ObservabilityConventions.Tags.ServiceName) == "orchestrator-poller"
            && Tag(activity, BackgroundServiceObservability.OperationTag) == "poll"
            && Tag(activity, ObservabilityConventions.Tags.Status) == "degraded");
        Assert.Contains(measurements, measurement => measurement.Name == "etlsql.background_service.run.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.ServiceName, "orchestrator-poller")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "degraded"));
        Assert.DoesNotContain(measurements, measurement => measurement.Tags.Any(tag =>
            tag.Value is string value
            && value.Contains(corruptOrchDb, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task OrchestratorPoller_RefreshesNormalizedReportJobLinkBeforeLegacyDatasetJob()
    {
        using var factory = new PortalWebFactory();
        _ = factory.CreateClient(); // build the host and apply migrations

        var scriptPath = Path.Combine(factory.TempDir, "scripts", "normalized_refresh.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Poller Normalized Refresh';");
        const string jobName = "shared_refresh_job";
        int normalizedReportId;
        int legacyReportId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var folder = new Folder { Name = "Poller", Path = "/poller", OwnerId = 1 };
            var normalizedReport = new Report
            {
                Folder = folder,
                Name = "Normalized",
                ScriptPath = scriptPath,
                CreatedBy = 1
            };
            var legacyReport = new Report
            {
                Folder = folder,
                Name = "Legacy",
                ScriptPath = scriptPath,
                CreatedBy = 1
            };
            db.Reports.AddRange(normalizedReport, legacyReport);
            await db.SaveChangesAsync();

            normalizedReportId = normalizedReport.Id;
            legacyReportId = legacyReport.Id;
            db.ReportJobLinks.Add(new ReportJobLink
            {
                ReportId = normalizedReportId,
                OrchestratorAlias = "default",
                JobName = jobName
            });
            db.DatasetJobs.Add(new DatasetJob
            {
                ReportId = legacyReportId,
                OrchestratorJobName = jobName,
                RefreshInterval = "Hourly"
            });
            await db.SaveChangesAsync();
        }

        var store = factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.InitializeAsync();
        var runId = await store.LogJobStartAsync(jobName);
        await store.LogJobEndAsync(runId, "SUCCESS");

        var poller = ActivatorUtilities.CreateInstance<OrchestratorPollerService>(factory.Services);
        await poller.PollAsync(CancellationToken.None);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var queued = await verifyDb.PortalExecutionJobs.SingleAsync(j => j.Kind == "Refresh");
        Assert.Equal(normalizedReportId, queued.ReportId);
        Assert.Equal(0, queued.UserId);

        var jobs = factory.Services.GetRequiredService<ExecutionJobService>();
        var inMemoryJob = await jobs.GetAsync(queued.Id);
        Assert.NotNull(inMemoryJob);
        Assert.True(inMemoryJob!.TrustedDatasetExecution);

        var link = await verifyDb.ReportJobLinks.SingleAsync(j => j.ReportId == normalizedReportId);
        var legacy = await verifyDb.DatasetJobs.SingleAsync(j => j.ReportId == legacyReportId);
        Assert.NotNull(link.LastRefreshedAt);
        Assert.Null(legacy.LastRefreshedAt);
    }

    [Fact]
    public async Task OrchestratorPoller_IgnoresUnlinkedScriptCompletion()
    {
        using var factory = new PortalWebFactory();
        _ = factory.CreateClient(); // build the host and apply migrations

        var store = factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.InitializeAsync();
        var runId = await store.LogJobStartAsync("standalone_script_job");
        await store.LogJobEndAsync(runId, "SUCCESS");

        var poller = ActivatorUtilities.CreateInstance<OrchestratorPollerService>(factory.Services);
        await poller.PollAsync(CancellationToken.None);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await verifyDb.PortalExecutionJobs.AnyAsync());
    }

    /// <summary>
    /// Storage unavailability must fail the load-balancer probe closed. The richer /health endpoint
    /// is for operators; /healthz is the traffic gate and should return 503 as soon as shared
    /// artifact storage cannot be enumerated.
    /// </summary>
    [Fact]
    public async Task Healthz_ReturnsUnavailableWhenSharedStorageFails()
    {
        using var factory = new StorageOutagePortalFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("Unhealthy", body!["status"]!.GetValue<string>());
        var checks = body["checks"]!.AsObject();
        Assert.Equal("ok", checks["database"]!.GetValue<string>());
        Assert.Equal(nameof(InvalidOperationException), checks["storage"]!.GetValue<string>());
        Assert.Equal("ok", checks["lease"]!.GetValue<string>());
    }

    /// <summary>
    /// A disk-pressure style write failure while saving the report snapshot must fail closed:
    /// the execution job is terminally failed, refresh status records the storage error, and no
    /// ReportSnapshots row is inserted for a manifest that was never durably written.
    /// </summary>
    [Fact]
    public async Task ExecutionSnapshotWriteFailure_FailsJobWithoutSnapshotRow()
    {
        var scriptRoot = Path.Combine(_root, "scripts");
        var snapshotRoot = Path.Combine(_root, "snapshots");
        Directory.CreateDirectory(scriptRoot);
        Directory.CreateDirectory(snapshotRoot);
        var scriptPath = Path.Combine(scriptRoot, "disk_pressure.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Disk Pressure';");

        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(_root, "portal-exec.db"),
            ScriptRootPath = scriptRoot,
            SnapshotDirectory = snapshotRoot,
            DatasetRootPath = Path.Combine(_root, "datasets"),
            Dataset = new DatasetConfig
            {
                AtRestKey = HostedPortalFactory.DefaultAtRestKey,
                AtRestKeyVersion = "v1"
            }
        };
        var services = new ServiceCollection()
            .AddDbContext<PortalDbContext>(options =>
                options.UseSqlite($"Data Source={config.DatabasePath}"))
            .BuildServiceProvider();
        await using var provider = services;

        int reportId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            await db.Database.EnsureCreatedAsync();
            var folder = new Folder { Name = "fault", Path = "/fault", OwnerId = 1 };
            var report = new Report
            {
                Folder = folder,
                Name = "disk pressure",
                ScriptPath = scriptPath,
                CreatedBy = 1
            };
            db.AddRange(folder, report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopes, NullLogger<SessionCache>.Instance);
        using var service = new ExecutionJobService(
            config,
            scopes,
            NullLogger<ExecutionJobService>.Instance,
            sessions,
            new HttpJobChannelClient(
                new HttpClient(new CompletedManifestHandler()) { BaseAddress = new Uri("http://orchestrator.test") },
                NullLogger<HttpJobChannelClient>.Instance),
            artifacts: new DiskPressureSnapshotStorage(),
            capacityMonitor: new HealthyCapacityMonitor());

        var jobId = await service.EnqueueExecutionAsync(reportId, userId: 1, scriptPath);
        await WaitForTerminalAsync(service, jobId);

        var job = await service.GetAsync(jobId);
        Assert.Equal(JobStatus.Failed, job!.Status);
        Assert.Contains("disk pressure", job.Error!, StringComparison.OrdinalIgnoreCase);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal(0, await verifyDb.ReportSnapshots.CountAsync(s => s.ReportId == reportId));

        Report? reportState = null;
        var dbDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < dbDeadline)
        {
            await using var pollScope = provider.CreateAsyncScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<PortalDbContext>();
            reportState = await pollDb.Reports.FindAsync(reportId);
            if (reportState?.LastRefreshStatus == "Failed")
                break;
            await Task.Delay(50);
        }

        Assert.NotNull(reportState);
        Assert.Equal("Failed", reportState.LastRefreshStatus);
        Assert.Contains("disk pressure", reportState.LastRefreshError!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Covers the same failure mode through the real filesystem-backed storage provider rather than a
    /// fake storage seam: the snapshot root is a file, so the provider fails while creating the
    /// destination directory on the backing volume.
    /// </summary>
    [Fact]
    public async Task ExecutionSnapshotFilesystemFailure_FailsJobWithoutSnapshotRow()
    {
        var scriptRoot = Path.Combine(_root, "fs-scripts");
        Directory.CreateDirectory(scriptRoot);
        var snapshotRootFile = Path.Combine(_root, "snapshots-as-file");
        await File.WriteAllTextAsync(snapshotRootFile, "not a directory");
        var scriptPath = Path.Combine(scriptRoot, "fs_pressure.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Filesystem Pressure';");

        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(_root, "portal-fs-exec.db"),
            ScriptRootPath = scriptRoot,
            SnapshotDirectory = snapshotRootFile,
            DatasetRootPath = Path.Combine(_root, "fs-datasets"),
            MapRootPath = Path.Combine(_root, "fs-maps"),
            Dataset = new DatasetConfig
            {
                AtRestKey = HostedPortalFactory.DefaultAtRestKey,
                AtRestKeyVersion = "v1"
            }
        };
        var services = new ServiceCollection()
            .AddDbContext<PortalDbContext>(options =>
                options.UseSqlite($"Data Source={config.DatabasePath}"))
            .BuildServiceProvider();
        await using var provider = services;

        int reportId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            await db.Database.EnsureCreatedAsync();
            var folder = new Folder { Name = "filesystem", Path = "/filesystem", OwnerId = 1 };
            var report = new Report
            {
                Folder = folder,
                Name = "filesystem pressure",
                ScriptPath = scriptPath,
                CreatedBy = 1
            };
            db.AddRange(folder, report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopes, NullLogger<SessionCache>.Instance);
        using var service = new ExecutionJobService(
            config,
            scopes,
            NullLogger<ExecutionJobService>.Instance,
            sessions,
            new HttpJobChannelClient(
                new HttpClient(new CompletedManifestHandler()) { BaseAddress = new Uri("http://orchestrator.test") },
                NullLogger<HttpJobChannelClient>.Instance),
            capacityMonitor: new HealthyCapacityMonitor());

        var jobId = await service.EnqueueExecutionAsync(reportId, userId: 1, scriptPath);
        await WaitForTerminalAsync(service, jobId);

        var job = await service.GetAsync(jobId);
        Assert.Equal(JobStatus.Failed, job!.Status);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal(0, await verifyDb.ReportSnapshots.CountAsync(s => s.ReportId == reportId));

        Report? reportState = null;
        var dbDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < dbDeadline)
        {
            await using var pollScope = provider.CreateAsyncScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<PortalDbContext>();
            reportState = await pollDb.Reports.FindAsync(reportId);
            if (reportState?.LastRefreshStatus == "Failed")
                break;
            await Task.Delay(50);
        }

        Assert.NotNull(reportState);
        Assert.Equal("Failed", reportState.LastRefreshStatus);
        Assert.False(string.IsNullOrWhiteSpace(reportState.LastRefreshError));
    }

    private sealed class StorageOutagePortalFactory : PortalWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IArtifactStorage>();
                services.AddSingleton<IArtifactStorage, FailingEnumerateStorage>();
            });
        }
    }

    private sealed class FailingEnumerateStorage : IArtifactStorage
    {
        public Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            Task.FromResult<ArtifactInfo?>(null);

        public IAsyncEnumerable<ArtifactInfo> EnumerateAsync(
            ArtifactArea area,
            string? prefix = null,
            bool recursive = true,
            CancellationToken ct = default) =>
            ThrowStorageUnavailableAsync();

        private static async IAsyncEnumerable<ArtifactInfo> ThrowStorageUnavailableAsync()
        {
            await Task.Yield();
            if (DateTime.UtcNow.Year > 0)
                throw new InvalidOperationException("simulated shared storage outage");
            yield break;
        }

        public Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            throw new FileNotFoundException(path);

        public Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            throw new FileNotFoundException(path);

        public Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            throw new FileNotFoundException(path);

        public Task WriteAsync(
            ArtifactArea area,
            string path,
            Stream content,
            bool overwrite = true,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated shared storage outage");

        public Task WriteAllBytesAsync(
            ArtifactArea area,
            string path,
            ReadOnlyMemory<byte> content,
            bool overwrite = true,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated shared storage outage");

        public Task WriteAllTextAsync(
            ArtifactArea area,
            string path,
            string content,
            bool overwrite = true,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated shared storage outage");

        public Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task MoveAsync(
            ArtifactArea area,
            string sourcePath,
            string destinationPath,
            bool overwrite = false,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated shared storage outage");

        public Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated shared storage outage");
    }

    private sealed class DiskPressureSnapshotStorage : IArtifactStorage
    {
        private readonly InMemoryArtifactStorage _inner = new();

        public Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            _inner.ExistsAsync(area, path, ct);

        public Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            _inner.GetInfoAsync(area, path, ct);

        public IAsyncEnumerable<ArtifactInfo> EnumerateAsync(
            ArtifactArea area,
            string? prefix = null,
            bool recursive = true,
            CancellationToken ct = default) =>
            _inner.EnumerateAsync(area, prefix, recursive, ct);

        public Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            _inner.OpenReadAsync(area, path, ct);

        public Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            _inner.ReadAllBytesAsync(area, path, ct);

        public Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            _inner.ReadAllTextAsync(area, path, ct);

        public Task WriteAsync(
            ArtifactArea area,
            string path,
            Stream content,
            bool overwrite = true,
            CancellationToken ct = default) =>
            area == ArtifactArea.Snapshots
                ? throw new IOException("simulated disk pressure while writing snapshot")
                : _inner.WriteAsync(area, path, content, overwrite, ct);

        public Task WriteAllBytesAsync(
            ArtifactArea area,
            string path,
            ReadOnlyMemory<byte> content,
            bool overwrite = true,
            CancellationToken ct = default) =>
            area == ArtifactArea.Snapshots
                ? throw new IOException("simulated disk pressure while writing snapshot")
                : _inner.WriteAllBytesAsync(area, path, content, overwrite, ct);

        public Task WriteAllTextAsync(
            ArtifactArea area,
            string path,
            string content,
            bool overwrite = true,
            CancellationToken ct = default) =>
            area == ArtifactArea.Snapshots
                ? throw new IOException("simulated disk pressure while writing snapshot")
                : _inner.WriteAllTextAsync(area, path, content, overwrite, ct);

        public Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            _inner.DeleteAsync(area, path, ct);

        public Task MoveAsync(
            ArtifactArea area,
            string sourcePath,
            string destinationPath,
            bool overwrite = false,
            CancellationToken ct = default) =>
            area == ArtifactArea.Snapshots
                ? throw new IOException("simulated disk pressure while writing snapshot")
                : _inner.MoveAsync(area, sourcePath, destinationPath, overwrite, ct);

        public Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
            _inner.LeaseLocalCopyAsync(area, path, ct);
    }

    private sealed class CompletedManifestHandler : HttpMessageHandler
    {
        private readonly string _manifestJson = JsonSerializer.Serialize(new ReportManifest
        {
            Source = "remote",
            BuiltAt = DateTime.UtcNow,
            Title = "Disk Pressure"
        });

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/jobs")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { jobId = "completed-manifest-job" })
                });
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/jobs/completed-manifest-job")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new JobStatusResponse
                    {
                        JobId = "completed-manifest-job",
                        Status = JobRunStatus.Completed,
                        ReportManifestJson = _manifestJson
                    })
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class HealthyCapacityMonitor : INodeCapacityMonitor
    {
        public NodeCapacitySnapshot Capture() => new(
            WorkingSetBytes: 1,
            GcHeapBytes: 1,
            TotalAvailableMemoryBytes: 100,
            MemoryLoadPercent: 10,
            ProcessCpuPercent: 10,
            ProcessorCount: 1,
            IsOverloaded: false,
            CapturedAtUtc: DateTime.UtcNow);
    }

    private static async Task WaitForTerminalAsync(ExecutionJobService service, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var job = await service.GetAsync(jobId);
            Assert.NotNull(job);
            if (job.Status is JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed)
                return;
            await Task.Delay(25);
        }

        Assert.Fail($"Job {jobId} did not reach a terminal state within 10s.");
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
