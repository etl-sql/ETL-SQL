using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Two guarded credential surfaces. Both share one rule — no key or secret material is ever
/// returned — and both exist to make a failure knowable before it bites rather than after.
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class KeyAndCredentialPostureTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DatasetKeyPosture_InventoriesVersionsWithoutRevealingKeyMaterial()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await RegisterDatasetAsync(factory, $"ds_current_{suffix}", keyVersion: "v1");
        await RegisterDatasetAsync(factory, $"ds_current2_{suffix}", keyVersion: "v1");

        var posture = await PostureAsync(client, adminToken, "/api/admin/datasets/at-rest-key/posture");

        Assert.Equal("v1", posture["currentVersion"]!.GetValue<string>());
        Assert.True(posture["currentKeyConfigured"]!.GetValue<bool>());

        var inventory = posture["inventory"]!.AsArray();
        var v1 = inventory.Single(entry => entry!["version"]!.GetValue<string>() == "v1")!.AsObject();
        Assert.Equal(2, v1["datasetCount"]!.GetValue<int>());
        Assert.True(v1["isCurrent"]!.GetValue<bool>());
        Assert.True(v1["keyConfigured"]!.GetValue<bool>());

        // Versions are non-secret identifiers and are named; the key itself never is.
        Assert.DoesNotContain(HostedPortalFactory.DefaultAtRestKey,
            posture.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatasetKeyPreflight_BlocksADatasetWhoseKeyVersionIsNoLongerConfigured()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // A cache encrypted under a retired version whose key was removed can neither be rotated nor
        // read. Finding that out by starting the rotation is finding out during the operation.
        await RegisterDatasetAsync(factory, $"ds_orphan_{suffix}", keyVersion: "v0-retired");

        var posture = await PostureAsync(client, adminToken, "/api/admin/datasets/at-rest-key/posture");
        var preflight = posture["preflight"]!.AsObject();

        Assert.False(preflight["canProceed"]!.GetValue<bool>());
        var blocked = preflight["blocked"]!.AsArray();
        var entry = Assert.Single(blocked, item => item!["name"]!.GetValue<string>() == $"ds_orphan_{suffix}");
        Assert.Equal("v0-retired", entry!["version"]!.GetValue<string>());
        Assert.Contains("neither be rotated nor read",
            entry["reason"]!.GetValue<string>(), StringComparison.Ordinal);

        // Rollback guidance has to say the dangerous part out loud.
        Assert.Contains("never remove a key",
            posture["rollbackGuidance"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatasetKeyVerification_ReportsFullyRotatedOnlyWhenEveryCacheIsCurrent()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await RegisterDatasetAsync(factory, $"ds_v1_{suffix}", keyVersion: "v1");
        var rotated = await PostureAsync(client, adminToken, "/api/admin/datasets/at-rest-key/posture");
        Assert.True(rotated["verification"]!["fullyRotated"]!.GetValue<bool>());

        await RegisterDatasetAsync(factory, $"ds_old_{suffix}", keyVersion: "v0-retired");
        var mixed = await PostureAsync(client, adminToken, "/api/admin/datasets/at-rest-key/posture");
        Assert.False(mixed["verification"]!["fullyRotated"]!.GetValue<bool>());
        Assert.Equal(1, mixed["verification"]!["onOtherVersions"]!.GetValue<int>());
    }

    [Fact]
    public async Task CredentialPosture_FindsAConnectionWhoseSecretIsMissing()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await SeedSecretAsync(factory, $"live_{suffix}");
        await SeedConnectionAsync(factory, $"ok_{suffix}", $"SECRET:live_{suffix}");
        await SeedConnectionAsync(factory, $"broken_{suffix}", $"SECRET:never_created_{suffix}");

        var posture = await PostureAsync(client, adminToken, "/api/admin/credentials/posture");
        var connections = posture["connections"]!.AsArray()
            .ToDictionary(c => c!["alias"]!.GetValue<string>(), c => c!.AsObject());

        Assert.True(connections[$"ok_{suffix}"]["healthy"]!.GetValue<bool>());
        Assert.Empty(connections[$"ok_{suffix}"]["unresolvedSecrets"]!.AsArray());

        // Healthy on the connections page, healthy on the secrets page, broken in the join.
        var broken = connections[$"broken_{suffix}"];
        Assert.False(broken["healthy"]!.GetValue<bool>());
        Assert.Equal([$"never_created_{suffix}"],
            broken["unresolvedSecrets"]!.AsArray().Select(v => v!.GetValue<string>()));

        Assert.Contains(posture["findings"]!.AsArray().Select(f => f!.GetValue<string>()),
            finding => finding.Contains("cannot authenticate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CredentialPosture_ShowsWhoReferencesASecret_AndFlagsOrphans()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await SeedSecretAsync(factory, $"used_{suffix}");
        await SeedSecretAsync(factory, $"unused_{suffix}");
        await SeedConnectionAsync(factory, $"uses_{suffix}", $"SECRET:used_{suffix}");

        var posture = await PostureAsync(client, adminToken, "/api/admin/credentials/posture");
        var secrets = posture["secrets"]!.AsArray()
            .ToDictionary(s => s!["name"]!.GetValue<string>(), s => s!.AsObject());

        // The blast radius of disabling a secret is the list of things that reference it.
        Assert.Equal([$"uses_{suffix}"],
            secrets[$"used_{suffix}"]["referencedBy"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.False(secrets[$"used_{suffix}"]["orphaned"]!.GetValue<bool>());

        // An unused credential is still a credential.
        Assert.True(secrets[$"unused_{suffix}"]["orphaned"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CredentialPosture_NeverReturnsASecretValue()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await SeedSecretAsync(factory, $"vault_{suffix}", value: "the-actual-secret-value");
        await SeedConnectionAsync(factory, $"conn_{suffix}", $"SECRET:vault_{suffix}");

        var posture = await PostureAsync(client, adminToken, "/api/admin/credentials/posture");

        Assert.Contains($"vault_{suffix}", posture.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("the-actual-secret-value", posture.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothAreAdministratorOnly()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await CreateViewerAsync(client, adminToken, $"key_deny_{suffix}");
        var viewerToken = await LoginAsync(client, $"key_deny_{suffix}", "Ready@Test2!");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken, "/api/admin/datasets/at-rest-key/posture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken, "/api/admin/credentials/posture")).StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task RegisterDatasetAsync(PortalWebFactory factory, string name, string keyVersion)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.Datasets.Add(new Dataset
        {
            Name = name,
            FolderPath = "/keys",
            ParquetFilePath = $"{name}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Private,
            AtRestKeyVersion = keyVersion
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSecretAsync(
        PortalWebFactory factory, string name, string value = "placeholder")
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PortalSecretStoreService>();
        await store.StoreAsync(name, value, null);
        await scope.ServiceProvider.GetRequiredService<PortalDbContext>().SaveChangesAsync();
    }

    private static async Task SeedConnectionAsync(PortalWebFactory factory, string alias, string secretReference)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.PortalSharedConnections.Add(new PortalSharedConnection
        {
            Alias = alias,
            ConnectorType = "SQLITE",
            OptionsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["PASSWORD"] = secretReference
            })
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonObject> PostureAsync(HttpClient client, string adminToken, string url)
    {
        var response = await AuthGet(client, adminToken, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task CreateViewerAsync(HttpClient client, string adminToken, string username)
    {
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var initial = await LoginAsync(client, username, "Initial@Test1!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
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
