using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// An <c>IF</c> on the pipeline canvas can be given an <c>ELSE</c>, and tasks can be put in either branch.
///
/// <para>The ELSE branch is addressed as <c>&lt;label&gt;:else</c>. A task label can never contain a
/// colon, so that name cannot collide with a task, and it gives the second body under one label the
/// identity the canvas needs to say which branch a drop landed in — the reason the canvas refused to
/// author an ELSE at all until now.</para>
///
/// <para>As with every other canvas edit, the ELSE is a span inserted into the author's bytes, never a
/// regenerated statement: adding one and removing it again gives back the file exactly as it was.</para>
/// </summary>
public class PipelineIfElseTests
{
    private readonly PipelineTaskAuthoringService _tasks = new();

    private const string Script = """
        CREATE CONNECTION staging_db AS MOCKDB();

        fetch_orders:
        EXECUTE staging_db BEGIN
            SELECT 1;
        END;

        fetch_rates:
        EXECUTE staging_db BEGIN
            SELECT 2;
        END;
        """;

    private const string Else = "orders_ready:else";

    private static Script Parse(string script) =>
        new CoreParser(new Lexer(script).Tokenize(), script).Parse();

    private static void AssertParses(string script)
    {
        var error = Parse(script).Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(error is null, $"Script does not parse: {error?.Message}\n---\n{script}");
    }

    /// <summary>The IF statement a label introduces, read by the canonical parser.</summary>
    private static IfStatement IfNamed(string script, string label)
    {
        var statements = Parse(script).Statements;
        for (var i = 0; i < statements.Count - 1; i++)
        {
            if (statements[i] is SectionLabelStatement named && named.LabelName == label && statements[i + 1] is IfStatement branch)
                return branch;
        }

        throw new Xunit.Sdk.XunitException($"No IF labelled '{label}' in:\n{script}");
    }

    /// <summary>The labels written directly in a block, which is where the engine will run them.</summary>
    private static List<string> LabelsIn(Statement? body) =>
        (body as BlockStatement)?.Statements.OfType<SectionLabelStatement>().Select(label => label.LabelName).ToList() ?? [];

    private PipelineTask TaskNamed(string script, string id) =>
        _tasks.Read(script).Single(task => task.Id == id);

    private string Applied(PipelineEditResult result)
    {
        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        return result.Script;
    }

    private string WithIf() =>
        Applied(_tasks.Add(Script, new PipelineTaskDraft("orders_ready", PipelineTaskKind.If, Condition: "1 = 1")));

    private string WithIfElse() => Applied(_tasks.AddElse(WithIf(), "orders_ready"));

    // ── Adding and removing the branch ───────────────────────────────────────

    [Fact]
    public void AnElseIsAddedToAnIfOnTheCanvas()
    {
        var script = WithIfElse();

        Assert.IsType<BlockStatement>(IfNamed(script, "orders_ready").ElseBody);
        Assert.True(TaskNamed(script, "orders_ready").HasElse);
        Assert.False(TaskNamed(WithIf(), "orders_ready").HasElse);
    }

    /// <summary>
    /// The byte-level guarantee: the ELSE is a span inserted into the author's text, so taking an
    /// empty one away gives back exactly the file that was there before — not a reformatted copy.
    /// </summary>
    [Fact]
    public void RemovingAnEmptyElseGivesBackTheScriptExactly()
    {
        var before = WithIf();
        var withElse = Applied(_tasks.AddElse(before, "orders_ready"));

        var removed = Applied(_tasks.RemoveElse(withElse, "orders_ready"));

        Assert.Equal(before, removed);
        Assert.False(TaskNamed(removed, "orders_ready").HasElse);
    }

