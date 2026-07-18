using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// P1.7 EXPORT PORTAL CONFIGURATION: the admin-only export emits a replayable bootstrap script in
/// dependency order with logical names, ${...} secret placeholders (never real credentials), and
/// an explicit emitted/skipped/runtime-only summary — and the emitted script parses with the real
/// ETL-SQL parser.
/// </summary>
[Trait("Category", "Portal")]
public sealed class ConfigurationExportTests
{
    [Fact]
    public async Task Export_EmitsReplayableScript_WithPlaceholdersAndSummary()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Seed one of each scriptable resource through the public API.
        var groupId = (await PostAsync(client, adminToken, "/api/admin/groups",
            new { name = $"exp_grp_{suffix}", description = "Export test group" }))!["id"]!.GetValue<int>();
        var userId = (await PostAsync(client, adminToken, "/api/admin/users", new
        {
            username = $"exp_user_{suffix}",
            email = $"exp_{suffix}@test.local",
            password = "Initial@Test1!",
            role = "Publisher"
        }))!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post,
            $"/api/admin/groups/{groupId}/members", adminToken, new { userId }, version: 1)).StatusCode);
        var folderId = (await PostAsync(client, adminToken, "/api/folders",
            new { name = $"exp_folder_{suffix}", parentId = (int?)null }))!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post,
            $"/api/folders/{folderId}/acl", adminToken,
            new { groupId, permission = 2 }, version: 1)).StatusCode);
        var smtp = await PostAsync(client, adminToken, "/api/admin/smtp", new
        {
            alias = $"exp_smtp_{suffix}",
            host = "smtp.test.local",
            port = 587,
            username = "mailer",
            password = "smtp-secret-marker",
            fromAddress = "reports@test.local",
            useSsl = true
        });
        Assert.NotNull(smtp);
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"exp_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "-- export test report\n");
        Assert.NotNull(await PostAsync(client, adminToken, "/api/reports", new
        {
            folderId,
            name = $"Export Report {suffix}",
            description = "Export test report",
            scriptPath
        }));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.SingleAsync(r => r.Name == $"Export Report {suffix}");
            var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
            db.Subscriptions.Add(new Subscription
            {
                ReportId = report.Id,
                UserId = admin.Id,
                Name = $"Paused Refresh {suffix}",
                DeliverOnRefresh = true,
                Format = SubscriptionFormat.PDF,
                SmtpAlias = $"exp_smtp_{suffix}",
                Recipients = "zeta@test.local; alpha@test.local",
                ParametersJson = """{"region":"North"}""",
                IsActive = false
            });
            db.ReportAlerts.Add(new ReportAlert
            {
                ReportId = report.Id,
                OwnerId = admin.Id,
                Name = $"Paused Alert {suffix}",
                VisualName = "Revenue",
                Operator = ">",
                Threshold = 100,
                Recipient = "ops@test.local",
                SmtpAlias = $"exp_smtp_{suffix}",
                IsActive = false
            });
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(client, HttpMethod.Get,
            "/api/admin/configuration/export", adminToken, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var script = await response.Content.ReadAsStringAsync();

        // Dependency-ordered, logical-name statements.
        Assert.Contains($"CREATE GROUP 'exp_grp_{suffix}' WITH (DESCRIPTION = 'Export test group')", script);
        Assert.Contains($"CREATE USER 'exp_user_{suffix}' WITH (EMAIL = 'exp_{suffix}@test.local', " +
            $"PASSWORD = '${{PORTAL_USER_EXP_USER_{suffix.ToUpperInvariant()}_PASSWORD}}', ROLE = Publisher)", script);
        Assert.Contains($"ADD USER 'exp_user_{suffix}' TO GROUP 'exp_grp_{suffix}';", script);
        Assert.Contains($"CREATE FOLDER '/exp_folder_{suffix}';", script);
        Assert.Contains($"GRANT MANAGE ON FOLDER '/exp_folder_{suffix}' TO GROUP 'exp_grp_{suffix}';", script);
        Assert.Contains($"CREATE SMTP CONNECTION 'exp_smtp_{suffix}'", script);
        Assert.Contains($"PASSWORD = '${{SMTP_EXP_SMTP_{suffix.ToUpperInvariant()}_PASSWORD}}'", script);
        Assert.Contains($"PUBLISH REPORT 'Export Report {suffix}'", script);
        Assert.Contains($"CREATE SUBSCRIPTION 'Paused Refresh {suffix} [alpha@test.local]'", script);
        Assert.Contains($"CREATE SUBSCRIPTION 'Paused Refresh {suffix} [zeta@test.local]'", script);
        Assert.Contains("ON REFRESH", script);
        Assert.Contains("@region = 'North'", script);
        Assert.Contains(") DISABLE;", script);
        Assert.Contains(
            $"CREATE ALERT 'Paused Alert {suffix}' FOR REPORT '/exp_folder_{suffix}/Export Report {suffix}'",
            script);
        Assert.Contains($"AT exp_smtp_{suffix} DISABLE;", script);
        Assert.Contains("REQUIRED SECRETS", script);
        Assert.Contains("Export summary", script);
        Assert.Contains("Runtime-only (never exported as configuration):", script);

        // Companion content manifest + runbook (P1.10): the report's .rptsql is content to copy
        // separately, not reconstructed by the bootstrap.
        Assert.Contains("Companion content manifest", script);
        Assert.Contains("Report scripts to copy into the target script root", script);
        Assert.Contains(scriptPath, script);
        Assert.Contains("Exact-state disaster recovery", script);

        // No real secret material leaves the portal.
        Assert.DoesNotContain("smtp-secret-marker", script);
        Assert.DoesNotContain("Initial@Test1!", script);
        Assert.DoesNotContain("Admin@Tests99!", script);
        Assert.DoesNotContain("PasswordHash", script);

        // The emitted bootstrap is genuinely replayable: the real parser accepts it.
        var statements = new ETL_SQL.Core.Parser.Parser(
            new ETL_SQL.Core.Parser.Lexer(script).Tokenize()).Parse();
        Assert.NotEmpty(statements.Statements);
    }

    [Fact]
    public async Task Export_WithOrchestratorAlias_EmitsParseableScheduledProductionBootstrap()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderId = (await PostAsync(client, adminToken, "/api/folders",
            new { name = $"prod_folder_{suffix}", parentId = (int?)null }))!["id"]!.GetValue<int>();
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"prod_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "-- scheduled production report\nSELECT 1 AS Value;\n");
        Assert.NotNull(await PostAsync(client, adminToken, "/api/reports", new
        {
            folderId,
            name = $"Production Report {suffix}",
            description = "Clean scheduled production test",
            scriptPath
        }));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.SingleAsync(r => r.Name == $"Production Report {suffix}");
            db.DatasetJobs.Add(new DatasetJob
            {
                ReportId = report.Id,
                OrchestratorJobName = $"dev-refresh-{suffix}",
                RefreshInterval = "0 2 * * *"
            });
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(client, HttpMethod.Get,
            "/api/admin/configuration/export?orchestratorAlias=prod_orchestrator", adminToken, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains(
            $"CREATE REFRESH JOB FOR REPORT '/prod_folder_{suffix}/Production Report {suffix}' " +
            "SCHEDULE '0 2 * * *' AT prod_orchestrator;",
            script);
        Assert.DoesNotContain($"dev-refresh-{suffix}", script);
        Assert.DoesNotContain("Admin@Tests99!", script);
        Assert.DoesNotContain("Admin@12345!", script);

        var statements = new ETL_SQL.Core.Parser.Parser(
            new ETL_SQL.Core.Parser.Lexer(script).Tokenize()).Parse();
        Assert.NotEmpty(statements.Statements);
    }

    [Fact]
    public async Task Export_RequiresAdmin()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Assert.NotNull(await PostAsync(client, adminToken, "/api/admin/users", new
        {
            username = $"viewer_{suffix}",
            email = $"viewer_{suffix}@test.local",
            password = "Initial@Test1!",
            role = "Viewer"
        }));
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = $"viewer_{suffix}", password = "Initial@Test1!" });
        var viewerToken = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();

        var denied = await SendAsync(client, HttpMethod.Get,
            "/api/admin/configuration/export", viewerToken, null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    /// <summary>The scripted statement itself parses inside an EXECUTE portal block.</summary>
    [Fact]
    public void ExportPortalConfigurationStatement_Parses()
    {
        const string script = """
            EXECUTE portal BEGIN
                EXPORT PORTAL CONFIGURATION TO 'portal_bootstrap.txt';
            END;
            """;
        var parsed = new ETL_SQL.Core.Parser.Parser(
            new ETL_SQL.Core.Parser.Lexer(script).Tokenize()).Parse();
        Assert.NotEmpty(parsed.Statements);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static async Task<JsonObject?> PostAsync(
        HttpClient client, string token, string url, object body)
    {
        var response = await SendAsync(client, HttpMethod.Post, url, token, body);
        Assert.True(response.IsSuccessStatusCode,
            $"POST {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text)?.AsObject();
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        var relogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Tests99!" });
        return (await relogin.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string token, object? body, long? version = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (version.HasValue)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version.Value}\"");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}
