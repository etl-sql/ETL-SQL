using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// A pipeline is a catalog document with <c>Kind = Pipeline</c>
/// (docs/architecture/decisions/portal-etl-documents.md). The authoring paths serve it like a
/// report; every surface that exists for readers must refuse it, because running a pipeline writes
/// and belongs to the Orchestrator, not to the report viewer.
/// </summary>
[Trait("Category", "Portal")]
public sealed class CatalogPipelineDocumentTests
{
    private const string Script = "CREATE TABLE #staged (Id INT);\nINSERT INTO #staged VALUES (1);\n";

    [Fact]
    public async Task APipelineIsCreatedListedLeasedSavedAndReopenedInStudio()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var pipeline = await CreatePipelineAsync(client, admin);
        var id = pipeline["id"]!.GetValue<int>();
        Assert.Equal("Pipeline", pipeline["kind"]!.GetValue<string>());

        using (var scope = factory.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Reports.SingleAsync(r => r.Id == id);
            Assert.Equal(CatalogDocumentKind.Pipeline, stored.Kind);
            Assert.EndsWith(".etlsql", stored.ScriptPath, StringComparison.Ordinal);
        }

        var listed = await Json<JsonArray>(await Send(client, HttpMethod.Get, admin, "/api/studio/reports"));
        Assert.Contains(listed, item => item!["id"]!.GetValue<int>() == id && item["kind"]!.GetValue<string>() == "Pipeline");

        var content = await Json<JsonObject>(await Send(client, HttpMethod.Get, admin, $"/api/reports/{id}/script-content"));
        Assert.Equal(Script, content["scriptText"]!.GetValue<string>());
        var version = content["version"]!.GetValue<long>();

        Assert.True((await Send(client, HttpMethod.Post, admin, "/api/designer/lease", new { reportId = id, force = false })).IsSuccessStatusCode);
        var edited = Script + "ASSERT (SELECT COUNT(*) FROM #staged) = 1;\n";
        using (var save = new HttpRequestMessage(HttpMethod.Post, "/api/designer/save") { Content = JsonContent.Create(new { reportId = id, scriptText = edited }) })
        {
            save.Headers.Authorization = new("Bearer", admin);
            save.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            var saved = await client.SendAsync(save);
            Assert.True(saved.IsSuccessStatusCode, $"save: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
        }

        var reopened = await Json<JsonObject>(await Send(client, HttpMethod.Get, admin, $"/api/reports/{id}/script-content"));
        Assert.Equal(edited, reopened["scriptText"]!.GetValue<string>());
        Assert.Equal(version + 1, reopened["version"]!.GetValue<long>());

        // Recovery drafts serve a pipeline too.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Put, admin, $"/api/studio/drafts/{id}", new { content = "-- draft" })).StatusCode);
    }

    [Fact]
    public async Task ReaderSurfacesRefuseAPipeline()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var id = (await CreatePipelineAsync(client, admin))["id"]!.GetValue<int>();

        // Each answers as if there were no such report: a pipeline is not something to view.
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, admin, $"/api/reports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Post, admin, $"/api/reports/{id}/execute", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, admin, $"/api/reports/{id}/manifest")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Post, admin, $"/api/reports/{id}/share-links", new { })).StatusCode);
        var subscription = await Send(client, HttpMethod.Post, admin, "/api/subscriptions",
            new { reportId = id, name = "probe", schedule = "EVERY 1 DAYS", format = "PDF", recipientEmail = "probe@localhost" });
        Assert.True(subscription.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"A subscription to a pipeline was accepted: {(int)subscription.StatusCode}");
    }

    [Fact]
    public async Task AnUnknownKindIsRefused()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var folderId = await CreateFolderAsync(client, admin);
        var response = await Send(client, HttpMethod.Post, admin, "/api/studio/reports",
            new { folderId, name = "Odd", scriptText = Script, kind = "Spreadsheet" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Setup ────────────────────────────────────────────────────────────────

    private static async Task<JsonObject> CreatePipelineAsync(HttpClient client, string admin)
    {
        var folderId = await CreateFolderAsync(client, admin);
        var created = await Send(client, HttpMethod.Post, admin, "/api/studio/reports",
            new { folderId, name = $"Load orders {Guid.NewGuid():N}"[..24], scriptText = Script, kind = "Pipeline" });
        Assert.True(created.IsSuccessStatusCode, $"create: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        return await Json<JsonObject>(created);
    }

    private static async Task<int> CreateFolderAsync(HttpClient client, string admin)
    {
        var folder = await Json<JsonObject>(await Send(client, HttpMethod.Post, admin, "/api/folders",
            new { name = $"pipelines_{Guid.NewGuid():N}"[..20], parentId = (int?)null }));
        return folder["id"]!.GetValue<int>();
    }

    private static async Task<string> AdminTokenAsync(HttpClient client)
    {
        const string initial = "Admin@12345!";
        const string changed = "Admin@Pipelines99!";
        var first = await LoginAsync(client, "admin", initial);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, first, "/api/auth/change-password",
            new { currentPassword = initial, newPassword = changed })).StatusCode);
        return await LoginAsync(client, "admin", changed);
    }

    private static async Task<string> LoginAsync(HttpClient client, string user, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json<JsonObject>(response))["token"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string token, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new("Bearer", token);
        if (method == HttpMethod.Post) await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }

    private static async Task<T> Json<T>(HttpResponseMessage response) where T : JsonNode =>
        (T)(await response.Content.ReadFromJsonAsync<JsonNode>())!;
}
