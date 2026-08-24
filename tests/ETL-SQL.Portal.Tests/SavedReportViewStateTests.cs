using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Portal saved views are the private half of the bookmark model: the author's <c>CREATE BOOKMARK</c>
/// definitions are shared and source-controlled, while a saved view is one person's snapshot. These
/// tests hold the properties that make the two safely interchangeable at the runtime boundary:
///
/// <list type="bullet">
///   <item><description>every stored view is readable as the shared <c>ResolvedReportState</c>
///     envelope, including views written before the envelope existed;</description></item>
///   <item><description>a view identifier resolves only for its owner, and an identifier belonging to
///     someone else is indistinguishable from one that never existed;</description></item>
///   <item><description>republishing the report surfaces as a drift warning rather than a silently
///     partial application;</description></item>
///   <item><description>"no default view" answers 204, because a reader without a personal default
///     must still get the base report.</description></item>
/// </list>
///
/// The endpoints under test are the ones the browser runtime calls during launch precedence, and they
/// previously had no test at all — the exact shape of defect recorded in the Portal silent-failure
/// pattern, where a control exists, looks implemented, and is never asserted end to end.
/// </summary>
[Trait("Category", "Portal")]
public sealed class SavedReportViewStateTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SavedViewRoundTripsTheResolvedStateEnvelope()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var reportId = await CreateReportAsync(factory, client, token, "envelope");

        var stateJson = """
            {"schemaVersion":1,"activePage":"Detail","parameters":{"@Region":"West","@Limit":25,"@Live":true},
             "visible":{"FilterPanel":false},"collapsed":{"Notes":true}}
            """;

        var created = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views",
            new { name = "Western detail", stateJson });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var viewId = (await created.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var fetched = await Send(client, HttpMethod.Get, token, $"/api/reports/{reportId}/saved-views/{viewId}", null);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var body = (await fetched.Content.ReadFromJsonAsync<JsonObject>(Json))!;

        var state = body["state"]!.AsObject();
        Assert.Equal(1, state["schemaVersion"]!.GetValue<int>());
        Assert.Equal("Detail", state["activePage"]!.GetValue<string>());

        // Typed values survive as JSON tokens rather than being flattened to quoted strings — the
        // whole point of the envelope over the legacy parameter map.
        var parameters = state["parameters"]!.AsObject();
        Assert.Equal("West", parameters["@Region"]!.GetValue<string>());
        Assert.Equal(25, parameters["@Limit"]!.GetValue<int>());
        Assert.True(parameters["@Live"]!.GetValue<bool>());

        Assert.False(state["visible"]!.AsObject()["FilterPanel"]!.GetValue<bool>());
        Assert.True(state["collapsed"]!.AsObject()["Notes"]!.GetValue<bool>());

        // The server, not the client, stamps the revision the view was captured against.
        Assert.False(string.IsNullOrWhiteSpace(body["scriptHash"]?.GetValue<string>()));
        Assert.Null(body["driftWarning"]?.GetValue<string>());
    }

    [Fact]
    public async Task LegacyParameterOnlyViewIsReadableAsAnEnvelope()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var reportId = await CreateReportAsync(factory, client, token, "legacy");

        // The pre-envelope client shape: a flat string map, no stateJson at all.
        var created = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views",
            new { name = "Legacy", parameters = new Dictionary<string, string> { ["@Region"] = "East" } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var viewId = (await created.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var fetched = await Send(client, HttpMethod.Get, token, $"/api/reports/{reportId}/saved-views/{viewId}", null);
        var body = (await fetched.Content.ReadFromJsonAsync<JsonObject>(Json))!;

        Assert.Equal("East", body["state"]!["parameters"]!["@Region"]!.GetValue<string>());
        // The legacy columns stay populated so an older client reading the same row keeps working.
        Assert.Equal("East", body["parameters"]!["@Region"]!.GetValue<string>());
    }

    [Fact]
    public async Task RepublishingTheReportSurfacesAsADriftWarning()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var (reportId, scriptPath) = await CreateReportWithPathAsync(factory, client, token, "drift");

        var created = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views",
            new { name = "Before", stateJson = """{"schemaVersion":1,"parameters":{"@Region":"West"}}""" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var viewId = (await created.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Drift v2';\n");
        var republished = await Send(client, HttpMethod.Put, token, $"/api/reports/{reportId}", new { scriptPath });
        Assert.Equal(HttpStatusCode.OK, republished.StatusCode);

        var fetched = await Send(client, HttpMethod.Get, token, $"/api/reports/{reportId}/saved-views/{viewId}", null);
        var body = (await fetched.Content.ReadFromJsonAsync<JsonObject>(Json))!;

        var warning = body["driftWarning"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(warning));
        Assert.Contains("Before", warning!);

        // Drift is a warning, never a block: the state is still returned so the reader gets the view.
        Assert.Equal("West", body["state"]!["parameters"]!["@Region"]!.GetValue<string>());

        // Re-capturing the view re-stamps the revision, so it stops being flagged.
        await Send(client, HttpMethod.Put, token, $"/api/reports/{reportId}/saved-views/{viewId}",
            new { stateJson = """{"schemaVersion":1,"parameters":{"@Region":"West"}}""" });
        var refetched = await Send(client, HttpMethod.Get, token, $"/api/reports/{reportId}/saved-views/{viewId}", null);
        Assert.Null((await refetched.Content.ReadFromJsonAsync<JsonObject>(Json))!["driftWarning"]?.GetValue<string>());
    }

    [Fact]
    public async Task AnotherUsersViewIdentifierIsIndistinguishableFromOneThatDoesNotExist()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await AdminTokenAsync(client);
        var reportId = await CreateReportAsync(factory, client, adminToken, "ownership");

        var created = await Send(client, HttpMethod.Post, adminToken, $"/api/reports/{reportId}/saved-views",
            new { name = "Private", stateJson = """{"schemaVersion":1,"parameters":{"@Region":"West"}}""" });
        var viewId = (await created.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var otherToken = await SecondUserTokenAsync(client, adminToken);

        // Same report access, different person: the identifier must resolve to nothing, and it must
        // look exactly like a deleted view so the URL cannot be used to probe for someone else's views.
        var mine = await Send(client, HttpMethod.Get, otherToken, $"/api/reports/{reportId}/saved-views/{viewId}", null);
        var missing = await Send(client, HttpMethod.Get, otherToken, $"/api/reports/{reportId}/saved-views/999999", null);
        Assert.Equal(HttpStatusCode.NotFound, mine.StatusCode);
        Assert.Equal(missing.StatusCode, mine.StatusCode);

        var theirList = await Send(client, HttpMethod.Get, otherToken, $"/api/reports/{reportId}/saved-views", null);
        Assert.Equal(HttpStatusCode.OK, theirList.StatusCode);
        Assert.Empty((await theirList.Content.ReadFromJsonAsync<JsonArray>(Json))!);
    }

    [Fact]
    public async Task DefaultViewAnswers204WhenTheReaderHasNoneAndRoundTripsWhenTheyDo()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var reportId = await CreateReportAsync(factory, client, token, "default");

        // No personal default is not an error — the report still has to open on its own defaults.
        var none = await Send(client, HttpMethod.Get, token, $"/api/reports/{reportId}/saved-views/default", null);
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);

        // The runtime posts the envelope, not a flat parameter map. Binding this body to a
        // Dictionary<string,string> is what previously made the "Save Default View" button a no-op.
        var saved = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views/default", new
        {
            state = new
            {
                schemaVersion = 1,
                activePage = "Summary",
                parameters = new Dictionary<string, object> { ["@Region"] = "North", ["@Limit"] = 10 }
            }
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var fetched = await Send(client, HttpMethod.Get, token, $"/api/reports/{reportId}/saved-views/default", null);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var body = (await fetched.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        Assert.True(body["isDefault"]!.GetValue<bool>());
        Assert.Equal("Summary", body["state"]!["activePage"]!.GetValue<string>());
        Assert.Equal("North", body["state"]!["parameters"]!["@Region"]!.GetValue<string>());
        Assert.Equal(10, body["state"]!["parameters"]!["@Limit"]!.GetValue<int>());
    }

    [Fact]
    public async Task OnlyOneViewCanBeTheDefault()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var reportId = await CreateReportAsync(factory, client, token, "onedefault");

        var first = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views",
            new { name = "First", isDefault = true, stateJson = """{"schemaVersion":1}""" });
        var firstId = (await first.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var second = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views",
            new { name = "Second", isDefault = true, stateJson = """{"schemaVersion":1}""" });
        var secondId = (await second.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var list = await Send(client, HttpMethod.Get, token, $"/api/reports/{reportId}/saved-views", null);
        var views = (await list.Content.ReadFromJsonAsync<JsonArray>(Json))!;
        var defaults = views.Where(v => v!["isDefault"]!.GetValue<bool>()).Select(v => v!["id"]!.GetValue<int>()).ToList();
        Assert.Equal(new[] { secondId }, defaults);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task MalformedStateIsRejectedRatherThanStored()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var reportId = await CreateReportAsync(factory, client, token, "malformed");

        var created = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views",
            new { name = "Broken", stateJson = "{not json" });
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);

        var list = await Send(client, HttpMethod.Get, token, $"/api/reports/{reportId}/saved-views", null);
        Assert.Empty((await list.Content.ReadFromJsonAsync<JsonArray>(Json))!);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":42}")]
    [InlineData("{\"schemaVersion\":1,\"parameters\":{\"@Region\":{\"nested\":true}}}")]
    public async Task StructurallyInvalidStateIsRejectedRatherThanNormalized(string stateJson)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var reportId = await CreateReportAsync(factory, client, token, "invalid-shape");

        var response = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views",
            new { name = "Broken", stateJson });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClientCannotSpoofTheSavedViewRevision()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var reportId = await CreateReportAsync(factory, client, token, "server-hash");

        var created = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views", new
        {
            name = "Stamped",
            scriptHash = "attacker-controlled",
            stateJson = """{"schemaVersion":1,"scriptHash":"also-attacker-controlled"}"""
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        Assert.NotEqual("attacker-controlled", body["scriptHash"]!.GetValue<string>());
        Assert.Equal(body["scriptHash"]!.GetValue<string>(), body["state"]!["scriptHash"]!.GetValue<string>());
    }

    [Fact]
    public async Task SavedViewHasAServerSideAtomicApplyEndpoint()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var reportId = await CreateReportAsync(factory, client, token, "apply");
        var created = await Send(client, HttpMethod.Post, token, $"/api/reports/{reportId}/saved-views",
            new { name = "Apply me", stateJson = """{"schemaVersion":1}""" });
        var viewId = (await created.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var applied = await Send(client, HttpMethod.Post, token,
            $"/api/reports/{reportId}/saved-views/{viewId}/apply", new { });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var manifest = (await applied.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        Assert.NotNull(manifest["appliedState"]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<int> CreateReportAsync(
        PortalWebFactory factory, HttpClient client, string token, string slug)
        => (await CreateReportWithPathAsync(factory, client, token, slug)).ReportId;

    private static async Task<(int ReportId, string ScriptPath)> CreateReportWithPathAsync(
        PortalWebFactory factory, HttpClient client, string token, string slug)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = await Send(client, HttpMethod.Post, token, "/api/folders",
            new { name = $"views_{slug}_{suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folder.StatusCode);
        var folderId = (await folder.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"{slug}-{suffix}.rptsql");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Views';\n");

        var report = await Send(client, HttpMethod.Post, token, "/api/reports",
            new { folderId, name = $"Views {slug} {suffix}", scriptPath });
        Assert.Equal(HttpStatusCode.Created, report.StatusCode);
        var reportId = (await report.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        return (reportId, scriptPath);
    }

    private static async Task<string> SecondUserTokenAsync(HttpClient client, string adminToken)
    {
        var username = "viewer_" + Guid.NewGuid().ToString("N")[..8];
        var create = await Send(client, HttpMethod.Post, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role = "Admin"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var initial = await LoginAsync(client, username, "Initial@Test1!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Post, initial, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
        return await LoginAsync(client, username, "Ready@Test2!");
    }

    private static async Task<string> AdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Post, initial, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
