using System.Net;
using System.Net.Sockets;
using System.Text;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Review;

public sealed class RestReproductions
{
    private static IExecutionContext Context()
    {
        var mock = new Mock<IExecutionContext>();
        mock.SetupGet(c => c.Logger).Returns(NullLogger.Instance);
        mock.SetupGet(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));
        mock.SetupGet(c => c.ServiceProvider).Returns(new Mock<IServiceProvider>().Object);
        mock.SetupGet(c => c.Connections).Returns(new Dictionary<string, IDataSource>());
        return mock.Object;
    }

    private static Dictionary<string, string> Auth() => new()
    {
        ["AUTH_TYPE"] = "BEARER",
        ["TOKEN"] = "synthetic-review-token"
    };

    private static async Task Read(RestDataSource source, CancellationToken ct = default)
    {
        await foreach (var _ in source.ReadBatches(10, ct)) { }
    }

    [Fact]
    public async Task RedirectToAnotherPortMustNotReceiveCredentials()
    {
        string? forwarded = null;
        await using var target = new Server(headers =>
        {
            forwarded = headers.GetValueOrDefault("Authorization");
            return new Response("[{\"id\":2}]");
        });
        await using var origin = new Server(_ => new Response("", 307, new() { ["Location"] = target.Url }));
        await Read(new RestDataSource(Context(), origin.Url, Auth()));
        Assert.Null(forwarded);
    }

    [Fact]
    public async Task PaginationLinkToAnotherHostMustNotReceiveCredentials()
    {
        string? forwarded = null;
        await using var target = new Server(headers =>
        {
            forwarded = headers.GetValueOrDefault("Authorization");
            return new Response("[{\"id\":2}]");
        });
        await using var origin = new Server(_ => new Response("[{\"id\":1}]", Headers: new()
        {
            ["Link"] = "<" + target.Url.Replace("127.0.0.1", "localhost", StringComparison.Ordinal) + ">; rel=\"next\""
        }));
        var options = Auth();
        options["PAGINATION_MODE"] = "LINK_HEADER";
        await Read(new RestDataSource(Context(), origin.Url, options));
        Assert.Null(forwarded);
    }

    [Fact]
    public async Task BodyReadMustRemainUnderTheConfiguredRequestTimeout()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new Server(_ => new Response("[{\"id\":1}]", BodyGate: release.Task));
        var options = new Dictionary<string, string> { ["TIMEOUT_SECONDS"] = "1" };
        var read = Read(new RestDataSource(Context(), server.Url, options));
        try
        {
            // The server sends headers immediately, then holds the body until this check finishes.
            var completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(read, completed);
            Assert.True(read.IsFaulted || read.IsCanceled);
        }
        finally
        {
            release.TrySetResult();
            try { await read; } catch (Exception) { /* inspected above */ }
        }
    }

    [Fact]
    public async Task CancellingBeforeHeadersMustPreserveOperationCanceledException()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new Server(_ => new Response("[]", HeaderGate: release.Task));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Read(new RestDataSource(Context(), server.Url), cancellation.Token));
        }
        finally { release.TrySetResult(); }
    }

    private sealed record Response(string Body, int Status = 200,
        Dictionary<string, string>? Headers = null, Task? HeaderGate = null, Task? BodyGate = null);

    private sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        public string Url { get; }

        public Server(Func<Dictionary<string, string>, Response> handler)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _loop = Serve(handler);
        }

        private async Task Serve(Func<Dictionary<string, string>, Response> handler)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    _ = await reader.ReadLineAsync(_stop.Token);
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token)))
                    {
                        var colon = line.IndexOf(':');
                        if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim();
                    }
                    var response = handler(headers);
                    if (response.HeaderGate is not null) await response.HeaderGate.WaitAsync(_stop.Token);
                    var body = Encoding.UTF8.GetBytes(response.Body);
                    var head = $"HTTP/1.1 {response.Status} Review\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n";
                    foreach (var header in response.Headers ?? []) head += $"{header.Key}: {header.Value}\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head + "\r\n"), _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                    if (response.BodyGate is not null) await response.BodyGate.WaitAsync(_stop.Token);
                    await stream.WriteAsync(body, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop;
            _stop.Dispose();
        }
    }
}
