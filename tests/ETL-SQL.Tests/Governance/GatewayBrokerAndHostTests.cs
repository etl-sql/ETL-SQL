using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using ETL_SQL.Core.Governance;
using ETL_SQL.Gateway;
using ETL_SQL.Services;

namespace ETL_SQL.Tests.Governance;

public sealed class GatewayBrokerAndHostTests
{
    private const string TenantA = "tenant-alpha";
    private const string TenantB = "tenant-beta";
    private const string GatewayId = "gw-local-1";
    private const string Thumbprint = "thumb-test-123";

    [Fact]
    public async Task Broker_AuthenticatesValidConsumedEnrollmentAndRegistersSession()
    {
        var enrollmentStore = new InMemoryGatewayEnrollmentStore();
        await enrollmentStore.IssueAsync(TenantA, GatewayId, "token-32-chars-long-minimum-length-req", DateTimeOffset.UtcNow.AddHours(1));
        await enrollmentStore.ConsumeAsync(TenantA, "token-32-chars-long-minimum-length-req", Thumbprint);

        var registry = new GatewaySessionRegistry();
        var broker = new GatewayBroker(enrollmentStore, registry, allowUnprovenTestIdentities: true);

        await using var server = await LoopbackWebSocketServer.StartAsync(broker.HandleInboundConnectionAsync);

        using var clientWs = new ClientWebSocket();
        await clientWs.ConnectAsync(server.Uri, CancellationToken.None);

        var helloFrame = new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            TenantId = TenantA,
            GatewayId = GatewayId,
            WorkloadPublicKeyThumbprint = Thumbprint
        };
        await SendFrameAsync(clientWs, helloFrame);

        var ack = await ReceiveFrameAsync(clientWs);
        Assert.NotNull(ack);
        Assert.Equal(GatewayFrameKind.HelloAck, ack.Kind);

        Assert.True(registry.TryGet(TenantA, GatewayId, out var activeSession));
        Assert.NotNull(activeSession);
        Assert.Equal(TenantA, activeSession.TenantId);
        Assert.Equal(GatewayId, activeSession.GatewayId);

        // Cross-tenant lookup returns false
        Assert.False(registry.TryGet(TenantB, GatewayId, out _));

