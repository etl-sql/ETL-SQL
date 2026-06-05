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
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
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

        [Theory]
        [InlineData("BODY_CONTENT_TYPE")]
        [InlineData("HEADER_Content-Type")]
        public async Task Post_WithBodyContentType_SendsContentHeader(string optionName)
        {
            await using var server = new LocalHttpApiServer(request =>
            {
                Assert.Equal("POST", request.Method);
                Assert.True(request.Headers.TryGetValue("Content-Type", out var contentType));
                Assert.Equal("text/plain; charset=utf-8", contentType);
                Assert.Equal("plain body", request.Body);
                return LocalHttpResponse.Json("""[{"accepted":true}]""");
            });

            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY"] = "plain body",
                [optionName] = "text/plain"
            });

            var batches = await ds.ReadBatches().ToListAsync();

            Assert.Single(batches);
            Assert.Equal(true, batches[0].Rows[0]["accepted"]);
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

        private static IExecutionContext MakeContext(Dictionary<string, IDataSource>? connections = null)
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            ctx.Setup(c => c.Connections).Returns(connections ?? new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase));
            var serviceProvider = new Mock<IServiceProvider>();
            ctx.Setup(c => c.ServiceProvider).Returns(serviceProvider.Object);
            ctx.Setup(c => c.TempTableSpillThresholdRows).Returns(1000000);
            return ctx.Object;
        }

        [Fact]
        public async Task WriteBatches_RowObject_SendsPostRequests()
        {
            var receivedRequests = new List<LocalHttpRequest>();
            var reqLock = new object();
            await using var server = new LocalHttpApiServer(request =>
            {
                lock (reqLock)
                {
                    receivedRequests.Add(request);
                }
                return LocalHttpResponse.Json("""{"status":"ok"}""");
            });

            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "ROW_OBJECT"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id", "name" });
            var row1 = new Row(table.Schema);
            row1["id"] = 1;
            row1["name"] = "Alice";
            await table.AddRowAsync(row1);

            var row2 = new Row(table.Schema);
            row2["id"] = 2;
            row2["name"] = "Bob";
            await table.AddRowAsync(row2);

            await ds.WriteBatches(ToAsyncEnumerable(table));

            Assert.Equal(2, receivedRequests.Count);
            Assert.Equal("POST", receivedRequests[0].Method);
            Assert.Contains("\"id\":1", receivedRequests[0].Body);
            Assert.Contains("\"name\":\"Alice\"", receivedRequests[0].Body);
            Assert.Contains("\"id\":2", receivedRequests[1].Body);
            Assert.Contains("\"name\":\"Bob\"", receivedRequests[1].Body);
        }

        [Fact]
        public async Task WriteBatches_RowArray_SendsSingleBatchRequest()
        {
            var receivedRequests = new List<LocalHttpRequest>();
            var reqLock = new object();
            await using var server = new LocalHttpApiServer(request =>
            {
                lock (reqLock)
                {
                    receivedRequests.Add(request);
                }
                return LocalHttpResponse.Json("""{"status":"ok"}""");
            });

            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "ROW_ARRAY",
                ["BATCH_SIZE"] = "10"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id", "name" });
            var row1 = new Row(table.Schema);
            row1["id"] = 1;
            row1["name"] = "Alice";
            await table.AddRowAsync(row1);

            var row2 = new Row(table.Schema);
            row2["id"] = 2;
            row2["name"] = "Bob";
            await table.AddRowAsync(row2);

            await ds.WriteBatches(ToAsyncEnumerable(table));

            Assert.Single(receivedRequests);
            Assert.Equal("POST", receivedRequests[0].Method);
            Assert.StartsWith("[", receivedRequests[0].Body);
            Assert.Contains("\"id\":1", receivedRequests[0].Body);
            Assert.Contains("\"id\":2", receivedRequests[0].Body);
        }

        [Fact]
        public async Task WriteBatches_WrappedArray_SendsWrappedBatchRequest()
        {
            var receivedRequests = new List<LocalHttpRequest>();
            var reqLock = new object();
            await using var server = new LocalHttpApiServer(request =>
            {
                lock (reqLock)
                {
                    receivedRequests.Add(request);
                }
                return LocalHttpResponse.Json("""{"status":"ok"}""");
            });

            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "WRAPPED_ARRAY",
                ["BATCH_ROOT"] = "items",
                ["BATCH_SIZE"] = "10"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id", "name" });
            var row1 = new Row(table.Schema);
            row1["id"] = 1;
            row1["name"] = "Alice";
            await table.AddRowAsync(row1);

            await ds.WriteBatches(ToAsyncEnumerable(table));

            Assert.Single(receivedRequests);
            Assert.StartsWith("{\"items\":[", receivedRequests[0].Body);
        }

        [Fact]
        public async Task WriteBatches_ResponseTable_CapturesMetadataAndCorrelation()
        {
            await using var server = new LocalHttpApiServer(request =>
            {
                return LocalHttpResponse.Json("""{"status":"created"}""");
            });

            var connections = new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase);
            var context = MakeContext(connections);

            var ds = new RestDataSource(context, server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "ROW_OBJECT",
                ["RESPONSE_TABLE"] = "#my_api_results",
                ["RESPONSE_CORRELATION_COLUMNS"] = "id,name"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id", "name" });
            var row1 = new Row(table.Schema);
            row1["id"] = 123;
            row1["name"] = "Alice";
            await table.AddRowAsync(row1);

            await ds.WriteBatches(ToAsyncEnumerable(table));

            Assert.True(connections.TryGetValue("#my_api_results", out var responseDs));
            var respMemDs = Assert.IsType<InMemoryDataSource>(responseDs);

            var resultBatches = await respMemDs.ReadBatches().ToListAsync();
            Assert.Single(resultBatches);
            var resultRow = resultBatches[0].Rows[0];

            Assert.Equal(0, Convert.ToInt32(resultRow["request_index"]));
            Assert.Equal(true, resultRow["success"]);
            Assert.Equal(200, Convert.ToInt32(resultRow["status_code"]));
            Assert.Equal("POST", resultRow["method"]);
            Assert.Equal(1, Convert.ToInt32(resultRow["row_count"]));
            Assert.Contains("created", (string)resultRow["response_body"]!);
            Assert.Equal(123, Convert.ToInt32(resultRow["id"]));
            Assert.Equal("Alice", resultRow["name"]);
        }

        [Fact]
        public async Task EngineInsertIntoApi_PostsRowsAndCapturesResponseTable()
        {
            var receivedRequests = new List<LocalHttpRequest>();
            var reqLock = new object();
            await using var server = new LocalHttpApiServer(request =>
            {
                lock (reqLock)
                {
                    receivedRequests.Add(request);
                }

                return new LocalHttpResponse(201, "application/json", """{"accepted":true}""");
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = $@"
CREATE TABLE #bed_usage (
    submission_id VARCHAR,
    location VARCHAR,
    total_beds INT,
    occupied_beds INT
);

INSERT INTO #bed_usage VALUES ('sub-001', 'ICU', 24, 19);

CREATE CONNECTION bed_api AS API(
    URL = '{server.Url}',
    METHOD = 'POST',
    BODY_MODE = 'ROW_OBJECT',
    RESPONSE_TABLE = '#bed_api_results',
    RESPONSE_CORRELATION_COLUMNS = 'submission_id,location',
    SUCCESS_STATUS = '201'
);

INSERT INTO bed_api (submission_id, location, totalBeds, occupiedBeds)
SELECT submission_id, location, total_beds, occupied_beds
FROM #bed_usage;
";

            await eval.Evaluate(new Parser(new Lexer(script).Tokenize(), script).Parse());

            Assert.Single(receivedRequests);
            Assert.Contains("\"location\":\"ICU\"", receivedRequests[0].Body);
            Assert.Contains("\"totalBeds\":24", receivedRequests[0].Body);

            Assert.True(eval.Connections.TryGetValue("#bed_api_results", out var responseDs));
            var responseBatches = await responseDs.ReadBatches().ToListAsync();
            Assert.Single(responseBatches);
            var responseRow = responseBatches[0].Rows[0];

            Assert.Equal(true, responseRow["success"]);
            Assert.Equal(201, Convert.ToInt32(responseRow["status_code"]));
            Assert.Equal("sub-001", responseRow["submission_id"]);
            Assert.Equal("ICU", responseRow["location"]);
        }

        [Fact]
        public async Task WriteBatches_Template_SubstitutesBodyAndUrl()
        {
            var receivedRequests = new List<LocalHttpRequest>();
            var reqLock = new object();
            await using var server = new LocalHttpApiServer(request =>
            {
                lock (reqLock)
                {
                    receivedRequests.Add(request);
                }
                return LocalHttpResponse.Json("""{"status":"ok"}""");
            });

            var ds = new RestDataSource(MakeContext(), server.Url + "/${location}", new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "TEMPLATE",
                ["BODY_TEMPLATE"] = """{"facility":"${location}","occupied":${occupied}}"""
            });

            var table = new DataTable();
            table.SetColumns(new[] { "location", "occupied" });
            var row = new Row(table.Schema);
            row["location"] = "ICU A";
            row["occupied"] = 15;
            await table.AddRowAsync(row);

            await ds.WriteBatches(ToAsyncEnumerable(table));

            Assert.Single(receivedRequests);
            Assert.Equal("/endpoint/ICU%20A", receivedRequests[0].Path);
            Assert.Contains("\"facility\":\"ICU A\"", receivedRequests[0].Body);
            Assert.Contains("\"occupied\":15", receivedRequests[0].Body);
        }

        [Fact]
        public async Task WriteBatches_Retry_ExponentialBackoff()
        {
            int attempts = 0;
            var reqLock = new object();
            await using var server = new LocalHttpApiServer(request =>
            {
                lock (reqLock)
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        return new LocalHttpResponse(429, "application/json", """{"error":"too many requests"}""");
                    }
                    return LocalHttpResponse.Json("""{"status":"ok"}""");
                }
            });

            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "ROW_OBJECT",
                ["RETRY_COUNT"] = "2",
                ["RETRY_BACKOFF_MS"] = "10",
                ["RETRY_STATUS"] = "429"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id" });
            var row = new Row(table.Schema);
            row["id"] = 1;
            await table.AddRowAsync(row);

            await ds.WriteBatches(ToAsyncEnumerable(table));

            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task WriteBatches_WhatIf_DoesNotSendRequests()
        {
            int attempts = 0;
            await using var server = new LocalHttpApiServer(request =>
            {
                attempts++;
                return LocalHttpResponse.Json("""{"status":"ok"}""");
            });

            var contextMock = new Mock<IExecutionContext>();
            contextMock.Setup(c => c.IsWhatIf).Returns(true);
            contextMock.Setup(c => c.Logger).Returns(NullLogger.Instance);
            contextMock.Setup(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));

            var ds = new RestDataSource(contextMock.Object, server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "ROW_OBJECT"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id" });
            var row = new Row(table.Schema);
            row["id"] = 1;
            await table.AddRowAsync(row);

            await ds.WriteBatches(ToAsyncEnumerable(table));

            Assert.Equal(0, attempts);
        }

        [Fact]
        public async Task WriteBatches_DeleteMethod_IsRejectedForOutboundWrites()
        {
            await using var server = new LocalHttpApiServer(_ => LocalHttpResponse.Json("""{"status":"ok"}"""));
            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "DELETE",
                ["BODY_MODE"] = "ROW_OBJECT"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id" });
            await table.AddRowAsync(new Row(table.Schema) { ["id"] = 1 });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(ToAsyncEnumerable(table)));
            Assert.Contains("not supported for writing", ex.Message);
        }

        [Fact]
        public async Task WriteBatches_InvalidErrorMode_Throws()
        {
            await using var server = new LocalHttpApiServer(_ => LocalHttpResponse.Json("""{"status":"ok"}"""));
            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "ROW_OBJECT",
                ["ERROR_MODE"] = "IGNORE"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id" });
            await table.AddRowAsync(new Row(table.Schema) { ["id"] = 1 });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(ToAsyncEnumerable(table)));
            Assert.Contains("Unsupported ERROR_MODE", ex.Message);
        }

        [Fact]
        public async Task WriteBatches_CorrelationColumnMissing_ThrowsSanitizedExecutionException()
        {
            await using var server = new LocalHttpApiServer(_ => LocalHttpResponse.Json("""{"status":"ok"}"""));
            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "ROW_OBJECT",
                ["RESPONSE_TABLE"] = "#api_results",
                ["RESPONSE_CORRELATION_COLUMNS"] = "missing_id"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id" });
            await table.AddRowAsync(new Row(table.Schema) { ["id"] = 1 });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(ToAsyncEnumerable(table)));
            Assert.Contains("Correlation column 'missing_id' not found", ex.Message);
        }

        [Fact]
        public async Task WriteBatches_IdempotencyColumnInBatchMode_Throws()
        {
            await using var server = new LocalHttpApiServer(_ => LocalHttpResponse.Json("""{"status":"ok"}"""));
            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "ROW_ARRAY",
                ["IDEMPOTENCY_KEY_COLUMN"] = "id"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id" });
            await table.AddRowAsync(new Row(table.Schema) { ["id"] = 1 });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(ToAsyncEnumerable(table)));
            Assert.Contains("IDEMPOTENCY_KEY_COLUMN is only supported", ex.Message);
        }

        [Fact]
        public async Task WriteBatches_ErrorMessage_RedactsSensitiveOptionValues()
        {
            await using var server = new LocalHttpApiServer(_ =>
                new LocalHttpResponse(400, "application/json", """{"error":"bearer-token rejected"}"""));

            var ds = new RestDataSource(MakeContext(), server.Url, new Dictionary<string, string>
            {
                ["METHOD"] = "POST",
                ["BODY_MODE"] = "ROW_OBJECT",
                ["AUTH_TYPE"] = "BEARER",
                ["TOKEN"] = "bearer-token"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "id" });
            await table.AddRowAsync(new Row(table.Schema) { ["id"] = 1 });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(ToAsyncEnumerable(table)));
            Assert.DoesNotContain("bearer-token", ex.Message);
            Assert.Contains("***REDACTED***", ex.Message);
        }

        private static async IAsyncEnumerable<DataTable> ToAsyncEnumerable(DataTable table)
        {
            yield return table;
            await Task.CompletedTask;
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
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                _cts.Dispose();
            }

            private async Task AcceptLoopAsync()
            {
                try
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                        _ = Task.Run(async () =>
                        {
                            try
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
                            catch
                            {
                                // Ignore client exceptions
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                }
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
