using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>Lowers the existing named Report-SQL visual syntax into renderer-neutral intent.</summary>
public sealed class NamedVisualChartLowerer
{
    private static readonly HashSet<VisualType> Supported =
    [
        VisualType.Bar, VisualType.Line, VisualType.Scatter, VisualType.Pie,
        VisualType.Donut, VisualType.Combo
    ];

    public static bool Supports(VisualType type) => Supported.Contains(type);

    public ChartSpec Lower(CreateVisualStatement statement, VisualManifest manifest)
    {
        if (!Supports(statement.VisualType))
            throw new NotSupportedException($"Visual type {statement.VisualType} is not in the representative GoG slice.");

        var bindings = BuildBindings(statement).ToImmutableArray();
        var layers = BuildLayers(statement, bindings).ToImmutableArray();
        var scales = BuildScales(statement, bindings, manifest).ToImmutableArray();
        var title = manifest.Options.GetValueOrDefault("title") ?? manifest.Name;

        var spec = ChartSpec.Create(
            id: manifest.Name,
            dataReference: statement.Source.TempTableName ?? $"inline:{manifest.Name}",
            bindings: bindings,
            layers: layers,
            coordinate: new CoordinateSpec(IsPolar(statement.VisualType) ? CoordinateKind.Polar : CoordinateKind.Cartesian,
                InnerRadius: statement.VisualType == VisualType.Donut ? ResolveInnerRadius(manifest) : null),
            scales: scales,
            formatting: new FormattingSpec(
                CultureInfo.InvariantCulture.Name,
                "UTC",
                manifest.Options.GetValueOrDefault("NULL_LABEL") ?? "",
                statement.Mappings.Select(mapping => new FieldFormat(mapping.Column, mapping.Format)).ToImmutableArray()),
            nullHandling: new NullHandlingSpec(
                statement.VisualType == VisualType.Line || statement.VisualType == VisualType.Combo
                    ? NullValuePolicy.Gap
                    : NullValuePolicy.Skip,
                []),
            theme: new ThemeSpec(manifest.Styles?.GetValueOrDefault("THEME") ?? "default", BuildStyleTokens(manifest)),
            accessibility: new AccessibilitySpec(
                title,
                manifest.Options.GetValueOrDefault("subtitle"),
                null,
                true),
            title: title,
            interactions: BuildInteractions(statement));
        spec.Validate();
        return spec;
    }

