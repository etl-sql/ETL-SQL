using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Enforces <c>CatalogOnly</c> and <c>ScriptIngress</c> authoring lockdown across both Orchestrator
/// proxy endpoints and <see cref="Controllers.ReportsController"/> external script mutation routes.
/// </summary>
public sealed class OrchestratorCatalogOnlyAuthoringTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CatalogOnly_Mode_Blocks_OrchestratorScripts_And_RawJobs_WhileAllowingBundlesAndReports()
    {
        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new FederatedAuthoringPortal(orchestrator, StudioDeploymentMode.CatalogOnly, [.. StudioCapabilities.All]);
        using var client = portal.CreateClient();
        var adminToken = await LoginAdminAsync(client);

        // 1. Authoring-mode probe advertises CatalogOnly lockdown.
        using (var modeRes = await Send(client, HttpMethod.Get, "/api/orchestrator/authoring-mode", adminToken))
        {
            Assert.Equal(HttpStatusCode.OK, modeRes.StatusCode);
            var mode = (await modeRes.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            Assert.Equal("CatalogOnly", mode["studioMode"]!.GetValue<string>());
            Assert.False(mode["externalScriptIngressEnabled"]!.GetValue<bool>());
            Assert.False(mode["canCreateInlineScriptJobs"]!.GetValue<bool>());
        }

        // 2. Server filesystem script listing and content reading return 404.
        using (var scriptsRes = await Send(client, HttpMethod.Get, "/api/orchestrator/scripts", adminToken))
            Assert.Equal(HttpStatusCode.NotFound, scriptsRes.StatusCode);
        using (var contentRes = await Send(client, HttpMethod.Get, "/api/orchestrator/scripts/content?path=etl/daily.etlsql", adminToken))
            Assert.Equal(HttpStatusCode.NotFound, contentRes.StatusCode);

        // 3. Unreviewed inline ScriptText and raw non-bundle script TargetPath job creation are refused.
        using (var inlineRes = await Send(client, HttpMethod.Post, "/api/orchestrator/jobs", adminToken, new
        {
            name = "blocked_inline_job",
            scriptText = "SELECT 1;",
            interval = 1,
            unit = "DAY"
        }))
        {
            Assert.Equal(HttpStatusCode.NotFound, inlineRes.StatusCode);
        }

        using (var rawPathRes = await Send(client, HttpMethod.Post, "/api/orchestrator/jobs", adminToken, new
        {
            name = "blocked_raw_path_job",
            jobType = "Script",
            targetPath = "etl/raw.etlsql",
            interval = 1,
            unit = "DAY"
        }))
        {
            Assert.Equal(HttpStatusCode.NotFound, rawPathRes.StatusCode);
        }

        // 4. Published orch:// bundles and Report jobs remain allowed in CatalogOnly mode.
        using (var bundleRes = await Send(client, HttpMethod.Post, "/api/orchestrator/jobs", adminToken, new
        {
            name = "catalog_bundle_job",
            jobType = "Script",
            targetPath = "orch://sales@1/main.etlsql",
            interval = 1,
            unit = "DAY"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, bundleRes.StatusCode);
        }

        using (var reportRes = await Send(client, HttpMethod.Post, "/api/orchestrator/jobs", adminToken, new
        {
            name = "catalog_report_job",
            jobType = "REPORT",
            targetPath = "/Finance/DailySales",
            interval = 1,
            unit = "DAY"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, reportRes.StatusCode);
        }

        // 5. Updating bundle_job with inline ScriptText is refused, while toggling IsEnabled succeeds.
        long version;
        using (var scope = orchestrator.Services.CreateScope())
        {
            var job = await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>()
                .GetJobAsync("portal-host", "catalog_bundle_job");
            Assert.NotNull(job);
            version = job.Version;
        }

        using (var inlineUpdate = Request(HttpMethod.Put, "/api/orchestrator/jobs/catalog_bundle_job", adminToken,
            new { scriptText = "SELECT 99;" }))
        {
            inlineUpdate.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var conflict = await client.SendAsync(inlineUpdate);
            Assert.Equal(HttpStatusCode.NotFound, conflict.StatusCode);
        }

        using (var toggleUpdate = Request(HttpMethod.Put, "/api/orchestrator/jobs/catalog_bundle_job", adminToken,
            new { isEnabled = false }))
        {
            toggleUpdate.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var toggled = await client.SendAsync(toggleUpdate);
            Assert.Equal(HttpStatusCode.NoContent, toggled.StatusCode);
        }
    }

    [Fact]
    public async Task SourceControlled_WithoutScriptIngress_Forbids_OrchestratorScripts_And_ReportsMutations()
    {
        var withoutIngress = StudioCapabilities.All
            .Where(capability => !string.Equals(capability, StudioCapabilities.ScriptIngress, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new FederatedAuthoringPortal(orchestrator, StudioDeploymentMode.SourceControlled, withoutIngress);
        using var client = portal.CreateClient();
        var adminToken = await LoginAdminAsync(client);

        int folderId;
        int reportId;
        using (var scope = portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = await db.Users.Where(user => user.UserName == "admin").Select(user => user.Id).SingleAsync();
            var folder = new Folder { Name = "IngressTest", Path = "/IngressTest", OwnerId = adminId };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            folderId = folder.Id;
        }

        // Studio in-catalog creation (ScriptSave + ReportPublish) still works without ScriptIngress.
        using (var studioCreate = await Send(client, HttpMethod.Post, "/api/studio/reports", adminToken, new
        {
            folderId,
            name = "Catalog Report",
            scriptText = "SET REPORT TITLE = 'Catalog';\nCREATE PAGE Main AS (TITLE = 'Catalog');"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, studioCreate.StatusCode);
            reportId = (await studioCreate.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        }

        // Write a real script file under ScriptRootPath so ReportsController tests authorization, not file missing.
        var scriptPath = Path.Combine(portal.TempDir, "scripts", "external.rptsql");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'External';");

        // Orchestrator script ingress routes forbid when ScriptIngress is omitted.
        using (var modeRes = await Send(client, HttpMethod.Get, "/api/orchestrator/authoring-mode", adminToken))
        {
            Assert.Equal(HttpStatusCode.OK, modeRes.StatusCode);
            var mode = (await modeRes.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            Assert.Equal("SourceControlled", mode["studioMode"]!.GetValue<string>());
            Assert.False(mode["externalScriptIngressEnabled"]!.GetValue<bool>());
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(client, HttpMethod.Get, "/api/orchestrator/scripts", adminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(client, HttpMethod.Post, "/api/orchestrator/jobs", adminToken, new
            {
                name = "forbidden_inline_job",
                scriptText = "SELECT 1;",
                interval = 1,
                unit = "DAY"
            })).StatusCode);

        // ReportsController upload & external ScriptPath publication/mutation forbid without ScriptIngress.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(client, HttpMethod.Post, "/api/scripts/upload", adminToken, new
            {
                filename = "ext.rptsql",
                contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("SET REPORT TITLE = 'Ext';"))
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(client, HttpMethod.Post, "/api/reports", adminToken, new
            {
                folderId,
                name = "External Script Report",
                scriptPath = "external.rptsql"
            })).StatusCode);

        long reportVersion;
        using (var scope = portal.Services.CreateScope())
            reportVersion = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Reports
                .Where(r => r.Id == reportId).Select(r => r.Version).SingleAsync();

        using (var pathUpdate = Request(HttpMethod.Put, $"/api/reports/{reportId}", adminToken,
            new { scriptPath = "external.rptsql" }))
        {
            pathUpdate.Headers.TryAddWithoutValidation("If-Match", $"\"{reportVersion}\"");
            using var refused = await client.SendAsync(pathUpdate);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        // Metadata-only update without ScriptPath continues to succeed.
        using (var metaUpdate = Request(HttpMethod.Put, $"/api/reports/{reportId}", adminToken,
            new { description = "Metadata updated safely" }))
        {
            metaUpdate.Headers.TryAddWithoutValidation("If-Match", $"\"{reportVersion}\"");
            using var updated = await client.SendAsync(metaUpdate);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }
    }

    private sealed class FederatedAuthoringPortal(
        OrchestratorWebFactory orchestrator,
        StudioDeploymentMode mode,
        string[] adminCapabilities) : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
            config.Studio.Mode = mode;
            config.Studio.RoleCapabilities["Admin"] = [.. adminCapabilities];
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
        }
    }

    private static async Task<string> LoginAdminAsync(HttpClient client)
    {
        const string password = "Authoring@Tests99!";
        using var initialRes = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, initialRes.StatusCode);
        var initial = (await initialRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
        using (var change = Request(HttpMethod.Post, "/api/auth/change-password", initial,
            new { currentPassword = "Admin@12345!", newPassword = password }))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(change)).StatusCode);
        }
        using var finalRes = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password });
        Assert.Equal(HttpStatusCode.OK, finalRes.StatusCode);
        return (await finalRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        using var req = Request(method, path, token, body);
        return await client.SendAsync(req);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }
}
