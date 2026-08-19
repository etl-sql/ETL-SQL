using System.Net.WebSockets;
using System.Text;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Gateway;

/// <summary>Identity and destination a Gateway session dials out with.</summary>
public sealed record GatewaySessionOptions(
    Uri BrokerUri,
    string TenantId,
    string GatewayId,
    string WorkloadPublicKeyThumbprint,
    string? NodeId = null,
    int MaxFrameBytes = 1 << 20)
{
    /// <summary>Resolved node identity (explicit or host machine name).</summary>
    public string EffectiveNodeId => !string.IsNullOrWhiteSpace(NodeId) ? NodeId : Environment.MachineName;

    /// <summary>
    /// The Gateway is outbound-only: it dials the broker and never listens. A non-TLS destination is
    /// refused outside loopback, so a misconfiguration cannot quietly downgrade the session.
    /// </summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(BrokerUri);
        if (string.IsNullOrWhiteSpace(TenantId))
            throw new GatewayProtocolException("A Gateway session requires a tenant.");
        if (string.IsNullOrWhiteSpace(GatewayId))
            throw new GatewayProtocolException("A Gateway session requires a Gateway ID.");
        if (string.IsNullOrWhiteSpace(WorkloadPublicKeyThumbprint))
            throw new GatewayProtocolException("A Gateway session requires a workload identity.");
        if (MaxFrameBytes <= 0)
            throw new GatewayProtocolException("A Gateway session requires a positive maximum frame size.");

        if (BrokerUri.Scheme is not ("wss" or "ws"))
            throw new GatewayProtocolException(
                $"A Gateway session speaks only the typed protocol over wss; '{BrokerUri.Scheme}' is not supported.");
        if (BrokerUri.Scheme == "ws" && !BrokerUri.IsLoopback)
            throw new GatewayProtocolException(
                "A Gateway session requires TLS; an unencrypted broker URI is permitted only on loopback for tests.");
    }
}

/// <summary>
/// The on-premises Gateway's outbound session.
///
/// <para>It dials out, presents its workload identity, then serves typed operations until the
/// session ends. It opens no listening port, and it has no code path that takes a host, port, or
/// URL from the wire — the only destination it ever knows is the broker it was configured with, and
/// the only work it accepts names a locally registered resource. That is the difference between
/// this and a tunnel, and it is a property of the type, not of a runtime check.</para>
/// </summary>
public sealed class GatewaySessionClient(
    GatewaySessionOptions options,
    GatewayOperationDispatcher dispatcher)
{
    /// <summary>
    /// Runs one session to completion. Returns the number of operations served, which lets a host
    /// distinguish "connected and idle" from "served work".
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        options.Validate();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(options.BrokerUri, cancellationToken).ConfigureAwait(false);

        await SendAsync(socket, new GatewayFrame
        {
            Kind = GatewayFrameKind.Hello,
            TenantId = options.TenantId,
            GatewayId = options.GatewayId,
            NodeId = options.EffectiveNodeId,
            WorkloadPublicKeyThumbprint = options.WorkloadPublicKeyThumbprint
        }, cancellationToken).ConfigureAwait(false);

        var ack = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
        if (ack is null || ack.Kind != GatewayFrameKind.HelloAck)
        {
            await CloseAsync(socket, "The broker refused the session.", cancellationToken).ConfigureAwait(false);
            throw new GatewayProtocolException(ack?.Reason ?? "The broker refused the Gateway session.");
        }

        var served = 0;
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var frame = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
            if (frame is null) break;

            var responses = await dispatcher
                .DispatchAsync(frame, options.TenantId, options.GatewayId, cancellationToken)
                .ConfigureAwait(false);

            foreach (var response in responses)
                await SendAsync(socket, response, cancellationToken).ConfigureAwait(false);

            served++;
        }

        await CloseAsync(socket, "Session complete.", cancellationToken).ConfigureAwait(false);
        return served;
    }

    private async Task SendAsync(WebSocket socket, GatewayFrame frame, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(frame.Serialize());
        if (payload.Length > options.MaxFrameBytes)
            throw new GatewayProtocolException("A Gateway frame exceeded the negotiated maximum frame size.");

        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<GatewayFrame?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        // Bounded buffering is mandatory (§11.5): a frame that will not fit is refused rather than
        // accumulated, so a hostile or broken broker cannot drive the Gateway out of memory.
        var buffer = new byte[Math.Min(options.MaxFrameBytes, 64 * 1024)];
        using var message = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;

            message.Write(buffer, 0, result.Count);
            if (message.Length > options.MaxFrameBytes)
                throw new GatewayProtocolException("An inbound Gateway frame exceeded the maximum frame size.");
            if (result.EndOfMessage) break;
        }

        if (message.Length == 0) return null;
        return GatewayFrame.Deserialize(Encoding.UTF8.GetString(message.ToArray()));
    }

    private static async Task CloseAsync(WebSocket socket, string reason, CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // The peer may already be gone; a failed courtesy close is not an error worth raising.
            }
        }
    }
}
