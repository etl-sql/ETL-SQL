using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
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
                    "orders", "MOCKDB", GatewayOperationClass.Read | GatewayOperationClass.Write,
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
        var catalogAlias = "gateway_route_" + Guid.NewGuid().ToString("N")[..8];
        var binding = new GatewayResourceBinding(gatewayId, "orders") { CatalogAlias = catalogAlias };
        var grantGroupId = 0;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var connection = new PortalSharedConnection
            {
                TenantId = "portal-host",
                Alias = catalogAlias,
                ConnectorType = "MOCKDB",
                OptionsJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["__gateway_id"] = gatewayId,
                    ["__gateway_resource_id"] = "orders"
                })
            };
            db.PortalSharedConnections.Add(connection);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => router.ExecuteAsync(
                identity, binding, GatewayOperationClass.Read, GatewayOperationEffect.ReadOnly,
                GatewayOperationBounds.Default, JsonSerializer.Serialize(new { Table = "Orders" }),
                null, CancellationToken.None));

            var group = new Group { TenantId = "portal-host", Name = "gateway-route-grant-" + catalogAlias };
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            grantGroupId = group.Id;
            db.SharedConnectionAcls.Add(new SharedConnectionAcl
            {
                TenantId = "portal-host",
                SharedConnectionId = connection.Id,
                GroupId = group.Id
            });
            await db.SaveChangesAsync();
        }
        var routedTask = router.ExecuteAsync(
            identity, binding,
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

        var ambiguousTask = router.ExecuteAsync(
            identity, binding,
            GatewayOperationClass.Write, GatewayOperationEffect.Mutating,
            GatewayOperationBounds.Default,
            JsonSerializer.Serialize(new { Table = "Orders", Columns = new[] { "id" }, Rows = new[] { new[] { "43" } } }),
            null, CancellationToken.None);
        received = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var ambiguousOperation = GatewayFrame.Deserialize(Encoding.UTF8.GetString(buffer, 0, received.Count));
        Assert.Equal(GatewayFrameKind.Operation, ambiguousOperation.Kind);
        payload = Encoding.UTF8.GetBytes(new GatewayFrame
        {
            Kind = GatewayFrameKind.Fault,
            OperationId = ambiguousOperation.OperationId,
            OutcomeState = GatewayOutcomeState.Ambiguous,
            Reason = "The operation failed on the Gateway."
        }.Serialize());
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        var ambiguousError = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.AmbiguousGatewayWriteException>(
            () => ambiguousTask);
        Assert.Contains(ambiguousOperation.OperationId!, ambiguousError.Message);

        using var caseListRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/admin/gateway-operations/ambiguous-writes");
        caseListRequest.Headers.Authorization = new("Bearer", adminToken);
        using var caseListResponse = await _client.SendAsync(caseListRequest);
        caseListResponse.EnsureSuccessStatusCode();
        var cases = await caseListResponse.Content.ReadFromJsonAsync<List<GatewayAmbiguousWriteCaseDto>>(_json);
        var triageCase = Assert.Single(cases!, item => item.OperationId == ambiguousOperation.OperationId);
        Assert.Equal("High", triageCase.Priority);
        Assert.Equal(gatewayId, triageCase.GatewayId);
        Assert.Equal("orders", triageCase.ResourceId);
        Assert.Equal(ambiguousOperation.CorrelationId, triageCase.CorrelationId);
        Assert.Equal("Detected", Assert.Single(triageCase.Events).EventType);
        var recorder = _factory.Services.GetRequiredService<IGatewayAmbiguousWriteRecorder>();
        await recorder.RecordAsync(new GatewayOperation(
            ambiguousOperation.OperationId!, "default", gatewayId, "orders",
            GatewayOperationClass.Write, GatewayOperationEffect.Mutating,
            GatewayOperationBounds.Default, ambiguousOperation.CorrelationId!), CancellationToken.None);
        using var deduplicatedRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/admin/gateway-operations/ambiguous-writes");
        deduplicatedRequest.Headers.Authorization = new("Bearer", adminToken);
        using var deduplicatedResponse = await _client.SendAsync(deduplicatedRequest);
        var deduplicatedCases = await deduplicatedResponse.Content
            .ReadFromJsonAsync<List<GatewayAmbiguousWriteCaseDto>>(_json);
        Assert.Single(deduplicatedCases!, item => item.OperationId == ambiguousOperation.OperationId);

        triageCase = await MutateCaseAsync($"{triageCase.Id}/acknowledge", new
        {
            triageCase.Version,
            Note = "Investigating against the destination audit log."
        });
        triageCase = await MutateCaseAsync($"{triageCase.Id}/assign", new
        {
            triageCase.Version,
            Owner = "database-operations",
            Note = "Assigned for reconciliation."
        });
        triageCase = await MutateCaseAsync($"{triageCase.Id}/evidence", new
        {
            triageCase.Version,
            Note = "Destination transaction ID captured.",
            EvidenceReference = "INC-4242"
        });

        using (var invalidResolution = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/gateway-operations/ambiguous-writes/{triageCase.Id}/resolve")
        {
            Content = JsonContent.Create(new
            {
                triageCase.Version,
                Resolution = "confirmed committed",
                Note = (string?)null,
                EvidenceReference = (string?)null
            })
        })
        {
            invalidResolution.Headers.Authorization = new("Bearer", adminToken);
            using var invalidResolutionResponse = await _client.SendAsync(invalidResolution);
            Assert.Equal(HttpStatusCode.BadRequest, invalidResolutionResponse.StatusCode);
        }

        triageCase = await MutateCaseAsync($"{triageCase.Id}/resolve", new
        {
            triageCase.Version,
            Resolution = "confirmed committed",
            Note = "Transaction 9127 is committed in the destination audit ledger.",
            EvidenceReference = "INC-4242#transaction-9127"
        });
        Assert.Equal("Resolved", triageCase.State);
        Assert.Equal("confirmed committed", triageCase.Resolution);
        Assert.Equal(["Detected", "Acknowledged", "Assigned", "EvidenceAdded", "Resolved"],
            triageCase.Events.Select(item => item.EventType));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var acl = db.SharedConnectionAcls.Single(item => item.GroupId == grantGroupId);
            var connectionId = acl.SharedConnectionId;
            db.SharedConnectionAcls.Remove(acl);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => router.ExecuteAsync(
                identity, binding, GatewayOperationClass.Read, GatewayOperationEffect.ReadOnly,
                GatewayOperationBounds.Default, JsonSerializer.Serialize(new { Table = "Orders" }),
                null, CancellationToken.None));

            db.SharedConnectionAcls.Add(new SharedConnectionAcl
            {
                TenantId = "portal-host",
                SharedConnectionId = connectionId,
                GroupId = grantGroupId
            });
            await db.SaveChangesAsync();
        }

        using var revoke = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/gateways/{gatewayId}/revoke");
        revoke.Headers.Authorization = new("Bearer", adminToken);
        (await _client.SendAsync(revoke)).EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => router.ExecuteAsync(
            identity, binding,
            GatewayOperationClass.Read, GatewayOperationEffect.ReadOnly,
            GatewayOperationBounds.Default, JsonSerializer.Serialize(new { Table = "Orders" }),
            null, CancellationToken.None));

        socket.Abort();

        async Task<GatewayAmbiguousWriteCaseDto> MutateCaseAsync(string path, object body)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"/api/admin/gateway-operations/ambiguous-writes/{path}")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Authorization = new("Bearer", adminToken);
            using var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<GatewayAmbiguousWriteCaseDto>(_json))!;
        }
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
