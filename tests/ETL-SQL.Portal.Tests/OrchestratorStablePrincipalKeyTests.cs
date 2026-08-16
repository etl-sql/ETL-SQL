using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Grants follow the principal, not the row.
///
/// <para>A numeric row id is only stable for as long as the row is. Renaming an account,
/// re-provisioning it from OIDC, or restoring the Portal database into a rebuilt environment can each
/// produce a different row holding the same id — and a grant keyed on the id would then belong to
/// whoever holds it now. None of those failures announces itself: the grant still resolves, still
/// matches, and simply matches the wrong person. Keying on an identifier that is minted once and never
/// reissued turns all three from silent into impossible.</para>
/// </summary>
public sealed class OrchestratorStablePrincipalKeyTests : IDisposable
{
    private readonly string dbPath = Path.Combine(
        Path.GetTempPath(), $"orchestrator_keys_{Guid.NewGuid():N}.db");

    private async Task<(SQLiteJobHistoryStore Store, OrchestratorObjectAuthorizationService Authorization, string JobId)>
        NewJobAsync()
    {
        var store = new SQLiteJobHistoryStore(dbPath);
        await store.SaveJobAsync(new JobDefinition("nightly", "SELECT 1;", 1, "HOUR", null, null, null));
        var jobId = (await store.GetJobAsync((string?)null, "nightly"))!.Id.Value;
        return (store, new OrchestratorObjectAuthorizationService(store), jobId);
    }

    private static OrchestratorCaller Member(string subjectKey, params string[] groupKeys) =>
        new("user", subjectKey, "member", [], groupKeys);

    [Fact]
    public async Task RenamingAGroupPreservesItsGrants()
    {
        var (store, authorization, jobId) = await NewJobAsync();
        var financeKey = PortalPrincipalKey.New();
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.Group, financeKey,
            OrchestratorObjectPermission.Execute, "user:1"));

        // The group is renamed "Finance" → "Financial Planning". Its key does not change, so nothing
        // about the grant does either — the rename is invisible to authorization, which is the point.
        Assert.True(await authorization.CanAsync(
            Member(PortalPrincipalKey.New(), financeKey), OrchestratorObjectKind.Job, jobId, null,
            OrchestratorObjectPermission.Execute, null));
    }

    [Fact]
    public async Task AnOidcUserReprovisionedUnderTheSameSubjectKeepsTheirGrants()
    {
        var (store, authorization, jobId) = await NewJobAsync();
        var aliceKey = PortalPrincipalKey.New();
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.User, aliceKey,
            OrchestratorObjectPermission.Manage, "user:1"));

        // Re-provisioning replaces the Portal row — new numeric id, same account, same principal key,
        // because the key is minted with the principal and not with the row that happens to carry it.
        Assert.True(await authorization.CanAsync(
            Member(aliceKey), OrchestratorObjectKind.Job, jobId, null,
            OrchestratorObjectPermission.Manage, null));
    }

    [Fact]
    public async Task ARebuiltPortalDoesNotHandAGrantToWhoeverInheritsTheOldRowId()
    {
        var (store, authorization, jobId) = await NewJobAsync();
        var aliceKey = PortalPrincipalKey.New();
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.User, aliceKey,
            OrchestratorObjectPermission.Manage, "user:1"));

        // The Portal database is rebuilt and Bob is created first, so Bob is now user 1 — the id Alice
        // used to have. Under the old scheme Bob would silently inherit Alice's grant. Bob's key was
        // minted for Bob, so he inherits nothing.
        var bob = Member(PortalPrincipalKey.New());

        Assert.False(await authorization.CanAsync(
            bob, OrchestratorObjectKind.Job, jobId, null, OrchestratorObjectPermission.Read, null));
    }

    [Fact]
    public async Task AGrantWhosePrincipalNoLongerExistsMatchesNobody()
    {
        var (store, authorization, jobId) = await NewJobAsync();

        // The principal was deleted; its key is never reissued, so the grant is an orphan. It must
        // grant nothing rather than widen — an unresolvable key that matched anyone would be the
        // failure this whole scheme exists to remove.
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
            jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.User, PortalPrincipalKey.New(),
            OrchestratorObjectPermission.Manage, "user:1"));

        Assert.False(await authorization.CanAsync(
            Member(PortalPrincipalKey.New()), OrchestratorObjectKind.Job, jobId, null,
            OrchestratorObjectPermission.Read, null));
    }

    [Fact]
    public void AKeyIsRecognisableAsOneSoAnOrphanCanBeToldFromAnUnkeyedRow()
    {
        // "No key yet" is a row from before the column existed and is repaired by backfill. "A key
        // that resolves to nothing" is an orphaned grant. Both deny, but only one is a bug, so they
        // have to be distinguishable to whoever has to fix it.
        Assert.True(PortalPrincipalKey.IsWellFormed(PortalPrincipalKey.New()));
        Assert.False(PortalPrincipalKey.IsWellFormed(null));
        Assert.False(PortalPrincipalKey.IsWellFormed(""));
        Assert.False(PortalPrincipalKey.IsWellFormed("7"));
        Assert.False(PortalPrincipalKey.IsWellFormed("not-a-key"));
    }

    [Fact]
    public void EveryMintedKeyIsDistinct()
    {
        var keys = Enumerable.Range(0, 1000).Select(_ => PortalPrincipalKey.New()).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(1000, keys.Count);
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
