using System.Net;
using System.Net.Sockets;
using System.Text;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("CompatBreak", "0.20.0")]
public sealed class RestBoundaryRegressionTests
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

    private static async Task Read(RestDataSource source, CancellationToken ct = default, bool enumeratorToken = false)
    {
        var batches = enumeratorToken ? source.ReadBatches(10) : source.ReadBatches(10, ct);
        await foreach (var _ in batches.WithCancellation(ct)) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RedirectToAnotherPortMustNotReceiveCredentials(bool apiKey)
    {
        string? forwarded = null;
        await using var target = new Server(headers =>
        {
            forwarded = headers.GetValueOrDefault(apiKey ? "X-Review-Access" : "Authorization");
            return new Response("[{\"id\":2}]");
        });
        await using var origin = new Server(_ => new Response("", 307, new() { ["Location"] = target.Url }));
        var options = Auth();
        if (apiKey)
        {
            options["AUTH_TYPE"] = "APIKEY";
            options["HEADER_NAME"] = "X-Review-Access";
        }
        await Read(new RestDataSource(Context(), origin.Url, options));
        Assert.Null(forwarded);
    }

    [Fact]
    public async Task CrossOriginRedirectCannotReceiveAutomaticallyStoredCookies()
    {
        string? cookie = null;
        await using var target = new Server(headers =>
        {
            cookie = headers.GetValueOrDefault("Cookie");
            return new Response("[]");
        });
        await using var origin = new Server(_ => new Response("", 307, new()
        {
            ["Location"] = target.Url,
            ["Set-Cookie"] = "review_session=synthetic-cookie-secret; Path=/"
        }));
        await Read(new RestDataSource(Context(), origin.Url));
        Assert.DoesNotContain("synthetic-cookie-secret", cookie ?? "");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PaginationLinkToAnotherHostMustNotReceiveCredentials(bool jsonLink)
    {
        string? forwarded = null;
        await using var target = new Server(headers =>
        {
            forwarded = headers.GetValueOrDefault("Authorization");
            return new Response("{\"items\":[{\"id\":2}]}");
        });
        await using var origin = new Server(_ => new Response(jsonLink
            ? $"{{\"items\":[{{\"id\":1}}],\"next\":\"{target.Url.Replace("127.0.0.1", "localhost", StringComparison.Ordinal)}\"}}"
            : "{\"items\":[{\"id\":1}]}", Headers: new()
            {
                ["Link"] = "<" + target.Url.Replace("127.0.0.1", "localhost", StringComparison.Ordinal) + ">; rel=\"next\""
            }));
        var options = Auth();
        options["PAGINATION_MODE"] = jsonLink ? "CURSOR" : "LINK_HEADER";
        options["ROOT_PATH"] = "items";
        if (jsonLink) options["NEXT_URL_PATH"] = "next";
        await Read(new RestDataSource(Context(), origin.Url, options));
        Assert.Null(forwarded);
    }

    [Fact]
    public async Task SameOriginPaginationMustKeepItsAuthorization()
    {
        var observed = new List<string?>();
        string? nextUrl = null;
        await using var server = new Server(headers =>
        {
            observed.Add(headers.GetValueOrDefault("Authorization"));
            return observed.Count == 1 ? new Response("[{\"id\":1}]", Headers: new() { ["Link"] = $"<{nextUrl}>; rel=\"next\"" })
                : new Response("[{\"id\":2}]");
        });
        nextUrl = server.Url + "?page=2";
        var options = Auth();
        options["PAGINATION_MODE"] = "LINK_HEADER";
        await Read(new RestDataSource(Context(), server.Url, options));
        Assert.Equal(new[] { "Bearer synthetic-review-token", "Bearer synthetic-review-token" }, observed);
    }

    [Fact]
    public async Task CrossOriginRedirectCannotReplayRequestBody()
    {
        var requests = 0;
        await using var target = new Server(_ => { requests++; return new Response("[]"); });
        await using var origin = new Server(_ => new Response("", 307, new() { ["Location"] = target.Url }));
        var options = Auth();
        options["METHOD"] = "POST";
        options["BODY"] = "{\"password\":\"synthetic-body-secret\"}";
        var exception = await Assert.ThrowsAsync<ExecutionException>(() => Read(new RestDataSource(Context(), origin.Url, options)));
        Assert.Contains("cross-origin", exception.Message);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringBodyReadRemainsCancellation(bool enumeratorToken)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new Server(_ => new Response("[]", BodyGate: release.Task));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Read(new RestDataSource(Context(), server.Url), cancellation.Token, enumeratorToken));
        }
        finally { release.TrySetResult(); }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingBeforeHeadersMustPreserveOperationCanceledException(bool enumeratorToken)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new Server(_ => new Response("[]", HeaderGate: release.Task));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Read(new RestDataSource(Context(), server.Url), cancellation.Token, enumeratorToken));
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
