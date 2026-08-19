using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class GatewayEnrollmentControllerTests : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client;
    private readonly PortalWebFactory _factory;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private static string? _adminToken;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public GatewayEnrollmentControllerTests(PortalWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task ListGateways_RequiresAuthentication()
    {
        var res = await _client.GetAsync("/api/admin/gateways");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task GatewayEnrollment_Lifecycle_IssueListAndRevoke()
    {
        var token = await GetAdminTokenAsync();
        var gatewayId = "test-gw-" + Guid.NewGuid().ToString("N")[..8];

        // 1. Issue enrollment
        var issueReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/gateways/enroll")
        {
            Content = JsonContent.Create(new { GatewayId = gatewayId, ExpirationMinutes = 30 })
        };
        issueReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var issueRes = await _client.SendAsync(issueReq);
        Assert.Equal(HttpStatusCode.OK, issueRes.StatusCode);

        var issueJson = await issueRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.NotNull(issueJson);
        Assert.Equal(gatewayId, issueJson["gatewayId"]?.ToString());
        Assert.NotNull(issueJson["oneTimeToken"]?.ToString());

        // 2. List gateways
        var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/gateways");
        listReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var listRes = await _client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        var listJson = await listRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.NotNull(listJson);
        var match = listJson.FirstOrDefault(item => item?["gatewayId"]?.ToString() == gatewayId);
        Assert.NotNull(match);
        Assert.Equal("Pending", match["state"]?.ToString());
        Assert.False(match["isOnline"]?.GetValue<bool>());

        // 3. Revoke gateway
        var revokeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/gateways/{gatewayId}/revoke");
        revokeReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var revokeRes = await _client.SendAsync(revokeReq);
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        // 4. Verify status after revocation
        var listReq2 = new HttpRequestMessage(HttpMethod.Get, "/api/admin/gateways");
        listReq2.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var listRes2 = await _client.SendAsync(listReq2);
        var listJson2 = await listRes2.Content.ReadFromJsonAsync<JsonArray>(_json);
        var match2 = listJson2?.FirstOrDefault(item => item?["gatewayId"]?.ToString() == gatewayId);
        Assert.NotNull(match2);
        Assert.Equal("Revoked", match2["state"]?.ToString());
    }

    private async Task<string> GetAdminTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_adminToken is not null) return _adminToken;

            var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@12345!"
            });
            loginRes.EnsureSuccessStatusCode();
            var token = (await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();

            using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            cpReq.Headers.Authorization = new("Bearer", token);
            cpReq.Content = JsonContent.Create(new
            {
                currentPassword = "Admin@12345!",
                newPassword = "Admin@Tests99!"
            });
            (await _client.SendAsync(cpReq)).EnsureSuccessStatusCode();

            var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@Tests99!"
            });
            reloginRes.EnsureSuccessStatusCode();
            _adminToken = (await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();

            return _adminToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
