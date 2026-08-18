using System.Net.WebSockets;
using System.Text;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Gateway;

/// <summary>
/// Server-side broker that authenticates incoming on-premises Gateway WebSocket sessions,
/// registers them in the <see cref="GatewaySessionRegistry"/>, and routes typed operations.
/// </summary>
public sealed class GatewayBroker(
    IGatewayEnrollmentStore enrollmentStore,
    GatewaySessionRegistry sessionRegistry,
    int maxFrameBytes = 1 << 20,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

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

        // 3. Acknowledge Hello
        await SendFrameAsync(socket, new GatewayFrame
        {
            Kind = GatewayFrameKind.HelloAck,
            TenantId = helloFrame.TenantId,
            GatewayId = helloFrame.GatewayId,
            Reason = "Authenticated successfully."
        }, cancellationToken).ConfigureAwait(false);

        // 4. Register session
        var session = new ActiveGatewaySession(
            helloFrame.TenantId,
            helloFrame.GatewayId,
            helloFrame.WorkloadPublicKeyThumbprint,
            socket,
            maxFrameBytes,
            _time.GetUtcNow());

        if (!sessionRegistry.TryRegister(session))
        {
            await SendFrameAsync(socket, GatewayFrame.Fault(null, "Another session is already active for this Gateway."), cancellationToken).ConfigureAwait(false);
            await CloseSocketAsync(socket, "Conflict.", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            // Keep session open until disconnected
            await session.WaitForClosureAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sessionRegistry.Unregister(session.TenantId, session.GatewayId, session);
            await session.DisposeAsync().ConfigureAwait(false);
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

    public string TenantId { get; }
    public string GatewayId { get; }
    public string WorkloadPublicKeyThumbprint { get; }
    public DateTimeOffset ConnectedUtc { get; }
    public bool IsActive => _socket.State == WebSocketState.Open && !_closeTcs.Task.IsCompleted;

    public ActiveGatewaySession(
        string tenantId,
        string gatewayId,
        string workloadPublicKeyThumbprint,
        WebSocket socket,
        int maxFrameBytes,
        DateTimeOffset connectedUtc)
    {
        TenantId = tenantId;
        GatewayId = gatewayId;
        WorkloadPublicKeyThumbprint = workloadPublicKeyThumbprint;
        _socket = socket;
        _maxFrameBytes = maxFrameBytes;
        ConnectedUtc = connectedUtc;
        _pumpTask = Task.Run(PumpInboundAsync);
    }

    public async Task<GatewayExecutionResult> ExecuteAsync(
        GatewayOperation operation,
        string? request,
        IReadOnlyList<string>? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _executeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                Parameters = parameters
            };

            await SendFrameInternalAsync(opFrame, cancellationToken).ConfigureAwait(false);

            var columns = new List<string>();
            var rows = new List<IReadOnlyList<string?>>();
            var truncated = false;

            while (true)
            {
                var frame = await _inboundChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (frame.Kind == GatewayFrameKind.Fault)
                    throw new GatewayProtocolException(frame.Reason ?? "Gateway returned an unclassified fault.");

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

            return new GatewayExecutionResult(columns, rows, truncated);
        }
        finally
        {
            _executeGate.Release();
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
