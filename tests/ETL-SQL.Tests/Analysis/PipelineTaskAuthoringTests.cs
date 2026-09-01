using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The round-trip spike behind editable pipeline nodes.
///
/// <para>The pipeline canvas has been read-only on the stated condition that any add/connect editing
/// must first prove two things: everything it emits is accepted by the canonical parser and
/// formatter, and everything it does not touch survives byte for byte. These tests are that proof —
/// they assert on exact bytes rather than on "the script still contains X", because a patcher that
/// reflows a hand-formatted body passes the second kind of assertion while quietly rewriting the
/// author's file.</para>
/// </summary>
public class PipelineTaskAuthoringTests
{
    private readonly PipelineTaskAuthoringService _tasks = new();

    /// <summary>
    /// A script with the things a patcher is most likely to damage: a comment, an odd indentation,
    /// a semicolon inside a string, and statements the canvas does not model at all.
    /// </summary>
    private const string Script = """
        -- Nightly load. Do not reformat: the spacing below is deliberate.
        CREATE CONNECTION staging_db AS SQLSERVER(
            SERVER   = 'db01;failover=db02',
            DATABASE = 'ops'
        );

        load_orders:
        EXECUTE staging_db BEGIN
                SELECT OrderId,
                       Total
                FROM   dbo.Orders;
        END;

        SELECT OrderId INTO #orders FROM staging_db.Orders;

        archive_orders:
        EXECUTE staging_db BEGIN
            INSERT INTO dbo.OrdersArchive SELECT * FROM dbo.Orders;
        END;
        """;

    private static void AssertParses(string script)
    {
        var ast = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
        var error = ast.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(error is null, $"Emitted script does not parse: {error?.Message}\n---\n{script}");
    }

    /// <summary>
    /// Everything the edit did not claim to touch must be identical. Comparing the two scripts with
    /// the edited task's text removed from each is the only assertion that actually catches a
    /// patcher which "helpfully" reindents the rest of the file.
    /// </summary>
    private static void AssertUntouched(string before, string after, params string[] removedFromEach)
    {
        static string Strip(string script, IEnumerable<string> fragments)
        {
            foreach (var fragment in fragments)
                script = script.Replace(fragment, string.Empty, StringComparison.Ordinal);
            return script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).Trim();
        }

