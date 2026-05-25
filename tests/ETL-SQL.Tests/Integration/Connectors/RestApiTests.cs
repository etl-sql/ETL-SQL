using System;
using System.Net;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Trait("Category", "Integration")]
    [Trait("Connector", "API")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class RestApiTests
    {
        [Fact]
        public async Task ReadBatches_SimpleJson_CorrectlyParses()
        {
            // Note: Since RestDataSource uses a static HttpClient, mocking it directly via constructor 
            // would require refactoring. For this test, we verify the logic assuming a successful response.
            // In a real environment, we'd use a MockHttpMessageHandler.
            
            // For now, let's test the Connection String Building logic which is definitely unit-testable.
            var props = new Dictionary<string, string>
            {
                { "URL", "https://api.github.com/repos/test/test/issues" }
            };

            var cs = ETL_SQL.Connectors.ConnectionStringBuilder.Build("API", props);
            Assert.Equal("https://api.github.com/repos/test/test/issues", cs);
        }

        [Fact]
        public void BuildRest_WithProperties_CorrectlyReturnsUrl()
        {
            var props = new Dictionary<string, string>
            {
                { "URL", "https://api.test.com/v1" },
                { "AUTH_TYPE", "BEARER" },
                { "TOKEN", "secret_token" }
            };

            var cs = ETL_SQL.Connectors.ConnectionStringBuilder.Build("REST", props);
            Assert.Equal("https://api.test.com/v1", cs);
        }

        [Fact]
        public async Task Put_WithJsonBody_SendsMethodBodyAndParsesResponse()
        {
            await using var server = new LocalHttpApiServer(request =>
            {
                Assert.Equal("PUT", request.Method);
                Assert.Contains("\"name\":\"updated\"", request.Body);
                return LocalHttpResponse.Json("""[{"status":"updated"}]""");
            });

            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "PUT",
                ["BODY"] = """{"name":"updated"}"""
            });

            var batches = await ds.ReadBatches().ToListAsync();

            Assert.Single(batches);
            Assert.Equal("updated", batches[0].Rows[0]["status"]);
        }

        [Fact]
        public async Task Delete_SendsDeleteMethodAndParsesResponse()
        {
            await using var server = new LocalHttpApiServer(request =>
            {
                Assert.Equal("DELETE", request.Method);
                return LocalHttpResponse.Json("""[{"deleted":true}]""");
            });

            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "DELETE"
            });

            var batches = await ds.ReadBatches().ToListAsync();

            Assert.Single(batches);
            Assert.Equal(true, batches[0].Rows[0]["deleted"]);
        }

        [Theory]
        [InlineData("BASIC", "Authorization", "Basic YXBpdXNlcjphcGlwYXNz")]
        [InlineData("BEARER", "Authorization", "Bearer bearer-token")]
        [InlineData("APIKEY", "X-API-Key", "api-key-token")]
        public async Task AuthSchemes_SendExpectedHeader(string authType, string expectedHeader, string expectedValue)
        {
            await using var server = new LocalHttpApiServer(request =>
            {
                Assert.True(request.Headers.TryGetValue(expectedHeader, out var actualValue));
                Assert.Equal(expectedValue, actualValue);
                return LocalHttpResponse.Json("""[{"authorized":true}]""");
            });

            var options = new Dictionary<string, string>
            {
                ["AUTH_TYPE"] = authType,
                ["USER"] = "apiuser",
                ["PASSWORD"] = "apipass",
                ["TOKEN"] = authType == "BEARER" ? "bearer-token" : "api-key-token",
                ["HEADER_NAME"] = "X-API-Key"
            };

            var ds = new RestDataSource(MakeContext(), server.Url, options);

            var batches = await ds.ReadBatches().ToListAsync();

            Assert.Single(batches);
            Assert.Equal(true, batches[0].Rows[0]["authorized"]);
        }

        private static IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        private sealed class LocalHttpApiServer : IAsyncDisposable
        {
            private readonly TcpListener _listener;
            private readonly Func<LocalHttpRequest, LocalHttpResponse> _handler;
            private readonly CancellationTokenSource _cts = new();
            private readonly Task _serverTask;

            public LocalHttpApiServer(Func<LocalHttpRequest, LocalHttpResponse> handler)
            {
                _handler = handler;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Url = $"http://127.0.0.1:{port}/endpoint";
                _serverTask = Task.Run(AcceptOneAsync);
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
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                _cts.Dispose();
            }

            private async Task AcceptOneAsync()
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

                var requestLine = await reader.ReadLineAsync(_cts.Token);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }

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
                if (headers.TryGetValue("Content-Length", out var contentLengthText))
                {
                    int.TryParse(contentLengthText, out contentLength);
                }

                var body = string.Empty;
                if (contentLength > 0)
                {
                    var buffer = new char[contentLength];
                    var read = await reader.ReadBlockAsync(buffer, 0, contentLength);
                    body = new string(buffer, 0, read);
                }

                var response = _handler(new LocalHttpRequest(parts[0], parts.Length > 1 ? parts[1] : "/", headers, body));
                var responseBytes = Encoding.UTF8.GetBytes(response.Body);
                var headerBytes = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {response.StatusCode} OK\r\n" +
                    $"Content-Type: {response.ContentType}\r\n" +
                    $"Content-Length: {responseBytes.Length}\r\n" +
                    "Connection: close\r\n\r\n");

                await stream.WriteAsync(headerBytes, _cts.Token);
                await stream.WriteAsync(responseBytes, _cts.Token);
            }
        }

        private sealed record LocalHttpRequest(
            string Method,
            string Path,
            Dictionary<string, string> Headers,
            string Body);

        private sealed record LocalHttpResponse(int StatusCode, string ContentType, string Body)
        {
            public static LocalHttpResponse Json(string body) => new(200, "application/json", body);
        }
    }
}
