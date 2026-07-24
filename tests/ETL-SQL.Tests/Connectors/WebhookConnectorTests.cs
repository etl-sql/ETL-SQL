using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Webhook;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    /// <summary>
    /// Webhook connector: payload shape per FORMAT, retry policy, redirect handling with
    /// per-hop egress re-validation, and URL-as-credential redaction. Uses an in-memory
    /// HTTP transport via <see cref="PolicyBoundHttp.CreateClient(HttpMessageHandler, TimeSpan?, Uri?)"/> —
    /// no sockets.
    /// </summary>
    public sealed class WebhookConnectorTests : IDisposable
    {
        private const string SecretUrl = "https://hooks.example.com/services/T000/B000/secrettoken";

        public void Dispose() => EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        // ── Payload shaping ────────────────────────────────────────────────

        [Fact]
        public async Task SlackFormat_PostsTextPayload()
        {
            var handler = new RecordingHandler();
            var ds = CreateDataSource(handler, new() { ["URL"] = SecretUrl, ["FORMAT"] = "slack" });

            await WriteRowAsync(ds, new Row { ["Text"] = "load complete" });

            var request = Assert.Single(handler.Requests);
            Assert.Equal(SecretUrl, request.Uri.ToString());
            Assert.Equal("application/json", request.ContentType);
            using var doc = JsonDocument.Parse(request.Body);
            Assert.Equal("load complete", doc.RootElement.GetProperty("text").GetString());
        }

        [Fact]
        public async Task TeamsFormat_PostsMessageCardWithTitleAndText()
        {
            var handler = new RecordingHandler();
            var ds = CreateDataSource(handler, new() { ["URL"] = SecretUrl, ["FORMAT"] = "teams" });

            await WriteRowAsync(ds, new Row { ["Title"] = "DQ alert", ["Text"] = "3 rows quarantined" });

            using var doc = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
            Assert.Equal("MessageCard", doc.RootElement.GetProperty("@type").GetString());
            Assert.Equal("DQ alert", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("3 rows quarantined", doc.RootElement.GetProperty("text").GetString());
        }

        [Fact]
        public async Task GenericFormat_PostsRowAsJsonObject()
        {
            var handler = new RecordingHandler();
            var ds = CreateDataSource(handler, new() { ["URL"] = SecretUrl });

            await WriteRowAsync(ds, new Row { ["Count"] = 42m, ["Status"] = "ok" });

            using var doc = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
            Assert.Equal(42m, doc.RootElement.GetProperty("Count").GetDecimal());
            Assert.Equal("ok", doc.RootElement.GetProperty("Status").GetString());
        }

        [Fact]
        public async Task BodyTemplate_SubstitutesColumnsJsonEscaped()
        {
            var handler = new RecordingHandler();
            var ds = CreateDataSource(handler, new()
            {
                ["URL"] = SecretUrl,
                ["BODY_TEMPLATE"] = "{\"text\": \"${msg}\"}"
            });

            await WriteRowAsync(ds, new Row { ["msg"] = "he said \"hi\"" });

            using var doc = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
            Assert.Equal("he said \"hi\"", doc.RootElement.GetProperty("text").GetString());
        }

        [Fact]
        public async Task SlackFormat_RowWithoutTextColumn_FallsBackToKeyValuePairs()
        {
            var handler = new RecordingHandler();
            var ds = CreateDataSource(handler, new() { ["URL"] = SecretUrl, ["FORMAT"] = "slack" });

            await WriteRowAsync(ds, new Row { ["Job"] = "import_csv", ["Rows"] = 10m });

            using var doc = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
            var text = doc.RootElement.GetProperty("text").GetString();
            Assert.Contains("Job: import_csv", text);
            Assert.Contains("Rows: 10", text);
        }

        // ── Failure handling & retries ─────────────────────────────────────

        [Fact]
        public async Task NonSuccessStatus_ThrowsExecutionException_WithoutLeakingUrl()
        {
            var handler = new RecordingHandler(Respond(HttpStatusCode.BadRequest, "invalid_payload"));
            var ds = CreateDataSource(handler, new() { ["URL"] = SecretUrl, ["FORMAT"] = "slack" });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                WriteRowAsync(ds, new Row { ["Text"] = "x" }));

            Assert.Contains("400", ex.Message);
            Assert.Contains("hooks.example.com", ex.Message);
            Assert.Contains("invalid_payload", ex.Message);
            Assert.DoesNotContain("secrettoken", ex.Message);
            Assert.DoesNotContain("/services/", ex.Message);
        }

        [Fact]
        public async Task RetryableStatus_RetriesThenSucceeds()
        {
            var handler = new RecordingHandler(
                Respond((HttpStatusCode)429, "rate limited"),
                Respond(HttpStatusCode.OK, "ok"));
            var ds = CreateDataSource(handler, new()
            {
                ["URL"] = SecretUrl,
                ["FORMAT"] = "slack",
                ["RETRY_BACKOFF_MS"] = "1"
            });

            await WriteRowAsync(ds, new Row { ["Text"] = "x" });

            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task NetworkFailure_IsWrappedAsExecutionException_WithoutProviderExceptionChained()
        {
            var handler = new ThrowingHandler(new HttpRequestException("Connection refused (hooks.example.com:443)"));
            var ds = new WebhookDataSource(CreateContext(), SecretUrl,
                new Dictionary<string, string> { ["URL"] = SecretUrl, ["FORMAT"] = "slack" },
                PolicyBoundHttp.CreateClient(handler));

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                WriteRowAsync(ds, new Row { ["Text"] = "x" }));

            Assert.Contains("hooks.example.com", ex.Message);
            Assert.DoesNotContain("secrettoken", ex.Message);
            Assert.Null(ex.InnerException); // Rule 5: provider exceptions are not chained
        }

        [Fact]
        public async Task NonRetryableStatus_DoesNotRetry()
        {
            var handler = new RecordingHandler(
                Respond(HttpStatusCode.InternalServerError, "boom"),
                Respond(HttpStatusCode.OK, "ok"));
            var ds = CreateDataSource(handler, new() { ["URL"] = SecretUrl, ["FORMAT"] = "slack" });

            await Assert.ThrowsAsync<ExecutionException>(() => WriteRowAsync(ds, new Row { ["Text"] = "x" }));

            Assert.Single(handler.Requests); // 5xx retries are opt-in via RETRY_STATUS
        }

        // ── Redirect handling ──────────────────────────────────────────────

        [Fact]
        public async Task PermanentRedirect301_FailsInsteadOfDowngradingToGet()
        {
            var handler = new RecordingHandler(
                Redirect(HttpStatusCode.MovedPermanently, "https://hooks.example.com/new"));
            var ds = CreateDataSource(handler, new() { ["URL"] = SecretUrl, ["FORMAT"] = "slack" });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                WriteRowAsync(ds, new Row { ["Text"] = "x" }));

            Assert.Contains("301", ex.Message);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task TemporaryRedirect307_IsFollowedWithBodyPreserved()
        {
            var handler = new RecordingHandler(
                Redirect(HttpStatusCode.TemporaryRedirect, "https://hooks.example.com/moved"),
                Respond(HttpStatusCode.OK, "ok"));
            var ds = CreateDataSource(handler, new() { ["URL"] = SecretUrl, ["FORMAT"] = "slack" });

            await WriteRowAsync(ds, new Row { ["Text"] = "still delivered" });

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal("https://hooks.example.com/moved", handler.Requests[1].Uri.ToString());
            Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
            Assert.Contains("still delivered", handler.Requests[1].Body);
        }

        [Fact]
        public async Task RedirectHop_ToPolicyDeniedHost_IsRejectedBeforeRequest()
        {
            // Enrolled policy allows only the webhook host; the endpoint tries to bounce the
            // POST (and the payload) to the cloud metadata service.
            EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["hooks.example.com"]));
            var handler = new RecordingHandler(
                Redirect(HttpStatusCode.TemporaryRedirect, "http://169.254.169.254/latest/meta-data/"));
            var ds = CreateDataSource(handler, new() { ["URL"] = SecretUrl, ["FORMAT"] = "slack" });

            await Assert.ThrowsAnyAsync<Exception>(() => WriteRowAsync(ds, new Row { ["Text"] = "x" }));

            var request = Assert.Single(handler.Requests); // the denied hop was never contacted
            Assert.Equal("hooks.example.com", request.Uri.Host);
        }

        [Fact]
        public void DeniedHost_IsRejectedAtCreation()
        {
            EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["hooks.example.com"]));

            Assert.ThrowsAny<Exception>(() =>
                CreateDataSource(new RecordingHandler(), new() { ["URL"] = "https://exfil.example.net/hook" }));
        }

        // ── URL-as-credential redaction ────────────────────────────────────

        [Fact]
        public void GetConfigAndToString_MaskUrlPath()
        {
            var ds = CreateDataSource(new RecordingHandler(), new() { ["URL"] = SecretUrl, ["FORMAT"] = "slack" });

            var config = ds.GetConfig();
            Assert.Contains("hooks.example.com", config["URL"]);
            Assert.DoesNotContain("secrettoken", config["URL"]);
            Assert.DoesNotContain("secrettoken", ds.ToString());
            Assert.Contains("hooks.example.com", ds.ToString());
        }

        [Fact]
        public void SecretReference_OnUrl_IsResolvableForWebhookTypesOnly()
        {
            Assert.True(SecretResolvableFields.IsResolvable("URL", "WEBHOOK"));
            Assert.True(SecretResolvableFields.IsResolvable("URL", "SLACK"));
            Assert.True(SecretResolvableFields.IsResolvable("URL", "TEAMS"));
            Assert.False(SecretResolvableFields.IsResolvable("URL", "API"));
            Assert.False(SecretResolvableFields.IsResolvable("URL", (string?)null));
        }

        // ── Lifecycle & metadata ───────────────────────────────────────────

        [Fact]
        public async Task ReadBatches_YieldsNothing_AndDisposeCompletes()
        {
            var ds = CreateDataSource(new RecordingHandler(), new() { ["URL"] = SecretUrl });

            var batches = new List<DataTable>();
            await foreach (var batch in ds.ReadBatches(100))
                batches.Add(batch);

            Assert.Empty(batches);
            await ds.DisposeAsync();
        }

        [Fact]
        public void Connector_Metadata_FollowsStandards()
        {
            var connector = new WebhookConnector();

            Assert.Equal("WEBHOOK", connector.Name);
            Assert.Equal(new[] { "SLACK", "TEAMS" }, connector.Aliases);
            Assert.All(connector.GetSupportedOptions().Keys,
                key => Assert.Equal(key.ToUpperInvariant(), key)); // Rule 11: UPPERCASE option keys
            Assert.Equal(SecretUrl, connector.BuildConnectionString(
                new Dictionary<string, string> { ["URL"] = SecretUrl }));
            Assert.Equal("hooks.example.com", connector.GetHost("", new() { ["URL"] = SecretUrl }));
        }

        [Fact]
        public void MissingOrRelativeUrl_ThrowsExecutionException()
        {
            Assert.Throws<ExecutionException>(() =>
                CreateDataSource(new RecordingHandler(), new Dictionary<string, string>()));
            Assert.Throws<ExecutionException>(() =>
                CreateDataSource(new RecordingHandler(), new() { ["URL"] = "not-a-url" }));
            Assert.Throws<ExecutionException>(() =>
                CreateDataSource(new RecordingHandler(), new() { ["URL"] = "ftp://hooks.example.com/x" }));
        }

        // ── Harness ────────────────────────────────────────────────────────

        private static WebhookDataSource CreateDataSource(RecordingHandler handler, Dictionary<string, string> options)
        {
            var opts = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
            var url = opts.TryGetValue("URL", out var u) ? u : string.Empty;
            return new WebhookDataSource(CreateContext(), url, opts, PolicyBoundHttp.CreateClient(handler));
        }

        private static IExecutionContext CreateContext()
        {
            var context = new Mock<IExecutionContext>();
            context.SetupGet(c => c.Logger).Returns(NullLogger.Instance);
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            context.SetupGet(c => c.SecurityService).Returns(
                new ETL_SQL.Services.SecurityService(NullLogger.Instance));
            context.SetupGet(c => c.ExecutionPolicy).Returns(ExecutionPolicySnapshot.Capture(
                EnterprisePolicyRuntime.Current, "test", ScriptExecutionMode.Batch, "hash"));
            return context.Object;
        }

        private static async Task WriteRowAsync(WebhookDataSource ds, Row row)
        {
            var table = new DataTable();
            await table.AddRowAsync(row);
            await ds.WriteBatches(new[] { table }.ToAsyncEnumerable(), append: true);
        }

        private static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string body) =>
            _ => new HttpResponseMessage(status) { Content = new StringContent(body) };

        private static Func<HttpRequestMessage, HttpResponseMessage> Redirect(HttpStatusCode status, string location) =>
            _ => new HttpResponseMessage(status)
            {
                Headers = { Location = new Uri(location) },
                Content = new StringContent("")
            };

        private static EffectiveEnterprisePolicy EnrolledPolicy(string[] allowedHosts)
        {
            var document = new OrganizationPolicyDocument
            {
                Connectors = new ConnectorPolicySection { AllowedTypes = [] },
                Network = new NetworkPolicySection { AllowedSchemes = [], AllowedPorts = [] },
                RemoteExecution = new RemoteExecutionPolicySection
                {
                    Mode = RemoteExecutionMode.AllowedHosts,
                    AllowedHosts = allowedHosts
                }
            };
            return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
                DateTimeOffset.UtcNow, document,
                EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
        }

        private sealed record RecordedRequest(Uri Uri, HttpMethod Method, string Body, string? ContentType);

        private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromException<HttpResponseMessage>(exception);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

            public RecordingHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
            {
                _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
            }

            public List<RecordedRequest> Requests { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add(new RecordedRequest(
                    request.RequestUri!,
                    request.Method,
                    body,
                    request.Content?.Headers.ContentType?.MediaType));

                var respond = _responses.Count > 0
                    ? _responses.Dequeue()
                    : static _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
                var response = respond(request);
                response.RequestMessage = request;
                return response;
            }
        }
    }
}
