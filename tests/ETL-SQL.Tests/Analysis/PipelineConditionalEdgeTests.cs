using ETL_SQL.Analysis.Services;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// Conditional precedence edges between pipeline tasks.
///
/// <para>An edge that fires only on an outcome is not a note on the diagram. The declaration goes
/// into the <c>-- @after:</c> tag, and the behaviour goes into the script: a <c>BEGIN TRY</c> guard
/// around the task being watched, which records its outcome, and an <c>IF</c> around the task that
/// waits, which reads it. The tests that matter here are the ones that run the result — a canvas
/// that draws a red edge and writes a script the engine runs unconditionally is exactly the
/// silent-failure shape this surface must not have.</para>
///
/// <para>The status variable is deliberately three-valued: 0 declared, 1 succeeded, -1 threw. A task
/// whose own gate was false never ran, so it stays at 0 and does not fire a downstream
/// <c>on failure</c> edge — "skipped" is not "failed".</para>
/// </summary>
public class PipelineConditionalEdgeTests
{
    private readonly PipelineTaskAuthoringService _tasks = new();

    private const string Script = """
        CREATE CONNECTION staging_db AS MOCKDB();

        fetch_orders:
        EXECUTE staging_db BEGIN
            SELECT 1;
        END;

        publish_orders:
        EXECUTE staging_db BEGIN
            SELECT 2;
        END;

        tell_ops:
        EXECUTE staging_db BEGIN
            SELECT 3;
        END;
        """;

    private static void AssertParses(string script)
    {
        var ast = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
        var error = ast.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(error is null, $"Script does not parse: {error?.Message}\n---\n{script}");
    }

    private PipelineTask TaskNamed(string script, string id) =>
        _tasks.Read(script).Single(task => task.Id == id);

    // ── What gets written ────────────────────────────────────────────────────

    [Fact]
    public void OnSuccessGuardsTheUpstreamTaskAndGatesTheDependent()
    {
        var result = _tasks.Connect(Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnSuccess);

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);

        Assert.Contains("-- @after: fetch_orders on success", result.Script, StringComparison.Ordinal);
        Assert.Contains("DECLARE @fetch_orders_status INT = 0;", result.Script, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRY", result.Script, StringComparison.Ordinal);
        Assert.Contains("SET @fetch_orders_status = 1;", result.Script, StringComparison.Ordinal);
        Assert.Contains("SET @fetch_orders_status = -1;", result.Script, StringComparison.Ordinal);
        Assert.Contains("IF @fetch_orders_status = 1", result.Script, StringComparison.Ordinal);

        Assert.True(TaskNamed(result.Script, "fetch_orders").Guarded);
        var dependency = Assert.Single(TaskNamed(result.Script, "publish_orders").DependsOn);
        Assert.Equal(new PipelineDependency("fetch_orders", PipelineEdgeCondition.OnSuccess), dependency);
    }

    [Fact]
    public void OnFailureGatesTheDependentOnTheErrorBranch()
    {
        var result = _tasks.Connect(Script, "fetch_orders", "tell_ops", PipelineEdgeCondition.OnFailure);

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        Assert.Contains("IF @fetch_orders_status = -1", result.Script, StringComparison.Ordinal);
        Assert.Equal(PipelineEdgeCondition.OnFailure, TaskNamed(result.Script, "tell_ops").DependsOn.Single().Condition);
    }

    /// <summary>
    /// On completion is the one condition with nothing to test at run time: it says the dependent
    /// runs either way. What it actually buys is the guard on the task above it, without which an
    /// error there would end the run before the dependent was reached at all.
    /// </summary>
    [Fact]
    public void OnCompletionGuardsTheUpstreamTaskAndWritesNoGate()
    {
        var result = _tasks.Connect(Script, "fetch_orders", "tell_ops", PipelineEdgeCondition.OnCompletion);

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);

