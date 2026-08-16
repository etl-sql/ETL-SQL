using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The grant administration surface, end to end against a real Orchestrator.
///
/// <para>Before this, a grant could only be set by hand-crafting a signed assertion with the shared
/// secret — which meant that in practice the per-object model existed but nobody could use it. What
/// these tests pin down is that the surface stays a <em>pass-through</em>: the Orchestrator owns the
/// grant store and decides tenant, ownership and scope, and the administration API must not develop
/// opinions of its own. A second place that decides who may change a grant is a second permission
/// model, which is the thing this item exists to prevent.</para>
/// </summary>
public sealed class OrchestratorGrantApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly OrchestratorWebFactory factory = new(requireFederatedIdentity: true);
    private readonly HttpClient client;

    public OrchestratorGrantApiTests() => client = factory.CreateClient();

    private static OrchestratorCaller Caller(string type, string id, params string[] roles) =>
        new(type, id, id, roles, []);

    private async Task CreateJobAsync(string name, OrchestratorCaller owner)
    {
        using var create = Request(HttpMethod.Post, "/api/scheduled-jobs", owner, new
        {
            name,
            scriptText = "SELECT 1 AS Value;",
            interval = 100,
            unit = "DAY"
        });
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);
    }

    [Fact]
    public async Task AnOwnerCanGrantListAndRevoke()
    {
        var owner = Caller("user", "owner-key", "OrchestratorManager");
        await CreateJobAsync("grant_roundtrip", owner);

        using (var set = Request(
            HttpMethod.Put, "/api/authorization/JOB/grant_roundtrip/GROUP/finance-key", owner,
            new { permission = "EXECUTE" }))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(set)).StatusCode);

        using (var list = Request(HttpMethod.Get, "/api/authorization/JOB/grant_roundtrip", owner))
        {
            var response = await client.SendAsync(list);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var grants = await response.Content.ReadFromJsonAsync<JsonArray>(Json);
            var grant = Assert.Single(grants!);
            Assert.Equal("finance-key", grant!["principalId"]!.GetValue<string>());
        }

        // The granted group can now execute — the grant is real, not merely recorded.
        var member = new OrchestratorCaller("user", "member-key", "member", [], ["finance-key"]);
        using (var trigger = Request(HttpMethod.Post, "/api/scheduled-jobs/grant_roundtrip/trigger", member))
            Assert.NotEqual(HttpStatusCode.Forbidden, (await client.SendAsync(trigger)).StatusCode);

        using (var revoke = Request(
            HttpMethod.Delete, "/api/authorization/JOB/grant_roundtrip/GROUP/finance-key", owner))
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(revoke)).StatusCode);

        using (var trigger = Request(HttpMethod.Post, "/api/scheduled-jobs/grant_roundtrip/trigger", member))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(trigger)).StatusCode);
    }

    [Fact]
    public async Task AReaderCannotChangeGrantsOnAnObjectTheyMerelyReach()
    {
        var owner = Caller("user", "owner-key", "OrchestratorManager");
        await CreateJobAsync("grant_guarded", owner);
        using (var set = Request(
            HttpMethod.Put, "/api/authorization/JOB/grant_guarded/GROUP/readers-key", owner,
            new { permission = "READ" }))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(set)).StatusCode);

        // Reaching an object is not administering it. A READ grant lets this caller see the job and
        // must not let them widen their own access — the mistake that makes a permission model
        // decorative.
        var reader = new OrchestratorCaller("user", "reader-key", "reader", [], ["readers-key"]);

        using (var list = Request(HttpMethod.Get, "/api/authorization/JOB/grant_guarded", reader))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(list)).StatusCode);

        using (var escalate = Request(
            HttpMethod.Put, "/api/authorization/JOB/grant_guarded/GROUP/readers-key", reader,
            new { permission = "MANAGE" }))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(escalate)).StatusCode);

        using (var revoke = Request(
            HttpMethod.Delete, "/api/authorization/JOB/grant_guarded/GROUP/readers-key", reader))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(revoke)).StatusCode);
    }

    [Fact]
    public async Task AnotherTenantIsToldTheObjectDoesNotExistRatherThanThatTheyMayNotTouchIt()
    {
        var owner = new OrchestratorCaller(
            "user", "owner-key", "owner", ["OrchestratorManager"], [], "tenant-a");
        await CreateJobAsync("grant_tenant_bound", owner);

        var stranger = new OrchestratorCaller(
            "user", "stranger-key", "stranger", ["Admin"], [], "tenant-b");

        // 404 and not 403, even for an administrator: the name is resolved in the caller's own tenant
        // before anything is authorized, so confirming it exists elsewhere would be the disclosure the
        // boundary exists to prevent.
        using var list = Request(HttpMethod.Get, "/api/authorization/JOB/grant_tenant_bound", stranger);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(list)).StatusCode);
    }

    [Fact]
    public async Task AGrantOnAnObjectThatDoesNotExistIsNotFound()
    {
        var owner = Caller("user", "owner-key", "OrchestratorManager");

        using var set = Request(
            HttpMethod.Put, "/api/authorization/JOB/no_such_job/GROUP/finance-key", owner,
            new { permission = "READ" });

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(set)).StatusCode);
    }

    [Fact]
    public async Task MalformedKindPrincipalOrPermissionIsRefusedBeforeAnythingIsWritten()
    {
        var owner = Caller("user", "owner-key", "OrchestratorManager");
        await CreateJobAsync("grant_validated", owner);

        foreach (var (path, body) in new (string, object)[]
        {
            ("/api/authorization/WIDGET/grant_validated/GROUP/x", new { permission = "READ" }),
            ("/api/authorization/JOB/grant_validated/ROBOT/x", new { permission = "READ" }),
            ("/api/authorization/JOB/grant_validated/GROUP/x", new { permission = "SUPERUSER" })
        })
        {
            using var request = Request(HttpMethod.Put, path, owner, body);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
        }

        using var list = Request(HttpMethod.Get, "/api/authorization/JOB/grant_validated", owner);
        var response = await client.SendAsync(list);
        Assert.Empty((await response.Content.ReadFromJsonAsync<JsonArray>(Json))!);
    }

    private static HttpRequestMessage Request(
        HttpMethod method, string path, OrchestratorCaller caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }
}
