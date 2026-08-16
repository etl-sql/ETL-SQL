using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Ownership over an object's life: who it starts with, who may change it, what happens when nobody
/// owns it, and what a name inherits when it comes back.
///
/// <para>Ownership is authority, not decoration — an owner may manage their object, including who
/// else reaches it — so the questions here are the ones that decide access after the people involved
/// have changed. They run against a real Orchestrator rather than the store directly, because the
/// rule being tested is enforced at the request boundary and a store-level test would prove only that
/// the SQL does what the SQL does.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class OrchestratorOwnershipLifecycleTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string OwnerKey = "9b1c77d2f4e84a1e8c0d6b3a5f27e410";
    private const string SuccessorKey = "c47ee0b18a9d4f3ea1b25c60d9f83a77";

    private readonly OrchestratorWebFactory factory = new(requireFederatedIdentity: true);
    private readonly HttpClient client;

    public OrchestratorOwnershipLifecycleTests() => client = factory.CreateClient();

    private static OrchestratorCaller Admin(string id = "admin-key") =>
        new("user", id, id, ["Admin"], []);

    private static OrchestratorCaller Manager(string id) =>
        new("user", id, id, ["OrchestratorManager"], []);

    [Fact]
    public async Task AnOwnerWhoLeavesNoLongerStrandsTheObject()
    {
        // The case this exists for: CreatedBy is immutable by design, so before reassignment an owner
        // who left made their objects administrator-only forever.
        var owner = Manager(OwnerKey);
        await CreateJobAsync("owner_reassigned", owner);

        using (var reassign = await SendAsync(
            HttpMethod.Put, "/api/authorization/JOB/owner_reassigned/owner", Admin(),
            new { principalKind = "USER", principalId = SuccessorKey }))
        {
            Assert.Equal(HttpStatusCode.OK, reassign.StatusCode);
            var body = await reassign.Content.ReadFromJsonAsync<JsonObject>(Json);
            Assert.Equal($"user:{OwnerKey}", body!["previousOwner"]!.GetValue<string>());
            Assert.Equal($"user:{SuccessorKey}", body["owner"]!.GetValue<string>());
        }

        // The successor can now administer it, and the previous owner cannot: reassignment moves the
        // authority rather than adding to it.
        using (var successorReads = await SendAsync(
            HttpMethod.Get, "/api/authorization/JOB/owner_reassigned", Manager(SuccessorKey)))
            Assert.Equal(HttpStatusCode.OK, successorReads.StatusCode);
        using (var formerReads = await SendAsync(
            HttpMethod.Get, "/api/authorization/JOB/owner_reassigned", owner))
            Assert.Equal(HttpStatusCode.Forbidden, formerReads.StatusCode);
    }

    [Fact]
    public async Task AnOwnerCannotHandOnTheirOwnObject()
    {
        // An owner may manage their object, which is exactly why they may not reassign it: ownership
        // is the authority grants are administered from, so passing it on would let an owner widen
        // access without anyone administering it.
        var owner = Manager(OwnerKey);
        await CreateJobAsync("owner_guarded", owner);

        using var attempt = await SendAsync(
            HttpMethod.Put, "/api/authorization/JOB/owner_guarded/owner", owner,
            new { principalKind = "USER", principalId = SuccessorKey });

        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
        Assert.Equal($"user:{OwnerKey}", await ReadOwnerAsync("owner_guarded"));
    }

    [Fact]
    public async Task AGroupCannotOwnAnObjectEvenThoughItCanBeGrantedOne()
    {
        // A group owner would read as owned and behave as unowned: the decision compares ownership
        // against one caller's key, and no caller's key is a group's.
        await CreateJobAsync("owner_group_refused", Manager(OwnerKey));

        using var attempt = await SendAsync(
            HttpMethod.Put, "/api/authorization/JOB/owner_group_refused/owner", Admin(),
            new { principalKind = "GROUP", principalId = "finance-key" });

        Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        Assert.Equal($"user:{OwnerKey}", await ReadOwnerAsync("owner_group_refused"));
    }

    [Fact]
    public async Task AnUnownedObjectIsAdministratorOnlyAndAnEditDoesNotAdoptIt()
    {
        // The pre-existing case: an object created before attribution, or restored from a deployment
        // that had none. Fail-open was rejected, and so was adoption-by-side-effect — an edit must not
        // decide who is accountable, because that answer would then be "whoever touched it last".
        var store = await SeedUnownedJobAsync("legacy_unowned");
        var manager = Manager(OwnerKey);

        using (var read = await SendAsync(HttpMethod.Get, "/api/authorization/JOB/legacy_unowned", manager))
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        using (var adminRead = await SendAsync(HttpMethod.Get, "/api/authorization/JOB/legacy_unowned", Admin()))
            Assert.Equal(HttpStatusCode.OK, adminRead.StatusCode);

        // An administrator edits it — the one principal who can — and it stays unowned.
        var existing = await store.GetJobAsync((string?)null, "legacy_unowned");
        Assert.True(await store.TrySaveJobAsync(
            existing! with { Description = "edited by an administrator", ModifiedBy = "user:admin-key" },
            existing!.Version));

        Assert.Null(await ReadOwnerAsync("legacy_unowned"));
        using var stillDenied = await SendAsync(
            HttpMethod.Get, "/api/authorization/JOB/legacy_unowned", manager);
        Assert.Equal(HttpStatusCode.Forbidden, stillDenied.StatusCode);
    }

    [Fact]
    public async Task AdoptionGivesEveryUnownedObjectAnOwnerAndLeavesOwnedOnesAlone()
    {
        // The solo → team promotion: a box that has just attached a Portal, assigning accountability
        // for everything it already had without touching what already has an owner.
        await SeedUnownedJobAsync("adopt_first");
        await SeedUnownedJobAsync("adopt_second");
        await CreateJobAsync("adopt_already_owned", Manager(OwnerKey));

        using (var listed = await SendAsync(HttpMethod.Get, "/api/authorization/unowned", Admin()))
        {
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var names = (await listed.Content.ReadFromJsonAsync<JsonArray>(Json))!
                .Select(entry => entry!["name"]!.GetValue<string>())
                .ToArray();
            Assert.Contains("adopt_first", names);
            Assert.Contains("adopt_second", names);
            Assert.DoesNotContain("adopt_already_owned", names);
        }

        using (var adopt = await SendAsync(
            HttpMethod.Post, "/api/authorization/adopt", Admin(),
            new { principalKind = "USER", principalId = SuccessorKey }))
        {
            Assert.Equal(HttpStatusCode.OK, adopt.StatusCode);
            var body = await adopt.Content.ReadFromJsonAsync<JsonObject>(Json);
            Assert.Equal(2, body!["count"]!.GetValue<int>());
        }

        Assert.Equal($"user:{SuccessorKey}", await ReadOwnerAsync("adopt_first"));
        Assert.Equal($"user:{SuccessorKey}", await ReadOwnerAsync("adopt_second"));
        Assert.Equal($"user:{OwnerKey}", await ReadOwnerAsync("adopt_already_owned"));

        using var empty = await SendAsync(HttpMethod.Get, "/api/authorization/unowned", Admin());
        Assert.Empty((await empty.Content.ReadFromJsonAsync<JsonArray>(Json))!);
    }

    [Fact]
    public async Task OnlyAnAdministratorSeesOrAdoptsUnownedObjects()
    {
        await SeedUnownedJobAsync("adopt_guarded");
        var manager = Manager(OwnerKey);

        using (var listed = await SendAsync(HttpMethod.Get, "/api/authorization/unowned", manager))
            Assert.Equal(HttpStatusCode.Forbidden, listed.StatusCode);
        using (var adopt = await SendAsync(
            HttpMethod.Post, "/api/authorization/adopt", manager,
            new { principalKind = "USER", principalId = OwnerKey }))
            Assert.Equal(HttpStatusCode.Forbidden, adopt.StatusCode);

        Assert.Null(await ReadOwnerAsync("adopt_guarded"));
    }

    [Fact]
    public async Task ARecreatedNameDoesNotInheritTheGrantsOfTheObjectItReplaces()
    {
        // Names are re-usable; identity is not. A dropped object's grants go with it, and the object
        // that later takes its name starts closed — otherwise deleting and recreating a job would be a
        // way to acquire someone else's access without anyone granting it.
        var owner = Manager(OwnerKey);
        await CreateJobAsync("resurrected", owner);
        using (var grant = await SendAsync(
            HttpMethod.Put, "/api/authorization/JOB/resurrected/GROUP/finance-key", owner,
            new { permission = "EXECUTE" }))
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var member = new OrchestratorCaller("user", "member-key", "member", [], ["finance-key"]);
        using (var beforeDrop = await SendAsync(
            HttpMethod.Post, "/api/scheduled-jobs/resurrected/trigger", member))
            Assert.NotEqual(HttpStatusCode.Forbidden, beforeDrop.StatusCode);

        // Dropping is version-checked, so the current version goes with it.
        var store = (IJobHistoryStore)factory.Services.GetService(typeof(IJobHistoryStore))!;
        var version = (await store.GetJobAsync((string?)null, "resurrected"))!.Version;
        using (var drop = await SendAsync(
            HttpMethod.Delete, "/api/scheduled-jobs/resurrected", owner, version: version))
            Assert.True(drop.IsSuccessStatusCode, $"drop returned {drop.StatusCode}");

        await CreateJobAsync("resurrected", owner);

        using (var grants = await SendAsync(HttpMethod.Get, "/api/authorization/JOB/resurrected", owner))
        {
            Assert.Equal(HttpStatusCode.OK, grants.StatusCode);
            Assert.Empty((await grants.Content.ReadFromJsonAsync<JsonArray>(Json))!);
        }
        using (var afterRecreate = await SendAsync(
            HttpMethod.Post, "/api/scheduled-jobs/resurrected/trigger", member))
            Assert.Equal(HttpStatusCode.Forbidden, afterRecreate.StatusCode);
    }

    /// <summary>
    /// Writes a job with no attribution, the way one arrives from a deployment that predates it or a
    /// restore from one. Seeded through the store rather than the API because the API cannot produce
    /// this state any more — which is the point: it exists in the field and nowhere else.
    /// </summary>
    private async Task<ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore> SeedUnownedJobAsync(string name)
    {
        var store = (ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore)
            factory.Services.GetService(typeof(IJobHistoryStore))!;
        await store.SaveJobAsync(new JobDefinition(
            name, "SELECT 1 AS Value;", 100, "DAY", null, null, null,
            CreatedBy: null, ModifiedBy: null, TenantId: null));
        return store;
    }

    private async Task<string?> ReadOwnerAsync(string name)
    {
        var store = (IJobHistoryStore)factory.Services.GetService(typeof(IJobHistoryStore))!;
        return (await store.GetJobAsync((string?)null, name))?.CreatedBy;
    }

    private async Task CreateJobAsync(string name, OrchestratorCaller owner)
    {
        using var create = await SendAsync(HttpMethod.Post, "/api/scheduled-jobs", owner, new
        {
            name,
            scriptText = "SELECT 1 AS Value;",
            interval = 100,
            unit = "DAY"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, OrchestratorCaller caller, object? body = null, long? version = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        if (version is { } expected)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{expected}\"");
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }
}
