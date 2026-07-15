using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public class PortalConnectionCatalogApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Endpoints_RejectAnonymousCallers()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/connections")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/api/admin/connections/x", new { connectorType = "MSSQL" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/connections/export")).StatusCode);
    }

    [Fact]
    public async Task Lifecycle_SetVerifyDisableDeleteExportImport_WithAudit()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        // stage the secret the entry references
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortalSecretStoreService>()
                .StoreAsync("sales_db_password", "s3cret-value");
        }

        // create
        var set = await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/sales_dw", new
        {
            connectorType = "MSSQL",
            options = new Dictionary<string, string>
            {
                ["SERVER"] = "sql01",
                ["DATABASE"] = "Sales",
                ["PASSWORD"] = "SECRET:sales_db_password"
            },
            environmentScope = "Prod"
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        // list + detail: reference visible, secret value never present
        var list = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections", null);
        Assert.Contains("sales_dw", await list.Content.ReadAsStringAsync());
        var detail = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/sales_dw", null);
        var detailBody = await detail.Content.ReadAsStringAsync();
        Assert.Contains("SECRET:sales_db_password", detailBody);
        Assert.DoesNotContain("s3cret-value", detailBody);

        // verify resolves the SECRET: reference
        var verify = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/verify", null);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var verifyBody = await verify.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal(1, verifyBody!["secretReferences"]!.GetValue<int>());

        // engine-facing provider resolves the definition and touches last-used
        var provider = factory.Services.GetRequiredService<IConnectionCatalogProvider>();
        Assert.Equal("PortalCatalog", provider.ProviderName);
        var definition = await provider.ResolveAsync("sales_dw");
        Assert.Equal("MSSQL", definition.ConnectorType);
        Assert.Equal("sql01", definition.Options["SERVER"]);
        using (var scope = factory.Services.CreateScope())
        {
            var entity = await scope.ServiceProvider.GetRequiredService<PortalDbContext>()
                .PortalSharedConnections.AsNoTracking().SingleAsync(c => c.Alias == "sales_dw");
            Assert.NotNull(entity.LastUsedAtUtc);
            Assert.NotNull(entity.LastVerifiedAtUtc);
        }

        // export → delete → import round-trip
        var export = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/export", null);
        var exported = await export.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.Single(exported!);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, token, "/api/admin/connections/sales_dw", null)).StatusCode);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("sales_dw"));

        var import = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/import", exported);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var reimported = await provider.ResolveAsync("sales_dw");
        Assert.Equal("SECRET:sales_db_password", reimported.Options["PASSWORD"]);

        // disable blocks resolution; enable restores it without re-supplying the definition
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/disable", null)).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("sales_dw"));
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/enable", null)).StatusCode);
        Assert.Equal("sql01", (await provider.ResolveAsync("sales_dw")).Options["SERVER"]);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/disable", null)).StatusCode);

        // audit trail
        using (var scope = factory.Services.CreateScope())
        {
            var actions = await scope.ServiceProvider.GetRequiredService<PortalDbContext>()
                .AuditLogs.Where(a => a.ResourceType == "PortalSharedConnection")
                .Select(a => a.Action).ToListAsync();
            Assert.Contains("SHARED_CONNECTION_CREATE", actions);
            Assert.Contains("SHARED_CONNECTION_VERIFY", actions);
            Assert.Contains("SHARED_CONNECTION_EXPORT", actions);
            Assert.Contains("SHARED_CONNECTION_IMPORT", actions);
            Assert.Contains("SHARED_CONNECTION_DELETE", actions);
            Assert.Contains("SHARED_CONNECTION_DISABLE", actions);
            Assert.Contains("SHARED_CONNECTION_ENABLE", actions);
        }
    }

    [Fact]
    public async Task UseAcls_RestrictExpansionToAdminsOwnersAndGrantedGroups()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        // catalog an unrestricted entry
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/acl_dw", new
            {
                connectorType = "MSSQL",
                options = new Dictionary<string, string> { ["SERVER"] = "sql01" }
            })).StatusCode);

        var provider = factory.Services.GetRequiredService<IConnectionCatalogProvider>();

        // no grants → usable without any identity (backward compatible)
        Assert.Equal("sql01", (await provider.ResolveAsync("acl_dw")).Options["SERVER"]);

        // create a group and grant it use
        var group = await SendAsync(client, HttpMethod.Post, token, "/api/admin/groups", new { name = "ConnUsers" });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        var groupId = (await group.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/acl_dw/acl", new { groupId })).StatusCode);

        var acl = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/acl_dw/acl", null);
        var aclBody = await acl.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.Single(aclBody!);
        Assert.Equal("ConnUsers", aclBody![0]!["groupName"]!.GetValue<string>());

        // once granted: no identity → denied (fail closed) and the denial is audited
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveAsync("acl_dw"));

        // non-member, non-admin identity → denied
        var outsider = new ExecutionIdentity
        {
            EffectiveUser = "bob",
            RealUser = "bob",
            IsAdmin = false,
            Groups = ["OtherTeam"]
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveAsync("acl_dw", outsider));

        // group member (name-based federated identity) → allowed
        var member = new ExecutionIdentity
        {
            EffectiveUser = "ann",
            RealUser = "ann",
            IsAdmin = false,
            Groups = ["connusers"]
        };
        Assert.Equal("sql01", (await provider.ResolveAsync("acl_dw", member)).Options["SERVER"]);

        // admin → allowed
        var admin = new ExecutionIdentity { EffectiveUser = "root", RealUser = "root", IsAdmin = true };
        Assert.Equal("sql01", (await provider.ResolveAsync("acl_dw", admin)).Options["SERVER"]);

        // revoke → unrestricted again
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, token, $"/api/admin/connections/acl_dw/acl/{groupId}", null)).StatusCode);
        Assert.Equal("sql01", (await provider.ResolveAsync("acl_dw")).Options["SERVER"]);

        using var scope = factory.Services.CreateScope();
        var actions = await scope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .AuditLogs.Where(a => a.ResourceType == "PortalSharedConnection" && a.ResourceId == "acl_dw")
            .Select(a => a.Action).ToListAsync();
        Assert.Contains("SHARED_CONNECTION_GRANT_USE", actions);
        Assert.Contains("SHARED_CONNECTION_REVOKE_USE", actions);
        Assert.Contains("SHARED_CONNECTION_USE_DENIED", actions);
    }

    [Fact]
    public async Task DesignerSchema_UsesCatalogAclsAndDoesNotLeakRestrictedSchema()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/designer_mock", new
            {
                connectorType = "MOCKDB",
                options = new Dictionary<string, string>()
            })).StatusCode);

        var group = await SendAsync(client, HttpMethod.Post, token, "/api/admin/groups", new { name = "DesignerConnUsers" });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        var groupId = (await group.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/designer_mock/acl", new { groupId })).StatusCode);

        var adminSchema = await SendAsync(client, HttpMethod.Get, token, "/api/designer/schema?connection=designer_mock", null);
        Assert.Equal(HttpStatusCode.OK, adminSchema.StatusCode);
        var adminBody = await adminSchema.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Contains(adminBody!["tables"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "Users");

        var outsider = await CreateReadyUserAsync(client, token, "designer_outsider", "Publisher");
        var deniedSchema = await SendAsync(client, HttpMethod.Get, outsider.AccessToken, "/api/designer/schema?connection=designer_mock", null);
        var deniedBody = await deniedSchema.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, deniedSchema.StatusCode);
        Assert.DoesNotContain("Users", deniedBody);
        Assert.DoesNotContain("Orders", deniedBody);

        var deniedComplete = await SendAsync(client, HttpMethod.Post, outsider.AccessToken, "/api/designer/complete", new
        {
            script = "SELECT * FROM designer_mock.",
            line = 0,
            column = 28,
            connectionRef = "designer_mock"
        });
        var deniedCompleteBody = await deniedComplete.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, deniedComplete.StatusCode);
        Assert.DoesNotContain("Users", deniedCompleteBody);
        Assert.DoesNotContain("Orders", deniedCompleteBody);
    }

    [Fact]
    public async Task SensitiveFields_RoundTripMaskExportAndResolve()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortalSecretStoreService>()
                .StoreAsync("prod_host", "pg01.internal");
        }

        var set = await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/sensitive_dw", new
        {
            connectorType = "POSTGRES",
            target = "Host=pg01.internal;Database=dw",
            options = new Dictionary<string, string>
            {
                ["HOST"] = "SECRET:prod_host",
                ["DATABASE"] = "dw"
            },
            sensitiveFields = new[] { "HOST" }
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var detail = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/sensitive_dw", null);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.Content.ReadAsStringAsync();
        Assert.Contains("SECRET:prod_host", body);
        Assert.Contains("\"sensitiveFields\":[\"HOST\"]", body);
        Assert.DoesNotContain("pg01.internal", body);

        var provider = factory.Services.GetRequiredService<IConnectionCatalogProvider>();
        var definition = await provider.ResolveAsync("sensitive_dw");
        Assert.Contains("HOST", definition.SensitiveFields!);

        var export = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/export", null);
        var exported = await export.Content.ReadFromJsonAsync<JsonArray>(Json);
        var entry = Assert.Single(exported!);
        Assert.Equal("HOST", entry!["sensitiveFields"]![0]!.GetValue<string>());

        var maskedSave = await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/sensitive_dw", new
        {
            connectorType = "POSTGRES",
            target = "Host=********;Database=dw",
            options = new Dictionary<string, string> { ["DATABASE"] = "dw" },
            sensitiveFields = new[] { "HOST" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, maskedSave.StatusCode);
        Assert.Contains("masked display placeholder", await maskedSave.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Impact_ListsReferencingScriptsJobsCatalogEntriesAndConsumers()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        // a catalog entry whose credential references a secret
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/impact_dw", new
            {
                connectorType = "MSSQL",
                options = new Dictionary<string, string> { ["SERVER"] = "sql01", ["PASSWORD"] = "SECRET:impact_secret" }
            })).StatusCode);

        // a published report whose script references both the alias and the secret
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var config = factory.Services.GetRequiredService<PortalConfig>();
            var scriptRoot = Path.GetFullPath(config.ScriptRootPath);
            Directory.CreateDirectory(scriptRoot);
            await File.WriteAllTextAsync(
                Path.Combine(scriptRoot, "impact_report.rptsql"),
                "CREATE CONNECTION dw AS MSSQL('SHARED:impact_dw');\n-- also uses SECRET:impact_secret");

            var folder = new Folder { Name = "impact", Path = "/impact", OwnerId = 1 };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            db.Reports.Add(new Report
            {
                FolderId = folder.Id,
                Name = "Impact Report",
                ScriptPath = "impact_report.rptsql",
                CreatedBy = 1
            });
            await db.SaveChangesAsync();
        }

        // an orchestrator scheduled job whose script value references the alias
        var jobs = factory.Services.GetRequiredService<IJobHistoryStore>();
        await jobs.InitializeAsync();
        await jobs.SaveJobAsync(new JobDefinition(
            "impact-job", "RUN SCRIPT referencing SHARED:impact_dw", 1, "days", null, null, null));

        // a recorded consumer via resolution
        var provider = factory.Services.GetRequiredService<IConnectionCatalogProvider>();
        var ann = new ExecutionIdentity { EffectiveUser = "ann", RealUser = "ann", IsAdmin = false };
        _ = await provider.ResolveAsync("impact_dw", ann);

        var connImpact = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/impact_dw/impact", null);
        Assert.Equal(HttpStatusCode.OK, connImpact.StatusCode);
        var connBody = await connImpact.Content.ReadAsStringAsync();
        Assert.Contains("Impact Report", connBody);
        Assert.Contains("impact-job", connBody);
        Assert.Contains("ann", connBody);

        // the secret's impact includes the report script and the catalog entry that references it
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortalSecretStoreService>()
                .StoreAsync("impact_secret", "value");
        }

        var secretImpact = await SendAsync(client, HttpMethod.Get, token, "/api/admin/secrets/impact_secret/impact", null);
        Assert.Equal(HttpStatusCode.OK, secretImpact.StatusCode);
        var secretBody = await secretImpact.Content.ReadAsStringAsync();
        Assert.Contains("Impact Report", secretBody);
        Assert.Contains("impact_dw", secretBody);
    }

    [Fact]
    public async Task Set_RejectsRawCredentialValues()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/bad", new
        {
            connectorType = "MSSQL",
            options = new Dictionary<string, string> { ["PASSWORD"] = "hunter2" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SECRET:name", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Detail_MasksNonReferenceCredentialValues()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        // Simulate a legacy/imported row that bypassed write-side validation.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.PortalSharedConnections.Add(new PortalSharedConnection
            {
                Alias = "legacy",
                ConnectorType = "MSSQL",
                Target = "Server=db;Password=raw-value",
                OptionsJson = """{"TOKEN":"raw-token"}"""
            });
            await db.SaveChangesAsync();
        }

        var detail = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/legacy", null);
        var body = await detail.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.DoesNotContain("raw-value", body);
        Assert.DoesNotContain("raw-token", body);
        Assert.Contains("********", body);
    }

    [Fact]
    public async Task Test_ProbesConnectionThroughSharedCoreWithAuditAndNoSecretLeak()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortalSecretStoreService>()
                .StoreAsync("probe_db_password", "p@ss-should-not-leak");
        }

        // unknown alias → 404
        var missing = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/nope/test", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // catalog an entry pointing at an unresolvable host (.invalid never resolves) so the probe
        // completes deterministically without external network access.
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/probe_dw", new
            {
                connectorType = "MSSQL",
                options = new Dictionary<string, string>
                {
                    ["SERVER"] = "no-such-host.invalid.example",
                    ["PORT"] = "1433",
                    ["PASSWORD"] = "SECRET:probe_db_password",
                },
                environmentScope = "Prod",
            })).StatusCode);

        var test = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/probe_dw/test", null);
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        var body = await test.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("probe_dw", body!["alias"]!.GetValue<string>());
        Assert.False(body["succeeded"]!.GetValue<bool>());
        var steps = body["steps"]!.AsArray();
        Assert.NotEmpty(steps);
        Assert.Contains(steps, s => s!["layer"]!.GetValue<string>() == "POLICY");
        // The resolved secret value is never fetched by a DNS/TCP probe and must never surface.
        Assert.DoesNotContain("p@ss-should-not-leak", await test.Content.ReadAsStringAsync());

        // disabled → 409
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/probe_dw/disable", null)).StatusCode);
        var disabled = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/probe_dw/test", null);
        Assert.Equal(HttpStatusCode.Conflict, disabled.StatusCode);

        using var auditScope = factory.Services.CreateScope();
        var actions = await auditScope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .AuditLogs.Where(a => a.ResourceType == "PortalSharedConnection" && a.ResourceId == "probe_dw")
            .Select(a => a.Action).ToListAsync();
        Assert.Contains("SHARED_CONNECTION_TEST", actions);
    }

    private sealed class CatalogFactory : PortalWebFactory
    {
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Governance:Secrets:Provider"] = "PortalStore";
            settings["Governance:ConnectionCatalog:Provider"] = "Portal";
        }
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await SendAsync(client, HttpMethod.Post, initial.AccessToken, "/api/auth/change-password",
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return (await LoginAsync(client, "admin", "Admin@Tests99!")).AccessToken;
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginAsync(
        HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        return (body!["token"]!.GetValue<string>(), body["refreshToken"]!.GetValue<string>());
    }

    private static async Task<(int UserId, string AccessToken)> CreateReadyUserAsync(
        HttpClient client,
        string adminToken,
        string usernamePrefix,
        string role)
    {
        var username = $"{usernamePrefix}_{Guid.NewGuid():N}"[..20];
        const string initialPassword = "User@Test1!";
        const string changedPassword = "User@Test2!";
        var create = await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = initialPassword,
            role
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var userId = (await create.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var initial = await LoginAsync(client, username, initialPassword);
        var change = await SendAsync(client, HttpMethod.Post, initial.AccessToken, "/api/auth/change-password", new
        {
            currentPassword = initialPassword,
            newPassword = changedPassword
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        return (userId, (await LoginAsync(client, username, changedPassword)).AccessToken);
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
