using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Webhook
{
    /// <summary>
    /// Write-only data source that POSTs each row as a JSON payload to a webhook endpoint.
    /// The URL is a credential (Slack/Teams webhook URLs embed their auth token): it never
    /// appears in logs, exceptions, <c>GetConfig()</c>, or <c>ToString()</c> — only the host does.
    /// </summary>
    public partial class WebhookDataSource : IDataSource
    {
        // Auto-redirect is disabled so redirects are followed explicitly and every target hop is
        // re-validated against the egress policy (SSRF hardening). UseProxy is disabled so an
        // ambient system proxy cannot route around egress controls, and a ConnectCallback
        // re-validates the DNS-resolved address at connect time (DNS-rebinding hardening).
        private static readonly HttpClient _httpClient = PolicyBoundHttp.CreateClient();

        private const int DefaultMaxRedirects = 5;
        private const int DefaultRetryCount = 2;
        private const int DefaultRetryBackoffMs = 500;
        private const int MaxRetryAfterSeconds = 60;
        private const int ErrorBodyMaxChars = 512;
        private static readonly int[] DefaultRetryStatuses = { 408, 429 };

        private readonly string _url;
        private readonly Uri _uri;
        private readonly Dictionary<string, string> _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly int _timeoutSeconds;
        private readonly HttpClient _http;

        public WebhookDataSource(IExecutionContext context, string url, Dictionary<string, string>? options = null)
            : this(context, url, options, _httpClient)
        {
        }

        // Test seam: lets tests supply a PolicyBoundHttp.CreateClient(handler) in-memory transport.
        internal WebhookDataSource(IExecutionContext context, string url, Dictionary<string, string>? options, HttpClient httpClient)
        {
            _http = httpClient;
            _context = context;
            _options = options ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _logger = context.Logger;
            _timeoutSeconds = ConnectorTimeouts.ResolveCommandTimeoutSeconds(context, options);

            _url = !string.IsNullOrWhiteSpace(url)
                ? url
                : (_options.TryGetValue("URL", out var optionUrl) ? optionUrl : string.Empty);

            if (!Uri.TryCreate(_url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ExecutionException("WEBHOOK requires an absolute http(s) URL (e.g. URL = 'https://hooks.example.com/...').");
            }
            _uri = uri;

            // Security Hardening: egress control (local guardrail + enterprise host/scheme/port/range policy)
            ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(context, _uri);
        }

        public string Path => _url;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "WEBHOOK";

        // SHOW CONNECTIONS prints ToString(); the URL path embeds the auth token, so only
        // scheme + host are ever shown.
        public override string ToString() => $"WEBHOOK: {RedactedEndpoint}";

        private string RedactedEndpoint => $"{_uri.Scheme}://{_uri.Host}/{SecretRedactor.Mask}";

        /// <summary>Options with the URL masked down to scheme + host (the path is the credential).</summary>
        public IReadOnlyDictionary<string, string> GetConfig()
        {
            var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _options)
            {
                config[kv.Key] = kv.Key.Equals("URL", StringComparison.OrdinalIgnoreCase)
                    ? RedactedEndpoint
                    : SecretRedactor.IsSensitiveKey(kv.Key) || SecretRedactor.LooksSensitiveValue(kv.Value)
                        ? SecretRedactor.Mask
                        : SecretRedactor.Redact(kv.Value) ?? string.Empty;
            }
            if (!config.ContainsKey("URL")) config["URL"] = RedactedEndpoint;
            return config;
        }

        /// <summary>Reading is not supported for webhooks; yields nothing.</summary>
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ReadBatches(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield break; // Write-only sink
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        {
            var ct = EffectiveCancellationToken(cancellationToken);
            long delivered = 0;
            await foreach (var batch in batches.WithCancellation(ct))
            {
                foreach (var row in batch.Rows)
                {
                    ct.ThrowIfCancellationRequested();
                    await PostRowAsync(row, ct);
                    delivered++;
                }
            }
            _logger.Debug("WEBHOOK: delivered {Count} payload(s) to {Host}.", delivered, _uri.Host);
        }

        private async Task PostRowAsync(Row row, CancellationToken ct)
        {
            var (body, contentType) = BuildPayload(row);

            int retryCount = GetIntOption("RETRY_COUNT", DefaultRetryCount, min: 0);
            int backoffMs = GetIntOption("RETRY_BACKOFF_MS", DefaultRetryBackoffMs, min: 0);
            var retryStatuses = GetRetryStatuses();

            for (int attempt = 0; ; attempt++)
            {
                HttpResponseMessage response;
                try
                {
                    response = await SendWithRedirectsAsync(body, contentType, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    // No response was received; the message may not contain the URL path, but
                    // redact defensively and never chain the provider exception (Rule 3/5).
                    throw new ExecutionException(
                        $"Webhook delivery to '{_uri.Host}' failed: {SecretRedactor.Redact(ex.Message)}");
                }

                using (response)
                {
                    int status = (int)response.StatusCode;
                    if (status is >= 200 and < 300) return;

                    if (attempt < retryCount && retryStatuses.Contains(status))
                    {
                        int delayMs = GetRetryAfterMs(response) ?? backoffMs * (1 << attempt);
                        _logger.Debug("WEBHOOK: {Host} responded {Status}; retrying in {Delay} ms (attempt {Attempt}/{Max}).",
                            _uri.Host, status, delayMs, attempt + 1, retryCount);
                        await Task.Delay(delayMs, ct);
                        continue;
                    }

                    var snippet = await ReadErrorSnippetAsync(response, ct);
                    throw new ExecutionException(
                        $"Webhook endpoint '{_uri.Host}' rejected the payload with HTTP {status}{snippet}.");
                }
            }
        }

        /// <summary>
        /// Sends the payload with automatic redirects disabled, following only 307/308 manually —
        /// those preserve the POST body. Every hop is re-validated against the egress policy so an
        /// allowed endpoint cannot bounce the request to a blocked internal host. A 301/302/303
        /// fails the statement: the HTTP convention downgrades it to a body-less GET, which would
        /// silently turn "delivered" into "not delivered".
        /// </summary>
        private async Task<HttpResponseMessage> SendWithRedirectsAsync(byte[] body, string contentType, CancellationToken ct)
        {
            int maxRedirects = GetIntOption("MAX_REDIRECTS", DefaultMaxRedirects, min: 0);
            var target = _uri;
            int redirects = 0;

            while (true)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

                var request = new HttpRequestMessage(HttpMethod.Post, target);
                var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                request.Content = content;

                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    request.Dispose();
                    throw new ExecutionException($"Webhook request to '{target.Host}' timed out after {_timeoutSeconds}s.");
                }
                finally
                {
                    request.Dispose();
                }

                if (!IsRedirectStatus(response.StatusCode)) return response;

                var location = response.Headers.Location;
                if (location == null) return response; // Redirect without Location — hand it back as a failure status.

                if (redirects >= maxRedirects)
                {
                    response.Dispose();
                    throw new ExecutionException(
                        $"Webhook request exceeded the maximum of {maxRedirects} redirect(s) (possible redirect loop).");
                }

                if (response.StatusCode is not (HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                {
                    int status = (int)response.StatusCode;
                    response.Dispose();
                    throw new ExecutionException(
                        $"Webhook endpoint '{target.Host}' redirected with HTTP {status}, which does not preserve the POST body. " +
                        "Update the connection URL to the endpoint's new address.");
                }

                var next = location.IsAbsoluteUri ? location : new Uri(target, location);
                if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps)
                {
                    response.Dispose();
                    throw new ExecutionException($"Webhook redirect to unsupported scheme '{next.Scheme}' was blocked.");
                }

                // Re-validate every hop against the egress policy before following it.
                if (_context != null) ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(_context, next);

                response.Dispose();
                target = next;
                redirects++;
            }
        }

        private static bool IsRedirectStatus(HttpStatusCode status) =>
            status is HttpStatusCode.MovedPermanently      // 301
                   or HttpStatusCode.Found                 // 302
                   or HttpStatusCode.SeeOther              // 303
                   or HttpStatusCode.TemporaryRedirect     // 307
                   or HttpStatusCode.PermanentRedirect;    // 308

        private (byte[] Body, string ContentType) BuildPayload(Row row)
        {
            if (_options.TryGetValue("BODY_TEMPLATE", out var template) && !string.IsNullOrEmpty(template))
            {
                var rendered = TemplatePlaceholderRegex().Replace(template, match =>
                {
                    var value = GetColumn(row, match.Groups[1].Value);
                    return JsonEscape(value?.ToString() ?? string.Empty);
                });
                return (Encoding.UTF8.GetBytes(rendered), "application/json");
            }

            var format = _options.TryGetValue("FORMAT", out var f) ? f.Trim() : "GENERIC";
            object payload = format.ToUpperInvariant() switch
            {
                "SLACK" => new Dictionary<string, object?> { ["text"] = ResolveText(row) },
                "TEAMS" => BuildTeamsCard(row),
                _ => RowAsDictionary(row)
            };

            return (JsonSerializer.SerializeToUtf8Bytes(payload), "application/json");
        }

        private static Dictionary<string, object?> BuildTeamsCard(Row row)
        {
            var card = new Dictionary<string, object?>
            {
                ["@type"] = "MessageCard",
                ["@context"] = "https://schema.org/extensions",
                ["summary"] = GetColumn(row, "Title")?.ToString() ?? ResolveText(row),
                ["text"] = ResolveText(row)
            };
            if (GetColumn(row, "Title") is { } title) card["title"] = title.ToString();
            return card;
        }

        private static string ResolveText(Row row) =>
            GetColumn(row, "Text")?.ToString()
            ?? string.Join("; ", row.Columns.Select(kv => $"{kv.Key}: {kv.Value}"));

        private static Dictionary<string, object?> RowAsDictionary(Row row) =>
            row.Columns.ToDictionary(kv => kv.Key, kv => kv.Value);

        private static object? GetColumn(Row row, string name)
        {
            if (row.Columns.TryGetValue(name, out var exact)) return exact;
            var key = row.Columns.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
            return key != null ? row.Columns[key] : null;
        }

        private static string JsonEscape(string value)
        {
            // Escaped-but-unquoted so ${col} can sit inside a JSON string in the template.
            var quoted = JsonSerializer.Serialize(value);
            return quoted[1..^1];
        }

        private int GetIntOption(string key, int fallback, int min)
        {
            if (_options.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed) && parsed >= min)
                return parsed;
            return fallback;
        }

        private HashSet<int> GetRetryStatuses()
        {
            if (_options.TryGetValue("RETRY_STATUS", out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                var parsed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => int.TryParse(s, out var v) ? v : -1)
                    .Where(v => v > 0);
                return new HashSet<int>(parsed);
            }
            return new HashSet<int>(DefaultRetryStatuses);
        }

        private static int? GetRetryAfterMs(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter == null) return null;
            var delay = retryAfter.Delta
                ?? (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            if (delay is not { } d || d <= TimeSpan.Zero) return null;
            return (int)Math.Min(d.TotalMilliseconds, MaxRetryAfterSeconds * 1000d);
        }

        private static async Task<string> ReadErrorSnippetAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(text)) return string.Empty;
                if (text.Length > ErrorBodyMaxChars) text = text[..ErrorBodyMaxChars] + "…";
                return $": {SecretRedactor.Redact(text)}";
            }
            catch
            {
                return string.Empty;
            }
        }

        public Task TruncateAsync() => Task.CompletedTask;

        public Task<IEnumerable<string>> GetColumnsAsync() =>
            Task.FromResult(new[] { "Title", "Text" }.AsEnumerable());

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask; // Shared static HttpClient; nothing per-instance to release.
        }

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);

        [GeneratedRegex(@"\$\{(\w+)\}")]
        private static partial Regex TemplatePlaceholderRegex();
    }
}
