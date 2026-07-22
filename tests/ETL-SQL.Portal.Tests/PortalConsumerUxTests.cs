using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Models;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class PortalConsumerUxTests : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client;

    public PortalConsumerUxTests(PortalWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_client.DefaultRequestHeaders.Authorization != null) return;

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        if (loginRes.IsSuccessStatusCode)
        {
            var body = await loginRes.Content.ReadFromJsonAsync<JsonObject>();
            var token = body?["token"]?.ToString();

            using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            cpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            cpReq.Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
            await _client.SendAsync(cpReq);

            var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@Tests99!" });
            var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>();
            token = reloginBody?["token"]?.ToString() ?? token;

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@Tests99!" });
            reloginRes.EnsureSuccessStatusCode();
            var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>();
            var token = reloginBody?["token"]?.ToString();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    [Fact]
    public async Task Search_WithFuzzyTokens_ReturnsSuccess()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.GetAsync("/api/catalog/search?q=Sales");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<CatalogSearchResultDto>>();
        Assert.NotNull(items);
    }

    [Fact]
    public async Task ConsumerHome_ReturnsAllDashboardCategories()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.GetAsync("/api/catalog/consumer-home");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var home = await response.Content.ReadFromJsonAsync<ConsumerHomeDto>();
        Assert.NotNull(home);
        Assert.NotNull(home.Favorites);
        Assert.NotNull(home.Recent);
        Assert.NotNull(home.Featured);
        Assert.NotNull(home.Popular);
    }

    [Fact]
    public async Task GetAccessInfo_ReturnsReportMetadataOrNotFound()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.GetAsync("/api/reports/99999/access-info");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequestAccess_ReturnsReportNotFoundForInvalidId()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.PostAsJsonAsync("/api/reports/99999/request-access", new RequestReportAccessDto("Need access"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
