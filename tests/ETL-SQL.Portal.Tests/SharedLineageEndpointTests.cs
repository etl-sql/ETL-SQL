using System.Net;
using System.Net.Http.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Integration")]
public sealed class SharedLineageEndpointTests
{
    [Fact]
    public async Task SignedTenantControlsGraphScopeAndRequestSelectorsCannotReplaceIt()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ITenantLineageCatalogStore>();
            await catalog.SaveLineageAsync(
                TenantContext.FromVerifiedCredential("tenant-alpha"),
                [Entry("alpha")], "same-job", null, DateTime.UtcNow);
            await catalog.SaveLineageAsync(
                TenantContext.FromVerifiedCredential("tenant-beta"),
                [Entry("beta")], "same-job", null, DateTime.UtcNow);
        }

        using var client = factory.CreateClient();
        using var alphaRequest = Request("tenant-alpha",
            "/api/lineage/history/table/same.table?tenant=tenant-beta&limit=100");
        using var alphaResponse = await client.SendAsync(alphaRequest);
        Assert.Equal(HttpStatusCode.OK, alphaResponse.StatusCode);
        var alphaRows = await alphaResponse.Content.ReadFromJsonAsync<List<LineageHistoryEntry>>();
        var alpha = Assert.Single(alphaRows!);
        Assert.Equal("tenant-alpha", alpha.TenantId);
        Assert.Equal("alpha", alpha.Tags["owner"]);

        using var betaRequest = Request("tenant-beta", "/api/lineage/history/table/same.table");
        var beta = Assert.Single((await (await client.SendAsync(betaRequest)).Content
            .ReadFromJsonAsync<List<LineageHistoryEntry>>())!);
        Assert.Equal("tenant-beta", beta.TenantId);
        Assert.Equal("beta", beta.Tags["owner"]);
    }

    private static LineageEntry Entry(string owner) => new("same.table", "SELECT")
    {
        SourceTables = ["same.source"],
        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner"] = owner
        }
    };

    private static HttpRequestMessage Request(string tenant, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(
                new OrchestratorCaller("user", "1", "reader", [], [], tenant),
                OrchestratorWebFactory.IdentitySecret));
        return request;
    }
}
