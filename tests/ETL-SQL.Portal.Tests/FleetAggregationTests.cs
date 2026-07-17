using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// P2.3 fleet aggregator credential containment: the scoped <c>FleetReader</c> role may read only
/// <c>GET /api/fleet/status</c> — it cannot pivot into the admin/identity surface or publish/execute
/// reports. A compromised aggregator credential therefore cannot reach any department's database,
/// secrets, keys, or execution capability.
/// </summary>
[Trait("Category", "Portal")]
public sealed class FleetContainmentTests : IClassFixture<PortalWebFactory>
{
    private readonly PortalWebFactory _factory;
    public FleetContainmentTests(PortalWebFactory factory) => _factory = factory;

    [Fact]
    public async Task FleetReader_CanReadFleetStatus()
    {
        var token = await MintTokenAsync("FleetReader");
        var res = await GetAsync("/api/fleet/status", token);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var status = await res.Content.ReadFromJsonAsync<FleetEnvironmentStatus>();
        Assert.NotNull(status);
        Assert.False(string.IsNullOrEmpty(status!.Status));
        Assert.NotNull(status.SecurityEvents);
        Assert.NotNull(status.Inventory);
        Assert.False(string.IsNullOrWhiteSpace(status.Inventory!.NodeId));
        Assert.False(string.IsNullOrWhiteSpace(status.Inventory.InstalledVersion));
        Assert.Equal("1.0", status.Inventory.SchemaVersions.Enrollment);
        Assert.Equal("1.0", status.Inventory.SchemaVersions.PolicyEnvelope);
        Assert.Equal("1.0", status.Inventory.SchemaVersions.PolicyPayload);
        Assert.Equal(1, status.Inventory.SchemaVersions.SecurityEvent);
        Assert.False(string.IsNullOrWhiteSpace(status.Inventory.ConfigurationFingerprint));
        Assert.False(string.IsNullOrWhiteSpace(status.Inventory.Providers.PortalDatabase));
        Assert.False(string.IsNullOrWhiteSpace(status.Inventory.Providers.ArtifactStorage));
        Assert.NotNull(status.Inventory.Compatibility);
        Assert.Equal("1.0", status.Inventory.Compatibility!.MetadataVersion);
        Assert.Contains("single-owner-database-migration", status.Inventory.Compatibility.RollingUpgradeSequence);
        Assert.Contains(status.Inventory.Compatibility.Components, c => c.Component == "portal-database");
        Assert.NotNull(status.Inventory.Migration);
        Assert.False(string.IsNullOrWhiteSpace(status.Inventory.Migration!.State));
    }

