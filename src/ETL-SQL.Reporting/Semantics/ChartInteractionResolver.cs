using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>
/// The one place chart interaction semantics are decided.
///
/// Authoring — named <c>ACTIONS</c>/<c>INTERACTIONS</c> clauses and <c>CUSTOM</c> charts alike —
/// lowers into <see cref="InteractionSpec"/>, the canonical intent. <see cref="PlotPlanResolver"/>
/// then resolves that intent against the chart's resolved encodings into a
/// <see cref="ResolvedInteraction"/>, and the manifest layer projects a compact
/// <c>InteractionManifest</c> from it for browser delivery.
///
/// The selection key is derived from resolved encodings, never from <c>mapping:*</c> options or a
/// visual type name: a <c>CUSTOM</c> chart has no <c>MAPPINGS</c> clause, and falling through to
/// "whatever column happens to be first" cross-filters on the wrong column silently.
/// </summary>
public static class ChartInteractionResolver
{
    /// <summary>INTERACTIONS key naming the column a selection is keyed on.</summary>
    public const string MatchingKey = "MATCHING";

    /// <summary>INTERACTIONS key selecting the cross-visual effect.</summary>
    public const string SelectKey = "ON_SELECT";

    /// <summary>Channels that can carry a selection key, in resolution priority order.</summary>
    private static readonly FieldChannel[] KeyChannels =
    [
        FieldChannel.X, FieldChannel.XStart, FieldChannel.Theta, FieldChannel.Column,
        FieldChannel.Row, FieldChannel.Wrap, FieldChannel.Text, FieldChannel.Color, FieldChannel.Detail
    ];

    /// <summary>Channels that can carry the measure a proportional highlight divides by.</summary>
    private static readonly FieldChannel[] ValueChannels =
    [
        FieldChannel.Y, FieldChannel.YEnd, FieldChannel.Y2, FieldChannel.Radius, FieldChannel.Size
    ];

