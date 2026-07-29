using System.Net;
using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Connectors.Portal;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Connectors;

public class OrchestratorDataSourceConnectionCatalogTests
{
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

    private static LiteralExpression Literal(string value) =>
        new(value, TokenType.STRING_LITERAL);

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
}