    private static IEnumerable<FieldBinding> BuildBindings(CreateVisualStatement statement)
    {
        foreach (var mapping in statement.Mappings)
        {
            var channel = MapChannel(statement.VisualType, mapping.Role);
            if (channel is null) continue;
            yield return new FieldBinding(
                channel.Value,
                mapping.Column,
                InferSemanticKind(statement.VisualType, channel.Value, mapping),
                ScaleId(channel.Value),
                channel == FieldChannel.Y2 ? AxisRole.Secondary : AxisRole.Primary,
                ParseSort(statement.Options),
                mapping.Format);
        }

        if (statement.VisualType == VisualType.Combo && statement.TypedSeries.Count > 0)
        {
            var used = statement.Mappings.Select(mapping => mapping.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var firstType = statement.TypedSeries[0].SeriesType;
            foreach (var series in statement.TypedSeries)
            {
                if (!used.Add(series.Column)) continue;
                var secondary = !series.SeriesType.Equals(firstType, StringComparison.OrdinalIgnoreCase);
                var channel = secondary ? FieldChannel.Y2 : FieldChannel.Y;
                yield return new FieldBinding(channel, series.Column, DataSemanticKind.Quantitative,
                    ScaleId(channel), secondary ? AxisRole.Secondary : AxisRole.Primary);
            }
        }
    }

    private static IEnumerable<MarkLayerSpec> BuildLayers(
        CreateVisualStatement statement,
        ImmutableArray<FieldBinding> bindings)
    {
        if (statement.VisualType == VisualType.Combo && statement.TypedSeries.Count > 0)
        {
            var x = bindings.Where(binding => binding.Channel == FieldChannel.X).ToImmutableArray();
            var firstType = statement.TypedSeries[0].SeriesType;
            for (var index = 0; index < statement.TypedSeries.Count; index++)
            {
                var series = statement.TypedSeries[index];
                var secondary = !series.SeriesType.Equals(firstType, StringComparison.OrdinalIgnoreCase);
                var channel = secondary ? FieldChannel.Y2 : FieldChannel.Y;
                var y = bindings.First(binding => binding.Field.Equals(series.Column, StringComparison.OrdinalIgnoreCase));
                yield return new MarkLayerSpec(
                    $"series-{index:D2}-{Sanitize(series.Column)}",
                    series.SeriesType.Equals("bar", StringComparison.OrdinalIgnoreCase) ? MarkKind.Rect : MarkKind.Line,
                    index,
                    x.Add(y with { Channel = channel, Axis = secondary ? AxisRole.Secondary : AxisRole.Primary }),
                    [new StyleToken("series", series.Column)],
                    series.Column);
            }
        }
        else
        {
            yield return new MarkLayerSpec(
                "primary",
                statement.VisualType switch
                {
                    VisualType.Bar => MarkKind.Rect,
                    VisualType.Line => MarkKind.Line,
                    VisualType.Scatter => MarkKind.Point,
                    VisualType.Pie or VisualType.Donut => MarkKind.Arc,
                    VisualType.Combo => MarkKind.Line,
                    _ => throw new InvalidOperationException()
                },
                0,
                bindings,
                [],
                statement.Name);
        }

        for (var index = 0; index < statement.Overlays.Count; index++)
        {
            var overlay = statement.Overlays[index];
            yield return new MarkLayerSpec(
                $"rule-{index:D2}-{overlay.OverlayType.ToString().ToLowerInvariant()}",
                overlay.OverlayType == OverlayType.MovingAvg ? MarkKind.Line : MarkKind.Rule,
                100 + index,
                [],
                [
                    new StyleToken("overlayType", overlay.OverlayType.ToString()),
                    new StyleToken("parameter", overlay.Parameter?.ToString(CultureInfo.InvariantCulture) ?? ""),
                    new StyleToken("lineStyle", overlay.LineStyle.ToString().ToLowerInvariant()),
                    new StyleToken("color", overlay.Color ?? "#888888"),
                    new StyleToken("label", overlay.Label ?? overlay.OverlayType.ToString())
                ],
                overlay.Label ?? overlay.OverlayType.ToString());
        }
    }

    private static IEnumerable<ScaleSpec> BuildScales(
        CreateVisualStatement statement,
        ImmutableArray<FieldBinding> bindings,
        VisualManifest manifest)
    {
        foreach (var group in bindings.Where(binding => binding.ScaleId is not null).GroupBy(binding => binding.ScaleId!))
        {
            var binding = group.First();
            var kind = binding.SemanticKind switch
            {
                DataSemanticKind.Temporal => ScaleKind.Time,
                DataSemanticKind.Quantitative => ScaleKind.Linear,
                DataSemanticKind.Ordinal => ScaleKind.Point,
                _ => binding.Channel is FieldChannel.Color ? ScaleKind.Ordinal : ScaleKind.Band
            };
            yield return new ScaleSpec(group.Key, binding.Channel, kind,
                IncludeZero: binding.Channel is FieldChannel.Y or FieldChannel.Y2 && statement.VisualType != VisualType.Scatter,
                CategoryOrder: [],
                DomainMinimum: ParseDomain(manifest, binding.Channel, "min"),
                DomainMaximum: ParseDomain(manifest, binding.Channel, "max"));
        }
    }

    private static InteractionSpec BuildInteractions(CreateVisualStatement statement)
    {
        var bindings = statement.Actions.Select(action => new InteractionBinding(
            action.Trigger,
            action switch
            {
                SetParameterAction => InteractionEffect.SetParameter,
                DrillDownAction or DrillInAction => InteractionEffect.Drill,
                DrillReportAction or NavigatePageAction => InteractionEffect.Navigate,
                _ => InteractionEffect.Highlight
            }))
            .Concat(statement.Interactions.Select(interaction => new InteractionBinding(
                interaction.Key,
                interaction.Value.Equals("FILTER", StringComparison.OrdinalIgnoreCase)
                    ? InteractionEffect.Filter
                    : InteractionEffect.Highlight)))
            .ToImmutableArray();
        return new InteractionSpec([], bindings);
    }

    private static ImmutableArray<StyleToken> BuildStyleTokens(VisualManifest manifest) =>
        (manifest.Styles ?? new Dictionary<string, string>())
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new StyleToken(pair.Key, pair.Value))
            .Concat(manifest.Options
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new StyleToken(pair.Key, pair.Value)))
            .GroupBy(token => token.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(token => token.Name, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

    private static FieldChannel? MapChannel(VisualType type, string role) => role.ToUpperInvariant() switch
    {
        "X" => FieldChannel.X,
        "Y" or "VALUE" when type is not VisualType.Pie and not VisualType.Donut => FieldChannel.Y,
        "Y2" => FieldChannel.Y2,
        "LABEL" or "CATEGORY" when type is VisualType.Pie or VisualType.Donut => FieldChannel.Theta,
        "VALUE" when type is VisualType.Pie or VisualType.Donut => FieldChannel.Radius,
        "SERIES" or "COLOR" => FieldChannel.Color,
        "SIZE" => FieldChannel.Size,
        "TOOLTIP" => FieldChannel.Tooltip,
        _ => null
    };

    private static DataSemanticKind InferSemanticKind(VisualType type, FieldChannel channel, VisualMapping mapping)
    {
        if (channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or FieldChannel.Size)
            return DataSemanticKind.Quantitative;
        if (type == VisualType.Scatter && channel == FieldChannel.X)
            return DataSemanticKind.Quantitative;
        if (mapping.Format?.Contains("date", StringComparison.OrdinalIgnoreCase) == true ||
            mapping.Column.Contains("date", StringComparison.OrdinalIgnoreCase) ||
            mapping.Column.Contains("time", StringComparison.OrdinalIgnoreCase))
            return DataSemanticKind.Temporal;
        return DataSemanticKind.Nominal;
    }

    private static string? ScaleId(FieldChannel channel) => channel switch
    {
        FieldChannel.X => "x",
        FieldChannel.Y => "y",
        FieldChannel.Y2 => "y2",
        FieldChannel.Color => "color",
        FieldChannel.Theta => "theta",
        FieldChannel.Radius => "radius",
        _ => null
    };

    private static SortDirection ParseSort(IReadOnlyList<VisualOption> options)
    {
        var value = options.FirstOrDefault(option => option.Key.Equals("AXIS_SORT", StringComparison.OrdinalIgnoreCase))?.Value.ToUpperInvariant();
        if (value is null) return SortDirection.None;
        return value switch
        {
            "DESC" or "VALUE_DESC" => SortDirection.Descending,
            "NONE" or "SOURCE" => SortDirection.None,
            _ => SortDirection.Ascending
        };
    }

    private static bool IsPolar(VisualType type) => type is VisualType.Pie or VisualType.Donut;
    private static ChartValue? ParseDomain(VisualManifest manifest, FieldChannel channel, string bound)
    {
        var axis = channel == FieldChannel.X ? "x" : "y";
        var text = manifest.Options.GetValueOrDefault($"axis:{axis}:{bound}");
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? ChartValue.From(value)
            : null;
    }
    private static decimal ResolveInnerRadius(VisualManifest manifest)
    {
        var text = manifest.Options.GetValueOrDefault("INNER_RADIUS")?.Trim().TrimEnd('%');
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value > 1m ? value / 100m : value, 0m, 0.9m)
            : 0.45m;
    }
    private static string Sanitize(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray());
}
