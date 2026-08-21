using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>Builds the one ordered, non-graphical interpretation used by every report surface.</summary>
public static class VisualSemanticFallbackBuilder
{
    public static SemanticFallback Build(VisualManifest visual)
    {
        if (visual.PlotPlan is not null)
            return visual.PlotPlan.Fallback with { Summary = visual.PlotPlan.AccessibleSummary };

        var type = visual.VisualType.ToUpperInvariant();
        return type switch
        {
            "MAP" => RankedRegions(visual),
            "SANKEY" => Transitions(visual),
            "TREEMAP" or "SUNBURST" => Hierarchy(visual),
            "NETWORK" => Network(visual),
            "PIE" or "DONUT" or "FUNNEL" => Proportions(visual),
            _ => Tabular(visual)
        };
    }

    private static SemanticFallback RankedRegions(VisualManifest visual)
    {
        var items = Rows(visual).OrderByDescending(item => item.Number).ThenBy(item => item.Index)
            .Select((item, order) => new SemanticFallbackItem(item.Label, item.Value, order)
            { Detail = "ranked region" }).ToImmutableArray();
        return Create(SemanticFallbackKind.RankedTable, visual, items, $"{items.Length} regions ranked by value.");
    }

    private static SemanticFallback Proportions(VisualManifest visual)
    {
        var rows = Rows(visual).ToList();
        var total = rows.Where(item => item.Number.HasValue).Sum(item => Math.Max(0m, item.Number!.Value));
        var items = rows.Select((item, order) => new SemanticFallbackItem(item.Label, item.Value, order)
        {
            Detail = item.Number.HasValue && total > 0m
                ? $"{item.Number.Value / total:P1} of total"
                : null
        }).ToImmutableArray();
        return Create(SemanticFallbackKind.ProportionalBreakdown, visual, items, $"{items.Length} proportional components.");
    }

    private static SemanticFallback Hierarchy(VisualManifest visual)
    {
        var items = Rows(visual).Select((item, order) =>
        {
            var path = item.Label.Split(new[] { '>', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new SemanticFallbackItem(string.Join(" > ", path), item.Value, order)
            {
                Group = path.Length > 1 ? string.Join(" > ", path.Take(path.Length - 1)) : null,
                Level = Math.Max(0, path.Length - 1),
                Detail = "proportional hierarchy node"
            };
        }).ToImmutableArray();
        return Create(SemanticFallbackKind.Hierarchy, visual, items, $"{items.Length} hierarchy nodes in source order.");
    }

    private static SemanticFallback Transitions(VisualManifest visual)
    {
        var transitions = visual.Rows.Select((row, index) =>
        {
            var source = Cell(row, 0, "Source");
            var target = Cell(row, 1, "Target");
            var value = Cell(row, 2, "0");
            return new { Source = source, Target = target, Value = value, Number = Parse(value), Index = index };
        }).ToList();
        var incoming = transitions.GroupBy(item => item.Target, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Number ?? 0m), StringComparer.OrdinalIgnoreCase);
        var outgoing = transitions.GroupBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Number ?? 0m), StringComparer.OrdinalIgnoreCase);
        var items = transitions.Select((item, order) =>
        {
            incoming.TryGetValue(item.Source, out var sourceInput);
            outgoing.TryGetValue(item.Source, out var sourceOutput);
            var drop = Math.Max(0m, sourceInput - sourceOutput);
            return new SemanticFallbackItem($"{item.Source} -> {item.Target}", item.Value, order)
            {
                Group = item.Source,
                Detail = sourceInput > 0m ? $"source drop-off {drop.ToString(CultureInfo.InvariantCulture)}" : "transition"
            };
        }).ToImmutableArray();
        return Create(SemanticFallbackKind.TransitionTable, visual, items, $"{items.Length} directed transitions with source drop-off context.");
    }

    private static SemanticFallback Network(VisualManifest visual)
    {
        var edges = visual.Rows.Select((row, index) => new
        {
            Source = Cell(row, 0, "Source"),
            Target = Cell(row, 1, "Target"),
            Weight = Cell(row, 2, "1"),
            Index = index
        }).ToList();
        var degrees = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            degrees[edge.Source] = degrees.GetValueOrDefault(edge.Source) + 1;
            degrees[edge.Target] = degrees.GetValueOrDefault(edge.Target) + 1;
        }
        var connections = edges.GroupBy(edge => edge.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(edge => edge.Target)), StringComparer.OrdinalIgnoreCase);
        var items = degrees.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select((pair, order) => new SemanticFallbackItem(pair.Key, $"degree {pair.Value}", order)
            {
                Detail = connections.TryGetValue(pair.Key, out var targets) ? $"connects to {targets}" : "incoming connections only"
            }).ToImmutableArray();
        return Create(SemanticFallbackKind.NetworkConnections, visual, items, $"{degrees.Count} nodes and {edges.Count} connections.");
    }

    private static SemanticFallback Tabular(VisualManifest visual)
    {
        var items = Rows(visual).Select((item, order) => new SemanticFallbackItem(item.Label, item.Value, order)).ToImmutableArray();
        return Create(SemanticFallbackKind.Summary, visual, items, $"{visual.Rows.Count} rows in source order.");
    }

    private static SemanticFallback Create(SemanticFallbackKind kind, VisualManifest visual,
        ImmutableArray<SemanticFallbackItem> items, string summary) =>
        new(kind, Title(visual), items) { Summary = $"{Title(visual)}: {summary}" };

    private static IEnumerable<(string Label, string Value, decimal? Number, int Index)> Rows(VisualManifest visual) =>
        visual.Rows.Select((row, index) =>
        {
            var label = Cell(row, 0, $"Row {index + 1}");
            var value = Cell(row, 1, row.Count > 0 ? Cell(row, 0, string.Empty) : string.Empty);
            return (label, value, Parse(value), index);
        });

    private static string Cell(IReadOnlyList<string?> row, int index, string fallback) =>
        index < row.Count && !string.IsNullOrWhiteSpace(row[index]) ? row[index]! : fallback;

    private static decimal? Parse(string value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string Title(VisualManifest visual) =>
        visual.Options.GetValueOrDefault("title") ?? visual.Options.GetValueOrDefault("TITLE") ?? visual.Name;
}