        Assert.True(TaskNamed(result.Script, "fetch_orders").Guarded);
        Assert.Null(TaskNamed(result.Script, "tell_ops").Gate);
        Assert.DoesNotContain("IF @fetch_orders_status", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExpressionEdgeWritesTheAuthorsConditionAndReadsItBack()
    {
        var result = _tasks.Connect(
            Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.Expression, "@@ROWCOUNT > 0");

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        Assert.Contains("-- @after: fetch_orders when @@ROWCOUNT > 0", result.Script, StringComparison.Ordinal);
        Assert.Contains("IF (@@ROWCOUNT > 0)", result.Script, StringComparison.Ordinal);

        // An expression edge asks nothing of the task it waits for, so nothing is wrapped up there.
        Assert.False(TaskNamed(result.Script, "fetch_orders").Guarded);

        var dependency = TaskNamed(result.Script, "publish_orders").DependsOn.Single();
        Assert.Equal(PipelineEdgeCondition.Expression, dependency.Condition);
        Assert.Equal("@@ROWCOUNT > 0", dependency.Expression);
    }

    /// <summary>
    /// A comma inside the author's expression must not read as the start of another prerequisite,
    /// which is why an expression edge gets a tag line of its own.
    /// </summary>
    [Fact]
    public void AnExpressionContainingACommaStaysOneDependency()
    {
        var connected = _tasks.Connect(Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnSuccess);
        var result = _tasks.Connect(
            connected.Script, "fetch_orders", "tell_ops",
            PipelineEdgeCondition.Expression, "ISNULL(@region, 'ALL') = 'ALL'");

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);

        var dependency = Assert.Single(TaskNamed(result.Script, "tell_ops").DependsOn);
        Assert.Equal("ISNULL(@region, 'ALL') = 'ALL'", dependency.Expression);
    }

