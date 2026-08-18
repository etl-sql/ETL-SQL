using System.Net;
using System.Net.WebSockets;
using System.Text;
using ETL_SQL.Core.Governance;
using ETL_SQL.Gateway;

namespace ETL_SQL.Tests.Governance;

/// <summary>
/// Slice D4's transport and the D8 runtime boundary, exercised over a real loopback socket.
///
/// <para>These tests stand up an actual broker endpoint on loopback and run the real
/// <see cref="GatewaySessionClient"/> against it — no mocked transport. The plan for this cell says
/// mocked evidence cannot support a connectivity claim, and the repository has been bitten five
/// times by controls that existed, looked implemented, and were never asserted end to end.</para>
/// </summary>
[Trait("Category", "GatewayTransport")]
public sealed class GatewayTransportTests
{
    private const string Tenant = "tenant-acme";
    private const string GatewayId = "hq-gateway";
    private const string ResourceId = "corp-sql-sales";
    private const string Thumbprint = "thumb-abc";

    [Fact]
    public async Task Session_DialsOutPresentsIdentityAndServesATypedOperation()
    {
        await using var broker = await LoopbackBroker.StartAsync();
        var registry = await ApprovedRegistryAsync();
        var ledger = new GatewayOutcomeLedger();

        broker.OnHello = hello =>
        {
            // The broker sees the Gateway's identity, and nothing that could be dialled.
            Assert.Equal(Tenant, hello.TenantId);
            Assert.Equal(GatewayId, hello.GatewayId);
            Assert.Equal(Thumbprint, hello.WorkloadPublicKeyThumbprint);
            return true;
        };
        broker.Operations.Add(new GatewayFrame
        {
            Kind = GatewayFrameKind.Operation,
            OperationId = "op-1",
            ResourceId = ResourceId,
            OperationClass = GatewayOperationClass.Read,
            Effect = GatewayOperationEffect.ReadOnly,
            Bounds = GatewayOperationBounds.Default,
            Request = "SELECT 1"
        });

        var client = new GatewaySessionClient(Options(broker.Uri), Dispatcher(registry, ledger));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.RunAsync(cts.Token);

        var batch = broker.Received.Single(frame => frame.Kind == GatewayFrameKind.RowBatch);
        Assert.Equal(["Value"], batch.Columns);
        Assert.Equal(2, batch.Rows!.Count);

        var complete = broker.Received.Single(frame => frame.Kind == GatewayFrameKind.Complete);
        Assert.Equal(GatewayOutcomeState.Committed, complete.OutcomeState);
        Assert.Equal(GatewayOutcomeState.Committed, ledger.Find(Tenant, "op-1")!.State);
    }

