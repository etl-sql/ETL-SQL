using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors;
using ETL_SQL.Services;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Trait("Category", "Integration")]
    [Trait("Connector", "SHAREPOINT")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class SharePointIntegrationTests
    {
        private IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        [Fact]
        public async Task SharePointConnector_GetVersionAsync_Success()
        {
            var ctx = MakeContext();
            
            // Start local loopback SharePoint mock server
            await using var server = new LocalSpServer(req =>
            {
                Assert.Contains("_api/web", req.Path);
                return LocalSpResponse.Json("""{"d":{"Title":"Finance Portal"}}""");
            });

            var connector = new SharePointConnector(ctx, server.Url, null);
            var version = await connector.GetVersionAsync(ctx, server.Url);

            Assert.Contains("Connected", version);
            Assert.Contains("Finance Portal", version);
        }

        [Fact]
        public async Task SharePointConnector_GetVersionAsync_Failure_ThrowsException()
        {
            var ctx = MakeContext();

            await using var server = new LocalSpServer(req =>
            {
                return new LocalSpResponse(500, "Internal Server Error");
            });

            var connector = new SharePointConnector(ctx, server.Url, null);
            await Assert.ThrowsAsync<ExecutionException>(() => connector.GetVersionAsync(ctx, server.Url));
        }

        [Fact]
        public async Task SharePointConnector_UploadAndDownload_Success()
        {
            var ctx = MakeContext();
            string uploadContent = "hello loopback sharepoint integration test";
            string remoteKey = "incoming/hello.txt";

            await using var server = new LocalSpServer(req =>
            {
                if (req.Method == "POST" && req.Path.Contains("Files/Add"))
                {
                    Assert.Contains("hello.txt", req.Path);
                    Assert.Equal(uploadContent, req.Body);
                    return LocalSpResponse.Json("{}");
                }
                if (req.Method == "GET" && req.Path.Contains("GetFileByServerRelativeUrl"))
                {
                    Assert.Contains("hello.txt", req.Path);
                    return new LocalSpResponse(200, uploadContent, "text/plain");
                }
                return new LocalSpResponse(404, "Not Found");
            });

            var connector = new SharePointConnector(ctx, server.Url, null);

            var localSrc = Path.GetTempFileName();
            var localDst = Path.GetTempFileName();

            try
            {
                await File.WriteAllTextAsync(localSrc, uploadContent);

                // 1. Upload File
                await connector.UploadFileAsync(localSrc, remoteKey, overwrite: true);

                // 2. Download File
                await connector.DownloadFileAsync(remoteKey, localDst, overwrite: true);
                var downloadedContent = await File.ReadAllTextAsync(localDst);

                Assert.Equal(uploadContent, downloadedContent);
            }
            finally
            {
                if (File.Exists(localSrc)) File.Delete(localSrc);
                if (File.Exists(localDst)) File.Delete(localDst);
            }
        }

        // ── Helper loopback web server ───────────────────────────────────────────

        private sealed class LocalSpServer : IAsyncDisposable
        {
            private readonly TcpListener _listener;
            private readonly Func<LocalSpRequest, LocalSpResponse> _handler;
            private readonly CancellationTokenSource _cts = new();
            private readonly Task _serverTask;

            public LocalSpServer(Func<LocalSpRequest, LocalSpResponse> handler)
            {
                _handler = handler;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Url = $"http://127.0.0.1:{port}/sites/Finance";
                _serverTask = Task.Run(AcceptLoopAsync);
            }

            public string Url { get; }

            public async ValueTask DisposeAsync()
            {
                _cts.Cancel();
                _listener.Stop();
                try
                {
                    await _serverTask;
                }
                catch { }
                _cts.Dispose();
            }

            private async Task AcceptLoopAsync()
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    }
                    catch
                    {
                        break;
                    }

                    _ = Task.Run(async () =>
                    {
                        using (client)
                        await using (var stream = client.GetStream())
                        using (var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true))
                        {
                            var requestLine = await reader.ReadLineAsync(_cts.Token);
                            if (string.IsNullOrWhiteSpace(requestLine)) return;

                            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            string? line;
                            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_cts.Token)))
                            {
                                var separator = line.IndexOf(':');
                                if (separator > 0)
                                {
                                    headers[line[..separator]] = line[(separator + 1)..].Trim();
                                }
                            }

                            var contentLength = 0;
                            if (headers.TryGetValue("Content-Length", out var cl))
                            {
                                int.TryParse(cl, out contentLength);
                            }

                            var body = string.Empty;
                            if (contentLength > 0)
                            {
                                var buffer = new char[contentLength];
                                var read = await reader.ReadBlockAsync(buffer, 0, contentLength);
                                body = new string(buffer, 0, read);
                            }

                            var response = _handler(new LocalSpRequest(parts[0], parts.Length > 1 ? parts[1] : "/", headers, body));
                            var responseBytes = Encoding.UTF8.GetBytes(response.Body);
                            var headerBytes = Encoding.ASCII.GetBytes(
                                $"HTTP/1.1 {response.StatusCode} OK\r\n" +
                                $"Content-Type: {response.ContentType}\r\n" +
                                $"Content-Length: {responseBytes.Length}\r\n" +
                                "Connection: close\r\n\r\n");

                            await stream.WriteAsync(headerBytes, _cts.Token);
                            await stream.WriteAsync(responseBytes, _cts.Token);
                        }
                    });
                }
            }
        }

        private sealed record LocalSpRequest(string Method, string Path, Dictionary<string, string> Headers, string Body);

        private sealed record LocalSpResponse(int StatusCode, string Body, string ContentType = "text/plain")
        {
            public static LocalSpResponse Json(string json) => new(200, json, "application/json");
        }
    }
}
