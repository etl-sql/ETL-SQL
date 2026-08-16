using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// A Portal host wired to a real Orchestrator over the in-memory test server.
///
/// <para>The two hosts are joined the way production joins them — the Portal's proxy client, its API
/// key, and the shared assertion signing secret — so what these tests exercise is the whole path a
/// browser takes: Portal RBAC, the assertion the Portal mints for the signed-in human, the
/// Orchestrator's own decision, and its answer coming back unaltered. Stubbing the Orchestrator here
/// would test the Portal's opinion of the grant store, which is precisely the thing this surface is
/// designed not to have.</para>
/// </summary>
public sealed class OrchestratorGrantAdministrationFixture : IAsyncLifetime
{
    public const string ManagerPassword = "Manager@Tests99!";
    public const string ViewerPassword = "Viewer@Tests99!";

    public OrchestratorWebFactory Orchestrator { get; } = new(requireFederatedIdentity: true);
    public PortalWebFactory Portal { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string AdminToken { get; private set; } = "";
    public string ManagerToken { get; private set; } = "";
    public string ViewerToken { get; private set; } = "";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        Portal = new FederatedPortalFactory(Orchestrator);
        Client = Portal.CreateClient();

        AdminToken = await GetAdminTokenAsync(Client);
        await CreateUserAsync("orch_manager", ManagerPassword, "OrchestratorManager");
        await CreateUserAsync("orch_viewer", ViewerPassword, "Viewer");
        ManagerToken = await LoginAsync(Client, "orch_manager", ManagerPassword);
        ViewerToken = await LoginAsync(Client, "orch_viewer", ViewerPassword);
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        Portal?.Dispose();
        Orchestrator.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the Orchestrator's grant store directly, as an administrator of the Portal's tenant.
    ///
    /// <para>This is the assertion that matters for "the panel reflects the Orchestrator's state": the
    /// answer comes from the store itself rather than from anything the Portal returned, so a
    /// Portal-side copy of the grants would show up here as a disagreement.</para>
    /// </summary>
    public async Task<JsonArray> ReadGrantsFromOrchestratorAsync(string kind, string name)
    {
        using var client = Orchestrator.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/authorization/{kind}/{Uri.EscapeDataString(name)}");
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(
                new OrchestratorCaller(
                    "user", "grant-inspector", "grant inspector", ["Admin"], [], "portal-host"),
                OrchestratorWebFactory.IdentitySecret));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonArray>(Json))!;
    }

    public async Task CreateJobAsync(string name, string token)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/orchestrator/jobs", token, new
        {
            name,
            scriptText = "SELECT 1 AS Value;",
            interval = 100,
            unit = "DAY"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }

    private async Task CreateUserAsync(string userName, string password, string role)
    {
        using var scope = Portal.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var user = new PortalUser
        {
            UserName = userName,
            Email = $"{userName}@test.local",
            IsActive = true,
            MustChangePassword = false,
            Provider = "Local"
        };
        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", initial);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private sealed class FederatedPortalFactory(OrchestratorWebFactory orchestrator) : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            // The host name is never resolved — the test server's handler serves whatever it is sent —
            // but the URL must be present, because an unset one is how the proxy reports "no
            // Orchestrator configured" and every call would return 503 instead of an answer.
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            // Re-registering the typed client re-configures the same named handler chain, so the
            // proxy's outbound calls land on the Orchestrator host in this process.
            services.AddHttpClient<ETL_SQL.Portal.Services.OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
        }
    }
}

