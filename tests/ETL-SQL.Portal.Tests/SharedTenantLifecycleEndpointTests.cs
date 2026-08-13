using System.Net;
using System.Net.Http.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Integration")]
public sealed class SharedTenantLifecycleEndpointTests
{
    [Fact]
    public async Task OnlySignedPlatformLifecycleIdentityCanMutateItsTenant()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var body = new
        {
            operationId = "endpoint-provision-alpha",
            kind = "Provision",
            authorizationReference = "change-p",
            targetRelease = "release-1",
            maxConcurrentJobs = 3,
            maxStorageMb = 2048,
            maxReportSessions = 4
        };

        using (var tenantAdmin = Request(new OrchestratorCaller(
                   "user", "1", "tenant admin", ["Admin", "PlatformLifecycle"], [], "tenant-alpha"), body))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(tenantAdmin)).StatusCode);

        using (var platformWithoutRole = Request(new OrchestratorCaller(
                   "platform", "operator", "operator", [], [], "tenant-alpha"), body))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(platformWithoutRole)).StatusCode);

        using (var platform = Request(new OrchestratorCaller(
                   "platform", "operator", "operator", ["PlatformLifecycle"], [], "tenant-alpha"), body))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(platform)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISharedTenantLifecycleStore>();
        var alpha = await store.GetSharedTenantStateAsync(
            TenantContext.FromVerifiedCredential("tenant-alpha"));
        var beta = await store.GetSharedTenantStateAsync(
            TenantContext.FromVerifiedCredential("tenant-beta"));
        Assert.Equal("Active", alpha!.State);
        Assert.Null(beta);
    }

    [Fact]
    public async Task UpgradeCancelsQueuedAdmissionsAndRemainsDrainingUntilActiveRuntimeCompletes()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var platform = new OrchestratorCaller(
            "platform", "operator", "operator", ["PlatformLifecycle"], [], "tenant-alpha");
        using (var provision = Request(platform, new
               {
                   operationId = "drain-provision-alpha", kind = "Provision",
                   authorizationReference = "change-p", targetRelease = "release-1",
                   maxConcurrentJobs = 3, maxStorageMb = 2048, maxReportSessions = 4
               }))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(provision)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var admissions = scope.ServiceProvider.GetRequiredService<ISandboxAdmissionLedger>();
        var tenant = TenantContext.FromVerifiedCredential("tenant-alpha");
        var policy = new ResolvedSandboxAdmissionPolicy
        {
            PoolId = "shared-hardened",
            TenantWeight = 1,
            MaxConcurrentAttempts = 2,
            MaxQueuedAttempts = 8
        };
        await admissions.EnqueueAsync("drain-active", tenant, policy);
        await admissions.EnqueueAsync("drain-queued", tenant, policy);
        var token = (await admissions.TryActivateAsync(
            "drain-active", "node-a", 3, TimeSpan.FromMinutes(5)))!.Value;
        var upgradeBody = new
        {
            operationId = "drain-upgrade-alpha", kind = "Upgrade",
            authorizationReference = "change-u", targetRelease = "release-2",
            maxConcurrentJobs = 4, maxStorageMb = 4096, maxReportSessions = 5
        };

        using (var upgrade = Request(platform, upgradeBody))
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(upgrade)).StatusCode);
        Assert.Equal(SandboxAdmissionState.Cancelled,
            (await admissions.ReadAsync("drain-queued"))!.State);
        Assert.Equal(SandboxAdmissionState.Active,
            (await admissions.ReadAsync("drain-active"))!.State);

        Assert.True(await admissions.TryCompleteAsync(
            "drain-active", "node-a", token));
        using (var retry = Request(platform, upgradeBody))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(retry)).StatusCode);
        var lifecycle = scope.ServiceProvider.GetRequiredService<ISharedTenantLifecycleStore>();
        Assert.Equal("release-2", (await lifecycle.GetSharedTenantStateAsync(tenant))!.ActiveRelease);
    }

    private static HttpRequestMessage Request(OrchestratorCaller caller, object body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/platform/shared-tenants/lifecycle")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        return request;
    }
}
