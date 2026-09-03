using ETL_SQL.Common;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Analysis.Services;

/// <param name="Applied">
/// False with an <c>Error</c> is an ordinary answer for a write — a value the tag catalog refuses, a
/// scope this script does not have — and the panel says so rather than redrawing unchanged.
/// </param>
/// <param name="Catalog">
/// The standard tag vocabulary, sent with every answer so the panel offers the tags the engine
/// validates against rather than keeping its own copy and letting it drift.
/// </param>
public sealed record GovernanceResponsePayload(
    bool Parsed,
    string? Error,
    string Script,
    bool Applied,
    IReadOnlyList<GovernanceScopePayload> Scopes,
    IReadOnlyList<GovernanceFindingPayload> Findings,
    IReadOnlyList<string> Tasks,
    IReadOnlyList<GovernanceTagDefinitionPayload> Catalog,
    IReadOnlyList<string> Required);

/// <param name="WriteTarget">inline | statement | header — the form a new tag here would take.</param>
/// <param name="Producer">The labelled task that writes this object, for grouping only.</param>
public sealed record GovernanceScopePayload(
    string Id,
    string Kind,
    string Name,
    string? Table,
    string WriteTarget,
    int Line,
    IReadOnlyList<GovernanceTagPayload> Tags,
    IReadOnlyList<string> Missing,
    string? Producer,
    string? Detail);

/// <param name="Origin">inline | statement | script | derived.</param>
/// <param name="DerivedFrom">Where an inherited value comes from, said out loud.</param>
/// <param name="Problem">Why the catalog rejects this value, when it does.</param>
public sealed record GovernanceTagPayload(
    string Name,
    string Value,
    string Origin,
    string? DerivedFrom,
    bool Editable,
    bool Known,
    string? Problem);

public sealed record GovernanceFindingPayload(
    string Code,
    string Severity,
    string Message,
    int Line,
    int Column,
    string? ScopeId);

/// <param name="Kind">string | boolean | enum | duration.</param>
/// <param name="Scopes">Which of script/table/column the tag is defined for.</param>
public sealed record GovernanceTagDefinitionPayload(
    string Name,
    string Kind,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Values);

/// <summary>
/// Shapes a governance projection into the payload both Studio hosts return.
///
/// <para>Shared rather than written twice on purpose: Studio ships one client to both hosts, so a
/// field one host spells differently is a panel that works in the browser and is blank on the
/// desktop — the kind of failure that only shows up on whichever host nobody happened to open.</para>
/// </summary>
public static class DesignerGovernanceMapper
{
    public static GovernanceResponsePayload Response(
        ScriptGovernance governance,
        string script,
        bool applied,
        string? writeError) =>
        new(
            governance.Parsed,
            writeError ?? governance.Error,
            script,
            applied,
            governance.Scopes.Select(scope => new GovernanceScopePayload(
                scope.Id,
                scope.Kind,
                scope.Name,
                scope.Table,
                scope.WriteTarget,
                scope.Line,
                scope.Tags.Select(tag => new GovernanceTagPayload(
                    tag.Name, tag.Value, tag.Origin, tag.DerivedFrom, tag.Editable, tag.Known, tag.Problem)).ToArray(),
                scope.MissingRequired,
                scope.Producer,
                scope.Detail)).ToArray(),
            governance.Findings.Select(finding => new GovernanceFindingPayload(
                finding.Code, finding.Severity, finding.Message, finding.Line, finding.Column, finding.ScopeId)).ToArray(),
            governance.TaskScopes,
            // The projected rule tags are left out: @expect/@fail are published by the engine from a
            // column's EXPECT clauses, and offering them in a tag picker would invite an author to
            // hand-write one, which is inert and looks enforced.
            StewardshipTagCatalog.StandardTags
                .Where(definition => !ColumnRuleParser.IsRuleTagKey(definition.Name))
                .Select(definition => new GovernanceTagDefinitionPayload(
                    definition.Name,
                    definition.ValueKind.ToString().ToLowerInvariant(),
                    definition.Scopes,
                    definition.AllowedValues))
                .ToArray(),
            StewardshipTagCatalog.RequiredStewardshipTags.ToArray());
}