    [Fact]
    public async Task Admin_CanAlsoReadFleetStatus()
    {
        var token = await MintTokenAsync("Admin");
        var res = await GetAsync("/api/fleet/status", token);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task FleetStatus_RequiresAuthentication()
    {
        var res = await _factory.CreateClient().GetAsync("/api/fleet/status");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task FleetReader_IsDeniedAdminSurface()
    {
        // Identity/secret/config/orchestrator management is Admin-only — no pivot for a FleetReader.
        var token = await MintTokenAsync("FleetReader");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = JsonContent.Create(new { username = "x", email = "x@x.test", role = "Viewer" })
        };
        req.Headers.Authorization = new("Bearer", token);
        var res = await _factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task FleetReader_IsDeniedReportPublish()
    {
        // Publish/write capability is Admin,Publisher only — no execution/write pivot for a FleetReader.
        var token = await MintTokenAsync("FleetReader");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/reports")
        {
            Content = JsonContent.Create(new { name = "x", folderPath = "/", scriptPath = "x.rptsql" })
        };
        req.Headers.Authorization = new("Bearer", token);
        var res = await _factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task FleetReader_IsDeniedReportExecute_EvenWithFolderExecutePermission()
    {
        var (user, token) = await MintUserAsync("FleetReader");
        var suffix = Guid.NewGuid().ToString("N");
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"fleet_exec_{suffix}.rptsql");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, "SELECT 1 AS Value INTO #data;");

        int reportId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var group = new Group { Name = $"fleet_exec_{suffix}"[..24] };
            var folder = new Folder
            {
                Name = $"fleet_exec_{suffix}"[..24],
                Path = $"/fleet_exec_{suffix}"[..25],
                OwnerId = user.Id
            };

            db.Groups.Add(group);
            db.Folders.Add(folder);
            await db.SaveChangesAsync();

            db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
            db.FolderAcls.Add(new FolderAcl
            {
                FolderId = folder.Id,
                GroupId = group.Id,
                Permission = FolderPermission.Execute
            });

            var report = new Report
            {
                FolderId = folder.Id,
                Name = $"Fleet Execute {suffix}"[..24],
                ScriptPath = scriptPath,
                CreatedBy = user.Id
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        var res = await PostAsync(
            $"/api/reports/{reportId}/execute",
            token,
            new { parameters = new Dictionary<string, string>() });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    private Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new("Bearer", token);
        return _factory.CreateClient().SendAsync(req);
    }

    private Task<HttpResponseMessage> PostAsync(string path, string token, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new("Bearer", token);
        return _factory.CreateClient().SendAsync(req);
    }

    private async Task<string> MintTokenAsync(string role)
    {
        var (_, token) = await MintUserAsync(role);
        return token;
    }

    private async Task<(PortalUser User, string Token)> MintUserAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var user = new PortalUser
        {
            UserName = $"fleet_{role}_{Guid.NewGuid():N}"[..24],
            Email = "fleet@example.test",
            IsActive = true,
            MustChangePassword = false
        };
        Assert.True((await userMgr.CreateAsync(user, "Fleet@12345!")).Succeeded);
        await userMgr.AddToRoleAsync(user, role);
        return (user, tokens.GenerateJwt(user, await userMgr.GetRolesAsync(user)));
    }
}

/// <summary>
/// P2.2 read-only fleet health aggregation: the aggregator fans out to each environment's status
/// endpoint, merges the results, and tolerates an unreachable environment instead of failing the
/// whole fleet view.
/// </summary>
[Trait("Category", "Portal")]
public sealed class FleetHealthAggregatorTests
{
    [Fact]
    public async Task Aggregates_AndTolerates_UnreachableEnvironment()
    {
        var handler = new StubHandler();
        var aggregator = new FleetHealthAggregator(new HttpClient(handler));

        var report = await aggregator.AggregateAsync(new[]
        {
            new FleetEnvironmentDescriptor("dev", new Uri("https://dev.test/"), "tok-dev"),
            new FleetEnvironmentDescriptor("prod", new Uri("https://prod.test/"), "tok-prod"),
            new FleetEnvironmentDescriptor("down", new Uri("https://down.test/"), "tok-down")
        });

        Assert.Equal(3, report.Total);
        Assert.Equal(1, report.Unreachable);   // 'down' returned 503
        Assert.Equal(1, report.Unhealthy);     // 'prod' reported Degraded
        Assert.Contains(report.Findings!, f => f.Code == "environment-unreachable" && f.Scope == "down");
        Assert.Contains(report.Findings!, f => f.Code == "environment-health" && f.Scope == "prod");
        Assert.Contains(report.Findings!, f => f.Code == "upgrade-readiness" && f.Scope == "prod");

        var dev = report.Environments.Single(e => e.Name == "dev");
        Assert.True(dev.Reachable);
        Assert.Equal("Healthy", dev.Status!.Status);
        Assert.Equal(2, dev.Status.QueueDepth);
        Assert.Equal("dev-node", dev.Status.Inventory!.NodeId);

        Assert.False(report.Environments.Single(e => e.Name == "down").Reachable);
    }

