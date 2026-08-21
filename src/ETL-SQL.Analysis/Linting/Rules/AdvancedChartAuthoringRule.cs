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
            var scales = chart.Scales.Select(scale => scale.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var scale in chart.Scales)
            {
                if (scale.Kind == AdvancedChartScaleKind.Logarithmic && scale.IncludeZero)
                    Add(results, visual, $"Logarithmic scale '{scale.Name}' cannot use INCLUDE_ZERO=ON.");
                if (scale.ExplicitOrder.Length > 0 && scale.Kind is not (AdvancedChartScaleKind.Band or AdvancedChartScaleKind.Point or AdvancedChartScaleKind.Ordinal))
                    Add(results, visual, $"Scale '{scale.Name}' may declare an explicit ORDER list only for categorical scales.");
            }
            foreach (var layer in chart.Layers)
            {
                Duplicate(results, visual, layer.Encodings.Select(encoding => encoding.Channel.ToString()), $"encoding channel in layer '{layer.Name}'");
                Duplicate(results, visual, layer.Styles.Select(style => style.Name), $"style in layer '{layer.Name}'");
                foreach (var encoding in layer.Encodings)
                {
                    if (encoding.Scale is not null && !scales.Contains(encoding.Scale))
                        Add(results, visual, $"Layer '{layer.Name}' references undeclared scale '{encoding.Scale}'.");
                    else if (encoding.Scale is not null && chart.Scales.First(scale => scale.Name.Equals(encoding.Scale, StringComparison.OrdinalIgnoreCase)).Channel != encoding.Channel)
                        Add(results, visual, $"Layer '{layer.Name}' binds {encoding.Channel} to scale '{encoding.Scale}', which is declared for a different channel.");
                    if (encoding.Axis == AdvancedChartAxisRole.Secondary && encoding.Channel != AdvancedChartChannel.Y2)
                        Add(results, visual, $"Layer '{layer.Name}' may use AXIS=SECONDARY only on the Y2 channel.");
                }
                if (layer.Conditions.Length > 0 && layer.Mark is AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area)
                    Add(results, visual, $"Layer '{layer.Name}' cannot use row-level CONDITIONS on connected {layer.Mark.ToString().ToUpperInvariant()} marks; stage separate series or layers in ETL-SQL.");
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
            if (chart.Facet is not null && chart.Facet.RowField is null && chart.Facet.ColumnField is null)
                Add(results, visual, "FACET must declare ROW, COLUMN, or both.");
            if (chart.Facet is null && (chart.Resolution.X == AdvancedChartResolutionMode.Independent ||
                chart.Resolution.Y == AdvancedChartResolutionMode.Independent || chart.Resolution.Color == AdvancedChartResolutionMode.Independent))
                Add(results, visual, "Independent scale resolution requires FACET.");
        }
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

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
