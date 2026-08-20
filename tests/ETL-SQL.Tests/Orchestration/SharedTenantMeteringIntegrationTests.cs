using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Storage;
using ETL_SQL.Gateway;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class SharedTenantMeteringIntegrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"etlsql-metering-integ-{Guid.NewGuid():N}.db");
    private readonly string _artifactDir = Path.Combine(
        Path.GetTempPath(), $"etlsql-metering-art-{Guid.NewGuid():N}");

    private ITenantMeteringLedger Ledger()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Orchestrator:Database:Provider"] = "Sqlite" }).Build();
        return new OrchestratorStoreFactory(configuration).CreateTenantMeteringLedger(_dbPath);
    }

    [Fact]
    public async Task TenantScopedArtifactStorage_AppendsStorageMeteringEvents()
    {
        var ledger = Ledger();
        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
        Directory.CreateDirectory(_artifactDir);
        var baseStorage = new FileSystemArtifactStorage(new Dictionary<ArtifactArea, string>
        {
            [ArtifactArea.Scripts] = _artifactDir
        });
        var factory = new TenantArtifactStorageFactory(baseStorage, requireExclusiveBackend: false, meteringLedger: ledger);

        var tenantStorage = factory.ForTenant(alpha);
        await tenantStorage.WriteAllTextAsync(ArtifactArea.Scripts, "test.etlsql", "SELECT 12345;");
        var readContent = await tenantStorage.ReadAllTextAsync(ArtifactArea.Scripts, "test.etlsql");

        Assert.Equal("SELECT 12345;", readContent);

        var records = await ledger.ListAsync(alpha);
        Assert.NotEmpty(records);
        Assert.Contains(records, r => r.Event.Source == TenantMeteringSource.Storage && r.Event.BytesWritten > 0);
        Assert.Contains(records, r => r.Event.Source == TenantMeteringSource.Storage && r.Event.BytesRead > 0);
    }

    [Fact]
    public async Task GatewayBrokerExecution_AppendsTenantAttributedMeteringEvent()
    {
        var ledger = Ledger();
        var registry = new GatewaySessionRegistry();
        var store = new InMemoryGatewayEnrollmentStore();
        var broker = new GatewayBroker(store, registry, meteringLedger: ledger, allowUnprovenTestIdentities: true);

        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");

        // Enroll gateway
        await store.IssueAsync("tenant-alpha", "gw-1", "token-32-chars-long-minimum-length-req", DateTimeOffset.UtcNow.AddMinutes(10));
        await store.ConsumeAsync("tenant-alpha", "token-32-chars-long-minimum-length-req", "thumb-123");

        // Stand up loopback server
        await using var server = await LoopbackWebSocketServer.StartAsync(broker.HandleInboundConnectionAsync);

        using var clientWs = new ClientWebSocket();
        await clientWs.ConnectAsync(server.Uri, CancellationToken.None);

        // Client sends Hello
        var hello = new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            TenantId = "tenant-alpha",
            GatewayId = "gw-1",
            WorkloadPublicKeyThumbprint = "thumb-123"
        };
        await SendFrameAsync(clientWs, hello);

        var ackFrame = await ReceiveFrameAsync(clientWs);
        Assert.NotNull(ackFrame);
        Assert.Equal(GatewayFrameKind.HelloAck, ackFrame.Kind);

        // Session is active in registry
        Assert.True(registry.TryGet("tenant-alpha", "gw-1", out var session));
        Assert.NotNull(session);

        // Execute operation in background, client responds with rows and complete
        var op = new GatewayOperation(
            "op-101",
            "tenant-alpha",
            "gw-1",
            "db-1",
            GatewayOperationClass.Read,
            GatewayOperationEffect.ReadOnly,
            GatewayOperationBounds.Default,
            "corr-1");

        var execTask = session.ExecuteAsync(op, "SELECT 1;", null, CancellationToken.None);

        // Client reads op frame
        var receivedOp = await ReceiveFrameAsync(clientWs);
        Assert.NotNull(receivedOp);
        Assert.Equal(GatewayFrameKind.Operation, receivedOp.Kind);

        // Client sends RowBatch and Complete
        var rowBatch = new GatewayFrame
        {
            Kind = GatewayFrameKind.RowBatch,
            OperationId = "op-101",
            Columns = ["col1"],
            Rows = [["val1"], ["val2"]]
        };
        await SendFrameAsync(clientWs, rowBatch);

        var complete = new GatewayFrame
        {
            Kind = GatewayFrameKind.Complete,
            OperationId = "op-101",
            RowsProduced = 2
        };
        await SendFrameAsync(clientWs, complete);

        var result = await execTask;
        Assert.Equal(2, result.Rows.Count);

        // Check metering ledger
        var records = await ledger.ListAsync(alpha);
        var gwRecord = Assert.Single(records, r => r.Event.Source == TenantMeteringSource.Gateway);
        Assert.Equal(2, gwRecord.Event.Rows);
        Assert.Equal(TenantWorkloadClass.Gateway, gwRecord.Event.WorkloadClass);
        Assert.Equal(TenantConnectorClass.Gateway, gwRecord.Event.ConnectorClass);
        Assert.Equal(TenantMeteringStatus.Succeeded, gwRecord.Event.Status);
        Assert.True(gwRecord.Event.GatewayIngressBytes > 0);
        Assert.True(gwRecord.Event.GatewayEgressBytes > 0);

        // Clean close
        await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
    }

    private static async Task SendFrameAsync(WebSocket socket, GatewayFrame frame)
    {
        var bytes = Encoding.UTF8.GetBytes(frame.Serialize());
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    private static async Task<GatewayFrame?> ReceiveFrameAsync(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var res = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (res.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, res.Count);
            if (res.EndOfMessage) break;
        }
        return GatewayFrame.Deserialize(Encoding.UTF8.GetString(ms.ToArray()));
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
            try { await _listenTask.WaitAsync(TimeSpan.FromMilliseconds(500)); } catch { }
            _cts.Dispose();
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_artifactDir)) Directory.Delete(_artifactDir, recursive: true);
    }
}
