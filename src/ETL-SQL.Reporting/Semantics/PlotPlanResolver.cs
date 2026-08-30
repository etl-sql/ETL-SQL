using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>Resolves all shared semantic decisions once, before any backend renders.</summary>
public sealed class PlotPlanResolver
{
    public PlotPlan Resolve(ChartSpec spec, ChartDataSet data, PlotBounds? bounds = null,
        ResolvedGeographicGeometry? geography = null)
    {
        spec.Validate();
        data.Validate();
        var columns = data.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var formatter = new ChartValueFormatter(spec.Formatting);
        var categories = ResolveCategories(spec, columns);
        var seriesKeys = ResolveSeries(spec, columns, categories);
        var distinctSortedSeries = seriesKeys.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var series = seriesKeys.Select((key, _) =>
        {
            var identityIndex = distinctSortedSeries.IndexOf(key);
            if (identityIndex < 0) identityIndex = 0;
            return new ResolvedSeries(key, key, identityIndex, ResolveColor(spec, key, identityIndex));
        }).OrderBy(s => s.Order).ThenBy(s => s.Key, StringComparer.Ordinal).ToImmutableArray();
        var palette = series.Select(item => new PaletteAssignment(item.Key, item.Color)).ToImmutableArray();
        var legend = series.Select(item => new LegendEntry(item.Key, item.Label, item.Order, item.Color)).ToImmutableArray();
        var layers = ResolveStacking(ResolveLayers(spec, data, columns, categories, series, formatter).ToImmutableArray());
        var scales = ResolveScales(spec, columns, categories, layers, formatter).ToImmutableArray();
        var plotBounds = bounds ?? new PlotBounds(0, 0, 600, 350);
        var facets = ResolveFacets(spec, columns, scales, plotBounds, formatter);
        layers = ResolveDisplayOffsets(spec, data, columns, layers, scales, facets, plotBounds);
        layers = ResolveMarkExtents(spec, layers);
        // One pass over the source layers. `sourceLayers` was a lazy query enumerated twice, and
        // `gapRows.Contains` on an ImmutableArray made the skipped-row scan O(rows x gap rows).
        var gapRowSet = new HashSet<int>();
        var usedRows = new HashSet<int>();
        foreach (var layer in layers)
        {
            if (!layer.Style.IsDefault && layer.Style.Any(token => token.Name == "overlayType")) continue;
            foreach (var datum in layer.Data)
                (datum.IsGap ? gapRowSet : usedRows).Add(datum.RowIndex);
        }
        var gapRows = gapRowSet.Order().ToImmutableArray();
        var skippedRows = Enumerable.Range(0, data.RowCount)
            .Where(index => !usedRows.Contains(index) && !gapRowSet.Contains(index)).ToImmutableArray();
        var fallback = BuildFallback(spec, layers, categories, formatter);
        var summary = BuildSummary(spec, data, series, layers, scales, facets, gapRows, skippedRows);

        var interaction = ChartInteractionResolver.Resolve(
            spec,
            data.Columns.Select(column => column.Name).ToArray(),
            ChartInteractionResolver.HighlightFor(layers));

        var plan = PlotPlan.Create(
            spec.Id,
            plotBounds,
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
            spec.Theme.Tokens,
            facets) with
        {
            CartesianViewport = ResolveCartesianViewport(spec.Coordinate, scales, plotBounds),
            Interaction = interaction,
            Geography = geography
        };
        plan.Validate();
        return plan;
    }

    /// <summary>
    /// Stamps the resolved value-extent semantics onto every layer, so renderers and the browser
    /// learn which dimension of a mark carries its value without recognising the chart by name.
    ///
    /// A mark has a baseline-anchored extent only when it is a plain rectangle in Cartesian or
    /// transposed-Cartesian space. An author-supplied interval (a ranged RECT) owns both endpoints,
    /// so its height is a span rather than a value and nothing may treat it as one. Focused layouts
    /// carry a `layout` style token and draw through their own geometry.
    /// </summary>
    private static ImmutableArray<ResolvedMarkLayer> ResolveMarkExtents(
        ChartSpec spec,
        ImmutableArray<ResolvedMarkLayer> layers)
    {
        var kind = spec.Coordinate.Kind;
        if (kind is not (CoordinateKind.Cartesian or CoordinateKind.TransposedCartesian)) return layers;

        return layers.Select(layer =>
        {
            if (layer.Mark != MarkKind.Rect) return layer;
            if (!layer.Style.IsDefaultOrEmpty && layer.Style.Any(token => token.Name == "layout")) return layer;
            if (layer.Stack == StackMode.None && layer.Data.Any(datum =>
                    (Channel(datum, FieldChannel.YStart) is not null && Channel(datum, FieldChannel.YEnd) is not null) ||
                    (Channel(datum, FieldChannel.XStart) is not null && Channel(datum, FieldChannel.XEnd) is not null)))
                return layer;

            return kind == CoordinateKind.Cartesian
                ? layer with { ExtentAxis = MarkExtentAxis.Y, ExtentAnchor = MarkExtentAnchor.End }
                : layer with { ExtentAxis = MarkExtentAxis.X, ExtentAnchor = MarkExtentAnchor.Start };
        }).ToImmutableArray();
    }

