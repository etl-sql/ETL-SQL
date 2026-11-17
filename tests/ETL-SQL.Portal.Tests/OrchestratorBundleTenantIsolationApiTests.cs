using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class OrchestratorBundleTenantIsolationApiTests : IDisposable
{
    private const string ValidKey = "test-orch-key-12345";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly OrchestratorWebFactory _factory = new(requireFederatedIdentity: true);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task BundleEndpointsAndJobPinning_AreIsolatedAcrossTenants()
    {
        var client = _factory.CreateClient();

        var aliceTenantA = new OrchestratorCaller(
            "user", "alice", "Alice", ["OrchestratorManager"], [], "tenant-a",
            ["orchestrator.admin"], []);
        var bobTenantB = new OrchestratorCaller(
            "user", "bob", "Bob", ["OrchestratorManager"], [], "tenant-b",
            ["orchestrator.admin"], []);

        // Both tenants publish a bundle with the same name ("shared-etl") -> both get version 1 independently.
        var pubA = await SendAsAsync(client, aliceTenantA, HttpMethod.Post, "/api/bundles",
            MakePublishBody("shared-etl", "SELECT 'tenant-a';", "sha256:tenant-a-1"));
        Assert.Equal(HttpStatusCode.OK, pubA.StatusCode);
        var versionA = await pubA.Content.ReadFromJsonAsync<BundleVersionInfo>(Json);
        Assert.Equal(1, versionA!.Version);

        var pubB = await SendAsAsync(client, bobTenantB, HttpMethod.Post, "/api/bundles",
            MakePublishBody("shared-etl", "SELECT 'tenant-b';", "sha256:tenant-b-1"));
        Assert.Equal(HttpStatusCode.OK, pubB.StatusCode);
        var versionB = await pubB.Content.ReadFromJsonAsync<BundleVersionInfo>(Json);
        Assert.Equal(1, versionB!.Version);

        // Tenant A publishes a second bundle and second version of shared-etl.
        Assert.Equal(HttpStatusCode.OK, (await SendAsAsync(client, aliceTenantA, HttpMethod.Post, "/api/bundles",
            MakePublishBody("shared-etl", "SELECT 'tenant-a-v2';", "sha256:tenant-a-2"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsAsync(client, aliceTenantA, HttpMethod.Post, "/api/bundles",
            MakePublishBody("alpha-secret", "SELECT 'alpha';", "sha256:alpha-1"))).StatusCode);

        // Tenant B listing/reads only see Tenant B's own v1 bundle.
        var listB = await SendAsAsync(client, bobTenantB, HttpMethod.Get, "/api/bundles", null);
        Assert.Equal(HttpStatusCode.OK, listB.StatusCode);
        var bundlesB = await listB.Content.ReadFromJsonAsync<List<BundleVersionInfo>>(Json);
        Assert.Single(bundlesB!);
        Assert.Equal("shared-etl", bundlesB![0].BundleName);
        Assert.Equal(1, bundlesB[0].Version);

        // Tenant B cannot inspect Tenant A's v2 or Tenant A-only bundle.
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsAsync(client, bobTenantB, HttpMethod.Get, "/api/bundles/shared-etl/versions/2", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsAsync(client, bobTenantB, HttpMethod.Get, "/api/bundles/alpha-secret/versions/1/files", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsAsync(client, bobTenantB, HttpMethod.Get, "/api/bundles/alpha-secret/versions/1/dependencies", null)).StatusCode);

        var filesB = await (await SendAsAsync(client, bobTenantB, HttpMethod.Get, "/api/bundles/shared-etl/versions/1/files", null))
            .Content.ReadFromJsonAsync<List<BundleFileInfo>>(Json);
        Assert.Single(filesB!);
        Assert.Contains("tenant-b", filesB![0].Content, StringComparison.Ordinal);

        // Scheduled job pinning resolves against the caller's tenant only:
        // Tenant B's job targeting orch://shared-etl/main.etlsql pins to v1 (not A's v2),
        // and targeting orch://alpha-secret/main.etlsql stays unpinned rather than resolving Tenant A's v1.
        var createJobB = await SendAsAsync(client, bobTenantB, HttpMethod.Post, "/api/scheduled-jobs", new
        {
            name = "job-b-shared",
            jobType = "SCRIPT",
            targetPath = "orch://shared-etl/main.etlsql",
            interval = 1,
            unit = "HOUR"
        });
        Assert.Equal(HttpStatusCode.Created, createJobB.StatusCode);
        var jobBJson = await createJobB.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("orch://shared-etl@1/main.etlsql", jobBJson!["targetPath"]!.GetValue<string>());

        var createJobBCross = await SendAsAsync(client, bobTenantB, HttpMethod.Post, "/api/scheduled-jobs", new
        {
            name = "job-b-cross",
            jobType = "SCRIPT",
            targetPath = "orch://alpha-secret/main.etlsql",
            interval = 1,
            unit = "HOUR"
        });
        Assert.Equal(HttpStatusCode.Created, createJobBCross.StatusCode);
        var jobBCrossJson = await createJobBCross.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("orch://alpha-secret/main.etlsql", jobBCrossJson!["targetPath"]!.GetValue<string>());
    }

    [Fact]
    public async Task LegacyDefaultBundlesEndpoint_ReassignsToCallersVerifiedTenant()
    {
        var client = _factory.CreateClient();

        // Seed a legacy 'default' bundle directly into the Orchestrator database.
        var store = _factory.Services.GetRequiredService<ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore>();
        await store.InitializeAsync();
        await using (var conn = new SqliteConnection($"Data Source={Path.Combine(_factory.TempDir, "etlsql.db")};Pooling=False"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO bundle_versions (tenant_id, bundle_name, version, entry_path, content_hash, published_at)
                VALUES ('default', 'legacy-api-bundle', 1, 'main.etlsql', 'sha256:leg1', '2026-10-01T00:00:00Z');
                INSERT INTO bundle_files (tenant_id, bundle_name, version, virtual_path, content, content_hash, size_bytes, content_type)
                VALUES ('default', 'legacy-api-bundle', 1, 'main.etlsql', 'SELECT 7;', 'sha256:leg1', 9, 'application/etlsql');";
            await cmd.ExecuteNonQueryAsync();
        }

        var adminA = new OrchestratorCaller(
            "user", "admin-a", "Admin A", ["Admin"], [], "tenant-a",
            ["orchestrator.admin"], []);
        var adminB = new OrchestratorCaller(
            "user", "admin-b", "Admin B", ["Admin"], [], "tenant-b",
            ["orchestrator.admin"], []);

        var unassignedRes = await SendAsAsync(client, adminA, HttpMethod.Get, "/api/bundles/unassigned", null);
        Assert.Equal(HttpStatusCode.OK, unassignedRes.StatusCode);
        var unassigned = await unassignedRes.Content.ReadFromJsonAsync<List<BundleVersionInfo>>(Json);
        Assert.Contains(unassigned!, b => b.BundleName == "legacy-api-bundle");

        // Reassign to adminA's verified tenant ('tenant-a'); adminB still cannot see it.
        var reassignRes = await SendAsAsync(client, adminA, HttpMethod.Post, "/api/bundles/reassign", new
        {
            bundleName = "legacy-api-bundle"
        });
        Assert.Equal(HttpStatusCode.OK, reassignRes.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await SendAsAsync(client, adminA, HttpMethod.Get, "/api/bundles/legacy-api-bundle/versions/1/files", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsAsync(client, adminB, HttpMethod.Get, "/api/bundles/legacy-api-bundle/versions/1/files", null)).StatusCode);
    }

    private static object MakePublishBody(string name, string content, string hash) => new
    {
        bundle = new
        {
            bundleName = name,
            entryPath = "main.etlsql",
            files = new[]
            {
                new
                {
                    virtualPath = "main.etlsql",
                    content,
                    contentHash = hash,
                    sizeBytes = (long)content.Length,
                    contentType = "application/etlsql"
                }
            },
            dependencies = Array.Empty<object>(),
            contentHash = hash,
            encryptionMode = "MACHINE",
            encryptionMetadata = (string?)null,
            publisher = "tester",
            description = (string?)null
        },
        password = (string?)null
    };

    private static Task<HttpResponseMessage> SendAsAsync(
        HttpClient client,
        OrchestratorCaller caller,
        HttpMethod method,
        string uri,
        object? body)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Orchestrator-Key", ValidKey);
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}
