using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Regressions for the 2026-11-01 Portal bug and security review, run against a Shared Tenancy
/// host: every request tenant must come from the verified credential, never from a selector.
/// </summary>
[Trait("Category", "Portal")]
public sealed class SharedPortalAuditRegressionTests
{
    private static readonly string AlphaMachine = Guid.NewGuid().ToString("N");
    private static readonly string BetaMachine = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task PolicyAuthority_SharedAdminCannotReadOrRevokeAnotherTenant()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        var token = await SeedAlphaAdminAsync(factory, async db =>
        {
            db.PolicyMachines.AddRange(
                new PolicyMachineEntity
                {
                    MachineId = AlphaMachine,
                    EnrollmentId = Guid.NewGuid().ToString("N"),
                    Tenant = "tenant-alpha",
                    Environment = "prod"
                },
                new PolicyMachineEntity
                {
                    MachineId = BetaMachine,
                    EnrollmentId = Guid.NewGuid().ToString("N"),
                    Tenant = "tenant-beta",
                    Environment = "prod"
                });
            await db.SaveChangesAsync();
        });

        using var active = await SendAsync(client, token, HttpMethod.Get,
            "/api/admin/policy-authority/active?tenant=tenant-beta&environment=prod");
        Assert.Equal(HttpStatusCode.Forbidden, active.StatusCode);

