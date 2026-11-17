using System.Net;
using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Connectors.Portal;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Connectors;

public class OrchestratorDataSourceConnectionCatalogTests
{
    [Theory]
    [InlineData("eng.data_quality_status", "/api/data-quality/status", "run_id", "42")]
    [InlineData("eng.data_quality_failures", "/api/data-quality/failures", "column_name", "email")]
    public async Task RemoteDataQualityCatalog_ReadsOrchestratorEndpoints(
        string tableName, string endpoint, string expectedColumn, string expectedValue)
    {
        var requested = "";
        var presentedKey = "";
        var handler = new RecordingHandler(request =>
        {
            requested = request.RequestUri?.AbsolutePath ?? "";
            presentedKey = request.Headers.TryGetValues("X-Orchestrator-Key", out var values)
                ? values.Single() : "";
            return tableName.EndsWith("status", StringComparison.Ordinal)
                ? JsonResponse("""[{"runId":"42","jobName":"nightly","startTime":"2026-01-01T00:00:00Z","status":"FAILED","rowsProcessed":100,"rowsWarned":5,"rowsQuarantined":2,"failedRuleCount":1,"freshnessState":"NOT_TRACKED"}]""")
                : JsonResponse("""[{"runId":42,"jobName":"nightly","startTime":"2026-01-01T00:00:00Z","status":"FAILED","columnName":"email","rule":"NOT NULL","action":"WARN","failureCount":5}]""");
        });
        await using var source = new OrchestratorDataSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://orchestrator.test/") },
            "key", NullLogger.Instance);

        DataTable? result = null;
        await foreach (var batch in source.WithTable(tableName).ReadBatches()) result = batch;