    [Fact]
    public async Task AppliesReadOnlyFleetViewFiltersSearchAndGroups()
    {
        var handler = new StubHandler();
        var aggregator = new FleetHealthAggregator(new HttpClient(handler));

        var report = await aggregator.AggregateAsync(
            new[]
            {
                new FleetEnvironmentDescriptor("dev", new Uri("https://dev.test/"), "tok-dev"),
                new FleetEnvironmentDescriptor("prod", new Uri("https://prod.test/"), "tok-prod"),
                new FleetEnvironmentDescriptor("down", new Uri("https://down.test/"), "tok-down")
            },
            new FleetViewOptions(
                Search: "prod-node",
                Status: "Degraded",
                Reachable: true,
                UpgradeReady: false,
                DatabaseProvider: "Postgres",
                StorageProvider: "Smb",
                PolicyVersion: "prod-policy",
                GroupBy: FleetGroupBy.DatabaseProvider));

        var only = Assert.Single(report.Environments);
        Assert.Equal("prod", only.Name);
        Assert.Equal("prod-node", only.Status!.Inventory!.NodeId);
        var group = Assert.Single(report.Groups!);
        Assert.Equal("Postgres", group.Key);
        Assert.Equal(1, group.Total);
        Assert.Equal(1, group.Unhealthy);
    }

    [Fact]
    public async Task FindingsSurfacePolicyAndConfigurationDivergenceWithinEnvironment()
    {
        var handler = new StubHandler();
        var aggregator = new FleetHealthAggregator(new HttpClient(handler));

        var report = await aggregator.AggregateAsync(new[]
        {
            new FleetEnvironmentDescriptor("prod-a", new Uri("https://prod-a.test/"), "tok-a"),
            new FleetEnvironmentDescriptor("prod-b", new Uri("https://prod-b.test/"), "tok-b")
        });

        Assert.Contains(report.Findings!, f => f.Scope == "prod"
            && f.Code == "policy-version-divergence");
        Assert.Contains(report.Findings!, f => f.Scope == "prod"
            && f.Code == "configuration-drift");
        Assert.Contains(report.Findings!, f => f.Scope == "prod"
            && f.Code == "installed-version-divergence");

        var postflight = FleetHealthAggregator.BuildUpgradeReport(report, FleetUpgradeReportMode.Postflight);
        Assert.False(postflight.Ready);
        Assert.Contains(postflight.Checks, check => check.Code == "postflight-no-divergence"
            && check.Status == "Fail");
    }

    [Fact]
    public async Task BuildsPassingUpgradePreflightReportWhenFleetIsReady()
    {
        var handler = new StubHandler();
        var aggregator = new FleetHealthAggregator(new HttpClient(handler));

        var report = await aggregator.AggregateAsync(new[]
        {
            new FleetEnvironmentDescriptor("dev", new Uri("https://dev.test/"), "tok-dev")
        });

        var preflight = FleetHealthAggregator.BuildUpgradeReport(report, FleetUpgradeReportMode.Preflight);

        Assert.True(preflight.Ready);
        Assert.All(preflight.Checks, check => Assert.Equal("Pass", check.Status));
    }

    [Fact]
    public async Task AllowsNMinusOneRollingCompatibilityWindow()
    {
        var handler = new StubHandler();
        var aggregator = new FleetHealthAggregator(new HttpClient(handler));

        var report = await aggregator.AggregateAsync(new[]
        {
            new FleetEnvironmentDescriptor("roll-n", new Uri("https://roll-n.test/"), "tok-n"),
            new FleetEnvironmentDescriptor("roll-n-1", new Uri("https://roll-n-1.test/"), "tok-n-1")
        });

        var preflight = FleetHealthAggregator.BuildUpgradeReport(report, FleetUpgradeReportMode.Preflight);

        Assert.True(preflight.Ready);
        Assert.DoesNotContain(report.Findings!, finding => finding.Code == "unsupported-compatibility-window");
        Assert.Contains(preflight.Checks, check => check.Code == "supported-compatibility-window"
            && check.Status == "Pass");
    }

