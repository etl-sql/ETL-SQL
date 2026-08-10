using System.Net;
using System.Text;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class SharedOidcAuthenticationServiceTests
{
    [Fact]
    public async Task AuthorizationRequestUsesOnlyRoutedAuthorityClientAndIssuer()
    {
        var service = Service("https://alpha.idp.test");
        var binding = Binding("https://alpha.idp.test", "alpha-client");

        var request = await service.BuildAuthorizationRequestAsync(
            binding, "https://alpha.portal.test/api/auth/oidc/callback");

        var uri = new Uri(request.AuthorizationUrl);
        Assert.Equal("alpha.idp.test", uri.Host);
        Assert.Contains("client_id=alpha-client", uri.Query);
        Assert.Contains("redirect_uri=https%3A%2F%2Falpha.portal.test", uri.Query);
        Assert.DoesNotContain("tenant-alpha", request.AuthorizationUrl);
    }

    [Fact]
    public async Task DiscoveryIssuerMismatchFailsBeforeBrowserRedirect()
    {
        var service = Service("https://other.idp.test");

        var error = await Assert.ThrowsAsync<OidcAuthenticationException>(() =>
            service.BuildAuthorizationRequestAsync(
                Binding("https://alpha.idp.test", "alpha-client"),
                "https://alpha.portal.test/api/auth/oidc/callback"));

        Assert.Contains("issuer did not match", error.Message);
    }

    private static SharedOidcAuthenticationService Service(string discoveredIssuer)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new SharedOidcAuthenticationService(
            new PortalConfig(),
            new StaticHttpClientFactory(new HttpClient(new DiscoveryHandler(discoveredIssuer))),
            services,
            null!);
    }

    private static SharedIdentityAuthorityBinding Binding(string issuer, string clientId) => new(
        "alpha", "tenant-alpha", "alpha.portal.test", "alpha.example.test",
        issuer, clientId, ClientSecretConfigured: false, Version: 1);

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class DiscoveryHandler(string issuer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = $$"""
                {
                  "issuer": "{{issuer}}",
                  "authorization_endpoint": "{{issuer}}/authorize",
                  "token_endpoint": "{{issuer}}/token"
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
