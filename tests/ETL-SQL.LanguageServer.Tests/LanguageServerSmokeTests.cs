using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.LSP;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.LanguageServer.Tests
{
    public class LanguageServerSmokeTests
    {
        // Drives a real LSP initialize handshake against the shipped composition (the same
        // services and handlers Program.Main registers). An empty input stream never sends
        // `initialize`, so LanguageServer.From would wait forever and prove nothing.
        [Fact]
        public async Task Server_Should_Initialize_Without_DI_Errors()
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            var serverTask = OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(
                options => LanguageServerComposition.Configure(
                    options
                        .WithInput(clientToServer.Reader)
                        .WithOutput(serverToClient.Writer)),
                cts.Token);

            await WriteMessageAsync(clientToServer.Writer,
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":null,"rootUri":null,"capabilities":{"textDocument":{"synchronization":{},"hover":{},"definition":{},"rename":{},"completion":{},"signatureHelp":{},"formatting":{},"documentSymbol":{}}}}}""",
                cts.Token);

            using var response = await ReadResponseAsync(serverToClient.Reader, expectedId: 1, cts.Token);
            Assert.False(response.RootElement.TryGetProperty("error", out var error), $"initialize failed: {error}");
            var capabilities = response.RootElement.GetProperty("result").GetProperty("capabilities");

            // Every standard-protocol handler must surface its capability; a handler that
            // failed to resolve or register is missing here.
            foreach (var capability in new[]
            {
                "textDocumentSync", "hoverProvider", "definitionProvider", "renameProvider",
                "completionProvider", "signatureHelpProvider", "documentFormattingProvider",
                "documentSymbolProvider",
            })
            {
                Assert.True(capabilities.TryGetProperty(capability, out var value), $"missing capability {capability} in {capabilities}");
                Assert.NotEqual(JsonValueKind.False, value.ValueKind);
                Assert.NotEqual(JsonValueKind.Null, value.ValueKind);
            }

            await WriteMessageAsync(clientToServer.Writer,
                """{"jsonrpc":"2.0","method":"initialized","params":{}}""", cts.Token);

            var server = await serverTask.WaitAsync(cts.Token);
            Assert.NotNull(server);
            Assert.NotNull(server.Services.GetService<ETL_SQL.Core.IMetadataManager>());

            // The custom-method handlers have no standard capability, so resolve every
            // registered handler type from the server's container directly.
            Assert.Equal(12, LanguageServerComposition.HandlerTypes.Count);
            foreach (var handlerType in LanguageServerComposition.HandlerTypes)
            {
                Assert.NotNull(ActivatorUtilities.GetServiceOrCreateInstance(server.Services, handlerType));
            }

            server.Dispose();
        }

        private static async Task WriteMessageAsync(PipeWriter writer, string json, CancellationToken ct)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await writer.WriteAsync(header, ct);
            await writer.WriteAsync(body, ct);
            await writer.FlushAsync(ct);
        }

        // Reads framed messages until the response with the given id arrives; server-sent
        // notifications (logs, telemetry) are skipped.
        private static async Task<JsonDocument> ReadResponseAsync(PipeReader reader, int expectedId, CancellationToken ct)
        {
            var buffer = new MemoryStream();
            while (true)
            {
                var message = TryTakeMessage(buffer);
                if (message is not null)
                {
                    var doc = JsonDocument.Parse(message);
                    if (doc.RootElement.TryGetProperty("id", out var id)
                        && id.ValueKind == JsonValueKind.Number
                        && id.GetInt32() == expectedId
                        && !doc.RootElement.TryGetProperty("method", out _))
                    {
                        return doc;
                    }

                    doc.Dispose();
                    continue;
                }

                var result = await reader.ReadAsync(ct);
                foreach (var segment in result.Buffer)
                {
                    buffer.Write(segment.Span);
                }

                reader.AdvanceTo(result.Buffer.End);
                if (result.IsCompleted)
                {
                    throw new InvalidOperationException("Server closed its output before responding.");
                }
            }
        }

        private static string TryTakeMessage(MemoryStream buffer)
        {
            var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0)
            {
                return null;
            }

            const string prefix = "Content-Length:";
            var lengthLine = text[..headerEnd].Split("\r\n")[0];
            Assert.StartsWith(prefix, lengthLine, StringComparison.OrdinalIgnoreCase);
            var length = int.Parse(lengthLine[prefix.Length..].Trim());
            var bytes = buffer.GetBuffer();
            var bodyStart = Encoding.UTF8.GetByteCount(text[..(headerEnd + 4)]);
            if (buffer.Length - bodyStart < length)
            {
                return null;
            }

            var body = Encoding.UTF8.GetString(bytes, bodyStart, length);
            var remainder = bytes.AsSpan(bodyStart + length, (int)buffer.Length - bodyStart - length).ToArray();
            buffer.SetLength(0);
            buffer.Write(remainder);
            return body;
        }
    }
}
