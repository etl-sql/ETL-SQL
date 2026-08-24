using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>Validates renderer-neutral native advanced chart declarations.</summary>
public sealed class AdvancedChartAuthoringRule : ILintRule
{
    public string Name => "AdvancedChartAuthoring";
    public string Description => "Validates CUSTOM CHART layers, scales, coordinates, conditions, and facets.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        foreach (var visual in script.Statements.OfType<CreateVisualStatement>().Where(visual => visual.AdvancedChart is not null))
        {
            var chart = visual.AdvancedChart!;
            Duplicate(results, visual, chart.Layers.Select(layer => layer.Name), "layer");
            Duplicate(results, visual, chart.Scales.Select(scale => scale.Name), "scale");
            Duplicate(results, visual, chart.Encodings.Select(encoding => encoding.Channel.ToString()), "global encoding channel");
            var scales = chart.Scales.Select(scale => scale.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var scale in chart.Scales)
            {
                if (scale.Kind == AdvancedChartScaleKind.Logarithmic && scale.IncludeZero)
                    Add(results, visual, $"Logarithmic scale '{scale.Name}' cannot use INCLUDE_ZERO=ON.");
                if (scale.ExplicitOrder.Length > 0 && scale.Kind is not (AdvancedChartScaleKind.Band or AdvancedChartScaleKind.Point or AdvancedChartScaleKind.Ordinal))
                    Add(results, visual, $"Scale '{scale.Name}' may declare an explicit ORDER list only for categorical scales.");
                if (scale.ColorRange is not null && (scale.Channel != AdvancedChartChannel.Color ||
                    scale.Kind is not (AdvancedChartScaleKind.Linear or AdvancedChartScaleKind.Logarithmic)))
                    Add(results, visual, $"Scale '{scale.Name}' RANGE requires a quantitative COLOR linear/logarithmic scale.");
            }
            foreach (var layer in chart.Layers)
            {
                Duplicate(results, visual, layer.Encodings.Select(encoding => encoding.Channel.ToString()), $"encoding channel in layer '{layer.Name}'");
                Duplicate(results, visual, layer.Styles.Select(style => style.Name), $"style in layer '{layer.Name}'");
                var effective = layer.InheritEncodings
                    ? chart.Encodings.Where(inherited => layer.Encodings.All(local => local.Channel != inherited.Channel)).Concat(layer.Encodings)
                    : layer.Encodings;
                if (!effective.Any())
                    Add(results, visual, $"Layer '{layer.Name}' has no effective encodings.");
                foreach (var encoding in effective)
                {
                    if (encoding.Scale is not null && !scales.Contains(encoding.Scale))
                        Add(results, visual, $"Layer '{layer.Name}' references undeclared scale '{encoding.Scale}'.");
                    else if (encoding.Scale is not null && !CompatibleScaleChannel(
                        chart.Scales.First(scale => scale.Name.Equals(encoding.Scale, StringComparison.OrdinalIgnoreCase)).Channel, encoding.Channel))
                        Add(results, visual, $"Layer '{layer.Name}' binds {encoding.Channel} to scale '{encoding.Scale}', which is declared for a different channel.");
                    if (encoding.Axis == AdvancedChartAxisRole.Secondary && encoding.Channel != AdvancedChartChannel.Y2)
                        Add(results, visual, $"Layer '{layer.Name}' may use AXIS=SECONDARY only on the Y2 channel.");
                    if (encoding.Source.Kind == AdvancedChartBindingSourceKind.Value && encoding.Scale is not null)
                        Add(results, visual, $"Layer '{layer.Name}' uses VALUE on {encoding.Channel}; VALUE bypasses scales and cannot declare SCALE.");
                    if (encoding.Source.Kind == AdvancedChartBindingSourceKind.Value && encoding.Axis != AdvancedChartAxisRole.None)
                        Add(results, visual, $"Layer '{layer.Name}' uses VALUE on {encoding.Channel}; VALUE cannot declare AXIS.");
                    if (encoding.Source.Kind == AdvancedChartBindingSourceKind.Value && encoding.Channel is
                        AdvancedChartChannel.X or AdvancedChartChannel.Y or AdvancedChartChannel.Y2)
                        Add(results, visual, $"Layer '{layer.Name}' cannot bind visual-range VALUE to positional channel {encoding.Channel}.");
                    if (encoding.Stack != AdvancedChartStackMode.None &&
                        (encoding.DataKind != AdvancedChartDataKind.Quantitative || encoding.Channel is not (AdvancedChartChannel.Y or AdvancedChartChannel.Y2) ||
                         chart.Coordinate.Kind == AdvancedChartCoordinateKind.Polar))
                        Add(results, visual, $"Layer '{layer.Name}' STACK requires a quantitative Cartesian/transposed Y or Y2 binding; polar/radial stacking is not yet portable.");
                    if (encoding.Scale is null && encoding.Source.Kind != AdvancedChartBindingSourceKind.Value &&
                        AdvancedChartScaleInference.Infer(encoding.Channel, encoding.DataKind, layer.Mark) is null &&
                        encoding.Channel is not (AdvancedChartChannel.Text or AdvancedChartChannel.Tooltip or AdvancedChartChannel.Detail))
                        Add(results, visual, $"Layer '{layer.Name}' has no deterministic scale inference for {layer.Mark} {encoding.Channel} {encoding.DataKind}; declare a compatible scale or encoding.");
                }
                if (layer.Conditions.Length > 0 && layer.Mark is AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area)
                    Add(results, visual, $"Layer '{layer.Name}' cannot use row-level CONDITIONS on connected {layer.Mark.ToString().ToUpperInvariant()} marks; stage separate series or layers in ETL-SQL.");
                if (layer.Mark == AdvancedChartMarkKind.Tick)
                {
                    var x = effective.FirstOrDefault(encoding => encoding.Channel == AdvancedChartChannel.X);
                    var y = effective.FirstOrDefault(encoding => encoding.Channel == AdvancedChartChannel.Y);
                    if (x?.DataKind is not (AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal) ||
                        y?.DataKind != AdvancedChartDataKind.Quantitative)
                        Add(results, visual, $"TICK layer '{layer.Name}' requires a nominal/ordinal X encoding and a quantitative Y encoding.");
                }
            }
            if (chart.Coordinate.Kind == AdvancedChartCoordinateKind.Polar &&
                !chart.Layers.Any(layer => layer.Encodings.Any(encoding => encoding.Channel == AdvancedChartChannel.Theta)) ||
                chart.Coordinate.Kind == AdvancedChartCoordinateKind.Polar &&
                !chart.Layers.Any(layer => layer.Encodings.Any(encoding => encoding.Channel == AdvancedChartChannel.Radius)))
                Add(results, visual, "POLAR charts require THETA and RADIUS encodings.");
            if (chart.Coordinate.Kind == AdvancedChartCoordinateKind.Polar && chart.Layers.Any(layer => layer.Mark != AdvancedChartMarkKind.Arc))
                Add(results, visual, "The native POLAR slice supports ARC layers only.");
            if (chart.Coordinate.Kind != AdvancedChartCoordinateKind.Polar && chart.Layers.Any(layer => layer.Mark == AdvancedChartMarkKind.Arc))
                Add(results, visual, "ARC layers require POLAR coordinates.");
            if (chart.Facet is not null && chart.Facet.RowField is null && chart.Facet.ColumnField is null && chart.Facet.WrapField is null)
                Add(results, visual, "FACET must declare ROW, COLUMN, or WRAP.");
            if (chart.Facet?.WrapField is not null && (chart.Facet.RowField is not null || chart.Facet.ColumnField is not null))
                Add(results, visual, "FACET WRAP is mutually exclusive with ROW and COLUMN.");
            if (chart.Facet?.Columns is not null && chart.Facet.WrapField is null)
                Add(results, visual, "FACET COLUMNS requires WRAP.");
            if (chart.Facet is null && (chart.Resolution.X == AdvancedChartResolutionMode.Independent ||
                chart.Resolution.Y == AdvancedChartResolutionMode.Independent || chart.Resolution.Color == AdvancedChartResolutionMode.Independent))
                Add(results, visual, "Independent scale resolution requires FACET.");
        }
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private static bool CompatibleScaleChannel(AdvancedChartChannel scale, AdvancedChartChannel binding) => scale == binding ||
        scale == AdvancedChartChannel.X && binding is AdvancedChartChannel.X2 or AdvancedChartChannel.XStart or AdvancedChartChannel.XEnd ||
        scale == AdvancedChartChannel.Y && binding is AdvancedChartChannel.Y2 or AdvancedChartChannel.YStart or AdvancedChartChannel.YEnd;

    private static void Duplicate(List<LintResult> results, CreateVisualStatement visual, IEnumerable<string> values, string kind)
    {
        var duplicate = values.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) Add(results, visual, $"Duplicate {kind} '{duplicate.Key}'.");
    }

    private static void Add(List<LintResult> results, CreateVisualStatement visual, string message) => results.Add(new LintResult
    {
        RuleName = "AdvancedChartAuthoring",
        Code = "RPT-CHART",
        Severity = LintSeverity.Error,
        Message = message,
        LineNumber = visual.Line,
        ColumnNumber = visual.Column
    });
}
