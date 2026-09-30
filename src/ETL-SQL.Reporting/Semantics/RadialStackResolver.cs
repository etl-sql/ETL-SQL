using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace ETL_SQL.Reporting.Semantics.Runtime;

internal static class RadialStackResolver
{
    internal static ImmutableArray<ResolvedMarkLayer> Resolve(ChartSpec spec, ImmutableArray<ResolvedMarkLayer> layers)
    {
        var categories = layers.SelectMany(layer => layer.Data).OrderBy(datum => datum.RowIndex)
            .Where(datum => Theta(datum) is not null).Select(datum => Theta(datum)!).Distinct(StringComparer.Ordinal).ToArray();
        var theta = spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Theta);
        var thetaScale = spec.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Theta);
        if (thetaScale is { CategoryOrder.Length: > 0 })
            categories = thetaScale.CategoryOrder.Where(category => categories.Contains(category, StringComparer.Ordinal))
                .Concat(categories.Where(category => !thetaScale.CategoryOrder.Contains(category, StringComparer.Ordinal))).ToArray();
        else if (theta?.Sort == SortDirection.Ascending) categories = categories.OrderBy(category => category, StringComparer.Ordinal).ToArray();
        else if (theta?.Sort == SortDirection.Descending) categories = categories.OrderByDescending(category => category, StringComparer.Ordinal).ToArray();
        if (thetaScale?.Reverse == true) Array.Reverse(categories);
        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var datum in layers.SelectMany(layer => layer.Data))
        {
            var value = Radius(datum);
            if (datum.IsGap || Theta(datum) is not { } key || value is null) continue;
            if (value < 0m) throw new InvalidOperationException("Radial STACK requires non-negative RADIUS values.");
            totals[key] = totals.GetValueOrDefault(key) + value.Value;
        }
        var normalize = layers[0].Stack == StackMode.Normalize;
        var maximum = normalize ? 1m : totals.Values.DefaultIfEmpty(0m).Max();
        if (maximum <= 0m) maximum = 1m;
        var cumulative = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var startAngle = spec.Coordinate.StartAngle ?? 0m;
        var sweep = (spec.Coordinate.EndAngle ?? startAngle + 360m) - startAngle;
        return layers.Select(layer => layer with
        {
            Data = layer.Data.Select(datum =>
        {
            var value = Radius(datum);
            if (datum.IsGap || Theta(datum) is not { } key || value is null) return datum with { IsGap = true };
            var rawStart = cumulative.GetValueOrDefault(key);
            var rawEnd = rawStart + value.Value;
            cumulative[key] = rawEnd;
            var start = normalize ? totals[key] == 0m ? 0m : rawStart / totals[key] : rawStart;
            var end = normalize ? totals[key] == 0m ? 0m : rawEnd / totals[key] : rawEnd;
            var index = Array.IndexOf(categories, key);
            return datum with
            {
                RadialInterval = new ResolvedRadialInterval(startAngle + sweep * index / categories.Length,
                startAngle + sweep * (index + 1) / categories.Length, start, end, maximum)
            };
        }).ToImmutableArray()
        }).ToImmutableArray();
    }

    internal static string? Theta(ResolvedDatum datum)
    {
        var channel = datum.Channels.FirstOrDefault(channel => channel.Channel == FieldChannel.Theta);
        return channel is null || channel.Value.Kind == ChartValueKind.Null ? null : channel.DisplayValue ?? PlotPlanResolver.Display(channel.Value);
    }
    internal static decimal? Radius(ResolvedDatum datum) => PlotPlanResolver.Number(datum.Channels.FirstOrDefault(channel => channel.Channel == FieldChannel.Radius)?.Value ?? ChartValue.Null());
    internal static string Description(ResolvedDatum datum) => datum.RadialInterval is { } interval
        ? $"{Theta(datum)}: {Radius(datum)?.ToString(CultureInfo.InvariantCulture)}; radius {interval.Start.ToString(CultureInfo.InvariantCulture)} to {interval.End.ToString(CultureInfo.InvariantCulture)}"
        : $"{Theta(datum)}: gap";
}
