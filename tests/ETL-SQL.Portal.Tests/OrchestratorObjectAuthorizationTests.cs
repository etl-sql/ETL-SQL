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

    [Fact]
    public async Task GrantsRoundTripAndUpdateWithoutDuplicatingPrincipal()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            OrchestratorObjectKind.Job, "nightly", OrchestratorPrincipalKind.Group, "7",
            OrchestratorObjectPermission.Read, "user:1"));
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            OrchestratorObjectKind.Job, "nightly", OrchestratorPrincipalKind.Group, "7",
            OrchestratorObjectPermission.Execute, "user:1"));

        var grant = Assert.Single(await store.GetObjectGrantsAsync(
            OrchestratorObjectKind.Job, "NIGHTLY"));
        Assert.Equal(OrchestratorObjectPermission.Execute, grant.Permission);
        Assert.Equal(2, grant.Version);
    }

    [Fact]
    public async Task ReadExecuteOverrideAndManageRemainDistinct()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        var authorization = new OrchestratorObjectAuthorizationService(store);
        var reader = new OrchestratorCaller("user", "20", "reader", [], ["7"]);
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            OrchestratorObjectKind.Job, "nightly", OrchestratorPrincipalKind.Group, "7",
            OrchestratorObjectPermission.Execute, "user:1"));

        Assert.True(await authorization.CanAsync(
            reader, OrchestratorObjectKind.Job, "nightly", OrchestratorObjectPermission.Read, "user:1"));
        Assert.True(await authorization.CanAsync(
            reader, OrchestratorObjectKind.Job, "nightly", OrchestratorObjectPermission.Execute, "user:1"));
        Assert.False(await authorization.CanAsync(
            reader, OrchestratorObjectKind.Job, "nightly", OrchestratorObjectPermission.Override, "user:1"));
        Assert.False(await authorization.CanAsync(
            reader, OrchestratorObjectKind.Job, "nightly", OrchestratorObjectPermission.Manage, "user:1"));
    }

    [Fact]
    public async Task OwnerAdminAndUnrelatedReachablePrincipalHaveExpectedAuthority()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        var authorization = new OrchestratorObjectAuthorizationService(store);
        var owner = new OrchestratorCaller("user", "1", "owner", [], []);
        var admin = new OrchestratorCaller("user", "2", "admin", ["Admin"], []);
        var stranger = new OrchestratorCaller("user", "3", "stranger", ["OrchestratorManager"], []);

        Assert.True(await authorization.CanAsync(
            owner, OrchestratorObjectKind.Job, "nightly", OrchestratorObjectPermission.Manage, "user:1"));
        Assert.True(await authorization.CanAsync(
            admin, OrchestratorObjectKind.Job, "nightly", OrchestratorObjectPermission.Manage, "user:1"));
        Assert.False(await authorization.CanAsync(
            stranger, OrchestratorObjectKind.Job, "nightly", OrchestratorObjectPermission.Read, "user:1"));
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
