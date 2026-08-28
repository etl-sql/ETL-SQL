using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Gateway;

/// <summary>
/// Server-side broker that authenticates incoming on-premises Gateway WebSocket sessions,
/// registers them in the <see cref="GatewaySessionRegistry"/>, and routes typed operations.
/// </summary>
public sealed class GatewayBroker(
    IGatewayEnrollmentStore enrollmentStore,
    GatewaySessionRegistry sessionRegistry,
    ITenantMeteringLedger? meteringLedger = null,
    int maxFrameBytes = 1 << 20,
    TimeProvider? timeProvider = null,
    bool allowUnprovenTestIdentities = false)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ITenantMeteringLedger? _meteringLedger = meteringLedger;

    /// <summary>
    /// Accepts and authenticates an incoming WebSocket connection from an on-premises Gateway.
    /// Runs until the session closes or is cancelled.
    /// </summary>
    public async Task HandleInboundConnectionAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);

        // 1. Receive Hello frame
        var helloFrame = await ReceiveFrameAsync(socket, cancellationToken).ConfigureAwait(false);
        if (helloFrame is null || helloFrame.Kind != GatewayFrameKind.Hello)
        {
            await CloseSocketAsync(socket, "Expected Hello frame.", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(helloFrame.TenantId) ||
            string.IsNullOrWhiteSpace(helloFrame.GatewayId) ||
            string.IsNullOrWhiteSpace(helloFrame.WorkloadPublicKeyThumbprint))
        {
            await SendFrameAsync(socket, GatewayFrame.Fault(null, "Hello frame missing required identity fields."), cancellationToken).ConfigureAwait(false);
            await CloseSocketAsync(socket, "Invalid identity.", cancellationToken).ConfigureAwait(false);
            return;
        }

        // 2. Validate enrollment
        var enrollment = await enrollmentStore.FindByGatewayAsync(helloFrame.TenantId, helloFrame.GatewayId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.State != GatewayEnrollmentState.Consumed ||
            !string.Equals(enrollment.WorkloadPublicKeyThumbprint, helloFrame.WorkloadPublicKeyThumbprint, StringComparison.Ordinal))
        {
            await SendFrameAsync(socket, GatewayFrame.Fault(null, "Gateway enrollment is not valid or thumbprint does not match."), cancellationToken).ConfigureAwait(false);
            await CloseSocketAsync(socket, "Authentication failed.", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!allowUnprovenTestIdentities)
        {
            if (string.IsNullOrWhiteSpace(helloFrame.WorkloadPublicKey)
                || !VerifyThumbprint(helloFrame.WorkloadPublicKey, enrollment.WorkloadPublicKeyThumbprint!))
            {
                await SendFrameAsync(socket, GatewayFrame.Fault(null, "Gateway workload-key proof is required."), cancellationToken).ConfigureAwait(false);
                await CloseSocketAsync(socket, "Authentication failed.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var challenge = RandomNumberGenerator.GetBytes(32);
            await SendFrameAsync(socket, new GatewayFrame
            {
                Kind = GatewayFrameKind.Challenge,
                Challenge = Convert.ToBase64String(challenge)
            }, cancellationToken).ConfigureAwait(false);
            var authentication = await ReceiveFrameAsync(socket, cancellationToken).ConfigureAwait(false);
            if (!VerifyProof(authentication, helloFrame.WorkloadPublicKey, challenge))
            {
                await SendFrameAsync(socket, GatewayFrame.Fault(null, "Gateway workload-key proof is not valid."), cancellationToken).ConfigureAwait(false);
                await CloseSocketAsync(socket, "Authentication failed.", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        // 3. Start the operation pump only after the handshake has consumed its authentication
        // frame, then register before acknowledging so an acknowledged session is immediately routable.
        var nodeId = !string.IsNullOrWhiteSpace(helloFrame.NodeId) ? helloFrame.NodeId : Guid.NewGuid().ToString("N")[..8];
        var session = new ActiveGatewaySession(
            helloFrame.TenantId,
            helloFrame.GatewayId,
            nodeId,
            helloFrame.WorkloadPublicKeyThumbprint,
            socket,
            maxFrameBytes,
            _time.GetUtcNow(),
            _meteringLedger,
            helloFrame.PublishedResources ?? []);

        if (!sessionRegistry.TryRegister(session))
        {
            await SendFrameAsync(socket, GatewayFrame.Fault(null, "Another session is already active for this Gateway node."), cancellationToken).ConfigureAwait(false);
            await CloseSocketAsync(socket, "Conflict.", cancellationToken).ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            // 4. Acknowledge only after the session is routable through the registry.
            await SendFrameAsync(socket, new GatewayFrame
            {
                Kind = GatewayFrameKind.HelloAck,
                TenantId = helloFrame.TenantId,
                GatewayId = helloFrame.GatewayId,
                Reason = "Authenticated successfully."
            }, cancellationToken).ConfigureAwait(false);

            // Keep session open until disconnected
            await session.WaitForClosureAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sessionRegistry.Unregister(session.TenantId, session.GatewayId, session);
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool VerifyThumbprint(string publicKeyBase64, string expectedThumbprint)
    {
        try
        {
            var actual = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyBase64)));
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(expectedThumbprint));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerifyProof(GatewayFrame? authentication, string publicKeyBase64, byte[] challenge)
    {
        if (authentication?.Kind != GatewayFrameKind.Authenticate
            || string.IsNullOrWhiteSpace(authentication.Signature))
            return false;
        try
        {
            using var publicKey = ECDsa.Create();
            publicKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return publicKey.VerifyData(
                challenge, Convert.FromBase64String(authentication.Signature), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private async Task SendFrameAsync(WebSocket socket, GatewayFrame frame, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(frame.Serialize());
        if (bytes.Length > maxFrameBytes)
            throw new GatewayProtocolException("Outbound frame exceeded max frame size.");

        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GatewayFrame?> ReceiveFrameAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(maxFrameBytes, 64 * 1024)];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;

            stream.Write(buffer, 0, result.Count);
            if (stream.Length > maxFrameBytes)
                throw new GatewayProtocolException("Inbound frame exceeded max frame size.");
            if (result.EndOfMessage) break;
        }

        if (stream.Length == 0) return null;
        return GatewayFrame.Deserialize(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static async Task CloseSocketAsync(WebSocket socket, string reason, CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException) { }
        }
    }
}

/// <summary>Active server-side Gateway session handle implementing <see cref="IGatewaySession"/>.</summary>
internal sealed class ActiveGatewaySession : IGatewaySession
{
    private readonly WebSocket _socket;
    private readonly int _maxFrameBytes;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _executeGate = new(1, 1);
    private readonly System.Threading.Channels.Channel<GatewayFrame> _inboundChannel =
        System.Threading.Channels.Channel.CreateUnbounded<GatewayFrame>(new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });
    private readonly TaskCompletionSource _closeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _sessionCts = new();
    private readonly Task _pumpTask;
    private readonly ITenantMeteringLedger? _meteringLedger;
    private long _lastSeenUtcTicks;

    public string TenantId { get; }
    public string GatewayId { get; }
    public string NodeId { get; }
    public string WorkloadPublicKeyThumbprint { get; }
    public DateTimeOffset ConnectedUtc { get; }
    public DateTimeOffset LastSeenUtc => new(Interlocked.Read(ref _lastSeenUtcTicks), TimeSpan.Zero);
    public bool IsActive => _socket.State == WebSocketState.Open && !_closeTcs.Task.IsCompleted;
    public IReadOnlyList<GatewayPublishedResource> PublishedResources { get; }

    public ActiveGatewaySession(
        string tenantId,
        string gatewayId,
        string nodeId,
        string workloadPublicKeyThumbprint,
        WebSocket socket,
        int maxFrameBytes,
        DateTimeOffset connectedUtc,
        ITenantMeteringLedger? meteringLedger = null,
        IReadOnlyList<GatewayPublishedResource>? publishedResources = null)
    {
        TenantId = tenantId;
        GatewayId = gatewayId;
        NodeId = nodeId;
        WorkloadPublicKeyThumbprint = workloadPublicKeyThumbprint;
        _socket = socket;
        _maxFrameBytes = maxFrameBytes;
        ConnectedUtc = connectedUtc;
        _lastSeenUtcTicks = connectedUtc.UtcTicks;
        _meteringLedger = meteringLedger;
        PublishedResources = publishedResources ?? [];
        _pumpTask = Task.Run(PumpInboundAsync);
    }

    public async Task<GatewayExecutionResult> ExecuteAsync(
        GatewayOperation operation,
        string? request,
        IReadOnlyList<string>? parameters,
        CancellationToken cancellationToken)
        => await ExecuteAsync(operation, request, parameters, null, cancellationToken).ConfigureAwait(false);

    public async Task<GatewayExecutionResult> ExecuteAsync(
        GatewayOperation operation,
        string? request,
        IReadOnlyList<string>? parameters,
        ViewerContextEnvelope? viewerContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _executeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        long egressBytes = 0;
        long ingressBytes = 0;
        long totalRows = 0;
        var status = TenantMeteringStatus.Failed;
        var dispatchAttempted = false;
        try
        {
            var opFrame = new GatewayFrame
            {
                Kind = GatewayFrameKind.Operation,
                OperationId = operation.OperationId,
                TenantId = TenantId,
                GatewayId = GatewayId,
                ResourceId = operation.ResourceId,
                OperationClass = operation.Class,
                Effect = operation.Effect,
                Bounds = operation.Bounds,
                CorrelationId = operation.CorrelationId,
                Request = request,
                Parameters = parameters,
                ViewerContext = viewerContext
            };

            var serialized = opFrame.Serialize();
            egressBytes = Encoding.UTF8.GetByteCount(serialized);
            dispatchAttempted = true;
            await SendFrameInternalAsync(opFrame, cancellationToken).ConfigureAwait(false);

            var columns = new List<string>();
            var rows = new List<IReadOnlyList<string?>>();
            var truncated = false;

            while (true)
            {
                var frame = await _inboundChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ingressBytes += Encoding.UTF8.GetByteCount(frame.Serialize());

                if (frame.Kind == GatewayFrameKind.Fault)
                    throw new GatewayProtocolException(
                        frame.Reason ?? "Gateway returned an unclassified fault.",
                        frame.OutcomeState,
                        frame.OperationId);

                if (frame.Kind == GatewayFrameKind.RowBatch)
                {
                    if (columns.Count == 0 && frame.Columns is not null)
                        columns.AddRange(frame.Columns);

                    if (frame.Rows is not null)
                        rows.AddRange(frame.Rows);
                }
                else if (frame.Kind == GatewayFrameKind.Complete)
                {
                    truncated = frame.RowsProduced > rows.Count;
                    break;
                }
            }

            totalRows = rows.Count;
            status = TenantMeteringStatus.Succeeded;
            return new GatewayExecutionResult(columns, rows, truncated);
        }
        catch (GatewayProtocolException)
        {
            throw;
        }
        catch (Exception) when (dispatchAttempted && operation.Effect == GatewayOperationEffect.Mutating)
        {
            throw new GatewayProtocolException(
                "The mutating Gateway operation lost its transport before a terminal outcome was received.",
                GatewayOutcomeState.Ambiguous,
                operation.OperationId);
        }
        finally
        {
            sw.Stop();
            _executeGate.Release();

            if (_meteringLedger is not null)
            {
                try
                {
                    var tenant = TenantContext.FromVerifiedCredential(TenantId);
                    await _meteringLedger.AppendAsync(tenant, new TenantMeteringEvent
                    {
                        SourceEventId = $"gw-{operation.OperationId}",
                        Source = TenantMeteringSource.Gateway,
                        WorkloadClass = TenantWorkloadClass.Gateway,
                        ConnectorClass = TenantConnectorClass.Gateway,
                        Status = status,
                        Rows = totalRows,
                        GatewayIngressBytes = ingressBytes,
                        GatewayEgressBytes = egressBytes,
                        ConcurrencyUnits = 1,
                        DurationMilliseconds = sw.ElapsedMilliseconds,
                        RecordedAtUtc = DateTimeOffset.UtcNow
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Metering ledger failures never alter execution results
                }
            }
        }
    }

    public Task WaitForClosureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.Register(() => _closeTcs.TrySetCanceled(cancellationToken));
        return _closeTcs.Task;
    }

    private async Task PumpInboundAsync()
    {
        var buffer = new byte[Math.Min(_maxFrameBytes, 64 * 1024)];
        try
        {
            while (!_sessionCts.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                using var stream = new MemoryStream();
                var closed = false;

                while (true)
                {
                    var result = await _socket.ReceiveAsync(buffer, _sessionCts.Token).ConfigureAwait(false);
                    Interlocked.Exchange(ref _lastSeenUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        closed = true;
                        break;
                    }

                    stream.Write(buffer, 0, result.Count);
                    if (stream.Length > _maxFrameBytes)
                    {
                        _inboundChannel.Writer.TryWrite(GatewayFrame.Fault(null, "Inbound frame exceeded max size."));
                        break;
                    }
                    if (result.EndOfMessage) break;
                }

                if (closed) break;
                if (stream.Length > 0)
                {
                    var frame = GatewayFrame.Deserialize(Encoding.UTF8.GetString(stream.ToArray()));
                    if (frame != null)
                    {
                        _inboundChannel.Writer.TryWrite(frame);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _inboundChannel.Writer.TryComplete(ex);
        }
        finally
        {
            _inboundChannel.Writer.TryComplete();
            _closeTcs.TrySetResult();
        }
    }

    private async Task SendFrameInternalAsync(GatewayFrame frame, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(frame.Serialize());
            if (bytes.Length > _maxFrameBytes)
                throw new GatewayProtocolException("Outbound operation frame exceeded max size.");

            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sessionCts.CancelAsync().ConfigureAwait(false);
        _closeTcs.TrySetResult();
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session disposed.", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }
        try
        {
            await _pumpTask.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }
        catch { }
        _sendGate.Dispose();
        _executeGate.Dispose();
        _sessionCts.Dispose();
        _socket.Dispose();
    }
}
