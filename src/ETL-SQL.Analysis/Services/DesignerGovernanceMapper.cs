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
    IReadOnlyList<string> Required,
    IReadOnlyList<GovernanceQualityStatementPayload> Quality,
    GovernanceQualityVocabularyPayload QualityVocabulary,
    string? StewardQueueUrl,
    IReadOnlyList<GovernanceDatasetPayload> Datasets,
    IReadOnlyList<string> AccessLevels,
    string? DatasetRegistryUrl);

/// <param name="Encryption">
/// none | machine | password | keyfile. Reported and never edited: a password or a key file is a
/// credential, and a surface that rewrites the clause holding one has read it, sent it through a
/// request, and written it back for no reason the author asked for.
/// </param>
/// <param name="Lifecycle">The refresh/export/publish steps this script already declares.</param>
public sealed record GovernanceDatasetPayload(
    string Name,
    string Access,
    string? Ttl,
    bool Compress,
    string Encryption,
    int Line,
    IReadOnlyList<GovernanceDatasetStepPayload> Lifecycle);

/// <param name="Kind">refresh | export | publish</param>
public sealed record GovernanceDatasetStepPayload(string Kind, string Detail, int Line);

/// <param name="MissingQuarantineTarget">
/// A column elects QUARANTINE and the statement routes nowhere, so those rows have nowhere to go.
/// </param>
public sealed record GovernanceQualityStatementPayload(
    string Id,
    string Kind,
    int Line,
    IReadOnlyList<GovernanceQualityColumnPayload> Columns,
    IReadOnlyList<GovernanceQualityRoutingPayload> Routing,
    bool MissingQuarantineTarget);

/// <param name="ScopeId">The same id the tag side uses, so one row addresses both.</param>
public sealed record GovernanceQualityColumnPayload(
    string ScopeId,
    string Column,
    string Table,
    int Line,
    IReadOnlyList<GovernanceQualityClausePayload> Clauses);

/// <param name="ActionExplicit">False when no action was written; the effective action is still WARN.</param>
public sealed record GovernanceQualityClausePayload(
    int Index,
    string Rule,
    string Action,
    bool ActionExplicit,
    int Line);

public sealed record GovernanceQualityRoutingPayload(
    string Action,
    string? Target,
    string? Retention,
    string Handling,
    int Line);

/// <param name="RuleForms">
/// The rule shapes the picker offers, each with the text it writes. A form is a starting point, not
/// a grammar: what reaches the script is what the author typed, and the parser is what accepts it.
/// </param>
public sealed record GovernanceQualityVocabularyPayload(
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Handling,
    IReadOnlyList<GovernanceQualityRuleFormPayload> RuleForms);

/// <param name="Template">The text placed in the rule box, with «guillemet» placeholders to replace.</param>
public sealed record GovernanceQualityRuleFormPayload(string Label, string Template, string Hint);

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
    /// <summary>
    /// The rule shapes the picker offers. Starting points only — the text stays editable and the
    /// parser is what decides whether it is a rule, so this list can grow without becoming a second,
    /// diverging definition of the grammar.
    /// </summary>
    private static readonly GovernanceQualityRuleFormPayload[] RuleForms =
    [
        new("Not null", "NOT NULL", "The only rule that fails on NULL; every other rule skips it."),
        new("Not blank", "NOT BLANK", "Must contain a non-whitespace character."),
        new("Unique", "UNIQUE", "Or UNIQUE WITH (a, b) for a composite key."),
        new("At least", ">= «number»", "A numeric floor, compared as decimal."),
        new("At most", "<= «number»", "A numeric ceiling, compared as decimal."),
        new("Between", "BETWEEN «lower» AND «upper»", "Inclusive; the bounds are expressions, so dates and variables work."),
        new("One of", "IN ('«a»', '«b»')", "Membership in a literal list. NOT IN excludes instead."),
        new("Length", "LENGTH BETWEEN «min» AND «max»", "Character count of the rendered value."),
        new("Matches", "MATCHES '«pattern»'", "Searches rather than matches whole. No backreferences or lookaround."),
        new("Castable", "CASTABLE AS «type»", "Uses the engine's own conversion, the one behind TRY_CAST."),
        new("Exists in", "EXISTS IN «table»(«column»)", "A relationship check. EXISTS WITH (a, b) IN t(x, y) for a scoped key."),
        new("Expression", "EXPR («predicate»)", "A cross-column predicate over the whole projected row."),
    ];

    public static GovernanceResponsePayload Response(
        ScriptGovernance governance,
        string script,
        bool applied,
        string? writeError,
        ScriptQuality? quality = null,
        string? stewardQueueUrl = null,
        ScriptDatasetLifecycle? datasets = null,
        string? datasetRegistryUrl = null) =>
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
            StewardshipTagCatalog.RequiredStewardshipTags.ToArray(),
            (quality?.Statements ?? []).Select(statement => new GovernanceQualityStatementPayload(
                statement.Id,
                statement.Kind,
                statement.Line,
                statement.Columns.Select(column => new GovernanceQualityColumnPayload(
                    column.ScopeId,
                    column.Column,
                    column.Table,
                    column.Line,
                    column.Clauses.Select(clause => new GovernanceQualityClausePayload(
                        clause.Index, clause.Rule, clause.Action, clause.ActionExplicit, clause.Line)).ToArray())).ToArray(),
                statement.Routing.Select(routing => new GovernanceQualityRoutingPayload(
                    routing.Action, routing.Target, routing.Retention, routing.Handling, routing.Line)).ToArray(),
                statement.MissingQuarantineTarget)).ToArray(),
            new GovernanceQualityVocabularyPayload(
                ScriptQualityRuleService.ColumnActions,
                ScriptQualityRuleService.HandlingModes,
                RuleForms),
            // Null on a host with no steward queue. The panel says where the queue lives rather than
            // offering a link that goes nowhere.
            stewardQueueUrl,
            (datasets?.Datasets ?? []).Select(dataset => new GovernanceDatasetPayload(
                dataset.Name,
                dataset.Access,
                dataset.Ttl,
                dataset.Compress,
                dataset.Encryption,
                dataset.Line,
                dataset.Lifecycle.Select(step => new GovernanceDatasetStepPayload(
                    step.Kind, step.Detail, step.Line)).ToArray())).ToArray(),
            ScriptDatasetLifecycleService.AccessLevels,
            // Null on a host with no dataset registry. Per-principal sharing lives there, with its
            // own permission model; the panel links to it rather than growing a second door.
            datasetRegistryUrl);
}