    private static ImmutableArray<string> ResolveCategories(
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns)
    {
        var binding = spec.Bindings.FirstOrDefault(item => item.Channel is FieldChannel.X or FieldChannel.Theta);
        if (binding?.Field is not { } categoryField || !columns.TryGetValue(categoryField, out var column)) return [];
        var categories = column.Values.Select(ValueKey).Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToList();
        var axisSort = spec.Theme.Tokens.FirstOrDefault(token => token.Name.Equals("AXIS_SORT", StringComparison.OrdinalIgnoreCase))?.Value.ToUpperInvariant();
        if (axisSort is "VALUE" or "VALUE_DESC")
        {
            var measureBinding = spec.Bindings.FirstOrDefault(item => item.Channel is FieldChannel.Y or FieldChannel.Radius);
            if (measureBinding?.Field is { } measureField && columns.TryGetValue(measureField, out var measure))
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
        if (spec.Coordinate.Kind == CoordinateKind.Polar && spec.Bindings.Any(binding => binding.Channel == FieldChannel.Theta)) return categories;
        var color = spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color);
        if (color?.SemanticKind is not DataSemanticKind.Quantitative && color?.Field is { } colorField && columns.TryGetValue(colorField, out var colorColumn))
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
        ImmutableArray<string> categories,
        ImmutableArray<ResolvedMarkLayer> resolvedLayers,
        ChartValueFormatter formatter)
    {
        foreach (var scale in spec.Scales)
        {
            var bindings = spec.Bindings.Concat(spec.Layers.SelectMany(layer => layer.Bindings))
                .Where(binding => binding.ScaleId == scale.Id &&
                    (binding.SourceKind != BindingSourceKind.Field || binding.Field is not null && columns.ContainsKey(binding.Field))).ToList();
            if (scale.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal)
            {
                var inferred = bindings.SelectMany(binding => BindingValues(binding, columns))
                    .Select(ValueKey).Where(value => value is not null).Cast<string>()
                    .Distinct(StringComparer.Ordinal).ToImmutableArray();
                var values = !scale.CategoryOrder.IsDefaultOrEmpty
                    ? scale.CategoryOrder
                    : scale.Channel is FieldChannel.X or FieldChannel.Theta && !categories.IsDefaultOrEmpty
                        ? categories
                        : !inferred.IsDefaultOrEmpty ? inferred : categories;
                yield return new ResolvedScale(scale.Id, scale.Channel, scale.Kind,
                    values.Select(ChartValue.From).ToImmutableArray(), values,
                    values.Select(value => new PlotTick(ChartValue.From(value), value)).ToImmutableArray(), scale.IncludeZero);
                continue;
            }

            var raw = bindings.SelectMany(binding => BindingValues(binding, columns)).Where(value => value.Kind != ChartValueKind.Null).ToList();
            var layout = spec.Layers.Select(layer => layer.Style.FirstOrDefault(token => token.Name.Equals("layout", StringComparison.OrdinalIgnoreCase))?.Value)
                .FirstOrDefault(value => value is not null);
            if (scale.Channel == FieldChannel.Y && layout is "boxplot" or "waterfall")
            {
                var channels = layout == "boxplot"
                    ? new[] { FieldChannel.Low, FieldChannel.Q1, FieldChannel.Median, FieldChannel.Q3, FieldChannel.High }
                    : new[] { FieldChannel.YStart, FieldChannel.YEnd };
                raw = resolvedLayers.SelectMany(layer => layer.Data).SelectMany(datum => datum.Channels)
                    .Where(channel => channels.Contains(channel.Channel) && channel.Value.Kind != ChartValueKind.Null)
                    .Select(channel => channel.Value).ToList();
            }
            if (scale.Channel is FieldChannel.Y or FieldChannel.Y2 && resolvedLayers.Any(layer => layer.Stack != StackMode.None))
            {
                // Stacked baselines replace the raw measure values (NORMALIZE rescales them entirely), but a
                // ranged layer sharing the chart still owns its authored endpoints and must stay inside the domain.
                raw = resolvedLayers.Where(layer => layer.Stack != StackMode.None).SelectMany(layer => layer.Data)
                    .SelectMany(datum => datum.Channels)
                    .Where(channel => channel.Channel is FieldChannel.YStart or FieldChannel.YEnd && channel.Value.Kind != ChartValueKind.Null)
                    .Select(channel => channel.Value)
                    .Concat(resolvedLayers.Where(layer => layer.Stack == StackMode.None).SelectMany(layer => layer.Data)
                        .SelectMany(datum => datum.Channels)
                        .Where(channel => channel.Channel is FieldChannel.YStart or FieldChannel.YEnd && channel.Value.Kind != ChartValueKind.Null)
                        .Select(channel => channel.Value))
                    .ToList();
            }
            if (scale.Kind == ScaleKind.Time)
            {
                var temporalValues = bindings.SelectMany(binding =>
                {
                    if (binding.Field is null || !columns.TryGetValue(binding.Field, out var column))
                        return BindingValues(binding, columns).Select(value => new { Value = value, Display = formatter.Format(value, binding.Field) });
                    return column.Values.Select((value, index) => new
                    {
                        Value = value,
                        Display = column.DisplayValues.IsDefaultOrEmpty
                            ? formatter.Format(value, binding.Field)
                            : column.DisplayValues[index] ?? formatter.Format(value, binding.Field)
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
            if (scale.Channel is FieldChannel.Y or FieldChannel.Y2)
            {
                numbers.AddRange(resolvedLayers
                    .Where(layer => layer.Style.Any(token => token.Name.Equals("overlayType", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(layer => layer.Data)
                    .Select(datum => Number(Channel(datum, scale.Channel) ?? ChartValue.Null()))
                    .Where(number => number.HasValue)
                    .Select(number => number!.Value));
            }
            var minimum = scale.DomainMinimum is not null ? Number(scale.DomainMinimum) ?? 0m : numbers.DefaultIfEmpty(0m).Min();
            var maximum = scale.DomainMaximum is not null ? Number(scale.DomainMaximum) ?? 1m : numbers.DefaultIfEmpty(1m).Max();
            if (scale.Kind == ScaleKind.Logarithmic && (minimum <= 0m || maximum <= 0m || numbers.Any(number => number <= 0m)))
                throw new InvalidOperationException($"Logarithmic scale '{scale.Id}' requires positive values and domain bounds.");
            if (scale.IncludeZero) { minimum = Math.Min(0m, minimum); maximum = Math.Max(0m, maximum); }
            if (minimum == maximum) maximum = minimum + 1m;
            var needsMarkHeadroom = !spec.Theme.Tokens.Any(token => token.Name.Equals("MICRO_CHART", StringComparison.OrdinalIgnoreCase)) &&
                resolvedLayers.Any(layer => !layer.Style.Any(token => token.Name.Equals("overlayType", StringComparison.OrdinalIgnoreCase)) &&
                    (scale.Channel is FieldChannel.Y or FieldChannel.Y2 && layer.Mark is MarkKind.Line or MarkKind.Area or MarkKind.Point ||
                     scale.Channel == FieldChannel.X && scale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic && layer.Mark == MarkKind.Point));
            if (needsMarkHeadroom)
            {
                var padding = Math.Max(.01m, (maximum - minimum) * .05m);
                if (scale.DomainMaximum is null) maximum += padding;
                if ((!scale.IncludeZero || scale.Channel == FieldChannel.X) && scale.DomainMinimum is null) minimum -= padding;
            }
            var ticks = scale.Kind == ScaleKind.Logarithmic
                ? Enumerable.Range(0, 5).Select(index => (decimal)Math.Pow(10d,
                    Math.Log10((double)minimum) + (Math.Log10((double)maximum) - Math.Log10((double)minimum)) * index / 4d)).ToList()
                : Enumerable.Range(0, 5).Select(index => minimum + ((maximum - minimum) * index / 4m)).ToList();
            var resolved = new ResolvedScale(scale.Id, scale.Channel, scale.Kind,
                [ChartValue.From(minimum), ChartValue.From(maximum)], [],
                ticks.Select(value => new PlotTick(ChartValue.From(value), formatter.Number(value))).ToImmutableArray(),
                scale.IncludeZero);
            if (scale.ColorRange is { } colorRange)
            {
                if (colorRange.Midpoint is { } midpoint && (midpoint < minimum || midpoint > maximum))
                    throw new InvalidOperationException($"Diverging midpoint {midpoint} for scale '{scale.Id}' must fall inside resolved domain [{minimum}, {maximum}].");
                var description = colorRange.Kind == ColorRangeKind.Diverging
                    ? $"Color ranges from {minimum:0.##} ({colorRange.Low}) through {colorRange.Midpoint:0.##} ({colorRange.Mid}) to {maximum:0.##} ({colorRange.High})."
                    : $"Color ranges from {minimum:0.##} ({colorRange.Low}) to {maximum:0.##} ({colorRange.High}).";
                resolved = resolved with
                {
                    ColorRange = new ResolvedColorRange(colorRange.Kind, colorRange.Low, colorRange.High,
                        colorRange.Mid, colorRange.Midpoint, colorRange.NullColor,
                        resolved.Ticks, description)
                };
            }
            yield return resolved;
        }
    }

    private static IEnumerable<ResolvedMarkLayer> ResolveLayers(
        ChartSpec spec,
        ChartDataSet data,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories,
        ImmutableArray<ResolvedSeries> series,
        ChartValueFormatter formatter)
    {
        foreach (var layer in spec.Layers.OrderBy(item => item.ZIndex))
        {
            var overlayType = layer.Style.FirstOrDefault(token => token.Name == "overlayType")?.Value;
            if (overlayType is not null)
            {
                yield return ResolveOverlay(layer, spec, columns, categories, formatter);
                continue;
            }

            var layout = layer.Style.FirstOrDefault(token => token.Name.Equals("layout", StringComparison.OrdinalIgnoreCase))?.Value;
            if (layout == "boxplot")
            {
                yield return ResolveBoxPlot(layer, spec, data, columns, categories, formatter);
                continue;
            }
            if (layout == "waterfall")
            {
                yield return ResolveWaterfall(layer, spec, data, columns, formatter);
                continue;
            }
            if (layout == "radar")
            {
                foreach (var radarLayer in ResolveRadar(layer, spec, data, columns, series, formatter)) yield return radarLayer;
                continue;
            }

            var colorBinding = layer.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color)
                ?? spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color);
            var explicitSeries = layer.Style.FirstOrDefault(token => token.Name == "series")?.Value;
            if (colorBinding?.SemanticKind is not DataSemanticKind.Quantitative && colorBinding?.Field is { } colorField && columns.TryGetValue(colorField, out var colorColumn))
            {
                foreach (var resolvedSeries in series)
                {
                    var dataPoints = ResolveLayerData(layer, spec, data, columns, categories, formatter,
                        rowIndex => ValueKey(colorColumn.Values[rowIndex]) == resolvedSeries.Key);
                    yield return new ResolvedMarkLayer($"{layer.Id}-{resolvedSeries.Order:D2}", layer.Mark, layer.ZIndex + resolvedSeries.Order,
                        resolvedSeries.Key, dataPoints)
                    { Style = layer.Style, Stack = LayerStack(layer, spec), BandSize = layer.BandSize, TickThickness = layer.TickThickness, TickOrientation = layer.TickOrientation, Position = layer.Position };
                }
            }
            else
            {
                var seriesKey = explicitSeries ?? series.FirstOrDefault()?.Key;
                yield return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, seriesKey,
                    ResolveLayerData(layer, spec, data, columns, categories, formatter, _ => true))
                { Style = layer.Style, Stack = LayerStack(layer, spec), BandSize = layer.BandSize, TickThickness = layer.TickThickness, TickOrientation = layer.TickOrientation, Position = layer.Position };
            }
        }
    }

    private static StackMode LayerStack(MarkLayerSpec layer, ChartSpec spec)
    {
        var declared = layer.Bindings.Select(binding => binding.Stack).FirstOrDefault(stack => stack != StackMode.None);
        return declared;
    }

    private static ImmutableArray<ResolvedMarkLayer> ResolveStacking(ImmutableArray<ResolvedMarkLayer> layers)
    {
        if (layers.All(layer => layer.Stack == StackMode.None)) return layers;
        var totals = layers.Where(layer => layer.Stack == StackMode.Normalize)
            .SelectMany(layer => layer.Data.Select(datum => (Layer: layer, Datum: datum)))
            .Select(item => (Key: StackKey(item.Datum), Value: StackValue(item.Datum)))
            .Where(item => item.Value.HasValue)
            .GroupBy(item => (item.Key, Sign: Math.Sign(item.Value!.Value)))
            .ToDictionary(group => group.Key, group => group.Sum(item => Math.Abs(item.Value!.Value)));
        var positive = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var negative = new Dictionary<string, decimal>(StringComparer.Ordinal);
        return layers.Select(layer =>
        {
            if (layer.Stack == StackMode.None) return layer;
            var data = layer.Data.Select(datum =>
            {
                var raw = StackValue(datum);
                if (!raw.HasValue || datum.IsGap) return datum;
                var key = StackKey(datum);
                var baselines = raw.Value < 0m ? negative : positive;
                var start = baselines.GetValueOrDefault(key);
                var amount = raw.Value;
                if (layer.Stack == StackMode.Normalize)
                {
                    var total = totals.GetValueOrDefault((key, Math.Sign(raw.Value)));
                    amount = total == 0m ? 0m : raw.Value / total;
                }
                var end = start + amount;
                baselines[key] = end;
                var channel = datum.Channels.Any(value => value.Channel == FieldChannel.Y2) ? FieldChannel.Y2 : FieldChannel.Y;
                var startChannel = channel == FieldChannel.Y2 ? FieldChannel.YStart : FieldChannel.YStart;
                var endChannel = channel == FieldChannel.Y2 ? FieldChannel.YEnd : FieldChannel.YEnd;
                return datum with
                {
                    Channels = datum.Channels
                        .Where(value => value.Channel != startChannel && value.Channel != endChannel).ToImmutableArray()
                        .Add(new ResolvedChannelValue(startChannel, ChartValue.From(start), start.ToString(CultureInfo.InvariantCulture)))
                        .Add(new ResolvedChannelValue(endChannel, ChartValue.From(end), end.ToString(CultureInfo.InvariantCulture)))
                };
            }).ToImmutableArray();
            return layer with { Data = data };
        }).ToImmutableArray();
    }

    private static decimal? StackValue(ResolvedDatum datum) =>
        Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? Channel(datum, FieldChannel.Radius) ?? ChartValue.Null());

    private static string StackKey(ResolvedDatum datum) => string.Join("\u001f", new[]
    {
        ValueKey(Channel(datum, FieldChannel.X) ?? Channel(datum, FieldChannel.Theta) ?? ChartValue.From(datum.RowIndex.ToString(CultureInfo.InvariantCulture))),
        ValueKey(Channel(datum, FieldChannel.Row) ?? ChartValue.From("")),
        ValueKey(Channel(datum, FieldChannel.Column) ?? ChartValue.From("")),
        ValueKey(Channel(datum, FieldChannel.Wrap) ?? ChartValue.From(""))
    });

    private static ResolvedMarkLayer ResolveBoxPlot(
        MarkLayerSpec layer,
        ChartSpec spec,
        ChartDataSet chartData,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories,
        ChartValueFormatter formatter)
    {
        // BOXPLOT supports both the ordinary X/Y form (the engine computes the
        // five-number summary) and the documented pre-calculated form used by
        // the kitchen sink (X, LOW, Q1, MEDIAN, Q3, HIGH). Preserve the latter
        // verbatim; there is no raw Y column to summarise in that form.
        if (!spec.Bindings.Any(binding => binding.Channel == FieldChannel.Y))
        {
            var resolved = ResolveLayerData(layer, spec, chartData, columns, categories, formatter, _ => true);
            return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, spec.Id, resolved)
            { Style = layer.Style, Stack = LayerStack(layer, spec), BandSize = layer.BandSize, TickThickness = layer.TickThickness, TickOrientation = layer.TickOrientation, Position = layer.Position };
        }
        var xBinding = spec.Bindings.First(binding => binding.Channel == FieldChannel.X);
        var yBinding = spec.Bindings.First(binding => binding.Channel == FieldChannel.Y);
        var xColumn = columns[xBinding.Field!];
        var yColumn = columns[yBinding.Field!];
        // Grouped in one pass. Each category used to rescan the whole X column, allocating a
        // ValueKey string per row per category.
        var rowsByCategory = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < xColumn.Values.Length; index++)
        {
            if (ValueKey(xColumn.Values[index]) is not { } categoryKey) continue;
            if (!rowsByCategory.TryGetValue(categoryKey, out var bucket))
                rowsByCategory[categoryKey] = bucket = [];
            bucket.Add(index);
        }
        var resolvedData = categories.Select((category, categoryIndex) =>
        {
            var rowIndices = rowsByCategory.TryGetValue(category, out var matched) ? matched : [];
            var values = rowIndices.Select(index => Number(yColumn.Values[index])).Where(value => value.HasValue)
                .Select(value => value!.Value).OrderBy(value => value).ToArray();
            var low = values.Length == 0 ? 0m : values[0];
            var q1 = Percentile(values, .25m);
            var median = Percentile(values, .5m);
            var q3 = Percentile(values, .75m);
            var high = values.Length == 0 ? 0m : values[^1];
            return new ResolvedDatum(rowIndices.FirstOrDefault(categoryIndex),
            [
                new ResolvedChannelValue(FieldChannel.X, ChartValue.From(category), category),
                new ResolvedChannelValue(FieldChannel.Low, ChartValue.From(low), low.ToString(CultureInfo.InvariantCulture)),
                new ResolvedChannelValue(FieldChannel.Q1, ChartValue.From(q1), q1.ToString(CultureInfo.InvariantCulture)),
                new ResolvedChannelValue(FieldChannel.Median, ChartValue.From(median), median.ToString(CultureInfo.InvariantCulture)),
                new ResolvedChannelValue(FieldChannel.Q3, ChartValue.From(q3), q3.ToString(CultureInfo.InvariantCulture)),
                new ResolvedChannelValue(FieldChannel.High, ChartValue.From(high), high.ToString(CultureInfo.InvariantCulture))
            ], values.Length == 0, $"{category}: {low}, {q1}, {median}, {q3}, {high}");
        }).ToImmutableArray();
        return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, spec.Id, resolvedData)
        { Style = layer.Style, Stack = LayerStack(layer, spec), BandSize = layer.BandSize, TickThickness = layer.TickThickness, TickOrientation = layer.TickOrientation, Position = layer.Position };
    }

    private static decimal Percentile(decimal[] sorted, decimal fraction)
    {
        if (sorted.Length == 0) return 0m;
        var position = fraction * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + (position - lower) * (sorted[upper] - sorted[lower]);
    }

    private static ResolvedMarkLayer ResolveWaterfall(
        MarkLayerSpec layer,
        ChartSpec spec,
        ChartDataSet data,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ChartValueFormatter formatter)
    {
        var raw = ResolveLayerData(layer, spec, data, columns, [], formatter, _ => true);
        var running = 0m;
        var output = raw.Select(datum =>
        {
            var delta = Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null()) ?? 0m;
            var total = Truthy(Channel(datum, FieldChannel.Detail) ?? ChartValue.From(false));
            var start = total ? 0m : running;
            var end = total ? delta : running + delta;
            running = end;
            return datum with
            {
                Channels = datum.Channels.Add(new ResolvedChannelValue(FieldChannel.YStart, ChartValue.From(start), start.ToString(CultureInfo.InvariantCulture)))
                    .Add(new ResolvedChannelValue(FieldChannel.YEnd, ChartValue.From(end), end.ToString(CultureInfo.InvariantCulture)))
            };
        }).ToImmutableArray();
        return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, spec.Id, output)
        { Style = layer.Style, Stack = LayerStack(layer, spec), BandSize = layer.BandSize, TickThickness = layer.TickThickness, TickOrientation = layer.TickOrientation, Position = layer.Position };
    }

    private static IEnumerable<ResolvedMarkLayer> ResolveRadar(
        MarkLayerSpec layer,
        ChartSpec spec,
        ChartDataSet data,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<ResolvedSeries> series,
        ChartValueFormatter formatter)
    {
        var seriesBinding = spec.Bindings.First(binding => binding.Channel == FieldChannel.Color && binding.Field is not null);
        var metrics = spec.Bindings.Where(binding => binding.Channel == FieldChannel.Detail && binding.Field is not null).ToList();
        for (var rowIndex = 0; rowIndex < data.RowCount; rowIndex++)
        {
            var key = ValueKey(columns[seriesBinding.Field!].Values[rowIndex]) ?? $"Series {rowIndex + 1}";
            var points = metrics.Select(binding =>
            {
                var column = columns[binding.Field!];
                var value = column.Values[rowIndex];
                return new ResolvedDatum(rowIndex,
                [
                    new ResolvedChannelValue(FieldChannel.Theta, ChartValue.From(binding.Field!), binding.Field),
                    new ResolvedChannelValue(FieldChannel.Radius, value,
                        column.DisplayValues.IsDefaultOrEmpty ? formatter.Format(value, binding.Field) : column.DisplayValues[rowIndex])
                ], value.Kind == ChartValueKind.Null, $"{binding.Field}: {formatter.Format(value, binding.Field)}");
            }).ToImmutableArray();
            yield return new ResolvedMarkLayer($"{layer.Id}-{rowIndex:D2}", layer.Mark, layer.ZIndex + rowIndex, key, points)
            { Style = layer.Style, Stack = LayerStack(layer, spec), BandSize = layer.BandSize, TickThickness = layer.TickThickness, TickOrientation = layer.TickOrientation, Position = layer.Position };
        }
    }

    private static ImmutableArray<ResolvedDatum> ResolveLayerData(
        MarkLayerSpec layer,
        ChartSpec spec,
        ChartDataSet data,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories,
        ChartValueFormatter formatter,
        Func<int, bool> include)
    {
        var layerBindings = layer.Bindings.IsDefaultOrEmpty ? spec.Bindings : layer.Bindings
            .AddRange(spec.Bindings.Where(binding => binding.Channel is FieldChannel.Row or FieldChannel.Column or FieldChannel.Wrap &&
                !layer.Bindings.Any(existing => existing.Channel == binding.Channel)));
        // The condition grouping is identical for every row of a layer; it used to be rebuilt per row.
        var conditionGroups = GroupConditions(layer.Conditions);
        var categoryBinding = layerBindings.FirstOrDefault(binding => binding.Channel is FieldChannel.X or FieldChannel.Theta);
        var rows = new List<ResolvedDatum>();
        var preserveRows = layer.Style.Any(token => token.Name.Equals("preserveRows", StringComparison.OrdinalIgnoreCase)
            && token.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        if (!preserveRows && categoryBinding is not null && categoryBinding.SemanticKind != DataSemanticKind.Quantitative &&
            categoryBinding.Field is { } categoryField && columns.TryGetValue(categoryField, out var categoryColumn) && !categories.IsDefaultOrEmpty)
        {
            var facetBindings = layerBindings.Where(binding => binding.Channel is FieldChannel.Row or FieldChannel.Column or FieldChannel.Wrap)
                .Where(binding => binding.Field is not null && columns.ContainsKey(binding.Field)).ToList();
            if (facetBindings.Count > 0)
            {
                // Indexed in one pass. This previously rescanned every row for each
                // (facet key, category) pair, rebuilding the joined facet key and a ValueKey string
                // per candidate row, so the work grew as facets x categories x rows.
                var facetColumns = facetBindings.Select(binding => columns[binding.Field!]).ToArray();
                var facetKeys = new List<string>();
                var seenFacetKeys = new HashSet<string>(StringComparer.Ordinal);
                var firstRowByFacetCategory = new Dictionary<(string Facet, string Category), int>();
                for (var index = 0; index < data.RowCount; index++)
                {
                    if (!include(index)) continue;
                    var facetKey = FacetKey(facetColumns, index);
                    if (seenFacetKeys.Add(facetKey)) facetKeys.Add(facetKey);
                    if (ValueKey(categoryColumn.Values[index]) is not { } categoryKey) continue;
                    firstRowByFacetCategory.TryAdd((facetKey, categoryKey), index);
                }
                foreach (var facetKey in facetKeys)
                    foreach (var category in categories)
                        if (firstRowByFacetCategory.TryGetValue((facetKey, category), out var rowIndex))
                            rows.Add(Datum(rowIndex, layerBindings, columns, spec.NullHandling, conditionGroups, formatter));
                return rows.ToImmutableArray();
            }
            // The same one-pass indexing for the unfaceted category path.
            var firstRowByCategory = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < data.RowCount; index++)
            {
                if (!include(index)) continue;
                if (ValueKey(categoryColumn.Values[index]) is { } categoryKey)
                    firstRowByCategory.TryAdd(categoryKey, index);
            }
            for (var categoryIndex = 0; categoryIndex < categories.Length; categoryIndex++)
            {
                var category = categories[categoryIndex];
                rows.Add(firstRowByCategory.TryGetValue(category, out var rowIndex)
                    ? Datum(rowIndex, layerBindings, columns, spec.NullHandling, conditionGroups, formatter)
                    : GapDatum(categoryIndex, layerBindings, categoryBinding, category));
            }
            return rows.ToImmutableArray();
        }

        for (var rowIndex = 0; rowIndex < data.RowCount; rowIndex++)
            if (include(rowIndex)) rows.Add(Datum(rowIndex, layerBindings, columns, spec.NullHandling, conditionGroups, formatter));
        return rows.ToImmutableArray();
    }

    private static ResolvedDatum Datum(
        int rowIndex,
        ImmutableArray<FieldBinding> bindings,
        IReadOnlyDictionary<string, ChartColumn> columns,
        NullHandlingSpec nulls,
        ImmutableArray<ImmutableArray<EncodingConditionSpec>> conditionGroups,
        ChartValueFormatter formatter)
    {
        var channels = bindings.Where(binding => binding.SourceKind != BindingSourceKind.Field || binding.Field is not null && columns.ContainsKey(binding.Field)).Select(binding =>
        {
            if (binding.SourceKind != BindingSourceKind.Field)
                return new ResolvedChannelValue(binding.Channel, binding.Constant!, formatter.Format(binding.Constant!));
            var column = columns[binding.Field!];
            return new ResolvedChannelValue(binding.Channel, column.Values[rowIndex],
                column.DisplayValues.IsDefaultOrEmpty ? null : column.DisplayValues[rowIndex]);
        }).ToImmutableArray();
        var requiredNull = channels.Any(channel =>
            (channel.Channel is FieldChannel.X or FieldChannel.X2 or FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or
                FieldChannel.XStart or FieldChannel.XEnd or FieldChannel.YStart or FieldChannel.YEnd or
                FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or FieldChannel.Q3 or FieldChannel.High or FieldChannel.Open or FieldChannel.Close)
            && channel.Value.Kind == ChartValueKind.Null);
        return new ResolvedDatum(rowIndex, channels, requiredNull && nulls.Default == NullValuePolicy.Gap,
            string.Join(", ", channels.Select(channel => $"{channel.Channel}: {channel.DisplayValue ?? formatter.Format(channel.Value)}")))
        {
            Encodings = ResolveConditions(conditionGroups, rowIndex, columns)
        };
    }

    private static ResolvedDatum GapDatum(int index, ImmutableArray<FieldBinding> bindings, FieldBinding categoryBinding, string category)
    {
        var channels = bindings.Select(binding => new ResolvedChannelValue(binding.Channel,
            binding == categoryBinding ? ChartValue.From(category) : ChartValue.Null(),
            binding == categoryBinding ? category : null)).ToImmutableArray();
        return new ResolvedDatum(index, channels, true, null);
    }

    /// <summary>
    /// Groups a layer's encoding conditions by channel, preserving first-appearance channel order and
    /// declaration order within each channel — the ordering <c>GroupBy</c> gave, hoisted out of the
    /// per-row path so it is computed once per layer.
    /// </summary>
    private static ImmutableArray<ImmutableArray<EncodingConditionSpec>> GroupConditions(
        ImmutableArray<EncodingConditionSpec> conditions)
    {
        if (conditions.IsDefaultOrEmpty) return [];
        var order = new List<ConditionalEncodingChannel>();
        var byChannel = new Dictionary<ConditionalEncodingChannel, List<EncodingConditionSpec>>();
        foreach (var condition in conditions)
        {
            if (!byChannel.TryGetValue(condition.Channel, out var group))
            {
                byChannel[condition.Channel] = group = [];
                order.Add(condition.Channel);
            }
            group.Add(condition);
        }
        var groups = ImmutableArray.CreateBuilder<ImmutableArray<EncodingConditionSpec>>(order.Count);
        foreach (var channel in order) groups.Add([.. byChannel[channel]]);
        return groups.MoveToImmutable();
    }

    private static ImmutableArray<ResolvedEncodingValue> ResolveConditions(
        ImmutableArray<ImmutableArray<EncodingConditionSpec>> conditionGroups,
        int rowIndex,
        IReadOnlyDictionary<string, ChartColumn> columns)
    {
        if (conditionGroups.IsDefaultOrEmpty) return [];
        var result = new List<ResolvedEncodingValue>();
        foreach (var group in conditionGroups)
        {
            foreach (var condition in group)
            {
                var matched = Evaluate(condition.Predicate, rowIndex, columns);
                var value = matched ? condition.WhenTrue : condition.WhenFalse;
                if (value is null) continue;
                result.Add(new ResolvedEncodingValue(condition.Channel, value));
                break;
            }
        }
        return result.ToImmutableArray();
    }

    private static bool Evaluate(EncodingPredicate predicate, int rowIndex, IReadOnlyDictionary<string, ChartColumn> columns) => predicate.Kind switch
    {
        PredicateKind.And => Evaluate(predicate.First!, rowIndex, columns) && Evaluate(predicate.Second!, rowIndex, columns),
        PredicateKind.Or => Evaluate(predicate.First!, rowIndex, columns) || Evaluate(predicate.Second!, rowIndex, columns),
        PredicateKind.Not => !Evaluate(predicate.First!, rowIndex, columns),
        PredicateKind.IsNull => Resolve(predicate.Left!, rowIndex, columns).Kind == ChartValueKind.Null,
        PredicateKind.IsNotNull => Resolve(predicate.Left!, rowIndex, columns).Kind != ChartValueKind.Null,
        PredicateKind.Truthy => Truthy(Resolve(predicate.Left!, rowIndex, columns)),
        PredicateKind.Comparison => Compare(Resolve(predicate.Left!, rowIndex, columns), Resolve(predicate.Right!, rowIndex, columns), predicate.Comparison!.Value),
        _ => false
    };

    private static ChartValue Resolve(PredicateOperand operand, int rowIndex, IReadOnlyDictionary<string, ChartColumn> columns) =>
        operand.Kind == PredicateOperandKind.Literal
            ? operand.Literal ?? ChartValue.Null()
            : operand.Field is not null && columns.TryGetValue(operand.Field, out var column) && rowIndex < column.Values.Length
                ? column.Values[rowIndex]
                : ChartValue.Null();

    private static bool Truthy(ChartValue value) => value.Kind switch
    {
        ChartValueKind.Null => false,
        ChartValueKind.Boolean => value.Boolean == true,
        _ when Number(value) is { } number => number != 0m,
        _ => !string.IsNullOrEmpty(Display(value))
    };

    private static bool Compare(ChartValue left, ChartValue right, ComparisonKind comparison)
    {
        if (left.Kind == ChartValueKind.Null || right.Kind == ChartValueKind.Null)
            return comparison == ComparisonKind.Equal ? left.Kind == right.Kind : comparison == ComparisonKind.NotEqual && left.Kind != right.Kind;
        var leftNumber = Number(left);
        var rightNumber = Number(right);
        var order = leftNumber.HasValue && rightNumber.HasValue
            ? leftNumber.Value.CompareTo(rightNumber.Value)
            : StringComparer.Ordinal.Compare(Display(left), Display(right));
        return comparison switch
        {
            ComparisonKind.Equal => order == 0,
            ComparisonKind.NotEqual => order != 0,
            ComparisonKind.LessThan => order < 0,
            ComparisonKind.LessThanOrEqual => order <= 0,
            ComparisonKind.GreaterThan => order > 0,
            ComparisonKind.GreaterThanOrEqual => order >= 0,
            _ => false
        };
    }

    private static ResolvedMarkLayer ResolveOverlay(
        MarkLayerSpec layer,
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories,
        ChartValueFormatter formatter)
    {
        var type = layer.Style.First(token => token.Name == "overlayType").Value;
        var parameterText = layer.Style.FirstOrDefault(token => token.Name == "parameter")?.Value;
        decimal.TryParse(parameterText, NumberStyles.Any, CultureInfo.InvariantCulture, out var parameter);
        var xBinding = spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.X);
        var yBinding = spec.Bindings.First(binding => binding.Channel is FieldChannel.Y or FieldChannel.Y2);
        var yValues = columns[yBinding.Field!].Values.Select(Number).Select(value => value ?? 0m).ToList();
        var xColumn = xBinding?.Field is { } xField && columns.TryGetValue(xField, out var resolvedXColumn) ? resolvedXColumn : null;
        var regressionX = Enumerable.Range(0, yValues.Count)
            .Select(index => xColumn is null ? index : Number(xColumn.Values[index]) ?? index)
            .ToList();
        var resolvedValues = type switch
        {
            "Goal" => Enumerable.Repeat(parameter, Math.Max(1, yValues.Count)).Select(value => (decimal?)value).ToList(),
            "Average" => Enumerable.Repeat(yValues.DefaultIfEmpty(0m).Average(), Math.Max(1, yValues.Count)).Select(value => (decimal?)value).ToList(),
            "MovingAvg" => MovingAverage(yValues, Math.Max(1, (int)(parameter == 0 ? 3 : parameter))),
            "Linear" => PolynomialRegression(regressionX, yValues, 1),
            "Polynomial" => PolynomialRegression(regressionX, yValues, Math.Max(1, (int)(parameter == 0 ? 2 : parameter))),
            _ => Enumerable.Repeat((decimal?)null, Math.Max(1, yValues.Count)).ToList()
        };
        var data = resolvedValues.Select((value, index) =>
        {
            var xValue = xColumn is null ? ChartValue.From(index) : xColumn.Values[index];
            var xDisplay = xColumn is null || xColumn.DisplayValues.IsDefaultOrEmpty
                ? formatter.Format(xValue)
                : xColumn.DisplayValues[index] ?? formatter.Format(xValue);
            return new ResolvedDatum(index,
                [
                    new ResolvedChannelValue(FieldChannel.X, xValue, xDisplay),
                    new ResolvedChannelValue(FieldChannel.Y, value.HasValue ? ChartValue.From(value.Value) : ChartValue.Null(),
                        value.HasValue ? formatter.Number(value.Value, "G") : formatter.NullLabel)
                ], !value.HasValue, null);
        }).ToImmutableArray();
        return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, null, data)
        { Style = layer.Style, Stack = LayerStack(layer, spec), BandSize = layer.BandSize, TickThickness = layer.TickThickness, TickOrientation = layer.TickOrientation, Position = layer.Position };
    }

    private static List<decimal?> MovingAverage(IReadOnlyList<decimal> values, int window) =>
        values.Select((_, index) => index < window - 1 ? (decimal?)null : values.Skip(index - window + 1).Take(window).Average()).ToList();

    private static List<decimal?> PolynomialRegression(
        IReadOnlyList<decimal> xValues,
        IReadOnlyList<decimal> yValues,
        int requestedDegree)
    {
        if (yValues.Count == 0 || xValues.Count != yValues.Count) return [];
        var degree = Math.Min(requestedDegree, yValues.Count - 1);
        var size = degree + 1;
        var matrix = new decimal[size, size + 1];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
                matrix[row, column] = xValues.Sum(value => Power(value, row + column));
            matrix[row, size] = Enumerable.Range(0, yValues.Count).Sum(index => yValues[index] * Power(xValues[index], row));
        }

        for (var pivot = 0; pivot < size; pivot++)
        {
            var best = Enumerable.Range(pivot, size - pivot)
                .OrderByDescending(row => Math.Abs(matrix[row, pivot])).First();
            if (matrix[best, pivot] == 0m) return Enumerable.Repeat((decimal?)null, yValues.Count).ToList();
            if (best != pivot)
                for (var column = pivot; column <= size; column++)
                    (matrix[pivot, column], matrix[best, column]) = (matrix[best, column], matrix[pivot, column]);
            var divisor = matrix[pivot, pivot];
            for (var column = pivot; column <= size; column++) matrix[pivot, column] /= divisor;
            for (var row = 0; row < size; row++)
            {
                if (row == pivot) continue;
                var factor = matrix[row, pivot];
                for (var column = pivot; column <= size; column++) matrix[row, column] -= factor * matrix[pivot, column];
            }
        }

        var coefficients = Enumerable.Range(0, size).Select(index => matrix[index, size]).ToArray();
        return xValues
            .Select(value => (decimal?)coefficients.Select((coefficient, power) => coefficient * Power(value, power)).Sum())
            .ToList();
    }

    private static decimal Power(decimal value, int exponent)
    {
        var result = 1m;
        for (var index = 0; index < exponent; index++) result *= value;
        return result;
    }

    /// <summary>Describes an author-supplied ranged rectangle so non-visual surfaces keep both endpoints.</summary>
    private static string? IntervalDetail(ResolvedDatum datum, ChartValueFormatter formatter)
    {
        var parts = new List<string>();
        Append(FieldChannel.XStart, FieldChannel.XEnd, "x range");
        Append(FieldChannel.YStart, FieldChannel.YEnd, "range");
        return parts.Count == 0 ? null : string.Join(", ", parts);

        void Append(FieldChannel start, FieldChannel end, string name)
        {
            var first = datum.Channels.FirstOrDefault(channel => channel.Channel == start);
            var second = datum.Channels.FirstOrDefault(channel => channel.Channel == end);
            if (first is null || second is null) return;
            parts.Add($"{name} {first.DisplayValue ?? formatter.Format(first.Value)} to {second.DisplayValue ?? formatter.Format(second.Value)}");
        }
    }

    private static SemanticFallback BuildFallback(ChartSpec spec, ImmutableArray<ResolvedMarkLayer> layers,
        ImmutableArray<string> categories, ChartValueFormatter formatter)
    {
        var sourceLayers = layers.Where(layer => layer.Mark is not MarkKind.Rule).ToList();
        var total = sourceLayers.Where(layer => layer.Mark == MarkKind.Arc).SelectMany(layer => layer.Data)
            .Select(datum => datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.Radius or FieldChannel.Y))
            .Where(value => value is not null).Sum(value => Math.Max(0m, Number(value!.Value) ?? 0m));
        var items = sourceLayers.SelectMany((layer, layerIndex) => layer.Data.Select((datum, index) =>
        {
            var label = datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.Region or FieldChannel.Route or FieldChannel.Text or FieldChannel.X or FieldChannel.Theta)?.DisplayValue
                ?? (index < categories.Length ? categories[index] : $"Row {index + 1}");
            var value = datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or
                FieldChannel.Median or FieldChannel.Close or FieldChannel.Size or FieldChannel.YEnd);
            var numeric = value is null ? null : Number(value.Value);
            var conditionDetail = datum.Encodings.IsDefaultOrEmpty ? null : string.Join(", ", datum.Encodings.Select(encoding =>
                $"conditional {encoding.Channel}: {formatter.Format(encoding.Value)}"));
            var interval = layer.Mark == MarkKind.Rect && layer.Stack == StackMode.None ? IntervalDetail(datum, formatter) : null;
            return new SemanticFallbackItem(label ?? $"Row {index + 1}", datum.IsGap ? "gap" : value is null ? "" : value.DisplayValue ?? formatter.Format(value.Value), (layerIndex * 100000) + index)
            {
                Group = layer.SeriesKey,
                Detail = datum.IsGap ? "null gap" : interval ?? conditionDetail ?? (layer.Mark == MarkKind.Arc && numeric.HasValue && total > 0m
                    ? $"{numeric.Value / total:P1} of total"
                    : null)
            };
        })).Concat(layers.Where(layer => layer.Mark == MarkKind.Rule).Select((layer, index) =>
        {
            var value = layer.Data.SelectMany(datum => datum.Channels)
                .FirstOrDefault(channel => channel.Channel is FieldChannel.Y or FieldChannel.X);
            var label = layer.Style.FirstOrDefault(token => token.Name.Equals("label", StringComparison.OrdinalIgnoreCase))?.Value
                ?? layer.Style.FirstOrDefault(token => token.Name == "overlayType")?.Value ?? layer.Id;
            return new SemanticFallbackItem(label, value is null ? "" : value.DisplayValue ?? formatter.Format(value.Value), ((sourceLayers.Count + 1) * 100000) + index)
            { Detail = "labeled reference rule", Group = "Reference" };
        })).ToImmutableArray();
        return new SemanticFallback(
            spec.Coordinate.Kind == CoordinateKind.Geographic && spec.Layers.Any(layer => layer.Bindings.Any(binding => binding.Channel == FieldChannel.Route)) ? SemanticFallbackKind.TransitionTable :
            spec.Coordinate.Kind == CoordinateKind.Polar ? SemanticFallbackKind.ProportionalBreakdown :
            spec.Bindings.Any(binding => binding.SemanticKind == DataSemanticKind.Temporal) ? SemanticFallbackKind.TimeSeriesTable : SemanticFallbackKind.RankedTable,
            spec.Title ?? spec.Id,
            items)
        { Summary = $"{spec.Title ?? spec.Id}: {items.Length} ordered semantic values." };
    }

    private static ImmutableArray<ResolvedFacetPanel> ResolveFacets(
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<ResolvedScale> globalScales,
        PlotBounds bounds,
        ChartValueFormatter formatter)
    {
        if (spec.Facet is null) return [];
        if (spec.Facet.WrapField is not null)
            return ResolveWrappedFacets(spec, columns, globalScales, bounds, formatter);
        var rowValues = FacetValues(spec.Facet.RowField, columns);
        var columnValues = FacetValues(spec.Facet.ColumnField, columns);
        if (rowValues.Count == 0) rowValues.Add(null);
        if (columnValues.Count == 0) columnValues.Add(null);
        ValidateFacetBudget(rowValues.Count * columnValues.Count,
            columns.Values.FirstOrDefault()?.Values.Length ?? 0, bounds, columnValues.Count, rowValues.Count,
            rowValues.Concat(columnValues).Where(value => value is not null).Select(value => value!.Length).DefaultIfEmpty(0).Max());
        var panels = new List<ResolvedFacetPanel>();
        var panelWidth = bounds.Width / columnValues.Count;
        var panelHeight = bounds.Height / rowValues.Count;
        var sourceRowCount = columns.Values.FirstOrDefault()?.Values.Length ?? 0;
        var rowFacetKeys = FacetRowKeys(spec.Facet.RowField, columns);
        var columnFacetKeys = FacetRowKeys(spec.Facet.ColumnField, columns);
        for (var rowIndex = 0; rowIndex < rowValues.Count; rowIndex++)
            for (var columnIndex = 0; columnIndex < columnValues.Count; columnIndex++)
            {
                var rowLabel = rowValues[rowIndex];
                var columnLabel = columnValues[columnIndex];
                var indices = FacetIndices(sourceRowCount, rowFacetKeys, rowLabel, columnFacetKeys, columnLabel);
                if (indices.IsDefaultOrEmpty) continue;
                var scales = globalScales.Select(scale => Independent(scale, spec.Facet.Resolution, spec, columns, indices, formatter)).ToImmutableArray();
                var panelBounds = new PlotBounds(bounds.X + columnIndex * panelWidth, bounds.Y + rowIndex * panelHeight, panelWidth, panelHeight);
                panels.Add(new ResolvedFacetPanel(
                    $"facet-{rowIndex:D2}-{columnIndex:D2}", rowLabel, columnLabel,
                    panelBounds, indices, scales)
                {
                    CartesianViewport = ResolveCartesianViewport(spec.Coordinate, scales, panelBounds)
                });
            }
        return panels.ToImmutableArray();
    }

    private static ImmutableArray<ResolvedFacetPanel> ResolveWrappedFacets(
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<ResolvedScale> globalScales,
        PlotBounds bounds,
        ChartValueFormatter formatter)
    {
        var facet = spec.Facet!;
        var values = FacetValues(facet.WrapField, columns);
        if (values.Count == 0) return [];
        var columnCount = Math.Min(facet.Columns ?? 3, values.Count);
        var rowCount = (values.Count + columnCount - 1) / columnCount;
        var sourceRowCount = columns.Values.FirstOrDefault()?.Values.Length ?? 0;
        ValidateFacetBudget(values.Count, sourceRowCount, bounds, columnCount, rowCount,
            values.Where(value => value is not null).Select(value => value!.Length).DefaultIfEmpty(0).Max());

        var panelWidth = bounds.Width / columnCount;
        var panelHeight = bounds.Height / rowCount;
        var panels = new List<ResolvedFacetPanel>(values.Count);
        var wrapFacetKeys = FacetRowKeys(facet.WrapField, columns);
        for (var index = 0; index < values.Count; index++)
        {
            var rowIndex = index / columnCount;
            var columnIndex = index % columnCount;
            var label = values[index];
            var indices = FacetIndices(sourceRowCount, wrapFacetKeys, label, null, null);
            var scales = globalScales.Select(scale => Independent(scale, facet.Resolution, spec, columns, indices, formatter)).ToImmutableArray();
            var panelBounds = new PlotBounds(bounds.X + columnIndex * panelWidth, bounds.Y + rowIndex * panelHeight, panelWidth, panelHeight);
            panels.Add(new ResolvedFacetPanel(
                $"facet-wrap-{index:D3}", null, label,
                panelBounds, indices, scales)
            {
                CartesianViewport = ResolveCartesianViewport(spec.Coordinate, scales, panelBounds)
            });
        }
        return panels.ToImmutableArray();
    }

    private static void ValidateFacetBudget(int panelCount, int sourceRowCount, PlotBounds bounds, int columns, int rows, int longestLabel)
    {
        const int maxPanels = 100;
        const long maxRenderWork = 1_000_000;
        const decimal minimumPanelWidth = 120m;
        const decimal minimumPanelHeight = 110m;
        if (panelCount > maxPanels)
            throw new InvalidDataException($"Facet cardinality {panelCount} exceeds the {maxPanels}-panel limit; filter or group the facet field.");
        if ((long)panelCount * sourceRowCount > maxRenderWork)
            throw new InvalidDataException($"Facet render work ({panelCount} panels × {sourceRowCount} rows) exceeds the {maxRenderWork:N0}-cell limit; filter or aggregate the source.");
        if (bounds.Width / columns < minimumPanelWidth || bounds.Height / rows < minimumPanelHeight)
            throw new InvalidDataException($"Facet layout would create panels smaller than {minimumPanelWidth}×{minimumPanelHeight}; reduce COLUMNS/cardinality or enlarge the visual.");
        if (longestLabel * 6m + 16m > bounds.Width / columns)
            throw new InvalidDataException($"Facet label length ({longestLabel} characters) exceeds the available strip width; reduce COLUMNS, shorten labels in SQL, or enlarge the visual.");
    }

    private static PlotBounds? ResolveCartesianViewport(
        CoordinateSpec coordinate, ImmutableArray<ResolvedScale> scales, PlotBounds bounds)
    {
        if (coordinate.AspectRatio is not { } aspectRatio) return null;
        var xScale = scales.First(scale => scale.Channel == FieldChannel.X);
        var yScale = scales.First(scale => scale.Channel == FieldChannel.Y);
        var xSpan = ScaleSpan(xScale);
        var ySpan = ScaleSpan(yScale);
        if (xSpan <= 0m || ySpan <= 0m)
            throw new InvalidDataException("ASPECT_RATIO requires non-degenerate X and Y domains.");

        const decimal horizontalChrome = 80m;
        const decimal verticalChrome = 100m;
        var availableWidth = bounds.Width - horizontalChrome;
        var availableHeight = bounds.Height - verticalChrome;
        if (availableWidth <= 0m || availableHeight <= 0m)
            throw new InvalidDataException("ASPECT_RATIO requires a visual large enough for axes and plot content.");
        var desiredPlotRatio = aspectRatio * ySpan / xSpan;
        var plotWidth = availableWidth;
        var plotHeight = plotWidth * desiredPlotRatio;
        if (plotHeight > availableHeight)
        {
            plotHeight = availableHeight;
            plotWidth = plotHeight / desiredPlotRatio;
        }
        var frameWidth = plotWidth + horizontalChrome;
        var frameHeight = plotHeight + verticalChrome;
        return new PlotBounds(bounds.X + (bounds.Width - frameWidth) / 2m,
            bounds.Y + (bounds.Height - frameHeight) / 2m, frameWidth, frameHeight);
    }

    private static decimal ScaleSpan(ResolvedScale scale)
    {
        var minimum = scale.Domain.Length == 0 ? 0m : Number(scale.Domain[0]) ?? 0m;
        var maximum = scale.Domain.Length < 2 ? minimum + 1m : Number(scale.Domain[^1]) ?? minimum + 1m;
        if (scale.Kind != ScaleKind.Logarithmic) return Math.Abs(maximum - minimum);
        if (minimum <= 0m || maximum <= 0m) return 0m;
        return Math.Abs((decimal)(Math.Log10((double)maximum) - Math.Log10((double)minimum)));
    }

    private static List<string?> FacetValues(string? field, IReadOnlyDictionary<string, ChartColumn> columns) =>
        field is null || !columns.TryGetValue(field, out var column)
            ? []
            : column.Values.Select(ValueKey).Distinct(StringComparer.Ordinal).Cast<string?>().ToList();

    /// <summary>
    /// Per-row keys for one facet field. <c>null</c> means the field is unset, so every row matches;
    /// an empty array means the field was declared but is absent from the data, which no row matches.
    /// Resolved once per field so panel membership costs a string comparison rather than a fresh
    /// <see cref="ValueKey"/> allocation for every panel-row pair.
    /// </summary>
    private static string?[]? FacetRowKeys(string? field, IReadOnlyDictionary<string, ChartColumn> columns)
    {
        if (field is null) return null;
        if (!columns.TryGetValue(field, out var column)) return [];
        var keys = new string?[column.Values.Length];
        for (var i = 0; i < keys.Length; i++) keys[i] = ValueKey(column.Values[i]);
        return keys;
    }

    private static bool MatchesFacet(string?[]? keys, string? value, int index) =>
        keys is null || (index < keys.Length && keys[index] == value);

    /// <summary>Row indices belonging to one panel, collected without a LINQ pipeline per panel.</summary>
    private static ImmutableArray<int> FacetIndices(
        int rowCount, string?[]? rowKeys, string? rowValue, string?[]? columnKeys, string? columnValue)
    {
        var indices = ImmutableArray.CreateBuilder<int>();
        for (var index = 0; index < rowCount; index++)
            if (MatchesFacet(rowKeys, rowValue, index) && MatchesFacet(columnKeys, columnValue, index))
                indices.Add(index);
        return indices.ToImmutable();
    }

    private static ResolvedScale Independent(
        ResolvedScale scale,
        ScaleResolutionSpec resolution,
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<int> rows,
        ChartValueFormatter formatter)
    {
        var mode = scale.Channel switch
        {
            FieldChannel.X => resolution.X,
            FieldChannel.Y or FieldChannel.Y2 => resolution.Y,
            FieldChannel.Color => resolution.Color,
            _ => ScaleResolutionMode.Shared
        };
        if (mode == ScaleResolutionMode.Shared) return scale;
        var bindings = spec.Bindings.Concat(spec.Layers.SelectMany(layer => layer.Bindings))
            .Where(binding => binding.ScaleId == scale.Id &&
                (binding.SourceKind != BindingSourceKind.Field || binding.Field is not null && columns.ContainsKey(binding.Field))).ToList();
        if (scale.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal)
        {
            var categories = bindings.SelectMany(binding => rows.Select(index => ValueKey(BindingValue(binding, columns, index))))
                .Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToImmutableArray();
            return scale with
            {
                Domain = categories.Select(ChartValue.From).ToImmutableArray(),
                Categories = categories,
                Ticks = categories.Select(value => new PlotTick(ChartValue.From(value), value)).ToImmutableArray()
            };
        }
        if (scale.Kind == ScaleKind.Time)
        {
            var temporal = bindings.SelectMany(binding => rows.Select(index => BindingValue(binding, columns, index)))
                .Where(value => value.Kind != ChartValueKind.Null)
                .GroupBy(ValueKey, StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(ValueKey, StringComparer.Ordinal).ToImmutableArray();
            return scale with
            {
                Domain = temporal,
                Categories = temporal.Select(value => formatter.Format(value)).ToImmutableArray(),
                Ticks = temporal.Select(value => new PlotTick(value, formatter.Format(value))).ToImmutableArray()
            };
        }
        var values = bindings.SelectMany(binding => rows.Select(index => Number(BindingValue(binding, columns, index))))
            .Where(value => value.HasValue).Select(value => value!.Value).ToList();
        if (values.Count == 0) return scale;
        var minimum = values.Min();
        var maximum = values.Max();
        if (scale.Kind == ScaleKind.Logarithmic && minimum <= 0m)
            throw new InvalidOperationException($"Independent logarithmic scale '{scale.Id}' requires positive values.");
        if (scale.IncludesZero) { minimum = Math.Min(0m, minimum); maximum = Math.Max(0m, maximum); }
        if (minimum == maximum) maximum = minimum + 1m;
        var ticks = (scale.Kind == ScaleKind.Logarithmic
            ? Enumerable.Range(0, 5).Select(index => (decimal)Math.Pow(10d,
                Math.Log10((double)minimum) + (Math.Log10((double)maximum) - Math.Log10((double)minimum)) * index / 4d))
            : Enumerable.Range(0, 5).Select(index => minimum + (maximum - minimum) * index / 4m)).ToImmutableArray();
        var colorRange = scale.ColorRange;
        if (colorRange is not null)
        {
            if (colorRange.Midpoint is { } midpoint && (midpoint < minimum || midpoint > maximum))
                throw new InvalidOperationException($"Diverging midpoint {midpoint} for independent scale '{scale.Id}' must fall inside panel domain [{minimum}, {maximum}].");
            colorRange = colorRange with
            {
                Ticks = ticks.Select(value => new PlotTick(ChartValue.From(value), formatter.Number(value))).ToImmutableArray(),
                AccessibleDescription = colorRange.Kind == ColorRangeKind.Diverging
                    ? $"Color ranges from {minimum:0.##} ({colorRange.Low}) through {colorRange.Midpoint:0.##} ({colorRange.Mid}) to {maximum:0.##} ({colorRange.High})."
                    : $"Color ranges from {minimum:0.##} ({colorRange.Low}) to {maximum:0.##} ({colorRange.High})."
            };
        }
        return scale with
        {
            Domain = [ChartValue.From(minimum), ChartValue.From(maximum)],
            Ticks = ticks.Select(value => new PlotTick(ChartValue.From(value), formatter.Number(value))).ToImmutableArray(),
            ColorRange = colorRange
        };
    }

    private static ImmutableArray<ResolvedMarkLayer> ResolveDisplayOffsets(
        ChartSpec spec,
        ChartDataSet data,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<ResolvedMarkLayer> layers,
        ImmutableArray<ResolvedScale> scales,
        ImmutableArray<ResolvedFacetPanel> facets,
        PlotBounds bounds)
    {
        var panelByRow = facets.SelectMany(panel => panel.RowIndices.Select(row => (row, panel)))
            .ToDictionary(item => item.row, item => item.panel);

        foreach (var layer in layers.Where(layer => layer.Position?.Kind == PositionAdjustmentKind.Jitter))
        {
            var keyField = layer.Position!.StableKeyField!;
            if (!columns.TryGetValue(keyField, out var keyColumn))
                throw new InvalidOperationException($"Layer '{layer.Id}' JITTER key field '{keyField}' does not exist.");
            var keys = keyColumn.Values.Select(ValueKey).ToArray();
            if (keys.Any(key => key is null))
                throw new InvalidOperationException($"Layer '{layer.Id}' JITTER key field '{keyField}' contains nulls.");
            var duplicate = keys.GroupBy(key => key!, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
                throw new InvalidOperationException($"Layer '{layer.Id}' JITTER key field '{keyField}' contains duplicate value '{duplicate.Key}'.");
        }

        return layers.Select(layer => layer with
        {
            Data = layer.Data.Select(datum =>
            {
                var panel = panelByRow.GetValueOrDefault(datum.RowIndex);
                var datumScales = panel?.Scales ?? scales;
                var datumBounds = panel?.CartesianViewport ?? panel?.Bounds ?? bounds;
                var xScale = datumScales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
                var yScale = datumScales.FirstOrDefault(scale => scale.Channel is FieldChannel.Y or FieldChannel.Y2);
                var xBand = datumBounds.Width / Math.Max(1, xScale?.Categories.Length ?? 1);
                var yBand = datumBounds.Height / Math.Max(1, yScale?.Categories.Length ?? 1);
                var offsetX = ResolveOffsetChannel(datum, FieldChannel.XOffset, datumScales, xBand);
                var offsetY = ResolveOffsetChannel(datum, FieldChannel.YOffset, datumScales, yBand);
                if (layer.Position is { } position)
                {
                    if (position.Kind == PositionAdjustmentKind.Jitter)
                    {
                        var key = ValueKey(columns[position.StableKeyField!].Values[datum.RowIndex])!;
                        var identity = LayerPlacementIdentity(layer);
                        offsetX += SignedHash(spec.Id, identity, key, "x", position.Seed) * position.X *
                            (xScale?.Kind is ScaleKind.Band or ScaleKind.Point ? xBand : datumBounds.Width);
                        offsetY += SignedHash(spec.Id, identity, key, "y", position.Seed) * position.Y *
                            (yScale?.Kind is ScaleKind.Band or ScaleKind.Point ? yBand : datumBounds.Height);
                    }
                    else if (position.Kind == PositionAdjustmentKind.Nudge)
                    {
                        var nudge = ResolveNudge(position, datum, xScale, yScale, datumBounds, xBand, yBand, layer.Id);
                        offsetX += nudge.X;
                        offsetY += nudge.Y;
                    }
                }
                return datum with { DisplayOffsetX = offsetX, DisplayOffsetY = offsetY };
            }).ToImmutableArray()
        }).ToImmutableArray();
    }

    private static decimal ResolveOffsetChannel(ResolvedDatum datum, FieldChannel channel,
        ImmutableArray<ResolvedScale> scales, decimal band)
    {
        var value = Channel(datum, channel);
        if (value is null || value.Kind == ChartValueKind.Null) return 0m;
        var categories = scales.FirstOrDefault(scale => scale.Channel == channel)?.Categories ?? [];
        var index = categories.IndexOf(Display(value));
        return index < 0 || categories.Length < 2 ? 0m :
            (((index + .5m) / categories.Length) - .5m) * band;
    }

    private static (decimal X, decimal Y) ResolveNudge(PositionAdjustmentSpec position, ResolvedDatum datum,
        ResolvedScale? xScale, ResolvedScale? yScale, PlotBounds bounds, decimal xBand, decimal yBand, string layerId)
    {
        if (position.Unit == PositionAdjustmentUnit.Em) return (position.X * 12m, -position.Y * 12m);
        if (position.Unit == PositionAdjustmentUnit.Band) return (position.X * xBand, -position.Y * yBand);
        if (xScale?.Kind is not (ScaleKind.Linear or ScaleKind.Logarithmic) ||
            yScale?.Kind is not (ScaleKind.Linear or ScaleKind.Logarithmic))
            throw new InvalidOperationException($"Layer '{layerId}' data-domain NUDGE requires two continuous numeric scales.");
        var x = Number(Channel(datum, FieldChannel.X) ?? ChartValue.Null());
        var y = Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
        if (!x.HasValue || !y.HasValue)
            throw new InvalidOperationException($"Layer '{layerId}' data-domain NUDGE requires numeric X and Y datum values.");
        return (DataNudge(x.Value, position.X, xScale, bounds.Width, layerId, "X"),
            -DataNudge(y.Value, position.Y, yScale, bounds.Height, layerId, "Y"));
    }

    private static decimal DataNudge(decimal value, decimal amount, ResolvedScale scale, decimal length,
        string layerId, string channel)
    {
        var minimum = scale.Domain.Length == 0 ? 0m : Number(scale.Domain[0]) ?? 0m;
        var maximum = scale.Domain.Length < 2 ? minimum : Number(scale.Domain[^1]) ?? minimum;
        if (maximum <= minimum)
            throw new InvalidOperationException($"Layer '{layerId}' data-domain NUDGE requires a non-zero {channel} domain.");
        if (scale.Kind == ScaleKind.Logarithmic && (value <= 0m || value + amount <= 0m))
            throw new InvalidOperationException($"Layer '{layerId}' data-domain NUDGE moves {channel} outside the positive logarithmic domain.");
        decimal Position(decimal candidate) => scale.Kind == ScaleKind.Logarithmic
            ? (decimal)((Math.Log10((double)candidate) - Math.Log10((double)minimum)) /
                        (Math.Log10((double)maximum) - Math.Log10((double)minimum)))
            : (candidate - minimum) / (maximum - minimum);
        return (Position(value + amount) - Position(value)) * length;
    }

    private static string LayerPlacementIdentity(ResolvedMarkLayer layer) =>
        $"{layer.Mark}|{layer.ZIndex}|{string.Join(',', layer.Data.FirstOrDefault()?.Channels.Select(channel => channel.Channel) ?? [])}";

    private static decimal SignedHash(string chart, string layer, string key, string channel, int seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{chart}\u001f{layer}\u001f{key}\u001f{channel}\u001f{seed}"));
        ulong value = 0;
        for (var index = 0; index < sizeof(ulong); index++) value = (value << 8) | bytes[index];
        return (decimal)value / ulong.MaxValue * 2m - 1m;
    }

    private static string BuildSummary(ChartSpec spec, ChartDataSet data, ImmutableArray<ResolvedSeries> series,
        ImmutableArray<ResolvedMarkLayer> layers, ImmutableArray<ResolvedScale> scales,
        ImmutableArray<ResolvedFacetPanel> facets, ImmutableArray<int> gaps, ImmutableArray<int> skipped) =>
        $"{spec.Title ?? spec.Id}: {data.RowCount} rows, {layers.Length} ordered layers, {series.Length} series, " +
        $"{(facets.IsDefaultOrEmpty ? 1 : facets.Length)} facet panels, {gaps.Length} gaps, {skipped.Length} skipped rows." +
        (scales.FirstOrDefault(scale => scale.ColorRange is not null)?.ColorRange is { } range ? " " + range.AccessibleDescription : string.Empty);

    private static string ResolveColor(ChartSpec spec, string key, int index) =>
        ChartPalette.Resolve(spec.Theme.Tokens, key, index);

    private static IEnumerable<ChartValue> BindingValues(FieldBinding binding, IReadOnlyDictionary<string, ChartColumn> columns)
    {
        if (binding.SourceKind != BindingSourceKind.Field)
            return binding.Constant is null ? [] : [binding.Constant];
        return binding.Field is not null && columns.TryGetValue(binding.Field, out var column) ? column.Values : [];
    }

    private static ChartValue BindingValue(FieldBinding binding, IReadOnlyDictionary<string, ChartColumn> columns, int rowIndex) =>
        binding.SourceKind == BindingSourceKind.Field && binding.Field is not null && columns.TryGetValue(binding.Field, out var column)
            ? column.Values[rowIndex]
            : binding.Constant ?? ChartValue.Null();

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

    private static ChartValue? Channel(ResolvedDatum datum, FieldChannel channel)
    {
        var channels = datum.Channels;
        for (var i = 0; i < channels.Length; i++)
        {
            if (channels[i].Channel == channel)
                return channels[i].Value;
        }
        return null;
    }

    private static string? ValueKey(ChartValue value) => value.Kind == ChartValueKind.Null ? null : Display(value);

    /// <summary>The unit-separator-joined facet key for one row across a layer's facet columns.</summary>
    private static string FacetKey(ChartColumn[] facetColumns, int rowIndex)
    {
        if (facetColumns.Length == 1) return ValueKey(facetColumns[0].Values[rowIndex]) ?? string.Empty;
        var parts = new string[facetColumns.Length];
        for (var i = 0; i < facetColumns.Length; i++)
            parts[i] = ValueKey(facetColumns[i].Values[rowIndex]) ?? string.Empty;
        return string.Join("\u001f", parts);
    }
}
