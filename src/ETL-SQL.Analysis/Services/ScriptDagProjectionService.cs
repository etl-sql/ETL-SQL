using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Analysis.Services;

public sealed record ScriptDagNodeDto(string Id, string Label, string Type, object? Meta = null);

public sealed record ScriptDagEdgeDto(string Source, string Target, string? Label = null);

public sealed record ScriptDagDto(IReadOnlyList<ScriptDagNodeDto> Nodes, IReadOnlyList<ScriptDagEdgeDto> Edges);

public sealed record ScriptDagProjection(bool Parsed, ScriptDagDto Dag, string? Error)
{
    public static ScriptDagProjection Empty { get; } = new(true, new ScriptDagDto([], []), null);
    public static ScriptDagProjection Success(ScriptDagDto dag) => new(true, dag, null);
    public static ScriptDagProjection Failed(string error) => new(false, new ScriptDagDto([], []), error);
}

public interface IScriptDagProjection
{
    ScriptDagProjection Project(string? scriptText);
}

/// <summary>
/// Projects ETL-SQL text into the host-neutral DAG shape used by design-time flow views.
/// </summary>
public sealed class ScriptDagProjectionService : IScriptDagProjection
{
    public ScriptDagProjection Project(string? scriptText)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            return ScriptDagProjection.Empty;

        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new CoreParser(tokens, scriptText).Parse();
            if (script.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } diagnostic)
                return ScriptDagProjection.Failed($"Could not parse script for flow preview: {diagnostic.Message}");

            var tasks = new PipelineTaskAuthoringService().Read(scriptText);
            var dag = ScriptDagBuilder.Build(script, CollapsedWrappers(tasks));
            var edges = WithDeclaredDependencies(dag, tasks);

            return ScriptDagProjection.Success(new ScriptDagDto(
                // `key` is the section label, when the statement has one. It is what the canvas
                // tracks a node by across a re-projection: ids are positional and shift under any
                // hand edit above them, so selection keyed by id follows the wrong box.
                dag.Nodes.Select(n => new ScriptDagNodeDto(n.Id, n.Label, n.Type, new { line = n.Line, key = n.Key })).ToList(),
                edges.Select(e => new ScriptDagEdgeDto(e.Source, e.Target, e.Label)).ToList()));
        }
        catch (Exception ex)
        {
            return ScriptDagProjection.Failed($"Could not parse script for flow preview: {ex.Message}");
        }
    }

    /// <summary>
    /// Replaces the implicit sequential edges into a task that declares its own dependencies.
    ///
    /// <para>Sequence is the default story: without a declaration, a stage runs after the one above
    /// it, and that is what the builder draws. A task carrying <c>-- @after: a, b</c> has said
    /// something more specific, so its incoming edges become exactly the ones it named. Several of
    /// them is a dependency join — it waits for all of them — and never an instruction to run
    /// anything at the same time; the script is still executed top to bottom, and concurrency in
    /// ETL-SQL is only ever a <c>PARALLEL</c> block.</para>
    ///
    /// <para>A declared name with no task behind it is dropped rather than drawn as an edge to
    /// nothing: the author is mid-rename, and inventing a node for a name that is not in the script
    /// would make the canvas disagree with the file it is drawn from.</para>
    ///
    /// <para>An edge that only fires on an outcome or on a condition says so in its label, and the
    /// shared renderer colours and dashes it from that label. The label is the whole channel: hosts
    /// draw the same graph shape, so a style that lived only in one host's canvas would make the
    /// same script read differently in the next one.</para>
    /// </summary>
    private static IReadOnlyList<ScriptDagEdge> WithDeclaredDependencies(ScriptDag dag, IReadOnlyList<PipelineTask> tasks)
    {
        var declared = tasks
            .Where(task => task.DependsOn.Count > 0)
            .ToDictionary(task => task.Id, task => task.DependsOn, StringComparer.OrdinalIgnoreCase);
        if (declared.Count == 0) return dag.Edges;

        var nodeByKey = dag.Nodes
            .Where(node => node.Key is not null)
            .GroupBy(node => node.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

        var keyByNode = dag.Nodes
            .Where(node => node.Key is not null)
            .ToDictionary(node => node.Id, node => node.Key!, StringComparer.Ordinal);

        var rewritten = dag.Edges
            .Where(edge => !(keyByNode.TryGetValue(edge.Target, out var key) && declared.ContainsKey(key)))
            .ToList();

        foreach (var (taskId, dependencies) in declared)
        {
            if (!nodeByKey.TryGetValue(taskId, out var targetNode)) continue;
            foreach (var dependency in dependencies)
            {
                if (nodeByKey.TryGetValue(dependency.Id, out var sourceNode))
                    rewritten.Add(new ScriptDagEdge(sourceNode, targetNode, EdgeLabel(dependency)));
            }
        }

        return rewritten;
    }

    /// <summary>
    /// The guard and gate wrappers the builder should draw as the task they wrap.
    /// </summary>
    private static Dictionary<int, int> CollapsedWrappers(IReadOnlyList<PipelineTask> tasks) =>
        tasks
            .Where(task => (task.Guarded || task.Gate is not null)
                && task.StatementStart >= 0
                && task.InnerStart > task.StatementStart)
            .GroupBy(task => task.StatementStart)
            .ToDictionary(group => group.Key, group => group.First().InnerStart);

    /// <summary>
    /// What an edge says about itself, or null when it is plain precedence and has nothing to add.
    ///
    /// <para>A long expression is elided rather than wrapped: the badge sits on the line between two
    /// cards, and the whole condition is one click away in the inspector.</para>
    /// </summary>
    private static string? EdgeLabel(PipelineDependency dependency) => dependency.Condition switch
    {
        PipelineEdgeCondition.OnSuccess => "ON SUCCESS",
        PipelineEdgeCondition.OnFailure => "ON FAILURE",
        PipelineEdgeCondition.OnCompletion => "ON COMPLETION",
        PipelineEdgeCondition.Expression => $"WHEN {Shorten(dependency.Expression)}",
        _ => null,
    };

    private const int MaxEdgeLabelExpression = 32;

    private static string Shorten(string? expression)
    {
        var text = (expression ?? string.Empty).Trim();
        return text.Length <= MaxEdgeLabelExpression ? text : text[..(MaxEdgeLabelExpression - 1)] + "…";
    }
}
