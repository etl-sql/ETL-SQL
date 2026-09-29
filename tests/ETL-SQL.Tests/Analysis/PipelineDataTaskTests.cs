using ETL_SQL.Analysis.Services;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The data-moving tasks: read a table into a <c>#temp</c>, and insert or upsert a <c>#temp</c> into a
/// table. These are what an ETL pipeline is for, and until now the canvas could not write any of them —
/// staging needed typing, and the only data chip ran vendor SQL on the remote side.
///
/// <para>The exact text matters here more than anywhere: the author sees it written into the script,
/// and it is how they learn what the statement looks like. So these pin the bytes.</para>
/// </summary>
public class PipelineDataTaskTests
{
    private readonly PipelineTaskAuthoringService _tasks = new();

    private const string Seed = "CREATE CONNECTION warehouse AS MOCKDB();\n";

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private void AssertWrites(string expected, PipelineTaskDraft draft) => Assert.Equal(Normalise(expected), Written(draft));

    private string Written(PipelineTaskDraft draft)
    {
        var preview = _tasks.Preview(draft);
        Assert.True(preview.Applied, preview.Error);
        return Normalise(preview.Script);
    }

    [Fact]
    public void ReadingATableWritesSelectInto()
    {
        AssertWrites("""
            read_users:
            SELECT UserID, UserName
            INTO #staged_users
            FROM warehouse.Users
            WHERE Active = 1;
            """, new PipelineTaskDraft("read_users", PipelineTaskKind.Extract,
            Connection: "warehouse", Table: "Users", Columns: "UserID, UserName",
            Condition: "Active = 1", Target: "#staged_users"));
    }

    [Fact]
    public void ReadingEveryColumnWithNoFilterIsTheShortForm()
    {
        AssertWrites("""
            read_users:
            SELECT *
            INTO #staged_users
            FROM warehouse.dbo.Users;
            """, new PipelineTaskDraft("read_users", PipelineTaskKind.Extract,
            Connection: "warehouse", Table: "dbo.Users", Target: "staged_users"));
    }

    [Fact]
    public void LoadingWritesInsertSelect()
    {
        AssertWrites("""
            load_users:
            INSERT INTO warehouse.UsersArchive (UserID, UserName)
            SELECT UserID, UserName
            FROM #staged_users;
            """, new PipelineTaskDraft("load_users", PipelineTaskKind.Load,
            Connection: "warehouse", Table: "UsersArchive", Columns: "UserID, UserName", Source: "#staged_users"));
    }

    [Fact]
    public void UpsertingWritesAMergeOnTheKeys()
    {
        AssertWrites("""
            sync_users:
            MERGE INTO warehouse.Users AS tgt
            USING #staged_users AS src
                ON tgt.UserID = src.UserID
            WHEN MATCHED THEN
                UPDATE SET tgt.UserName = src.UserName, tgt.Email = src.Email
            WHEN NOT MATCHED THEN
                INSERT (UserID, UserName, Email)
                VALUES (src.UserID, src.UserName, src.Email);
            """, new PipelineTaskDraft("sync_users", PipelineTaskKind.Upsert,
            Connection: "warehouse", Table: "Users", Keys: "UserID", Columns: "UserName, Email",
            Source: "#staged_users"));
    }

    /// <summary>With nothing but keys there is nothing to update, so only new rows are inserted.</summary>
    [Fact]
    public void AnUpsertOfKeysAloneOnlyInsertsNewRows()
    {
        AssertWrites("""
            sync_ids:
            MERGE INTO warehouse.Users AS tgt
            USING #staged_users AS src
                ON tgt.TenantID = src.TenantID AND tgt.UserID = src.UserID
            WHEN NOT MATCHED THEN
                INSERT (TenantID, UserID)
                VALUES (src.TenantID, src.UserID);
            """, new PipelineTaskDraft("sync_ids", PipelineTaskKind.Upsert,
            Connection: "warehouse", Table: "Users", Keys: "TenantID, UserID", Source: "#staged_users"));
    }

    /// <summary>The preview is the same text an Add writes, and it writes nothing.</summary>
    [Fact]
    public void ThePreviewIsExactlyWhatAnAddWrites()
    {
        var draft = new PipelineTaskDraft("read_users", PipelineTaskKind.Extract,
            Connection: "warehouse", Table: "Users", Target: "#staged_users");

        var added = _tasks.Add(Seed, draft);
        Assert.True(added.Applied, added.Error);

        Assert.EndsWith(Written(draft), Normalise(added.Script), StringComparison.Ordinal);
    }

    [Fact]
    public void AnIncompletePreviewSaysWhatIsMissing()
    {
        var preview = _tasks.Preview(new PipelineTaskDraft("read_users", PipelineTaskKind.Extract, Connection: "warehouse"));

        Assert.False(preview.Applied);
        Assert.Contains("table it reads", preview.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A labelled statement written by hand is a task of the same kind, not a read-only stage.</summary>
    [Theory]
    [InlineData("SELECT * INTO #x FROM warehouse.Users;", PipelineTaskKind.Extract)]
    [InlineData("INSERT INTO warehouse.Archive (UserID) SELECT UserID FROM #x;", PipelineTaskKind.Load)]
    [InlineData("MERGE INTO warehouse.Users AS t USING #x AS s ON t.UserID = s.UserID WHEN NOT MATCHED THEN INSERT (UserID) VALUES (s.UserID);", PipelineTaskKind.Upsert)]
    public void AHandWrittenDataStatementIsReadAsATask(string statement, PipelineTaskKind kind)
    {
        var script = Seed + "SELECT 1 AS UserID INTO #x;\n\nstep:\n" + statement + "\n";

        Assert.Equal(kind, _tasks.Read(script).Single(task => task.Id == "step").Kind);
    }
}
