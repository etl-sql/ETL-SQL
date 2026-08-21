using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>Resolves all shared semantic decisions once, before any backend renders.</summary>
public sealed class PlotPlanResolver
{
    private static readonly string[] PaletteColors =
    ["#5470c6", "#91cc75", "#fac858", "#ee6666", "#73c0de", "#3ba272", "#fc8452"];

    public PlotPlan Resolve(ChartSpec spec, ChartDataSet data, PlotBounds? bounds = null)
    {
        spec.Validate();
        data.Validate();
        var columns = data.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var categories = ResolveCategories(spec, columns);
        var seriesKeys = ResolveSeries(spec, columns, categories);
        var series = seriesKeys.Select((key, index) => new ResolvedSeries(key, key, index, ResolveColor(spec, key, index))).ToImmutableArray();
        var palette = series.Select(item => new PaletteAssignment(item.Key, item.Color)).ToImmutableArray();
        var legend = series.Select(item => new LegendEntry(item.Key, item.Label, item.Order, item.Color)).ToImmutableArray();
        var layers = ResolveLayers(spec, data, columns, categories, series).ToImmutableArray();
        var scales = ResolveScales(spec, columns, categories).ToImmutableArray();
        var sourceLayers = layers.Where(layer => layer.Style.IsDefault || !layer.Style.Any(token => token.Name == "overlayType"));
        var gapRows = sourceLayers.SelectMany(layer => layer.Data).Where(datum => datum.IsGap).Select(datum => datum.RowIndex).Distinct().Order().ToImmutableArray();
        var usedRows = sourceLayers.SelectMany(layer => layer.Data).Where(datum => !datum.IsGap).Select(datum => datum.RowIndex).ToHashSet();
        var skippedRows = Enumerable.Range(0, data.RowCount).Where(index => !usedRows.Contains(index) && !gapRows.Contains(index)).ToImmutableArray();
        var fallback = BuildFallback(spec, layers, categories);
        var summary = BuildSummary(spec, data, series, gapRows, skippedRows);

        var plan = PlotPlan.Create(
            spec.Id,
            bounds ?? new PlotBounds(0, 0, 600, 350),
            scales,
            series,
            palette,
            legend,
            layers,
            new ResolvedNullPolicy(spec.NullHandling.Default, spec.NullHandling.Fields, gapRows, skippedRows),
            summary,
            fallback,
            spec.Title,
            spec.Coordinate,
            spec.Theme.Tokens);
        plan.Validate();
        return plan;
    }

    private static ImmutableArray<string> ResolveCategories(
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns)
    {
        var binding = spec.Bindings.FirstOrDefault(item => item.Channel is FieldChannel.X or FieldChannel.Theta);
        if (binding is null || !columns.TryGetValue(binding.Field, out var column)) return [];
        var categories = column.Values.Select(ValueKey).Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToList();
        var axisSort = spec.Theme.Tokens.FirstOrDefault(token => token.Name.Equals("AXIS_SORT", StringComparison.OrdinalIgnoreCase))?.Value.ToUpperInvariant();
        if (axisSort is "VALUE" or "VALUE_DESC")
        {
            var measureBinding = spec.Bindings.FirstOrDefault(item => item.Channel is FieldChannel.Y or FieldChannel.Radius);
            if (measureBinding is not null && columns.TryGetValue(measureBinding.Field, out var measure))
            {
                var sums = categories.ToDictionary(category => category, _ => 0m, StringComparer.Ordinal);
                for (var index = 0; index < column.Values.Length; index++)
                {
                    var category = ValueKey(column.Values[index]);
                    if (category is not null) sums[category] += Number(measure.Values[index]) ?? 0m;
                }
                categories = (axisSort == "VALUE_DESC"
                    ? categories.OrderByDescending(category => sums[category]).ThenBy(category => category, StringComparer.Ordinal)
                    : categories.OrderBy(category => sums[category]).ThenBy(category => category, StringComparer.Ordinal)).ToList();
            }
        }
        else if (binding.Sort == SortDirection.Descending)
        {
            categories.Sort((left, right) => StringComparer.Ordinal.Compare(right, left));
        }
        else if (binding.Sort == SortDirection.Ascending)
        {
            categories.Sort(StringComparer.Ordinal);
        }
        return categories.ToImmutableArray();
    }

    private static ImmutableArray<string> ResolveSeries(
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories)
    {
        if (spec.Coordinate.Kind == CoordinateKind.Polar) return categories;
        var color = spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color);
        if (color is not null && columns.TryGetValue(color.Field, out var colorColumn))
            return colorColumn.Values.Select(ValueKey).Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToImmutableArray();

