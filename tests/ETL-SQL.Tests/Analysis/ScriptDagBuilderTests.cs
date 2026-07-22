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
