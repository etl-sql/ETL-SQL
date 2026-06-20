using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests;

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

        var dev = report.Environments.Single(e => e.Name == "dev");
        Assert.True(dev.Reachable);
        Assert.Equal("Healthy", dev.Status!.Status);
        Assert.Equal(2, dev.Status.QueueDepth);

        Assert.False(report.Environments.Single(e => e.Name == "down").Reachable);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/api/fleet/status", request.RequestUri!.AbsolutePath);
            var host = request.RequestUri!.Host;
            if (host == "down.test")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var status = host == "prod.test"
                ? Status("prod", "Degraded", queue: 0)
                : Status("dev", "Healthy", queue: 2);
            var json = JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        private static FleetEnvironmentStatus Status(string env, string state, int queue) =>
            new(env, state, queue, ActiveExecutions: 1, FailedRefreshes: 0,
                AuditOutboxPending: 0, AuditOutboxFailed: 0, Storage: "ok", CapturedAtUtc: DateTime.UtcNow);
    }
}