    /// <summary>
    /// Lowers a visual's authored actions and interactions into the canonical interaction contract.
    /// Both lowerers call this, so a named visual and a hand-composed <c>CUSTOM</c> chart that express
    /// the same intent produce the same spec.
    /// </summary>
    public static InteractionSpec Lower(CreateVisualStatement statement, ImmutableArray<FieldBinding> bindings)
    {
        var bindingKey = KeyField(bindings);
        var key = Matching(statement) ?? bindingKey;
        var value = ValueField(bindings);

        var fields = new[] { key, value }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(field => field!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        var mode = SelectionModeFor(statement);
        var selections = mode == SelectionMode.None || fields.IsEmpty
            ? ImmutableArray<SelectionSpec>.Empty
            : [new SelectionSpec("selection", mode, fields)];

        var triggers = statement.Actions
            .Select(action => new InteractionBinding(action.Trigger, Effect(action), Target(action), Parameter(action)))
            .Concat(statement.Interactions
                .Where(interaction => !interaction.Key.Equals(MatchingKey, StringComparison.OrdinalIgnoreCase))
                .Select(interaction => new InteractionBinding(interaction.Key, CrossVisualEffect(interaction.Value))))
            .ToImmutableArray();

        return new InteractionSpec(selections, triggers);
    }

    /// <summary>
    /// Resolves canonical intent against the chart's resolved encodings and its actual data columns.
    ///
    /// A key that does not name a real column resolves to null rather than to a positional guess: a
    /// cross-filter on the wrong column is a wrong answer, and a wrong answer outranks doing nothing.
    /// </summary>
    // COMPAT_BREAK: 0.19 — a CUSTOM chart's selection key is resolved from its encodings; the
    // browser's old columns[0] fallback filtered on the wrong column.
    public static ResolvedInteraction Resolve(
        ChartSpec spec,
        IReadOnlyCollection<string> dataColumns,
        SelectionHighlightMode highlight)
    {
        var interactions = spec.Interactions;
        var selection = interactions.Selections.IsDefaultOrEmpty
            ? null
            : interactions.Selections[0];

        var declared = selection?.Fields ?? [];
        var key = Present(declared.FirstOrDefault(), dataColumns)
            ?? Present(KeyField(AllBindings(spec)), dataColumns);
        var value = Present(declared.Skip(1).FirstOrDefault(), dataColumns)
            ?? Present(ValueField(AllBindings(spec)), dataColumns);

        var triggers = interactions.Bindings.IsDefaultOrEmpty
            ? ImmutableArray<ResolvedInteractionTrigger>.Empty
            : interactions.Bindings
                .Select(binding => new ResolvedInteractionTrigger(
                    binding.Trigger.ToUpperInvariant(), binding.Effect, binding.Target, binding.Parameter))
                .ToImmutableArray();

        var effect = triggers.FirstOrDefault(trigger => trigger.Trigger == SelectKey)?.Effect
            ?? InteractionEffect.Highlight;
        var mode = selection?.Mode ?? SelectionMode.None;

        // Proportional highlighting divides a selected value by the mark's own value. Without a
        // resolved measure column there is nothing to divide by, so it degrades to categorical.
        var resolvedHighlight = mode == SelectionMode.None
            ? SelectionHighlightMode.None
            : highlight == SelectionHighlightMode.Proportional && value is null
                ? SelectionHighlightMode.Categorical
                : highlight;

        return new ResolvedInteraction(mode, effect, resolvedHighlight, triggers) { Key = key, ValueKey = value };
    }

    /// <summary>
    /// Resolves interaction semantics for a visual with no chart contract — TABLE, SLICER, and the
    /// focused native layout modules. These have rows and columns but no resolved encodings, so the
    /// key comes from the authored MATCHING clause, then the X/LABEL mapping, and never from a
    /// positional fallback.
    /// </summary>
    public static ResolvedInteraction ResolveTabular(
        CreateVisualStatement statement,
        IReadOnlyCollection<string> dataColumns)
    {
        var mode = SelectionModeFor(statement);
        var key = Present(Matching(statement), dataColumns) ?? Present(MappedKey(statement), dataColumns);
        var value = Present(MappedValue(statement), dataColumns);
        var triggers = statement.Actions
            .Select(action => new ResolvedInteractionTrigger(
                action.Trigger.ToUpperInvariant(), Effect(action), Target(action), Parameter(action)))
            .Concat(statement.Interactions
                .Where(interaction => !interaction.Key.Equals(MatchingKey, StringComparison.OrdinalIgnoreCase))
                .Select(interaction => new ResolvedInteractionTrigger(
                    interaction.Key.ToUpperInvariant(), CrossVisualEffect(interaction.Value))))
            .ToImmutableArray();
        var effect = triggers.FirstOrDefault(trigger => trigger.Trigger == SelectKey)?.Effect
            ?? InteractionEffect.Highlight;

        return new ResolvedInteraction(
            mode,
            effect,
            mode == SelectionMode.None ? SelectionHighlightMode.None : SelectionHighlightMode.Categorical,
            triggers)
        { Key = key, ValueKey = value };
    }

    /// <summary>
    /// Whether a resolved layer set supports proportional highlighting: one baseline-anchored
    /// rectangular extent per row. Ranged rects, points, lines, and arcs do not.
    /// </summary>
    public static SelectionHighlightMode HighlightFor(ImmutableArray<ResolvedMarkLayer> layers) =>
        !layers.IsDefaultOrEmpty && layers.Any(layer => layer.ExtentAxis != MarkExtentAxis.None)
            ? SelectionHighlightMode.Proportional
            : SelectionHighlightMode.Categorical;

    private static ImmutableArray<FieldBinding> AllBindings(ChartSpec spec) =>
        spec.Bindings.Concat(spec.Layers.SelectMany(layer => layer.Bindings)).ToImmutableArray();

    private static string? KeyField(ImmutableArray<FieldBinding> bindings) => Field(bindings, KeyChannels);

    private static string? ValueField(ImmutableArray<FieldBinding> bindings) => Field(bindings, ValueChannels);

    private static string? Field(ImmutableArray<FieldBinding> bindings, FieldChannel[] channels)
    {
        if (bindings.IsDefaultOrEmpty) return null;
        foreach (var channel in channels)
        {
            var match = bindings.FirstOrDefault(binding =>
                binding.Channel == channel &&
                binding.SourceKind == BindingSourceKind.Field &&
                !string.IsNullOrWhiteSpace(binding.Field));
            if (match is not null) return match.Field;
        }
        return null;
    }

    private static string? Present(string? field, IReadOnlyCollection<string> columns) =>
        field is not null && columns.Any(column => column.Equals(field, StringComparison.OrdinalIgnoreCase))
            ? columns.First(column => column.Equals(field, StringComparison.OrdinalIgnoreCase))
            : null;

    private static string? Matching(CreateVisualStatement statement) => statement.Interactions
        .FirstOrDefault(interaction => interaction.Key.Equals(MatchingKey, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string? MappedKey(CreateVisualStatement statement) => Mapped(statement, "X", "LABEL", "NAME", "CATEGORY", "REGION");

    private static string? MappedValue(CreateVisualStatement statement) => Mapped(statement, "Y", "VALUE");

    private static string? Mapped(CreateVisualStatement statement, params string[] roles)
    {
        foreach (var role in roles)
        {
            var mapping = statement.Mappings.FirstOrDefault(item => item.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
            if (mapping is not null) return mapping.Column;
        }
        return null;
    }

    private static SelectionMode SelectionModeFor(CreateVisualStatement statement)
    {
        var value = statement.Interactions
            .FirstOrDefault(interaction => interaction.Key.Equals(SelectKey, StringComparison.OrdinalIgnoreCase))?.Value;
        if (value is null)
            // TABLE and SLICER cross-filter by default; every other visual opts in through INTERACTIONS.
            return statement.VisualType is VisualType.Table or VisualType.Slicer
                ? SelectionMode.Multiple
                : SelectionMode.None;
        return value.Equals("NONE", StringComparison.OrdinalIgnoreCase) ? SelectionMode.None : SelectionMode.Multiple;
    }

    private static InteractionEffect CrossVisualEffect(string value) =>
        value.Equals("FILTER", StringComparison.OrdinalIgnoreCase)
            ? InteractionEffect.Filter
            : InteractionEffect.Highlight;

    private static InteractionEffect Effect(VisualAction action) => action switch
    {
        SetParameterAction => InteractionEffect.SetParameter,
        DrillDownAction or DrillInAction => InteractionEffect.Drill,
        DrillReportAction or NavigatePageAction => InteractionEffect.Navigate,
        ClearFiltersAction => InteractionEffect.Filter,
        _ => InteractionEffect.Highlight
    };

    private static string? Target(VisualAction action) => action switch
    {
        DrillDownAction drill => drill.TargetVisual,
        DrillReportAction report => report.TargetReport,
        NavigatePageAction navigate => navigate.TargetPage,
        _ => null
    };

    private static string? Parameter(VisualAction action) =>
        action is SetParameterAction parameter ? parameter.ParameterName : null;
}
