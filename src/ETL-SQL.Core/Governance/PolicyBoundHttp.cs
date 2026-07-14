using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Creates outbound HTTP clients that preserve enterprise egress controls at the actual socket
/// boundary. Callers still validate request URIs with <see cref="ConnectorPolicyAuthorizer"/> when
/// they have an execution context; this handler closes the lower-level gaps common to all HTTP
/// clients: ambient proxy bypass, automatic redirect bypass, and DNS rebinding at connect time.
/// </summary>
public static class PolicyBoundHttp
{
    public static SocketsHttpHandler CreateHandler(
        Action<SocketsHttpHandler>? configure = null,
        SslClientAuthenticationOptions? sslOptions = null)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectValidatedAsync
        };

        if (sslOptions != null)
            handler.SslOptions = sslOptions;

        configure?.Invoke(handler);
        return handler;
    }

    public static HttpClient CreateClient(
        Action<SocketsHttpHandler>? configureHandler = null,
        SslClientAuthenticationOptions? sslOptions = null,
        TimeSpan? timeout = null,
        Uri? baseAddress = null)
    {
        var client = new HttpClient(CreateHandler(configureHandler, sslOptions));
        if (timeout.HasValue)
            client.Timeout = timeout.Value;
        if (baseAddress != null)
            client.BaseAddress = baseAddress;
        return client;
    }

    /// <summary>
    /// Centralizes exceptional clients that use a supplied in-memory or test transport instead of a
    /// socket transport. Production network clients must use <see cref="CreateClient(Action{SocketsHttpHandler}?, SslClientAuthenticationOptions?, TimeSpan?, Uri?)"/>.
    /// </summary>
    public static HttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null, Uri? baseAddress = null)
    {
        var client = new HttpClient(handler);
        if (timeout.HasValue)
            client.Timeout = timeout.Value;
        if (baseAddress != null)
            client.BaseAddress = baseAddress;
        return client;
    }

    private static async ValueTask<Stream> ConnectValidatedAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        foreach (var address in addresses)
            ConnectorPolicyAuthorizer.EnforceResolvedAddress(host, address);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken)
                .ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
