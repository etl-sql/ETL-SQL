using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// Every stage inside a <c>PARALLEL</c> knows which block and which branch it is in, so the map can
/// draw each branch as its own lane.
///
/// <para>Without that, the layout places stages by depth alone: a three-step branch and a one-step
/// branch share rows, the short one's stage is centred against the long one's, and nothing on the
/// map says which steps run side by side. <c>PARALLEL</c> is the only construct in ETL-SQL that means
/// concurrency, so it is the one the map most needs to show plainly.</para>
/// </summary>
public class ScriptDagParallelLaneTests
{
    private static ScriptDag Build(string sql)
    {
        var script = new CoreParser(new Lexer(sql).Tokenize(), sql).Parse();
        return ScriptDagBuilder.Build(script);
    }

    private static ScriptDagNode Keyed(ScriptDag dag, string key) => dag.Nodes.Single(node => node.Key == key);

    private const string TwoBranches = """
        CREATE CONNECTION m AS MOCKDB();

        load_fanout:
        PARALLEL BEGIN
            load_primary:
            EXECUTE m BEGIN
                SELECT 1;
            END;

            BEGIN
                stage_audit:
                EXECUTE m BEGIN
                    SELECT 2;
                END;

                load_audit:
                EXECUTE m BEGIN
                    SELECT 3;
                END;
            END;
        END;

        after_load:
        EXECUTE m BEGIN
            SELECT 4;
        END;
        """;

    [Fact]
    public void EveryStageInABranchKnowsItsBlockAndBranch()
    {
        var dag = Build(TwoBranches);
        var block = Keyed(dag, "load_fanout");

        Assert.Equal(2, block.Lanes);
        Assert.Equal((block.Id, 0), (Keyed(dag, "load_primary").LaneOf, Keyed(dag, "load_primary").Lane));
        Assert.Equal((block.Id, 1), (Keyed(dag, "stage_audit").LaneOf, Keyed(dag, "stage_audit").Lane));
        Assert.Equal((block.Id, 1), (Keyed(dag, "load_audit").LaneOf, Keyed(dag, "load_audit").Lane));
    }

    [Fact]
    public void TheBlockAndWhatFollowsItAreInNoLane()
    {
        var dag = Build(TwoBranches);

        Assert.Null(Keyed(dag, "load_fanout").LaneOf);
        Assert.Null(Keyed(dag, "after_load").LaneOf);
        Assert.Null(dag.Nodes.Single(node => node.Label.StartsWith("CONNECT", StringComparison.Ordinal)).LaneOf);
    }

    /// <summary>
    /// A PARALLEL inside a branch is itself a stage of that branch, and its own branches belong to it —
    /// not to the outer block, which would flatten two levels of concurrency into one.
    /// </summary>
    [Fact]
    public void ANestedBlockBelongsToItsBranchAndItsBranchesToIt()
    {
        var dag = Build("""
            CREATE CONNECTION m AS MOCKDB();

            outer_block:
            PARALLEL BEGIN
                first_task:
                EXECUTE m BEGIN
                    SELECT 1;
                END;

                inner_block:
                PARALLEL BEGIN
                    left_task:
                    EXECUTE m BEGIN
                        SELECT 2;
                    END;

                    right_task:
                    EXECUTE m BEGIN
                        SELECT 3;
                    END;
                END;
            END;
            """);

        var outer = Keyed(dag, "outer_block");
        var inner = Keyed(dag, "inner_block");

        Assert.Equal((outer.Id, 1), (inner.LaneOf, inner.Lane));
        Assert.Equal(2, inner.Lanes);
        Assert.Equal((inner.Id, 0), (Keyed(dag, "left_task").LaneOf, Keyed(dag, "left_task").Lane));
        Assert.Equal((inner.Id, 1), (Keyed(dag, "right_task").LaneOf, Keyed(dag, "right_task").Lane));
    }

    [Fact]
    public void TheProjectionSendsTheLanesToTheMap()
    {
        var projected = new ScriptDagProjectionService().Project(TwoBranches);
        Assert.True(projected.Parsed, projected.Error);

        var byKey = projected.Dag.Nodes.ToDictionary(
            node => (string?)node.Meta!.GetType().GetProperty("key")!.GetValue(node.Meta) ?? node.Id,
            node => node);
        object? Meta(string key, string name) => byKey[key].Meta!.GetType().GetProperty(name)!.GetValue(byKey[key].Meta);

        Assert.Equal(2, Meta("load_fanout", "lanes"));
        Assert.Equal(byKey["load_fanout"].Id, Meta("load_audit", "laneOf"));
        Assert.Equal(1, Meta("load_audit", "lane"));
        Assert.Null(Meta("after_load", "laneOf"));
    }
}
