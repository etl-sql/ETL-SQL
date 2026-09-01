using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Services;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// Control-flow containers on the pipeline canvas: <c>PARALLEL</c>, <c>FOREACH</c>, and transaction
/// scopes that hold other tasks.
///
/// <para>The rule these exist to hold: concurrency is something the script says, never something the
/// canvas infers. A <c>PARALLEL</c> block is the only construct in ETL-SQL that means it, so the
/// canvas writes one only when the author asks for one — and refuses to put two tasks in it that
/// have declared an order, rather than quietly dropping the edge that said so.</para>
/// </summary>
public class PipelineContainerTests
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

    private static Script Parse(string script) =>
        new CoreParser(new Lexer(script).Tokenize(), script).Parse();

    private static void AssertParses(string script)
    {
        var error = Parse(script).Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(error is null, $"Script does not parse: {error?.Message}\n---\n{script}");
    }

    private PipelineTask TaskNamed(string script, string id) =>
        _tasks.Read(script).Single(task => task.Id == id);

    private string WithContainer(PipelineTaskKind kind, string id, string? variable = null, string? collection = null)
    {
        var result = _tasks.Add(Script, new PipelineTaskDraft(id, kind, Variable: variable, Collection: collection));
        Assert.True(result.Applied, result.Error);
        return result.Script;
    }

    public static TheoryData<PipelineTaskKind, PipelineTaskDraft> EveryContainer() => new()
    {
        { PipelineTaskKind.Parallel, new PipelineTaskDraft("load_all", PipelineTaskKind.Parallel) },
        {
            PipelineTaskKind.Foreach,
            new PipelineTaskDraft("per_region", PipelineTaskKind.Foreach, Variable: "region", Collection: "#regions")
        },
        { PipelineTaskKind.Transaction, new PipelineTaskDraft("atomic_load", PipelineTaskKind.Transaction) },
    };

    // ── What gets written ────────────────────────────────────────────────────

    /// <summary>
    /// A container is created empty and filled by dragging tasks into it. Every shape parses with an
    /// empty body, so there is no placeholder statement in the author's file to explain away.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryContainer))]
    public void EveryContainerIsWrittenEmptyAndParses(PipelineTaskKind kind, PipelineTaskDraft draft)
    {
        var result = _tasks.Add(Script, draft);

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);

        var container = TaskNamed(result.Script, draft.Id);
        Assert.Equal(kind, container.Kind);
        Assert.Null(container.Container);
        Assert.DoesNotContain(_tasks.Read(result.Script), task => task.Container == draft.Id);
    }

    [Theory]
    [MemberData(nameof(EveryContainer))]
    public async Task EveryContainerIntroducesNoLintFinding(PipelineTaskKind kind, PipelineTaskDraft draft)
    {
        var result = _tasks.Add(Script, draft);
        Assert.True(result.Applied, result.Error);

        var before = await Lint(Script);
        var after = await Lint(result.Script);

        var introduced = after.Except(before, StringComparer.Ordinal).ToList();
        Assert.True(introduced.Count == 0,
            $"The {kind} container introduced lint findings:\n  " + string.Join("\n  ", introduced));

        static async Task<List<string>> Lint(string script)
        {
            var linter = LinterFactory.CreateWithAllRules(null);
            var results = await linter.AnalyzeAsync(Parse(script), new DefaultLintContext());
            return results
                .Where(finding => finding.Severity is LintSeverity.Error or LintSeverity.Warning)
                .Select(finding => $"{finding.Severity} {finding.Code ?? finding.RuleName}: {finding.Message}")
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>
    /// ETL-SQL has no single transaction statement, so a scope is the documented rollback handler
    /// around <c>BEGIN TRANSACTION</c> / <c>COMMIT</c>. A scope that committed on the way out but
    /// left a half-written transaction open on the way in would be worse than no scope at all.
    /// </summary>
    [Fact]
    public void ATransactionScopeCommitsAndRollsBack()
    {
        var script = WithContainer(PipelineTaskKind.Transaction, "atomic_load");

        Assert.Contains("BEGIN TRANSACTION;", script, StringComparison.Ordinal);
        Assert.Contains("COMMIT;", script, StringComparison.Ordinal);
        Assert.Contains("IF @@TRANCOUNT > 0 ROLLBACK;", script, StringComparison.Ordinal);
        Assert.Contains("THROW;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ALoopIsWrittenWithItsVariableAndCollectionAndReadsThemBack()
    {
        var script = WithContainer(PipelineTaskKind.Foreach, "per_region", "region", "#regions");

        Assert.Contains("FOREACH @region IN #regions", script, StringComparison.Ordinal);
        var loop = TaskNamed(script, "per_region");
        Assert.Equal("@region", loop.Variable);
        Assert.Equal("#regions", loop.Collection);
    }

    [Theory]
    [InlineData(null, "#regions")]
    [InlineData("region", null)]
    [InlineData("1bad", "#regions")]
    [InlineData("region", "#regions; DROP TABLE dbo.Orders")]
    public void AHalfFilledLoopIsRefusedBeforeAnythingIsWritten(string? variable, string? collection)
    {
        var result = _tasks.Add(Script, new PipelineTaskDraft(
            "per_region", PipelineTaskKind.Foreach, Variable: variable, Collection: collection));

        Assert.False(result.Applied);
        Assert.Equal(Script, result.Script);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ALoopCanBeRepointedWithoutTouchingWhatIsInsideIt()
    {
        var script = WithContainer(PipelineTaskKind.Foreach, "per_region", "region", "#regions");
        var nested = _tasks.Nest(script, "fetch_orders", "per_region");
        Assert.True(nested.Applied, nested.Error);

        var updated = _tasks.Update(nested.Script, "per_region", collection: "(SELECT DISTINCT region FROM #orders)");

        Assert.True(updated.Applied, updated.Error);
        AssertParses(updated.Script);
        Assert.Equal("(SELECT DISTINCT region FROM #orders)", TaskNamed(updated.Script, "per_region").Collection);
        Assert.Equal("per_region", TaskNamed(updated.Script, "fetch_orders").Container);
    }

    // ── Putting tasks in and taking them out ─────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryContainer))]
    public void ATaskNestedIntoAContainerKeepsItsBytesAndSaysWhereItIs(PipelineTaskKind kind, PipelineTaskDraft draft)
    {
        var script = _tasks.Add(Script, draft).Script;

        var nested = _tasks.Nest(script, "fetch_orders", draft.Id);

        Assert.True(nested.Applied, nested.Error);
        AssertParses(nested.Script);

        var task = TaskNamed(nested.Script, "fetch_orders");
        Assert.Equal(draft.Id, task.Container);
        Assert.Equal(kind, TaskNamed(nested.Script, draft.Id).Kind);

        // The task's own text is relocated and re-indented, never regenerated.
        Assert.Equal("staging_db", task.Connection);
        Assert.Contains("SELECT 1;", task.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedTaskComesBackOutAtTheLevelItsContainerSitsOn()
    {
        var script = WithContainer(PipelineTaskKind.Parallel, "load_all");
        var nested = _tasks.Nest(script, "fetch_orders", "load_all");
        Assert.True(nested.Applied, nested.Error);

        var out_ = _tasks.Nest(nested.Script, "fetch_orders", null);

        Assert.True(out_.Applied, out_.Error);
        AssertParses(out_.Script);
        Assert.Null(TaskNamed(out_.Script, "fetch_orders").Container);
    }

    [Fact]
    public void DeletingAContainerDeletesWhatIsInsideIt()
    {
        var script = WithContainer(PipelineTaskKind.Parallel, "load_all");
        var nested = _tasks.Nest(script, "fetch_orders", "load_all");
        Assert.True(nested.Applied, nested.Error);

        var removed = _tasks.Remove(nested.Script, "load_all");

        Assert.True(removed.Applied, removed.Error);
        AssertParses(removed.Script);

        // The children were inside the block, so they went with it. Leaving them behind would drop
        // statements out of the scope that gave them their meaning.
        Assert.DoesNotContain(_tasks.Read(removed.Script), task => task.Id == "fetch_orders");
        Assert.Contains(_tasks.Read(removed.Script), task => task.Id == "fetch_rates");
    }

    [Fact]
    public void AContainerCannotBePutInsideItselfOrSomethingItHolds()
    {
        var script = WithContainer(PipelineTaskKind.Parallel, "load_all");
        var inner = _tasks.Add(script, new PipelineTaskDraft("inner_scope", PipelineTaskKind.Transaction));
        Assert.True(inner.Applied, inner.Error);
        var nested = _tasks.Nest(inner.Script, "inner_scope", "load_all");
        Assert.True(nested.Applied, nested.Error);

        Assert.False(_tasks.Nest(nested.Script, "load_all", "load_all").Applied);
        var cycle = _tasks.Nest(nested.Script, "load_all", "inner_scope");
        Assert.False(cycle.Applied);
        Assert.Equal(nested.Script, cycle.Script);
    }

    [Fact]
    public void ATaskCannotBePutInsideSomethingThatDoesNotHoldTasks()
    {
        var result = _tasks.Nest(Script, "fetch_orders", "fetch_rates");

        Assert.False(result.Applied);
        Assert.Equal(Script, result.Script);
        Assert.Contains("does not hold other tasks", result.Error!, StringComparison.Ordinal);
    }

    // ── Concurrency is what the script says, never what the canvas infers ────

    /// <summary>
    /// Branches of a <c>PARALLEL</c> block all start together, so one cannot wait for another. The
    /// canvas refuses rather than silently dropping the edge the author drew: a quietly deleted
    /// dependency is the same silent failure as a quietly ignored one.
    /// </summary>
    [Fact]
    public void TwoBranchesOfAParallelBlockCannotBeGivenAnOrder()
    {
        var script = WithContainer(PipelineTaskKind.Parallel, "load_all");
        var first = _tasks.Nest(script, "fetch_orders", "load_all");
        var both = _tasks.Nest(first.Script, "fetch_rates", "load_all");
        Assert.True(both.Applied, both.Error);

        var connected = _tasks.Connect(both.Script, "fetch_orders", "fetch_rates");

        Assert.False(connected.Applied);
        Assert.Equal(both.Script, connected.Script);
        Assert.Contains("start together", connected.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ATaskThatAlreadyWaitsForOneBranchCannotJoinThatParallelBlock()
    {
        var edge = _tasks.Connect(Script, "fetch_orders", "fetch_rates");
        Assert.True(edge.Applied, edge.Error);

        var script = _tasks.Add(edge.Script, new PipelineTaskDraft("load_all", PipelineTaskKind.Parallel));
        Assert.True(script.Applied, script.Error);
        var nested = _tasks.Nest(script.Script, "fetch_orders", "load_all");
        Assert.True(nested.Applied, nested.Error);

        var joined = _tasks.Nest(nested.Script, "fetch_rates", "load_all");

        Assert.False(joined.Applied);
        Assert.Equal(nested.Script, joined.Script);
        Assert.Contains("start together", joined.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Order between blocks is the nesting, not a declaration. A task inside a container cannot be
    /// moved past one outside it, so an edge across that boundary could never be made true — and a
    /// reorder must not quietly slide a task into a scope nobody put it in.
    /// </summary>
    [Fact]
    public void NeitherAnEdgeNorAReorderCrossesAContainerBoundary()
    {
        var script = WithContainer(PipelineTaskKind.Transaction, "atomic_load");
        var nested = _tasks.Nest(script, "fetch_orders", "atomic_load");
        Assert.True(nested.Applied, nested.Error);

        var connected = _tasks.Connect(nested.Script, "fetch_orders", "fetch_rates");
        Assert.False(connected.Applied);
        Assert.Equal(nested.Script, connected.Script);

        var moved = _tasks.Move(nested.Script, "fetch_rates", "fetch_orders");
        Assert.False(moved.Applied);
        Assert.Equal(nested.Script, moved.Script);
        Assert.Null(TaskNamed(moved.Script, "fetch_rates").Container);
    }

    /// <summary>
    /// Nothing about a container makes the canvas write concurrency on its own. A task nested into a
    /// loop or a transaction scope produces no <c>PARALLEL</c> block anywhere.
    /// </summary>
    [Theory]
    [InlineData(PipelineTaskKind.Foreach)]
    [InlineData(PipelineTaskKind.Transaction)]
    public void NoOtherContainerEverWritesAParallelBlock(PipelineTaskKind kind)
    {
        var script = kind == PipelineTaskKind.Foreach
            ? WithContainer(kind, "per_region", "region", "#regions")
            : WithContainer(kind, "atomic_load");

        var nested = _tasks.Nest(script, "fetch_orders", kind == PipelineTaskKind.Foreach ? "per_region" : "atomic_load");

        Assert.True(nested.Applied, nested.Error);
        Assert.DoesNotContain("PARALLEL", nested.Script, StringComparison.OrdinalIgnoreCase);
    }

    // ── What the canvas is told ──────────────────────────────────────────────

    [Fact]
    public void TheProjectionDrawsAContainerAsOneStageWithItsChildrenInside()
    {
        var script = WithContainer(PipelineTaskKind.Parallel, "load_all");
        var first = _tasks.Nest(script, "fetch_orders", "load_all");
        var both = _tasks.Nest(first.Script, "fetch_rates", "load_all");
        Assert.True(both.Applied, both.Error);

        var projection = new ScriptDagProjectionService().Project(both.Script);

        Assert.True(projection.Parsed, projection.Error);
        var byKey = projection.Dag.Nodes
            .Where(node => Key(node) is not null)
            .ToDictionary(node => Key(node)!, node => node);

        Assert.Equal("parallel", byKey["load_all"].Type);
        Assert.Contains(projection.Dag.Edges, edge =>
            edge.Source == byKey["load_all"].Id && edge.Target == byKey["fetch_orders"].Id);
        Assert.Contains(projection.Dag.Edges, edge =>
            edge.Source == byKey["load_all"].Id && edge.Target == byKey["fetch_rates"].Id);
    }

    /// <summary>
    /// A transaction scope is one container, not an error handler. Drawn as a raw TRY/CATCH it would
    /// put the rollback boilerplate the canvas emitted on the map next to the work the author wrote.
    /// </summary>
    [Fact]
    public void TheProjectionDrawsATransactionScopeWithoutItsRollbackBoilerplate()
    {
        var script = WithContainer(PipelineTaskKind.Transaction, "atomic_load");
        var nested = _tasks.Nest(script, "fetch_orders", "atomic_load");
        Assert.True(nested.Applied, nested.Error);

        var projection = new ScriptDagProjectionService().Project(nested.Script);

        Assert.True(projection.Parsed, projection.Error);
        Assert.Contains(projection.Dag.Nodes, node => Key(node) == "atomic_load" && node.Type == "transaction");
        Assert.DoesNotContain(projection.Dag.Nodes, node => node.Label.Contains("TRY", StringComparison.Ordinal));
        Assert.DoesNotContain(projection.Dag.Nodes, node => node.Label.Contains("Throw", StringComparison.Ordinal));
    }

    private static string? Key(ScriptDagNodeDto node) =>
        node.Meta?.GetType().GetProperty("key")?.GetValue(node.Meta) as string;

    [Theory]
    [MemberData(nameof(EveryContainer))]
    public void AContainerAndItsChildSurviveTheCanonicalFormatter(PipelineTaskKind kind, PipelineTaskDraft draft)
    {
        var script = _tasks.Add(Script, draft).Script;
        var nested = _tasks.Nest(script, "fetch_orders", draft.Id);
        Assert.True(nested.Applied, nested.Error);

        var formatted = SqlFormatter.Format(nested.Script, new FormatterOptions());

        AssertParses(formatted);
        var container = _tasks.Read(formatted).SingleOrDefault(task => task.Id == draft.Id);
        Assert.True(container is not null, $"The formatter lost the {kind} container:\n{formatted}");
        Assert.Equal(kind, container!.Kind);
        Assert.Equal(draft.Id, TaskNamed(formatted, "fetch_orders").Container);
    }

    // ── What the engine does with it ─────────────────────────────────────────

    private static async Task RunAsync(string script)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
    }

    /// <summary>
    /// The scope the canvas writes has to behave like one when it is run: a failure inside it is
    /// re-thrown, so the orchestrator sees a failed job rather than a silently half-applied load.
    /// </summary>
    [Fact]
    public async Task AFailureInsideATransactionScopeStillFailsTheRun()
    {
        const string script = """
            SELECT 1 AS Ok INTO #orders;

            check_orders:
            ASSERT 1 = 0,
                'the scope did not swallow this';
            """;

        var scope = _tasks.Add(script, new PipelineTaskDraft("atomic_load", PipelineTaskKind.Transaction));
        Assert.True(scope.Applied, scope.Error);
        var nested = _tasks.Nest(scope.Script, "check_orders", "atomic_load");
        Assert.True(nested.Applied, nested.Error);

        // The scope's CATCH re-throws with a bare THROW, which is what makes the orchestrator mark
        // the job failed — and which is why the original message is not what comes back out.
        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(nested.Script));
    }

    [Fact]
    public async Task EveryBranchOfAParallelBlockActuallyRuns()
    {
        const string script = """
            SELECT 1 AS Ok INTO #orders;

            check_a:
            ASSERT (SELECT COUNT(*) FROM #orders) = 1,
                'branch a did not run';

            check_b:
            ASSERT (SELECT COUNT(*) FROM #orders) = 1,
                'branch b did not run';
            """;

        var block = _tasks.Add(script, new PipelineTaskDraft("checks", PipelineTaskKind.Parallel));
        Assert.True(block.Applied, block.Error);
        var first = _tasks.Nest(block.Script, "check_a", "checks");
        var both = _tasks.Nest(first.Script, "check_b", "checks");
        Assert.True(both.Applied, both.Error);

        await RunAsync(both.Script);
    }
}
