using ETL_SQL.WorkstationEditor;
using Xunit;

namespace ETL_SQL.Tests.WorkstationEditor;

/// <summary>
/// The self-installed host's recovery drafts. They are unsaved script text on the author's own
/// disk, so the questions are the Portal's: whose draft is it, may it hold a credential, and does
/// it go away.
/// </summary>
public sealed class WorkstationDraftStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "etlsql-drafts", Guid.NewGuid().ToString("N"));

    private string Dir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private WorkstationDraftStore Store(string workspace, TimeSpan? retention = null) =>
        new(new WorkstationWorkspace(Dir(workspace), readOnly: false), Dir("drafts"), retention);

    [Fact]
    public async Task ADraftRoundTripsAndIsDeleted()
    {
        var store = Store("ws");
        Assert.True(await store.WriteAsync("reports/a.rptsql", new WorkstationDraft("-- edit", "r1", default), default));

        var draft = await store.ReadAsync("reports/a.rptsql", default);
        Assert.Equal(("-- edit", "r1"), (draft!.Content, draft.BaseSourceRevision));

        store.Delete("reports/a.rptsql");
        Assert.Null(await store.ReadAsync("reports/a.rptsql", default));
    }

    [Fact]
    public async Task TwoWorkspacesWithTheSameFileDoNotShareADraft()
    {
        var first = Store("one");
        var second = Store("two");
        await first.WriteAsync("a.etlsql", new WorkstationDraft("-- one", null, default), default);

        Assert.Null(await second.ReadAsync("a.etlsql", default));
        Assert.Equal("-- one", (await first.ReadAsync("a.etlsql", default))!.Content);
    }

    [Fact]
    public async Task ADraftHoldingAPlaintextCredentialIsNotKept()
    {
        var store = Store("ws");
        Assert.False(await store.WriteAsync("a.etlsql",
            new WorkstationDraft("CREATE CONNECTION db AS MSSQL(SERVER = 'h', PASSWORD = 'hunter2');", null, default), default));
        Assert.Null(await store.ReadAsync("a.etlsql", default));
        Assert.True(await store.WriteAsync("a.etlsql",
            new WorkstationDraft("CREATE CONNECTION db AS MSSQL(SERVER = 'h', PASSWORD = 'SECRET:db');", null, default), default));
    }

    [Fact]
    public async Task AnUntouchedDraftExpires()
    {
        var store = Store("ws", TimeSpan.FromDays(7));
        await store.WriteAsync("a.etlsql", new WorkstationDraft("-- old", null, default), default);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_root, "drafts"), "*.json"));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8));

        Assert.Null(await store.ReadAsync("a.etlsql", default));
        Assert.False(File.Exists(file));
    }

    [Theory]
    [InlineData("../outside.etlsql")]
    [InlineData("notes.txt")]
    public async Task ADraftIsKeptOnlyForAFileTheHostWouldOpen(string path)
    {
        var store = Store("ws");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.WriteAsync(path, new WorkstationDraft("-- x", null, default), default));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort cleanup must not mask a failed assertion */ }
    }
}