        await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test done", CancellationToken.None);
    }

    [Fact]
    public async Task Broker_MultiNodeCluster_AcceptsMultipleNodesAndLoadBalances()
    {
        var enrollmentStore = new InMemoryGatewayEnrollmentStore();
        await enrollmentStore.IssueAsync(TenantA, GatewayId, "token-32-chars-long-minimum-length-req", DateTimeOffset.UtcNow.AddHours(1));
        await enrollmentStore.ConsumeAsync(TenantA, "token-32-chars-long-minimum-length-req", Thumbprint);

        var registry = new GatewaySessionRegistry();
        var broker = new GatewayBroker(enrollmentStore, registry, allowUnprovenTestIdentities: true);

        await using var server = await LoopbackWebSocketServer.StartAsync(broker.HandleInboundConnectionAsync);

        // Node 1 connects
        using var client1 = new ClientWebSocket();
        await client1.ConnectAsync(server.Uri, CancellationToken.None);
        await SendFrameAsync(client1, new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            TenantId = TenantA,
            GatewayId = GatewayId,
            NodeId = "node-alpha",
            WorkloadPublicKeyThumbprint = Thumbprint
        });
        var ack1 = await ReceiveFrameAsync(client1);
        Assert.NotNull(ack1);
        Assert.Equal(GatewayFrameKind.HelloAck, ack1.Kind);

        // Node 2 connects with the same Gateway ID
        using var client2 = new ClientWebSocket();
        await client2.ConnectAsync(server.Uri, CancellationToken.None);
        await SendFrameAsync(client2, new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            TenantId = TenantA,
            GatewayId = GatewayId,
            NodeId = "node-beta",
            WorkloadPublicKeyThumbprint = Thumbprint
        });
        var ack2 = await ReceiveFrameAsync(client2);
        Assert.NotNull(ack2);
        Assert.Equal(GatewayFrameKind.HelloAck, ack2.Kind);

        // Check cluster registry
        var clusters = registry.ListActiveClusters(TenantA);
        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].ActiveNodes);
        Assert.Equal(2, clusters[0].TotalNodes);

        // Round-robin load balancing
        var pickedNodes = new HashSet<string>();
        for (int i = 0; i < 4; i++)
        {
            Assert.True(registry.TryGet(TenantA, GatewayId, out var session));
            Assert.NotNull(session);
            pickedNodes.Add(session.NodeId);
        }
        Assert.Contains("node-alpha", pickedNodes);
        Assert.Contains("node-beta", pickedNodes);

        await client1.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        await client2.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
    }

    [Fact]
    public async Task Broker_RefusesUnenrolledOrMismatchedThumbprint()
    {
        var enrollmentStore = new InMemoryGatewayEnrollmentStore();
        var registry = new GatewaySessionRegistry();
        var broker = new GatewayBroker(enrollmentStore, registry, allowUnprovenTestIdentities: true);

        await using var server = await LoopbackWebSocketServer.StartAsync(broker.HandleInboundConnectionAsync);

        using var clientWs = new ClientWebSocket();
        await clientWs.ConnectAsync(server.Uri, CancellationToken.None);

        var helloFrame = new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            TenantId = TenantA,
            GatewayId = GatewayId,
            WorkloadPublicKeyThumbprint = "wrong-thumbprint"
        };
        await SendFrameAsync(clientWs, helloFrame);

        var fault = await ReceiveFrameAsync(clientWs);
        Assert.NotNull(fault);
        Assert.Equal(GatewayFrameKind.Fault, fault.Kind);
        Assert.Contains("not valid or thumbprint does not match", fault.Reason);

        Assert.False(registry.TryGet(TenantA, GatewayId, out _));
    }

    [Fact]
    public async Task Broker_RefusesCrossTenantAmbiguousOutcomeBeforeRegisteringSession()
    {
        var enrollmentStore = new InMemoryGatewayEnrollmentStore();
        await enrollmentStore.IssueAsync(
            TenantA, GatewayId, "token-32-chars-long-minimum-length-req", DateTimeOffset.UtcNow.AddHours(1));
        await enrollmentStore.ConsumeAsync(
            TenantA, "token-32-chars-long-minimum-length-req", Thumbprint);
        var registry = new GatewaySessionRegistry();
        var sink = new CapturingAmbiguousOutcomeSink();
        var broker = new GatewayBroker(
            enrollmentStore, registry, allowUnprovenTestIdentities: true, ambiguousOutcomeSink: sink);
        await using var server = await LoopbackWebSocketServer.StartAsync(broker.HandleInboundConnectionAsync);
        using var client = new ClientWebSocket();
        await client.ConnectAsync(server.Uri, CancellationToken.None);

        await SendFrameAsync(client, new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            TenantId = TenantA,
            GatewayId = GatewayId,
            WorkloadPublicKeyThumbprint = Thumbprint,
            AmbiguousOutcomes =
            [
                new GatewayAmbiguousOutcomeNotice(
                    "op-cross-tenant", TenantB, GatewayId, "resource-a", "corr-a", DateTimeOffset.UtcNow)
            ]
        });

        var fault = await ReceiveFrameAsync(client);
        Assert.Equal(GatewayFrameKind.Fault, fault?.Kind);
        Assert.False(registry.TryGet(TenantA, GatewayId, out _));
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public async Task GatewayHost_TransitionsStatesAndConnectsSuccessfully()
    {
        var enrollmentStore = new InMemoryGatewayEnrollmentStore();
        await enrollmentStore.IssueAsync(TenantA, GatewayId, "token-32-chars-long-minimum-length-req", DateTimeOffset.UtcNow.AddHours(1));
        await enrollmentStore.ConsumeAsync(TenantA, "token-32-chars-long-minimum-length-req", Thumbprint);

        var registry = new GatewaySessionRegistry();
        var broker = new GatewayBroker(enrollmentStore, registry, allowUnprovenTestIdentities: true);

        await using var server = await LoopbackWebSocketServer.StartAsync(broker.HandleInboundConnectionAsync);

        var hostOptions = new GatewayHostOptions(
            new GatewaySessionOptions(server.Uri, TenantA, GatewayId, Thumbprint),
            InitialBackoff: TimeSpan.FromMilliseconds(50),
            MaxBackoff: TimeSpan.FromMilliseconds(200),
            MaxRetries: 2);

        var resourceRegistry = new GatewayResourceRegistry();
        var outcomeLedger = new GatewayOutcomeLedger();
        var mockExecutor = new MockResourceExecutor();
        var dispatcher = new GatewayOperationDispatcher(resourceRegistry, mockExecutor, outcomeLedger);

        var host = new GatewayHost(hostOptions, dispatcher);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var runTask = host.RunAsync(cts.Token);

        // Allow connection to establish
        await Task.Delay(100); // flaky-delay-ok: broker connection establishment
        Assert.Equal(GatewayHostStatus.Connected, host.Status);

        cts.Cancel();
        await runTask;
        Assert.Equal(GatewayHostStatus.Stopped, host.Status);
    }

    [Fact]
    public async Task Session_NeverHandsAnAbandonedOperationsLateRowsToTheNextOperation()
    {
        var enrollmentStore = new InMemoryGatewayEnrollmentStore();
        await enrollmentStore.IssueAsync(TenantA, GatewayId, "token-32-chars-long-minimum-length-req", DateTimeOffset.UtcNow.AddHours(1));
        await enrollmentStore.ConsumeAsync(TenantA, "token-32-chars-long-minimum-length-req", Thumbprint);
        var registry = new GatewaySessionRegistry();
        var broker = new GatewayBroker(enrollmentStore, registry, allowUnprovenTestIdentities: true);
        await using var server = await LoopbackWebSocketServer.StartAsync(broker.HandleInboundConnectionAsync);

        using var gateway = new ClientWebSocket();
        await gateway.ConnectAsync(server.Uri, CancellationToken.None);
        await SendFrameAsync(gateway, new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            TenantId = TenantA,
            GatewayId = GatewayId,
            WorkloadPublicKeyThumbprint = Thumbprint
        });
        Assert.Equal(GatewayFrameKind.HelloAck, (await ReceiveFrameAsync(gateway))!.Kind);
        Assert.True(registry.TryGet(TenantA, GatewayId, out var session));

        static GatewayOperation Operation(string id) => new(
            id, TenantA, GatewayId, "orders", GatewayOperationClass.Read, GatewayOperationEffect.ReadOnly,
            GatewayOperationBounds.Default, id);

        // The first caller gives up before its rows arrive (a timeout, a closed browser tab).
        using (var abandoned = new CancellationTokenSource())
        {
            var first = session!.ExecuteAsync(Operation("op-first"), "SELECT 1", null, abandoned.Token);
            Assert.Equal("op-first", (await ReceiveFrameAsync(gateway))!.OperationId);
            abandoned.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        }
        await SendFrameAsync(gateway, new GatewayFrame
        {
            Kind = GatewayFrameKind.RowBatch,
            OperationId = "op-first",
            Columns = ["who"],
            Rows = [["first viewer's row"]]
        });
        await SendFrameAsync(gateway, new GatewayFrame { Kind = GatewayFrameKind.Complete, OperationId = "op-first", RowsProduced = 1 });

        // The next caller gets its own rows, not the leftovers.
        var second = session!.ExecuteAsync(Operation("op-second"), "SELECT 2", null, CancellationToken.None);
        Assert.Equal("op-second", (await ReceiveFrameAsync(gateway))!.OperationId);
        await SendFrameAsync(gateway, new GatewayFrame
        {
            Kind = GatewayFrameKind.RowBatch,
            OperationId = "op-second",
            Columns = ["who"],
            Rows = [["second viewer's row"]]
        });
        await SendFrameAsync(gateway, new GatewayFrame { Kind = GatewayFrameKind.Complete, OperationId = "op-second", RowsProduced = 1 });

        var result = await second;
        Assert.Equal("second viewer's row", Assert.Single(result.Rows)[0]);
        await gateway.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test done", CancellationToken.None);
    }

    [Fact]
    public async Task GatewayHost_BackoffDoublesAcrossFailedConnections()
    {
        var time = new RecordingDelayTimeProvider();
        var options = new GatewayHostOptions(
            new GatewaySessionOptions(new Uri("ws://127.0.0.1:1/"), TenantA, GatewayId, Thumbprint),
            InitialBackoff: TimeSpan.FromMilliseconds(10),
            MaxBackoff: TimeSpan.FromSeconds(30),
            MaxRetries: 4);
        var dispatcher = new GatewayOperationDispatcher(new GatewayResourceRegistry(), new MockResourceExecutor(), new GatewayOutcomeLedger());

        await new GatewayHost(options, dispatcher, time).RunAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);

        Assert.Equal(
            new[] { 10, 20, 40, 80 },
            time.Delays.Select(delay => (int)delay.TotalMilliseconds).ToArray());
    }

    /// <summary>Records each requested delay and lets it elapse at once.</summary>
    private sealed class RecordingDelayTimeProvider : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (Delays) Delays.Add(dueTime);
            return System.CreateTimer(callback, state, TimeSpan.Zero, period);
        }
    }

    [Fact]
    public void CapabilityAuthorizedDestinationScope_RestrictsOutboundDestinations()
    {
        using (CapabilityAuthorizedDestinationScope.Enter(["api.example.com", "db.internal.corp"]))
        {
            // Allowed
            CapabilityAuthorizedDestinationScope.Enforce("api.example.com");
            CapabilityAuthorizedDestinationScope.Enforce("db.internal.corp");
            CapabilityAuthorizedDestinationScope.Enforce(null, "https://api.example.com/v1/data");

            // Denied
            Assert.Throws<SecurityException>(() =>
                CapabilityAuthorizedDestinationScope.Enforce("other.unauthorized.com"));

            Assert.Throws<SecurityException>(() =>
                CapabilityAuthorizedDestinationScope.Enforce(null, "https://evil.target.com/feed"));
        }

        // Outside scope: no exception thrown by Enforce
        CapabilityAuthorizedDestinationScope.Enforce("other.unauthorized.com");
    }

    private static async Task SendFrameAsync(WebSocket ws, GatewayFrame frame)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(frame.Serialize());
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<GatewayFrame?> ReceiveFrameAsync(WebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var res = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (res.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, res.Count);
            if (res.EndOfMessage) break;
        }
        return GatewayFrame.Deserialize(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }

    private sealed class CapturingAmbiguousOutcomeSink : IGatewayAmbiguousOutcomeSink
    {
        public List<GatewayAmbiguousOutcomeNotice> Notices { get; } = [];

        public Task RecordAsync(GatewayAmbiguousOutcomeNotice notice, CancellationToken cancellationToken)
        {
            Notices.Add(notice);
            return Task.CompletedTask;
        }
    }

    private sealed class MockResourceExecutor : IGatewayResourceExecutor
    {
        public Task<GatewayExecutionResult> ExecuteAsync(
            GatewayResource resource,
            GatewayOperationClass operationClass,
            string? request,
            IReadOnlyList<string>? parameters,
            GatewayOperationBounds bounds,
            CancellationToken cancellationToken) =>
            Task.FromResult(new GatewayExecutionResult(["Col1"], [["Val1"]]));
    }

    private sealed class LoopbackWebSocketServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly Func<WebSocket, CancellationToken, Task> _handler;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _listenTask;

        public Uri Uri { get; }

        private LoopbackWebSocketServer(HttpListener listener, Uri uri, Func<WebSocket, CancellationToken, Task> handler)
        {
            _listener = listener;
            Uri = uri;
            _handler = handler;
            _listenTask = Task.Run(ListenLoop);
        }

        public static async Task<LoopbackWebSocketServer> StartAsync(Func<WebSocket, CancellationToken, Task> handler)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var listener = new HttpListener();
                var port = RandomNumberGenerator.GetInt32(30000, 60000);
                var prefix = $"http://127.0.0.1:{port}/";
                try
                {
                    listener.Prefixes.Add(prefix);
                    listener.Start();

                    var wsUri = new Uri($"ws://127.0.0.1:{port}/");
                    return new LoopbackWebSocketServer(listener, wsUri, handler);
                }
                catch (HttpListenerException)
                {
                    try { listener.Close(); } catch { }
                }
                catch (Exception)
                {
                    try { listener.Close(); } catch { }
                    throw;
                }
            }

            throw new InvalidOperationException("Could not bind a loopback WebSocket server port after multiple attempts.");
        }

        private async Task ListenLoop()
        {
            while (!_cts.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    if (ctx.Request.IsWebSocketRequest)
                    {
                        var wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
                        _ = Task.Run(() => _handler(wsContext.WebSocket, _cts.Token));
                    }
                    else
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                    }
                }
                catch when (_cts.IsCancellationRequested || !_listener.IsListening)
                {
                    break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            try { await _listenTask; } catch { }
            _cts.Dispose();
        }
    }
}
