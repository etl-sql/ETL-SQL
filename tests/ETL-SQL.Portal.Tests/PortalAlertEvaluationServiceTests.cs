using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.Portal.Tests;

public sealed class PortalAlertEvaluationServiceTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), $"etlsql-alert-eval-{Guid.NewGuid():N}");

    public PortalAlertEvaluationServiceTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    [Fact]
    public async Task EvaluateScheduledRefresh_DispatchesOnTriggeredTransitionOnly()
    {
        var dbPath = Path.Combine(_scratch, "portal.db");
        var config = new PortalConfig
        {
            DatabasePath = dbPath,
            ScriptRootPath = _scratch,
            SnapshotDirectory = _scratch,
            Orchestrator = { ApiUrl = "https://orchestrator.example.invalid", ApiKey = "test-key" }
        };
        var storage = new InMemoryArtifactStorage();
        var packages = new SnapshotPackageService(
            config,
            storage,
            NullLogger<SnapshotPackageService>.Instance);
        var manifestKey = SnapshotPackageService.BuildSnapshotKey(1, "refresh-1");
        await packages.SaveAsync(new ReportManifest
        {
            Visuals =
            [
                new VisualManifest
                {
                    Name = "RevenueCard",
                    VisualType = "CARD",
                    Columns = ["Value"],
                    Rows = [["125.5"]]
                }
            ]
        }, manifestKey);

        var services = new ServiceCollection();
        services.AddDbContext<PortalDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        await using (var provider = services.BuildServiceProvider())
        await using (var db = provider.GetRequiredService<PortalDbContext>())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new PortalUser { Id = 1, UserName = "owner@example.com", NormalizedUserName = "OWNER@EXAMPLE.COM" });
            // The owner administers the folder, so they genuinely retain read access to the report.
            // Alert evaluation re-authorizes the owner before dispatching, exactly as subscription
            // delivery does, so a fixture where the owner holds no grant would deliver nothing.
            var folder = new Folder { Name = "Reports", OwnerId = 1 };
            var report = new Report { Name = "Ops", Folder = folder, ScriptPath = "ops.rptsql", CreatedBy = 1 };
            var alert = new ReportAlert
            {
                Report = report,
                OwnerId = 1,
                Name = "RevenueHigh",
                VisualName = "RevenueCard",
                Operator = ">",
                Threshold = 100
            };
            alert.Notifications.Add(new AlertNotification
            {
                OrchestratorAlias = "default",
                NotificationName = "OpsMail"
            });
            db.ReportAlerts.Add(alert);
            await db.SaveChangesAsync();
        }

        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var protector = new OrchestratorApiKeyProtector(
            DataProtectionProvider.Create(Path.Combine(_scratch, "keys")));
        var settings = new OrchestratorSettingsService(config, protector);
        var proxy = new OrchestratorProxyService(
            http,
            settings,
            NullLogger<OrchestratorProxyService>.Instance);

        var serviceProvider = services.BuildServiceProvider();
        var evaluator = new PortalAlertEvaluationService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            config,
            packages,
            proxy,
            NullLogger<PortalAlertEvaluationService>.Instance);

        await evaluator.EvaluateScheduledRefreshAsync(1, "refresh-1", manifestKey, DateTime.UtcNow);
        await evaluator.EvaluateScheduledRefreshAsync(1, "refresh-2", manifestKey, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("POST", handler.LastMethod);
        Assert.Equal("https://orchestrator.example.invalid/api/notifications/OpsMail/dispatch", handler.LastUri);
        Assert.Contains("\"sourceKind\":\"ALERT\"", handler.LastBody);
        Assert.Contains("\"alertName\":\"RevenueHigh\"", handler.LastBody);

        await using var verifyDb = serviceProvider.GetRequiredService<PortalDbContext>();
        var stored = await verifyDb.ReportAlerts.SingleAsync();
        Assert.Equal("TRIGGERED", stored.LastState);
        Assert.NotNull(stored.LastTriggeredAt);
        Assert.NotNull(stored.LastNotifiedAt);
    }

    /// <summary>
    /// An alert notification carries the value that crossed the threshold, so an alert left running
    /// after its owner lost access keeps pushing report data into the channel that owner chose.
    /// Disabling the account, or removing the owner's last grant, has to stop it — the same
    /// delivery-time re-authorization subscriptions perform.
    /// </summary>
    [Theory]
    [InlineData(true, true, 1)]    // owner active and still granted → delivered
    [InlineData(false, true, 0)]   // owner deactivated → silent
    [InlineData(true, false, 0)]   // owner lost every grant on the folder → silent
    public async Task EvaluateScheduledRefresh_ReauthorizesTheAlertOwner(
        bool ownerIsActive,
        bool ownerRetainsAccess,
        int expectedDispatches)
    {
        var dbPath = Path.Combine(_scratch, $"portal-{ownerIsActive}-{ownerRetainsAccess}.db");
        var config = new PortalConfig
        {
            DatabasePath = dbPath,
            ScriptRootPath = _scratch,
            SnapshotDirectory = _scratch,
            Orchestrator = { ApiUrl = "https://orchestrator.example.invalid", ApiKey = "test-key" }
        };
        var storage = new InMemoryArtifactStorage();
        var packages = new SnapshotPackageService(config, storage, NullLogger<SnapshotPackageService>.Instance);
        var manifestKey = SnapshotPackageService.BuildSnapshotKey(1, "refresh-1");
        await packages.SaveAsync(new ReportManifest
        {
            Visuals =
            [
                new VisualManifest
                {
                    Name = "RevenueCard",
                    VisualType = "CARD",
                    Columns = ["Value"],
                    Rows = [["125.5"]]
                }
            ]
        }, manifestKey);

        var services = new ServiceCollection();
        services.AddDbContext<PortalDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        await using (var provider = services.BuildServiceProvider())
        await using (var db = provider.GetRequiredService<PortalDbContext>())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new PortalUser
            {
                Id = 1,
                UserName = "owner@example.com",
                NormalizedUserName = "OWNER@EXAMPLE.COM",
                IsActive = ownerIsActive
            });
            // Folder ownership is the owner's only grant, so handing it elsewhere revokes their
            // read access without touching the alert itself.
            var folder = new Folder { Name = "Reports", OwnerId = ownerRetainsAccess ? 1 : 0 };
            var report = new Report { Name = "Ops", Folder = folder, ScriptPath = "ops.rptsql", CreatedBy = 1 };
            var alert = new ReportAlert
            {
                Report = report,
                OwnerId = 1,
                Name = "RevenueHigh",
                VisualName = "RevenueCard",
                Operator = ">",
                Threshold = 100
            };
            alert.Notifications.Add(new AlertNotification
            {
                OrchestratorAlias = "default",
                NotificationName = "OpsMail"
            });
            db.ReportAlerts.Add(alert);
            await db.SaveChangesAsync();
        }

        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var protector = new OrchestratorApiKeyProtector(
            DataProtectionProvider.Create(Path.Combine(_scratch, "keys")));
        var proxy = new OrchestratorProxyService(
            http,
            new OrchestratorSettingsService(config, protector),
            NullLogger<OrchestratorProxyService>.Instance);

        var serviceProvider = services.BuildServiceProvider();
        var evaluator = new PortalAlertEvaluationService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            config,
            packages,
            proxy,
            NullLogger<PortalAlertEvaluationService>.Instance);

        await evaluator.EvaluateScheduledRefreshAsync(1, "refresh-1", manifestKey, DateTime.UtcNow);

        Assert.Equal(expectedDispatches, handler.RequestCount);

        // A skipped alert is skipped whole. Recording a TRIGGERED transition nobody was told about
        // would swallow the notification for good if access were later restored.
        await using var verifyDb = serviceProvider.GetRequiredService<PortalDbContext>();
        var stored = await verifyDb.ReportAlerts.SingleAsync();
        if (expectedDispatches == 0)
        {
            Assert.Null(stored.LastState);
            Assert.Null(stored.LastNotifiedAt);
        }
        else
        {
            Assert.Equal("TRIGGERED", stored.LastState);
            Assert.NotNull(stored.LastNotifiedAt);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastMethod { get; private set; }
        public string? LastUri { get; private set; }
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = request.Method.Method;
            LastUri = request.RequestUri?.ToString();
            LastBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
