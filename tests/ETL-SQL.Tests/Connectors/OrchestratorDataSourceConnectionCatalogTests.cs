using System.Net;
using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Connectors;

public class OrchestratorDataSourceConnectionCatalogTests
{
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
                "GET" => JsonResponse("""{"alias":"notify_smtp","status":"active"}"""),
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
        await source.ExecuteAdminStatementAsync(new ShowConnectionsStatement(), context);
        await source.ExecuteAdminStatementAsync(new DropConnectionStatement("notify_smtp", ifExists: false), context);

        Assert.Equal(HttpMethod.Put, requests[0].Method);
        Assert.Equal("/api/admin/connections/notify_smtp", requests[0].RequestUri!.AbsolutePath);
        var body = await requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"ConnectorType\":\"SMTP\"", body);
        Assert.Contains("\"PASSWORD\":\"SECRET:smtp_password\"", body);

        Assert.Equal(HttpMethod.Get, requests[1].Method);
        Assert.Equal("/api/admin/connections", requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Delete, requests[2].Method);
        Assert.Equal("/api/admin/connections/notify_smtp", requests[2].RequestUri!.AbsolutePath);
        Assert.Equal("notify_smtp", context.LastResult!.Rows[0]["Alias"]);
        Assert.Equal("SMTP", context.LastResult.Rows[0]["ConnectorType"]);
    }

    private static LiteralExpression Literal(string value) =>
        new(value, TokenType.STRING_LITERAL);

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
