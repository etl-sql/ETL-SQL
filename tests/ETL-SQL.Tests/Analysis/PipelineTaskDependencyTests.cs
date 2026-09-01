using ETL_SQL.Analysis.Services;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// Explicit sequential dependencies between pipeline tasks.
///
/// <para>An edge is written into the script as an <c>-- @after:</c> tag above the task's label. The
/// lexer reads it as a tag and the parser skips tags between statements, so the declaration is free
/// at run time and the script stays the source of truth.</para>
///
/// <para>The rule these tests exist to hold: ETL-SQL runs a script top to bottom, so a declared
/// dependency that contradicted the physical order would be the canvas lying about the file.
/// Several incoming edges are a join — the task waits for all of them — and never a licence to run
/// anything concurrently, which in ETL-SQL is only ever a <c>PARALLEL</c> block.</para>
/// </summary>
public class PipelineTaskDependencyTests
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

        merge_all:
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

    private List<string> DependenciesOf(string script, string id) =>
        _tasks.Read(script).Single(task => task.Id == id).DependsOn.Select(dependency => dependency.Id).ToList();

    [Fact]
    public void ConnectDeclaresTheEdgeInTheScriptAndTheScriptStillParses()
    {
        var result = _tasks.Connect(Script, "fetch_orders", "merge_all");

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        Assert.Contains("-- @after: fetch_orders", result.Script, StringComparison.Ordinal);
        Assert.Equal(["fetch_orders"], DependenciesOf(result.Script, "merge_all"));

        // Only the tag line was added; the tasks themselves are untouched.
        var withoutTag = string.Join("\n", result.Script
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("-- @after:", StringComparison.Ordinal)));
        Assert.Equal(Script.Replace("\r\n", "\n", StringComparison.Ordinal), withoutTag);
    }

    [Fact]
    public void SeveralIncomingEdgesAreAJoinAndStayOneStatementApiece()
    {
        var first = _tasks.Connect(Script, "fetch_orders", "merge_all");
        Assert.True(first.Applied, first.Error);
        var second = _tasks.Connect(first.Script, "fetch_rates", "merge_all");
        Assert.True(second.Applied, second.Error);

        AssertParses(second.Script);
        Assert.Equal(["fetch_orders", "fetch_rates"], DependenciesOf(second.Script, "merge_all"));

        // One tag line carries the join, and nothing about it says "run these at the same time":
        // there is no PARALLEL block anywhere in what the canvas wrote.
        Assert.Equal(1, second.Script.Split("-- @after:", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("PARALLEL", second.Script, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, _tasks.Read(second.Script).Count);
    }

    [Fact]
    public void ConnectingBackwardsAlsoReordersSoTheDeclarationCannotContradictExecution()
    {
        // merge_all runs last, so "fetch_orders after merge_all" is only true if fetch_orders moves
        // below it. A tag claiming a dependency the linear script does not honour would be a lie.
        var result = _tasks.Connect(Script, "merge_all", "fetch_orders");

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        Assert.Equal(["merge_all"], DependenciesOf(result.Script, "fetch_orders"));

        var order = _tasks.Read(result.Script).Select(task => task.Id).ToList();
        Assert.True(order.IndexOf("merge_all") < order.IndexOf("fetch_orders"),
            $"A declared dependency must run before its dependent. Order was: {string.Join(", ", order)}");
    }

    [Fact]
    public void EveryDeclaredDependencyRunsBeforeItsDependent()
    {
        // The invariant, checked over a graph built by several connects rather than one.
        var script = Script;
        foreach (var (from, to) in new[] { ("fetch_orders", "merge_all"), ("fetch_rates", "merge_all") })
        {
            var step = _tasks.Connect(script, from, to);
            Assert.True(step.Applied, step.Error);
            script = step.Script;
        }

        var positions = _tasks.Read(script)
            .Select((task, index) => (task, index))
            .ToDictionary(entry => entry.task.Id, entry => entry.index, StringComparer.OrdinalIgnoreCase);

        foreach (var task in _tasks.Read(script))
        {
            foreach (var dependency in task.DependsOn)
            {
                Assert.True(positions[dependency.Id] < positions[task.Id],
                    $"'{task.Id}' declares it runs after '{dependency.Id}', but the script runs it first.");
            }
        }
    }

    [Fact]
    public void DisconnectRemovesOneEdgeAndLeavesTheRest()
    {
        var script = _tasks.Connect(Script, "fetch_orders", "merge_all").Script;
        script = _tasks.Connect(script, "fetch_rates", "merge_all").Script;

        var result = _tasks.Disconnect(script, "fetch_orders", "merge_all");

        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        Assert.Equal(["fetch_rates"], DependenciesOf(result.Script, "merge_all"));

        // Removing the last one takes the tag with it: a tag declaring nothing reads as a dependency
        // the reader cannot see.
        var emptied = _tasks.Disconnect(result.Script, "fetch_rates", "merge_all");
        Assert.True(emptied.Applied, emptied.Error);
        Assert.DoesNotContain("@after", emptied.Script, StringComparison.Ordinal);
        Assert.Empty(DependenciesOf(emptied.Script, "merge_all"));
        Assert.Equal(3, _tasks.Read(emptied.Script).Count);
    }

    [Fact]
    public void ACycleIsRefusedRatherThanDrawn()
    {
        var script = _tasks.Connect(Script, "fetch_orders", "merge_all").Script;
        var cycle = _tasks.Connect(script, "merge_all", "fetch_orders");

        Assert.False(cycle.Applied);
        Assert.Contains("cycle", cycle.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(script, cycle.Script);
    }

    [Fact]
    public void ATaskCannotDependOnItself()
    {
        var result = _tasks.Connect(Script, "merge_all", "merge_all");

        Assert.False(result.Applied);
        Assert.Contains("itself", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Script, result.Script);
    }

    [Fact]
    public void ConnectingTwiceIsNotAnEdit()
    {
        var once = _tasks.Connect(Script, "fetch_orders", "merge_all");
        var twice = _tasks.Connect(once.Script, "fetch_orders", "merge_all");

        Assert.True(twice.Applied);
        Assert.Equal(once.Script, twice.Script);
    }

    [Fact]
    public void DisconnectingAnEdgeThatWasNeverDeclaredSaysSo()
    {
        var result = _tasks.Disconnect(Script, "fetch_orders", "merge_all");

        Assert.False(result.Applied);
        Assert.Contains("does not wait on", result.Error!, StringComparison.Ordinal);
        Assert.Equal(Script, result.Script);
    }

    [Fact]
    public void ATagAboveSomethingElseIsNotThisTasksDependency()
    {
        // Only the line immediately above the label counts. A tag further up belongs to whatever sits
        // between them, and claiming it would attach one task's dependencies to another.
        const string strays = """
            CREATE CONNECTION staging_db AS MOCKDB();

            -- @after: fetch_orders

            merge_all:
            EXECUTE staging_db BEGIN
                SELECT 3;
            END;
            """;

        AssertParses(strays);
        Assert.Empty(_tasks.Read(strays).Single(task => task.Id == "merge_all").DependsOn);
    }

    [Fact]
    public void MovingAndDeletingStillWorkOnTasksThatCarryDependencies()
    {
        var script = _tasks.Connect(Script, "fetch_orders", "merge_all").Script;
        script = _tasks.Connect(script, "fetch_rates", "merge_all").Script;

        // A move takes the tag with the task, so the declaration does not end up over its neighbour.
        var moved = _tasks.Move(script, "merge_all", "fetch_orders");
        Assert.True(moved.Applied, moved.Error);
        AssertParses(moved.Script);
        Assert.Equal(["fetch_orders", "fetch_rates"], DependenciesOf(moved.Script, "merge_all"));

        var removed = _tasks.Remove(moved.Script, "merge_all");
        Assert.True(removed.Applied, removed.Error);
        AssertParses(removed.Script);
        Assert.DoesNotContain("@after", removed.Script, StringComparison.Ordinal);
        Assert.Equal(["fetch_orders", "fetch_rates"], _tasks.Read(removed.Script).Select(task => task.Id));
    }

    [Fact]
    public void DeclarationsSurviveTheCanonicalFormatter()
    {
        var script = _tasks.Connect(Script, "fetch_orders", "merge_all").Script;
        script = _tasks.Connect(script, "fetch_rates", "merge_all").Script;

        var formatted = SqlFormatter.Format(script, new FormatterOptions());

        AssertParses(formatted);
        Assert.Equal(["fetch_orders", "fetch_rates"], DependenciesOf(formatted, "merge_all"));
    }

    [Fact]
    public void TheProjectionDrawsDeclaredEdgesInsteadOfTheImplicitSequentialOne()
    {
        var script = _tasks.Connect(Script, "fetch_orders", "merge_all").Script;
        script = _tasks.Connect(script, "fetch_rates", "merge_all").Script;

        var projected = new ScriptDagProjectionService().Project(script);
        Assert.True(projected.Parsed, projected.Error);

        var nodeByKey = projected.Dag.Nodes
            .Where(node => KeyOf(node) is not null)
            .ToDictionary(node => KeyOf(node)!, node => node.Id, StringComparer.OrdinalIgnoreCase);

        var incoming = projected.Dag.Edges
            .Where(edge => edge.Target == nodeByKey["merge_all"])
            .Select(edge => edge.Source)
            .ToList();

        // Both declared dependencies, and nothing else: the implicit "runs after the statement above"
        // edge is replaced by what the author actually said.
        Assert.Equal(2, incoming.Count);
        Assert.Contains(nodeByKey["fetch_orders"], incoming);
        Assert.Contains(nodeByKey["fetch_rates"], incoming);
    }

    [Fact]
    public void AProjectionWithNoDeclarationsIsUnchanged()
    {
        var plain = new ScriptDagProjectionService().Project(Script);
        var withTag = new ScriptDagProjectionService().Project(
            _tasks.Connect(Script, "fetch_orders", "merge_all").Script);

        Assert.True(plain.Parsed);
        Assert.True(withTag.Parsed);

        // Declaring one edge must not disturb the rest of the map.
        Assert.Equal(plain.Dag.Nodes.Count, withTag.Dag.Nodes.Count);
        Assert.Equal(plain.Dag.Edges.Count, withTag.Dag.Edges.Count);
    }

    private static string? KeyOf(ScriptDagNodeDto node) =>
        node.Meta?.GetType().GetProperty("key")?.GetValue(node.Meta) as string;
}