    [Fact]
    public async Task FailsPreflightWhenFleetExceedsNMinusOneCompatibilityWindow()
    {
        var handler = new StubHandler();
        var aggregator = new FleetHealthAggregator(new HttpClient(handler));

        var report = await aggregator.AggregateAsync(new[]
        {
            new FleetEnvironmentDescriptor("wide-old", new Uri("https://wide-old.test/"), "tok-old"),
            new FleetEnvironmentDescriptor("wide-new", new Uri("https://wide-new.test/"), "tok-new")
        });

        var preflight = FleetHealthAggregator.BuildUpgradeReport(report, FleetUpgradeReportMode.Preflight);

        Assert.False(preflight.Ready);
        Assert.Contains(report.Findings!, finding => finding.Scope == "wide"
            && finding.Code == "unsupported-compatibility-window");
        Assert.Contains(preflight.Checks, check => check.Code == "supported-compatibility-window"
            && check.Status == "Fail");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/api/fleet/status", request.RequestUri!.AbsolutePath);
            var host = request.RequestUri!.Host;
            if (host == "down.test")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var status = host switch
            {
                "prod.test" => Status("prod", "Degraded", queue: 0, "Postgres", "Smb", "prod-policy", upgradeReady: false),
                "prod-a.test" => Status("prod", "Healthy", queue: 0, "Postgres", "Smb", "prod-policy-a",
                    installedVersion: "1.0.0", fingerprint: "fp-a"),
                "prod-b.test" => Status("prod", "Healthy", queue: 0, "Postgres", "Smb", "prod-policy-b",
                    installedVersion: "1.0.1", fingerprint: "fp-b"),
                "roll-n.test" => Status("roll", "Healthy", queue: 0, installedVersion: "1.3.0"),
                "roll-n-1.test" => Status("roll", "Healthy", queue: 0, installedVersion: "1.2.9"),
                "wide-old.test" => Status("wide", "Healthy", queue: 0, installedVersion: "1.1.0"),
                "wide-new.test" => Status("wide", "Healthy", queue: 0, installedVersion: "1.3.0"),
                _ => Status("dev", "Healthy", queue: 2)
            };
            var json = JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        private static FleetEnvironmentStatus Status(
            string env,
            string state,
            int queue,
            string databaseProvider = "Sqlite",
            string storageProvider = "Local",
            string? policyVersion = null,
            bool upgradeReady = true,
            string installedVersion = "0.0.0-test",
            string fingerprint = "fingerprint") =>
            new(env, state, queue, ActiveExecutions: 1, FailedRefreshes: 0,
                AuditOutboxPending: 0, AuditOutboxFailed: 0, Storage: "ok", CapturedAtUtc: DateTime.UtcNow,
                Inventory: new FleetNodeInventory(
                    env,
                    $"{env}-node",
                    installedVersion,
                    new FleetSchemaVersions("1.0", "1.0", "1.0", 1, null, 0, 0),
                    new FleetPolicyInventory(policyVersion is not null, policyVersion is not null, policyVersion is null ? "Standalone" : "Live",
                        policyVersion, null, null, null, null,
                        false, false, null, 0),
                    new FleetRuntimeProviders(databaseProvider, storageProvider),
                    fingerprint,
                    new FleetUpgradeReadiness(upgradeReady,
                        upgradeReady ? Array.Empty<string>() : new[] { "portal-schema-has-pending-migrations" }),
                    new FleetCompatibilityMetadata(
                        "1.0",
                        "N-1 rolling when all nodes report ready and no pending migrations",
                        new[] { "preflight-readiness", "single-owner-database-migration", "postflight-readiness" },
                        new[]
                        {
                            new FleetComponentCompatibility("portal-database", databaseProvider,
                                "ef-migrations:last=none;pending=0", "expand/migrate/contract", true, "ready")
                        })));
    }
}
