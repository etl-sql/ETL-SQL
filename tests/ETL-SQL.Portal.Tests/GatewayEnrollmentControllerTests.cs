using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Governance;
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

    [Fact]
    public async Task GatewayBootstrap_ConsumesTokenOnceWithoutAnAdminSession()
    {
        var adminToken = await GetAdminTokenAsync();
        var gatewayId = "bootstrap-gw-" + Guid.NewGuid().ToString("N")[..8];
        using var issue = new HttpRequestMessage(HttpMethod.Post, "/api/admin/gateways/enroll")
        {
            Content = JsonContent.Create(new { GatewayId = gatewayId, ExpirationMinutes = 30 })
        };
        issue.Headers.Authorization = new("Bearer", adminToken);
        using var issuedResponse = await _client.SendAsync(issue);
        issuedResponse.EnsureSuccessStatusCode();
        var issued = await issuedResponse.Content.ReadFromJsonAsync<JsonObject>(_json);
        var oneTimeToken = issued!["oneTimeToken"]!.GetValue<string>();

        var bootstrapRequest = new
        {
            TenantId = "default",
            OneTimeToken = oneTimeToken,
            WorkloadPublicKeyThumbprint = new string('A', 64)
        };
        using var consumed = await _client.PostAsJsonAsync(
            "/api/gateway/enrollment/consume", bootstrapRequest);
        Assert.Equal(HttpStatusCode.OK, consumed.StatusCode);
        var body = await consumed.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal(gatewayId, body?["gatewayId"]?.ToString());

        using var replay = await _client.PostAsJsonAsync(
            "/api/gateway/enrollment/consume", bootstrapRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.DoesNotContain(oneTimeToken, await replay.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PortalBroker_AcknowledgesOnlyAfterTheGatewayIsVisibleAsOnline()
    {
        using var workloadKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = workloadKey.ExportSubjectPublicKeyInfo();
        var thumbprint = Convert.ToHexString(SHA256.HashData(publicKey));
        var adminToken = await GetAdminTokenAsync();
        var gatewayId = "broker-gw-" + Guid.NewGuid().ToString("N")[..8];
        using var issue = new HttpRequestMessage(HttpMethod.Post, "/api/admin/gateways/enroll")
        {
            Content = JsonContent.Create(new { GatewayId = gatewayId, ExpirationMinutes = 30 })
        };
        issue.Headers.Authorization = new("Bearer", adminToken);
        using var issuedResponse = await _client.SendAsync(issue);
        issuedResponse.EnsureSuccessStatusCode();
        var issued = await issuedResponse.Content.ReadFromJsonAsync<JsonObject>(_json);
        var oneTimeToken = issued!["oneTimeToken"]!.GetValue<string>();

        using var consumed = await _client.PostAsJsonAsync("/api/gateway/enrollment/consume", new
        {
            TenantId = "default",
            OneTimeToken = oneTimeToken,
            WorkloadPublicKeyThumbprint = thumbprint
        });
        consumed.EnsureSuccessStatusCode();

        var webSocketClient = _factory.Server.CreateWebSocketClient();
        using var socket = await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/api/gateway/broker"), CancellationToken.None);
        var hello = new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            TenantId = "default",
            GatewayId = gatewayId,
            NodeId = "portal-route-test-node",
            WorkloadPublicKeyThumbprint = thumbprint,
            WorkloadPublicKey = Convert.ToBase64String(publicKey),
            PublishedResources =
            [
                new GatewayPublishedResource(
                    "orders", "MOCKDB", GatewayOperationClass.Read,
                    new GatewayResourceLimits(), GatewayResourceState.Approved, "Orders")
            ]
        };
        var payload = Encoding.UTF8.GetBytes(hello.Serialize());
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[4096];
        var received = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var challenge = GatewayFrame.Deserialize(Encoding.UTF8.GetString(buffer, 0, received.Count));
        Assert.Equal(GatewayFrameKind.Challenge, challenge.Kind);
        var proof = new GatewayFrame
        {
            Kind = GatewayFrameKind.Authenticate,
            TenantId = "default",
            GatewayId = gatewayId,
            Signature = Convert.ToBase64String(workloadKey.SignData(
                Convert.FromBase64String(challenge.Challenge!), HashAlgorithmName.SHA256))
        };
        payload = Encoding.UTF8.GetBytes(proof.Serialize());
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        received = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var ack = GatewayFrame.Deserialize(Encoding.UTF8.GetString(buffer, 0, received.Count));
        Assert.Equal(GatewayFrameKind.HelloAck, ack.Kind);

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/admin/gateways");
        list.Headers.Authorization = new("Bearer", adminToken);
        using var listResponse = await _client.SendAsync(list);
        var gateways = await listResponse.Content.ReadFromJsonAsync<JsonArray>(_json);
        var online = gateways?.FirstOrDefault(item => item?["gatewayId"]?.ToString() == gatewayId);
        Assert.True(online?["isOnline"]?.GetValue<bool>());
        Assert.Equal(1, online?["activeNodes"]?.GetValue<int>());

        // The same authenticated session is the data plane for a catalog-authorized typed request.
        var router = _factory.Services.GetRequiredService<IGatewayOperationRouter>();
        var identity = new ExecutionIdentity
        {
            TenantId = "default",
            EffectiveUser = "admin",
            RealUser = "admin",
            IsAdmin = true
        };
        var routedTask = router.ExecuteAsync(
            identity, new GatewayResourceBinding(gatewayId, "orders"),
            GatewayOperationClass.Read, GatewayOperationEffect.ReadOnly,
            GatewayOperationBounds.Default, JsonSerializer.Serialize(new { Table = "Orders" }),
            null, CancellationToken.None);
        received = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var operation = GatewayFrame.Deserialize(Encoding.UTF8.GetString(buffer, 0, received.Count));
        Assert.Equal(GatewayFrameKind.Operation, operation.Kind);
        foreach (var responseFrame in new[]
        {
            new GatewayFrame
            {
                Kind = GatewayFrameKind.RowBatch,
                OperationId = operation.OperationId,
                Columns = ["id"],
                Rows = [["42"]]
            },
            new GatewayFrame
            {
                Kind = GatewayFrameKind.Complete,
                OperationId = operation.OperationId,
                OutcomeState = GatewayOutcomeState.Committed,
                RowsProduced = 1
            }
        })
        {
            payload = Encoding.UTF8.GetBytes(responseFrame.Serialize());
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        var routed = await routedTask;
        Assert.Equal("42", routed.Rows.Single().Single());

        using var revoke = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/gateways/{gatewayId}/revoke");
        revoke.Headers.Authorization = new("Bearer", adminToken);
        (await _client.SendAsync(revoke)).EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => router.ExecuteAsync(
            identity, new GatewayResourceBinding(gatewayId, "orders"),
            GatewayOperationClass.Read, GatewayOperationEffect.ReadOnly,
            GatewayOperationBounds.Default, JsonSerializer.Serialize(new { Table = "Orders" }),
            null, CancellationToken.None));

        socket.Abort();
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
