using ETL_SQL.Analysis.Services;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// What "run to this node" would actually execute, and what it would cost to do it.
///
/// <para>The plan is the whole safety story for the action. It decides which of the author's tasks
/// run for real — against real connections, with real writes — and it is the only thing that can
/// tell the author what those writes are before they happen. Two failures matter here and they pull
/// in opposite directions: a slice that quietly drops something the node needed fails a run for a
/// reason nothing on screen explains, and an effect list that misses a write lets the author approve
/// something they were never shown.</para>
/// </summary>
public class PipelineRunPlanTests
{
    private readonly PipelineRunPlanService _plan = new();

    /// <summary>The slice has to be runnable, so every test that produces one parses it.</summary>
    private static void AssertParses(string script)
    {
        var ast = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
        Assert.DoesNotContain(ast.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    private PipelineRunPlan To(string script, string id)
    {
        var plan = _plan.To(script, id);
        if (plan.Resolved) AssertParses(plan.Script);
        return plan;
    }

    // ── Which tasks run ──────────────────────────────────────────────────────

    private const string Sequential = """
        CREATE CONNECTION warehouse AS MOCKDB();

        DECLARE @batch VARCHAR = 'B-001';

        stage_orders:
        EXECUTE warehouse BEGIN
            SELECT 1;
        END;

        stage_returns:
        EXECUTE warehouse BEGIN
            SELECT 2;
        END;

        publish:
        EXECUTE warehouse BEGIN
            SELECT 3;
        END;
        """;

    [Fact]
    public void AnUntaggedScriptRunsEverythingAboveTheSelection()
    {
        // Nothing here declares a dependency, so the only reading available is the one the script
        // gives: top to bottom. Narrowing that would drop work the author had every reason to expect.
        var plan = To(Sequential, "publish");

        Assert.True(plan.Resolved, plan.Error);
        Assert.Equal(["stage_orders", "stage_returns", "publish"], plan.Included);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void TheSelectionIsTheEndOfTheSlice()
    {
        var plan = To(Sequential, "stage_orders");

        Assert.Equal(["stage_orders"], plan.Included);
        Assert.DoesNotContain("stage_returns", plan.Script);
        Assert.DoesNotContain("publish:", plan.Script);
    }

    [Fact]
    public void ADeclaredDependencyNarrowsTheRunToWhatItNames()
    {
        // `publish` says what it needs. Honouring the sequence as well would make saying so pointless
        // — and would re-run a sibling the author deliberately took out of the path.
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            stage_orders:
            EXECUTE warehouse BEGIN
                SELECT 1;
            END;

            unrelated_report:
            EXECUTE warehouse BEGIN
                SELECT 2;
            END;

            -- @after: stage_orders
            publish:
            EXECUTE warehouse BEGIN
                SELECT 3;
            END;
            """;

        var plan = To(script, "publish");

        Assert.Equal(["stage_orders", "publish"], plan.Included);
        Assert.Equal(["unrelated_report"], plan.Skipped);
        Assert.DoesNotContain("unrelated_report", plan.Script);
    }

    [Fact]
    public void ADependencyIsFollowedThroughTheTasksItDependsOnItself()
    {
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            extract:
            EXECUTE warehouse BEGIN
                SELECT 1;
            END;

            noise:
            EXECUTE warehouse BEGIN
                SELECT 99;
            END;

            -- @after: extract
            transform:
            EXECUTE warehouse BEGIN
                SELECT 2;
            END;

            -- @after: transform
            publish:
            EXECUTE warehouse BEGIN
                SELECT 3;
            END;
            """;

        var plan = To(script, "publish");

        Assert.Equal(["extract", "transform", "publish"], plan.Included);
        Assert.Equal(["noise"], plan.Skipped);
    }

    [Fact]
    public void AmbientScriptTheCanvasDoesNotModelIsAlwaysKept()
    {
        // A connection or a DECLARE carries no dependency information, so nothing can establish it is
        // unrelated. Dropping one would fail the run on a name the author can plainly see is there.
        var plan = To(Sequential, "publish");

        Assert.Contains("CREATE CONNECTION warehouse", plan.Script);
        Assert.Contains("DECLARE @batch", plan.Script);
    }

    [Fact]
    public void ASkippedTaskTakesItsDependencyTagWithIt()
    {
        // The tag is part of the task's span. Leaving it behind would strand a comment naming a
        // prerequisite for a task the slice no longer has.
        //
        // The status DECLARE is a different thing and deliberately stays: it belongs to `extract`,
        // the task being watched, and sits above `extract`'s own declaration. `extract` is in the
        // closure, so its bookkeeping is too — removing it here would strip a declaration out from
        // under a task that is still in the slice.
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            DECLARE @extract_status INT = 0;
            extract:
            EXECUTE warehouse BEGIN
                SELECT 1;
            END;

            -- @after: extract on failure
            recover:
            EXECUTE warehouse BEGIN
                SELECT 2;
            END;

            -- @after: extract
            publish:
            EXECUTE warehouse BEGIN
                SELECT 3;
            END;
            """;

        var plan = To(script, "publish");

        Assert.Equal(["recover"], plan.Skipped);
        Assert.DoesNotContain("recover", plan.Script);
        Assert.DoesNotContain("on failure", plan.Script);
        Assert.Contains("@extract_status", plan.Script);
    }

    // ── Containers ───────────────────────────────────────────────────────────

    [Fact]
    public void SelectingATaskInsideAContainerRunsTheWholeContainer()
    {
        // A PARALLEL block is one statement. There is no way to run one of its branches without
        // rewriting the author's bytes into something they did not write, so the block goes whole —
        // and the sibling branch is reported as included rather than being smuggled in silently.
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            fanout:
            PARALLEL BEGIN
                branch_a:
                EXECUTE warehouse BEGIN
                    SELECT 1;
                END;

                branch_b:
                EXECUTE warehouse BEGIN
                    SELECT 2;
                END;
            END;

            after_fanout:
            EXECUTE warehouse BEGIN
                SELECT 3;
            END;
            """;

        var plan = To(script, "branch_a");

        Assert.Contains("fanout", plan.Included);
        Assert.Contains("branch_a", plan.Included);
        Assert.Contains("branch_b", plan.Included);
        Assert.DoesNotContain("after_fanout", plan.Script);
    }

    [Fact]
    public void AContainerTheSelectionDependsOnBringsItsChildrenWithIt()
    {
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            fanout:
            PARALLEL BEGIN
                branch_a:
                EXECUTE warehouse BEGIN
                    SELECT 1;
                END;
            END;

            -- @after: fanout
            publish:
            EXECUTE warehouse BEGIN
                SELECT 3;
            END;
            """;

        var plan = To(script, "publish");

        Assert.Equal(["fanout", "branch_a", "publish"], plan.Included);
        Assert.Contains("branch_a", plan.Script);
    }

    // ── What it would cost ───────────────────────────────────────────────────

    // The canvas models the palette's own statement kinds — execution, file, validation,
    // notification, and the three containers. A labelled MERGE is not one of them, so it is read as
    // ambient script: always kept, never skipped, and reported as an effect with no owning task.
    // These tests are written the way a real script is shaped rather than the way the DTO suggests.

    [Fact]
    public void AWriteToARealTableIsReportedWithTheConnectionItWrites()
    {
        // "MERGE INTO Customers" does not tell the author which database is about to change. Naming
        // the connection is the one thing the confirmation exists to say.
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            MERGE INTO warehouse.Customers AS t
            USING warehouse.Staging AS s ON t.Id = s.Id
            WHEN MATCHED THEN UPDATE SET t.Name = s.Name;

            check_it:
            ASSERT 1 = 1, 'staged';
            """;

        var plan = To(script, "check_it");

        var effect = Assert.Single(plan.Effects);
        Assert.Equal("MERGE INTO", effect.Action);
        Assert.Equal("warehouse.Customers", effect.Target);
        // Ambient script, so there is no task to blame. Attributing it to whichever label sits
        // nearest would point the author at code that does not contain the write.
        Assert.Null(effect.TaskId);
    }

    [Fact]
    public void AWriteInsideAContainerIsAttributedToThatContainer()
    {
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            SELECT 1 AS Id INTO #orders;

            purge:
            FOREACH @row IN #orders
            BEGIN
                DELETE FROM warehouse.Archive;
            END;
            """;

        var plan = To(script, "purge");

        var effect = Assert.Single(plan.Effects);
        Assert.Equal("DELETE FROM", effect.Action);
        Assert.Equal("warehouse.Archive", effect.Target);
        Assert.Equal("purge", effect.TaskId);
    }

    [Fact]
    public void StagingIntoATempTableIsNotAnEffect()
    {
        // A #temp dies with the session. Warning about it would put a confirmation in front of the
        // ordinary shape of a staging script, which is how a confirmation stops being read.
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            SELECT 1 AS Id INTO #orders;

            check_it:
            ASSERT 1 = 1, 'staged';
            """;

        var plan = To(script, "check_it");

        Assert.True(plan.Resolved, plan.Error);
        Assert.Empty(plan.Effects);
    }

    [Fact]
    public void SendingMailAndCopyingFilesAreEffects()
    {
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            copy_extract:
            COPY FILE 'C:\in\orders.csv' TO 'C:\out\orders.csv';

            notify:
            SEND EMAIL
                TO 'ops@example.com'
                FROM 'etl@example.com'
                SUBJECT 'Done'
                BODY 'Finished'
                AT warehouse;
            """;

        var plan = To(script, "notify");

        Assert.Collection(
            plan.Effects,
            effect =>
            {
                Assert.Contains("FILE", effect.Action);
                Assert.Equal("copy_extract", effect.TaskId);
            },
            effect =>
            {
                Assert.Equal("SEND EMAIL to", effect.Action);
                Assert.Equal("ops@example.com", effect.Target);
                Assert.Equal("notify", effect.TaskId);
            });
    }

    [Fact]
    public void PushedSqlIsAnEffectBecauseNothingHereCanProveItOnlyReads()
    {
        // The body of an EXECUTE is the connection's own dialect, pushed down unparsed. Calling it
        // read-only would be a claim this code is in no position to make.
        var plan = To(Sequential, "stage_orders");

        var effect = Assert.Single(plan.Effects);
        Assert.Equal("EXECUTE on", effect.Action);
        Assert.Equal("warehouse", effect.Target);
        Assert.Equal("stage_orders", effect.TaskId);
    }

    [Fact]
    public void AnEffectInsideControlFlowIsStillReported()
    {
        // Whether the branch is taken is not knowable here. Listing it costs the author a glance;
        // omitting it costs them a write they were never told about.
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            DECLARE @force INT = 0;

            IF @force = 1
            BEGIN
                DELETE FROM warehouse.Customers;
            END;

            check_it:
            ASSERT 1 = 1, 'done';
            """;

        var plan = To(script, "check_it");

        var effect = Assert.Single(plan.Effects);
        Assert.Equal("DELETE FROM", effect.Action);
        Assert.Equal("warehouse.Customers", effect.Target);
    }

    [Fact]
    public void AnEffectInASkippedTaskIsNotReported()
    {
        // The point of the effect list is that everything on it is about to happen. Padding it with
        // writes the slice will not perform is how an author learns to click through it.
        const string script = """
            CREATE CONNECTION warehouse AS MOCKDB();

            SELECT 1 AS Id INTO #orders;

            stage:
            ASSERT 1 = 1, 'staged';

            wipe_archive:
            FOREACH @row IN #orders
            BEGIN
                TRUNCATE TABLE warehouse.Archive;
            END;

            -- @after: stage
            publish:
            ASSERT 1 = 1, 'published';
            """;

        var plan = To(script, "publish");

        Assert.Equal(["wipe_archive"], plan.Skipped);
        Assert.DoesNotContain("TRUNCATE", plan.Script);
        Assert.Empty(plan.Effects);
    }

    // ── The three distinct answers ───────────────────────────────────────────

    [Fact]
    public void AScriptThatDoesNotParseIsRefusedRatherThanPlanned()
    {
        var plan = _plan.To("SELECT FROM WHERE", "publish");

        Assert.False(plan.Resolved);
        Assert.NotNull(plan.Error);
        Assert.Empty(plan.Script);
    }

    [Fact]
    public void AnUnknownTaskIsRefusedRatherThanPlannedAsAnEmptyRun()
    {
        // An empty plan and "there is no such task" are different answers. Returning the first for
        // the second would run nothing and report success.
        var plan = _plan.To(Sequential, "not_a_task");

        Assert.False(plan.Resolved);
        Assert.Contains("not_a_task", plan.Error);
    }

    [Fact]
    public void NoSelectionIsRefused()
    {
        var plan = _plan.To(Sequential, null);

        Assert.False(plan.Resolved);
        Assert.NotNull(plan.Error);
    }
}