        var namedLayers = spec.Layers.Where(layer => layer.Mark is not MarkKind.Rule &&
            layer.Style.Any(token => token.Name == "series")).Select(layer => layer.Style.First(token => token.Name == "series").Value).ToImmutableArray();
        if (!namedLayers.IsDefaultOrEmpty) return namedLayers;
        var measure = spec.Bindings.FirstOrDefault(binding => binding.Channel is FieldChannel.Y or FieldChannel.Radius);
        return [measure?.Field ?? spec.Id];
    }

    private static IEnumerable<ResolvedScale> ResolveScales(
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories)
    {
        foreach (var scale in spec.Scales)
        {
            var bindings = spec.Bindings.Concat(spec.Layers.SelectMany(layer => layer.Bindings))
                .Where(binding => binding.ScaleId == scale.Id && columns.ContainsKey(binding.Field)).ToList();
            if (scale.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal)
            {
                var values = scale.CategoryOrder.IsDefaultOrEmpty ? categories : scale.CategoryOrder;
                yield return new ResolvedScale(scale.Id, scale.Channel, scale.Kind,
                    values.Select(ChartValue.From).ToImmutableArray(), values,
                    values.Select(value => new PlotTick(ChartValue.From(value), value)).ToImmutableArray(), scale.IncludeZero);
                continue;
            }

            var raw = bindings.SelectMany(binding => columns[binding.Field].Values).Where(value => value.Kind != ChartValueKind.Null).ToList();
            if (scale.Kind == ScaleKind.Time)
            {
                var temporalValues = bindings.SelectMany(binding =>
                {
                    var column = columns[binding.Field];
                    return column.Values.Select((value, index) => new
                    {
                        Value = value,
                        Display = column.DisplayValues.IsDefaultOrEmpty ? Display(value) : column.DisplayValues[index] ?? Display(value)
                    });
                })
                    .Where(item => item.Value.Kind != ChartValueKind.Null)
                    .GroupBy(item => ValueKey(item.Value), StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(item => ValueKey(item.Value), StringComparer.Ordinal)
                    .ToImmutableArray();
                yield return new ResolvedScale(scale.Id, scale.Channel, scale.Kind,
                    temporalValues.Select(item => item.Value).ToImmutableArray(),
                    temporalValues.Select(item => item.Display).ToImmutableArray(),
                    temporalValues.Select(item => new PlotTick(item.Value, item.Display)).ToImmutableArray(), scale.IncludeZero);
                continue;
            }

            var numbers = raw.Select(Number).Where(number => number.HasValue).Select(number => number!.Value).ToList();
            if (scale.Channel == FieldChannel.Y && IsEnabled(spec.Theme.Tokens, "STACKED"))
            {
                var xBinding = spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.X);
                var yBinding = bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Y);
                if (xBinding is not null && yBinding is not null && columns.TryGetValue(xBinding.Field, out var xColumn))
                {
                    numbers = Enumerable.Range(0, xColumn.Values.Length)
                        .GroupBy(index => ValueKey(xColumn.Values[index]) ?? string.Empty, StringComparer.Ordinal)
                        .SelectMany(group =>
                        {
                            var values = group.Select(index => Number(columns[yBinding.Field].Values[index]) ?? 0m);
                            return new[] { values.Where(value => value < 0m).Sum(), values.Where(value => value > 0m).Sum() };
                        })
                        .ToList();
                }
            }
            var minimum = scale.DomainMinimum is not null ? Number(scale.DomainMinimum) ?? 0m : numbers.DefaultIfEmpty(0m).Min();
            var maximum = scale.DomainMaximum is not null ? Number(scale.DomainMaximum) ?? 1m : numbers.DefaultIfEmpty(1m).Max();
            if (scale.IncludeZero) { minimum = Math.Min(0m, minimum); maximum = Math.Max(0m, maximum); }
            if (minimum == maximum) maximum = minimum + 1m;
            var ticks = Enumerable.Range(0, 5).Select(index => minimum + ((maximum - minimum) * index / 4m)).ToList();
            yield return new ResolvedScale(scale.Id, scale.Channel, scale.Kind,
                [ChartValue.From(minimum), ChartValue.From(maximum)], [],
                ticks.Select(value => new PlotTick(ChartValue.From(value), value.ToString("0.##", CultureInfo.InvariantCulture))).ToImmutableArray(),
                scale.IncludeZero);
        }
    }

    private static IEnumerable<ResolvedMarkLayer> ResolveLayers(
        ChartSpec spec,
        ChartDataSet data,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories,
        ImmutableArray<ResolvedSeries> series)
    {
        foreach (var layer in spec.Layers.OrderBy(item => item.ZIndex).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var overlayType = layer.Style.FirstOrDefault(token => token.Name == "overlayType")?.Value;
            if (overlayType is not null)
            {
                yield return ResolveOverlay(layer, spec, columns, categories);
                continue;
            }

            var colorBinding = layer.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color)
                ?? spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color);
            var explicitSeries = layer.Style.FirstOrDefault(token => token.Name == "series")?.Value;
            if (colorBinding is not null && columns.TryGetValue(colorBinding.Field, out var colorColumn))
            {
                foreach (var resolvedSeries in series)
                {
                    var dataPoints = ResolveLayerData(layer, spec, data, columns, categories,
                        rowIndex => ValueKey(colorColumn.Values[rowIndex]) == resolvedSeries.Key);
                    yield return new ResolvedMarkLayer($"{layer.Id}-{resolvedSeries.Order:D2}", layer.Mark, layer.ZIndex + resolvedSeries.Order,
                        resolvedSeries.Key, dataPoints)
                    { Style = layer.Style };
                }
            }
            else
            {
                var seriesKey = explicitSeries ?? series.FirstOrDefault()?.Key;
                yield return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, seriesKey,
                    ResolveLayerData(layer, spec, data, columns, categories, _ => true))
                { Style = layer.Style };
            }
        }
    }

    private static ImmutableArray<ResolvedDatum> ResolveLayerData(
        MarkLayerSpec layer,
        ChartSpec spec,
        ChartDataSet data,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories,
        Func<int, bool> include)
    {
        var layerBindings = layer.Bindings.IsDefaultOrEmpty ? spec.Bindings : layer.Bindings;
        var categoryBinding = layerBindings.FirstOrDefault(binding => binding.Channel is FieldChannel.X or FieldChannel.Theta);
        var rows = new List<ResolvedDatum>();
        if (categoryBinding is not null && categoryBinding.SemanticKind != DataSemanticKind.Quantitative &&
            columns.TryGetValue(categoryBinding.Field, out var categoryColumn) && !categories.IsDefaultOrEmpty)
        {
            for (var categoryIndex = 0; categoryIndex < categories.Length; categoryIndex++)
            {
                var category = categories[categoryIndex];
                var rowIndex = Enumerable.Range(0, data.RowCount).FirstOrDefault(index => include(index) && ValueKey(categoryColumn.Values[index]) == category, -1);
                rows.Add(rowIndex < 0
                    ? GapDatum(categoryIndex, layerBindings, categoryBinding, category)
                    : Datum(rowIndex, layerBindings, columns, spec.NullHandling));
            }
            return rows.ToImmutableArray();
        }

        for (var rowIndex = 0; rowIndex < data.RowCount; rowIndex++)
            if (include(rowIndex)) rows.Add(Datum(rowIndex, layerBindings, columns, spec.NullHandling));
        return rows.ToImmutableArray();
    }

    private static ResolvedDatum Datum(
        int rowIndex,
        ImmutableArray<FieldBinding> bindings,
        IReadOnlyDictionary<string, ChartColumn> columns,
        NullHandlingSpec nulls)
    {
        var channels = bindings.Where(binding => columns.ContainsKey(binding.Field)).Select(binding =>
        {
            var column = columns[binding.Field];
            return new ResolvedChannelValue(binding.Channel, column.Values[rowIndex],
                column.DisplayValues.IsDefaultOrEmpty ? null : column.DisplayValues[rowIndex]);
        }).ToImmutableArray();
        var requiredNull = channels.Any(channel => channel.Channel is FieldChannel.X or FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius
            && channel.Value.Kind == ChartValueKind.Null);
        return new ResolvedDatum(rowIndex, channels, requiredNull && nulls.Default == NullValuePolicy.Gap,
            string.Join(", ", channels.Select(channel => $"{channel.Channel}: {channel.DisplayValue ?? Display(channel.Value)}")));
    }

    private static ResolvedDatum GapDatum(int index, ImmutableArray<FieldBinding> bindings, FieldBinding categoryBinding, string category)
    {
        var channels = bindings.Select(binding => new ResolvedChannelValue(binding.Channel,
            binding == categoryBinding ? ChartValue.From(category) : ChartValue.Null(),
            binding == categoryBinding ? category : null)).ToImmutableArray();
        return new ResolvedDatum(index, channels, true, null);
    }

    private static ResolvedMarkLayer ResolveOverlay(
        MarkLayerSpec layer,
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories)
    {
        var type = layer.Style.First(token => token.Name == "overlayType").Value;
        var parameterText = layer.Style.FirstOrDefault(token => token.Name == "parameter")?.Value;
        decimal.TryParse(parameterText, NumberStyles.Any, CultureInfo.InvariantCulture, out var parameter);
        var yBinding = spec.Bindings.First(binding => binding.Channel is FieldChannel.Y or FieldChannel.Y2);
        var yValues = columns[yBinding.Field].Values.Select(Number).Select(value => value ?? 0m).ToList();
        var resolvedValues = type switch
        {
            "Goal" => Enumerable.Repeat(parameter, Math.Max(1, categories.Length)).Select(value => (decimal?)value).ToList(),
            "Average" => Enumerable.Repeat(yValues.DefaultIfEmpty(0m).Average(), Math.Max(1, categories.Length)).Select(value => (decimal?)value).ToList(),
            "MovingAvg" => MovingAverage(yValues, Math.Max(1, (int)(parameter == 0 ? 3 : parameter))),
            _ => Enumerable.Repeat((decimal?)0m, Math.Max(1, categories.Length)).ToList()
        };
        var data = resolvedValues.Select((value, index) => new ResolvedDatum(index,
            [
                new ResolvedChannelValue(FieldChannel.X, ChartValue.From(categories.IsDefaultOrEmpty ? index.ToString(CultureInfo.InvariantCulture) : categories[index]), categories.IsDefaultOrEmpty ? null : categories[index]),
                new ResolvedChannelValue(FieldChannel.Y, value.HasValue ? ChartValue.From(value.Value) : ChartValue.Null(), value?.ToString(CultureInfo.InvariantCulture))
            ], !value.HasValue, null)).ToImmutableArray();
        return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, null, data) { Style = layer.Style };
    }

    private static List<decimal?> MovingAverage(IReadOnlyList<decimal> values, int window) =>
        values.Select((_, index) => index < window - 1 ? (decimal?)null : values.Skip(index - window + 1).Take(window).Average()).ToList();

    private static SemanticFallback BuildFallback(ChartSpec spec, ImmutableArray<ResolvedMarkLayer> layers, ImmutableArray<string> categories)
    {
        var primary = layers.FirstOrDefault(layer => layer.Mark is not MarkKind.Rule);
        var items = primary?.Data.Select((datum, index) =>
        {
            var label = datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.X or FieldChannel.Theta)?.DisplayValue
                ?? (index < categories.Length ? categories[index] : $"Row {index + 1}");
            var value = datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius);
            return new SemanticFallbackItem(label ?? $"Row {index + 1}", value is null ? "" : value.DisplayValue ?? Display(value.Value), index);
        }).ToImmutableArray() ?? [];
        return new SemanticFallback(
            spec.Coordinate.Kind == CoordinateKind.Polar ? SemanticFallbackKind.ProportionalBreakdown :
            spec.Bindings.Any(binding => binding.SemanticKind == DataSemanticKind.Temporal) ? SemanticFallbackKind.TimeSeriesTable : SemanticFallbackKind.RankedTable,
            spec.Title ?? spec.Id,
            items);
    }

    private static string BuildSummary(ChartSpec spec, ChartDataSet data, ImmutableArray<ResolvedSeries> series, ImmutableArray<int> gaps, ImmutableArray<int> skipped) =>
        $"{spec.Title ?? spec.Id}: {data.RowCount} rows, {series.Length} series, {gaps.Length} gaps, {skipped.Length} skipped rows.";

    private static string ResolveColor(ChartSpec spec, string key, int index) =>
        spec.Theme.Tokens.FirstOrDefault(token => token.Name.Equals($"COLOR:{key}", StringComparison.OrdinalIgnoreCase))?.Value
        ?? PaletteColors[index % PaletteColors.Length];

    private static bool IsEnabled(ImmutableArray<StyleToken> tokens, string name)
    {
        var value = tokens.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        return value is not null && !value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0";
    }

    internal static decimal? Number(ChartValue value) => value.Kind switch
    {
        ChartValueKind.Integer => value.Integer,
        ChartValueKind.FloatingPoint => (decimal?)value.FloatingPoint,
        ChartValueKind.Decimal => value.Decimal,
        _ => null
    };

    internal static string Display(ChartValue value) => value.Kind switch
    {
        ChartValueKind.Null => "",
        ChartValueKind.Integer => value.Integer?.ToString(CultureInfo.InvariantCulture) ?? "",
        ChartValueKind.FloatingPoint => value.FloatingPoint?.ToString("G", CultureInfo.InvariantCulture) ?? "",
        ChartValueKind.Decimal => value.Decimal?.ToString(CultureInfo.InvariantCulture) ?? "",
        ChartValueKind.Text => value.Text ?? "",
        ChartValueKind.Date => value.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        ChartValueKind.Time => value.Time?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
        ChartValueKind.LocalDateTime => value.LocalDateTime?.ToString("O", CultureInfo.InvariantCulture) ?? "",
        ChartValueKind.OffsetDateTime => value.OffsetDateTime?.ToString("O", CultureInfo.InvariantCulture) ?? "",
        ChartValueKind.Boolean => value.Boolean == true ? "true" : "false",
        _ => ""
    };

    private static string? ValueKey(ChartValue value) => value.Kind == ChartValueKind.Null ? null : Display(value);
}