        Assert.Equal(Strip(before, removedFromEach), Strip(after, removedFromEach));
    }

    [Fact]
    public void Read_FindsOnlyLabelledExecuteBlocks()
    {
        var tasks = _tasks.Read(Script);

        Assert.Equal(2, tasks.Count);
        Assert.Equal("load_orders", tasks[0].Id);
        Assert.Equal("staging_db", tasks[0].Connection);
        Assert.Contains("FROM   dbo.Orders;", tasks[0].Body, StringComparison.Ordinal);
        Assert.Equal("archive_orders", tasks[1].Id);

        // The unlabelled SELECT INTO between them is a projection stage, not an editable task.
        Assert.DoesNotContain(tasks, task => task.Body.Contains("#orders", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_IgnoresALabelInsideABlock()
    {
        // A label inside a block is scoped to that block; lifting or renaming it from the canvas
        // would change what the script means, so it is not offered as a task at all.
        const string nested = """
            IF 1 = 1 BEGIN
                inner_task:
                EXECUTE staging_db BEGIN
                    SELECT 1;
                END;
            END
            """;

        Assert.Empty(_tasks.Read(nested));
    }

    [Fact]
    public void Add_EmitsParserValidScriptAndLeavesEverythingElseByteIdentical()
    {
        var result = _tasks.Add(Script, new PipelineTaskDraft(
            "refresh_totals", "staging_db", "UPDATE dbo.Totals SET Amount = 0;", After: "load_orders"));

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);

        var added = _tasks.Read(result.Script).Single(task => task.Id == "refresh_totals");
        Assert.Equal("staging_db", added.Connection);
        Assert.Equal("UPDATE dbo.Totals SET Amount = 0;", added.Body.Trim());

        // Inserted in the requested position, not appended.
        Assert.Equal(["load_orders", "refresh_totals", "archive_orders"], _tasks.Read(result.Script).Select(t => t.Id));

        var emitted = result.Script[added.StartOffset..added.EndOffset];
        AssertUntouched(Script, result.Script, emitted);
    }

    [Fact]
    public void Add_AppendsWhenNoAnchorIsNamed()
    {
        var result = _tasks.Add(Script, new PipelineTaskDraft("cleanup", "staging_db", "DELETE FROM dbo.Scratch;"));

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        Assert.Equal("cleanup", _tasks.Read(result.Script)[^1].Id);
        Assert.StartsWith(Script, result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_RefusesADuplicateLabelRatherThanWritingAnAmbiguousScript()
    {
        var result = _tasks.Add(Script, new PipelineTaskDraft("load_orders", "staging_db", "SELECT 1;"));

        Assert.False(result.Applied);
        Assert.Contains("already has a task", result.Error!, StringComparison.Ordinal);
        Assert.Equal(Script, result.Script);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("1st")]
    [InlineData("drop; DROP TABLE x")]
    [InlineData("")]
    public void Add_RefusesALabelThatWouldNotLexAsOneIdentifier(string id)
    {
        var result = _tasks.Add(Script, new PipelineTaskDraft(id, "staging_db", "SELECT 1;"));

        Assert.False(result.Applied);
        Assert.Equal(Script, result.Script);
    }

    [Fact]
    public void Update_RelabelsWithoutReflowingTheBody()
    {
        var before = _tasks.Read(Script).Single(task => task.Id == "load_orders");
        var result = _tasks.Update(Script, "load_orders", newId: "load_orders_v2");

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);

        var after = _tasks.Read(result.Script).Single(task => task.Id == "load_orders_v2");
        Assert.Equal(before.Body, after.Body);

        // Only the label's own bytes moved: the two scripts differ by exactly the label name.
        Assert.Equal(Script.Replace("load_orders:", "load_orders_v2:", StringComparison.Ordinal), result.Script);
    }

    [Fact]
    public void Update_RepointsTheConnectionAndReplacesTheBodyIndependently()
    {
        var repointed = _tasks.Update(Script, "load_orders", connection: "warehouse_db");
        Assert.True(repointed.Applied, repointed.Error);
        AssertParses(repointed.Script);
        Assert.Equal("warehouse_db", _tasks.Read(repointed.Script).Single(t => t.Id == "load_orders").Connection);
        Assert.Equal(
            Script.Replace("EXECUTE staging_db BEGIN\n        SELECT OrderId,", "EXECUTE warehouse_db BEGIN\n        SELECT OrderId,", StringComparison.Ordinal),
            repointed.Script.Replace("\r\n", "\n", StringComparison.Ordinal));

        var rebodied = _tasks.Update(Script, "archive_orders", body: "TRUNCATE TABLE dbo.OrdersArchive;");
        Assert.True(rebodied.Applied, rebodied.Error);
        AssertParses(rebodied.Script);
        Assert.Equal("TRUNCATE TABLE dbo.OrdersArchive;", _tasks.Read(rebodied.Script).Single(t => t.Id == "archive_orders").Body.Trim());
        Assert.Contains("FROM   dbo.Orders;", rebodied.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_RefusesToRenameAGotoTarget()
    {
        const string withJump = """
            IF 1 = 1 BEGIN
                GOTO load_orders;
            END

            load_orders:
            EXECUTE staging_db BEGIN
                SELECT 1;
            END;
            """;

        var result = _tasks.Update(withJump, "load_orders", newId: "renamed");

        Assert.False(result.Applied);
        Assert.Contains("GOTO target", result.Error!, StringComparison.Ordinal);
        Assert.Equal(withJump, result.Script);
    }

    [Fact]
    public void Move_RelocatesTheTasksOwnBytesAndReordersTheFlow()
    {
        var before = _tasks.Read(Script).Single(task => task.Id == "archive_orders");
        var moved = Script[before.StartOffset..before.EndOffset];

        var result = _tasks.Move(Script, "archive_orders", afterId: null);

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        Assert.Equal(["archive_orders", "load_orders"], _tasks.Read(result.Script).Select(task => task.Id));

        // The relocated task is the same bytes, not a regenerated statement, and lifting it out of
        // both scripts leaves the rest of the file in the same order it was already in.
        Assert.Contains(moved, result.Script, StringComparison.Ordinal);
        AssertUntouched(Script, result.Script, moved);
    }

    [Fact]
    public void Move_ToTheHeadStillLeavesTheConnectionDeclaredFirst()
    {
        var result = _tasks.Move(Script, "archive_orders", afterId: null);

        Assert.True(result.Applied, result.Error);
        Assert.True(
            result.Script.IndexOf("CREATE CONNECTION", StringComparison.Ordinal)
            < result.Script.IndexOf("archive_orders:", StringComparison.Ordinal),
            "A task moved to the head of the flow must still run after the connection it uses.");
    }

    [Fact]
    public void Remove_TakesTheLabelWithTheBlockAndNothingElse()
    {
        var task = _tasks.Read(Script).Single(t => t.Id == "load_orders");
        var removed = Script[task.StartOffset..task.EndOffset];

        var result = _tasks.Remove(Script, "load_orders");

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        Assert.Equal(["archive_orders"], _tasks.Read(result.Script).Select(t => t.Id));
        Assert.DoesNotContain("load_orders", result.Script, StringComparison.Ordinal);
        AssertUntouched(Script, result.Script, removed);
    }

    [Fact]
    public void EveryEditIsRefusedOnAScriptThatDoesNotParse()
    {
        const string broken = "CREATE CONNECTION staging_db AS ;;; SELECT";

        foreach (var result in new[]
                 {
                     _tasks.Add(broken, new PipelineTaskDraft("t", "staging_db", "SELECT 1;")),
                     _tasks.Update(broken, "t", newId: "u"),
                     _tasks.Move(broken, "t", null),
                     _tasks.Remove(broken, "t"),
                 })
        {
            Assert.False(result.Applied);
            Assert.Equal(broken, result.Script);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }
    }

    [Fact]
    public void EmittedTasksSurviveTheCanonicalFormatter()
    {
        // The emitted form has to be one the formatter accepts, or the first author who hits Format
        // loses the task the canvas wrote. Formatting must keep it parseable and keep it a task.
        var added = _tasks.Add(Script, new PipelineTaskDraft(
            "refresh_totals", "staging_db", "UPDATE dbo.Totals SET Amount = 0;", After: "archive_orders"));
        Assert.True(added.Applied, added.Error);

        var formatted = SqlFormatter.Format(added.Script, new FormatterOptions());
        AssertParses(formatted);

        Assert.Equal(
            _tasks.Read(added.Script).Select(task => task.Id),
            _tasks.Read(formatted).Select(task => task.Id));
    }

    [Fact]
    public void EmittedTasksSurviveTheAstSerializer()
    {
        var added = _tasks.Add("", new PipelineTaskDraft("first_task", "staging_db", "SELECT 1;"));
        Assert.True(added.Applied, added.Error);

        var ast = new CoreParser(new Lexer(added.Script).Tokenize(), added.Script).Parse();
        var reserialized = string.Join("\n", ast.Statements.Select(statement => statement.ToSql()));

        AssertParses(reserialized);
        Assert.Equal(["first_task"], _tasks.Read(reserialized).Select(task => task.Id));
    }

    [Fact]
    public void AHandEditToTheScriptKeepsTheSameCanvasNodeIdentity()
    {
        // The point of labelling a task: the author edits the script by hand, every positional node
        // id shifts, and the canvas still knows which box is which.
        var handEdited = Script.Replace(
            "SELECT OrderId INTO #orders FROM staging_db.Orders;",
            "SELECT OrderId, Total INTO #orders FROM staging_db.Orders;\n\nSELECT 1;",
            StringComparison.Ordinal);

        var before = ProjectedKeys(Script);
        var after = ProjectedKeys(handEdited);

        Assert.Equal(["load_orders", "archive_orders"], before);
        Assert.Equal(before, after);

        // Positional ids did shift, which is exactly why the key is what the canvas must track.
        Assert.NotEqual(ProjectedTaskNodeIds(Script), ProjectedTaskNodeIds(handEdited));
    }

    private static List<string> ProjectedKeys(string script) =>
        ProjectedTaskNodes(script).Select(node => node.Key!).ToList();

    private static List<string> ProjectedTaskNodeIds(string script) =>
        ProjectedTaskNodes(script).Select(node => node.Id).ToList();

    private static List<ScriptDagNode> ProjectedTaskNodes(string script)
    {
        var ast = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
        return ScriptDagBuilder.Build(ast).Nodes.Where(node => node.Key is not null).ToList();
    }
}
