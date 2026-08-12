using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class PortalModuleRouteFencingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Disabled_Reporting_Module_Hides_Reporting_Api_Routes()
    {
        using var factory = new ModuleFenceFactory(config => config.Modules.Reporting = false);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/reports/1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/catalog/search?q=x", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/datasets", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/subscriptions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/reports/1/export/csv", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login.html")).StatusCode);
    }

    [Fact]
    public async Task Disabled_Designer_Module_Hides_Designer_Api_Routes()
    {
        using var factory = new ModuleFenceFactory(config => config.Modules.Designer = false);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var parse = await SendAsync(client, HttpMethod.Post, token, "/api/designer/parse", new { script = "" });
        var analyze = await SendAsync(client, HttpMethod.Post, token, "/api/designer/analyze", new { script = "SELECT * FROM #stage;" });
        var complete = await SendAsync(client, HttpMethod.Post, token, "/api/designer/complete", new { script = "SEL", line = 0, column = 3 });
        var run = await SendAsync(client, HttpMethod.Post, token, "/api/designer/run", new { script = "SELECT 1 AS One;" });
        var dataPreview = await SendAsync(client, HttpMethod.Post, token, "/api/designer/data-preview", new
        {
            sourceKind = "temp",
            tempTable = "#stage",
            script = "SELECT 1 AS One INTO #stage;"
        });
        var save = await SendAsync(client, HttpMethod.Post, token, "/api/designer/save", new { reportId = 1, scriptText = "SELECT 1;" });
        var schema = await SendAsync(client, HttpMethod.Get, token, "/api/designer/schema?connection=x", null);

        Assert.Equal(HttpStatusCode.NotFound, parse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, analyze.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, complete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, run.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, dataPreview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, save.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, schema.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/designer.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/index.html")).StatusCode);
    }

    [Fact]
    public async Task Disabled_Studio_Mode_Hides_Designer_And_Authoring_Routes()
    {
        using var factory = new ModuleFenceFactory(config => config.Studio.Mode = StudioDeploymentMode.Disabled);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Post, token, "/api/designer/parse", new { script = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Get, token, "/api/reports/available-scripts", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Get, token, "/api/studio/session", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/designer.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/studio.html")).StatusCode);
    }

    [Fact]
    public async Task Studio_Capabilities_Are_DenyByDefault_And_ActionSpecific()
    {
        using var factory = new ModuleFenceFactory(config =>
        {
            config.Studio.RoleCapabilities.Clear();
            config.Studio.RoleCapabilities["Admin"] =
                [StudioCapabilities.StudioAccess, StudioCapabilities.ScriptPreview];
        });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, token, "/api/designer/parse", new { script = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Post, token, "/api/designer/data-preview", new
            {
                sourceKind = "temp",
                tempTable = "#missing",
                script = "SELECT 1;"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Post, token, "/api/designer/run", new { script = "SELECT 1;" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Post, token, "/api/scripts/upload", new { filename = "x.rptsql", contentBase64 = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, token, "/api/studio/reports", null)).StatusCode);
    }

    [Fact]
    public async Task CatalogOnly_Mode_Removes_External_Ingress_Even_When_Capability_Is_Assigned()
    {
        using var factory = new ModuleFenceFactory(config =>
        {
            config.Studio.Mode = StudioDeploymentMode.CatalogOnly;
            config.Studio.RoleCapabilities["Admin"] = StudioCapabilities.All.ToList();
        });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Post, token, "/api/scripts/upload", new { filename = "x.rptsql", contentBase64 = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Get, token, "/api/reports/available-scripts", null)).StatusCode);
    }

    [Fact]
    public async Task CatalogOnly_Studio_Creates_And_Lists_Report_Without_Exposing_Script_Path()
    {
        using var factory = new ModuleFenceFactory(config =>
        {
            config.Studio.Mode = StudioDeploymentMode.CatalogOnly;
            config.Studio.RoleCapabilities["Admin"] = StudioCapabilities.All.ToList();
        });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        int folderId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = await db.Users.Where(user => user.UserName == "admin").Select(user => user.Id).SingleAsync();
            var folder = new Folder { Name = "Studio", Path = "/Studio", OwnerId = adminId };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            folderId = folder.Id;
        }

        var session = await SendAsync(client, HttpMethod.Get, token, "/api/studio/session", null);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var sessionBody = await session.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("CatalogOnly", sessionBody!["mode"]!.GetValue<string>());
        Assert.False(sessionBody["sourceControlEnabled"]!.GetValue<bool>());

        var created = await SendAsync(client, HttpMethod.Post, token, "/api/studio/reports", new
        {
            folderId,
            name = "Margin Review",
            scriptText = "SET REPORT TITLE = 'Margin Review';\nCREATE PAGE Main AS (TITLE = 'Margin Review');"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonObject>(Json);
        var reportId = createdBody!["id"]!.GetValue<int>();
        Assert.Equal("/Studio", createdBody["folderPath"]!.GetValue<string>());
        Assert.Null(createdBody["scriptPath"]);

        var listed = await SendAsync(client, HttpMethod.Get, token, "/api/studio/reports", null);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listedBody = await listed.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.Contains(listedBody!, node => node!["id"]!.GetValue<int>() == reportId);
        Assert.All(listedBody!, node => Assert.Null(node!["scriptPath"]));

        var scripts = Directory.GetFiles(Path.Combine(factory.TempDir, "scripts", "studio"), "*.rptsql", SearchOption.AllDirectories);
        Assert.Single(scripts);
        Assert.Contains("Margin Review", await File.ReadAllTextAsync(scripts[0]));

        using var auditScope = factory.Services.CreateScope();
        var auditDb = auditScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.True(await auditDb.AuditLogs.AnyAsync(row => row.Action == "CREATE_STUDIO_REPORT" && row.ResourceType == "Report"));
    }

    [Fact]
    public async Task SourcePush_Is_Separate_From_SourceCommit()
    {
        using var factory = new ModuleFenceFactory(config =>
        {
            config.SourceControl.PushOnSave = true;
            config.Studio.RoleCapabilities["Admin"] =
                [StudioCapabilities.StudioAccess, StudioCapabilities.SourceCommit];
        });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Post, token, "/api/reports/1/script-source/commit", new { })).StatusCode);
    }

    [Fact]
    public async Task Designer_Run_ExecutesReadOnlySelectAndAudits()
    {
        using var factory = new ModuleFenceFactory(_ => { });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, token, "/api/designer/run", new
        {
            script = "SELECT 'ssn-123-45-6789' AS SensitiveValue, 1 AS One;",
            selection = "SELECT 'ssn-123-45-6789' AS SensitiveValue, 1 AS One;"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Single(body!["rows"]!.AsArray());
        Assert.Equal(1, body["rows"]![0]!["One"]!.GetValue<int>());
        Assert.False(body["capped"]!.GetValue<bool>());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "AD_HOC_RUN");
        Assert.Contains("QueryHash=sha256:", audit.Detail);
        Assert.Contains("SelectStatement", audit.Detail);
        Assert.DoesNotContain("Query=", audit.Detail);
        Assert.DoesNotContain("ssn-123-45-6789", audit.Detail);
    }

    [Fact]
    public async Task Designer_Run_RejectsNonSelect()
    {
        using var factory = new ModuleFenceFactory(_ => { });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, token, "/api/designer/run", new
        {
            script = "DELETE FROM #stage;"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Designer_DataPreview_RecreatesIntermediateTempTableWithinBoundedRun()
    {
        using var factory = new ModuleFenceFactory(_ => { });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, token, "/api/designer/data-preview", new
        {
            sourceKind = "temp",
            tempTable = "#stage",
            script = "SELECT 1 AS Id, 'SECRET:preview-token' AS Token INTO #stage;"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("temp", body!["sourceKind"]!.GetValue<string>());
        Assert.Equal("#stage", body["source"]!.GetValue<string>());
        Assert.Single(body["rows"]!.AsArray());
        Assert.Equal(1, body["rows"]![0]!["Id"]!.GetValue<int>());
        Assert.DoesNotContain("preview-token", await response.Content.ReadAsStringAsync());
        Assert.InRange(body["bytesReturned"]!.GetValue<long>(), 1, 256 * 1024);
    }

    [Fact]
    public async Task Designer_EditLease_ConflictsRecoversRenewsAndDoesNotAdvanceContentVersion()
    {
        using var factory = new ModuleFenceFactory(_ => { });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        int reportId;
        long contentVersion;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = await db.Users.Where(user => user.UserName == "admin").Select(user => user.Id).SingleAsync();
            var folder = new Folder { Name = "Lease tests", Path = "/lease-tests", OwnerId = adminId };
            var report = new Report
            {
                Folder = folder,
                Name = "Shared report",
                ScriptPath = "lease-tests/shared.rptsql",
                CreatedBy = adminId,
                EditSessionUserId = 999_999,
                EditSessionUserName = "other-author",
                EditSessionExpiresAtUtc = DateTime.UtcNow.AddMinutes(3)
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
            contentVersion = report.Version;
        }

        var blocked = await SendAsync(client, HttpMethod.Post, token, "/api/designer/lease", new { reportId });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var blockedBody = await blocked.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("other-author", blockedBody!["owner"]!.GetValue<string>());
        Assert.NotNull(blockedBody["expiresAt"]);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            await db.Reports.Where(report => report.Id == reportId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    report => report.EditSessionExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        }

        var acquired = await SendAsync(client, HttpMethod.Post, token, "/api/designer/lease", new { reportId });
        Assert.Equal(HttpStatusCode.OK, acquired.StatusCode);
        var renewed = await SendAsync(client, HttpMethod.Post, token, "/api/designer/lease", new { reportId });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.AsNoTracking().SingleAsync(value => value.Id == reportId);
            Assert.Equal(contentVersion, report.Version);
            Assert.Equal("admin", report.EditSessionUserName);
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, token, $"/api/designer/lease/{reportId}", null)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.AsNoTracking().SingleAsync(value => value.Id == reportId);
            Assert.Null(report.EditSessionUserId);
            Assert.Null(report.EditSessionExpiresAtUtc);
            Assert.Equal(contentVersion, report.Version);
        }
    }

    [Theory]
    [InlineData("SELECT 1 AS Value INTO #written UNION SELECT 2 AS Value;")]
    [InlineData("SELECT 1 AS Value UNION SELECT 2 AS Value INTO #written;")]
    [InlineData("SELECT 1 AS Value UNION SELECT 2 AS Value INTERSECT SELECT 3 AS Value INTO #written;")]
    [InlineData("SELECT 1 AS Value EXCEPT SELECT 2 AS Value INTO #written;")]
    public async Task Designer_Run_RejectsNestedSelectInto(string script)
    {
        using var factory = new ModuleFenceFactory(_ => { });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, token, "/api/designer/run", new { script });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_Module_Does_Not_Replace_Authentication_Challenge()
    {
        using var factory = new ModuleFenceFactory(config => config.Modules.Reporting = false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/reports/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class ModuleFenceFactory(Action<PortalConfig> customize) : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config) => customize(config);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await SendAsync(client, HttpMethod.Post, initial.AccessToken, "/api/auth/change-password",
            new { currentPassword = "Admin@12345!", newPassword = "Admin@ModuleFence99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return (await LoginAsync(client, "admin", "Admin@ModuleFence99!")).AccessToken;
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginAsync(
        HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        return (body!["token"]!.GetValue<string>(), body["refreshToken"]!.GetValue<string>());
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method,
        string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