    [Fact]
    public void AnIfThatAlreadyHasAnElseIsNotGivenASecond()
    {
        var script = WithIfElse();

        var again = _tasks.AddElse(script, "orders_ready");

        Assert.False(again.Applied);
        Assert.Equal(script, again.Script);
        Assert.Contains("ELSE", again.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fetch_orders")]
    [InlineData("no_such_task")]
    public void OnlyAnIfTakesAnElse(string id)
    {
        var script = WithIf();

        var result = _tasks.AddElse(script, id);

        Assert.False(result.Applied);
        Assert.Equal(script, result.Script);
        Assert.NotNull(result.Error);
    }

    /// <summary>Removing a branch that holds work would delete it, so the author moves it out first.</summary>
    [Fact]
    public void AnElseThatHoldsTasksIsNotRemoved()
    {
        var script = Applied(_tasks.Nest(WithIfElse(), "fetch_rates", Else));

        var result = _tasks.RemoveElse(script, "orders_ready");

        Assert.False(result.Applied);
        Assert.Equal(script, result.Script);
        Assert.Contains("fetch_rates", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingAnElseThatIsNotThereIsRefused()
    {
        var script = WithIf();

        var result = _tasks.RemoveElse(script, "orders_ready");

        Assert.False(result.Applied);
        Assert.Equal(script, result.Script);
    }

    /// <summary>
    /// The branch name is an address, not a task. Deleting "the ELSE" by that name must never be read
    /// as deleting the IF that owns it.
    /// </summary>
    [Fact]
    public void TheBranchNameIsNotATask()
    {
        var script = WithIfElse();

        Assert.False(_tasks.Remove(script, Else).Applied);
        Assert.DoesNotContain(_tasks.Read(script), task => task.Id == Else);
    }

    // ── Putting tasks in each branch ─────────────────────────────────────────

    [Fact]
    public void AChipDroppedOnTheElseIsWrittenInsideIt()
    {
        var script = Applied(_tasks.Add(WithIfElse(), new PipelineTaskDraft(
            "load_fallback", PipelineTaskKind.Execution, Connection: "staging_db", Body: "SELECT 3;", Into: Else)));

        var branch = IfNamed(script, "orders_ready");
        Assert.Equal(["load_fallback"], LabelsIn(branch.ElseBody));
        Assert.Empty(LabelsIn(branch.IfBody));
        Assert.Equal(Else, TaskNamed(script, "load_fallback").Container);
    }

    [Fact]
    public void EachBranchHoldsItsOwnTasks()
    {
        var script = Applied(_tasks.Nest(WithIfElse(), "fetch_orders", "orders_ready"));
        script = Applied(_tasks.Nest(script, "fetch_rates", Else));

        var branch = IfNamed(script, "orders_ready");
        Assert.Equal(["fetch_orders"], LabelsIn(branch.IfBody));
        Assert.Equal(["fetch_rates"], LabelsIn(branch.ElseBody));
        Assert.Equal("orders_ready", TaskNamed(script, "fetch_orders").Container);
        Assert.Equal(Else, TaskNamed(script, "fetch_rates").Container);

        // The task's own bytes travel with it; nothing is regenerated.
        Assert.Contains("SELECT 2;", TaskNamed(script, "fetch_rates").Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ATaskComesOutOfTheElseBesideTheIf()
    {
        var script = Applied(_tasks.Nest(WithIfElse(), "fetch_rates", Else));

        var out_ = Applied(_tasks.Nest(script, "fetch_rates", null));

        Assert.Null(TaskNamed(out_, "fetch_rates").Container);
        Assert.Empty(LabelsIn(IfNamed(out_, "orders_ready").ElseBody));
        Assert.True(TaskNamed(out_, "fetch_rates").StartOffset > TaskNamed(out_, "orders_ready").StartOffset);
    }

    /// <summary>
    /// The two branches never both run, so an order or a dependency between them is something the
    /// script can never make true.
    /// </summary>
    [Fact]
    public void TheTwoBranchesAreSeparateScopes()
    {
        var script = Applied(_tasks.Nest(WithIfElse(), "fetch_orders", "orders_ready"));
        script = Applied(_tasks.Nest(script, "fetch_rates", Else));

        Assert.False(_tasks.Move(script, "fetch_rates", "fetch_orders").Applied);
        Assert.False(_tasks.Connect(script, "fetch_orders", "fetch_rates").Applied);
    }

    [Fact]
    public void TasksInTheElseReorderAmongThemselves()
    {
        var script = Applied(_tasks.Nest(WithIfElse(), "fetch_orders", Else));
        script = Applied(_tasks.Nest(script, "fetch_rates", Else));
        Assert.Equal(["fetch_orders", "fetch_rates"], LabelsIn(IfNamed(script, "orders_ready").ElseBody));

        var moved = Applied(_tasks.Move(script, "fetch_orders", "fetch_rates"));

        Assert.Equal(["fetch_rates", "fetch_orders"], LabelsIn(IfNamed(moved, "orders_ready").ElseBody));
    }

    /// <summary>
    /// An ELSE inside a loop is still inside the loop, so BREAK is legal there. Finding that means
    /// walking out through the branch to the IF that owns it and on to the loop.
    /// </summary>
    [Fact]
    public void BreakIsAllowedInAnElseInsideALoop()
    {
        var script = Applied(_tasks.Add(Script, new PipelineTaskDraft("keep_going", PipelineTaskKind.While, Condition: "1 = 1")));
        script = Applied(_tasks.Add(script, new PipelineTaskDraft(
            "orders_ready", PipelineTaskKind.If, Condition: "1 = 1", Into: "keep_going")));
        script = Applied(_tasks.AddElse(script, "orders_ready"));

        var result = _tasks.Add(script, new PipelineTaskDraft("stop_now", PipelineTaskKind.Break, Into: Else));

        Assert.True(result.Applied, result.Error);
        Assert.Equal(Else, TaskNamed(result.Script, "stop_now").Container);
    }

    /// <summary>
    /// A <c>CASE … END</c> in a branch ends in the same keyword as the block. Read as the block's END,
    /// it would put the new task in the middle of the statement — or put an ELSE there.
    /// </summary>
    [Fact]
    public void ACaseExpressionInABranchDoesNotEndIt()
    {
        const string withCase = """
            CREATE CONNECTION staging_db AS MOCKDB();

            orders_ready:
            IF 1 = 1
            BEGIN
                SELECT CASE WHEN 1 = 1 THEN 'yes' ELSE 'no' END AS answer INTO #answer;
            END;

            fetch_rates:
            EXECUTE staging_db BEGIN
                SELECT 2;
            END;
            """;

        var script = Applied(_tasks.AddElse(withCase, "orders_ready"));
        script = Applied(_tasks.Nest(script, "fetch_rates", Else));

        var branch = IfNamed(script, "orders_ready");
        Assert.Single((branch.IfBody as BlockStatement)!.Statements);
        Assert.Equal(["fetch_rates"], LabelsIn(branch.ElseBody));
    }

    // ── What the canvas leaves alone ─────────────────────────────────────────

    /// <summary>
    /// An ELSE IF chain has more than two branches, and one name per branch is not something the canvas
    /// offers yet. The chain stays the author's: no ELSE is adopted and none can be added.
    /// </summary>
    [Fact]
    public void AnElseIfChainIsLeftToTheScript()
    {
        const string chain = """
            CREATE CONNECTION staging_db AS MOCKDB();

            orders_ready:
            IF 1 = 1
            BEGIN
                SELECT 1;
            END
            ELSE IF 2 = 2
            BEGIN
                SELECT 2;
            END
            ELSE
            BEGIN
                fetch_rates:
                EXECUTE staging_db BEGIN
                    SELECT 3;
                END;
            END;
            """;

        Assert.False(TaskNamed(chain, "orders_ready").HasElse);
        Assert.DoesNotContain(_tasks.Read(chain), task => task.Container == Else);
        Assert.False(_tasks.AddElse(chain, "orders_ready").Applied);
    }

    [Fact]
    public void AHandWrittenElseIsReadAsTheBranch()
    {
        const string written = """
            CREATE CONNECTION staging_db AS MOCKDB();

            orders_ready:
            IF 1 = 1
            BEGIN
                fetch_orders:
                EXECUTE staging_db BEGIN
                    SELECT 1;
                END;
            END
            ELSE
            BEGIN
                fetch_rates:
                EXECUTE staging_db BEGIN
                    SELECT 2;
                END;
            END;
            """;

        Assert.True(TaskNamed(written, "orders_ready").HasElse);
        Assert.Equal("orders_ready", TaskNamed(written, "fetch_orders").Container);
        Assert.Equal(Else, TaskNamed(written, "fetch_rates").Container);
    }

    // ── The map ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A labelled IF is a canvas task, so its card has to carry the label — that is what makes it a
    /// card the author can select and drop into. Every other container passed its key; the IF did not.
    /// </summary>
    [Fact]
    public void ALabelledIfIsKeyedOnTheMap()
    {
        var dag = ScriptDagBuilder.Build(Parse(WithIf()));

        Assert.Contains(dag.Nodes, node => node.Label == "IF" && node.Key == "orders_ready");
    }

    [Fact]
    public void TheElseIsItsOwnStageOnTheMapWithItsTasksAfterIt()
    {
        var script = Applied(_tasks.Nest(WithIfElse(), "fetch_orders", "orders_ready"));
        script = Applied(_tasks.Nest(script, "fetch_rates", Else));

        var dag = ScriptDagBuilder.Build(Parse(script));
        var branch = dag.Nodes.Single(node => node.Key == "orders_ready");
        var otherwise = dag.Nodes.Single(node => node.Key == Else);
        var inIf = dag.Nodes.Single(node => node.Key == "fetch_orders");
        var inElse = dag.Nodes.Single(node => node.Key == "fetch_rates");

        Assert.Equal("ELSE", otherwise.Label);
        Assert.Contains(dag.Edges, edge => edge.Source == branch.Id && edge.Target == inIf.Id && edge.Label == "TRUE");
        Assert.Contains(dag.Edges, edge => edge.Source == branch.Id && edge.Target == otherwise.Id && edge.Label == "ELSE");
        Assert.Contains(dag.Edges, edge => edge.Source == otherwise.Id && edge.Target == inElse.Id);
    }

    /// <summary>An unlabelled IF is not a canvas task, and its map is exactly what it always was.</summary>
    [Fact]
    public void AnUnlabelledIfElseKeepsItsShape()
    {
        const string plain = """
            IF 1 = 1
            BEGIN
                SELECT 1;
            END
            ELSE
            BEGIN
                SELECT 2;
            END;
            """;

        var dag = ScriptDagBuilder.Build(Parse(plain));

        Assert.DoesNotContain(dag.Nodes, node => node.Label == "ELSE");
        Assert.Contains(dag.Edges, edge => edge.Label == "ELSE");
    }
}