/// <summary>
/// Grant administration through the Portal — the surface an operator actually uses.
///
/// <para><see cref="OrchestratorGrantApiTests"/> covers the Orchestrator's own routes. What is left to
/// answer is whether the Portal in front of them is a pass-through or a second opinion: that only an
/// authorized administrator can change a grant, that a change lands in the Orchestrator's store rather
/// than a Portal-side copy, that a refusal arrives as the refusal it was, and that an accepted change
/// names the human who made it.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class OrchestratorGrantAdministrationTests(OrchestratorGrantAdministrationFixture fixture)
    : IClassFixture<OrchestratorGrantAdministrationFixture>
{
    private const string GroupKey = "3f2a9c1d84be47a0b6c25e7f9d031a48";

    [Fact]
    public async Task AGrantChangedThroughThePortalIsTheOrchestratorsOwnState()
    {
        const string job = "portal_grant_roundtrip";
        await fixture.CreateJobAsync(job, fixture.AdminToken);

        using (var set = await Put(job, "EXECUTE", fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
            var written = await set.Content.ReadFromJsonAsync<JsonObject>();
            Assert.Equal("EXECUTE", written!["permission"]!.GetValue<string>());
        }

        // The store, not the Portal's answer: a Portal-side copy would satisfy the GET below and fail
        // here, which is the whole distinction this test exists to make.
        var stored = await fixture.ReadGrantsFromOrchestratorAsync("JOB", job);
        var grant = Assert.Single(stored);
        Assert.Equal(GroupKey, grant!["principalId"]!.GetValue<string>());

        // Read back in the vocabulary it was set in. The permission and principal kind are enums in
        // the store, and returning their ordinals would make every consumer decode a declaration order
        // it cannot see — which is how the CLI came to print "1:key = 2" and the Access panel to render
        // an empty chip for a grant that exists.
        Assert.Equal("EXECUTE", grant["permission"]!.GetValue<string>());
        Assert.Equal("GROUP", grant["principalKind"]!.GetValue<string>());

        using (var list = await fixture.SendAsync(
            HttpMethod.Get, $"/api/orchestrator/authorization/JOB/{job}", fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var relayed = Assert.Single((await list.Content.ReadFromJsonAsync<JsonArray>())!);
            Assert.Equal(grant["principalId"]!.GetValue<string>(), relayed!["principalId"]!.GetValue<string>());
            Assert.Equal(grant["permission"]!.GetValue<string>(), relayed["permission"]!.GetValue<string>());
        }

        using (var revoke = await fixture.SendAsync(
            HttpMethod.Delete,
            $"/api/orchestrator/authorization/JOB/{job}/GROUP/{GroupKey}",
            fixture.AdminToken))
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        Assert.Empty(await fixture.ReadGrantsFromOrchestratorAsync("JOB", job));
    }

    [Fact]
    public async Task AnAcceptedGrantChangeNamesTheHumanWhoMadeIt()
    {
        const string job = "portal_grant_audited";
        await fixture.CreateJobAsync(job, fixture.AdminToken);

        using (var set = await Put(job, "READ", fixture.AdminToken))
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        using (var revoke = await fixture.SendAsync(
            HttpMethod.Delete,
            $"/api/orchestrator/authorization/JOB/{job}/GROUP/{GroupKey}",
            fixture.AdminToken))
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        // The Orchestrator records what its store did; this record answers a different question —
        // which Portal session asked for it. Both matter during an incident, and an audit row that is
        // staged but never saved would look present in the code and be absent from the database.
        using var scope = fixture.Portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var entries = await db.AuditLogs
            .Where(entry => entry.ResourceId == job)
            .ToListAsync();

        var granted = Assert.Single(entries, entry => entry.Action == "ORCHESTRATOR_GRANT");
        var revoked = Assert.Single(entries, entry => entry.Action == "ORCHESTRATOR_REVOKE");
        Assert.Contains(GroupKey, granted.Detail);
        Assert.Contains("READ", granted.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(GroupKey, revoked.Detail);
        foreach (var entry in new[] { granted, revoked })
        {
            Assert.Equal("OrchestratorJOB", entry.ResourceType);
            Assert.NotNull(entry.UserId);
        }
    }

    [Fact]
    public async Task ARefusedGrantChangeLeavesNoTrailSayingAccessWasWidened()
    {
        const string job = "portal_grant_refused_audit";
        await fixture.CreateJobAsync(job, fixture.AdminToken);

        // The manager may reach the Orchestrator tab, so Portal RBAC admits them; the Orchestrator
        // refuses because they neither own this job nor hold MANAGE on it.
        using (var set = await Put(job, "MANAGE", fixture.ManagerToken))
            Assert.Equal(HttpStatusCode.Forbidden, set.StatusCode);

        using var scope = fixture.Portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(
            await db.AuditLogs.AnyAsync(entry => entry.ResourceId == job),
            "a refused grant must not be recorded as though it had been made");
        Assert.Empty(await fixture.ReadGrantsFromOrchestratorAsync("JOB", job));
    }

    [Fact]
    public async Task APortalUserWithoutOrchestratorAccessNeverReachesTheGrantStore()
    {
        const string job = "portal_grant_viewer_denied";
        await fixture.CreateJobAsync(job, fixture.AdminToken);

        // Refused by the Portal's own policy, before an assertion is minted or the Orchestrator is
        // asked. Both gates are real: Portal RBAC decides who may operate the Orchestrator at all,
        // and the Orchestrator decides what that principal may do to a given object.
        foreach (var (method, path) in new (HttpMethod, string)[]
        {
            (HttpMethod.Get, $"/api/orchestrator/authorization/JOB/{job}"),
            (HttpMethod.Put, $"/api/orchestrator/authorization/JOB/{job}/GROUP/{GroupKey}"),
            (HttpMethod.Delete, $"/api/orchestrator/authorization/JOB/{job}/GROUP/{GroupKey}")
        })
        {
            using var response = await fixture.SendAsync(
                method, path, fixture.ViewerToken,
                method == HttpMethod.Put ? new { permission = "MANAGE" } : null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Empty(await fixture.ReadGrantsFromOrchestratorAsync("JOB", job));
    }

    [Fact]
    public async Task TheOrchestratorsRefusalReachesTheOperatorAsItself()
    {
        const string job = "portal_grant_relayed_refusal";
        await fixture.CreateJobAsync(job, fixture.AdminToken);

        // 403 and 404 mean different things — "you may reach this but not administer it" versus "no
        // such object in your tenant" — and both render as an empty grants table if the Portal
        // flattens them. Reaching an object is not administering it, so the manager who can list the
        // job still cannot list its grants.
        using (var list = await fixture.SendAsync(
            HttpMethod.Get, $"/api/orchestrator/authorization/JOB/{job}", fixture.ManagerToken))
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using (var missing = await fixture.SendAsync(
            HttpMethod.Get, "/api/orchestrator/authorization/JOB/no_such_job", fixture.AdminToken))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using (var setMissing = await Put("no_such_job", "READ", fixture.AdminToken))
            Assert.Equal(HttpStatusCode.NotFound, setMissing.StatusCode);

        using (var malformed = await Put(job, "SUPERUSER", fixture.AdminToken))
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    private Task<HttpResponseMessage> Put(string job, string permission, string token) =>
        fixture.SendAsync(
            HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{job}/GROUP/{GroupKey}",
            token,
            new { permission });
}
