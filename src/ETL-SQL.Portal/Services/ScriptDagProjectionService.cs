using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Models;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// The outcome of projecting a script to a DAG. Deliberately not an <c>IActionResult</c>: the
/// distinction between "parsed" and "could not parse" is a domain fact, and mapping it to a status
/// code is the controller's job. Keeping them apart is what lets this be tested without HTTP.
/// </summary>
public sealed record ScriptDagProjection(bool Parsed, DagDto Dag, string? Error)
{
    public static ScriptDagProjection Empty { get; } = new(true, new DagDto([], []), null);
    public static ScriptDagProjection Success(DagDto dag) => new(true, dag, null);
    public static ScriptDagProjection Failed(string error) => new(false, new DagDto([], []), error);
}

/// <summary>
/// Turns ETL-SQL script text into the DAG shape the structure and flow views render.
///
/// Extracted from <c>OrchestratorController</c>, which was lexing, parsing, building the graph and
/// converting it to DTOs inline — the last place in the Portal controllers doing parsing work.
/// </summary>
public sealed class ScriptDagProjectionService
{
    /// <summary>
    /// Projects script text to a DAG. Empty or whitespace-only script is an empty graph rather than
    /// an error: a job with no script is a legitimate state, not a parse failure.
    /// </summary>
    public ScriptDagProjection Project(string? scriptText)
    {
        if (string.IsNullOrWhiteSpace(scriptText)) return ScriptDagProjection.Empty;

        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new CoreParser(tokens, scriptText).Parse();
            var dag = ScriptDagBuilder.Build(script);

            return ScriptDagProjection.Success(new DagDto(
                dag.Nodes.Select(n => new DagNodeDto(n.Id, n.Label, n.Type, new { line = n.Line })).ToList(),
                dag.Edges.Select(e => new DagEdgeDto(e.Source, e.Target, e.Label)).ToList()));
        }
        catch (Exception ex)
        {
            // The script is caller-supplied and may be anything, so a malformed one must surface as a
            // handled outcome rather than a 500. The message is the parser's own, which describes the
            // syntax and never the script's values.
            return ScriptDagProjection.Failed($"Could not parse job script: {ex.Message}");
        }
    }
}
