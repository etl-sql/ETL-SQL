using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The flow DAG is rendered by two hosts (Portal Orchestrator job view, VS Code Visual Flow),
/// so its shape is a contract rather than a rendering detail.
/// </summary>
public class ScriptDagBuilderTests
{
    private static ScriptDag Build(string sql)
    {
        var script = new CoreParser(new Lexer(sql).Tokenize(), sql).Parse();
        return ScriptDagBuilder.Build(script);
    }

    [Fact]
    public void ChainsStatementsInOrder()
    {
        var dag = Build(@"
CREATE CONNECTION m AS MOCKDB();
SELECT UserID INTO #staging FROM m.Users;
SELECT UserID FROM #staging;");

        Assert.Equal(3, dag.Nodes.Count);
        Assert.Equal(2, dag.Edges.Count);

        // Edges follow statement order: each node links to the next.
        Assert.Equal(dag.Nodes[0].Id, dag.Edges[0].Source);
        Assert.Equal(dag.Nodes[1].Id, dag.Edges[0].Target);
        Assert.Equal(dag.Nodes[1].Id, dag.Edges[1].Source);
        Assert.Equal(dag.Nodes[2].Id, dag.Edges[1].Target);
    }

    [Fact]
    public void PreservesConditionalBranchesAndTheirConvergence()
    {
        var dag = Build("""
            IF 1 = 1 BEGIN
              SELECT 1;
            END
            ELSE BEGIN
              SELECT 2;
            END;
            ASSERT 1 = 1;
            """);

        Assert.Collection(
            dag.Nodes,
            n => Assert.Equal(("IF", "conditional"), (n.Label, n.Type)),
            n => Assert.Equal(("SELECT", "statement"), (n.Label, n.Type)),
            n => Assert.Equal(("SELECT", "statement"), (n.Label, n.Type)),
            n => Assert.Equal(("ASSERT", "validation"), (n.Label, n.Type)));

        var conditionalId = dag.Nodes[0].Id;
        var validationId = dag.Nodes[3].Id;
        Assert.Contains(dag.Edges, e => e.Source == conditionalId && e.Target == dag.Nodes[1].Id && e.Label == "TRUE");
        Assert.Contains(dag.Edges, e => e.Source == conditionalId && e.Target == dag.Nodes[2].Id && e.Label == "ELSE");
        Assert.Contains(dag.Edges, e => e.Source == dag.Nodes[1].Id && e.Target == validationId);
        Assert.Contains(dag.Edges, e => e.Source == dag.Nodes[2].Id && e.Target == validationId);
    }

    [Fact]
    public void PreservesParallelBranchesAndJoinsAtTheNextStage()
    {
        var dag = Build("""
            PARALLEL BEGIN
              SELECT 1 INTO #north;
              SELECT 2 INTO #south;
            END;
            EXPECT SCHEMA #north (Value INT);
            """);

        Assert.Equal(("PARALLEL", "parallel"), (dag.Nodes[0].Label, dag.Nodes[0].Type));
        Assert.Equal("validation", dag.Nodes[3].Type);
        Assert.Contains(dag.Edges, e => e.Source == dag.Nodes[0].Id && e.Target == dag.Nodes[1].Id && e.Label == "BRANCH 1");
        Assert.Contains(dag.Edges, e => e.Source == dag.Nodes[0].Id && e.Target == dag.Nodes[2].Id && e.Label == "BRANCH 2");
        Assert.Contains(dag.Edges, e => e.Source == dag.Nodes[1].Id && e.Target == dag.Nodes[3].Id);
        Assert.Contains(dag.Edges, e => e.Source == dag.Nodes[2].Id && e.Target == dag.Nodes[3].Id);
    }

    [Fact]
    public void ProjectsLoopBodyAndCompletionPathsWithoutCreatingACycle()
    {
        var dag = Build("""
            WHILE 1 = 1 BEGIN
              SELECT 1;
            END;
            SELECT 2;
            """);

        Assert.Equal("loop", dag.Nodes[0].Type);
        Assert.Contains(dag.Edges, e => e.Source == dag.Nodes[0].Id && e.Target == dag.Nodes[1].Id && e.Label == "BODY");
        Assert.Contains(dag.Edges, e => e.Source == dag.Nodes[0].Id && e.Target == dag.Nodes[2].Id && e.Label == "DONE");
        Assert.Contains(dag.Edges, e => e.Source == dag.Nodes[1].Id && e.Target == dag.Nodes[2].Id);
        Assert.DoesNotContain(dag.Edges, e => e.Source == dag.Nodes[1].Id && e.Target == dag.Nodes[0].Id);
    }

    [Fact]
    public void ClassifiesConnectionsAndTargets()
    {
        var dag = Build(@"
CREATE CONNECTION m AS MOCKDB();
SELECT UserID INTO #staging FROM m.Users;");

        Assert.Equal("connection", dag.Nodes[0].Type);
        Assert.Equal("CONNECT m", dag.Nodes[0].Label);

        Assert.Equal("io", dag.Nodes[1].Type);
        Assert.Equal("SELECT INTO #staging", dag.Nodes[1].Label);
    }

    [Fact]
    public void PlainSelectIsAStatementNotIo()
    {
        var dag = Build("SELECT 1;");
        Assert.Equal("statement", dag.Nodes[0].Type);
        Assert.Equal("SELECT", dag.Nodes[0].Label);
    }

    [Fact]
    public void SkipsHousekeepingStatements()
    {
        // DECLARE/SET/PRINT would dominate a real script's diagram without describing the flow.
        var dag = Build(@"
DECLARE @x INT;
SET @x = 1;
PRINT 'hello';
SELECT 1;");

        Assert.Single(dag.Nodes);
        Assert.Equal("SELECT", dag.Nodes[0].Label);
        Assert.Empty(dag.Edges);
    }

    [Fact]
    public void CarriesSourceLineSoHostsCanNavigate()
    {
        // The VS Code panel jumps the editor to this line when a node is clicked.
        var dag = Build("SELECT 1;\n\nSELECT 2;");
        Assert.Equal(1, dag.Nodes[0].Line);
        Assert.Equal(3, dag.Nodes[1].Line);
    }

    [Fact]
    public void EmptyScriptProducesEmptyGraph()
    {
        var dag = Build(string.Empty);
        Assert.Empty(dag.Nodes);
        Assert.Empty(dag.Edges);
    }

    [Fact]
    public void ClassifiesEtlAndReportAuthoringStatements()
    {
        var dag = Build("""
            CREATE CONNECTION vendor AS SFTP(HOST='sftp.example.invalid', USER='etl', PASSWORD='SECRET:sftp_password');
            SEND FILE 'C:\tmp\out.csv' TO '/inbox/out.csv' AT vendor;
            MOVE FILE 'C:\tmp\out.csv' TO 'C:\tmp\sent\out.csv';
            CREATE DATASET &sales AS (SELECT 1 AS amount);
            CREATE VISUAL SalesCard AS CARD (SOURCE = &sales);
            CREATE CONTAINER Drawer AS DRAWER ();
            CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = SalesCard));
            """);

        Assert.Collection(
            dag.Nodes,
            n => Assert.Equal(("CONNECT vendor", "connection"), (n.Label, n.Type)),
            n => Assert.Equal(("SEND FILE → vendor", "outbound"), (n.Label, n.Type)),
            n => Assert.Equal(("MOVE FILE", "destructive"), (n.Label, n.Type)),
            n => Assert.Equal(("DATASET &sales", "dataset"), (n.Label, n.Type)),
            n => Assert.Equal(("VISUAL SalesCard", "visual"), (n.Label, n.Type)),
            n => Assert.Equal(("CONTAINER Drawer", "container"), (n.Label, n.Type)),
            n => Assert.Equal(("PAGE Overview", "page"), (n.Label, n.Type)));
    }

    [Fact]
    public void ProjectionReturnsParseFailureWithoutThrowing()
    {
        var projection = new ScriptDagProjectionService().Project("CREATE CONNECTION c AS;");

        Assert.False(projection.Parsed);
        Assert.Contains("Could not parse script for flow preview", projection.Error);
        Assert.Empty(projection.Dag.Nodes);
    }
}
