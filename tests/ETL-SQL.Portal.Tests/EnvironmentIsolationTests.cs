using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Departmental isolation is deployment isolation, not shared-table multitenancy: two environments
/// are two complete deployments with their own databases, artifact roots, keys and identities.
///
/// These cover both halves of the Environments workflow — planning one, and proving the model holds.
/// The isolation proof matters more than the plan: a plan that is merely documented is a document,
/// while "a token from one environment does not authenticate to another" is a property that either
/// holds or does not.
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class EnvironmentIsolationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PlanDerivesEveryIsolatedResourceFromTheEnvironmentId_AndCarriesNoSecret()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var plan = await PlanAsync(client, adminToken, "finance", 5100);

        var resources = plan["resources"]!.AsArray()
            .ToDictionary(r => r!["kind"]!.GetValue<string>(), r => r!.AsObject());
        Assert.Equal("portal_finance", resources["PortalDatabase"]["name"]!.GetValue<string>());
        Assert.Equal("etlsql-finance", resources["ServiceIdentity"]["name"]!.GetValue<string>());
        Assert.Contains("finance", resources["KeyRing"]["singleNodeValue"]!.GetValue<string>(), StringComparison.Ordinal);

        // Ports follow the documented offsets from the base.
        var ports = plan["ports"]!.AsArray()
            .ToDictionary(p => p!["endpoint"]!.GetValue<string>(), p => p!["port"]!.GetValue<int>());
        Assert.Equal(5100, ports["Portal HTTP"]);
        Assert.Equal(5101, ports["Orchestrator HTTP"]);
        Assert.Equal(5102, ports["Portal HTTPS"]);

        // Keys are requirements, never values: a plan carrying key material is a plan you cannot
        // safely email, review, or store.
        var raw = plan.ToJsonString();
        Assert.DoesNotContain(HostedPortalFactory.DefaultAtRestKey, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("integration-test-secret-key-1234567890", raw, StringComparison.Ordinal);
        var requirements = plan["secretRequirements"]!.AsArray()
            .Select(r => r!["configurationKey"]!.GetValue<string>()).ToList();
        Assert.Contains("Portal:Jwt:Secret", requirements);
        Assert.Contains("Portal:Dataset:AtRestKey", requirements);
    }

    [Fact]
    public async Task PlanSaysTheirPortalDoesNotProvision()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var notes = (await PlanAsync(client, adminToken, "hr", 5200))["notes"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();

        // An environment able to provision another is not isolated from it, so the boundary is
        // stated in the artifact rather than left to the reader.
        Assert.Contains(notes, note => note.Contains("does not provision", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("No secret values", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidationRejectsAnEnvironmentIdAlreadyInUse()
    {
        using var factory = new KnownFleetFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var clean = await ValidateAsync(client, adminToken, "brand-new", 5300);
        Assert.True(clean["isValid"]!.GetValue<bool>());

        var taken = await ValidateAsync(client, adminToken, "finance", 5400);
        Assert.False(taken["isValid"]!.GetValue<bool>());
        Assert.Contains(taken["collisions"]!.AsArray().Select(c => c!["kind"]!.GetValue<string>()),
            kind => kind == "EnvironmentId");

        // Validation is not provisioning, and says so.
        Assert.Contains("does not provision",
            taken["provisioningNote"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedEnvironmentIdsAreRejected()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        // The id becomes hostnames, account names and paths, so anything that is not a DNS-safe
        // token is refused rather than sanitised into something the operator did not ask for.
        foreach (var bad in new[] { "Finance", "fin ance", "-fin", "fin/ance", "" })
        {
            var response = await AuthGet(client, adminToken,
                $"/api/admin/environments/plan?environmentId={Uri.EscapeDataString(bad)}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task ThePlan_NamesTheSecurityEventOutbox_BecauseItsDefaultIsMachineWide()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var plan = await PlanAsync(client, adminToken, "finance", 5200);
        var resources = plan["resources"]!.AsArray();

        // The outbox defaults to a machine-wide path under LocalApplicationData, shared by every
        // ETL-SQL process on the host. A plan that lists databases, artifact roots and key rings but
        // omits this one reads as complete while leaving two environments writing security events
        // into a single queue — a leak of exactly the records isolation exists to keep apart, and
        // the one resource here whose default is *wrong* rather than merely unset.
        var outbox = Assert.Single(resources,
            r => r!["kind"]!.GetValue<string>() == "SecurityEventOutbox")!.AsObject();

        Assert.Contains("ETLSQL_SECURITY_EVENT_OUTBOX_PATH",
            outbox["singleNodeValue"]!.GetValue<string>());
        Assert.Contains("machine-wide", outbox["singleNodeValue"]!.GetValue<string>());
        Assert.Contains("shared by every process",
            outbox["isolationRequirement"]!.GetValue<string>());
    }

    [Fact]
    public async Task CurrentEnvironmentEvidence_AdmitsWhatItCannotSee()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var response = await AuthGet(client, adminToken, "/api/admin/environments/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evidence = (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;

        var items = evidence["evidence"]!.AsArray()
            .ToDictionary(i => i!["resource"]!.GetValue<string>(), i => i!.AsObject());

        // Key uniqueness and the OS account are properties of provisioning, not of this process.
        // Reporting them as unknown is the honest answer; claiming isolation would not be.
        Assert.Null(items["JwtSecret"]["isolated"]?.GetValue<bool?>());
        Assert.Null(items["ServiceIdentity"]["isolated"]?.GetValue<bool?>());
        Assert.Equal("configured", items["JwtSecret"]["observed"]!.GetValue<string>());

        Assert.Equal("/api/fleet/workspace", evidence["fleetStatusPath"]!.GetValue<string>());
    }

    [Fact]
    public async Task TwoEnvironmentsShareNoCatalog_AndNeitherTokenAuthenticatesToTheOther()
    {
        // Two deployments, exactly as departmental isolation defines them: separate databases,
        // separate artifact roots, separate JWT secrets.
        using var finance = new PortalWebFactory();
        using var hr = new PortalWebFactory();
        using var financeClient = finance.CreateClient();
        using var hrClient = hr.CreateClient();

        var financeToken = await GetAdminTokenAsync(financeClient);
        var hrToken = await GetAdminTokenAsync(hrClient);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var financeFolder = await CreateFolderAsync(financeClient, financeToken, $"finance_only_{suffix}");
        await CreateFolderAsync(hrClient, hrToken, $"hr_only_{suffix}");

        // Catalogs do not merge: neither environment can see the other's folders.
        Assert.True(await FolderVisibleAsync(financeClient, financeToken, $"finance_only_{suffix}"));
        Assert.False(await FolderVisibleAsync(hrClient, hrToken, $"finance_only_{suffix}"));
        Assert.False(await FolderVisibleAsync(financeClient, financeToken, $"hr_only_{suffix}"));

        // Search does not merge either — the same query returns each environment's own results only.
        var hrSearch = await AuthGet(hrClient, hrToken, $"/api/catalog/search?q=finance_only_{suffix}");
        Assert.DoesNotContain($"finance_only_{suffix}",
            await hrSearch.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // And a resource id from one environment is meaningless in the other, rather than resolving
        // to whatever happens to share that id.
        var crossRead = await AuthGet(hrClient, hrToken, $"/api/folders/{financeFolder}");
        Assert.True(crossRead.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden
            or HttpStatusCode.OK, "a cross-environment id must never reach the other environment's data");
        if (crossRead.StatusCode == HttpStatusCode.OK)
        {
            // If an id collides, it must be HR's own folder — never Finance's.
            Assert.DoesNotContain($"finance_only_{suffix}",
                await crossRead.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ATokenFromOneEnvironmentDoesNotAuthenticateToAnother()
    {
        // Two independent controls enforce this, and it is worth being precise about which.
        //
        // The per-environment JWT secret means the signature does not verify. But the token also
        // carries a security stamp that is validated against the *environment's own* database, and
        // that check alone rejects the token even when two environments share a signing secret —
        // confirmed by temporarily giving both hosts the same secret and watching this still pass.
        //
        // So this asserts the property that matters (a token minted in one environment is refused by
        // another) without claiming to isolate which control produced it. Both are load-bearing: the
        // stamp defends a misconfigured deployment that shared a secret, and the secret defends
        // against a forged stamp.
        using var finance = new PortalWebFactory();
        using var hr = new DifferentJwtSecretFactory();
        using var financeClient = finance.CreateClient();
        using var hrClient = hr.CreateClient();

        var financeToken = await GetAdminTokenAsync(financeClient);

        var crossUse = await AuthGet(hrClient, financeToken, "/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, crossUse.StatusCode);

        // And the token still works where it was minted, so the rejection above is about the
        // environment boundary rather than a token that was never valid.
        Assert.Equal(HttpStatusCode.OK,
            (await AuthGet(financeClient, financeToken, "/api/admin/users")).StatusCode);
    }

    /// <summary>A portal that already knows about a 'finance' environment through fleet configuration.</summary>
    private sealed class KnownFleetFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.Fleet.Environments =
            [
                new PortalFleetEnvironmentConfig { Name = "finance", BaseUrl = "https://finance.example.invalid" }
            ];
    }

    /// <summary>A second environment with its own signing secret, as the contract requires.</summary>
    private sealed class DifferentJwtSecretFactory : PortalWebFactory
    {
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings) =>
            settings["Portal:Jwt:Secret"] = "a-completely-different-environment-secret-0123456789";

        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.Jwt.Secret = "a-completely-different-environment-secret-0123456789";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<JsonObject> PlanAsync(
        HttpClient client, string adminToken, string environmentId, int portBase)
    {
        var response = await AuthGet(client, adminToken,
            $"/api/admin/environments/plan?environmentId={environmentId}&portBase={portBase}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task<JsonObject> ValidateAsync(
        HttpClient client, string adminToken, string environmentId, int portBase)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/environments/validate",
            new { environmentId, portBase });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task<int> CreateFolderAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/folders", new { name, parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<bool> FolderVisibleAsync(HttpClient client, string token, string name)
    {
        var response = await AuthGet(client, token, "/api/folders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadAsStringAsync()).Contains(name, StringComparison.Ordinal);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url) =>
        SendAsync(client, HttpMethod.Get, token, url, null);

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body) =>
        SendAsync(client, HttpMethod.Post, token, url, body);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