        using var machines = await SendAsync(client, token, HttpMethod.Get,
            "/api/admin/policy-authority/machines");
        var inventory = await machines.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, machines.StatusCode);
        Assert.Contains(AlphaMachine, inventory, StringComparison.Ordinal);
        Assert.DoesNotContain(BetaMachine, inventory, StringComparison.Ordinal);

        using var selected = await SendAsync(client, token, HttpMethod.Get,
            "/api/admin/policy-authority/machines?tenant=tenant-beta");
        Assert.Equal(HttpStatusCode.Forbidden, selected.StatusCode);

        using var revoke = await SendAsync(client, token, HttpMethod.Post,
            $"/api/admin/policy-authority/machines/{BetaMachine}/revoke", new { reason = "cross-tenant" });
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().PolicyMachines
            .AsNoTracking().SingleAsync(machine => machine.MachineId == BetaMachine);
        Assert.False(stored.Revoked);
    }

    [Fact]
    public async Task PasswordLogin_OnSharedHostIsRefusedBeforeAnyLookup()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("single sign-on", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .RefreshTokens.AnyAsync());
    }

    [Fact]
    public async Task AnonymousStudioSession_OnSharedHostAnswersUnauthorizedNotServerError()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/studio/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousShareLinkAndEmbedToken_ResolveAndAuditUnderTheReportTenant()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        var shareToken = Guid.NewGuid().ToString("N");
        var embedToken = Guid.NewGuid().ToString("N");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var beta = new PortalUser
            {
                UserName = $"beta-{Guid.NewGuid():N}",
                Email = $"beta-{Guid.NewGuid():N}@test.local",
                TenantId = "tenant-beta",
                MustChangePassword = false,
                IsActive = true
            };
            Assert.True((await users.CreateAsync(beta, "Beta@Test99!")).Succeeded);
            Assert.True((await users.AddToRoleAsync(beta, "Admin")).Succeeded);
            var report = new Report
            {
                TenantId = "tenant-beta",
                Folder = new Folder { TenantId = "tenant-beta", Name = "Beta", Path = "/beta", OwnerId = beta.Id },
                Name = "Beta shared",
                ScriptPath = "beta/shared.rptsql",
                CreatedBy = beta.Id
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            db.ReportShareLinks.Add(new ReportShareLink
            {
                ReportId = report.Id,
                CreatedBy = beta.Id,
                Name = "Share",
                Token = AnonymousReportTokens.Hash(shareToken)
            });
            db.ReportEmbedTokens.Add(new ReportEmbedToken
            {
                ReportId = report.Id,
                CreatedBy = beta.Id,
                Name = "Embed",
                Token = AnonymousReportTokens.Hash(embedToken)
            });
            await db.SaveChangesAsync();
        }

        using var share = await client.GetAsync($"/api/share/{shareToken}");
        Assert.Equal(HttpStatusCode.OK, share.StatusCode);
        using var embed = await client.GetAsync($"/api/embed/{embedToken}");
        Assert.Equal(HttpStatusCode.OK, embed.StatusCode);

        await using var verify = factory.Services.CreateAsyncScope();
        var audits = await verify.ServiceProvider.GetRequiredService<PortalDbContext>().AuditLogs
            .AsNoTracking()
            .Where(row => row.Action == "ANONYMOUS_SHARE_LINK_VIEW" || row.Action == "ANONYMOUS_EMBED_TOKEN_VIEW")
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, row => Assert.Equal("tenant-beta", row.TenantId));
    }

    [Fact]
    public async Task ServiceAccountTokenExchange_AuditsUnderTheAccountTenant()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        const string secret = "service-secret-for-shared-audit-0123456789";
        var clientId = $"svc-{Guid.NewGuid():N}";

        await SeedAlphaAdminAsync(factory, async db =>
        {
            var owner = await db.Users.SingleAsync(user => user.UserName == "admin");
            var account = new ServiceAccount
            {
                TenantId = "tenant-alpha",
                ClientId = clientId,
                Name = "alpha-svc",
                NormalizedName = "ALPHA-SVC",
                OwnerUserId = owner.Id,
                Scopes = "reports.read"
            };
            account.SecretHash = new PasswordHasher<ServiceAccount>().HashPassword(account, secret);
            db.ServiceAccounts.Add(account);
            await db.SaveChangesAsync();
        });

        using var response = await client.PostAsJsonAsync("/api/auth/service-token",
            new { clientId, clientSecret = secret });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var issued = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().AuditLogs
            .AsNoTracking().SingleAsync(row => row.Action == "SERVICE_ACCOUNT_TOKEN_ISSUED");
        Assert.Equal("tenant-alpha", issued.TenantId);
    }

    [Fact]
    public async Task AdminAndDesignerDiagnostics_StayInsideTheCallerTenantAndRankManageAboveAuthor()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        int betaFolderId = 0, alphaUserId = 0, alphaFolderId = 0;
        var betaGroup = $"beta-only-{Guid.NewGuid():N}";
        var token = await SeedAlphaAdminAsync(factory, async db =>
        {
            var users = db.Users;
            var alphaUser = new PortalUser
            {
                UserName = $"alpha-{Guid.NewGuid():N}",
                TenantId = "tenant-alpha",
                IsActive = true
            };
            var authors = new Group { TenantId = "tenant-alpha", Name = "alpha-authors" };
            var managers = new Group { TenantId = "tenant-alpha", Name = "alpha-managers" };
            var beta = new Group { TenantId = "tenant-beta", Name = betaGroup };
            var alphaFolder = new Folder { TenantId = "tenant-alpha", Name = "A", Path = "/a" };
            var betaFolder = new Folder { TenantId = "tenant-beta", Name = "B", Path = "/b" };
            users.Add(alphaUser);
            db.Groups.AddRange(authors, managers, beta);
            db.Folders.AddRange(alphaFolder, betaFolder);
            await db.SaveChangesAsync();
            db.UserGroups.AddRange(
                new UserGroup { TenantId = "tenant-alpha", UserId = alphaUser.Id, GroupId = authors.Id },
                new UserGroup { TenantId = "tenant-alpha", UserId = alphaUser.Id, GroupId = managers.Id });
            db.FolderAcls.AddRange(
                new FolderAcl { FolderId = alphaFolder.Id, GroupId = authors.Id, Permission = FolderPermission.Author },
                new FolderAcl { FolderId = alphaFolder.Id, GroupId = managers.Id, Permission = FolderPermission.Manage },
                new FolderAcl { FolderId = betaFolder.Id, GroupId = beta.Id, Permission = FolderPermission.Read });
            await db.SaveChangesAsync();
            betaFolderId = betaFolder.Id;
            alphaFolderId = alphaFolder.Id;
            alphaUserId = alphaUser.Id;
        });

        using var foreignFolder = await SendAsync(client, token, HttpMethod.Get,
            $"/api/admin/permissions/effective/folder/{betaFolderId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignFolder.StatusCode);

        using var effective = await SendAsync(client, token, HttpMethod.Get,
            $"/api/admin/permissions/effective/user/{alphaUserId}");
        Assert.Equal(HttpStatusCode.OK, effective.StatusCode);
        using (var json = System.Text.Json.JsonDocument.Parse(await effective.Content.ReadAsStringAsync()))
        {
            var folder = json.RootElement.GetProperty("folders").EnumerateArray()
                .Single(entry => entry.GetProperty("resourceId").GetInt32() == alphaFolderId);
            Assert.Equal("Manage", folder.GetProperty("permission").GetString());
        }

        using var previewAs = await SendAsync(client, token, HttpMethod.Get, "/api/designer/preview-as");
        var vocabulary = await previewAs.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, previewAs.StatusCode);
        Assert.Contains("alpha-managers", vocabulary, StringComparison.Ordinal);
        Assert.DoesNotContain(betaGroup, vocabulary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlertNames_AreUniquePerTenantNotPerHost()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        int alphaReportId = 0;
        var token = await SeedAlphaAdminAsync(factory, async db =>
        {
            var admin = await db.Users.SingleAsync(user => user.UserName == "admin");
            var alpha = new Report
            {
                TenantId = "tenant-alpha",
                Folder = new Folder { TenantId = "tenant-alpha", Name = "A", Path = "/alerts-a", OwnerId = admin.Id },
                Name = "Alpha",
                ScriptPath = "a/alpha.rptsql",
                CreatedBy = admin.Id
            };
            var beta = new Report
            {
                TenantId = "tenant-beta",
                Folder = new Folder { TenantId = "tenant-beta", Name = "B", Path = "/alerts-b", OwnerId = admin.Id },
                Name = "Beta",
                ScriptPath = "b/beta.rptsql",
                CreatedBy = admin.Id
            };
            db.Reports.AddRange(alpha, beta);
            await db.SaveChangesAsync();
            db.ReportAlerts.Add(new ReportAlert
            {
                TenantId = "tenant-beta",
                ReportId = beta.Id,
                OwnerId = admin.Id,
                Name = "daily-revenue",
                VisualName = "Revenue"
            });
            await db.SaveChangesAsync();
            alphaReportId = alpha.Id;
        });

        using var created = await SendAsync(client, token, HttpMethod.Post,
            $"/api/reports/{alphaReportId}/alerts",
            new { name = "daily-revenue", visualName = "Revenue", @operator = ">=", threshold = 1m });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var duplicate = await SendAsync(client, token, HttpMethod.Post,
            $"/api/reports/{alphaReportId}/alerts",
            new { name = "daily-revenue", visualName = "Revenue", @operator = ">=", threshold = 1m });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().ReportAlerts
            .AsNoTracking().SingleAsync(alert => alert.ReportId == alphaReportId);
        Assert.Equal("tenant-alpha", stored.TenantId);
    }

    private static async Task<string> SeedAlphaAdminAsync(
        PortalWebFactory factory, Func<PortalDbContext, Task>? seed = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var admin = await users.FindByNameAsync("admin")
            ?? throw new InvalidOperationException("Seeded admin was not found.");
        admin.TenantId = "tenant-alpha";
        admin.MustChangePassword = false;
        await db.SaveChangesAsync();
        if (seed is not null) await seed(db);
        return scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
            admin, await users.GetRolesAsync(admin),
            tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string? token, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private sealed class SharedPortalFactory : PortalWebFactory
    {
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings) =>
            settings["Portal:SharedTenancy:Enabled"] = "true";

        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.SharedTenancy = new SharedTenancyConfig { Enabled = true };

        protected override void CustomizeServices(IServiceCollection services) =>
            services.AddScoped<TenantContext>(sp =>
                sp.GetRequiredService<RequestTenantContextAccessor>().RequireCurrent());
    }
}
