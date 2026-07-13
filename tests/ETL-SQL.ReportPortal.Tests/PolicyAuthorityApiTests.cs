using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Governance;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public class PolicyAuthorityApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Portal host whose policy signer is a real in-memory RSA key instead of the
    /// not-configured placeholder, so publish/activate/rollback can be driven end to end.</summary>
    private sealed class SigningPortalFactory : PortalWebFactory
    {
        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IPolicyEnvelopeSigner>();
            services.AddSingleton<IPolicyEnvelopeSigner>(new RsaPolicyEnvelopeSigner(RSA.Create(2048)));
        }
    }

    private static string DocJson(bool withExtensions = false)
    {
        var doc = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection
            {
                ApprovedRoots = [Path.GetTempPath().TrimEnd('\\', '/')],
                AllowedWriteExtensions = withExtensions ? [".csv"] : []
            }
        };
        return OrganizationPolicySchema.Serialize(doc);
    }

    [Fact]
    public async Task Endpoints_RejectAnonymousAndNonAdminCallers()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/policy-authority/status")).StatusCode);

        var adminToken = await GetAdminTokenAsync(client);
        var (_, _, viewerToken, _) = await CreateReadyUserAsync(client, adminToken, "Viewer");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken, "/api/admin/policy-authority/status")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await AuthPost(client, viewerToken, "/api/admin/policy-authority/publish", new
            {
                tenant = "acme",
                environment = "prod",
                policyVersion = "1.0.0",
                policyJson = DocJson(),
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
            })).StatusCode);
    }

    [Fact]
    public async Task Status_And_Publish_ReportNotConfigured_WithoutSigningCertificate()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var status = await (await AuthGet(client, adminToken, "/api/admin/policy-authority/status"))
            .Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.False(status!["configured"]!.GetValue<bool>());

        var publish = await AuthPost(client, adminToken, "/api/admin/policy-authority/publish", new
        {
            tenant = "acme",
            environment = "prod",
            policyVersion = "1.0.0",
            policyJson = DocJson(),
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
        });
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
        var error = await publish.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Contains("not configured", error!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Validate_ReportsSchemaErrorsAndMalformedJson()
    {
        using var factory = new SigningPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var ok = await (await AuthPost(client, adminToken, "/api/admin/policy-authority/validate",
            new { policyJson = DocJson() })).Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.True(ok!["isValid"]!.GetValue<bool>());

        var relative = OrganizationPolicySchema.Serialize(new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { ApprovedRoots = ["relative/root"] }
        });
        var invalid = await (await AuthPost(client, adminToken, "/api/admin/policy-authority/validate",
            new { policyJson = relative })).Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.False(invalid!["isValid"]!.GetValue<bool>());
        Assert.NotEmpty(invalid["errors"]!.AsArray());

        var malformed = await (await AuthPost(client, adminToken, "/api/admin/policy-authority/validate",
            new { policyJson = "{ not json" })).Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.False(malformed!["isValid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task PublishStageActivateRollback_FullAdminWorkflow()
    {
        using var factory = new SigningPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var expires = DateTimeOffset.UtcNow.AddDays(30);

        // Publish 1.0.0 active; the served envelope must verify through the client signature path
        // using the public key the status endpoint reports.
        var v100 = await PublishAsync(client, adminToken, "1.0.0", DocJson(), staged: false, expires);
        Assert.Equal("Active", v100["rolloutState"]!.GetValue<string>());
        Assert.Equal("admin", v100["author"]!.GetValue<string>());

        var status = await (await AuthGet(client, adminToken, "/api/admin/policy-authority/status"))
            .Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.True(status!["configured"]!.GetValue<bool>());
        var publicKeyPem = status["signingPublicKeyPem"]!.GetValue<string>();

        var active = await (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/active?tenant=acme&environment=prod"))
            .Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("1.0.0", active!["version"]!["policyVersion"]!.GetValue<string>());
        var envelope = JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(
            active["signedEnvelopeJson"]!.GetValue<string>(), Json)!;
        var enrollment = new EnterpriseEnrollmentDocument
        {
            Tenant = "acme",
            PolicyEndpoint = "https://policy.example.test/etl-sql",
            PolicySigningPublicKey = publicKeyPem
        };
        var parsed = EnterprisePolicySignature.VerifyAndParse(envelope, enrollment, DateTimeOffset.UtcNow);
        Assert.NotEmpty(parsed.Filesystem.ApprovedRoots);

        // Staged publish does not change the active version until it is activated.
        var v110 = await PublishAsync(client, adminToken, "1.1.0", DocJson(withExtensions: true), staged: true, expires);
        Assert.Equal("Staged", v110["rolloutState"]!.GetValue<string>());
        active = await (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/active?tenant=acme&environment=prod"))
            .Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("1.0.0", active!["version"]!["policyVersion"]!.GetValue<string>());

        var activate = await AuthPost(client, adminToken, "/api/admin/policy-authority/activate",
            new { tenant = "acme", environment = "prod", policyVersion = "1.1.0" });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        active = await (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/active?tenant=acme&environment=prod"))
            .Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("1.1.0", active!["version"]!["policyVersion"]!.GetValue<string>());

        // A staged version overtaken by a newer publish can never activate (clients reject older
        // issuance) — the API refuses it with guidance instead of serving a rejectable envelope.
        await PublishAsync(client, adminToken, "2.0.0", DocJson(), staged: true, expires);
        await PublishAsync(client, adminToken, "2.1.0", DocJson(withExtensions: true), staged: false, expires);
        var stale = await AuthPost(client, adminToken, "/api/admin/policy-authority/activate",
            new { tenant = "acme", environment = "prod", policyVersion = "2.0.0" });
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        Assert.Contains("republish",
            (await stale.Content.ReadFromJsonAsync<JsonObject>(Json))!["error"]!.GetValue<string>());

        // Emergency rollback republishes the 1.0.0 document as a new active version and records the
        // abandoned version as RolledBack in the durable history.
        var rollback = await AuthPost(client, adminToken, "/api/admin/policy-authority/rollback", new
        {
            tenant = "acme",
            environment = "prod",
            targetPolicyVersion = "1.0.0",
            newPolicyVersion = "3.0.0",
            expiresAtUtc = expires
        });
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        var v300 = await rollback.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("Active", v300!["rolloutState"]!.GetValue<string>());
        Assert.Equal(v100["policyHash"]!.GetValue<string>(), v300["policyHash"]!.GetValue<string>());

        var versions = await (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/versions?tenant=acme&environment=prod"))
            .Content.ReadFromJsonAsync<JsonArray>(Json);
        var states = versions!.ToDictionary(
            v => v!["policyVersion"]!.GetValue<string>(),
            v => v!["rolloutState"]!.GetValue<string>());
        Assert.Equal(5, states.Count);
        Assert.Equal("Superseded", states["1.0.0"]);
        Assert.Equal("Superseded", states["1.1.0"]);
        Assert.Equal("Staged", states["2.0.0"]);
        Assert.Equal("RolledBack", states["2.1.0"]);
        Assert.Equal("Active", states["3.0.0"]);

        // Publication, activation, and rollback each leave a durable audit record, and each publish
        // records whether the signing key rotated since the previously active version.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var auditRows = await db.AuditLogs
            .Where(a => a.ResourceType == "OrganizationPolicy" && a.ResourceId == "acme/prod")
            .Select(a => new { a.Action, a.Detail })
            .ToListAsync();
        Assert.Equal(4, auditRows.Count(a => a.Action == "PUBLISH_ORG_POLICY"));
        Assert.Single(auditRows, a => a.Action == "ACTIVATE_ORG_POLICY");
        Assert.Single(auditRows, a => a.Action == "ROLLBACK_ORG_POLICY");
        Assert.All(auditRows.Where(a => a.Action == "PUBLISH_ORG_POLICY"),
            a => Assert.Contains("SigningKeyRotated=False", a.Detail));
    }

    [Fact]
    public async Task HealthEndpoint_ReportsPolicyAuthorityAvailability()
    {
        // Unconfigured signing is a valid standalone state and must not degrade the node.
        using (var factory = new PortalWebFactory())
        using (var client = factory.CreateClient())
        {
            var health = await client.GetStringAsync("/health");
            Assert.Contains("policy-authority", health);
            Assert.Contains("not configured", health);
        }

        // With a configured signer the check proves the key material is accessible.
        using (var factory = new SigningPortalFactory())
        using (var client = factory.CreateClient())
        {
            var health = await client.GetStringAsync("/health");
            Assert.Contains("policy-authority", health);
            Assert.Contains("signing key is accessible", health);
        }
    }

    [Fact]
    public async Task CanaryLifecycle_PublishPromoteHalt_ThroughTheAdminApi()
    {
        using var factory = new SigningPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var expires = DateTimeOffset.UtcNow.AddDays(30);

        await PublishAsync(client, adminToken, "1.0.0", DocJson(), staged: false, expires);

        // Publish a 25% canary; the fleet active stays on 1.0.0 and the cohort surfaces on the version.
        var canary = await AuthPost(client, adminToken, "/api/admin/policy-authority/publish-canary", new
        {
            tenant = "acme", environment = "prod", policyVersion = "1.1.0-canary",
            policyJson = DocJson(withExtensions: true), reviewer = "bob",
            expiresAtUtc = expires, canaryPercentage = 25
        });
        Assert.Equal(HttpStatusCode.OK, canary.StatusCode);
        var canaryDto = await canary.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("Canary", canaryDto!["rolloutState"]!.GetValue<string>());
        Assert.Equal(25, canaryDto["canaryPercentage"]!.GetValue<int>());

        var active = await (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/active?tenant=acme&environment=prod"))
            .Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("1.0.0", active!["version"]!["policyVersion"]!.GetValue<string>());

        // GET canary surfaces the in-progress version; a second canary and an invalid cohort are refused.
        var getCanary = await (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/canary?tenant=acme&environment=prod"))
            .Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("1.1.0-canary", getCanary!["policyVersion"]!.GetValue<string>());

        var second = await AuthPost(client, adminToken, "/api/admin/policy-authority/publish-canary", new
        {
            tenant = "acme", environment = "prod", policyVersion = "1.2.0-canary",
            policyJson = DocJson(), expiresAtUtc = expires, canaryPercentage = 50
        });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        var bothSelectors = await AuthPost(client, adminToken, "/api/admin/policy-authority/publish-canary", new
        {
            tenant = "acme", environment = "prod", policyVersion = "1.3.0-canary",
            policyJson = DocJson(), expiresAtUtc = expires, canaryGroup = "ring0", canaryPercentage = 50
        });
        Assert.Equal(HttpStatusCode.BadRequest, bothSelectors.StatusCode);

        // Promote the canary to the whole fleet; no canary remains in progress.
        var promote = await AuthPost(client, adminToken, "/api/admin/policy-authority/promote-canary",
            new { tenant = "acme", environment = "prod", policyVersion = "1.1.0-canary" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        active = await (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/active?tenant=acme&environment=prod"))
            .Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("1.1.0-canary", active!["version"]!["policyVersion"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/canary?tenant=acme&environment=prod")).StatusCode);

        // Publish a group canary and halt it; halt re-issues the active document as a fresh active.
        await AuthPost(client, adminToken, "/api/admin/policy-authority/publish-canary", new
        {
            tenant = "acme", environment = "prod", policyVersion = "1.4.0-canary",
            policyJson = DocJson(), expiresAtUtc = expires, canaryGroup = "ring0"
        });
        var halt = await AuthPost(client, adminToken, "/api/admin/policy-authority/halt-canary",
            new { tenant = "acme", environment = "prod", policyVersion = "1.4.0-canary", reviewer = "bob" });
        Assert.Equal(HttpStatusCode.OK, halt.StatusCode);
        Assert.Equal("Active",
            (await halt.Content.ReadFromJsonAsync<JsonObject>(Json))!["rolloutState"]!.GetValue<string>());

        var versions = await (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/versions?tenant=acme&environment=prod"))
            .Content.ReadFromJsonAsync<JsonArray>(Json);
        var states = versions!.ToDictionary(
            v => v!["policyVersion"]!.GetValue<string>(),
            v => v!["rolloutState"]!.GetValue<string>());
        Assert.Equal("Superseded", states["1.0.0"]);
        Assert.Equal("RolledBack", states["1.4.0-canary"]);
        Assert.Contains(states, kv => kv.Value == "Active"); // the re-issued active

        // Every cohort operation leaves a durable audit record.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var actions = await db.AuditLogs
            .Where(a => a.ResourceType == "OrganizationPolicy" && a.ResourceId == "acme/prod")
            .Select(a => a.Action).ToListAsync();
        Assert.Contains("PUBLISH_CANARY_POLICY", actions);
        Assert.Contains("PROMOTE_CANARY_POLICY", actions);
        Assert.Contains("HALT_CANARY_POLICY", actions);
    }

    private static async Task<JsonObject> PublishAsync(
        HttpClient client, string adminToken, string version, string policyJson,
        bool staged, DateTimeOffset expires)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/policy-authority/publish", new
        {
            tenant = "acme",
            environment = "prod",
            policyVersion = version,
            policyJson,
            reviewer = "bob",
            expiresAtUtc = expires,
            staged
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task<(int UserId, string Username, string AccessToken, string RefreshToken)> CreateReadyUserAsync(
        HttpClient client,
        string adminToken,
        string role)
    {
        var username = $"policy_{Guid.NewGuid():N}"[..20];
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>(Json);
        var userId = created!["id"]!.GetValue<int>();

        var initial = await LoginAsync(client, username, "Initial@Test1!");
        var change = await AuthPost(client, initial.AccessToken, "/api/auth/change-password", new
        {
            currentPassword = "Initial@Test1!",
            newPassword = "Ready@Test2!"
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var ready = await LoginAsync(client, username, "Ready@Test2!");
        return (userId, username, ready.AccessToken, ready.RefreshToken);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await AuthPost(client, initial.AccessToken, "/api/auth/change-password", new
        {
            currentPassword = "Admin@12345!",
            newPassword = "Admin@Tests99!"
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return (await LoginAsync(client, "admin", "Admin@Tests99!")).AccessToken;
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginAsync(
        HttpClient client,
        string username,
        string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        return (body!["token"]!.GetValue<string>(), body["refreshToken"]!.GetValue<string>());
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url) =>
        SendAsync(client, HttpMethod.Get, token, url, null);

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body) =>
        SendAsync(client, HttpMethod.Post, token, url, body);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string token,
        string url,
        object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
