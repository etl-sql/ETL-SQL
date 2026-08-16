using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Portal.Tests;

public sealed class OrchestratorObjectAuthorizationTests : IDisposable
{
    private readonly string dbPath = Path.Combine(
        Path.GetTempPath(), $"orchestrator_acl_{Guid.NewGuid():N}.db");

    /// <summary>
    /// Saves a job in <paramref name="tenantId"/> and returns its surrogate id. Grants hang off that
    /// id, never the name, which is what makes two tenants' <c>nightly</c> two different objects.
    /// </summary>
    private static async Task<string> SaveJobAsync(
        SQLiteJobHistoryStore store, string name, string? tenantId)
    {
        await store.SaveJobAsync(new JobDefinition(
            name, "SELECT 1;", 1, "HOUR", null, null, null, TenantId: tenantId));
        return (await store.GetJobAsync(tenantId, name))!.Id.Value;
    }

    [Fact]
    public async Task GrantsRoundTripAndUpdateWithoutDuplicatingPrincipal()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        var jobId = await SaveJobAsync(store, "nightly", null);

        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.Group, "7",
            OrchestratorObjectPermission.Read, "user:1"));
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.Group, "7",
            OrchestratorObjectPermission.Execute, "user:1"));

        var grant = Assert.Single(await store.GetObjectGrantsAsync(jobId));
        Assert.Equal(OrchestratorObjectPermission.Execute, grant.Permission);
        Assert.Equal(2, grant.Version);
    }

    [Fact]
    public async Task ReadExecuteOverrideAndManageRemainDistinct()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        var authorization = new OrchestratorObjectAuthorizationService(store);
        var jobId = await SaveJobAsync(store, "nightly", null);
        var reader = new OrchestratorCaller("user", "20", "reader", [], ["7"]);
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.Group, "7",
            OrchestratorObjectPermission.Execute, "user:1"));

        Assert.True(await authorization.CanAsync(
            reader, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Read, "user:1"));
        Assert.True(await authorization.CanAsync(
            reader, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Execute, "user:1"));
        Assert.False(await authorization.CanAsync(
            reader, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Override, "user:1"));
        Assert.False(await authorization.CanAsync(
            reader, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Manage, "user:1"));
    }

    [Fact]
    public async Task OwnerAdminAndUnrelatedReachablePrincipalHaveExpectedAuthority()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        var authorization = new OrchestratorObjectAuthorizationService(store);
        var jobId = await SaveJobAsync(store, "nightly", null);
        var owner = new OrchestratorCaller("user", "1", "owner", [], []);
        var admin = new OrchestratorCaller("user", "2", "admin", ["Admin"], []);
        var stranger = new OrchestratorCaller("user", "3", "stranger", ["OrchestratorManager"], []);

        Assert.True(await authorization.CanAsync(
            owner, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Manage, "user:1"));
        Assert.True(await authorization.CanAsync(
            admin, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Manage, "user:1"));
        Assert.False(await authorization.CanAsync(
            stranger, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Read, "user:1"));
    }

    [Fact]
    public async Task SameJobNameInTwoTenantsAreSeparateObjectsWithSeparateGrants()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        var authorization = new OrchestratorObjectAuthorizationService(store);
        var acmeJob = await SaveJobAsync(store, "nightly", "acme");
        var evilJob = await SaveJobAsync(store, "nightly", "acme-evil");

        Assert.NotEqual(acmeJob, evilJob);

        // Granted on acme's job only.
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            acmeJob, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.Group, "7",
            OrchestratorObjectPermission.Execute, "user:1"));

        var acmeMember = new OrchestratorCaller("user", "20", "member", [], ["7"], "acme");

        Assert.True(await authorization.CanAsync(
            acmeMember, OrchestratorObjectKind.Job, acmeJob, "acme",
            OrchestratorObjectPermission.Execute, null));
        // The grant does not reach the other tenant's job of the same name.
        Assert.Empty(await store.GetObjectGrantsAsync(evilJob));
        Assert.False(await authorization.CanAsync(
            acmeMember, OrchestratorObjectKind.Job, evilJob, "acme-evil",
            OrchestratorObjectPermission.Read, null));
    }

    [Fact]
    public async Task TenantBoundaryOutranksOwnershipAndAdmin()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        var authorization = new OrchestratorObjectAuthorizationService(store);
        var jobId = await SaveJobAsync(store, "nightly", "acme");

        // Both would otherwise be allowed: one owns the object, the other is an administrator. Being
        // in the wrong tenant denies before either is consulted — the defence-in-depth check that
        // makes a forgotten endpoint filter non-fatal.
        var foreignOwner = new OrchestratorCaller("user", "1", "owner", [], [], "acme-evil");
        var foreignAdmin = new OrchestratorCaller("user", "2", "admin", ["Admin"], [], "acme-evil");
        var unboundAdmin = new OrchestratorCaller("user", "3", "solo-admin", ["Admin"], []);

        Assert.False(await authorization.CanAsync(
            foreignOwner, OrchestratorObjectKind.Job, jobId, "acme",
            OrchestratorObjectPermission.Read, "user:1"));
        Assert.False(await authorization.CanAsync(
            foreignAdmin, OrchestratorObjectKind.Job, jobId, "acme",
            OrchestratorObjectPermission.Manage, "user:1"));
        // An unbound (Solo) caller does not inherit a tenant's objects either.
        Assert.False(await authorization.CanAsync(
            unboundAdmin, OrchestratorObjectKind.Job, jobId, "acme",
            OrchestratorObjectPermission.Read, "user:1"));
    }

    [Fact]
    public async Task RecreatingADroppedNameDoesNotInheritItsGrants()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        var authorization = new OrchestratorObjectAuthorizationService(store);
        var originalId = await SaveJobAsync(store, "nightly", null);
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            originalId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.Group, "7",
            OrchestratorObjectPermission.Manage, "user:1"));

        await store.DeleteObjectGrantsAsync(originalId);
        await store.DeleteJobAsync(JobId.From(originalId));

        var recreatedId = await SaveJobAsync(store, "nightly", null);
        var member = new OrchestratorCaller("user", "20", "member", [], ["7"]);

        Assert.NotEqual(originalId, recreatedId);
        Assert.False(await authorization.CanAsync(
            member, OrchestratorObjectKind.Job, recreatedId, null,
            OrchestratorObjectPermission.Read, null));
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
