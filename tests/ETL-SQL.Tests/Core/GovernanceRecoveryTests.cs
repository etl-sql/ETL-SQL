using System.Net;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// P2.2 governance recovery certification (Core surfaces): an unavailable remote policy endpoint
/// fails secure rather than silently running unprotected, and secret-provider failures surface as
/// errors rather than resolving to an empty/blank secret. Policy-cache freshness/expiry recovery is
/// certified in <see cref="OrganizationPolicyCacheTests"/>; the fail-closed audit mutation path is
/// certified in the Portal suite (GovernanceRecoveryCertificationTests).
/// </summary>
public class GovernanceRecoveryTests
{
    [Fact]
    public async Task PolicyEndpointUnavailable_WithNoCache_FailsSecure()
    {
        using var http = new HttpClient(new FailingHandler());
        var source = new HttpsOrganizationPolicySource(new Uri("https://policy.example.test/org-policy.json"), http);
        var loader = new CachedOrganizationPolicyLoader(
            new OrganizationPolicyLoader(new[] { (IOrganizationPolicySource)source }),
            new EmptyCacheStore(),
            new OrganizationPolicyCacheOptions { MaxOfflineAge = TimeSpan.FromHours(1) });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync());
        Assert.Contains("no offline cache is available", ex.Message);
    }

    [Fact]
    public async Task EnvironmentSecretProvider_MissingSecret_FailsClosed()
    {
        var provider = new EnvironmentSecretProvider(getEnvironmentVariable: _ => null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("sales_db_password"));
    }

    [Fact]
    public async Task HttpsVaultSecretProvider_EndpointFailure_FailsClosed()
    {
        using var http = new HttpClient(new FailingHandler());
        var provider = new HttpsVaultSecretProvider(new Uri("https://vault.example.test/secrets"), http);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.ResolveAsync("sales_db_password"));
    }

    [Fact]
    public async Task OsSecretStoreProvider_MissingSecret_FailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "secret_store_" + Guid.NewGuid().ToString("N")[..8]);
        var provider = new OsSecretStoreProvider(root);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("sales_db_password"));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class EmptyCacheStore : IOrganizationPolicyCacheStore
    {
        public Task<OrganizationPolicyCacheEntry?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationPolicyCacheEntry?>(null);

        public Task WriteAsync(OrganizationPolicyCacheEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