    [Fact]
    public async Task Session_RefusesAnUnapprovedResourceWithoutNamingTheTarget()
    {
        await using var broker = await LoopbackBroker.StartAsync();
        // Proposed but never approved: the Gateway is the one that says no.
        var registry = new GatewayResourceRegistry();
        await registry.ProposeAsync(SampleResource());

        broker.Operations.Add(ReadOperation("op-1", ResourceId));

        var client = new GatewaySessionClient(Options(broker.Uri), Dispatcher(registry, new GatewayOutcomeLedger()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.RunAsync(cts.Token);

        var fault = broker.Received.Single(frame => frame.Kind == GatewayFrameKind.Fault);
        Assert.Contains("not available for execution", fault.Reason!, StringComparison.OrdinalIgnoreCase);
        AssertNoLocalDetail(broker.Received);
    }

    [Fact]
    public async Task Session_RefusesAnOperationClassTheResourceDoesNotPermit()
    {
        await using var broker = await LoopbackBroker.StartAsync();
        var registry = new GatewayResourceRegistry();
        await registry.ProposeAsync(SampleResource() with { AllowedOperations = GatewayOperationClass.Read });
        await registry.ApproveAsync(ResourceId);

        broker.Operations.Add(ReadOperation("op-1", ResourceId) with
        {
            OperationClass = GatewayOperationClass.Write,
            Effect = GatewayOperationEffect.Mutating
        });

        var client = new GatewaySessionClient(Options(broker.Uri), Dispatcher(registry, new GatewayOutcomeLedger()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.RunAsync(cts.Token);

        Assert.Contains(broker.Received, frame => frame.Kind == GatewayFrameKind.Fault);
        AssertNoLocalDetail(broker.Received);
    }

    [Fact]
    public async Task Session_RefusesAnOperationClaimingAnotherTenantOrGateway()
    {
        await using var broker = await LoopbackBroker.StartAsync();
        var registry = await ApprovedRegistryAsync();

        broker.Operations.Add(ReadOperation("op-1", ResourceId) with { TenantId = "tenant-globex" });
        broker.Operations.Add(ReadOperation("op-2", ResourceId) with { GatewayId = "other-gateway" });

        var client = new GatewaySessionClient(Options(broker.Uri), Dispatcher(registry, new GatewayOutcomeLedger()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.RunAsync(cts.Token);

        var faults = broker.Received.Where(frame => frame.Kind == GatewayFrameKind.Fault).ToList();
        Assert.Equal(2, faults.Count);
        Assert.Contains(faults, f => f.Reason!.Contains("different tenant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(faults, f => f.Reason!.Contains("different Gateway", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Session_NarrowsBoundsToTheResourceAndTruncatesAtTheRowLimit()
    {
        await using var broker = await LoopbackBroker.StartAsync();
        var registry = new GatewayResourceRegistry();
        // The resource permits one row; the cloud asks for a million.
        await registry.ProposeAsync(SampleResource() with
        {
            Limits = new GatewayResourceLimits(MaxConcurrency: 1, MaxRows: 1, MaxBytes: 4096, TimeoutSeconds: 30)
        });
        await registry.ApproveAsync(ResourceId);

        broker.Operations.Add(ReadOperation("op-1", ResourceId) with
        {
            Bounds = GatewayOperationBounds.Default with { MaxRows = 1_000_000 }
        });

        var client = new GatewaySessionClient(Options(broker.Uri), Dispatcher(registry, new GatewayOutcomeLedger()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.RunAsync(cts.Token);

        var batch = broker.Received.Single(frame => frame.Kind == GatewayFrameKind.RowBatch);
        Assert.Single(batch.Rows!);
        var complete = broker.Received.Single(frame => frame.Kind == GatewayFrameKind.Complete);
        Assert.Equal(1, complete.RowsProduced);
    }

    [Fact]
    public async Task Session_ReplaysACommittedOutcomeRatherThanRunningTheWriteAgain()
    {
        await using var broker = await LoopbackBroker.StartAsync();
        var registry = await ApprovedRegistryAsync();
        var ledger = new GatewayOutcomeLedger();
        var executor = new CountingExecutor();

        // The same operation ID arrives twice, as it would after a reconnect.
        broker.Operations.Add(ReadOperation("op-1", ResourceId) with { Effect = GatewayOperationEffect.Mutating, OperationClass = GatewayOperationClass.Write });
        broker.Operations.Add(ReadOperation("op-1", ResourceId) with { Effect = GatewayOperationEffect.Mutating, OperationClass = GatewayOperationClass.Write });

        var client = new GatewaySessionClient(
            Options(broker.Uri), new GatewayOperationDispatcher(registry, executor, ledger));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.RunAsync(cts.Token);

        // Executed once; the second arrival replayed the recorded outcome.
        Assert.Equal(1, executor.Executions);
        var completes = broker.Received.Where(frame => frame.Kind == GatewayFrameKind.Complete).ToList();
        Assert.Equal(2, completes.Count);
        Assert.Contains(completes, frame =>
            frame.Reason?.Contains("not executed again", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Session_ReportsAFailedLocalExecutionWithoutLeakingItsMessage()
    {
        await using var broker = await LoopbackBroker.StartAsync();
        var registry = await ApprovedRegistryAsync();
        broker.Operations.Add(ReadOperation("op-1", ResourceId));

        var throwing = new ThrowingExecutor(
            "Login failed for user 'sa' on myserver:1433 using password 'hunter2'");
        var client = new GatewaySessionClient(
            Options(broker.Uri), new GatewayOperationDispatcher(registry, throwing, new GatewayOutcomeLedger()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.RunAsync(cts.Token);

        var fault = broker.Received.Single(frame => frame.Kind == GatewayFrameKind.Fault);
        Assert.Equal("The operation failed on the Gateway.", fault.Reason);
        // The provider's message named a host, a user, and a password. None of it crossed the wire.
        AssertNoLocalDetail(broker.Received);
        Assert.DoesNotContain("hunter2", string.Concat(broker.Received.Select(f => f.Serialize())),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_RefusesABrokerThatWillNotAcknowledge()
    {
        await using var broker = await LoopbackBroker.StartAsync();
        broker.OnHello = _ => false;

        var client = new GatewaySessionClient(
            Options(broker.Uri), Dispatcher(await ApprovedRegistryAsync(), new GatewayOutcomeLedger()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<GatewayProtocolException>(() => client.RunAsync(cts.Token));
    }

    [Fact]
    public void Session_RefusesANonTlsBrokerOffLoopbackAndAnyNonWebSocketScheme()
    {
        // Outbound-only and encrypted: a misconfiguration cannot silently downgrade the session, and
        // the client has no code path for an arbitrary scheme.
        Assert.Throws<GatewayProtocolException>(() =>
            Options(new Uri("ws://gateway.example.com/session")).Validate());
        Assert.Throws<GatewayProtocolException>(() =>
            Options(new Uri("https://gateway.example.com/session")).Validate());
        Assert.Throws<GatewayProtocolException>(() =>
            Options(new Uri("file:///etc/passwd")).Validate());

        Options(new Uri("wss://gateway.example.com/session")).Validate();
        Options(new Uri("ws://127.0.0.1:5000/session")).Validate();
    }

    [Fact]
    public void Frame_HasNoFieldThatCouldNameADestination()
    {
        // The protocol cannot express "connect to this host" — a structural property, not a check.
        var forbidden = new[] { "host", "port", "scheme", "url", "uri", "address", "endpoint", "path", "command", "connectionstring" };
        var properties = typeof(GatewayFrame).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToArray();

        Assert.DoesNotContain(properties, name => forbidden.Contains(name));
    }

    // ------------------------------------------------------------------------------- helpers

    private static void AssertNoLocalDetail(IEnumerable<GatewayFrame> frames)
    {
        var wire = string.Concat(frames.Select(frame => frame.Serialize()));
        foreach (var secret in new[] { "myserver", "1433", "sales-etl-credential" })
            Assert.DoesNotContain(secret, wire, StringComparison.OrdinalIgnoreCase);
    }

    private static GatewaySessionOptions Options(Uri uri) => new(uri, Tenant, GatewayId, Thumbprint);

    private static GatewayOperationDispatcher Dispatcher(GatewayResourceRegistry registry, GatewayOutcomeLedger ledger) =>
        new(registry, new CountingExecutor(), ledger);

    private static GatewayFrame ReadOperation(string operationId, string resourceId) => new()
    {
        Kind = GatewayFrameKind.Operation,
        OperationId = operationId,
        ResourceId = resourceId,
        OperationClass = GatewayOperationClass.Read,
        Effect = GatewayOperationEffect.ReadOnly,
        Bounds = GatewayOperationBounds.Default,
        Request = "SELECT 1"
    };

    private static async Task<GatewayResourceRegistry> ApprovedRegistryAsync()
    {
        var registry = new GatewayResourceRegistry();
        await registry.ProposeAsync(SampleResource());
        await registry.ApproveAsync(ResourceId);
        return registry;
    }

    private static GatewayResource SampleResource() => new(
        ResourceId, "SQLSERVER", "sqlserver://myserver:1433/Sales", "SECRET:sales-etl-credential",
        GatewayOperationClass.Read | GatewayOperationClass.Write, new GatewayResourceLimits());

    private sealed class CountingExecutor : IGatewayResourceExecutor
    {
        public int Executions { get; private set; }

        public Task<GatewayExecutionResult> ExecuteAsync(
            GatewayResource resource, string? request, IReadOnlyList<string>? parameters,
            GatewayOperationBounds bounds, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(new GatewayExecutionResult(
                ["Value"], [["1"], ["2"]]));
        }
    }

    private sealed class ThrowingExecutor(string message) : IGatewayResourceExecutor
    {
        public Task<GatewayExecutionResult> ExecuteAsync(
            GatewayResource resource, string? request, IReadOnlyList<string>? parameters,
            GatewayOperationBounds bounds, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }

    /// <summary>
    /// A real broker endpoint on loopback. Uses <see cref="HttpListener"/> so the test exercises an
    /// actual WebSocket over an actual socket rather than an in-memory duplex stream.
    /// </summary>
    private sealed class LoopbackBroker : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public Uri Uri { get; private set; } = null!;
        public List<GatewayFrame> Operations { get; } = [];
        public List<GatewayFrame> Received { get; } = [];
        public Func<GatewayFrame, bool> OnHello { get; set; } = _ => true;

        public static async Task<LoopbackBroker> StartAsync()
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var port = Random.Shared.Next(30000, 60000);
                var broker = new LoopbackBroker();
                try
                {
                    broker._listener.Prefixes.Add($"http://127.0.0.1:{port}/session/");
                    broker._listener.Start();
                    broker.Uri = new Uri($"ws://127.0.0.1:{port}/session/");
                    broker._loop = Task.Run(() => broker.ServeAsync(broker._cts.Token));
                    return broker;
                }
                catch (HttpListenerException)
                {
                    await broker.DisposeAsync();
                }
            }

            throw new InvalidOperationException("Could not bind a loopback broker port.");
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) { return; }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            var socketContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
            var socket = socketContext.WebSocket;

            var hello = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
            if (hello is null) return;

            if (!OnHello(hello))
            {
                await SendAsync(socket, GatewayFrame.Fault(null, "Session refused."), cancellationToken).ConfigureAwait(false);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "refused", cancellationToken).ConfigureAwait(false);
                return;
            }

            await SendAsync(socket, new GatewayFrame { Kind = GatewayFrameKind.HelloAck }, cancellationToken)
                .ConfigureAwait(false);

            foreach (var operation in Operations)
            {
                await SendAsync(socket, operation, cancellationToken).ConfigureAwait(false);

                // Drain this operation's responses before sending the next one.
                while (true)
                {
                    var response = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
                    if (response is null) return;
                    Received.Add(response);
                    if (response.Kind is GatewayFrameKind.Complete or GatewayFrameKind.Fault) break;
                }
            }

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken).ConfigureAwait(false);
        }

        private static async Task SendAsync(WebSocket socket, GatewayFrame frame, CancellationToken cancellationToken) =>
            await socket.SendAsync(Encoding.UTF8.GetBytes(frame.Serialize()),
                WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);

        private static async Task<GatewayFrame?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) { return null; }

                if (result.MessageType == WebSocketMessageType.Close) return null;
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) break;
            }

            return message.Length == 0 ? null : GatewayFrame.Deserialize(Encoding.UTF8.GetString(message.ToArray()));
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try { _listener.Stop(); } catch (Exception) { /* already stopped */ }
            try { _listener.Close(); } catch (Exception) { /* already closed */ }
            if (_loop is not null)
            {
                try { await _loop.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { /* best effort */ }
            }
            _cts.Dispose();
        }
    }
}