    [Theory]
    [InlineData("@x > 0; DROP TABLE dbo.Orders")]
    [InlineData("@x > 0 -- and nothing else")]
    [InlineData("@x > 0 /* comment */")]
    [InlineData("@x > 0\nAND @y < 1")]
    public void AnExpressionThatWouldEscapeTheGateIsRefused(string expression)
    {
        var result = _tasks.Connect(
            Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.Expression, expression);

        Assert.False(result.Applied);
        Assert.Equal(Script, result.Script);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void AnExpressionEdgeWithNoExpressionIsRefused()
    {
        var result = _tasks.Connect(Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.Expression);

        Assert.False(result.Applied);
        Assert.Equal(Script, result.Script);
    }

    // ── Changing and removing an edge ────────────────────────────────────────

    [Fact]
    public void ChangingAnEdgesConditionReplacesItRatherThanAddingASecondPrerequisite()
    {
        var success = _tasks.Connect(Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnSuccess);
        var failure = _tasks.Connect(success.Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnFailure);

        Assert.True(failure.Applied, failure.Error);
        AssertParses(failure.Script);

        var dependency = Assert.Single(TaskNamed(failure.Script, "publish_orders").DependsOn);
        Assert.Equal(PipelineEdgeCondition.OnFailure, dependency.Condition);
        Assert.Contains("IF @fetch_orders_status = -1", failure.Script, StringComparison.Ordinal);
        Assert.DoesNotContain("IF @fetch_orders_status = 1", failure.Script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wrappers are the canvas's, so removing the edge has to take all of them with it. A gate
    /// left behind reads as an ordinary <c>IF</c> the author wrote, and the next reader has no way
    /// to know the pipeline is still refusing to run a task for a reason nothing declares.
    /// </summary>
    [Theory]
    [InlineData(PipelineEdgeCondition.OnSuccess, null)]
    [InlineData(PipelineEdgeCondition.OnFailure, null)]
    [InlineData(PipelineEdgeCondition.OnCompletion, null)]
    [InlineData(PipelineEdgeCondition.Expression, "@@ROWCOUNT > 0")]
    public void RemovingAConditionalEdgeLeavesTheScriptExactlyAsItWas(
        PipelineEdgeCondition condition,
        string? expression)
    {
        var connected = _tasks.Connect(Script, "fetch_orders", "publish_orders", condition, expression);
        Assert.True(connected.Applied, connected.Error);
        Assert.NotEqual(Script, connected.Script);

        var removed = _tasks.Disconnect(connected.Script, "fetch_orders", "publish_orders");

        Assert.True(removed.Applied, removed.Error);
        Assert.Equal(Script, removed.Script);
    }

    [Fact]
    public void RemovingOneOfTwoConditionsLeavesTheOtherEnforced()
    {
        var first = _tasks.Connect(Script, "fetch_orders", "tell_ops", PipelineEdgeCondition.OnFailure);
        var second = _tasks.Connect(
            first.Script, "publish_orders", "tell_ops", PipelineEdgeCondition.Expression, "@@ROWCOUNT > 0");
        Assert.True(second.Applied, second.Error);

        var removed = _tasks.Disconnect(second.Script, "fetch_orders", "tell_ops");

        Assert.True(removed.Applied, removed.Error);
        AssertParses(removed.Script);
        Assert.Contains("IF (@@ROWCOUNT > 0)", removed.Script, StringComparison.Ordinal);
        Assert.DoesNotContain("@fetch_orders_status", removed.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void DeletingTheTaskAnEdgeWatchedTakesTheGateWithIt()
    {
        var connected = _tasks.Connect(Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnSuccess);
        Assert.True(connected.Applied, connected.Error);

        var removed = _tasks.Remove(connected.Script, "fetch_orders");

        Assert.True(removed.Applied, removed.Error);
        AssertParses(removed.Script);

        // Left behind, the gate would read a variable nothing declares and the run would fail on a
        // line the author never wrote.
        Assert.DoesNotContain("@fetch_orders_status", removed.Script, StringComparison.Ordinal);
        Assert.Empty(TaskNamed(removed.Script, "publish_orders").DependsOn);
    }

    /// <summary>
    /// A rename carries the edges that named the task. Dropping them would silently undo canvas
    /// work, and keeping them would leave a gate reading a status variable nothing declares.
    /// </summary>
    [Fact]
    public void RenamingATaskCarriesTheConditionalEdgesThatNameIt()
    {
        var connected = _tasks.Connect(Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnSuccess);
        Assert.True(connected.Applied, connected.Error);

        var renamed = _tasks.Update(connected.Script, "fetch_orders", newId: "stage_orders");

        Assert.True(renamed.Applied, renamed.Error);
        AssertParses(renamed.Script);
        Assert.DoesNotContain("fetch_orders", renamed.Script, StringComparison.Ordinal);

        Assert.Contains("-- @after: stage_orders on success", renamed.Script, StringComparison.Ordinal);
        Assert.Contains("SET @stage_orders_status = 1;", renamed.Script, StringComparison.Ordinal);
        Assert.Contains("IF @stage_orders_status = 1", renamed.Script, StringComparison.Ordinal);

        Assert.True(TaskNamed(renamed.Script, "stage_orders").Guarded);
        Assert.Equal(
            new PipelineDependency("stage_orders", PipelineEdgeCondition.OnSuccess),
            TaskNamed(renamed.Script, "publish_orders").DependsOn.Single());
    }

    /// <summary>
    /// Renaming the dependent moves the gate with it, and the guard it reads stays where it is.
    /// </summary>
    [Fact]
    public void RenamingAGatedTaskKeepsItsGate()
    {
        var connected = _tasks.Connect(Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnFailure);
        Assert.True(connected.Applied, connected.Error);

        var renamed = _tasks.Update(connected.Script, "publish_orders", newId: "recover_orders");

        Assert.True(renamed.Applied, renamed.Error);
        AssertParses(renamed.Script);

        var task = TaskNamed(renamed.Script, "recover_orders");
        Assert.Equal(PipelineEdgeCondition.OnFailure, task.DependsOn.Single().Condition);
        Assert.Equal("staging_db", task.Connection);
        Assert.Contains("SELECT 2;", task.Body, StringComparison.Ordinal);
    }

    // ── What the canvas is told ──────────────────────────────────────────────

    /// <summary>
    /// Drawn literally, a guarded task is a TRY/CATCH stage, an IF stage, and the statement — three
    /// boxes where the author put one, and none of them the card the label names. The projection
    /// collapses the wrappers and puts the condition on the edge, which is where it was drawn.
    /// </summary>
    [Fact]
    public void TheProjectionShowsOneNodePerTaskAndNamesTheConditionOnTheEdge()
    {
        var success = _tasks.Connect(Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnSuccess);
        var failure = _tasks.Connect(success.Script, "fetch_orders", "tell_ops", PipelineEdgeCondition.OnFailure);
        Assert.True(failure.Applied, failure.Error);

        var projection = new ScriptDagProjectionService().Project(failure.Script);

        Assert.True(projection.Parsed, projection.Error);
        var keys = projection.Dag.Nodes
            .Select(node => node.Meta?.GetType().GetProperty("key")?.GetValue(node.Meta) as string)
            .Where(key => key is not null)
            .ToList();
        Assert.Equal(["fetch_orders", "publish_orders", "tell_ops"], keys);

        var byKey = projection.Dag.Nodes.ToDictionary(
            node => node.Meta?.GetType().GetProperty("key")?.GetValue(node.Meta) as string ?? node.Id,
            node => node.Id);

        Assert.Contains(projection.Dag.Edges, edge =>
            edge.Source == byKey["fetch_orders"] && edge.Target == byKey["publish_orders"] && edge.Label == "ON SUCCESS");
        Assert.Contains(projection.Dag.Edges, edge =>
            edge.Source == byKey["fetch_orders"] && edge.Target == byKey["tell_ops"] && edge.Label == "ON FAILURE");
    }

    [Fact]
    public void AConditionalEdgeSurvivesTheCanonicalFormatter()
    {
        var connected = _tasks.Connect(Script, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnSuccess);
        Assert.True(connected.Applied, connected.Error);

        var formatted = SqlFormatter.Format(connected.Script, new FormatterOptions());

        AssertParses(formatted);
        var task = _tasks.Read(formatted).SingleOrDefault(entry => entry.Id == "publish_orders");
        Assert.True(task is not null, $"The formatter lost the gated task:\n{formatted}");
        Assert.Equal(PipelineEdgeCondition.OnSuccess, task!.DependsOn.Single().Condition);
    }

    /// <summary>
    /// Every task the edit did not name has to come out byte for byte as it went in. This is the
    /// invariant the whole span-editing design exists to hold, and adding wrappers is the first
    /// thing that could break it.
    /// </summary>
    [Fact]
    public void TasksTheEdgeDidNotNameAreNotReflowed()
    {
        const string handFormatted = """
            CREATE CONNECTION staging_db AS MOCKDB();

            fetch_orders:
            EXECUTE staging_db BEGIN
                SELECT 1;
            END;

            keep_me:
            EXECUTE staging_db BEGIN
                    SELECT      42     AS  Answer;   -- deliberately ragged
            END;

            publish_orders:
            EXECUTE staging_db BEGIN
                SELECT 2;
            END;
            """;

        var result = _tasks.Connect(handFormatted, "fetch_orders", "publish_orders", PipelineEdgeCondition.OnSuccess);

        Assert.True(result.Applied, result.Error);
        Assert.Contains("        SELECT      42     AS  Answer;   -- deliberately ragged", result.Script, StringComparison.Ordinal);
    }

    // ── What the engine does with it ─────────────────────────────────────────

    private static async Task RunAsync(string script)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
    }

    /// <summary>A pipeline whose first task always throws, and a second task wired to notice.</summary>
    private const string FailingScript = """
        SELECT 1 AS Ok INTO #orders;

        check_orders:
        ASSERT (SELECT COUNT(*) FROM #orders) > 99,
            'check_orders failed';

        tell_ops:
        ASSERT 1 = 0,
            'the dependent ran';
        """;

    [Fact]
    public async Task OnFailure_RunsTheDependentWhenTheTaskItWatchesThrows()
    {
        var wired = _tasks.Connect(FailingScript, "check_orders", "tell_ops", PipelineEdgeCondition.OnFailure);
        Assert.True(wired.Applied, wired.Error);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(wired.Script));

        // The guard swallowed the upstream error, so the only thing that can still fail the run is
        // the dependent — which is the proof that the failure branch is the one that ran.
        Assert.Contains("the dependent ran", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnSuccess_SkipsTheDependentWhenTheTaskItWatchesThrows()
    {
        var wired = _tasks.Connect(FailingScript, "check_orders", "tell_ops", PipelineEdgeCondition.OnSuccess);
        Assert.True(wired.Applied, wired.Error);

        // Nothing throws: the upstream failure is caught by its guard, and the gate is false, so the
        // dependent's own always-failing assertion is never reached.
        await RunAsync(wired.Script);
    }

    [Fact]
    public async Task OnCompletion_RunsTheDependentEvenThoughTheTaskAboveItThrew()
    {
        var wired = _tasks.Connect(FailingScript, "check_orders", "tell_ops", PipelineEdgeCondition.OnCompletion);
        Assert.True(wired.Applied, wired.Error);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(wired.Script));

        Assert.Contains("the dependent ran", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without any edge the same script stops at the first failure, which is what makes the three
    /// tests above mean something: they are not passing because the assertions never fire.
    /// </summary>
    [Fact]
    public async Task WithNoEdgeDeclaredTheFirstFailureStillStopsTheRun()
    {
        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(FailingScript));

        Assert.Contains("check_orders failed", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A task that was skipped is not a task that failed. Its status stays at the 0 it was declared
    /// with, so an <c>on failure</c> edge downstream of it does not fire — the pipeline does not
    /// invent an error out of a branch nobody took.
    /// </summary>
    [Fact]
    public async Task ASkippedTaskDoesNotFireADownstreamFailureEdge()
    {
        const string chain = """
            SELECT 1 AS Ok INTO #orders;

            check_orders:
            ASSERT (SELECT COUNT(*) FROM #orders) > 99,
                'check_orders failed';

            publish_orders:
            ASSERT 1 = 1,
                'publish_orders failed';

            tell_ops:
            ASSERT 1 = 0,
                'the dependent ran';
            """;

        // publish_orders is skipped because check_orders threw; tell_ops watches publish_orders for
        // a failure that never happened.
        var first = _tasks.Connect(chain, "check_orders", "publish_orders", PipelineEdgeCondition.OnSuccess);
        Assert.True(first.Applied, first.Error);
        var second = _tasks.Connect(first.Script, "publish_orders", "tell_ops", PipelineEdgeCondition.OnFailure);
        Assert.True(second.Applied, second.Error);

        await RunAsync(second.Script);
    }
}
