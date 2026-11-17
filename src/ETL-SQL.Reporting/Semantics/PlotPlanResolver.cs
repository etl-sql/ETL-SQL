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
        var independent = ResolveIndependent(spec, data, geography);
        return ResolveBounds(spec, data, independent, bounds ?? new PlotBounds(0, 0, 600, 350));
    }

    /// <summary>Recomputes only geometry that depends on the requested container bounds.</summary>
    public PlotPlan Relayout(ChartSpec spec, ChartDataSet data, PlotPlan plan, PlotBounds bounds)
    {
        spec.Validate();
        data.Validate();
        plan.Validate();
        var columns = data.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var formatter = new ChartValueFormatter(spec.Formatting);
        var facets = ResolveFacets(spec, columns, plan.Scales, bounds, formatter);
        var layers = ResolveDisplayOffsets(spec, data, columns, plan.Layers, plan.Scales, plan.Series, facets, bounds, plan.Legend.Length);
        layers = ResolveMarkExtents(spec, layers);
        var summary = BuildSummary(spec, data, plan.Series, layers, plan.Scales, facets,
            plan.Nulls.GapRows, plan.Nulls.SkippedRows);
        var resolved = plan with
        {
            Bounds = bounds,
            Layers = layers,
            AccessibleSummary = summary,
            Facets = facets,
            CartesianViewport = ResolveCartesianViewport(spec.Coordinate, plan.Scales, bounds, plan.CartesianAxes)
        };
        resolved.Validate();
        return resolved;
    }

    private static BoundsIndependentPlan ResolveIndependent(
        ChartSpec spec,
        ChartDataSet data,
        ResolvedGeographicGeometry? geography)
    {
        spec.Validate();
        data.Validate();
        var columns = data.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var formatter = new ChartValueFormatter(spec.Formatting);
        var categories = ResolveCategories(spec, columns);
        var seriesKeys = ResolveSeries(spec, columns, categories);
        var seriesComparer = spec.IsConnectedComposition ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var distinctSortedSeries = seriesKeys.Distinct(seriesComparer).OrderBy(k => k, seriesComparer).ToList();
        var series = seriesKeys.Select((key, _) =>
        {
            var identityIndex = distinctSortedSeries.IndexOf(key);
            if (identityIndex < 0) identityIndex = 0;
            return new ResolvedSeries(key, spec.IsConnectedComposition ? CompositionSeriesLabel(spec, key) : key, identityIndex, ResolveColor(spec, key, identityIndex));
        }).OrderBy(s => s.Order).ThenBy(s => s.Key, StringComparer.Ordinal).ToImmutableArray();
        var palette = series.Select(item => new PaletteAssignment(item.Key, item.Color)).ToImmutableArray();
        var legend = series.Select(item => new LegendEntry(item.Key, item.Label, item.Order, item.Color)).ToImmutableArray();
        var layers = ResolveLayers(spec, data, columns, categories, series, formatter).ToImmutableArray();
        if (spec.IsConnectedComposition)
            layers = layers.OrderBy(layer => layer.ZIndex).ThenBy(layer => layer.Id, StringComparer.Ordinal).ToImmutableArray();
        if (layers.Any(layer => !layer.Connections.IsDefault && ResolvedMarkConnection.FillsNullsWithZero(layer)))
            layers = ConnectedMarkResolver.ResolveZeroGeometry(layers);
        if (layers.Any(layer => layer.ConnectionDecorations))
            layers = ConnectedMarkResolver.ResolveDecorations(layers, spec.Theme.Tokens);
        var radial = spec.Coordinate.Kind == CoordinateKind.Polar && layers.Any(layer => layer.Stack != StackMode.None);
        layers = radial ? RadialStackResolver.Resolve(spec, layers.Select((layer, index) => layer with { ZIndex = index }).ToImmutableArray()) : ResolveStacking(layers);
        var axes = spec.ResolveCartesianAxes();
        var scaleSpec = spec.Layers.Any(layer => (layer.Mark == MarkKind.Area && (!layer.Conditions.IsDefaultOrEmpty || spec.IsTransposedAreaAspect || spec.IsTransposedConnected) || layer.Mark == MarkKind.Line && !layer.Conditions.IsDefaultOrEmpty && layer.Style.Any(token => token.Name.Equals("nullHandling", StringComparison.OrdinalIgnoreCase) && token.Value.Equals("ZERO", StringComparison.OrdinalIgnoreCase))) && layer.Bindings.Any(binding => binding.Channel == FieldChannel.Y))
            ? spec with { Scales = spec.Scales.Select(scale => scale.Channel == FieldChannel.Y && (axes is null || scale.Id.Equals(axes.YScaleId, StringComparison.OrdinalIgnoreCase)) ? scale with { IncludeZero = true } : scale).ToImmutableArray() }
            : spec;
        var scales = ResolveScales(scaleSpec, columns, categories, layers, formatter).ToImmutableArray();
        if (radial)
        {
            var maximum = layers.SelectMany(layer => layer.Data).Select(datum => datum.RadialInterval?.Maximum ?? 1m).DefaultIfEmpty(1m).Max();
            scales = scales.Select(scale => scale.Channel != FieldChannel.Radius ? scale : scale with
            {
                Domain = [ChartValue.From(0m), ChartValue.From(maximum)],
                IncludesZero = true,
                Ticks = Enumerable.Range(0, 5).Select(index => new PlotTick(ChartValue.From(maximum * index / 4m), (maximum * index / 4m).ToString(CultureInfo.InvariantCulture))).ToImmutableArray()
            }).ToImmutableArray();
        }
        var syncAxes = spec.Theme.Tokens.Any(token =>
            token.Name.Equals("SYNC_AXES", StringComparison.OrdinalIgnoreCase) &&
            (token.Value.Equals("ON", StringComparison.OrdinalIgnoreCase) || token.Value.Equals("TRUE", StringComparison.OrdinalIgnoreCase))) ||
            layers.Any(l => l.Style.Any(t =>
                t.Name.Equals("SYNC_AXES", StringComparison.OrdinalIgnoreCase) &&
                (t.Value.Equals("ON", StringComparison.OrdinalIgnoreCase) || t.Value.Equals("TRUE", StringComparison.OrdinalIgnoreCase))));

        if (syncAxes)
        {
            int yIdx = -1, y2Idx = -1;
            for (int i = 0; i < scales.Length; i++)
            {
                if (scales[i].Channel == FieldChannel.Y) yIdx = i;
                else if (scales[i].Channel == FieldChannel.Y2) y2Idx = i;
            }
            if (yIdx >= 0 && y2Idx >= 0)
            {
                var y1 = scales[yIdx];
                var y2 = scales[y2Idx];
                if (y1.Domain.Length >= 2 && y2.Domain.Length >= 2 &&
                    y1.Kind is ScaleKind.Linear or ScaleKind.Logarithmic &&
                    y2.Kind is ScaleKind.Linear or ScaleKind.Logarithmic)
                {
                    var minY1 = Number(y1.Domain[0]) ?? 0m;
                    var maxY1 = Number(y1.Domain[^1]) ?? 1m;
                    var minY2 = Number(y2.Domain[0]) ?? 0m;
                    var maxY2 = Number(y2.Domain[^1]) ?? 1m;

                    var minDomain = Math.Min(minY1, minY2);
                    var maxDomain = Math.Max(maxY1, maxY2);
                    if (minDomain == maxDomain) maxDomain = minDomain + 1m;

                    var tickCount = Math.Max(y1.Ticks.Length, y2.Ticks.Length);
                    if (tickCount < 2) tickCount = 5;

                    var tickVals = Enumerable.Range(0, tickCount)
                        .Select(i => minDomain + ((maxDomain - minDomain) * i / (tickCount - 1m)))
                        .ToList();

                    var newY1Ticks = tickVals.Select(v => new PlotTick(ChartValue.From(v), y1.TickFormat is not null ? formatter.Number(v, y1.TickFormat) : formatter.Number(v))).ToImmutableArray();
                    var newY2Ticks = tickVals.Select(v => new PlotTick(ChartValue.From(v), y2.TickFormat is not null ? formatter.Number(v, y2.TickFormat) : formatter.Number(v))).ToImmutableArray();

                    var updatedY1 = y1 with
                    {
                        Domain = [ChartValue.From(minDomain), ChartValue.From(maxDomain)],
                        Ticks = newY1Ticks
                    };
                    var updatedY2 = y2 with
                    {
                        Domain = [ChartValue.From(minDomain), ChartValue.From(maxDomain)],
                        Ticks = newY2Ticks
                    };

                    var builder = scales.ToBuilder();
                    builder[yIdx] = updatedY1;
                    builder[y2Idx] = updatedY2;
                    scales = builder.ToImmutable();
                }
            }
        }
        layers = ResolveMarkExtents(spec, layers);
        // One pass over the source layers. `sourceLayers` was a lazy query enumerated twice, and
        // `gapRows.Contains` on an ImmutableArray made the skipped-row scan O(rows x gap rows).
        var gapRowSet = new HashSet<int>();
        var usedRows = new HashSet<int>();
        var conditionedConnection = spec.Layers.Any(layer => layer.Mark is (MarkKind.Line or MarkKind.Area) && !layer.Conditions.IsDefaultOrEmpty);
        foreach (var layer in layers)
        {
            if (!layer.Style.IsDefault && layer.Style.Any(token => token.Name == "overlayType")) continue;
            var connect = conditionedConnection && !layer.Connections.IsDefault && ResolvedMarkConnection.ConnectsAcrossNulls(layer);
            foreach (var datum in layer.Data)
            {
                if (connect && !ResolvedMarkConnection.HasCompleteCoordinates(datum, layer.AreaRibbon)) continue;
                (datum.IsGap ? gapRowSet : usedRows).Add(datum.RowIndex);
            }
        }
        var gapRows = gapRowSet.Order().ToImmutableArray();
        var skippedRows = Enumerable.Range(0, data.RowCount)
            .Where(index => !usedRows.Contains(index) && !gapRowSet.Contains(index)).ToImmutableArray();
        var fallback = BuildFallback(spec, layers, categories, formatter);
        var interaction = ChartInteractionResolver.Resolve(
            spec,
            data.Columns.Select(column => column.Name).ToArray(),
            ChartInteractionResolver.HighlightFor(layers));

        return new BoundsIndependentPlan(columns, formatter, series, palette, legend, layers, scales,
            new ResolvedNullPolicy(spec.NullHandling.Default, spec.NullHandling.Fields, gapRows, skippedRows),
            fallback, interaction, geography);
    }

    private static PlotPlan ResolveBounds(
        ChartSpec spec,
        ChartDataSet data,
        BoundsIndependentPlan independent,
        PlotBounds bounds)
    {
        var facets = ResolveFacets(spec, independent.Columns, independent.Scales, bounds, independent.Formatter);
        var layers = ResolveDisplayOffsets(spec, data, independent.Columns, independent.Layers,
            independent.Scales, independent.Series, facets, bounds, independent.Legend.Length);
        if (spec.HasOrdinaryInterpolation)
            layers = layers.Select(layer => layer.Mark is (MarkKind.Line or MarkKind.Area) && layer.Connections.IsDefault
                ? layer with { PathInterpolation = ResolvedConnectionGeometry.Kind(layer.Style) } : layer).ToImmutableArray();
        var summary = BuildSummary(spec, data, independent.Series, layers, independent.Scales, facets,
            independent.Nulls.GapRows, independent.Nulls.SkippedRows);

        var plan = PlotPlan.Create(
            spec.Id,
            bounds,
            independent.Scales,
            independent.Series,
            independent.Palette,
            independent.Legend,
            layers,
            independent.Nulls,
            summary,
            independent.Fallback,
            spec.Title,
            spec.Coordinate,
            spec.Theme.Tokens,
            facets) with
        {
            CartesianAxes = spec.ResolveCartesianAxes(),
            CartesianViewport = ResolveCartesianViewport(spec.Coordinate, independent.Scales, bounds, spec.ResolveCartesianAxes()),
            Interaction = independent.Interaction,
            Geography = independent.Geography
        };
        if (layers.Any(layer => layer.Mark == MarkKind.Arc && layer.Stack != StackMode.None))
            plan = plan with { Schema = ChartContractVersions.RadialPlotPlanSchema, Version = ChartContractVersions.RadialPlotPlanVersion };
        if (layers.Any(layer => layer.AreaRibbonScaleId is not null))
            plan = plan with { Schema = ChartContractVersions.ScaledRibbonPlotPlanSchema, Version = ChartContractVersions.ScaledRibbonPlotPlanVersion };
        if (spec.IsTransposedAreaAspect)
            plan = plan with { Schema = ChartContractVersions.TransposedAreaPlotPlanSchema, Version = ChartContractVersions.TransposedAreaPlotPlanVersion };
        if (spec.Layers.Any(layer => layer.Mark is (MarkKind.Line or MarkKind.Area) && !layer.Conditions.IsDefaultOrEmpty))
            plan = ConnectedMarkResolver.Attach(plan);
        if (spec.HasOrdinaryInterpolation)
            plan = plan with { Schema = ChartContractVersions.OrdinaryInterpolationPlotPlanSchema, Version = ChartContractVersions.OrdinaryInterpolationPlotPlanVersion };
        if (spec.HasOrdinaryAreaInterpolation)
            plan = plan with { Schema = ChartContractVersions.OrdinaryAreaInterpolationPlotPlanSchema, Version = ChartContractVersions.OrdinaryAreaInterpolationPlotPlanVersion };
        if (spec.HasTransposedConfidence)
            plan = plan with { Schema = ChartContractVersions.TransposedConfidencePlotPlanSchema, Version = ChartContractVersions.TransposedConfidencePlotPlanVersion };
        plan.Validate();
        return plan;
    }

    private sealed record BoundsIndependentPlan(
        IReadOnlyDictionary<string, ChartColumn> Columns,
        ChartValueFormatter Formatter,
        ImmutableArray<ResolvedSeries> Series,
        ImmutableArray<PaletteAssignment> Palette,
        ImmutableArray<LegendEntry> Legend,
        ImmutableArray<ResolvedMarkLayer> Layers,
        ImmutableArray<ResolvedScale> Scales,
        ResolvedNullPolicy Nulls,
        SemanticFallback Fallback,
        ResolvedInteraction? Interaction,
        ResolvedGeographicGeometry? Geography);

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
        var axisSort = spec.Theme.Tokens.FirstOrDefault(token => token.Name.Equals("X_SORT", StringComparison.OrdinalIgnoreCase) || token.Name.Equals("AXIS_SORT", StringComparison.OrdinalIgnoreCase) || token.Name.Equals("SORT", StringComparison.OrdinalIgnoreCase))?.Value.ToUpperInvariant();
        if (axisSort is "VALUE" or "VALUE_DESC")
        {
            var measureBinding = spec.Bindings.FirstOrDefault(item => item.SemanticKind == DataSemanticKind.Quantitative && item.Channel is FieldChannel.Y or FieldChannel.Radius or FieldChannel.Size)
                ?? spec.Bindings.FirstOrDefault(item => item.Channel is FieldChannel.Size or FieldChannel.Y or FieldChannel.Radius);
            if (measureBinding?.Field is { } measureField && columns.TryGetValue(measureField, out var measure))
            {
                var sums = categories.ToDictionary(category => category, _ => 0m, StringComparer.Ordinal);
                for (var index = 0; index < column.Values.Length; index++)
                {
                    var category = ValueKey(column.Values[index]);
                    if (category is not null && sums.ContainsKey(category)) sums[category] += Number(measure.Values[index]) ?? 0m;
                }
                categories = categories.OrderByDescending(category => sums[category]).ThenBy(category => category, StringComparer.Ordinal).ToList();
            }
        }
        else if (axisSort is "VALUE_ASC")
        {
            var measureBinding = spec.Bindings.FirstOrDefault(item => item.SemanticKind == DataSemanticKind.Quantitative && item.Channel is FieldChannel.Y or FieldChannel.Radius or FieldChannel.Size)
                ?? spec.Bindings.FirstOrDefault(item => item.Channel is FieldChannel.Size or FieldChannel.Y or FieldChannel.Radius);
            if (measureBinding?.Field is { } measureField && columns.TryGetValue(measureField, out var measure))
            {
                var sums = categories.ToDictionary(category => category, _ => 0m, StringComparer.Ordinal);
                for (var index = 0; index < column.Values.Length; index++)
                {
                    var category = ValueKey(column.Values[index]);
                    if (category is not null && sums.ContainsKey(category)) sums[category] += Number(measure.Values[index]) ?? 0m;
                }
                categories = categories.OrderBy(category => sums[category]).ThenBy(category => category, StringComparer.Ordinal).ToList();
            }
        }
        else if (axisSort is "ALPHA" || binding.Sort == SortDirection.Ascending)
        {
            categories.Sort(StringComparer.OrdinalIgnoreCase);
        }
        else if (axisSort is "SOURCE")
        {
            // Keep distinct values in source encounter order
        }
        else if (binding.Sort == SortDirection.Descending)
        {
            categories.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(right, left));
        }
        return categories.ToImmutableArray();
    }

    private static ImmutableArray<string> ResolveSeries(
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories)
    {
        if (spec.IsConnectedComposition)
            return spec.Layers.SelectMany(layer =>
            {
                var colorBinding = layer.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color && binding.SemanticKind is DataSemanticKind.Nominal or DataSemanticKind.Ordinal);
                if (colorBinding?.Field is { } field && columns.TryGetValue(field, out var column))
                    return column.Values.Select(CompositionSeriesKey);
                if (colorBinding?.SourceKind == BindingSourceKind.Datum && colorBinding.Constant is { } constant)
                    return [CompositionSeriesKey(constant)];
                return layer.Mark == MarkKind.Rule ? [] : new[] { "layer:" + layer.Id };
            }).Distinct(StringComparer.Ordinal).ToImmutableArray();
        if (spec.Coordinate.Kind == CoordinateKind.Polar && !spec.Layers.Any(layer => layer.Bindings.Any(binding => binding.Stack != StackMode.None)) && spec.Bindings.Any(binding => binding.Channel == FieldChannel.Theta)) return categories;

        var collected = new List<string>();
        foreach (var layer in spec.Layers)
        {
            if (layer.Style.Any(token => token.Name == "overlayType")) continue;
            var layerColor = layer.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color);
            if (layerColor is not null && layerColor.SemanticKind is not DataSemanticKind.Quantitative)
            {
                if (layerColor.Field is { } layerField && columns.TryGetValue(layerField, out var layerColumn))
                {
                    collected.AddRange(layerColumn.Values.Select(ValueKey).Where(value => value is not null).Cast<string>());
                    continue;
                }
                if (layerColor.SourceKind == BindingSourceKind.Datum && layerColor.Constant is { } datumConstant && ValueKey(datumConstant) is { } datumKey)
                {
                    collected.Add(datumKey);
                    continue;
                }
            }
            if (layer.Mark is not MarkKind.Rule &&
                layer.Style.FirstOrDefault(token => token.Name == "series") is { } seriesToken)
            {
                collected.Add(seriesToken.Value);
            }
        }
        if (collected.Count > 0)
            return collected.Distinct(StringComparer.Ordinal).ToImmutableArray();

        foreach (var color in spec.Bindings.Where(binding => binding.Channel == FieldChannel.Color && binding.SemanticKind is not DataSemanticKind.Quantitative))
        {
            if (color.Field is { } colorField && columns.TryGetValue(colorField, out var colorColumn))
                collected.AddRange(colorColumn.Values.Select(ValueKey).Where(value => value is not null).Cast<string>());
            else if (color.SourceKind == BindingSourceKind.Datum && color.Constant is { } constant && ValueKey(constant) is { } constantKey)
                collected.Add(constantKey);
        }
        if (collected.Count > 0)
            return collected.Distinct(StringComparer.Ordinal).ToImmutableArray();

        var measure = spec.Bindings.FirstOrDefault(binding => binding.Channel is FieldChannel.Y or FieldChannel.Radius);
        return [measure?.Field ?? spec.Id];
    }

    private static string CompositionSeriesKey(ChartValue value) => value.Kind == ChartValueKind.Null ? "null:" : "value:" + ValueKey(value);

    private static string CompositionSeriesLabel(ChartSpec spec, string key) => key.StartsWith("value:", StringComparison.Ordinal) ? key[6..]
        : key == "null:" ? "(null)" : spec.Layers.First(layer => "layer:" + layer.Id == key).Style.FirstOrDefault(token => token.Name == "series")?.Value ?? key[6..];

    private static IEnumerable<ResolvedScale> ResolveScales(
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories,
        ImmutableArray<ResolvedMarkLayer> resolvedLayers,
        ChartValueFormatter formatter)
    {
        foreach (var scale in spec.Scales)
        {
            // COMPAT_BREAK: 0.20.0 — scale IDs have the same case-insensitive identity as validation.
            var bindings = spec.Bindings.Concat(spec.Layers.SelectMany(layer => layer.Bindings))
                .Where(binding => string.Equals(binding.ScaleId, scale.Id, StringComparison.OrdinalIgnoreCase) &&
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

                if (scale.Channel == FieldChannel.Y)
                {
                    var ySort = spec.Theme.Tokens.FirstOrDefault(token => token.Name.Equals("Y_SORT", StringComparison.OrdinalIgnoreCase))?.Value.ToUpperInvariant();
                    if (ySort is not null)
                    {
                        var yList = values.ToList();
                        if (ySort is "VALUE" or "VALUE_DESC")
                        {
                            var yBinding = spec.Bindings.FirstOrDefault(item => item.Channel == FieldChannel.Y);
                            var measureBinding = spec.Bindings.FirstOrDefault(item => item.SemanticKind == DataSemanticKind.Quantitative && item.Channel is FieldChannel.Size or FieldChannel.X or FieldChannel.Radius)
                                ?? spec.Bindings.FirstOrDefault(item => item.Channel is FieldChannel.Size or FieldChannel.Radius);
                            if (yBinding?.Field is { } yField && columns.TryGetValue(yField, out var yCol) &&
                                measureBinding?.Field is { } measureField && columns.TryGetValue(measureField, out var measure))
                            {
                                var sums = yList.ToDictionary(c => c, _ => 0m, StringComparer.Ordinal);
                                for (var index = 0; index < yCol.Values.Length; index++)
                                {
                                    var cat = ValueKey(yCol.Values[index]);
                                    if (cat is not null && sums.ContainsKey(cat))
                                    {
                                        sums[cat] += Number(measure.Values[index]) ?? 0m;
                                    }
                                }
                                values = yList.OrderByDescending(c => sums[c]).ThenBy(c => c, StringComparer.Ordinal).ToImmutableArray();
                            }
                        }
                        else if (ySort is "VALUE_ASC")
                        {
                            var yBinding = spec.Bindings.FirstOrDefault(item => item.Channel == FieldChannel.Y);
                            var measureBinding = spec.Bindings.FirstOrDefault(item => item.SemanticKind == DataSemanticKind.Quantitative && item.Channel is FieldChannel.Size or FieldChannel.X or FieldChannel.Radius)
                                ?? spec.Bindings.FirstOrDefault(item => item.Channel is FieldChannel.Size or FieldChannel.Radius);
                            if (yBinding?.Field is { } yField && columns.TryGetValue(yField, out var yCol) &&
                                measureBinding?.Field is { } measureField && columns.TryGetValue(measureField, out var measure))
                            {
                                var sums = yList.ToDictionary(c => c, _ => 0m, StringComparer.Ordinal);
                                for (var index = 0; index < yCol.Values.Length; index++)
                                {
                                    var cat = ValueKey(yCol.Values[index]);
                                    if (cat is not null && sums.ContainsKey(cat))
                                    {
                                        sums[cat] += Number(measure.Values[index]) ?? 0m;
                                    }
                                }
                                values = yList.OrderBy(c => sums[c]).ThenBy(c => c, StringComparer.Ordinal).ToImmutableArray();
                            }
                        }
                        else if (ySort is "ALPHA")
                        {
                            values = yList.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
                        }
                        else if (ySort is "SOURCE")
                        {
                            values = inferred;
                        }
                    }
                }

                yield return new ResolvedScale(scale.Id, scale.Channel, scale.Kind,
                    values.Select(ChartValue.From).ToImmutableArray(), values,
                    values.Select(value => new PlotTick(ChartValue.From(value),
                        scale.TickFormat is not null ? (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var nv) ? formatter.Number(nv, scale.TickFormat) : value) : value)).ToImmutableArray(),
                    scale.IncludeZero,
                    scale.Reverse, scale.MinorTicks, scale.LabelRotation, scale.LabelSkip, scale.OuterPadding,
                    scale.TickFormat, scale.TimeUnit);
                continue;
            }

            var raw = bindings.SelectMany(binding => BindingValues(binding, columns)).Where(value => value.Kind != ChartValueKind.Null).ToList();
            var layout = spec.Layers.Select(layer => layer.Style.FirstOrDefault(token => token.Name.Equals("layout", StringComparison.OrdinalIgnoreCase))?.Value)
                .FirstOrDefault(value => value is not null);
            if ((scale.Channel == FieldChannel.Y || scale.Channel == FieldChannel.X) && layout is "boxplot" or "waterfall")
            {
                var channels = layout == "boxplot"
                    ? new[] { FieldChannel.Low, FieldChannel.Q1, FieldChannel.Median, FieldChannel.Q3, FieldChannel.High, FieldChannel.Mean }
                    : new[] { FieldChannel.YStart, FieldChannel.YEnd, FieldChannel.XStart, FieldChannel.XEnd };
                var boxOrWaterfallRaw = resolvedLayers.SelectMany(layer => layer.Data).SelectMany(datum => datum.Channels)
                    .Where(channel => channels.Contains(channel.Channel) && channel.Value.Kind != ChartValueKind.Null)
                    .Select(channel => channel.Value).ToList();
                if (boxOrWaterfallRaw.Count > 0) raw = boxOrWaterfallRaw;
            }
            if (scale.Channel is FieldChannel.Y or FieldChannel.Y2)
            {
                var axisLayers = resolvedLayers.Where(layer =>
                    LayerTargetsYScale(layer, spec, scale)).ToList();
                if (axisLayers.Any(layer => layer.Stack != StackMode.None))
                {
                    // Stacked baselines replace the raw measure values (NORMALIZE rescales them entirely),
                    // while unstacked layers sharing the axis still own their point/line Y (or Y2) values
                    // alongside ranged channels.
                    var stacked = axisLayers.Where(layer => layer.Stack != StackMode.None)
                        .SelectMany(layer => layer.Data)
                        .SelectMany(datum => datum.Channels)
                        .Where(channel => channel.Channel is FieldChannel.YStart or FieldChannel.YEnd or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh
                            && channel.Value.Kind != ChartValueKind.Null)
                        .Select(channel => channel.Value);
                    var unstacked = axisLayers.Where(layer => layer.Stack == StackMode.None)
                        .SelectMany(layer => layer.Data)
                        .SelectMany(datum => datum.Channels)
                        .Where(channel => (channel.Channel == scale.Channel
                                || channel.Channel is FieldChannel.YStart or FieldChannel.YEnd or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh)
                            && channel.Value.Kind != ChartValueKind.Null)
                        .Select(channel => channel.Value);
                    raw = stacked.Concat(unstacked).ToList();
                }
            }
            if (scale.Kind == ScaleKind.Time)
            {
                var temporalInputs = raw.ToList();
                if (scale.Channel == FieldChannel.X)
                {
                    temporalInputs.AddRange(resolvedLayers
                        .Where(layer => layer.Style.Any(token =>
                            token.Name.Equals("overlayType", StringComparison.OrdinalIgnoreCase) &&
                            token.Value.Equals("AnnotationPoint", StringComparison.OrdinalIgnoreCase)))
                        .SelectMany(layer => layer.Data)
                        .Select(datum => Channel(datum, FieldChannel.X) ?? ChartValue.Null()));
                }
                yield return ResolveTimeScale(
                    scale.Id, scale.Kind, scale.Channel, temporalInputs,
                    scale.DomainMinimum, scale.DomainMaximum, scale.IncludeZero, scale.Reverse,
                    scale.MajorTickCount, scale.TickInterval, scale.MinorTicks, scale.LabelRotation,
                    scale.LabelSkip, scale.OuterPadding, scale.TickFormat, scale.TimeUnit, formatter);
                continue;
            }

            var numbers = raw.Select(Number).Where(number => number.HasValue).Select(number => number!.Value).ToList();
            if (scale.Channel == FieldChannel.X)
            {
                numbers.AddRange(resolvedLayers
                    .Where(layer => layer.Style.Any(token =>
                        token.Name.Equals("overlayType", StringComparison.OrdinalIgnoreCase) &&
                        token.Value.Equals("AnnotationPoint", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(layer => layer.Data)
                    .Select(datum => Number(Channel(datum, FieldChannel.X) ?? ChartValue.Null()))
                    .Where(number => number.HasValue)
                    .Select(number => number!.Value));
            }
            if (scale.Channel is FieldChannel.Y or FieldChannel.Y2)
            {
                numbers.AddRange(resolvedLayers
                    .Where(layer => layer.Style.Any(token => token.Name.Equals("overlayType", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(layer => layer.Data)
                    .SelectMany(datum => scale.Channel == FieldChannel.Y
                        ? new[]
                        {
                            Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null()),
                            Number(Channel(datum, FieldChannel.YStart) ?? ChartValue.Null()),
                            Number(Channel(datum, FieldChannel.YEnd) ?? ChartValue.Null())
                        }
                        : new[] { Number(Channel(datum, scale.Channel) ?? ChartValue.Null()) })
                    .Where(number => number.HasValue)
                    .Select(number => number!.Value));
            }
            var minimum = scale.DomainMinimum is not null ? Number(scale.DomainMinimum) ?? 0m : numbers.DefaultIfEmpty(0m).Min();
            var maximum = scale.DomainMaximum is not null ? Number(scale.DomainMaximum) ?? 1m : numbers.DefaultIfEmpty(1m).Max();
            if (scale.Kind == ScaleKind.Logarithmic && (minimum <= 0m || maximum <= 0m || numbers.Any(number => number <= 0m)))
                throw new InvalidOperationException($"Logarithmic scale '{scale.Id}' requires positive values and domain bounds.");
            if (scale.IncludeZero) { minimum = Math.Min(0m, minimum); maximum = Math.Max(0m, maximum); }
            if (scale.ColorRange?.Midpoint is { } colorMidpoint) { minimum = Math.Min(colorMidpoint, minimum); maximum = Math.Max(colorMidpoint, maximum); }
            if (minimum == maximum) maximum = minimum + 1m;
            var needsMarkHeadroom = !spec.Theme.Tokens.Any(token => token.Name.Equals("MICRO_CHART", StringComparison.OrdinalIgnoreCase)) &&
                resolvedLayers.Any(layer => !layer.Style.Any(token => token.Name.Equals("overlayType", StringComparison.OrdinalIgnoreCase)) &&
                    (scale.Channel is FieldChannel.Y or FieldChannel.Y2 && layer.Mark is MarkKind.Line or MarkKind.Area or MarkKind.Point ||
                     scale.Channel == FieldChannel.X && scale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic && layer.Mark == MarkKind.Point));
            if (needsMarkHeadroom)
            {
                if (scale.Kind == ScaleKind.Logarithmic)
                {
                    if (scale.DomainMaximum is null) maximum = (decimal)Math.Pow(10d, Math.Ceiling(Math.Log10((double)maximum)));
                    if (scale.DomainMinimum is null) minimum = (decimal)Math.Pow(10d, Math.Floor(Math.Log10((double)minimum)));
                }
                else
                {
                    var padding = Math.Max(.01m, (maximum - minimum) * .05m);
                    if (scale.DomainMaximum is null) maximum += padding;
                    if ((!scale.IncludeZero || scale.Channel == FieldChannel.X) && scale.DomainMinimum is null) minimum -= padding;
                }
            }
            if (scale.MajorTickCount is < 2 or > 100)
                throw new InvalidOperationException($"MAJOR_TICK_COUNT for scale '{scale.Id}' must be between 2 and 100.");
            if (scale.TickInterval is <= 0m)
                throw new InvalidOperationException($"TICK_INTERVAL for scale '{scale.Id}' must be greater than zero.");
            var tickCount = scale.MajorTickCount ?? 5;
            var ticks = scale.TickInterval is { } interval
                ? Enumerable.Range(0, Math.Min(100, (int)Math.Floor((maximum - minimum) / interval) + 2))
                    .Select(index => Math.Ceiling(minimum / interval) * interval + index * interval)
                    .Where(value => value <= maximum).ToList()
                : scale.Kind == ScaleKind.Logarithmic
                ? Enumerable.Range(0, tickCount).Select(index => (decimal)Math.Pow(10d,
                    Math.Log10((double)minimum) + (Math.Log10((double)maximum) - Math.Log10((double)minimum)) * index / (tickCount - 1d))).ToList()
                : Enumerable.Range(0, tickCount).Select(index => minimum + ((maximum - minimum) * index / (tickCount - 1m))).ToList();
            var resolved = new ResolvedScale(scale.Id, scale.Channel, scale.Kind,
                [ChartValue.From(minimum), ChartValue.From(maximum)], [],
                ticks.Select(value => new PlotTick(ChartValue.From(value),
                    scale.TickFormat is not null ? formatter.Number(value, scale.TickFormat) : formatter.Number(value))).ToImmutableArray(),
                scale.IncludeZero, scale.Reverse, scale.MinorTicks, scale.LabelRotation, scale.LabelSkip, scale.OuterPadding,
                scale.TickFormat, scale.TimeUnit);
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
            if (overlayType is not null && layer.Bindings.IsEmpty)
            {
                yield return ResolveOverlay(layer, spec, columns, categories, series, formatter);
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

            // COMPAT_BREAK: 0.20 — isolated single-axis rules must not inherit another layer's color groups.
            var isolatedGeometry = spec.IsConnectedComposition || layer.Mark is (MarkKind.Rule or MarkKind.Rect or MarkKind.Line or MarkKind.Area) && spec.Coordinate is { Kind: CoordinateKind.TransposedCartesian, AspectRatio: not null };
            var colorBinding = layer.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color)
                ?? (isolatedGeometry || spec.Coordinate.Kind == CoordinateKind.Polar && layer.Bindings.Any(binding => binding.Stack != StackMode.None) ? null : spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color));
            var explicitSeries = layer.Style.FirstOrDefault(token => token.Name == "series")?.Value;
            var conditioned = layer.Mark is (MarkKind.Line or MarkKind.Area) && !layer.Conditions.IsDefaultOrEmpty;
            var areaConfidence = spec.HasTransposedConfidence && layer.Mark == MarkKind.Area && layer.Bindings.Any(binding => binding.Channel == FieldChannel.ConfidenceLow);
            var areaRibbon = areaConfidence || layer.Mark == MarkKind.Area && (conditioned || spec.IsTransposedAreaAspect || spec.IsConnectedComposition) && layer.Bindings.Any(binding => binding.Channel == FieldChannel.YStart);
            var ribbonScaleId = layer.Mark == MarkKind.Area &&
                layer.Bindings.FirstOrDefault(binding => binding.Channel == (areaConfidence ? FieldChannel.ConfidenceLow : FieldChannel.YStart))?.ScaleId is { } endpointScaleId &&
                !endpointScaleId.Equals(spec.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y)?.Id, StringComparison.OrdinalIgnoreCase)
                    ? endpointScaleId : null;
            if (colorBinding?.SemanticKind is not DataSemanticKind.Quantitative && colorBinding?.Field is { } colorField && columns.TryGetValue(colorField, out var colorColumn))
            {
                if (spec.IsConnectedComposition && colorColumn.Values.IsDefaultOrEmpty)
                {
                    yield return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, null, [])
                    { Style = layer.Style, Stack = LayerStack(layer, spec), Position = layer.Position, AreaRibbon = areaRibbon, AreaConfidence = areaConfidence, AreaRibbonScaleId = ribbonScaleId, ConnectionInterpolation = conditioned ? ResolvedConnectionGeometry.Kind(layer.Style) : null, ConnectionDecorations = conditioned && layer.Conditions.Any(condition => ResolvedConnectionDecoration.IsDecorationChannel(condition.Channel)), Connections = conditioned ? [] : default };
                    continue;
                }
                var presentKeys = spec.IsConnectedComposition ? colorColumn.Values.Select(CompositionSeriesKey).ToHashSet(StringComparer.Ordinal) : null;
                var layerSeries = presentKeys is not null ? series.Where(item => presentKeys.Contains(item.Key)) : series.AsEnumerable();
                foreach (var resolvedSeries in layerSeries)
                {
                    var dataPoints = ResolveLayerData(layer, spec, data, columns, categories, formatter,
                        rowIndex => (spec.IsConnectedComposition ? CompositionSeriesKey(colorColumn.Values[rowIndex]) : ValueKey(colorColumn.Values[rowIndex])) == resolvedSeries.Key);
                    yield return new ResolvedMarkLayer($"{layer.Id}-{resolvedSeries.Order:D2}", layer.Mark, spec.IsConnectedComposition ? layer.ZIndex : layer.ZIndex + resolvedSeries.Order,
                        resolvedSeries.Key, dataPoints)
                    { Style = layer.Style, Stack = LayerStack(layer, spec), BandSize = layer.BandSize, TickThickness = layer.TickThickness, TickOrientation = layer.TickOrientation, Position = layer.Position, AreaRibbon = areaRibbon, AreaConfidence = areaConfidence, AreaRibbonScaleId = ribbonScaleId, ConnectionInterpolation = conditioned ? ResolvedConnectionGeometry.Kind(layer.Style) : null, ConnectionDecorations = conditioned && layer.Conditions.Any(condition => ResolvedConnectionDecoration.IsDecorationChannel(condition.Channel)), Connections = conditioned ? [] : default };
                }
            }
            else
            {
                var datumSeries = colorBinding?.SemanticKind is not DataSemanticKind.Quantitative &&
                    colorBinding?.SourceKind == BindingSourceKind.Datum &&
                    colorBinding.Constant is { } datumConstant
                        ? ValueKey(datumConstant)
                        : null;
                var seriesKey = spec.IsConnectedComposition ? layer.Mark == MarkKind.Rule ? null : "layer:" + layer.Id : explicitSeries ?? datumSeries ?? (spec.Coordinate.Kind == CoordinateKind.Polar && layer.Bindings.Any(binding => binding.Stack != StackMode.None) ? layer.Id : series.FirstOrDefault()?.Key);
                yield return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, seriesKey,
                    ResolveLayerData(layer, spec, data, columns, categories, formatter, _ => true))
                {
                    Style = layer.Style,
                    Stack = LayerStack(layer, spec),
                    BandSize = layer.BandSize,
                    TickThickness = layer.TickThickness,
                    TickOrientation = layer.TickOrientation,
                    Position = layer.Position,
                    AreaRibbon = areaRibbon,
                    AreaConfidence = areaConfidence,
                    ConnectionInterpolation = conditioned ? ResolvedConnectionGeometry.Kind(layer.Style) : null,
                    ConnectionDecorations = conditioned && layer.Conditions.Any(condition => ResolvedConnectionDecoration.IsDecorationChannel(condition.Channel)),
                    Connections = conditioned ? [] : default,
                    AreaRibbonScaleId = ribbonScaleId
                };
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
            var mean = values.Length == 0 ? 0m : values.Average();
            return new ResolvedDatum(rowIndices.FirstOrDefault(categoryIndex),
            [
                new ResolvedChannelValue(FieldChannel.X, ChartValue.From(category), category),
                new ResolvedChannelValue(FieldChannel.Low, ChartValue.From(low), low.ToString(CultureInfo.InvariantCulture)),
                new ResolvedChannelValue(FieldChannel.Q1, ChartValue.From(q1), q1.ToString(CultureInfo.InvariantCulture)),
                new ResolvedChannelValue(FieldChannel.Median, ChartValue.From(median), median.ToString(CultureInfo.InvariantCulture)),
                new ResolvedChannelValue(FieldChannel.Q3, ChartValue.From(q3), q3.ToString(CultureInfo.InvariantCulture)),
                new ResolvedChannelValue(FieldChannel.High, ChartValue.From(high), high.ToString(CultureInfo.InvariantCulture)),
                new ResolvedChannelValue(FieldChannel.Mean, ChartValue.From(mean), mean.ToString(CultureInfo.InvariantCulture))
            ], values.Length == 0);
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

    private static bool IsFlagTrue(ChartValue? value)
    {
        if (value is null || value.Kind == ChartValueKind.Null) return false;
        if (value.Kind == ChartValueKind.Boolean) return value.Boolean == true;
        if (Number(value) is { } num) return num != 0m;
        var text = Display(value).Trim();
        return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "y", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "on", StringComparison.OrdinalIgnoreCase);
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
            var totalVal = Channel(datum, FieldChannel.Detail);
            var totalText = totalVal is not null ? Display(totalVal)?.Trim() : null;
            var subtotalVal = Channel(datum, FieldChannel.Low);
            var isSubtotal = (totalText is not null && (totalText.Equals("SUBTOTAL", StringComparison.OrdinalIgnoreCase) || totalText.Equals("SUB_TOTAL", StringComparison.OrdinalIgnoreCase) || totalText.Equals("SUB", StringComparison.OrdinalIgnoreCase)))
                || IsFlagTrue(subtotalVal);
            var isTotal = !isSubtotal && (IsFlagTrue(totalVal) || (totalText is not null && totalText.Equals("TOTAL", StringComparison.OrdinalIgnoreCase)));
            var isAnchor = isTotal || isSubtotal;

            var start = isAnchor ? 0m : running;
            var end = isAnchor ? (delta != 0m ? delta : running) : running + delta;
            running = end;

            var barType = isTotal ? "TOTAL" : isSubtotal ? "SUBTOTAL" : (end >= start ? "INCREASE" : "DECREASE");

            return datum with
            {
                Channels = datum.Channels
                    .Add(new ResolvedChannelValue(FieldChannel.YStart, ChartValue.From(start), start.ToString(CultureInfo.InvariantCulture)))
                    .Add(new ResolvedChannelValue(FieldChannel.YEnd, ChartValue.From(end), end.ToString(CultureInfo.InvariantCulture)))
                    .Add(new ResolvedChannelValue(FieldChannel.XStart, ChartValue.From(start), start.ToString(CultureInfo.InvariantCulture)))
                    .Add(new ResolvedChannelValue(FieldChannel.XEnd, ChartValue.From(end), end.ToString(CultureInfo.InvariantCulture)))
                    .Add(new ResolvedChannelValue(FieldChannel.Text, ChartValue.From(barType), barType))
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
                ], value.Kind == ChartValueKind.Null);
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
        var nulls = spec.IsConnectedComposition ? spec.NullHandling with
        {
            Default = layer.Style.FirstOrDefault(token => token.Name.Equals("nullHandling", StringComparison.OrdinalIgnoreCase))?.Value.ToUpperInvariant() switch
            {
                "CONNECT" => NullValuePolicy.Skip,
                "GAP" => NullValuePolicy.Gap,
                "ZERO" => NullValuePolicy.Zero,
                _ => spec.NullHandling.Default
            }
        } : spec.NullHandling;
        var categoryBinding = layerBindings.FirstOrDefault(binding => binding.Channel is FieldChannel.X or FieldChannel.Theta);
        var rows = new List<ResolvedDatum>();
        var preserveRows = spec.Coordinate.Kind == CoordinateKind.Polar && layer.Bindings.Any(binding => binding.Channel == FieldChannel.Radius && binding.Stack != StackMode.None)
            || layerBindings.Any(binding => binding.Channel is FieldChannel.XOffset or FieldChannel.YOffset)
            || layer.Style.Any(token => token.Name.Equals("preserveRows", StringComparison.OrdinalIgnoreCase)
            && token.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        var xScaleIsContinuous = categoryBinding is not null &&
            (categoryBinding.SemanticKind is DataSemanticKind.Quantitative or DataSemanticKind.Temporal ||
             spec.Scales.Any(s => string.Equals(s.Id, categoryBinding.ScaleId, StringComparison.OrdinalIgnoreCase) &&
                s.Kind is ScaleKind.Linear or ScaleKind.Logarithmic or ScaleKind.Time));
        if (!preserveRows && categoryBinding is not null && !xScaleIsContinuous &&
            categoryBinding.Field is { } categoryField && columns.TryGetValue(categoryField, out var categoryColumn) && !categories.IsDefaultOrEmpty)
        {
            var measureFields = layerBindings
                .Where(b => b.SourceKind == BindingSourceKind.Field && b.Field is not null && columns.ContainsKey(b.Field) &&
                    b.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.YStart or FieldChannel.YEnd
                        or FieldChannel.Radius or FieldChannel.Low or FieldChannel.High or FieldChannel.Open or FieldChannel.Close
                        or FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh)
                .Select(b => columns[b.Field!])
                .Distinct()
                .ToList();
            if (measureFields.Count == 0)
            {
                measureFields = layerBindings
                    .Where(b => b.SourceKind == BindingSourceKind.Field && b.Field is not null && columns.ContainsKey(b.Field) &&
                        b.Channel == FieldChannel.Size)
                    .Select(b => columns[b.Field!])
                    .Distinct()
                    .ToList();
            }
            bool HasMeasureValue(int rowIndex)
            {
                if (measureFields.Count == 0) return true;
                for (var i = 0; i < measureFields.Count; i++)
                {
                    if (measureFields[i].Values[rowIndex].Kind != ChartValueKind.Null)
                        return true;
                }
                return false;
            }

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
                    var key = (facetKey, categoryKey);
                    if (!firstRowByFacetCategory.TryGetValue(key, out var existingIndex) ||
                        (!HasMeasureValue(existingIndex) && HasMeasureValue(index)))
                        firstRowByFacetCategory[key] = index;
                }
                foreach (var facetKey in facetKeys)
                    foreach (var category in categories)
                        if (firstRowByFacetCategory.TryGetValue((facetKey, category), out var rowIndex))
                            rows.Add(Datum(rowIndex, layerBindings, columns, nulls, conditionGroups, formatter));
                return rows.ToImmutableArray();
            }
            // The same one-pass indexing for the unfaceted category path.
            var firstRowByCategory = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < data.RowCount; index++)
            {
                if (!include(index)) continue;
                if (ValueKey(categoryColumn.Values[index]) is { } categoryKey)
                {
                    if (!firstRowByCategory.TryGetValue(categoryKey, out var existingIndex) ||
                        (!HasMeasureValue(existingIndex) && HasMeasureValue(index)))
                        firstRowByCategory[categoryKey] = index;
                }
            }
            for (var categoryIndex = 0; categoryIndex < categories.Length; categoryIndex++)
            {
                var category = categories[categoryIndex];
                rows.Add(firstRowByCategory.TryGetValue(category, out var rowIndex)
                    ? Datum(rowIndex, layerBindings, columns, nulls, conditionGroups, formatter)
                    : GapDatum(categoryIndex, layerBindings, categoryBinding, category));
            }
            return rows.ToImmutableArray();
        }

        for (var rowIndex = 0; rowIndex < data.RowCount; rowIndex++)
            if (include(rowIndex)) rows.Add(Datum(rowIndex, layerBindings, columns, nulls, conditionGroups, formatter));
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
                FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or FieldChannel.Q3 or FieldChannel.High or FieldChannel.Open or FieldChannel.Close or
                FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh)
            && channel.Value.Kind == ChartValueKind.Null);
        return new ResolvedDatum(rowIndex, channels, requiredNull && nulls.Default == NullValuePolicy.Gap)
        {
            Encodings = ResolveConditions(conditionGroups, rowIndex, columns)
        };
    }

    private static ResolvedDatum GapDatum(int index, ImmutableArray<FieldBinding> bindings, FieldBinding categoryBinding, string category)
    {
        var channels = bindings.Select(binding => new ResolvedChannelValue(binding.Channel,
            binding == categoryBinding ? ChartValue.From(category) : ChartValue.Null(),
            binding == categoryBinding ? category : null)).ToImmutableArray();
        return new ResolvedDatum(index, channels, true);
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

    private static bool LayerTargetsYScale(ResolvedMarkLayer layer, ChartSpec spec, ScaleSpec scale)
    {
        if (layer.Style.Any(token => token.Name.Equals("overlayType", StringComparison.OrdinalIgnoreCase)))
            return false;

        var layerSpec = spec.Layers.FirstOrDefault(candidate =>
            candidate.Id.Equals(layer.Id, StringComparison.OrdinalIgnoreCase) ||
            layer.Id.StartsWith(candidate.Id + ":", StringComparison.OrdinalIgnoreCase));
        if (layerSpec is not null)
        {
            var yBinding = layerSpec.Bindings.FirstOrDefault(binding => binding.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.YStart or FieldChannel.YEnd);
            if (yBinding is not null)
                return string.Equals(yBinding.ScaleId, scale.Id, StringComparison.OrdinalIgnoreCase);
        }

        if (scale.Channel == FieldChannel.Y2)
            return layer.Data.Any(datum => datum.Channels.Any(c => c.Channel == FieldChannel.Y2));
        return layer.Data.Any(datum => datum.Channels.Any(c => c.Channel is FieldChannel.Y or FieldChannel.YStart or FieldChannel.YEnd));
    }

    private static ResolvedMarkLayer ResolveOverlay(
        MarkLayerSpec layer,
        ChartSpec spec,
        IReadOnlyDictionary<string, ChartColumn> columns,
        ImmutableArray<string> categories,
        ImmutableArray<ResolvedSeries> series,
        ChartValueFormatter formatter)
    {
        var type = layer.Style.First(token => token.Name == "overlayType").Value;
        var parameterText = layer.Style.FirstOrDefault(token => token.Name == "parameter")?.Value;
        decimal.TryParse(parameterText, NumberStyles.Any, CultureInfo.InvariantCulture, out var parameter);
        if (type == "ReferenceBand")
        {
            decimal.TryParse(layer.Style.FirstOrDefault(token => token.Name == "low")?.Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out var low);
            decimal.TryParse(layer.Style.FirstOrDefault(token => token.Name == "high")?.Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out var high);
            return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, null,
                [new ResolvedDatum(0,
                [
                    new ResolvedChannelValue(FieldChannel.YStart, ChartValue.From(low), formatter.Number(low, "G")),
                    new ResolvedChannelValue(FieldChannel.YEnd, ChartValue.From(high), formatter.Number(high, "G"))
                ], false)])
            { Style = layer.Style, Stack = StackMode.None, BandSize = layer.BandSize };
        }
        if (type == "AnnotationPoint")
        {
            var annotType = layer.Style.FirstOrDefault(token => token.Name == "annotationType")?.Value ?? "MAX";
            var annotData = ImmutableArray<ResolvedDatum>.Empty;
            if (annotType.Equals("COORD", StringComparison.OrdinalIgnoreCase))
            {
                var coordChannels = new List<ResolvedChannelValue>();
                var coordXString = layer.Style.FirstOrDefault(token => token.Name == "coordXString")?.Value;
                var coordXText = layer.Style.FirstOrDefault(token => token.Name == "coordX")?.Value;
                var coordYText = layer.Style.FirstOrDefault(token => token.Name == "coordY")?.Value;
                if (!string.IsNullOrEmpty(coordXString))
                {
                    coordChannels.Add(new ResolvedChannelValue(FieldChannel.X, ChartValue.From(coordXString), coordXString));
                }
                else if (decimal.TryParse(coordXText, NumberStyles.Number, CultureInfo.InvariantCulture, out var cx))
                {
                    coordChannels.Add(new ResolvedChannelValue(FieldChannel.X, ChartValue.From(cx), formatter.Number(cx, "G")));
                }
                if (decimal.TryParse(coordYText, NumberStyles.Number, CultureInfo.InvariantCulture, out var cy))
                {
                    coordChannels.Add(new ResolvedChannelValue(FieldChannel.Y, ChartValue.From(cy), formatter.Number(cy, "G")));
                }
                if (coordChannels.Count > 0)
                    annotData = [new ResolvedDatum(0, coordChannels.ToImmutableArray(), false)];
            }
            return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, null, annotData)
            { Style = layer.Style, Stack = StackMode.None, BandSize = layer.BandSize };
        }

        var isStatistical = type is "Average" or "MovingAvg" or "Linear" or "Polynomial" or "Exponential" or "Logarithmic" or "Power";
        var targetSeries = layer.Style.FirstOrDefault(token => token.Name.Equals("series", StringComparison.OrdinalIgnoreCase))?.Value;
        if (isStatistical && series.Length > 1)
        {
            if (string.IsNullOrWhiteSpace(targetSeries))
            {
                throw new InvalidOperationException(
                    $"Statistical overlay '{type}' requires SERIES = '<name>' when the chart has multiple series ({series.Length} series).");
            }
            if (!series.Any(item => item.Key.Equals(targetSeries, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Statistical overlay '{type}' referenced unknown series '{targetSeries}'.");
            }
        }
        else if (isStatistical && !string.IsNullOrWhiteSpace(targetSeries) && series.Length > 0 &&
            !series.Any(item => item.Key.Equals(targetSeries, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Statistical overlay '{type}' referenced unknown series '{targetSeries}'.");
        }

        var matchingSeriesLayer = !string.IsNullOrWhiteSpace(targetSeries)
            ? spec.Layers.FirstOrDefault(candidate =>
                !candidate.Style.Any(token => token.Name == "overlayType") &&
                (candidate.Id.Equals(targetSeries, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.Style.FirstOrDefault(t => t.Name == "series")?.Value, targetSeries, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.Bindings.FirstOrDefault(b => b.Channel is FieldChannel.Y or FieldChannel.Y2)?.Field, targetSeries, StringComparison.OrdinalIgnoreCase)))
            : null;

        var xBinding = matchingSeriesLayer?.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.X)
            ?? spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.X);
        var yBinding = matchingSeriesLayer?.Bindings.FirstOrDefault(binding => binding.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Close)
            ?? spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Y && binding.Axis != AxisRole.Secondary)
            ?? spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Y)
            ?? spec.Bindings.FirstOrDefault(binding => binding.Channel is FieldChannel.Y or FieldChannel.Y2)
            ?? spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Close)
            ?? spec.Bindings.First(binding => binding.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Close);

        var yColumn = columns[yBinding.Field!];
        var xColumn = xBinding?.Field is { } xField && columns.TryGetValue(xField, out var resolvedXColumn) ? resolvedXColumn : null;
        var colorBinding = spec.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color)
            ?? spec.Layers.FirstOrDefault(candidate => !candidate.Style.Any(t => t.Name == "overlayType"))
                ?.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Color);
        var colorColumn = colorBinding?.Field is { } colorField && columns.TryGetValue(colorField, out var resolvedColorColumn)
            ? resolvedColorColumn
            : null;

        var rowIndices = (!string.IsNullOrWhiteSpace(targetSeries) && colorColumn is not null)
            ? Enumerable.Range(0, yColumn.Values.Length)
                .Where(index => string.Equals(ValueKey(colorColumn.Values[index]), targetSeries, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : Enumerable.Range(0, yColumn.Values.Length).ToList();

        var rawY = rowIndices.Select(index => Number(yColumn.Values[index])).ToList();
        var rawX = rowIndices.Select((index, pos) =>
        {
            if (xColumn is null) return (decimal?)pos;
            var xVal = xColumn.Values[index];
            if (xVal.Kind == ChartValueKind.Null) return (decimal?)null;
            return Number(xVal) ?? (type is "Logarithmic" or "Power" ? (decimal?)null : (decimal)pos);
        }).ToList();

        if (type is "Logarithmic" or "Power" && xColumn is not null)
        {
            foreach (var index in rowIndices)
            {
                var xVal = xColumn.Values[index];
                if (xVal.Kind != ChartValueKind.Null && Number(xVal) is null)
                    throw new InvalidOperationException($"{type} overlay requires positive numeric X values.");
            }
        }

        List<decimal?> resolvedValues = type switch
        {
            "Goal" or "ReferenceLine" => Enumerable.Repeat((decimal?)parameter, Math.Max(1, rowIndices.Count)).ToList(),
            "Average" => ComputeAverageOverlay(rawY),
            "MovingAvg" => MovingAverage(rawY, Math.Max(1, (int)(parameter == 0 ? 3 : parameter))),
            "Linear" => PolynomialRegression(rawX, rawY, 1),
            "Polynomial" => PolynomialRegression(rawX, rawY, Math.Max(1, (int)(parameter == 0 ? 2 : parameter))),
            "Exponential" => ExponentialRegression(rawX, rawY),
            "Logarithmic" => LogarithmicRegression(rawX, rawY),
            "Power" => PowerRegression(rawX, rawY),
            _ => throw new InvalidOperationException($"Unsupported overlay type '{type}'.")
        };

        var datumIndices = rowIndices.Count > 0 ? rowIndices : [0];
        var data = resolvedValues.Select((value, pos) =>
        {
            var sourceIndex = pos < datumIndices.Count ? datumIndices[pos] : pos;
            var xValue = xColumn is null || sourceIndex >= xColumn.Values.Length ? ChartValue.From(pos) : xColumn.Values[sourceIndex];
            var xDisplay = xColumn is null || sourceIndex >= xColumn.Values.Length || xColumn.DisplayValues.IsDefaultOrEmpty
                ? formatter.Format(xValue)
                : xColumn.DisplayValues[sourceIndex] ?? formatter.Format(xValue);
            return new ResolvedDatum(pos,
                [
                    new ResolvedChannelValue(FieldChannel.X, xValue, xDisplay),
                    new ResolvedChannelValue(FieldChannel.Y, value.HasValue ? ChartValue.From(value.Value) : ChartValue.Null(),
                        value.HasValue ? formatter.Number(value.Value, "G") : formatter.NullLabel)
                ], !value.HasValue);
        }).ToImmutableArray();
        return new ResolvedMarkLayer(layer.Id, layer.Mark, layer.ZIndex, string.IsNullOrWhiteSpace(targetSeries) ? null : targetSeries, data)
        { Style = layer.Style, Stack = LayerStack(layer, spec), BandSize = layer.BandSize, TickThickness = layer.TickThickness, TickOrientation = layer.TickOrientation, Position = layer.Position };
    }

    private static List<decimal?> ComputeAverageOverlay(IReadOnlyList<decimal?> yValues)
    {
        var valid = yValues.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        if (valid.Count == 0)
            return Enumerable.Repeat((decimal?)null, Math.Max(1, yValues.Count)).ToList();
        var mean = valid.Average();
        return Enumerable.Repeat((decimal?)mean, yValues.Count).ToList();
    }

    private static List<decimal?> MovingAverage(IReadOnlyList<decimal?> values, int window)
    {
        var result = new List<decimal?>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            if (index < window - 1)
            {
                result.Add(null);
                continue;
            }

            decimal sum = 0m;
            var count = 0;
            for (var k = index - window + 1; k <= index; k++)
            {
                if (values[k] is { } v)
                {
                    sum += v;
                    count++;
                }
            }
            result.Add(count > 0 ? sum / count : null);
        }
        return result;
    }

    private static List<decimal?> PolynomialRegression(
        IReadOnlyList<decimal?> xValues,
        IReadOnlyList<decimal?> yValues,
        int requestedDegree)
    {
        if (yValues.Count == 0 || xValues.Count != yValues.Count) return [];
        var valid = CollectValidPairs(xValues, yValues);
        if (valid.Count == 0)
            return Enumerable.Repeat((decimal?)null, yValues.Count).ToList();

        var distinctX = valid.Select(pair => pair.X).Distinct().Count();
        if (distinctX < requestedDegree + 1)
        {
            throw new InvalidOperationException(
                $"Polynomial degree {requestedDegree} requires at least {requestedDegree + 1} distinct X points (found {distinctX}).");
        }

        // Scale X into [-1, 1] before raising to powers so large domains (epoch ms, revenue)
        // stay well within floating-point and decimal dynamic range and condition cleanly.
        // COMPAT_BREAK: 0.23 - subtract decimal offsets before conversion so nearby large X values stay distinct.
        var xMin = valid.Min(pair => pair.X);
        var xMax = valid.Max(pair => pair.X);
        var span = DecimalDifference(xMax, xMin);
        double NormalizeX(decimal x) => span == 0d ? 0d : (2d * DecimalDifference(x, xMin) / span) - 1d;

        var size = requestedDegree + 1;
        var matrix = new double[size, size + 1];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
                matrix[row, column] = valid.Sum(pair => Math.Pow(NormalizeX(pair.X), row + column));
            matrix[row, size] = valid.Sum(pair => (double)pair.Y * Math.Pow(NormalizeX(pair.X), row));
        }

        for (var pivot = 0; pivot < size; pivot++)
        {
            var best = Enumerable.Range(pivot, size - pivot)
                .OrderByDescending(row => Math.Abs(matrix[row, pivot])).First();
            if (Math.Abs(matrix[best, pivot]) <= 1e-12d)
                throw new InvalidOperationException("Regression system is singular for the supplied data points.");
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
        return xValues.Select(x =>
        {
            if (!x.HasValue) return (decimal?)null;
            var u = NormalizeX(x.Value);
            double prediction = 0d;
            double power = 1d;
            for (var degree = 0; degree < coefficients.Length; degree++)
            {
                prediction += coefficients[degree] * power;
                power *= u;
            }
            return ToFiniteDecimal(prediction);
        }).ToList();
    }

    private static double DecimalDifference(decimal value, decimal origin) =>
        Math.Sign(value) == Math.Sign(origin)
            ? (double)(value - origin)
            : (double)value - (double)origin;

    private static List<decimal?> ExponentialRegression(
        IReadOnlyList<decimal?> xValues,
        IReadOnlyList<decimal?> yValues)
    {
        if (yValues.Count == 0 || xValues.Count != yValues.Count) return [];
        foreach (var y in yValues)
        {
            if (y.HasValue && y.Value <= 0m)
                throw new InvalidOperationException($"Exponential overlay requires strictly positive Y values (found {y.Value}).");
        }

        var valid = CollectValidPairs(xValues, yValues);
        if (valid.Count == 0)
            return Enumerable.Repeat((decimal?)null, yValues.Count).ToList();
        var distinctX = valid.Select(pair => pair.X).Distinct().Count();
        if (distinctX < 2)
            throw new InvalidOperationException($"Exponential overlay requires at least 2 distinct X points (found {distinctX}).");

        // Fit ln(y) = alpha + beta * (x - xMean) to avoid exp(intercept) overflow on shifted X.
        var xMean = valid.Average(pair => (double)pair.X);
        var (alpha, beta) = FitSimpleLinear(
            valid.Select(pair => (X: (double)pair.X - xMean, Y: Math.Log((double)pair.Y))).ToList(),
            "Exponential");

        return xValues.Select(x =>
            !x.HasValue ? (decimal?)null : ToFiniteDecimal(Math.Exp(alpha + beta * ((double)x.Value - xMean)))).ToList();
    }

    private static List<decimal?> LogarithmicRegression(
        IReadOnlyList<decimal?> xValues,
        IReadOnlyList<decimal?> yValues)
    {
        if (yValues.Count == 0 || xValues.Count != yValues.Count) return [];
        foreach (var x in xValues)
        {
            if (x.HasValue && x.Value <= 0m)
                throw new InvalidOperationException($"Logarithmic overlay requires strictly positive X values (found {x.Value}).");
        }

        var valid = CollectValidPairs(xValues, yValues);
        if (valid.Count == 0)
            return Enumerable.Repeat((decimal?)null, yValues.Count).ToList();
        var distinctX = valid.Select(pair => pair.X).Distinct().Count();
        if (distinctX < 2)
            throw new InvalidOperationException($"Logarithmic overlay requires at least 2 distinct positive X points (found {distinctX}).");

        var (alpha, beta) = FitSimpleLinear(
            valid.Select(pair => (X: Math.Log((double)pair.X), Y: (double)pair.Y)).ToList(),
            "Logarithmic");

        return xValues.Select(x =>
            !x.HasValue ? (decimal?)null : ToFiniteDecimal(alpha + beta * Math.Log((double)x.Value))).ToList();
    }

    private static List<decimal?> PowerRegression(
        IReadOnlyList<decimal?> xValues,
        IReadOnlyList<decimal?> yValues)
    {
        if (yValues.Count == 0 || xValues.Count != yValues.Count) return [];
        foreach (var x in xValues)
        {
            if (x.HasValue && x.Value <= 0m)
                throw new InvalidOperationException($"Power overlay requires strictly positive X values (found {x.Value}).");
        }
        foreach (var y in yValues)
        {
            if (y.HasValue && y.Value <= 0m)
                throw new InvalidOperationException($"Power overlay requires strictly positive Y values (found {y.Value}).");
        }

        var valid = CollectValidPairs(xValues, yValues);
        if (valid.Count == 0)
            return Enumerable.Repeat((decimal?)null, yValues.Count).ToList();
        var distinctX = valid.Select(pair => pair.X).Distinct().Count();
        if (distinctX < 2)
            throw new InvalidOperationException($"Power overlay requires at least 2 distinct positive X points (found {distinctX}).");

        var (alpha, beta) = FitSimpleLinear(
            valid.Select(pair => (X: Math.Log((double)pair.X), Y: Math.Log((double)pair.Y))).ToList(),
            "Power");

        return xValues.Select(x =>
            !x.HasValue ? (decimal?)null : ToFiniteDecimal(Math.Exp(alpha + beta * Math.Log((double)x.Value)))).ToList();
    }

    private static List<(decimal X, decimal Y)> CollectValidPairs(
        IReadOnlyList<decimal?> xValues,
        IReadOnlyList<decimal?> yValues)
    {
        var pairs = new List<(decimal X, decimal Y)>(yValues.Count);
        for (var index = 0; index < yValues.Count; index++)
        {
            if (xValues[index] is { } x && yValues[index] is { } y)
                pairs.Add((x, y));
        }
        return pairs;
    }

    private static (double Intercept, double Slope) FitSimpleLinear(
        IReadOnlyList<(double X, double Y)> points,
        string overlayName)
    {
        var meanX = points.Average(p => p.X);
        var meanY = points.Average(p => p.Y);
        double sxx = 0d;
        double sxy = 0d;
        foreach (var (x, y) in points)
        {
            var dx = x - meanX;
            sxx += dx * dx;
            sxy += dx * (y - meanY);
        }

        if (Math.Abs(sxx) <= 1e-18d)
            throw new InvalidOperationException($"{overlayName} overlay regression is singular for identical transformed X inputs.");

        var slope = sxy / sxx;
        var intercept = meanY - slope * meanX;
        return (intercept, slope);
    }

    private static decimal? ToFiniteDecimal(double value)
    {
        if (!double.IsFinite(value))
            throw new InvalidOperationException("Overlay regression produced a non-finite value.");
        if (value > (double)decimal.MaxValue || value < (double)decimal.MinValue)
            throw new InvalidOperationException("Overlay regression prediction exceeded decimal range.");
        return (decimal)value;
    }

    private static string? ConfidenceIntervalDetail(ResolvedDatum datum, ChartValueFormatter formatter)
    {
        var first = datum.Channels.FirstOrDefault(channel => channel.Channel == FieldChannel.ConfidenceLow);
        var second = datum.Channels.FirstOrDefault(channel => channel.Channel == FieldChannel.ConfidenceHigh);
        if (first is null || second is null || first.Value.Kind == ChartValueKind.Null || second.Value.Kind == ChartValueKind.Null) return null;
        return $"confidence {first.DisplayValue ?? formatter.Format(first.Value)} to {second.DisplayValue ?? formatter.Format(second.Value)}";
    }

    /// <summary>Describes an author-supplied ranged rectangle so non-visual surfaces keep both endpoints.</summary>
    private static string? ErrorIntervalDetail(ResolvedDatum datum, ChartValueFormatter formatter)
    {
        var first = datum.Channels.FirstOrDefault(channel => channel.Channel == FieldChannel.ErrorLow);
        var second = datum.Channels.FirstOrDefault(channel => channel.Channel == FieldChannel.ErrorHigh);
        if (first is null || second is null || first.Value.Kind == ChartValueKind.Null || second.Value.Kind == ChartValueKind.Null) return null;
        return $"error {first.DisplayValue ?? formatter.Format(first.Value)} to {second.DisplayValue ?? formatter.Format(second.Value)}";
    }

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

    internal static string? OffsetDetail(ResolvedDatum datum)
    {
        var groups = datum.Channels.Where(channel => channel.Channel is FieldChannel.XOffset or FieldChannel.YOffset)
            .OrderBy(channel => channel.Channel)
            .Select(channel => $"{(channel.Channel == FieldChannel.XOffset ? "X_OFFSET" : "Y_OFFSET")}: " +
                (channel.Value.Kind == ChartValueKind.Null ? "null (unshifted)" : channel.DisplayValue ?? Display(channel.Value))).ToArray();
        return groups.Length == 0 ? null : string.Join(", ", groups);
    }

    internal static string? TextLabel(ResolvedDatum datum)
    {
        if (!datum.Encodings.IsDefaultOrEmpty)
            for (var index = datum.Encodings.Length - 1; index >= 0; index--)
                if (datum.Encodings[index].Channel == ConditionalEncodingChannel.Text)
                    return Display(datum.Encodings[index].Value);
        var text = datum.Channels.FirstOrDefault(channel => channel.Channel == FieldChannel.Text);
        return text is null ? null : text.DisplayValue ?? Display(text.Value);
    }

    internal static string CartesianAreaDescription(ResolvedDatum datum, bool ribbon, bool transposedAxes = false)
    {
        string Value(FieldChannel channel) => Display(datum.Channels.FirstOrDefault(value => value.Channel == channel)?.Value ?? ChartValue.Null());
        if (datum.IsGap) return "gap";
        var x = Value(transposedAxes ? FieldChannel.Y : FieldChannel.X);
        var end = Value(transposedAxes ? ribbon ? FieldChannel.XEnd : FieldChannel.X : ribbon ? FieldChannel.YEnd : FieldChannel.Y);
        var start = ribbon ? Value(transposedAxes ? FieldChannel.XStart : FieldChannel.YStart) : "0";
        return $"X {x}; Y {start} to {end}";
    }

    private static SemanticFallback BuildFallback(ChartSpec spec, ImmutableArray<ResolvedMarkLayer> layers,
        ImmutableArray<string> categories, ChartValueFormatter formatter)
    {
        var transposedPhysical = spec.Coordinate.Kind == CoordinateKind.TransposedCartesian && (spec.Coordinate.AspectRatio is not null || spec.IsTransposedConnected);
        if (spec.Coordinate.Kind == CoordinateKind.Polar && layers.Any(layer => layer.Stack != StackMode.None))
            return new SemanticFallback(SemanticFallbackKind.ProportionalBreakdown, spec.Title ?? spec.Id,
                layers.SelectMany(layer => layer.Data.Select(datum => new SemanticFallbackItem(RadialStackResolver.Theta(datum) ?? "gap",
                    RadialStackResolver.Description(datum), datum.RowIndex)
                { Group = layer.SeriesKey ?? layer.Id, Detail = "radial stack" })).ToImmutableArray());
        var sourceLayers = layers.Where(layer => layer.Mark is not MarkKind.Rule).ToList();
        var total = sourceLayers.Where(layer => layer.Mark == MarkKind.Arc).SelectMany(layer => layer.Data)
            .Select(datum => datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.Radius or FieldChannel.Y))
            .Where(value => value is not null).Sum(value => Math.Max(0m, Number(value!.Value) ?? 0m));
        var items = sourceLayers.SelectMany((layer, layerIndex) => layer.Data.Select((datum, index) =>
        {
            if (layer.ConnectionDecorations)
                return new SemanticFallbackItem($"Row {datum.RowIndex + 1}", ConnectedMarkResolver.DatumDescription(datum, layer), (layerIndex * 100000) + index)
                { Group = layer.Id, Detail = ConnectedMarkResolver.DecorationDescription(datum) + "; " + string.Join(", ", datum.Encodings.Select(value => $"conditional {value.Channel}: {formatter.Format(value.Value)}")) };
            if ((spec.IsTransposedConnected || layer.ConnectionInterpolation is not null) && !layer.Connections.IsDefault)
            {
                var presentation = datum.Encodings.IsDefaultOrEmpty ? "layer presentation" : string.Join(", ",
                    datum.Encodings.Select(value => $"conditional {value.Channel}: {formatter.Format(value.Value)}"));
                return new SemanticFallbackItem($"Row {datum.RowIndex + 1}", ConnectedMarkResolver.DatumDescription(datum, layer), (layerIndex * 100000) + index)
                { Group = layer.Id, Detail = (spec.IsTransposedConnected ? "X vertical, Y horizontal; " : "") + presentation };
            }
            // COMPAT_BREAK: 0.20.0 — transposed ribbons expose their raw interval in semantic fallbacks.
            if (layer.AreaRibbon || layer.Mark == MarkKind.Area && spec.Coordinate.Kind == CoordinateKind.TransposedCartesian && datum.Channels.Any(value => value.Channel == FieldChannel.YStart))
                return new SemanticFallbackItem($"Row {datum.RowIndex + 1}", ConnectedMarkResolver.RibbonDescription(datum, !layer.Connections.IsDefault && ResolvedMarkConnection.FillsNullsWithZero(layer), layer.AreaConfidence), index)
                { Group = layer.Id, Detail = layer.AreaConfidence ? "confidence interval" : "ribbon interval" };
            if (layer.Mark == MarkKind.Area && spec.IsTransposedAreaAspect)
                return new SemanticFallbackItem($"Row {datum.RowIndex + 1}", CartesianAreaDescription(datum, false), (layerIndex * 100000) + index)
                { Group = layer.Id, Detail = "zero-baseline area" };
            if (layer.Mark == MarkKind.Rect && transposedPhysical)
            {
                var range = datum.IsGap ? null : CartesianRangeDescription(datum);
                return new SemanticFallbackItem($"Row {datum.RowIndex + 1}", range ?? "gap", (layerIndex * 100000) + index)
                {
                    Group = layer.SeriesKey ?? layer.Id,
                    Detail = range is null ? "null gap" : "rectangle range"
                };
            }
            var label = datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.Region or FieldChannel.Route or FieldChannel.Text or FieldChannel.X or FieldChannel.Theta)?.DisplayValue
                ?? (index < categories.Length ? categories[index] : $"Row {index + 1}");
            if (layer.Mark == MarkKind.Text && transposedPhysical)
                label = TextLabel(datum) ?? label;
            var value = datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or
                FieldChannel.Median or FieldChannel.Close or FieldChannel.Size or FieldChannel.YEnd);
            var numeric = value is null ? null : Number(value.Value);
            var conditionDetail = datum.Encodings.IsDefaultOrEmpty ? null : string.Join(", ", datum.Encodings.Select(encoding =>
                $"conditional {encoding.Channel}: {formatter.Format(encoding.Value)}"));
            var interval = layer.Mark == MarkKind.Rect && layer.Stack == StackMode.None ? IntervalDetail(datum, formatter) : null;
            var errorDetail = ErrorIntervalDetail(datum, formatter);
            var confidenceDetail = ConfidenceIntervalDetail(datum, formatter);
            var overlayKind = layer.Style.FirstOrDefault(token => token.Name == "overlayType")?.Value;
            var overlayDetail = overlayKind switch
            {
                "ForecastAnomaly" => "anomaly",
                "Forecast" => "forecast",
                _ => null
            };
            var item = new SemanticFallbackItem(label ?? $"Row {index + 1}", datum.IsGap ? "gap" : value is null ? "" : value.DisplayValue ?? formatter.Format(value.Value), (layerIndex * 100000) + index)
            {
                Group = spec.IsConnectedComposition ? layer.Id : layer.SeriesKey ?? (overlayKind is "Forecast" or "ForecastConfidence" or "ForecastAnomaly" ? layer.Id : null),
                Detail = datum.IsGap ? "null gap" : confidenceDetail ?? errorDetail ?? interval ?? overlayDetail ?? conditionDetail ?? (layer.Mark == MarkKind.Arc && numeric.HasValue && total > 0m
                    ? $"{numeric.Value / total:P1} of total"
                    : null)
            };
            var offsetDetail = transposedPhysical
                ? OffsetDetail(datum) : null;
            if (ConnectedMarkResolver.ZeroDetail(datum) is { } zeroDetail)
                item = item with { Detail = item.Detail is null ? zeroDetail : $"{zeroDetail}; {item.Detail}" };
            else if (!layer.Connections.IsDefault && ResolvedMarkConnection.FillsNullsWithZero(layer) && datum.IsGap)
            {
                string Raw(FieldChannel channel) => datum.Channels.FirstOrDefault(value => value.Channel == channel) is { Value.Kind: not ChartValueKind.Null } raw ? formatter.Format(raw.Value) : "null";
                item = item with { Detail = $"gap; raw X {Raw(FieldChannel.X)}; raw Y {Raw(FieldChannel.Y)}" };
            }
            return offsetDetail is null ? item : item with
            {
                Detail = item.Detail is null ? offsetDetail : $"{item.Detail}; {offsetDetail}"
            };
        })).Concat(layers.Where(layer => layer.Mark == MarkKind.Rule)
            .SelectMany(layer =>
            {
                if (!transposedPhysical)
                    return Enumerable.Repeat(layer, 1);
                var references = ReferenceRuleData(layer.Data).Select(datum => layer with { Data = [datum] });
                return layer.Data.Any(IsRangeRule) ? references : references.DefaultIfEmpty(layer with { Data = [] });
            })
            .Select((layer, index) =>
        {
            var value = layer.Data.SelectMany(datum => datum.Channels)
                .FirstOrDefault(channel => channel.Channel == FieldChannel.Y && channel.Value.Kind != ChartValueKind.Null)
                ?? layer.Data.SelectMany(datum => datum.Channels)
                .FirstOrDefault(channel => channel.Channel == FieldChannel.X && channel.Value.Kind != ChartValueKind.Null);
            var overlayKind = layer.Style.FirstOrDefault(token => token.Name == "overlayType")?.Value;
            var rawLabel = layer.Style.FirstOrDefault(token => token.Name.Equals("label", StringComparison.OrdinalIgnoreCase))?.Value;
            var label = !string.IsNullOrWhiteSpace(rawLabel)
                ? rawLabel
                : (overlayKind == "ReferenceLine" ? "Reference" : overlayKind ?? layer.Id);
            var detail = overlayKind == "ReferenceLine" ? "author reference line" : "labeled reference rule";
            if (transposedPhysical && value is not null)
                detail = $"{value.Channel} = {value.DisplayValue ?? formatter.Format(value.Value)}; {(value.Channel == FieldChannel.X ? "horizontal" : "vertical")} reference rule";
            if (transposedPhysical &&
                layer.Data.FirstOrDefault() is { } rangeDatum && CartesianRangeDescription(rangeDatum) is { } range)
                return new SemanticFallbackItem(label, range, ((sourceLayers.Count + 1) * 100000) + index)
                { Detail = "reference segment", Group = "Reference" };
            return new SemanticFallbackItem(label, value is null ? "" : value.DisplayValue ?? formatter.Format(value.Value), ((sourceLayers.Count + 1) * 100000) + index)
            { Detail = detail, Group = "Reference" };
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
                if (indices.IsDefaultOrEmpty && !spec.IsConnectedComposition) continue;
                var scales = globalScales.Select(scale => Independent(scale, spec.Facet.Resolution, spec, columns, indices, formatter)).ToImmutableArray();
                var panelBounds = new PlotBounds(bounds.X + columnIndex * panelWidth, bounds.Y + rowIndex * panelHeight, panelWidth, panelHeight);
                panels.Add(new ResolvedFacetPanel(
                    $"facet-{rowIndex:D2}-{columnIndex:D2}", rowLabel, columnLabel,
                    panelBounds, indices, scales)
                {
                    CartesianViewport = ResolveCartesianViewport(spec.Coordinate, scales, panelBounds, spec.ResolveCartesianAxes())
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
                CartesianViewport = ResolveCartesianViewport(spec.Coordinate, scales, panelBounds, spec.ResolveCartesianAxes())
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
        CoordinateSpec coordinate, ImmutableArray<ResolvedScale> scales, PlotBounds bounds, ResolvedCartesianAxes? axes = null)
    {
        if (coordinate.AspectRatio is not { } aspectRatio) return null;
        var xScale = scales.First(scale => axes is null ? scale.Channel == FieldChannel.X : scale.Id.Equals(axes.XScaleId, StringComparison.OrdinalIgnoreCase));
        var yScale = scales.First(scale => axes is null ? scale.Channel == FieldChannel.Y : scale.Id.Equals(axes.YScaleId, StringComparison.OrdinalIgnoreCase));
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
        // The ratio follows semantic Y/X units when their physical axes are exchanged.
        var desiredPlotRatio = coordinate.Kind == CoordinateKind.TransposedCartesian
            ? xSpan / (aspectRatio * ySpan)
            : aspectRatio * ySpan / xSpan;
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
        // COMPAT_BREAK: 0.20.0 — independent domains honor case-insensitive scale references too.
        var bindings = spec.Bindings.Concat(spec.Layers.SelectMany(layer => layer.Bindings))
            .Where(binding => string.Equals(binding.ScaleId, scale.Id, StringComparison.OrdinalIgnoreCase) &&
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
            var panelIntent = spec.Scales.Single(candidate => candidate.Id.Equals(scale.Id, StringComparison.OrdinalIgnoreCase));
            var temporalInputs = bindings.SelectMany(binding => rows.Select(index => BindingValue(binding, columns, index)));
            return ResolveTimeScale(
                scale.Id, scale.Kind, scale.Channel, temporalInputs,
                panelIntent.DomainMinimum, panelIntent.DomainMaximum, scale.IncludesZero, scale.Reverse,
                panelIntent.MajorTickCount, panelIntent.TickInterval, scale.MinorTicks, scale.LabelRotation,
                scale.LabelSkip, scale.OuterPadding, scale.TickFormat, scale.TimeUnit, formatter);
        }
        var values = bindings.SelectMany(binding => rows.Select(index => Number(BindingValue(binding, columns, index))))
            .Where(value => value.HasValue).Select(value => value!.Value).ToList();
        if (values.Count == 0) return scale;
        var intent = spec.Scales.Single(candidate => candidate.Id.Equals(scale.Id, StringComparison.OrdinalIgnoreCase));
        // COMPAT_BREAK: 0.20.0 — independent numeric facets retain explicit MIN/MAX bounds.
        var minimum = intent.DomainMinimum is { } lower ? Number(lower)!.Value : values.Min();
        var maximum = intent.DomainMaximum is { } upper ? Number(upper)!.Value : values.Max();
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
                Ticks = ticks.Select(value => new PlotTick(ChartValue.From(value),
                scale.TickFormat is not null ? formatter.Number(value, scale.TickFormat) : formatter.Number(value))).ToImmutableArray(),
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
        ImmutableArray<ResolvedSeries> series,
        ImmutableArray<ResolvedFacetPanel> facets,
        PlotBounds bounds,
        int legendCount)
    {
        var panelByRow = facets.SelectMany(panel => panel.RowIndices.Select(row => (row, panel)))
            .ToDictionary(item => item.row, item => item.panel);

        if (layers.Any(l => l.Position?.Kind == PositionAdjustmentKind.Jitter && l.Position.StableKeyField == "__row_id") &&
            !columns.ContainsKey("__row_id"))
        {
            var rowIndices = Enumerable.Range(0, data.RowCount).Select(i => ChartValue.From((decimal)i)).ToImmutableArray();
            var rowDisplays = Enumerable.Range(0, data.RowCount).Select(i => (string?)i.ToString()).ToImmutableArray();
            var rowCol = new ChartColumn("__row_id", ChartValueKind.Decimal, DataSemanticKind.Nominal, rowIndices, rowDisplays);
            var newCols = columns.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
            newCols["__row_id"] = rowCol;
            columns = newCols;
        }

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

        var physicalTransposed = spec.Coordinate.Kind == CoordinateKind.TransposedCartesian && (spec.Coordinate.AspectRatio is not null || spec.IsTransposedConnected);
        var transposedPlacementViewport = physicalTransposed &&
            layers.Any(layer => layer.Position is { Kind: PositionAdjustmentKind.Jitter } or
            { Kind: PositionAdjustmentKind.Nudge, Unit: PositionAdjustmentUnit.Band or PositionAdjustmentUnit.Data } ||
                layer.Data.Any(datum => datum.Channels.Any(channel => channel.Channel is FieldChannel.XOffset or FieldChannel.YOffset)))
            ? ResolveCartesianViewport(spec.Coordinate, scales, bounds, spec.ResolveCartesianAxes()) ?? bounds : null;

        var dataAreas = new Dictionary<PlotBounds, PlotBounds>();
        PlotBounds DataArea(PlotBounds viewport)
        {
            if (!dataAreas.TryGetValue(viewport, out var area))
                dataAreas[viewport] = area = TransposedAspectLayout.Resolve(viewport, spec.Theme.Tokens, layers, legendCount);
            return area;
        }

        var axes = spec.ResolveCartesianAxes();
        var cartesianViewport = spec.Coordinate.Kind == CoordinateKind.Cartesian && spec.Coordinate.AspectRatio is not null
            ? ResolveCartesianViewport(spec.Coordinate, scales, bounds, axes)
            : null;
        var cartesianAreas = new Dictionary<PlotBounds, PlotBounds>();
        PlotBounds CartesianArea(PlotBounds frame, ResolvedScale? frameXScale)
        {
            if (!cartesianAreas.TryGetValue(frame, out var area))
            {
                cartesianAreas[frame] = area = CartesianPlotLayout.ResolveCartesianArea(
                    frame, spec.Theme.Tokens, layers, series, frameXScale, legendCount);
            }
            return area;
        }

        return layers.Select(layer => layer with
        {
            Data = layer.Data.Select(datum =>
            {
                var panel = panelByRow.GetValueOrDefault(datum.RowIndex);
                var datumScales = panel?.Scales ?? scales;
                var datumBounds = panel?.CartesianViewport ?? panel?.Bounds ?? cartesianViewport ?? bounds;
                var xScale = datumScales.FirstOrDefault(scale => axes is null ? scale.Channel == FieldChannel.X : scale.Id.Equals(axes.XScaleId, StringComparison.OrdinalIgnoreCase));
                var yScale = datumScales.FirstOrDefault(scale => axes is null ? scale.Channel is FieldChannel.Y or FieldChannel.Y2 : scale.Id.Equals(axes.YScaleId, StringComparison.OrdinalIgnoreCase));
                decimal xBand;
                decimal yBand;
                decimal offsetX;
                decimal offsetY;
                if (transposedPlacementViewport is not null)
                {
                    var viewport = panel?.CartesianViewport ?? panel?.Bounds ?? transposedPlacementViewport;
                    xBand = datumBounds.Width / Math.Max(1, xScale?.Categories.Length ?? 1);
                    yBand = datumBounds.Height / Math.Max(1, yScale?.Categories.Length ?? 1);
                    offsetX = ResolveOffsetChannel(datum, FieldChannel.YOffset, datumScales, viewport.Width - 80m, respectReverse: true);
                    offsetY = -ResolveOffsetChannel(datum, FieldChannel.XOffset, datumScales, viewport.Height - 100m, respectReverse: true);
                }
                else if (spec.Coordinate.Kind == CoordinateKind.TransposedCartesian)
                {
                    var plotWidth = Math.Max(0m, datumBounds.Width - CartesianPlotLayout.BaseLeftMargin - CartesianPlotLayout.BaseRightMargin);
                    var plotHeight = Math.Max(0m, datumBounds.Height - CartesianPlotLayout.TopMargin - CartesianPlotLayout.BottomMargin);
                    xBand = plotHeight / Math.Max(1, xScale?.Categories.Length ?? 1);
                    yBand = plotWidth / Math.Max(1, yScale?.Categories.Length ?? 1);
                    offsetX = ResolveOffsetChannel(datum, FieldChannel.YOffset, datumScales, yBand, respectReverse: true);
                    offsetY = ResolveOffsetChannel(datum, FieldChannel.XOffset, datumScales, xBand);
                }
                else
                {
                    var cartesianArea = CartesianArea(datumBounds, xScale);
                    xBand = cartesianArea.Width / Math.Max(1, xScale?.Categories.Length ?? 1);
                    yBand = cartesianArea.Height / Math.Max(1, yScale?.Categories.Length ?? 1);
                    offsetX = ResolveOffsetChannel(datum, FieldChannel.XOffset, datumScales, xBand);
                    offsetY = ResolveOffsetChannel(datum, FieldChannel.YOffset, datumScales, yBand);
                }
                if (layer.Position is { } position)
                {
                    if (position.Kind == PositionAdjustmentKind.Jitter)
                    {
                        var key = ValueKey(columns[position.StableKeyField!].Values[datum.RowIndex])!;
                        var identity = LayerPlacementIdentity(layer);
                        if (transposedPlacementViewport is not null)
                        {
                            // Jitter amplitudes use the fitted plot before renderer-specific legend layout.
                            // Semantic X moves vertically; semantic Y moves horizontally.
                            var viewport = panel?.CartesianViewport ?? panel?.Bounds ?? transposedPlacementViewport;
                            offsetX += SignedHash(spec.Id, identity, key, "y", position.Seed) * position.Y * (viewport.Width - 80m);
                            offsetY -= SignedHash(spec.Id, identity, key, "x", position.Seed) * position.X * (viewport.Height - 100m);
                        }
                        else
                        {
                            offsetX += SignedHash(spec.Id, identity, key, "x", position.Seed) * position.X *
                                (xScale?.Kind is ScaleKind.Band or ScaleKind.Point ? xBand : datumBounds.Width);
                            offsetY += SignedHash(spec.Id, identity, key, "y", position.Seed) * position.Y *
                                (yScale?.Kind is ScaleKind.Band or ScaleKind.Point ? yBand : datumBounds.Height);
                        }
                    }
                    else if (position.Kind == PositionAdjustmentKind.Nudge)
                    {
                        // Display offsets are physical coordinates. In a transposed composition,
                        // semantic X is vertical and semantic Y is horizontal.
                        var viewport = panel?.CartesianViewport ?? panel?.Bounds ?? transposedPlacementViewport;
                        var nudge = physicalTransposed &&
                            position.Unit == PositionAdjustmentUnit.Em
                            ? (X: position.Y * 12m, Y: -position.X * 12m)
                            : transposedPlacementViewport is not null && position.Unit == PositionAdjustmentUnit.Band
                            ? (X: position.Y * (viewport!.Width - 80m), Y: -position.X * (viewport.Height - 100m))
                            : transposedPlacementViewport is not null && position.Unit == PositionAdjustmentUnit.Data
                            ? ResolveTransposedDataNudge(position, datum, xScale!, yScale!, DataArea(viewport!), layer.Id, layer.Mark)
                            : ResolveNudge(position, datum, xScale, yScale, datumBounds, xBand, yBand, layer.Id);
                        offsetX += nudge.X;
                        offsetY += nudge.Y;
                    }
                }
                return datum with { DisplayOffsetX = offsetX, DisplayOffsetY = offsetY };
            }).ToImmutableArray()
        }).ToImmutableArray();
    }

    private static decimal ResolveOffsetChannel(ResolvedDatum datum, FieldChannel channel,
        ImmutableArray<ResolvedScale> scales, decimal band, bool respectReverse = false)
    {
        var value = Channel(datum, channel);
        if (value is null || value.Kind == ChartValueKind.Null) return 0m;
        var scale = scales.FirstOrDefault(scale => scale.Channel == channel);
        var categories = scale?.Categories ?? [];
        var index = categories.IndexOf(Display(value));
        if (index < 0 || categories.Length < 2) return 0m;
        if (respectReverse && scale!.Reverse) index = categories.Length - 1 - index;
        return (((index + .5m) / categories.Length) - .5m) * band;
    }

    private static (decimal X, decimal Y) ResolveTransposedDataNudge(PositionAdjustmentSpec position, ResolvedDatum datum,
        ResolvedScale xScale, ResolvedScale yScale, PlotBounds area, string layerId, MarkKind mark)
    {
        if (mark is (MarkKind.Line or MarkKind.Area) && datum.IsGap) return (0m, 0m);
        var x = Number(Channel(datum, FieldChannel.X) ?? ChartValue.Null());
        var y = Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
        // A ribbon's authored start anchors the whole cross-section, including crossing bounds.
        if (mark == MarkKind.Area && (Channel(datum, FieldChannel.YStart) ?? Channel(datum, FieldChannel.ConfidenceLow)) is { } start)
            y = Number(start);
        if (mark is (MarkKind.Rule or MarkKind.Rect) && IsRangeRule(datum))
        {
            // Translate the complete range geometry by its authored start point's displacement.
            // The end point retains its screen-space distance from the start, including on log axes.
            x = Number(Channel(datum, FieldChannel.XStart) ?? Channel(datum, FieldChannel.X) ?? ChartValue.Null());
            y = Number(Channel(datum, FieldChannel.YStart) ?? Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            var endX = Number(Channel(datum, FieldChannel.XEnd) ?? Channel(datum, FieldChannel.X) ?? ChartValue.Null());
            var endY = Number(Channel(datum, FieldChannel.YEnd) ?? Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            if (datum.IsGap || !x.HasValue || !y.HasValue || !endX.HasValue || !endY.HasValue) return (0m, 0m);
        }
        else if (mark == MarkKind.Rule)
        {
            // Missing thresholds draw no rule; only the bound scale participates in displacement.
            if (datum.IsGap) return (0m, 0m);
            if (x.HasValue)
                return (0m, DataNudge(x.Value, position.X, xScale, area.Height, layerId, "X") * (xScale.Reverse ? 1m : -1m));
            if (y.HasValue)
                return (DataNudge(y.Value, position.Y, yScale, area.Width, layerId, "Y") * (yScale.Reverse ? -1m : 1m), 0m);
            return (0m, 0m);
        }
        if (!x.HasValue || !y.HasValue)
            throw new InvalidOperationException($"Layer '{layerId}' data-domain NUDGE requires numeric X and Y datum values.");
        return (DataNudge(y.Value, position.Y, yScale, area.Width, layerId, "Y") * (yScale.Reverse ? -1m : 1m),
            DataNudge(x.Value, position.X, xScale, area.Height, layerId, "X") * (xScale.Reverse ? 1m : -1m));
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
        ImmutableArray<ResolvedFacetPanel> facets, ImmutableArray<int> gaps, ImmutableArray<int> skipped)
    {
        var summary = $"{spec.Title ?? spec.Id}: {data.RowCount} rows, {layers.Length} ordered layers, {series.Length} series, " +
        $"{(facets.IsDefaultOrEmpty ? 1 : facets.Length)} facet panels, {gaps.Length} gaps, {skipped.Length} skipped rows." +
        (scales.FirstOrDefault(scale => scale.ColorRange is not null)?.ColorRange is { } range ? " " + range.AccessibleDescription : string.Empty);
        var zeroCount = layers.SelectMany(layer => layer.Data).Count(datum => ConnectedMarkResolver.ZeroDetail(datum) is not null);
        foreach (var layer in layers.Where(layer => layer.PathInterpolation is not null))
            summary += $" {layer.Id}: {ResolvedConnectionGeometry.Name(layer.PathInterpolation!.Value)} interpolation after scale mapping and placement; X vertical, Y horizontal.";
        foreach (var layer in layers.Where(layer => layer.AreaConfidence))
            summary += $" {layer.Id}: confidence interval from authored low/high bounds; X vertical, Y horizontal.";
        return zeroCount > 0 ? summary + $" {zeroCount} null Y values rendered at zero; raw values retained." : summary;
    }

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

    private enum TemporalValueShape { None, Date, Time, LocalDateTime, OffsetDateTime }

    internal static decimal? TemporalTicks(ChartValue? value) =>
        value is not null && TryGetTemporalShape(value, out var ticks, out _) ? (decimal)ticks : null;

    internal static decimal? PositionNumber(ChartValue? value) => value is null
        ? null
        : Number(value) ?? TemporalTicks(value);

    private static bool TryGetTemporalShape(ChartValue value, out long ticks, out TemporalValueShape shape)
    {
        switch (value.Kind)
        {
            case ChartValueKind.Date when value.Date.HasValue:
                ticks = value.Date.Value.DayNumber * TimeSpan.TicksPerDay;
                shape = TemporalValueShape.Date;
                return true;
            case ChartValueKind.Time when value.Time.HasValue:
                ticks = value.Time.Value.Ticks;
                shape = TemporalValueShape.Time;
                return true;
            case ChartValueKind.LocalDateTime when value.LocalDateTime.HasValue:
                ticks = value.LocalDateTime.Value.Ticks;
                shape = TemporalValueShape.LocalDateTime;
                return true;
            case ChartValueKind.OffsetDateTime when value.OffsetDateTime.HasValue:
                ticks = value.OffsetDateTime.Value.UtcTicks;
                shape = TemporalValueShape.OffsetDateTime;
                return true;
            case ChartValueKind.Text when !string.IsNullOrWhiteSpace(value.Text):
                {
                    var text = value.Text.Trim();
                    if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateOnly))
                    {
                        ticks = dateOnly.DayNumber * TimeSpan.TicksPerDay;
                        shape = TemporalValueShape.Date;
                        return true;
                    }
                    if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var timeOnly))
                    {
                        ticks = timeOnly.Ticks;
                        shape = TemporalValueShape.Time;
                        return true;
                    }
                    if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var dt))
                    {
                        if (dt.Kind == DateTimeKind.Unspecified)
                        {
                            ticks = dt.Ticks;
                            shape = TemporalValueShape.LocalDateTime;
                            return true;
                        }
                        ticks = dt.ToUniversalTime().Ticks;
                        shape = TemporalValueShape.OffsetDateTime;
                        return true;
                    }
                    if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var dto))
                    {
                        ticks = dto.UtcTicks;
                        shape = TemporalValueShape.OffsetDateTime;
                        return true;
                    }
                    ticks = 0;
                    shape = TemporalValueShape.None;
                    return false;
                }
            default:
                ticks = 0;
                shape = TemporalValueShape.None;
                return false;
        }
    }

    private static ResolvedScale ResolveTimeScale(
        string scaleId,
        ScaleKind kind,
        FieldChannel channel,
        IEnumerable<ChartValue> rawValues,
        ChartValue? domainMinimum,
        ChartValue? domainMaximum,
        bool includeZero,
        bool reverse,
        int? majorTickCount,
        decimal? tickInterval,
        bool minorTicks,
        string? labelRotation,
        int? labelSkip,
        decimal outerPadding,
        string? tickFormat,
        string? timeUnit,
        ChartValueFormatter formatter)
    {
        if (majorTickCount is < 2 or > 100)
            throw new InvalidOperationException($"MAJOR_TICK_COUNT for scale '{scaleId}' must be between 2 and 100.");
        if (tickInterval is <= 0m)
            throw new InvalidOperationException($"TICK_INTERVAL for scale '{scaleId}' must be greater than zero.");

        var parsedTicks = new List<long>();
        bool hasDate = false, hasTime = false, hasLocal = false, hasOffset = false;

        void RecordShape(TemporalValueShape shape)
        {
            switch (shape)
            {
                case TemporalValueShape.Date: hasDate = true; break;
                case TemporalValueShape.Time: hasTime = true; break;
                case TemporalValueShape.LocalDateTime: hasLocal = true; break;
                case TemporalValueShape.OffsetDateTime: hasOffset = true; break;
            }
        }

        foreach (var item in rawValues)
        {
            if (item.Kind == ChartValueKind.Null) continue;
            if (TryGetTemporalShape(item, out var itemTicks, out var shape))
            {
                parsedTicks.Add(itemTicks);
                RecordShape(shape);
            }
        }

        long? minOverride = null;
        if (domainMinimum is { Kind: not ChartValueKind.Null })
        {
            if (!TryGetTemporalShape(domainMinimum, out var minTickValue, out var minShape))
                throw new InvalidOperationException($"MIN bound '{Display(domainMinimum)}' for temporal scale '{scaleId}' must be a valid date or time.");
            minOverride = minTickValue;
            RecordShape(minShape);
        }

        long? maxOverride = null;
        if (domainMaximum is { Kind: not ChartValueKind.Null })
        {
            if (!TryGetTemporalShape(domainMaximum, out var maxTickValue, out var maxShape))
                throw new InvalidOperationException($"MAX bound '{Display(domainMaximum)}' for temporal scale '{scaleId}' must be a valid date or time.");
            maxOverride = maxTickValue;
            RecordShape(maxShape);
        }

        if (minOverride.HasValue && maxOverride.HasValue && minOverride.Value > maxOverride.Value)
            throw new InvalidOperationException($"MIN bound for scale '{scaleId}' cannot exceed MAX bound.");

        if (parsedTicks.Count == 0 && !minOverride.HasValue && !maxOverride.HasValue)
        {
            return new ResolvedScale(scaleId, channel, kind, [], [], [], includeZero,
                reverse, minorTicks, labelRotation, labelSkip, outerPadding, tickFormat, timeUnit);
        }

        var minTicks = minOverride ?? (parsedTicks.Count > 0 ? parsedTicks.Min() : maxOverride!.Value);
        var maxTicks = maxOverride ?? (parsedTicks.Count > 0 ? parsedTicks.Max() : minOverride!.Value);
        if (minTicks > maxTicks) (minTicks, maxTicks) = (maxTicks, minTicks);

        var isTimeOnly = hasTime && !hasDate && !hasLocal && !hasOffset;
        var isDateOnly = hasDate && !hasTime && !hasLocal && !hasOffset;

        if (minTicks == maxTicks)
        {
            if (isTimeOnly)
            {
                if (minTicks + TimeSpan.TicksPerHour <= TimeSpan.TicksPerDay - 1L)
                    maxTicks = minTicks + TimeSpan.TicksPerHour;
                else
                    minTicks = Math.Max(0L, maxTicks - TimeSpan.TicksPerHour);
            }
            else
            {
                maxTicks = minTicks + TimeSpan.TicksPerDay;
            }
        }

        ChartValue ToChartValue(long ticks)
        {
            if (isTimeOnly)
                return ChartValue.From(new TimeOnly(Math.Clamp(ticks, 0L, TimeSpan.TicksPerDay - 1L)));
            if (hasOffset)
                return ChartValue.From(new DateTimeOffset(ticks, TimeSpan.Zero));
            if (isDateOnly && ticks % TimeSpan.TicksPerDay == 0L)
                return ChartValue.From(DateOnly.FromDayNumber((int)(ticks / TimeSpan.TicksPerDay)));
            return ChartValue.FromLocal(new DateTime(ticks, DateTimeKind.Unspecified));
        }

        var normalizedUnit = timeUnit?.Trim().ToUpperInvariant();
        var unitFormat = normalizedUnit switch
        {
            "DAY" => "yyyy-MM-dd",
            "WEEK" => "yyyy-MM-dd",
            "MONTH" => "yyyy-MM",
            "QUARTER" => "QUARTER",
            "YEAR" => "yyyy",
            _ => null
        };

        string FormatTickLabel(ChartValue tickValue, long ticks)
        {
            if (tickFormat is not null)
                return formatter.FormatWithPattern(tickValue, tickFormat);
            if (unitFormat == "QUARTER")
            {
                var dt = new DateTime(ticks, DateTimeKind.Unspecified);
                return $"{dt.Year}-Q{(dt.Month - 1) / 3 + 1}";
            }
            if (unitFormat is not null)
                return formatter.FormatWithPattern(tickValue, unitFormat);
            return formatter.Format(tickValue);
        }

        List<long> GenerateFixedStepTicks(long min, long max, long stepTicks, bool alignToDay)
        {
            var result = new List<long>();
            if (stepTicks <= 0L) return result;
            long current;
            if (alignToDay && !isTimeOnly && stepTicks % TimeSpan.TicksPerDay == 0L)
            {
                var startDay = new DateTime(min, DateTimeKind.Unspecified).Date;
                if (startDay.Ticks < min) startDay = startDay.AddDays(1);
                current = startDay.Ticks;
            }
            else
            {
                var rem = min % stepTicks;
                current = rem == 0L ? min : min + (stepTicks - rem);
            }
            while (current <= max && result.Count < 100)
            {
                result.Add(current);
                if (max - current < stepTicks) break;
                current += stepTicks;
            }
            return result;
        }

        List<long> GenerateCalendarTicks(long min, long max, int monthsStep)
        {
            var result = new List<long>();
            if (monthsStep <= 0) return result;
            var startDt = new DateTime(min, DateTimeKind.Unspecified);
            var startMonth = monthsStep >= 12 ? 1 : ((startDt.Month - 1) / monthsStep) * monthsStep + 1;
            var yearsStep = monthsStep >= 12 ? monthsStep / 12 : 1;
            var startYear = yearsStep > 1 ? Math.Max(1, (startDt.Year / yearsStep) * yearsStep) : startDt.Year;
            var current = new DateTime(startYear, startMonth, 1, 0, 0, 0, DateTimeKind.Unspecified);
            while (current.Ticks < min)
            {
                if (current > DateTime.MaxValue.AddMonths(-monthsStep)) return result;
                current = current.AddMonths(monthsStep);
            }
            while (current.Ticks <= max && result.Count < 100)
            {
                result.Add(current.Ticks);
                if (current > DateTime.MaxValue.AddMonths(-monthsStep)) break;
                current = current.AddMonths(monthsStep);
            }
            return result;
        }

        List<long> tickPositions;
        if (tickInterval is { } interval)
        {
            tickPositions = normalizedUnit switch
            {
                "YEAR" => GenerateCalendarTicks(minTicks, maxTicks, Math.Max(1, (int)Math.Round(interval)) * 12),
                "QUARTER" => GenerateCalendarTicks(minTicks, maxTicks, Math.Max(1, (int)Math.Round(interval)) * 3),
                "MONTH" => GenerateCalendarTicks(minTicks, maxTicks, Math.Max(1, (int)Math.Round(interval))),
                "WEEK" => GenerateFixedStepTicks(minTicks, maxTicks, Math.Max(1L, (long)Math.Round((double)interval * 7d * TimeSpan.TicksPerDay)), alignToDay: true),
                "DAY" => GenerateFixedStepTicks(minTicks, maxTicks, Math.Max(1L, (long)Math.Round((double)interval * TimeSpan.TicksPerDay)), alignToDay: true),
                _ when isTimeOnly => GenerateFixedStepTicks(
                    minTicks, maxTicks,
                    (maxTicks - minTicks) / ((decimal)interval * TimeSpan.TicksPerHour) >= 1m
                        ? Math.Max(1L, (long)Math.Round((double)interval * TimeSpan.TicksPerHour))
                        : (maxTicks - minTicks) / ((decimal)interval * TimeSpan.TicksPerMinute) >= 1m
                            ? Math.Max(1L, (long)Math.Round((double)interval * TimeSpan.TicksPerMinute))
                            : Math.Max(1L, (long)Math.Round((double)interval * TimeSpan.TicksPerSecond)),
                    alignToDay: false),
                _ => GenerateFixedStepTicks(minTicks, maxTicks, Math.Max(1L, (long)Math.Round((double)interval * TimeSpan.TicksPerDay)), alignToDay: true)
            };
            if (tickPositions.Count == 0) tickPositions = [minTicks, maxTicks];
        }
        else
        {
            var targetCount = majorTickCount ?? 5;
            var span = maxTicks - minTicks;
            var candidates = new List<List<long>>();
            var minStepFloor = span / 120L;

            if (!isDateOnly && normalizedUnit is null or "AUTO")
            {
                long[] subDaySteps =
                [
                    TimeSpan.TicksPerSecond,
                    5L * TimeSpan.TicksPerSecond,
                    15L * TimeSpan.TicksPerSecond,
                    30L * TimeSpan.TicksPerSecond,
                    TimeSpan.TicksPerMinute,
                    2L * TimeSpan.TicksPerMinute,
                    5L * TimeSpan.TicksPerMinute,
                    10L * TimeSpan.TicksPerMinute,
                    15L * TimeSpan.TicksPerMinute,
                    30L * TimeSpan.TicksPerMinute,
                    TimeSpan.TicksPerHour,
                    2L * TimeSpan.TicksPerHour,
                    3L * TimeSpan.TicksPerHour,
                    4L * TimeSpan.TicksPerHour,
                    6L * TimeSpan.TicksPerHour,
                    12L * TimeSpan.TicksPerHour
                ];
                foreach (var step in subDaySteps)
                {
                    if (step < minStepFloor || step > span) continue;
                    candidates.Add(GenerateFixedStepTicks(minTicks, maxTicks, step, alignToDay: false));
                }
            }

            if (!isTimeOnly && normalizedUnit is null or "AUTO" or "DAY" or "WEEK")
            {
                int[] daySteps = normalizedUnit == "WEEK" ? [7, 14, 28] : [1, 2, 3, 4, 5, 7, 10, 14];
                foreach (var days in daySteps)
                {
                    var step = days * TimeSpan.TicksPerDay;
                    if (step < minStepFloor || step > span) continue;
                    candidates.Add(GenerateFixedStepTicks(minTicks, maxTicks, step, alignToDay: true));
                }
            }

            if (!isTimeOnly && normalizedUnit is not "DAY" and not "WEEK")
            {
                int[] monthSteps = normalizedUnit switch
                {
                    "YEAR" => [12, 24, 60, 120, 240, 600, 1200],
                    "QUARTER" => [3, 6, 12, 24, 60, 120],
                    "MONTH" => [1, 2, 3, 6, 12, 24, 60, 120],
                    _ => [1, 2, 3, 6, 12, 24, 60, 120, 240, 600, 1200]
                };
                foreach (var months in monthSteps)
                    candidates.Add(GenerateCalendarTicks(minTicks, maxTicks, months));
            }

            var best = candidates
                .Where(list => list.Count >= 2 && list.Count <= Math.Max(2, targetCount + 2))
                .OrderBy(list => Math.Abs(list.Count - targetCount))
                .ThenByDescending(list => list.Count)
                .FirstOrDefault();

            if (best is not null)
            {
                tickPositions = best;
            }
            else
            {
                var count = Math.Clamp(targetCount, 2, 100);
                tickPositions = Enumerable.Range(0, count)
                    .Select(index => minTicks + (long)Math.Round((decimal)span * index / (count - 1m)))
                    .Distinct()
                    .ToList();
                if (tickPositions.Count < 2) tickPositions = [minTicks, maxTicks];
            }
        }

        var ticks = tickPositions
            .Select(tick =>
            {
                var value = ToChartValue(tick);
                return new PlotTick(value, FormatTickLabel(value, tick));
            })
            .ToImmutableArray();

        return new ResolvedScale(
            scaleId, channel, kind,
            [ToChartValue(minTicks), ToChartValue(maxTicks)],
            [],
            ticks,
            includeZero, reverse, minorTicks, labelRotation, labelSkip, outerPadding, tickFormat, timeUnit);
    }

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

    internal static bool IsRangeRule(ResolvedDatum datum) => datum.Channels.Any(channel =>
        channel.Channel is FieldChannel.XStart or FieldChannel.XEnd or FieldChannel.YStart or FieldChannel.YEnd);

    internal static string? CartesianRangeDescription(ResolvedDatum datum, bool transposed = false)
    {
        string? Value(FieldChannel channel)
        {
            var resolved = datum.Channels.FirstOrDefault(value => value.Channel == channel);
            return resolved is null || Number(resolved.Value) is null ? null : resolved.DisplayValue ?? Display(resolved.Value);
        }
        var x = Value(FieldChannel.X);
        var y = Value(FieldChannel.Y);
        var xs = Value(FieldChannel.XStart);
        var xe = Value(FieldChannel.XEnd);
        var ys = Value(FieldChannel.YStart);
        var ye = Value(FieldChannel.YEnd);
        if (transposed) (x, y, xs, xe, ys, ye) = (y, x, ys, ye, xs, xe);
        if (xs is not null && xe is not null && ys is not null && ye is not null) return $"X = {xs} to {xe}; Y = {ys} to {ye}";
        if (x is not null && ys is not null && ye is not null) return $"X = {x}; Y = {ys} to {ye}";
        if (y is not null && xs is not null && xe is not null) return $"X = {xs} to {xe}; Y = {y}";
        return null;
    }

    /// <summary>Complete range rows, or one reference per distinct numeric threshold, in source order.</summary>
    internal static IEnumerable<ResolvedDatum> ReferenceRuleData(IEnumerable<ResolvedDatum> data)
    {
        var seen = new HashSet<(FieldChannel Channel, decimal Value)>();
        foreach (var datum in data)
        {
            if (IsRangeRule(datum))
            {
                if (!datum.IsGap && CartesianRangeDescription(datum) is not null) yield return datum;
                continue;
            }
            var channel = datum.Channels.FirstOrDefault(channel => channel.Channel is FieldChannel.X or FieldChannel.Y);
            if (datum.IsGap || channel is null || Number(channel.Value) is not { } number) continue;
            if (seen.Add((channel.Channel, number))) yield return datum;
        }
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
