using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Validates real API responses against <c>BrowserContracts/critical-api-contracts.json</c> — the
/// same file the browser client validates against at runtime.
///
/// <para>The contract already existed and was already enforced, but only <b>in the user's
/// session</b>: a server-side rename reached production, and the first thing that noticed was a
/// `TypeError` on somebody's screen. Enforcing the same contract here moves that discovery to the
/// build, which is the entire value — a contract nobody checks until it is violated in front of a
/// user is documentation, not a contract.</para>
///
/// <para>The contract file is read rather than restated. A C# copy of the field list would be a
/// second source of truth that agrees with the browser's until the day it quietly does not, and
/// this test would then be checking the copy rather than the thing being shipped.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class BrowserApiContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static JsonObject Contracts()
    {
        var path = Path.Combine(
            RepoRoot(), "src", "ETL-SQL.Portal", "BrowserContracts", "critical-api-contracts.json");
        Assert.True(File.Exists(path), $"Contract file not found at {path}");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    [Fact]
    public async Task EveryContractedEndpoint_ReturnsWhatTheBrowserIsPromised()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var contracts = Contracts();
        var admin = await GetAdminTokenAsync(client);

        // A folder, a report in it, and a run of that report: enough to exercise every contract the
        // browser client declares, through the endpoints it actually calls.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderId = await CreateFolderAsync(client, admin, $"contract_{suffix}");
        var scriptName = $"contract_{suffix}.rptsql";
        await File.WriteAllTextAsync(
            Path.Combine(factory.TempDir, "scripts", scriptName),
            "SELECT 1 AS Value INTO #d;\nCREATE VISUAL V AS TABLE (SOURCE = #d, MAPPINGS (Value = Value));");
        var reportId = await PublishAsync(client, admin, folderId, $"Contract {suffix}", scriptName);

        await AssertContractAsync(contracts, "folderList", client, admin, "/api/folders");
        await AssertContractAsync(contracts, "reportList", client, admin, $"/api/folders/{folderId}/reports");
        await AssertContractAsync(contracts, "userCatalog", client, admin, "/api/admin/users/catalog");

        var accepted = await AssertContractAsync(
            contracts, "jobAccepted", client, admin, $"/api/reports/{reportId}/execute", HttpMethod.Post);
        var jobId = accepted!["jobId"]!.GetValue<string>();
        await AssertContractAsync(contracts, "jobStatus", client, admin, $"/api/jobs/{jobId}");

        var created = await AuthPost(client, admin, "/api/admin/users",
            new
            {
                username = $"contract_user_{suffix}",
                password = "Contract@Tests99!",
                role = "Viewer",
                email = $"contract_{suffix}@example.com"
            });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        Validate(contracts, "user", (await created.Content.ReadFromJsonAsync<JsonNode>(Json))!, "user");
    }

    [Fact]
    public void EveryDeclaredContract_IsReachableFromTheBrowserClient()
    {
        // A contract nothing calls is a claim about an endpoint that may no longer exist, and it
        // will pass every validation forever because it is never exercised.
        var contracts = Contracts();
        var apiClient = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot", "js", "api.js"));

        var unreferenced = contracts
            .Select(pair => pair.Key)
            .Where(name => !apiClient.Contains($"'{name}'", StringComparison.Ordinal)
                // Nested contracts are reached through their parent rather than named at a call site.
                && !contracts.Any(other => ReferencesType(other.Value!, name)))
            .ToList();

        Assert.True(unreferenced.Count == 0,
            "These contracts are declared but never applied to a response, so nothing checks them:\n  "
            + string.Join("\n  ", unreferenced));
    }

    private static bool ReferencesType(JsonNode contract, string name)
    {
        var obj = contract.AsObject();
        if (obj.TryGetPropertyValue("items", out var items)
            && items?.GetValue<string>().TrimEnd('[', ']') == name)
            return true;
        if (!obj.TryGetPropertyValue("fields", out var fields) || fields is null) return false;
        return fields.AsObject().Any(f => f.Value!.GetValue<string>().TrimEnd('[', ']') == name);
    }

    // ── contract evaluation, mirroring the generated browser validator ──────────────────────────

    private static async Task<JsonObject?> AssertContractAsync(
        JsonObject contracts, string name, HttpClient client, string token, string url,
        HttpMethod? method = null)
    {
        var req = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        if (method == HttpMethod.Post) req.Content = JsonContent.Create(new { });
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await client.SendAsync(req);
        Assert.True(res.IsSuccessStatusCode,
            $"{url} returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");

        var body = await res.Content.ReadFromJsonAsync<JsonNode>(Json);
        Validate(contracts, name, body!, name);
        return body as JsonObject;
    }

    private static void Validate(JsonObject contracts, string name, JsonNode value, string path)
    {
        var contract = contracts[name]?.AsObject();
        Assert.True(contract is not null, $"Contract '{name}' is not declared.");

        var kind = contract!["kind"]!.GetValue<string>();
        if (kind == "array")
        {
            Assert.True(value is JsonArray, $"{path}: expected an array for contract '{name}'.");
            var itemType = contract["items"]!.GetValue<string>();
            var index = 0;
            foreach (var item in value.AsArray())
                Validate(contracts, itemType, item!, $"{path}[{index++}]");
            return;
        }

        Assert.True(value is JsonObject, $"{path}: expected an object for contract '{name}'.");
        var obj = value.AsObject();
        foreach (var (field, declared) in contract["fields"]!.AsObject())
        {
            var expected = declared!.GetValue<string>();
            Assert.True(obj.ContainsKey(field),
                $"{path}.{field} is missing. The browser client declares contract '{name}' on this "
                + "response, so a field it expects must be present.");
            ValidateType(contracts, expected, obj[field], $"{path}.{field}");
        }
    }

    private static void ValidateType(JsonObject contracts, string expected, JsonNode? value, string path)
    {
        if (expected.EndsWith("[]", StringComparison.Ordinal))
        {
            Assert.True(value is JsonArray, $"{path}: expected {expected}.");
            var itemType = expected[..^2];
            var index = 0;
            foreach (var item in value!.AsArray())
                ValidateType(contracts, itemType, item, $"{path}[{index++}]");
            return;
        }

        if (contracts.ContainsKey(expected))
        {
            // Nulls are allowed for nested contracts, matching the browser validator: an absent
            // child is a shape the UI already copes with.
            if (value is null) return;
            Validate(contracts, expected, value, path);
            return;
        }

        // A nullable field is expressed by the UI tolerating null, so null passes any scalar type.
        if (value is null) return;

        var kind = value.GetValueKind();
        var ok = expected switch
        {
            "string" => kind == JsonValueKind.String,
            "number" => kind == JsonValueKind.Number,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            _ => true,
        };
        Assert.True(ok, $"{path}: expected {expected}, received {kind}.");
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    private static async Task<int> CreateFolderAsync(HttpClient client, string token, string name)
    {
        var res = await AuthPost(client, token, "/api/folders", new { name });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<int> PublishAsync(
        HttpClient client, string token, int folderId, string name, string scriptPath)
    {
        var res = await AuthPost(client, token, "/api/reports", new { folderId, name, scriptPath });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static Task<HttpResponseMessage> AuthPost(
        HttpClient client, string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(req);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var first = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, first, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
