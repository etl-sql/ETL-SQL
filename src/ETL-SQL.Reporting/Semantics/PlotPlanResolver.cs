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
        var scales = ResolveScales(spec, columns, categories, layers).ToImmutableArray();
        var plotBounds = bounds ?? new PlotBounds(0, 0, 600, 350);
        var facets = ResolveFacets(spec, columns, scales, plotBounds);
        var sourceLayers = layers.Where(layer => layer.Style.IsDefault || !layer.Style.Any(token => token.Name == "overlayType"));
        var gapRows = sourceLayers.SelectMany(layer => layer.Data).Where(datum => datum.IsGap).Select(datum => datum.RowIndex).Distinct().Order().ToImmutableArray();
        var usedRows = sourceLayers.SelectMany(layer => layer.Data).Where(datum => !datum.IsGap).Select(datum => datum.RowIndex).ToHashSet();
        var skippedRows = Enumerable.Range(0, data.RowCount).Where(index => !usedRows.Contains(index) && !gapRows.Contains(index)).ToImmutableArray();
        var fallback = BuildFallback(spec, layers, categories);
        var summary = BuildSummary(spec, data, series, layers, facets, gapRows, skippedRows);

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
            facets);
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
        if (spec.Coordinate.Kind == CoordinateKind.Polar && spec.Bindings.Any(binding => binding.Channel == FieldChannel.Theta)) return categories;
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
        ImmutableArray<string> categories,
        ImmutableArray<ResolvedMarkLayer> resolvedLayers)
    {
        foreach (var scale in spec.Scales)
        {
            var bindings = spec.Bindings.Concat(spec.Layers.SelectMany(layer => layer.Bindings))
                .Where(binding => binding.ScaleId == scale.Id && columns.ContainsKey(binding.Field)).ToList();
            if (scale.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal)
            {
                var inferred = bindings.SelectMany(binding => columns[binding.Field].Values)
                    .Select(ValueKey).Where(value => value is not null).Cast<string>()
                    .Distinct(StringComparer.Ordinal).ToImmutableArray();
                var values = !scale.CategoryOrder.IsDefaultOrEmpty
                    ? scale.CategoryOrder
                    : !inferred.IsDefaultOrEmpty ? inferred : categories;
                yield return new ResolvedScale(scale.Id, scale.Channel, scale.Kind,
                    values.Select(ChartValue.From).ToImmutableArray(), values,
                    values.Select(value => new PlotTick(ChartValue.From(value), value)).ToImmutableArray(), scale.IncludeZero);
                continue;
            }

            var raw = bindings.SelectMany(binding => columns[binding.Field].Values).Where(value => value.Kind != ChartValueKind.Null).ToList();
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
            if (scale.Kind == ScaleKind.Logarithmic && (minimum <= 0m || maximum <= 0m || numbers.Any(number => number <= 0m)))
                throw new InvalidOperationException($"Logarithmic scale '{scale.Id}' requires positive values and domain bounds.");
            if (scale.IncludeZero) { minimum = Math.Min(0m, minimum); maximum = Math.Max(0m, maximum); }
            if (minimum == maximum) maximum = minimum + 1m;
            var ticks = scale.Kind == ScaleKind.Logarithmic
                ? Enumerable.Range(0, 5).Select(index => (decimal)Math.Pow(10d,
                    Math.Log10((double)minimum) + (Math.Log10((double)maximum) - Math.Log10((double)minimum)) * index / 4d)).ToList()
                : Enumerable.Range(0, 5).Select(index => minimum + ((maximum - minimum) * index / 4m)).ToList();
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

            var layout = layer.Style.FirstOrDefault(token => token.Name.Equals("layout", StringComparison.OrdinalIgnoreCase))?.Value;
            if (layout == "boxplot")
            {
                yield return ResolveBoxPlot(layer, spec, data, columns, categories);
                continue;
            }
            if (layout == "waterfall")
            {
                yield return ResolveWaterfall(layer, spec, data, columns);
                continue;
            }
            if (layout == "radar")
            {
                foreach (var radarLayer in ResolveRadar(layer, spec, data, columns, series)) yield return radarLayer;
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

    private static ResolvedMarkLayer ResolveBoxPlot(
        MarkLayerSpec layer,
        ChartSpec spec,
        ChartDataSet chartData,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories)
    {
        // BOXPLOT supports both the ordinary X/Y form (the engine computes the
        // five-number summary) and the documented pre-calculated form used by
        // the kitchen sink (X, LOW, Q1, MEDIAN, Q3, HIGH). Preserve the latter
        // verbatim; there is no raw Y column to summarise in that form.
        if (!spec.Bindings.Any(binding => binding.Channel == FieldChannel.Y))
        {
            var resolved = ResolveLayerData(layer, spec, chartData, columns, categories, _ => true);
            return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, spec.Id, resolved)
            { Style = layer.Style };
        }
        var xBinding = spec.Bindings.First(binding => binding.Channel == FieldChannel.X);
        var yBinding = spec.Bindings.First(binding => binding.Channel == FieldChannel.Y);
        var xColumn = columns[xBinding.Field];
        var yColumn = columns[yBinding.Field];
        var resolvedData = categories.Select((category, categoryIndex) =>
        {
            var rowIndices = Enumerable.Range(0, xColumn.Values.Length)
                .Where(index => ValueKey(xColumn.Values[index]) == category).ToList();
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
        return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, spec.Id, resolvedData) { Style = layer.Style };
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
        IReadOnlyDictionary<string, ChartColumn> columns)
    {
        var raw = ResolveLayerData(layer, spec, data, columns, [], _ => true);
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
        return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, spec.Id, output) { Style = layer.Style };
    }

    private static IEnumerable<ResolvedMarkLayer> ResolveRadar(
        MarkLayerSpec layer,
        ChartSpec spec,
        ChartDataSet data,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<ResolvedSeries> series)
    {
        var seriesBinding = spec.Bindings.First(binding => binding.Channel == FieldChannel.Color);
        var metrics = spec.Bindings.Where(binding => binding.Channel == FieldChannel.Detail).ToList();
        for (var rowIndex = 0; rowIndex < data.RowCount; rowIndex++)
        {
            var key = ValueKey(columns[seriesBinding.Field].Values[rowIndex]) ?? $"Series {rowIndex + 1}";
            var points = metrics.Select(binding =>
            {
                var column = columns[binding.Field];
                var value = column.Values[rowIndex];
                return new ResolvedDatum(rowIndex,
                [
                    new ResolvedChannelValue(FieldChannel.Theta, ChartValue.From(binding.Field), binding.Field),
                    new ResolvedChannelValue(FieldChannel.Radius, value,
                        column.DisplayValues.IsDefaultOrEmpty ? Display(value) : column.DisplayValues[rowIndex])
                ], value.Kind == ChartValueKind.Null, $"{binding.Field}: {Display(value)}");
            }).ToImmutableArray();
            yield return new ResolvedMarkLayer($"{layer.Id}-{rowIndex:D2}", layer.Mark, layer.ZIndex + rowIndex, key, points)
            { Style = layer.Style };
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
        var layerBindings = layer.Bindings.IsDefaultOrEmpty ? spec.Bindings : layer.Bindings
            .AddRange(spec.Bindings.Where(binding => binding.Channel is FieldChannel.Row or FieldChannel.Column &&
                !layer.Bindings.Any(existing => existing.Channel == binding.Channel && existing.Field.Equals(binding.Field, StringComparison.OrdinalIgnoreCase))));
        var categoryBinding = layerBindings.FirstOrDefault(binding => binding.Channel is FieldChannel.X or FieldChannel.Theta);
        var rows = new List<ResolvedDatum>();
        var preserveRows = layer.Style.Any(token => token.Name.Equals("preserveRows", StringComparison.OrdinalIgnoreCase)
            && token.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        if (!preserveRows && categoryBinding is not null && categoryBinding.SemanticKind != DataSemanticKind.Quantitative &&
            columns.TryGetValue(categoryBinding.Field, out var categoryColumn) && !categories.IsDefaultOrEmpty)
        {
            var facetBindings = layerBindings.Where(binding => binding.Channel is FieldChannel.Row or FieldChannel.Column)
                .Where(binding => columns.ContainsKey(binding.Field)).ToList();
            if (facetBindings.Count > 0)
            {
                var facetKeys = Enumerable.Range(0, data.RowCount).Where(include)
                    .Select(index => string.Join("\u001f", facetBindings.Select(binding => ValueKey(columns[binding.Field].Values[index]) ?? string.Empty)))
                    .Distinct(StringComparer.Ordinal).ToList();
                foreach (var facetKey in facetKeys)
                    foreach (var category in categories)
                    {
                        var rowIndex = Enumerable.Range(0, data.RowCount).FirstOrDefault(index => include(index)
                            && ValueKey(categoryColumn.Values[index]) == category
                            && string.Join("\u001f", facetBindings.Select(binding => ValueKey(columns[binding.Field].Values[index]) ?? string.Empty)) == facetKey, -1);
                        if (rowIndex >= 0) rows.Add(Datum(rowIndex, layerBindings, columns, spec.NullHandling, layer.Conditions));
                    }
                return rows.ToImmutableArray();
            }
            for (var categoryIndex = 0; categoryIndex < categories.Length; categoryIndex++)
            {
                var category = categories[categoryIndex];
                var rowIndex = Enumerable.Range(0, data.RowCount).FirstOrDefault(index => include(index) && ValueKey(categoryColumn.Values[index]) == category, -1);
                rows.Add(rowIndex < 0
                    ? GapDatum(categoryIndex, layerBindings, categoryBinding, category)
                    : Datum(rowIndex, layerBindings, columns, spec.NullHandling, layer.Conditions));
            }
            return rows.ToImmutableArray();
        }

        for (var rowIndex = 0; rowIndex < data.RowCount; rowIndex++)
            if (include(rowIndex)) rows.Add(Datum(rowIndex, layerBindings, columns, spec.NullHandling, layer.Conditions));
        return rows.ToImmutableArray();
    }

    private static ResolvedDatum Datum(
        int rowIndex,
        ImmutableArray<FieldBinding> bindings,
        IReadOnlyDictionary<string, ChartColumn> columns,
        NullHandlingSpec nulls,
        ImmutableArray<EncodingConditionSpec> conditions)
    {
        var channels = bindings.Where(binding => columns.ContainsKey(binding.Field)).Select(binding =>
        {
            var column = columns[binding.Field];
            return new ResolvedChannelValue(binding.Channel, column.Values[rowIndex],
                column.DisplayValues.IsDefaultOrEmpty ? null : column.DisplayValues[rowIndex]);
        }).ToImmutableArray();
        var requiredNull = channels.Any(channel =>
            (channel.Channel is FieldChannel.X or FieldChannel.X2 or FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or
                FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or FieldChannel.Q3 or FieldChannel.High or FieldChannel.Open or FieldChannel.Close)
            && channel.Value.Kind == ChartValueKind.Null);
        return new ResolvedDatum(rowIndex, channels, requiredNull && nulls.Default == NullValuePolicy.Gap,
            string.Join(", ", channels.Select(channel => $"{channel.Channel}: {channel.DisplayValue ?? Display(channel.Value)}")))
        {
            Encodings = ResolveConditions(conditions, rowIndex, columns)
        };
    }

    private static ResolvedDatum GapDatum(int index, ImmutableArray<FieldBinding> bindings, FieldBinding categoryBinding, string category)
    {
        var channels = bindings.Select(binding => new ResolvedChannelValue(binding.Channel,
            binding == categoryBinding ? ChartValue.From(category) : ChartValue.Null(),
            binding == categoryBinding ? category : null)).ToImmutableArray();
        return new ResolvedDatum(index, channels, true, null);
    }

    private static ImmutableArray<ResolvedEncodingValue> ResolveConditions(
        ImmutableArray<EncodingConditionSpec> conditions,
        int rowIndex,
        IReadOnlyDictionary<string, ChartColumn> columns)
    {
        if (conditions.IsDefaultOrEmpty) return [];
        var result = new List<ResolvedEncodingValue>();
        foreach (var group in conditions.GroupBy(condition => condition.Channel))
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
        var sourceLayers = layers.Where(layer => layer.Mark is not MarkKind.Rule).ToList();
        var total = sourceLayers.Where(layer => layer.Mark == MarkKind.Arc).SelectMany(layer => layer.Data)
            .Select(datum => datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.Radius or FieldChannel.Y))
            .Where(value => value is not null).Sum(value => Math.Max(0m, Number(value!.Value) ?? 0m));
        var items = sourceLayers.SelectMany((layer, layerIndex) => layer.Data.Select((datum, index) =>
        {
            var label = datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.X or FieldChannel.Theta)?.DisplayValue
                ?? (index < categories.Length ? categories[index] : $"Row {index + 1}");
            var value = datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or
                FieldChannel.Median or FieldChannel.Close or FieldChannel.Size or FieldChannel.YEnd);
            var numeric = value is null ? null : Number(value.Value);
            var conditionDetail = datum.Encodings.IsDefaultOrEmpty ? null : string.Join(", ", datum.Encodings.Select(encoding =>
                $"conditional {encoding.Channel}: {Display(encoding.Value)}"));
            return new SemanticFallbackItem(label ?? $"Row {index + 1}", datum.IsGap ? "gap" : value is null ? "" : value.DisplayValue ?? Display(value.Value), (layerIndex * 100000) + index)
            {
                Group = layer.SeriesKey,
                Detail = datum.IsGap ? "null gap" : conditionDetail ?? (layer.Mark == MarkKind.Arc && numeric.HasValue && total > 0m
                    ? $"{numeric.Value / total:P1} of total"
                    : null)
            };
        })).Concat(layers.Where(layer => layer.Mark == MarkKind.Rule).Select((layer, index) =>
        {
            var value = layer.Data.SelectMany(datum => datum.Channels)
                .FirstOrDefault(channel => channel.Channel is FieldChannel.Y or FieldChannel.X);
            var label = layer.Style.FirstOrDefault(token => token.Name.Equals("label", StringComparison.OrdinalIgnoreCase))?.Value
                ?? layer.Style.FirstOrDefault(token => token.Name == "overlayType")?.Value ?? layer.Id;
            return new SemanticFallbackItem(label, value is null ? "" : value.DisplayValue ?? Display(value.Value), ((sourceLayers.Count + 1) * 100000) + index)
            { Detail = "labeled reference rule", Group = "Reference" };
        })).ToImmutableArray();
        return new SemanticFallback(
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
        PlotBounds bounds)
    {
        if (spec.Facet is null) return [];
        var rowValues = FacetValues(spec.Facet.RowField, columns);
        var columnValues = FacetValues(spec.Facet.ColumnField, columns);
        if (rowValues.Count == 0) rowValues.Add(null);
        if (columnValues.Count == 0) columnValues.Add(null);
        var panels = new List<ResolvedFacetPanel>();
        var panelWidth = bounds.Width / columnValues.Count;
        var panelHeight = bounds.Height / rowValues.Count;
        for (var rowIndex = 0; rowIndex < rowValues.Count; rowIndex++)
            for (var columnIndex = 0; columnIndex < columnValues.Count; columnIndex++)
            {
                var rowLabel = rowValues[rowIndex];
                var columnLabel = columnValues[columnIndex];
                var indices = Enumerable.Range(0, columns.Values.FirstOrDefault()?.Values.Length ?? 0)
                    .Where(index => MatchesFacet(spec.Facet.RowField, rowLabel, columns, index) &&
                        MatchesFacet(spec.Facet.ColumnField, columnLabel, columns, index))
                    .ToImmutableArray();
                if (indices.IsDefaultOrEmpty) continue;
                var scales = globalScales.Select(scale => Independent(scale, spec.Facet.Resolution, spec, columns, indices)).ToImmutableArray();
                panels.Add(new ResolvedFacetPanel(
                    $"facet-{rowIndex:D2}-{columnIndex:D2}", rowLabel, columnLabel,
                    new PlotBounds(bounds.X + columnIndex * panelWidth, bounds.Y + rowIndex * panelHeight, panelWidth, panelHeight),
                    indices, scales));
            }
        return panels.ToImmutableArray();
    }

    private static List<string?> FacetValues(string? field, IReadOnlyDictionary<string, ChartColumn> columns) =>
        field is null || !columns.TryGetValue(field, out var column)
            ? []
            : column.Values.Select(ValueKey).Distinct(StringComparer.Ordinal).Cast<string?>().ToList();

    private static bool MatchesFacet(string? field, string? value, IReadOnlyDictionary<string, ChartColumn> columns, int index) =>
        field is null || columns.TryGetValue(field, out var column) && ValueKey(column.Values[index]) == value;

    private static ResolvedScale Independent(
        ResolvedScale scale,
        ScaleResolutionSpec resolution,
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<int> rows)
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
            .Where(binding => binding.ScaleId == scale.Id && columns.ContainsKey(binding.Field)).ToList();
        if (scale.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal)
        {
            var categories = bindings.SelectMany(binding => rows.Select(index => ValueKey(columns[binding.Field].Values[index])))
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
            var temporal = bindings.SelectMany(binding => rows.Select(index => columns[binding.Field].Values[index]))
                .Where(value => value.Kind != ChartValueKind.Null)
                .GroupBy(ValueKey, StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(ValueKey, StringComparer.Ordinal).ToImmutableArray();
            return scale with
            {
                Domain = temporal,
                Categories = temporal.Select(Display).ToImmutableArray(),
                Ticks = temporal.Select(value => new PlotTick(value, Display(value))).ToImmutableArray()
            };
        }
        var values = bindings.SelectMany(binding => rows.Select(index => Number(columns[binding.Field].Values[index])))
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
        return scale with
        {
            Domain = [ChartValue.From(minimum), ChartValue.From(maximum)],
            Ticks = ticks.Select(value => new PlotTick(ChartValue.From(value), value.ToString("0.##", CultureInfo.InvariantCulture))).ToImmutableArray()
        };
    }

    private static string BuildSummary(ChartSpec spec, ChartDataSet data, ImmutableArray<ResolvedSeries> series,
        ImmutableArray<ResolvedMarkLayer> layers, ImmutableArray<ResolvedFacetPanel> facets,
        ImmutableArray<int> gaps, ImmutableArray<int> skipped) =>
        $"{spec.Title ?? spec.Id}: {data.RowCount} rows, {layers.Length} ordered layers, {series.Length} series, " +
        $"{(facets.IsDefaultOrEmpty ? 1 : facets.Length)} facet panels, {gaps.Length} gaps, {skipped.Length} skipped rows.";

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

    private static ChartValue? Channel(ResolvedDatum datum, FieldChannel channel) =>
        datum.Channels.FirstOrDefault(item => item.Channel == channel)?.Value;

    private static string? ValueKey(ChartValue value) => value.Kind == ChartValueKind.Null ? null : Display(value);
}
