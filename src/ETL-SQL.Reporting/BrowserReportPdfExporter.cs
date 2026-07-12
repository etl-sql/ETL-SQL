using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Reporting
{
    public sealed class BrowserReportPdfExporter : IReportPdfExporter
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public byte[] Export(ReportManifest manifest, PdfExportOptions? options = null)
        {
            options ??= PdfExportOptions.Static;
            if (string.IsNullOrWhiteSpace(options.Host))
                throw new InvalidOperationException("Browser-backed PDF export requires a HOST URL.");

            return ExportAsync(options).GetAwaiter().GetResult();
        }

        public Task<byte[]> ExportAsync(ReportManifest manifest, PdfExportOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= PdfExportOptions.Static;
            if (string.IsNullOrWhiteSpace(options.Host))
                throw new InvalidOperationException("Browser-backed PDF export requires a HOST URL.");

            return ExportAsync(options, cancellationToken);
        }

        private static async Task<byte[]> ExportAsync(PdfExportOptions options, CancellationToken cancellationToken = default)
        {
            var browserPath = ResolveBrowserPath(options.BrowserPath);
            var port = GetFreeTcpPort();
            var userDataDir = Path.Combine(Path.GetTempPath(), "etl-sql-browser-pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userDataDir);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            var token = linkedCts.Token;
            using var process = StartBrowser(browserPath, port, userDataDir);

            try
            {
                var wsUrl = await WaitForPageWebSocketAsync(port, token);
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(wsUrl), token);

                var cdp = new CdpClient(socket);
                await cdp.SendAsync("Page.enable", null, token);
                await cdp.SendAsync("Runtime.enable", null, token);

                if (options.RequestHeaders is { Count: > 0 })
                {
                    await cdp.SendAsync("Network.enable", null, token);
                    await cdp.SendAsync("Network.setExtraHTTPHeaders", new { headers = options.RequestHeaders }, token);
                }

                await cdp.SendAsync("Page.navigate", new { url = options.Host }, token);
                await cdp.WaitForEventAsync("Page.loadEventFired", token);
                await cdp.SendAsync("Runtime.evaluate", new
                {
                    expression =
                        "window.__etlSqlReportWhenExportReady ? " +
                        "window.__etlSqlReportWhenExportReady(30000).then(() => true) : true",
                    awaitPromise = true,
                    returnByValue = true
                }, token);

                var printResult = await cdp.SendAsync("Page.printToPDF", new
                {
                    printBackground = true,
                    preferCSSPageSize = true,
                    marginTop = 0.25,
                    marginBottom = 0.25,
                    marginLeft = 0.25,
                    marginRight = 0.25
                }, token);

                var base64 = printResult.RootElement.GetProperty("result").GetProperty("data").GetString();
                return Convert.FromBase64String(base64 ?? throw new InvalidOperationException("Browser PDF export returned no PDF data."));
            }
            finally
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None);
                    }
                }
                catch { }

                try { Directory.Delete(userDataDir, recursive: true); }
                catch { }
            }
        }

        private static Process StartBrowser(string browserPath, int port, string userDataDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = browserPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("--headless=new");
            psi.ArgumentList.Add("--disable-gpu");
            psi.ArgumentList.Add("--no-first-run");
            psi.ArgumentList.Add("--no-default-browser-check");
            psi.ArgumentList.Add("--disable-extensions");
            psi.ArgumentList.Add("--remote-allow-origins=*");
            psi.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
            psi.ArgumentList.Add($"--remote-debugging-port={port}");
            psi.ArgumentList.Add($"--user-data-dir={userDataDir}");
            psi.ArgumentList.Add("about:blank");

            return Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start configured browser for PDF export.");
        }

        private static async Task<string> WaitForPageWebSocketAsync(int port, CancellationToken ct)
        {
            using var client = PolicyBoundHttp.CreateClient(timeout: TimeSpan.FromSeconds(2));
            var endpoint = $"http://127.0.0.1:{port}/json/list";

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var json = await client.GetStringAsync(endpoint, ct);
                    var pages = JsonSerializer.Deserialize<List<DevToolsPage>>(json, JsonOptions) ?? new();
                    var ws = pages.FirstOrDefault(p => string.Equals(p.Type, "page", StringComparison.OrdinalIgnoreCase))
                        ?.WebSocketDebuggerUrl;
                    if (!string.IsNullOrWhiteSpace(ws))
                        return ws!;
                }
                catch
                {
                    await Task.Delay(150, ct);
                }
            }

            throw new InvalidOperationException("Timed out waiting for the browser DevTools endpoint.");
        }

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static string ResolveBrowserPath(string? configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (File.Exists(configuredPath))
                    return configuredPath;
                throw new InvalidOperationException($"Configured browser path was not found: '{configuredPath}'.");
            }

            var names = OperatingSystem.IsWindows()
                ? new[] { "msedge.exe", "chrome.exe", "chromium.exe" }
                : new[] { "msedge", "google-chrome", "chrome", "chromium", "chromium-browser" };

            foreach (var path in FindOnPath(names).Concat(FindCommonBrowserPaths()))
            {
                if (File.Exists(path))
                    return path;
            }

            throw new InvalidOperationException("No supported Chrome, Edge, or Chromium executable was found. Set BROWSER_PATH to an installed browser.");
        }

        private static IEnumerable<string> FindOnPath(IEnumerable<string> names)
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathValue.Split(Path.PathSeparator).Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                foreach (var name in names)
                    yield return Path.Combine(dir.Trim(), name);
            }
        }

        private static IEnumerable<string> FindCommonBrowserPaths()
        {
            if (!OperatingSystem.IsWindows())
                yield break;

            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };

            foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
                yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(root, "Chromium", "Application", "chrome.exe");
            }
        }

        private sealed record DevToolsPage(string? Type, string? WebSocketDebuggerUrl);

        private sealed class CdpClient(ClientWebSocket socket)
        {
            private int _nextId;
            private readonly List<string> _pendingEvents = new();

            public async Task<JsonDocument> SendAsync(string method, object? parameters, CancellationToken ct)
            {
                var id = Interlocked.Increment(ref _nextId);
                var message = parameters == null
                    ? JsonSerializer.Serialize(new { id, method })
                    : JsonSerializer.Serialize(new { id, method, @params = parameters });
                await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct);

                while (true)
                {
                    var doc = await ReceiveAsync(ct);
                    if (doc.RootElement.TryGetProperty("id", out var responseId)
                        && responseId.GetInt32() == id)
                    {
                        if (doc.RootElement.TryGetProperty("error", out var error))
                            throw new InvalidOperationException(error.ToString());
                        return doc;
                    }

                    BufferEvent(doc);
                    doc.Dispose();
                }
            }

            public async Task WaitForEventAsync(string method, CancellationToken ct)
            {
                while (true)
                {
                    lock (_pendingEvents)
                    {
                        var index = _pendingEvents.FindIndex(e => string.Equals(e, method, StringComparison.Ordinal));
                        if (index >= 0)
                        {
                            _pendingEvents.RemoveAt(index);
                            return;
                        }
                    }

                    using var doc = await ReceiveAsync(ct);
                    if (doc.RootElement.TryGetProperty("method", out var eventName)
                        && string.Equals(eventName.GetString(), method, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }

            private void BufferEvent(JsonDocument doc)
            {
                if (!doc.RootElement.TryGetProperty("method", out var eventName))
                    return;

                var method = eventName.GetString();
                if (string.IsNullOrWhiteSpace(method))
                    return;

                lock (_pendingEvents)
                {
                    _pendingEvents.Add(method!);
                }
            }

            private async Task<JsonDocument> ReceiveAsync(CancellationToken ct)
            {
                var buffer = new byte[64 * 1024];
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new InvalidOperationException("Browser DevTools connection closed before PDF export completed.");
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                return JsonDocument.Parse(ms.ToArray());
            }
        }
    }
}
