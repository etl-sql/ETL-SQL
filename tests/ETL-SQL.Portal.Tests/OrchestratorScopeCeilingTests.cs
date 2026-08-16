using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The scope ceiling: what a token may do, independently of what its grants say.
///
/// <para>A grant answers "may this principal touch this object". A scope answers "may this token do
/// this kind of thing at all". They are separate questions, and the ceiling is what makes it possible
/// to hand an automation a narrow token without also narrowing the human who owns it — so these tests
/// deliberately pair a broad ACL, and in places an Admin role, with a narrow scope.</para>
/// </summary>
public sealed class OrchestratorScopeCeilingTests : IDisposable
{
    private readonly string dbPath = Path.Combine(
        Path.GetTempPath(), $"orchestrator_scope_{Guid.NewGuid():N}.db");

    private async Task<(SQLiteJobHistoryStore Store, OrchestratorObjectAuthorizationService Authorization, string JobId)>
        SeedGrantedJobAsync(string principalId)
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        await store.SaveJobAsync(new JobDefinition("nightly", "SELECT 1;", 1, "HOUR", null, null, null));
        var jobId = (await store.GetJobAsync((string?)null, "nightly"))!.Id.Value;

        // Deliberately the broadest grant there is, so every denial below is the ceiling and not a
        // missing permission.
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.Service, principalId,
            OrchestratorObjectPermission.Manage, "test"));

        return (store, new OrchestratorObjectAuthorizationService(store), jobId);
    }

    private static OrchestratorCaller Service(string id, params string[] scopes) =>
        new("service", id, id, [], [], null, scopes);

    [Theory]
    [InlineData("orchestrator.read", OrchestratorObjectPermission.Read, true)]
    [InlineData("orchestrator.read", OrchestratorObjectPermission.Execute, false)]
    [InlineData("orchestrator.read", OrchestratorObjectPermission.Override, false)]
    [InlineData("orchestrator.read", OrchestratorObjectPermission.Manage, false)]
    [InlineData("orchestrator.execute", OrchestratorObjectPermission.Read, true)]
    [InlineData("orchestrator.execute", OrchestratorObjectPermission.Execute, true)]
    [InlineData("orchestrator.execute", OrchestratorObjectPermission.Override, true)]
    [InlineData("orchestrator.execute", OrchestratorObjectPermission.Manage, false)]
    [InlineData("orchestrator.publish", OrchestratorObjectPermission.Manage, true)]
    [InlineData("orchestrator.admin", OrchestratorObjectPermission.Manage, true)]
    public async Task AScopeCapsWhatAGrantCanAuthorize(
        string scope, OrchestratorObjectPermission required, bool expected)
    {
        var (_, authorization, jobId) = await SeedGrantedJobAsync("automation");

        Assert.Equal(expected, await authorization.CanAsync(
            Service("automation", scope), OrchestratorObjectKind.Job, jobId, null, required, null));
    }

    [Fact]
    public async Task AServiceTokenWithNoScopesCanDoNothing()
    {
        var (_, authorization, jobId) = await SeedGrantedJobAsync("automation");

        // Not "unscoped means unlimited". A token is issued to an automation for a stated purpose, so
        // the absence of a purpose is the absence of authority — which is also why v1 assertions,
        // which cannot carry scopes, are rejected outright rather than read as unscoped.
        Assert.False(await authorization.CanAsync(
            Service("automation"), OrchestratorObjectKind.Job, jobId, null,
            OrchestratorObjectPermission.Read, null));
    }

    [Fact]
    public async Task AnInteractiveUserIsNotCappedByScopes()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        await store.SaveJobAsync(new JobDefinition("nightly", "SELECT 1;", 1, "HOUR", null, null, null));
        var jobId = (await store.GetJobAsync((string?)null, "nightly"))!.Id.Value;
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.User, "20",
            OrchestratorObjectPermission.Execute, "test"));
        var authorization = new OrchestratorObjectAuthorizationService(store);

        // A person's authority is their roles and grants; the Portal session the assertion came from
        // is what bounded it. Capping them by an empty scope list would lock every human out.
        var member = new OrchestratorCaller("user", "20", "member", [], [], null, []);

        Assert.True(await authorization.CanAsync(
            member, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Execute, null));
    }

    [Fact]
    public async Task TheCeilingOutranksTheAdminRoleAndOwnership()
    {
        var (_, authorization, jobId) = await SeedGrantedJobAsync("automation");

        // Both would otherwise be allowed outright. A narrow token held by a broad principal is the
        // entire reason to issue one, so the ceiling has to be consulted before either.
        var admin = new OrchestratorCaller(
            "service", "automation", "automation", ["Admin"], [], null, ["orchestrator.read"]);
        var owner = new OrchestratorCaller(
            "service", "automation", "automation", [], [], null, ["orchestrator.read"]);

        Assert.False(await authorization.CanAsync(
            admin, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Execute, null));
        Assert.False(await authorization.CanAsync(
            owner, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Execute,
            "service:automation"));
    }

    [Fact]
    public async Task AScopeIsACeilingAndNeverAGrant()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        await store.SaveJobAsync(new JobDefinition("nightly", "SELECT 1;", 1, "HOUR", null, null, null));
        var jobId = (await store.GetJobAsync((string?)null, "nightly"))!.Id.Value;
        var authorization = new OrchestratorObjectAuthorizationService(store);

        // The widest scope there is, and no grant at all. A scope says what a token may attempt, never
        // what it may reach: a publish account still cannot touch a job it was not granted.
        Assert.False(await authorization.CanAsync(
            Service("automation", "orchestrator.admin", "orchestrator.publish"),
            OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Read, null));
    }

    [Fact]
    public async Task NonOrchestratorScopesGrantNothingHere()
    {
        var (_, authorization, jobId) = await SeedGrantedJobAsync("automation");

        // A token usually carries portal scopes too; none of them is authority over a job.
        Assert.False(await authorization.CanAsync(
            Service("automation", "portal.read", "reports.execute", "admin.identity"),
            OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Read, null));
    }

    [Fact]
    public void AVersionOneAssertionIsRefused()
    {
        const string secret = "unit-test-orchestrator-identity-signing-secret";

        // Forged as v1 by hand — there is no longer a way to mint one — to prove the version check
        // refuses it rather than reading a scopeless token as unlimited.
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1,
            issuer = OrchestratorIdentityAssertion.Issuer,
            audience = OrchestratorIdentityAssertion.Audience,
            issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds(),
            nonce = Guid.NewGuid().ToString("N"),
            subjectType = "service",
            subjectId = "legacy",
            displayName = "legacy",
            roles = Array.Empty<string>(),
            groupIds = Array.Empty<string>()
        });
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes(encoded)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.False(OrchestratorIdentityAssertion.TryValidate(
            encoded + "." + signature, secret, out _, out var error));
        Assert.Contains("version", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScopesSurviveTheAssertionRoundTrip()
    {
        const string secret = "unit-test-orchestrator-identity-signing-secret";
        var issued = OrchestratorIdentityAssertion.Create(
            new OrchestratorCaller("service", "automation", "automation", [], [], "acme",
                ["orchestrator.read", "orchestrator.execute"]),
            secret);

        Assert.True(OrchestratorIdentityAssertion.TryValidate(issued, secret, out var caller, out _));
        Assert.Equal(
            ["orchestrator.execute", "orchestrator.read"],
            caller!.EffectiveScopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
