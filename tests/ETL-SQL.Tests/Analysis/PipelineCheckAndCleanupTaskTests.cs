using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The checking and tidying steps, and the map drawing data where it flows.
///
/// <para><b>Expect schema</b> fails the run when a staged table is missing a column or has the wrong
/// type, which is the check that catches a source changing shape. <b>Drop #temp</b> frees a staged
/// table's memory once nothing later needs it.</para>
///
/// <para>On the map, a line between two steps carries the name of the #temp one writes and the other
/// reads, so the data's path is visible and not only the order the steps run in.</para>
/// </summary>
public class PipelineCheckAndCleanupTaskTests
{
    private readonly PipelineTaskAuthoringService _tasks = new();

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private void AssertWrites(string expected, PipelineTaskDraft draft)
    {
        var preview = _tasks.Preview(draft);
        Assert.True(preview.Applied, preview.Error);
        Assert.Equal(Normalise(expected), Normalise(preview.Script));
    }

    // ── Expect schema ────────────────────────────────────────────────────────

    [Fact]
    public void ExpectingASchemaListsEachColumnOnItsOwnLine()
    {
        AssertWrites("""
            users_have_shape:
            EXPECT SCHEMA #staged_users (
                UserID INT NOT NULL,
                UserName VARCHAR
            );
            """, new PipelineTaskDraft("users_have_shape", PipelineTaskKind.ExpectSchema,
            Source: "#staged_users", Schema: "UserID INT NOT NULL, UserName VARCHAR"));
    }

    [Fact]
    public void ASchemaCheckCanWarnInsteadOfStopping()
    {
        AssertWrites("""
            users_have_shape:
            EXPECT SCHEMA #staged_users (
                Amount DECIMAL(10,2)
            ) ON DRIFT WARN;
            """, new PipelineTaskDraft("users_have_shape", PipelineTaskKind.ExpectSchema,
            Source: "staged_users", Schema: "Amount DECIMAL(10,2)", WarnOnly: true));
    }

    [Theory]
    [InlineData(null, "UserID INT", "#temp table it checks")]
    [InlineData("#staged_users", "", "columns it expects")]
    [InlineData("#staged_users", "UserID", "is not a column and type")]
    [InlineData("#staged_users", "UserID INT; DROP TABLE x", "is not a column and type")]
    public void ASchemaCheckIsRefusedUntilItIsComplete(string? source, string schema, string expected)
    {
        var result = _tasks.Preview(new PipelineTaskDraft("t", PipelineTaskKind.ExpectSchema, Source: source, Schema: schema));

        Assert.False(result.Applied);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Drop #temp ───────────────────────────────────────────────────────────

    [Fact]
    public void DroppingATempTableFreesIt()
    {
        AssertWrites("""
            free_staging:
            DROP TABLE #staged_users;
            """, new PipelineTaskDraft("free_staging", PipelineTaskKind.DropTemp, Source: "staged_users"));
    }

    [Fact]
    public void OnlyATempTableIsDropped()
    {
        var result = _tasks.Preview(new PipelineTaskDraft("t", PipelineTaskKind.DropTemp, Source: "dbo.Users"));

        Assert.False(result.Applied);
        Assert.Contains("is not a usable #temp table name", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("EXPECT SCHEMA #x (UserID INT);", PipelineTaskKind.ExpectSchema)]
    [InlineData("DROP TABLE #x;", PipelineTaskKind.DropTemp)]
    public void AHandWrittenCheckOrDropIsReadAsATask(string statement, PipelineTaskKind kind)
    {
        var script = "SELECT 1 AS UserID INTO #x;\n\nstep:\n" + statement + "\n";

        Assert.Equal(kind, _tasks.Read(script).Single(task => task.Id == "step").Kind);
    }

    // ── Data on the map ──────────────────────────────────────────────────────

    private const string Flow = """
        CREATE CONNECTION m AS MOCKDB();

        read_users:
        SELECT UserID, UserName
        INTO #staged_users
        FROM m.Users;

        clean_users:
        SELECT UserID
        INTO #clean_users
        FROM #staged_users;

        report_count:
        SELECT COUNT(*) AS N
        INTO #count
        FROM #clean_users;

        load_users:
        INSERT INTO m.Users (UserID, UserName)
        SELECT UserID, UserName
        FROM #staged_users;
        """;

    private static ScriptDag Build(string sql) =>
        ScriptDagBuilder.Build(new CoreParser(new Lexer(sql).Tokenize(), sql).Parse());

    /// <summary>The line between a step and the one that reads what it wrote says what flows along it.</summary>
    [Fact]
    public void AnAdjacentHandOverIsLabelledWithTheTempTable()
    {
        var dag = Build(Flow);
        string Id(string key) => dag.Nodes.Single(node => node.Key == key).Id;

        Assert.Contains(dag.Edges, edge => edge.Source == Id("read_users") && edge.Target == Id("clean_users") && edge.Label == "#staged_users");
        Assert.Contains(dag.Edges, edge => edge.Source == Id("clean_users") && edge.Target == Id("report_count") && edge.Label == "#clean_users");
    }

    /// <summary>
    /// When the reader is further down than the next step, the data still has a line: from the step
    /// that last wrote the table, not from whatever happened to run just before.
    /// </summary>
    [Fact]
    public void ADistantReaderGetsItsOwnDataLine()
    {
        var dag = Build(Flow);
        string Id(string key) => dag.Nodes.Single(node => node.Key == key).Id;

        Assert.Contains(dag.Edges, edge => edge.Source == Id("read_users") && edge.Target == Id("load_users") && edge.Label == "#staged_users");
        // The order line into the load is still there, and still says nothing about data.
        Assert.Contains(dag.Edges, edge => edge.Source == Id("report_count") && edge.Target == Id("load_users") && edge.Label is null);
    }

    /// <summary>Only #temp tables flow on the map: a connection's table is where data comes from, not a hand-over.</summary>
    [Fact]
    public void AConnectionTableIsNotADataLine()
    {
        var dag = Build(Flow);

        Assert.DoesNotContain(dag.Edges, edge => edge.Label is { } label && !label.StartsWith('#') && label.Contains("Users", StringComparison.Ordinal));
    }
}
