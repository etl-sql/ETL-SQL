using System.Net;
using System.Net.Http.Json;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public class RateLimitingTests
{
    [Fact]
    public async Task AuthEndpoint_Returns429AfterConfiguredLimit()
    {
        using var factory = new PortalWebFactory(authPermitLimit: 2, anonymousTokenPermitLimit: 3);
        using var client = factory.CreateClient();
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= 2; attempt++)
        {
            response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = $"rate-limit-missing-{attempt}",
                password = "not-a-password"
            });
        }

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("60", Assert.Single(response.Headers.GetValues("Retry-After")));
    }

    [Fact]
    public async Task AnonymousTokenEndpoints_DeclareRateLimitPolicy()
    {
        using var factory = new PortalWebFactory(authPermitLimit: 2, anonymousTokenPermitLimit: 3);
        using var client = factory.CreateClient();
        var shareResponses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt <= 3; attempt++)
            shareResponses.Add(await client.GetAsync($"/api/share/missing-{attempt}"));

        Assert.All(shareResponses.Take(3), response =>
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, shareResponses[^1].StatusCode);

        // The partition includes the endpoint path, so embed resolution has its own bucket.
        var embedResponse = await client.GetAsync("/api/embed/missing");
        Assert.Equal(HttpStatusCode.NotFound, embedResponse.StatusCode);
    }
}
