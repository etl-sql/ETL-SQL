using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedTenantHttpBoundaryTests
{
    [Fact]
    public async Task SignedTenantClaimScopesStoreDespiteCallerTenantSelectors()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string token;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>()
                .FindByNameAsync("admin") ?? throw new InvalidOperationException("Seeded admin was not found.");
            admin.TenantId = "tenant-alpha";
            admin.MustChangePassword = false;
            db.PortalSecrets.AddRange(
                new PortalSecret { TenantId = "tenant-alpha", Name = "alpha-visible", EncryptedValue = "unused" },
                new PortalSecret { TenantId = "tenant-beta", Name = "beta-hidden", EncryptedValue = "unused" });
            await db.SaveChangesAsync();

            token = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
                admin,
                await scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>().GetRolesAsync(admin),
                tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/admin/secrets?tenant=tenant-beta&issuer=https%3A%2F%2Fevil.test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Tenant-Id", "tenant-beta");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("alpha-visible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("beta-hidden", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedPortalTokenWithoutTenantClaimIsRejectedBeforeControllerActivation()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string token;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var admin = await scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>()
                .FindByNameAsync("admin") ?? throw new InvalidOperationException("Seeded admin was not found.");
            var sharedConfig = scope.ServiceProvider.GetRequiredService<PortalConfig>();
            var legacyConfig = new PortalConfig
            {
                Jwt = sharedConfig.Jwt,
                SharedTenancy = new SharedTenancyConfig { Enabled = false }
            };
            token = new TokenService(legacyConfig).GenerateJwt(
                admin,
                await scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>().GetRolesAsync(admin));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/secrets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_tenant_credential", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesignerLeaseKeyCannotCrossTheSignedTenantBoundary()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string alphaToken;
        int betaReportId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin")
                ?? throw new InvalidOperationException("Seeded admin was not found.");
            admin.TenantId = "tenant-alpha";
            admin.MustChangePassword = false;

            var beta = new PortalUser
            {
                UserName = $"beta-{Guid.NewGuid():N}",
                Email = $"beta-{Guid.NewGuid():N}@test.local",
                TenantId = "tenant-beta",
                MustChangePassword = false,
                IsActive = true
            };
            Assert.True((await users.CreateAsync(beta, "Beta@Test99!")).Succeeded);
            var folder = new Folder
            {
                TenantId = "tenant-beta",
                Name = "Beta",
                Path = "/beta",
                OwnerId = beta.Id
            };
            var report = new Report
            {
                TenantId = "tenant-beta",
                Folder = folder,
                Name = "Beta only",
                ScriptPath = "beta/only.rptsql",
                CreatedBy = beta.Id
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            betaReportId = report.Id;

            alphaToken = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
                admin,
                await users.GetRolesAsync(admin),
                tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/designer/lease")
        {
            Content = JsonContent.Create(new { reportId = betaReportId })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var leased = await verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .Reports.AsNoTracking().SingleAsync(report => report.Id == betaReportId);
        Assert.Null(leased.EditSessionUserId);
    }

    [Fact]
    public async Task SharedResourceNamespacesIgnoreCallerTenantSelectorsAndForeignIds()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string alphaToken;
        long betaId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin")
                ?? throw new InvalidOperationException("Seeded admin was not found.");
            admin.TenantId = "tenant-alpha";
            admin.MustChangePassword = false;
            db.SharedTenantResources.Add(new SharedTenantResource
            {
                TenantId = "tenant-beta",
                Kind = "gateway",
                LogicalId = "equal-id",
                ScopedId = "tenant-beta/gateway/equal-id"
            });
            await db.SaveChangesAsync();
            betaId = await db.SharedTenantResources
                .Where(value => value.TenantId == "tenant-beta")
                .Select(value => value.Id)
                .SingleAsync();
            alphaToken = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
                admin, await users.GetRolesAsync(admin),
                tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        }

        using var create = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/shared/resources/gateway?tenant=tenant-beta")
        {
            Content = JsonContent.Create(new { logicalId = "equal-id" })
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        create.Headers.Add("X-Tenant-Id", "tenant-beta");
        var created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<SharedTenantResourceDto>();
        Assert.Equal("tenant-alpha/gateway/equal-id", createdBody!.ScopedId);

        using var foreignNumeric = new HttpRequestMessage(
            HttpMethod.Get, $"/api/shared/resources/gateway/{betaId}?tenant=tenant-beta");
        foreignNumeric.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        foreignNumeric.Headers.Add("X-Tenant-Id", "tenant-beta");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreignNumeric)).StatusCode);

        using var foreignScope = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/shared/resources/gateway/by-scope?scopedId=tenant-beta%2Fgateway%2Fequal-id");
        foreignScope.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreignScope)).StatusCode);

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/shared/resources/gateway");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        var listed = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var values = await listed.Content.ReadFromJsonAsync<List<SharedTenantResourceDto>>();
        Assert.Single(values!);
        Assert.Equal("tenant-alpha/gateway/equal-id", values![0].ScopedId);
    }

    [Fact]
    public async Task SignedTenantScopesQualityJobsHistoryAndQuarantineSearchBelowController()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string alphaToken;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin")
                ?? throw new InvalidOperationException("Seeded admin was not found.");
            admin.TenantId = "tenant-alpha";
            admin.MustChangePassword = false;
            await db.SaveChangesAsync();

            var jobs = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            await jobs.SaveJobAsync(Job("tenant-alpha--daily", "tenant-alpha"));
            await jobs.SaveJobAsync(Job("tenant-beta--daily", "tenant-beta"));
            var evidence = scope.ServiceProvider.GetRequiredService<ITenantJobEvidenceStore>();
            await evidence.SetJobStateAsync(
                TenantContext.FromVerifiedCredential("tenant-alpha"),
                "tenant-alpha--daily",
                "dq:quarantine-manifest:same",
                JsonSerializer.Serialize(Manifest("tenant-alpha--daily", "alpha.q_rows")));
            await evidence.SetJobStateAsync(
                TenantContext.FromVerifiedCredential("tenant-beta"),
                "tenant-beta--daily",
                "dq:quarantine-manifest:same",
                JsonSerializer.Serialize(Manifest("tenant-beta--daily", "beta.q_rows")));

            alphaToken = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
                admin, await users.GetRolesAsync(admin),
                tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        }

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/data-quality/jobs?tenant=tenant-beta");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        listRequest.Headers.Add("X-Tenant-Id", "tenant-beta");
        var jobsBody = await (await client.SendAsync(listRequest)).Content.ReadAsStringAsync();
        Assert.Contains("tenant-alpha--daily", jobsBody, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-beta--daily", jobsBody, StringComparison.Ordinal);

        using var queueRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/data-quality/quarantine?tenant=tenant-beta");
        queueRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        queueRequest.Headers.Add("X-Tenant-Id", "tenant-beta");
        var queueBody = await (await client.SendAsync(queueRequest)).Content.ReadAsStringAsync();
        Assert.Contains("alpha.q_rows", queueBody, StringComparison.Ordinal);
        Assert.DoesNotContain("beta.q_rows", queueBody, StringComparison.Ordinal);

        using var foreignJobRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/data-quality/quarantine?jobName=tenant-beta--daily");
        foreignJobRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        Assert.Equal("[]", await (await client.SendAsync(foreignJobRequest)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignedTenantScopesFolderIdsPathsAndCatalogSearchForAdministrators()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string alphaToken;
        int betaFolderId;
        int alphaReportId;
        int betaReportId;
        int betaSubscriptionId;
        const string BetaJobId = "same-visible-job-id";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin")
                ?? throw new InvalidOperationException("Seeded admin was not found.");
            admin.TenantId = "tenant-alpha";
            admin.MustChangePassword = false;
            var beta = new PortalUser
            {
                UserName = $"beta-folder-{Guid.NewGuid():N}",
                Email = $"beta-folder-{Guid.NewGuid():N}@test.local",
                TenantId = "tenant-beta",
                MustChangePassword = false,
                IsActive = true
            };
            Assert.True((await users.CreateAsync(beta, "Beta@Test99!")).Succeeded);
            var alphaFolder = new Folder
            {
                TenantId = "tenant-alpha",
                Name = "Alpha marker",
                Path = "/shared",
                OwnerId = admin.Id
            };
            var betaFolder = new Folder
            {
                TenantId = "tenant-beta",
                Name = "Beta secret",
                Path = "/shared",
                OwnerId = beta.Id
            };
            db.Folders.AddRange(alphaFolder, betaFolder);
            await db.SaveChangesAsync();
            var alphaReport = new Report
            {
                TenantId = "tenant-alpha",
                FolderId = alphaFolder.Id,
                Name = "Alpha report",
                ScriptPath = "alpha/report.rptsql",
                CreatedBy = admin.Id
            };
            var betaReport = new Report
            {
                TenantId = "tenant-beta",
                FolderId = betaFolder.Id,
                Name = "Beta report secret",
                ScriptPath = "beta/report.rptsql",
                CreatedBy = beta.Id
            };
            db.Reports.AddRange(alphaReport, betaReport);
            await db.SaveChangesAsync();
            db.ReportSnapshots.Add(new ReportSnapshot
            {
                ReportId = betaReport.Id,
                ManifestPath = "tenant-beta/snapshots/secret.json",
                BuiltBy = beta.Id
            });
            var betaSubscription = new Subscription
            {
                ReportId = betaReport.Id,
                UserId = beta.Id,
                Name = "Beta subscription secret",
                Recipients = "beta-secret@test.local"
            };
            db.Subscriptions.Add(betaSubscription);
            db.ReportShareLinks.Add(new ReportShareLink
            {
                ReportId = betaReport.Id,
                CreatedBy = beta.Id,
                Name = "Beta share secret",
                Token = "beta-share-secret"
            });
            db.ReportEmbedTokens.Add(new ReportEmbedToken
            {
                ReportId = betaReport.Id,
                CreatedBy = beta.Id,
                Name = "Beta embed secret",
                Token = "beta-embed-secret"
            });
            db.PortalExecutionJobs.Add(new PortalExecutionJob
            {
                Id = BetaJobId,
                TenantId = "tenant-beta",
                ReportId = betaReport.Id,
                UserId = beta.Id,
                Status = "Running"
            });
            await db.SaveChangesAsync();
            betaFolderId = betaFolder.Id;
            alphaReportId = alphaReport.Id;
            betaReportId = betaReport.Id;
            betaSubscriptionId = betaSubscription.Id;
            alphaToken = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
                admin, await users.GetRolesAsync(admin),
                tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        }

        using var foreign = new HttpRequestMessage(
            HttpMethod.Get, $"/api/folders/{betaFolderId}?tenant=tenant-beta");
        foreign.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        foreign.Headers.Add("X-Tenant-Id", "tenant-beta");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreign)).StatusCode);

        using var tree = new HttpRequestMessage(HttpMethod.Get, "/api/folders");
        tree.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        var treeBody = await (await client.SendAsync(tree)).Content.ReadAsStringAsync();
        Assert.Contains("Alpha marker", treeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Beta secret", treeBody, StringComparison.Ordinal);

        using var search = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/search?q=secret");
        search.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        var searchBody = await (await client.SendAsync(search)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Beta secret", searchBody, StringComparison.Ordinal);

        using var alphaReportRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/reports/{alphaReportId}");
        alphaReportRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(alphaReportRequest)).StatusCode);

        using var foreignReport = new HttpRequestMessage(
            HttpMethod.Get, $"/api/reports/{betaReportId}?tenant=tenant-beta");
        foreignReport.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        foreignReport.Headers.Add("X-Tenant-Id", "tenant-beta");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreignReport)).StatusCode);

        using var foreignSnapshot = new HttpRequestMessage(
            HttpMethod.Get, $"/api/reports/{betaReportId}/export/csv?tenant=tenant-beta");
        foreignSnapshot.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreignSnapshot)).StatusCode);

        using var foreignSubscription = new HttpRequestMessage(
            HttpMethod.Get, $"/api/subscriptions/{betaSubscriptionId}?tenant=tenant-beta");
        foreignSubscription.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreignSubscription)).StatusCode);

        using var foreignJob = new HttpRequestMessage(
            HttpMethod.Get, $"/api/jobs/{BetaJobId}?tenant=tenant-beta");
        foreignJob.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        foreignJob.Headers.Add("X-Tenant-Id", "tenant-beta");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreignJob)).StatusCode);

        using var cancelForeignJob = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/jobs/{BetaJobId}?tenant=tenant-beta");
        cancelForeignJob.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(cancelForeignJob)).StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var betaJob = await verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .PortalExecutionJobs.AsNoTracking().SingleAsync(job => job.Id == BetaJobId);
        Assert.Equal("Running", betaJob.Status);

        using var anonymousInventory = new HttpRequestMessage(
            HttpMethod.Get, "/api/admin/anonymous-report-access?tenant=tenant-beta");
        anonymousInventory.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        anonymousInventory.Headers.Add("X-Tenant-Id", "tenant-beta");
        var anonymousInventoryBody = await (await client.SendAsync(anonymousInventory))
            .Content.ReadAsStringAsync();
        Assert.DoesNotContain("Beta share secret", anonymousInventoryBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Beta embed secret", anonymousInventoryBody, StringComparison.Ordinal);
    }

    private static JobDefinition Job(string name, string tenant) => new(
        name, "SELECT 1;", 1, "HOUR", null, null, null,
        DisplayName: "daily-quality", TenantId: tenant);

    private static QuarantineReplayManifest Manifest(string jobName, string target) => new(
        jobName,
        "quality/daily.etlsql",
        "load",
        "source.rows",
        target,
        true,
        null,
        ["id"],
        "same-schema",
        DateTimeOffset.UtcNow);

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
