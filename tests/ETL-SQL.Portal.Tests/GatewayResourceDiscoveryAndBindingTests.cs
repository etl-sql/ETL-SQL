using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Gateway;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class GatewayResourceDiscoveryAndBindingTests : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client;
    private readonly PortalWebFactory _factory;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private static string? _adminToken;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public GatewayResourceDiscoveryAndBindingTests(PortalWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GatewayResourceDiscovery_ReturnsApprovedResourcesWithoutLeakingEndpointsOrSecrets()
    {
        var adminToken = await GetAdminTokenAsync();
        var gatewayId = "disc-gw-" + Guid.NewGuid().ToString("N")[..8];

        // 1. Enroll gateway
        using var issue = new HttpRequestMessage(HttpMethod.Post, "/api/admin/gateways/enroll")
        {
            Content = JsonContent.Create(new { GatewayId = gatewayId, ExpirationMinutes = 30 })
        };
        issue.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var issuedResponse = await _client.SendAsync(issue);
        issuedResponse.EnsureSuccessStatusCode();
        var issued = await issuedResponse.Content.ReadFromJsonAsync<JsonObject>(_json);
        var oneTimeToken = issued!["oneTimeToken"]!.GetValue<string>();

        using var workloadKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = workloadKey.ExportSubjectPublicKeyInfo();
        var thumbprint = Convert.ToHexString(SHA256.HashData(publicKey));

        // 2. Consume token
        using var consumed = await _client.PostAsJsonAsync("/api/gateway/enrollment/consume", new
        {
            TenantId = "default",
            OneTimeToken = oneTimeToken,
            WorkloadPublicKeyThumbprint = thumbprint
        });
        consumed.EnsureSuccessStatusCode();

        // 3. Connect WebSocket broker with published resources (one approved, one pending)
        var webSocketClient = _factory.Server.CreateWebSocketClient();
        using var socket = await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/api/gateway/broker"), CancellationToken.None);

        var hello = new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            NodeId = "node-1",
            TenantId = "default",
            GatewayId = gatewayId,
            WorkloadPublicKeyThumbprint = thumbprint,
            WorkloadPublicKey = Convert.ToBase64String(publicKey),
            PublishedResources =
            [
                new GatewayPublishedResource(
                    "orders-dw",
                    "MSSQL",
                    GatewayOperationClass.Read | GatewayOperationClass.Write,
                    new GatewayResourceLimits(),
                    GatewayResourceState.Approved,
                    "Corporate Orders DW"),
                new GatewayPublishedResource(
                    "unapproved-db",
                    "POSTGRES",
                    GatewayOperationClass.Read,
                    new GatewayResourceLimits(),
                    GatewayResourceState.Disabled,
                    "Unapproved PG")
            ]
        };

        var payload = Encoding.UTF8.GetBytes(hello.Serialize());
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[8192];
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

        // 4. Query /api/admin/gateways/{gatewayId}/resources
        using var resReq = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/gateways/{gatewayId}/resources");
        resReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var resResp = await _client.SendAsync(resReq);
        resResp.EnsureSuccessStatusCode();

        var resources = await resResp.Content.ReadFromJsonAsync<List<GatewayEnrollmentController.GatewayDiscoveredResourceDto>>(_json);
        Assert.NotNull(resources);
        Assert.Single(resources); // Only approved resource returned

        var res = resources.Single();
        Assert.Equal("orders-dw", res.ResourceId);
        Assert.Equal("MSSQL", res.ConnectorType);
        Assert.Equal("Read, Write", res.AllowedOperations);
        Assert.Equal("Approved", res.State);
        Assert.True(res.IsOnline);
        Assert.True(res.LastSeenUtc <= DateTimeOffset.UtcNow);

        // Ensure raw JSON does not contain physical target or credentials
        var rawJson = await resResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-sql.internal", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supersecret", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-cred-ref", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Corporate Orders DW", rawJson, StringComparison.Ordinal);

        // 5. Query /api/connectors/gateways
        using var connGwReq = new HttpRequestMessage(HttpMethod.Get, "/api/connectors/gateways");
        connGwReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var connGwResp = await _client.SendAsync(connGwReq);
        connGwResp.EnsureSuccessStatusCode();

        var gwList = await connGwResp.Content.ReadFromJsonAsync<List<ConnectorsController.DiscoveredGatewayDto>>(_json);
        Assert.NotNull(gwList);
        var matchedGw = gwList.FirstOrDefault(g => g.Id == gatewayId);
        Assert.NotNull(matchedGw);
        Assert.True(matchedGw.IsOnline);
        Assert.NotNull(matchedGw.Resources);
        Assert.Single(matchedGw.Resources);
        Assert.Equal("orders-dw", matchedGw.Resources[0].ResourceId);

        // 6. Save connection bound to this resource
        var alias = "shared_orders_" + Guid.NewGuid().ToString("N")[..6];
        using var saveReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/connections/{alias}")
        {
            Content = JsonContent.Create(new
            {
                ConnectorType = "MSSQL",
                Gateway = new { GatewayId = gatewayId, ResourceId = "orders-dw" },
                EnvironmentScope = "Production"
            })
        };
        saveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var saveResp = await _client.SendAsync(saveReq);
        Assert.Equal(HttpStatusCode.NoContent, saveResp.StatusCode);

        // 7. Verify Get detail on saved connection
        using var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/connections/{alias}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var getResp = await _client.SendAsync(getReq);
        getResp.EnsureSuccessStatusCode();
        var detail = await getResp.Content.ReadFromJsonAsync<PortalSharedConnectionDetail>(_json);
        Assert.NotNull(detail);
        Assert.Equal("MSSQL", detail.Summary.ConnectorType);
        Assert.NotNull(detail.Gateway);
        Assert.Equal(gatewayId, detail.Gateway.GatewayId);
        Assert.Equal("orders-dw", detail.Gateway.ResourceId);

        // 8. Rejection on mismatched connector type
        var invalidAlias = "shared_invalid_" + Guid.NewGuid().ToString("N")[..6];
        using var invalidReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/connections/{invalidAlias}")
        {
            Content = JsonContent.Create(new
            {
                ConnectorType = "POSTGRES", // Mismatch with MSSQL
                Gateway = new { GatewayId = gatewayId, ResourceId = "orders-dw" }
            })
        };
        invalidReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var invalidResp = await _client.SendAsync(invalidReq);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResp.StatusCode);
        var errJson = await invalidResp.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Contains("Connector type mismatch", errJson?["error"]?.ToString(), StringComparison.OrdinalIgnoreCase);

        // 9. Rejection on physical host/target when gateway binding is set (Zero-Trust)
        using var targetViolationReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/connections/{invalidAlias}")
        {
            Content = JsonContent.Create(new
            {
                ConnectorType = "MSSQL",
                Target = "sql.corp.internal",
                Gateway = new { GatewayId = gatewayId, ResourceId = "orders-dw" }
            })
        };
        targetViolationReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var targetViolationResp = await _client.SendAsync(targetViolationReq);
        Assert.Equal(HttpStatusCode.BadRequest, targetViolationResp.StatusCode);

        // Reserved persistence keys cannot be supplied without a validated Gateway binding.
        using var reservedKeyReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/connections/{invalidAlias}")
        {
            Content = JsonContent.Create(new
            {
                ConnectorType = "MSSQL",
                Options = new Dictionary<string, string>
                {
                    ["__gateway_id"] = gatewayId,
                    ["__gateway_resource_id"] = "orders-dw"
                }
            })
        };
        reservedKeyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var reservedKeyResp = await _client.SendAsync(reservedKeyReq);
        Assert.Equal(HttpStatusCode.BadRequest, reservedKeyResp.StatusCode);

        // 10. Rejection on unapproved resource
        using var unapprovedReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/connections/{invalidAlias}")
        {
            Content = JsonContent.Create(new
            {
                ConnectorType = "POSTGRES",
                Gateway = new { GatewayId = gatewayId, ResourceId = "unapproved-db" }
            })
        };
        unapprovedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var unapprovedResp = await _client.SendAsync(unapprovedReq);
        Assert.Equal(HttpStatusCode.BadRequest, unapprovedResp.StatusCode);
        var unapprovedErr = await unapprovedResp.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Contains("not approved", unapprovedErr?["error"]?.ToString(), StringComparison.OrdinalIgnoreCase);

        // 11. Service resolution check
        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<PortalConnectionCatalogService>();
            var resolved = await catalog.ResolveDefinitionAsync(alias);
            Assert.Equal("MSSQL", resolved.ConnectorType);
            Assert.NotNull(resolved.Gateway);
            Assert.Equal(gatewayId, resolved.Gateway.GatewayId);
            Assert.Equal("orders-dw", resolved.Gateway.ResourceId);
            Assert.False(resolved.Options.ContainsKey("__gateway_id"));
            Assert.False(resolved.Options.ContainsKey("__gateway_resource_id"));
        }

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