        Assert.Equal(endpoint, requested);
        Assert.Equal("key", presentedKey);
        Assert.Equal(expectedValue, Assert.Single(result!.Rows)[expectedColumn]?.ToString());
    }

    [Theory]
    [InlineData("eng.stewardship_score", "/api/stewardship/score", "component", "required_tag_completeness")]
    [InlineData("eng.stewardship_gaps", "/api/stewardship/gaps", "requirement", "@owner")]
    public async Task RemoteStewardshipCatalog_UsesVersionedServiceContract(
        string tableName, string endpoint, string expectedColumn, string expectedValue)
    {
        var requested = "";
        var handler = new RecordingHandler(request =>
        {
            requested = request.RequestUri?.AbsolutePath ?? "";
            return tableName.EndsWith("score", StringComparison.Ordinal)
                ? JsonResponse("""[{"scopeType":"GLOBAL","scopeName":"*","component":"required_tag_completeness","numerator":1,"denominator":2,"percentage":50,"assetCount":1,"columnCount":1,"weight":1,"evaluatedAtUtc":"2026-08-02T12:00:00Z","definitionVersion":"1.0"}]""")
                : JsonResponse("""[{"scopeType":"GLOBAL","scopeName":"*","component":"required_tag_completeness","targetTable":"customers","requirement":"@owner","sourceFile":"pipelines/customers.etlsql","line":8,"evaluatedAtUtc":"2026-08-02T12:00:00Z","definitionVersion":"1.0"}]""");
        });
        await using var source = new OrchestratorDataSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://orchestrator.test/") },
            "key", NullLogger.Instance);

        DataTable? result = null;
        await foreach (var batch in source.WithTable(tableName).ReadBatches()) result = batch;

        Assert.Equal(endpoint, requested);
        Assert.Equal(expectedValue, Assert.Single(result!.Rows)[expectedColumn]?.ToString());
        Assert.Equal("1.0", result.Rows[0]["definition_version"]?.ToString());
    }

    [Fact]
    public async Task RemoteStewardshipCatalog_PreservesSharedServiceTotalsAndGaps()
    {
        var policy = new WorkspacePolicyDocument
        {
            RequiredTags = [new WorkspaceRequiredTagRule { Tag = "@owner", Scopes = ["COLUMN"] }]
        };
        var evaluation = StewardshipScoring.Evaluate(
        [
            new StewardshipAsset("nightly", "customers", "email",
                new Dictionary<string, string> { ["pii"] = "true" }, "pipelines/customers.etlsql", 8)
        ], policy, new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var handler = new RecordingHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/stewardship/score" => JsonResponse(JsonSerializer.Serialize(evaluation.Scores, jsonOptions)),
            "/api/stewardship/gaps" => JsonResponse(JsonSerializer.Serialize(evaluation.Gaps, jsonOptions)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        await using var source = new OrchestratorDataSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://orchestrator.test/") },
            "key", NullLogger.Instance);

        DataTable? scores = null;
        DataTable? gaps = null;
        await foreach (var batch in source.WithTable("eng.stewardship_score").ReadBatches()) scores = batch;
        await foreach (var batch in source.WithTable("eng.stewardship_gaps").ReadBatches()) gaps = batch;

        Assert.Equal(evaluation.Scores.Count, scores!.Rows.Count);
        Assert.Equal(evaluation.Gaps.Count, gaps!.Rows.Count);
        foreach (var score in scores.Rows)
        {
            var missing = Convert.ToInt32(score["denominator"]) - Convert.ToInt32(score["numerator"]);
            Assert.Equal(missing, gaps.Rows.Count(g =>
                g["scope_type"]?.ToString() == score["scope_type"]?.ToString()
                && g["scope_name"]?.ToString() == score["scope_name"]?.ToString()
                && g["component"]?.ToString() == score["component"]?.ToString()));
        }
    }

    [Fact]
    public async Task PortalEngSubscriptions_ReadsSubscriptionCatalogEndpoint()
    {
        var requests = new List<string>();
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/auth/login")
                return JsonResponse("""{"token":"token","refreshToken":"refresh","expiresAt":"2099-01-01T00:00:00Z"}""");

            requests.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            return JsonResponse("""[{"id":7,"name":"Daily Sales","isActive":true}]""");
        });
        await using var portal = new PortalDataSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://portal.test/") },
            "admin",
            "password",
            NullLogger.Instance);

        DataTable? result = null;
        await foreach (var batch in portal.WithTable("eng.subscriptions").ReadBatches())
            result = batch;

        Assert.Equal(new[] { "/api/subscriptions" }, requests);
        Assert.NotNull(result);
        Assert.Single(result!.Rows);
        Assert.Equal("Daily Sales", result.Rows[0]["name"]);
    }

    [Fact]
    public async Task WhatIf_SkipsPortalAndOrchestratorConnectionMutations()
    {
        var portalRequests = new List<HttpRequestMessage>();
        var portalHandler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/auth/login")
                return JsonResponse("""{"token":"token","refreshToken":"refresh","expiresAt":"2099-01-01T00:00:00Z"}""");

            portalRequests.Add(CloneRequest(request));
            return request.Method == HttpMethod.Get
                ? JsonResponse("""
                  {
                    "alias": "notify_smtp",
                    "connectorType": "SMTP",
                    "target": null,
                    "options": { "HOST": "smtp.example.test" },
                    "status": "active"
                  }
                  """)
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await using (var portal = new PortalDataSource(
            new HttpClient(portalHandler) { BaseAddress = new Uri("http://portal.test/") },
            "admin",
            "password",
            NullLogger.Instance))
        {
            await ExerciseWhatIfConnectionMutationsAsync(portal);
            await ExerciseWhatIfWebhookConnectionMutationsAsync(portal);
        }

        Assert.DoesNotContain(portalRequests, r => r.Method is { } m && (m == HttpMethod.Put || m == HttpMethod.Delete));

        var orchestratorRequests = new List<HttpRequestMessage>();
        var orchestratorHandler = new RecordingHandler(request =>
        {
            orchestratorRequests.Add(CloneRequest(request));
            return request.Method == HttpMethod.Get
                ? JsonResponse("""
                  {
                    "alias": "notify_smtp",
                    "connectorType": "SMTP",
                    "target": null,
                    "options": { "HOST": "smtp.example.test" },
                    "status": "active"
                  }
                  """)
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await using (var orchestrator = new OrchestratorDataSource(
            new HttpClient(orchestratorHandler) { BaseAddress = new Uri("http://orchestrator.test/") },
            "key",
            NullLogger.Instance))
        {
            await ExerciseWhatIfConnectionMutationsAsync(orchestrator);
            await ExerciseWhatIfWebhookConnectionMutationsAsync(orchestrator);
        }

        Assert.DoesNotContain(orchestratorRequests, r => r.Method is { } m && (m == HttpMethod.Put || m == HttpMethod.Delete));
    }

    [Fact]
    public async Task PortalExecuteAdminStatement_DispatchesAlterAndTestConnectionLifecycle()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/auth/login")
                return JsonResponse("""{"token":"token","refreshToken":"refresh","expiresAt":"2099-01-01T00:00:00Z"}""");

            requests.Add(CloneRequest(request));
            return request.Method.Method switch
            {
                "GET" when request.RequestUri?.AbsolutePath == "/api/admin/connections/notify_smtp" =>
                    JsonResponse("""
                    {
                      "alias": "notify_smtp",
                      "connectorType": "SMTP",
                      "target": null,
                      "options": { "HOST": "smtp.example.test", "PASSWORD": "SECRET:smtp_password" },
                      "status": "active"
                    }
                    """),
                "PUT" => new HttpResponseMessage(HttpStatusCode.NoContent),
                "POST" when request.RequestUri?.AbsolutePath == "/api/admin/connections/notify_smtp/test" =>
                    JsonResponse("""
                    {
                      "alias": "notify_smtp",
                      "succeeded": true,
                      "steps": [
                        { "layer": "POLICY", "status": "ok", "detail": "Destination permitted.", "remedy": null }
                      ]
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://portal.test/") };
        await using var source = new PortalDataSource(http, "admin", "password", NullLogger.Instance);
        var context = new LiteralEvalContext();

        await source.ExecuteAdminStatementAsync(new AlterConnectionStatement(
            "notify_smtp",
            type: null,
            target: null,
            new Dictionary<string, Expression>
            {
                ["USER"] = Literal("etl")
            }), context);
        await source.ExecuteAdminStatementAsync(new TestConnectionStatement("notify_smtp"), context);
        await source.ExecuteAdminStatementAsync(new ShowConnectionConfigStatement("notify_smtp"), context);

        Assert.Equal(HttpMethod.Get, requests[0].Method);
        Assert.Equal("/api/admin/connections/notify_smtp", requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Put, requests[1].Method);
        Assert.Contains("\"USER\":\"etl\"", await requests[1].Content!.ReadAsStringAsync());
        Assert.Equal(HttpMethod.Post, requests[2].Method);
        Assert.Equal("/api/admin/connections/notify_smtp/test", requests[2].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, requests[3].Method);
        Assert.Equal("/api/admin/connections/notify_smtp", requests[3].RequestUri!.AbsolutePath);
        Assert.Contains(context.LastResult!.Rows, r =>
            r["Option"]?.ToString() == "PASSWORD"
            && r["Value"]?.ToString() == "SECRET:smtp_password");
    }

    [Fact]
    public async Task ExecuteAdminStatement_DispatchesConnectionLifecycleToOrchestratorApi()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new RecordingHandler(request =>
        {
            requests.Add(CloneRequest(request));
            return request.Method.Method switch
            {
                "PUT" => new HttpResponseMessage(HttpStatusCode.NoContent),
                "GET" when request.RequestUri?.AbsolutePath == "/api/admin/connections" =>
                    JsonResponse("""
                    [
                      {
                        "alias": "notify_smtp",
                        "connectorType": "SMTP",
                        "target": null,
                        "options": { "HOST": "smtp.example.test", "PASSWORD": "SECRET:smtp_password" },
                        "status": "active"
                      }
                    ]
                    """),
                "GET" => JsonResponse("""
                    {
                      "alias": "notify_smtp",
                      "connectorType": "SMTP",
                      "target": null,
                      "options": { "HOST": "smtp.example.test", "PASSWORD": "SECRET:smtp_password" },
                      "status": "active"
                    }
                    """),
                "POST" when request.RequestUri?.AbsolutePath == "/api/admin/connections/notify_smtp/test" =>
                    JsonResponse("""
                    {
                      "alias": "notify_smtp",
                      "succeeded": true,
                      "steps": [
                        { "layer": "POLICY", "status": "ok", "detail": "Destination permitted.", "remedy": null }
                      ]
                    }
                    """),
                "DELETE" => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://orchestrator.test/") };
        await using var source = new OrchestratorDataSource(http, "key", NullLogger.Instance);
        var context = new LiteralEvalContext();

        var create = new CreateConnectionStatement(
            "notify_smtp",
            "SMTP",
            target: null,
            new Dictionary<string, Expression>
            {
                ["HOST"] = Literal("smtp.example.test"),
                ["PASSWORD"] = Literal("SECRET:smtp_password")
            });
        await source.ExecuteAdminStatementAsync(create, context);
        await source.ExecuteAdminStatementAsync(new AlterConnectionStatement(
            "notify_smtp",
            type: null,
            target: null,
            new Dictionary<string, Expression>
            {
                ["USER"] = Literal("etl")
            }), context);
        await source.ExecuteAdminStatementAsync(new ShowConnectionsStatement(), context);
        await source.ExecuteAdminStatementAsync(new TestConnectionStatement("notify_smtp"), context);
        await source.ExecuteAdminStatementAsync(new ShowConnectionConfigStatement("notify_smtp"), context);
        await source.ExecuteAdminStatementAsync(new DropConnectionStatement("notify_smtp", ifExists: false), context);

        Assert.Equal(HttpMethod.Put, requests[0].Method);
        Assert.Equal("/api/admin/connections/notify_smtp", requests[0].RequestUri!.AbsolutePath);
        var body = await requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"ConnectorType\":\"SMTP\"", body);
        Assert.Contains("\"PASSWORD\":\"SECRET:smtp_password\"", body);

        Assert.Equal(HttpMethod.Get, requests[1].Method);
        Assert.Equal("/api/admin/connections/notify_smtp", requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Put, requests[2].Method);
        Assert.Contains("\"USER\":\"etl\"", await requests[2].Content!.ReadAsStringAsync());
        Assert.Equal(HttpMethod.Get, requests[3].Method);
        Assert.Equal("/api/admin/connections", requests[3].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, requests[4].Method);
        Assert.Equal("/api/admin/connections/notify_smtp/test", requests[4].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, requests[5].Method);
        Assert.Equal("/api/admin/connections/notify_smtp", requests[5].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Delete, requests[6].Method);
        Assert.Equal("/api/admin/connections/notify_smtp", requests[6].RequestUri!.AbsolutePath);
        Assert.Contains(context.LastResult!.Rows, r =>
            r["Option"]?.ToString() == "PASSWORD"
            && r["Value"]?.ToString() == "SECRET:smtp_password");
        await source.ExecuteAdminStatementAsync(new ShowConnectionsStatement(), context);
        Assert.Equal("notify_smtp", context.LastResult!.Rows[0]["Alias"]);
        Assert.Equal("SMTP", context.LastResult.Rows[0]["ConnectorType"]);
    }

    [Fact]
    public async Task PortalConnectionAdminErrors_RedactSecretReferencesAndCredentialFields()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/auth/login")
                return JsonResponse("""{"token":"token","refreshToken":"refresh","expiresAt":"2099-01-01T00:00:00Z"}""");

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"error":"bad","PASSWORD":"plain-text","reference":"SECRET:smtp_password"}""")
            };
        });
        await using var source = new PortalDataSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://portal.test/") },
            "admin",
            "password",
            NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            source.ExecuteAdminStatementAsync(CreateSmtpStatement(), new LiteralEvalContext()));

        Assert.DoesNotContain("plain-text", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET:smtp_password", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("********", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrchestratorConnectionAdminErrors_RedactSecretReferencesAndCredentialFields()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"error":"bad","PASSWORD":"plain-text","reference":"SECRET:smtp_password"}""")
        });
        await using var source = new OrchestratorDataSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://orchestrator.test/") },
            "key",
            NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            source.ExecuteAdminStatementAsync(CreateSmtpStatement(), new LiteralEvalContext()));

        Assert.DoesNotContain("plain-text", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET:smtp_password", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("********", ex.Message, StringComparison.Ordinal);
    }

    private static LiteralExpression Literal(string value) =>
        new(value, TokenType.STRING_LITERAL);

    private static CreateConnectionStatement CreateSmtpStatement() =>
        new(
            "notify_smtp",
            "SMTP",
            target: null,
            new Dictionary<string, Expression>
            {
                ["HOST"] = Literal("smtp.example.test"),
                ["PASSWORD"] = Literal("SECRET:smtp_password")
            });

    private static async Task ExerciseWhatIfConnectionMutationsAsync(
        IPortalAdminConnection source)
    {
        var context = new LiteralEvalContext { IsWhatIf = true };
        await source.ExecuteAdminStatementAsync(new CreateConnectionStatement(
            "notify_smtp",
            "SMTP",
            target: null,
            new Dictionary<string, Expression> { ["HOST"] = Literal("smtp.example.test") }), context);
        await source.ExecuteAdminStatementAsync(new AlterConnectionStatement(
            "notify_smtp",
            type: null,
            target: null,
            new Dictionary<string, Expression> { ["USER"] = Literal("etl") }), context);
        await source.ExecuteAdminStatementAsync(new DropConnectionStatement("notify_smtp", ifExists: true), context);
    }

    private static async Task ExerciseWhatIfWebhookConnectionMutationsAsync(
        IPortalAdminConnection source)
    {
        var context = new LiteralEvalContext { IsWhatIf = true };
        await source.ExecuteAdminStatementAsync(new CreateConnectionStatement(
            "notify_webhook",
            "WEBHOOK",
            target: null,
            new Dictionary<string, Expression>
            {
                ["URL"] = Literal("SECRET:webhook_url"),
                ["FORMAT"] = Literal("generic")
            }), context);
        await source.ExecuteAdminStatementAsync(new AlterConnectionStatement(
            "notify_webhook",
            type: null,
            target: null,
            new Dictionary<string, Expression> { ["FORMAT"] = Literal("slack") }), context);
        await source.ExecuteAdminStatementAsync(new DropConnectionStatement("notify_webhook", ifExists: true), context);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is not null)
            clone.Content = new StringContent(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        return clone;
    }

    private sealed class LiteralEvalContext : SystemExecutionContext
    {
        public override ValueTask<object?> EvaluateValue(Expression? expr, Row context, bool decryptSensitive = false) =>
            expr is LiteralExpression literal
                ? new ValueTask<object?>(literal.Value)
                : new ValueTask<object?>(null as object);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    [Fact]
    public async Task Or08StatementParity_RoutesRestEndpointsCorrectly()
    {
        var requests = new List<(HttpMethod Method, string Path, string? Query, string? IfMatch, string Body)>();
        var handler = new RecordingHandler(request =>
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            var ifMatch = request.Headers.TryGetValues("If-Match", out var vals) ? vals.FirstOrDefault() : null;
            var path = request.RequestUri?.AbsolutePath ?? "";
            var query = request.RequestUri?.Query;
            requests.Add((request.Method, path, query, ifMatch, body));

            if (request.Method == HttpMethod.Get && path == "/api/scheduled-jobs")
            {
                return JsonResponse("""
                [
                  {"name":"nightly","script":"SELECT 1;","interval":1,"unit":"HOUR","isEnabled":true,"version":7}
                ]
                """);
            }
            if (request.Method == HttpMethod.Get && path == "/api/scheduled-jobs/nightly/state")
            {
                return JsonResponse("""
                [
                  {"jobName":"nightly","stateKey":"watermark","stateValue":"2026-11-17","updatedAt":"2026-11-17T00:00:00Z"}
                ]
                """);
            }
            if (request.Method == HttpMethod.Post && path == "/api/notifications")
            {
                // Simulate first call conflict so CREATE OR REPLACE falls back to PUT.
                return requests.Count(r => r.Method == HttpMethod.Post && r.Path == "/api/notifications") == 1
                    ? new HttpResponseMessage(HttpStatusCode.Conflict)
                    : JsonResponse("""{"name":"ops_alert"}""");
            }
            if (request.Method == HttpMethod.Delete && path == "/api/schedules/missing_sched")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return JsonResponse("{}");
        });

        await using var source = new OrchestratorDataSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://orchestrator.test/") },
            "key",
            NullLogger.Instance);
        var context = new LiteralEvalContext();

        await source.ExecuteAdminStatementAsync(new CreateNotificationStatement
        {
            Name = "ops_alert",
            ConnectionName = "smtp_main",
            Recipient = "oncall@example.test",
            Mode = ObjectCreationMode.CreateOrReplace
        }, context);
        await source.ExecuteAdminStatementAsync(new AlterCatalogObjectStatement
        {
            Kind = CatalogObjectKind.Notification,
            Name = "ops_alert",
            Recipient = "tier2@example.test"
        }, context);
        await source.ExecuteAdminStatementAsync(new SetCatalogObjectEnabledStatement
        {
            Kind = CatalogObjectKind.Notification,
            Name = "ops_alert",
            IsEnabled = false
        }, context);
        await source.ExecuteAdminStatementAsync(new AlterJobAttachmentStatement
        {
            Action = JobAttachmentAction.Add,
            Kind = CatalogObjectKind.Notification,
            JobName = "nightly",
            TargetName = "ops_alert",
            Trigger = "FAILURE"
        }, context);
        await source.ExecuteAdminStatementAsync(new AlterJobAttachmentStatement
        {
            Action = JobAttachmentAction.Remove,
            Kind = CatalogObjectKind.Notification,
            JobName = "nightly",
            TargetName = "ops_alert",
            Trigger = "FAILURE"
        }, context);
        await source.ExecuteAdminStatementAsync(new AlterCatalogObjectStatement
        {
            Kind = CatalogObjectKind.Schedule,
            Name = "daily_2am",
            Cron = "0 2 * * *",
            TimeZone = "UTC"
        }, context);
        await source.ExecuteAdminStatementAsync(new SetCatalogObjectEnabledStatement
        {
            Kind = CatalogObjectKind.Schedule,
            Name = "daily_2am",
            IsEnabled = true
        }, context);
        await source.ExecuteAdminStatementAsync(new DropCatalogObjectStatement
        {
            Kind = CatalogObjectKind.Schedule,
            Name = "missing_sched",
            IfExists = true
        }, context);
        await source.ExecuteAdminStatementAsync(new DropCatalogObjectStatement
        {
            Kind = CatalogObjectKind.Notification,
            Name = "ops_alert",
            IfExists = false
        }, context);
        await source.ExecuteAdminStatementAsync(new KillJobStatement(new LiteralExpression(42m, TokenType.NUMBER)), context);
        await source.ExecuteAdminStatementAsync(new KillJobStatement(new IdentifierExpression("nightly")), context);
        await source.ExecuteAdminStatementAsync(new ShowJobStateStatement(), context);
        Assert.Equal("watermark", Assert.Single(context.LastResult!.Rows)["StateKey"]?.ToString());

        await source.ExecuteAdminStatementAsync(new DropJobStatement("missing_job", IfExists: true), context);
        await source.ExecuteAdminStatementAsync(new DropJobStatement("nightly", IfExists: false), context);

        Assert.Contains(requests, r => r.Method == HttpMethod.Put && r.Path == "/api/notifications/ops_alert");
        Assert.Contains(requests, r => r.Method == HttpMethod.Put && r.Path == "/api/schedules/daily_2am");
        Assert.Contains(requests, r => r.Method == HttpMethod.Post && r.Path == "/api/scheduled-jobs/nightly/notifications/ops_alert" && r.Body.Contains("FAILURE"));
        Assert.Contains(requests, r => r.Method == HttpMethod.Delete && r.Path == "/api/scheduled-jobs/nightly/notifications/ops_alert" && r.Query == "?trigger=FAILURE");
        Assert.Contains(requests, r => r.Method == HttpMethod.Delete && r.Path == "/api/notifications/ops_alert");
        Assert.Contains(requests, r => r.Method == HttpMethod.Post && r.Path == "/api/job-runs/42/cancel");
        Assert.Contains(requests, r => r.Method == HttpMethod.Post && r.Path == "/api/scheduled-jobs/nightly/kill");
        Assert.Contains(requests, r => r.Method == HttpMethod.Delete && r.Path == "/api/scheduled-jobs/nightly" && r.IfMatch == "\"7\"");

        var beforeWhatIf = requests.Count(r => r.Method != HttpMethod.Get);
        var whatIf = new LiteralEvalContext { IsWhatIf = true };
        await source.ExecuteAdminStatementAsync(new KillJobStatement(new LiteralExpression(42m, TokenType.NUMBER)), whatIf);
        await source.ExecuteAdminStatementAsync(new DropCatalogObjectStatement
        {
            Kind = CatalogObjectKind.Schedule,
            Name = "daily_2am"
        }, whatIf);
        await source.ExecuteAdminStatementAsync(new DropJobStatement("nightly", IfExists: false), whatIf);
        Assert.Equal(beforeWhatIf, requests.Count(r => r.Method != HttpMethod.Get));
    }

    [Fact]
    public async Task AssertJob_HistoricalBaselineAndNotify_UsesOrchestratorRestApi()
    {
        var dispatchedTitle = "";
        var savedAlertState = false;
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            if (request.Method == HttpMethod.Get && path == "/api/scheduled-jobs/nightly/history")
            {
                return JsonResponse("""
                [
                  {"id":1,"jobName":"nightly","startTime":"2026-11-14T00:00:00Z","endTime":"2026-11-14T00:01:00Z","status":"SUCCESS","rowsProcessed":100},
                  {"id":2,"jobName":"nightly","startTime":"2026-11-15T00:00:00Z","endTime":"2026-11-15T00:01:00Z","status":"SUCCESS","rowsProcessed":100},
                  {"id":3,"jobName":"nightly","startTime":"2026-11-16T00:00:00Z","endTime":"2026-11-16T00:01:00Z","status":"SUCCESS","rowsProcessed":100}
                ]
                """);
            }
            if (request.Method == HttpMethod.Get && path == "/api/scheduled-jobs/nightly/state")
                return JsonResponse("[]");
            if (request.Method == HttpMethod.Post && path == "/api/notifications/ops_alert/dispatch")
            {
                dispatchedTitle = JsonDocument.Parse(body).RootElement.GetProperty("Title").GetString() ?? "";
                return JsonResponse("""{"delivered":true}""");
            }
            if (request.Method == HttpMethod.Put && path.StartsWith("/api/scheduled-jobs/nightly/state/dq%3Aassert-alert%3A", StringComparison.Ordinal))
            {
                savedAlertState = true;
                return JsonResponse("{}");
            }
            return JsonResponse("{}");
        });

        await using var source = new OrchestratorDataSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://orchestrator.test/") },
            "key",
            NullLogger.Instance);
        var context = new LiteralEvalContext();
        context.Telemetry.RowsProcessed = 10;

        var script = new Parser(new Lexer(
            "ASSERT JOB nightly (ROW_COUNT WITHIN 0.1 OF HISTORICAL) ON FAILURE NOTIFY ops_alert ON FAILURE THROW;")
            .Tokenize(),
            "ASSERT JOB nightly (ROW_COUNT WITHIN 0.1 OF HISTORICAL) ON FAILURE NOTIFY ops_alert ON FAILURE THROW;")
            .Parse();
        var assertStmt = Assert.IsType<AssertJobStatement>(Assert.Single(script.Statements));

        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            source.ExecuteAdminStatementAsync(assertStmt, context));

        Assert.Contains("ROW_COUNT", ex.Message, StringComparison.Ordinal);
        Assert.Equal("Data quality alert: nightly", dispatchedTitle);
        Assert.True(savedAlertState);
    }
}
