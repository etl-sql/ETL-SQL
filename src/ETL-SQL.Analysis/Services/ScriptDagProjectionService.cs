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

            var dag = ScriptDagBuilder.Build(script);

            return ScriptDagProjection.Success(new ScriptDagDto(
                dag.Nodes.Select(n => new ScriptDagNodeDto(n.Id, n.Label, n.Type, new { line = n.Line })).ToList(),
                dag.Edges.Select(e => new ScriptDagEdgeDto(e.Source, e.Target, e.Label)).ToList()));
        }
        catch (Exception ex)
        {
            return ScriptDagProjection.Failed($"Could not parse script for flow preview: {ex.Message}");
        }
    }
}
