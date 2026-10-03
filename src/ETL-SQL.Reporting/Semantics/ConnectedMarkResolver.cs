using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ETL_SQL.Reporting.Semantics.Runtime;

internal static class ConnectedMarkResolver
{
    internal static ImmutableArray<ResolvedMarkLayer> ResolveDecorations(ImmutableArray<ResolvedMarkLayer> layers, ImmutableArray<StyleToken> style) => layers.Select(layer => !layer.ConnectionDecorations ? layer : layer with
    {
        Data = layer.Data.Select(datum => datum with { ConnectionDecoration = ResolvedConnectionDecoration.FromRaw(datum, layer, style) }).ToImmutableArray()
    }).ToImmutableArray();

    internal static string DecorationDescription(ResolvedDatum datum)
    {
        if (datum.ConnectionDecoration is not { } decoration)
            return "no decoration anchor" + (datum.Encodings.IsDefaultOrEmpty ? "" : "; " + string.Join(", ", datum.Encodings
                .Where(value => ResolvedConnectionDecoration.IsDecorationChannel(value.Channel)).Select(value => $"{value.Channel}: {PlotPlanResolver.Display(value.Value)}")));
        var detail = $"symbol radius {decoration.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture)} px; shape {decoration.Shape}";
        if (decoration.Text is { } text) detail += string.IsNullOrWhiteSpace(PlotPlanResolver.Display(text)) ? "; text suppressed" : $"; text {PlotPlanResolver.Display(text)}";
        return detail;
    }

    internal static ImmutableArray<ResolvedMarkLayer> ResolveZeroGeometry(ImmutableArray<ResolvedMarkLayer> layers) => layers.Select(layer =>
        layer.Connections.IsDefault || !ResolvedMarkConnection.FillsNullsWithZero(layer) ? layer : layer with
        {
            Data = layer.Data.Select(datum =>
            {
                var coordinates = ResolvedConnectionCoordinates.FromRaw(datum, layer.AreaRibbon, true);
                return datum with { ConnectionCoordinates = coordinates, IsGap = coordinates is null };
            }).ToImmutableArray()
        }).ToImmutableArray();

    internal static string? ZeroDetail(ResolvedDatum datum) => datum.ConnectionCoordinates?.Y == 0m &&
        datum.Channels.Any(channel => channel.Channel == FieldChannel.Y && channel.Value.Kind == ChartValueKind.Null)
            ? "null Y rendered at zero" : null;

    internal static string DatumDescription(ResolvedDatum datum, ResolvedMarkLayer layer)
    {
        string Raw(FieldChannel channel)
        {
            var value = datum.Channels.FirstOrDefault(value => value.Channel == channel)?.Value ?? ChartValue.Null();
            return value.Kind == ChartValueKind.Null ? "null" : PlotPlanResolver.Display(value);
        }
        var values = layer.AreaRibbon ? $"X {Raw(FieldChannel.X)}; Y {Raw(FieldChannel.YStart)} to {Raw(FieldChannel.YEnd)}"
            : $"X {Raw(FieldChannel.X)}; Y {Raw(FieldChannel.Y)}{(layer.Mark == MarkKind.Area ? "; baseline 0" : "")}";
        if (!ResolvedMarkConnection.HasCompleteCoordinates(datum, layer.AreaRibbon)) values += "; gap";
        if (ZeroDetail(datum) is { } zero) values += $"; {zero}";
        return values;
    }

    internal static string RibbonDescription(ResolvedDatum datum, bool preserveRawGap = false, bool confidence = false)
    {
        string Value(FieldChannel channel)
        {
            var value = datum.Channels.FirstOrDefault(value => value.Channel == channel)?.Value ?? ChartValue.Null();
            return preserveRawGap && value.Kind == ChartValueKind.Null ? "null" : PlotPlanResolver.Display(value);
        }
        return datum.IsGap && !preserveRawGap ? "gap" : $"X {Value(FieldChannel.X)}; {(confidence ? "confidence " : "")}Y {Value(confidence ? FieldChannel.ConfidenceLow : FieldChannel.YStart)} to {Value(confidence ? FieldChannel.ConfidenceHigh : FieldChannel.YEnd)}{(datum.IsGap ? "; gap" : "")}";
    }

    internal static PlotPlan Attach(PlotPlan plan)
    {
        var composition = plan.CartesianAxes is not null && plan.Coordinate?.Kind is (CoordinateKind.Cartesian or CoordinateKind.TransposedCartesian);
        var layers = plan.Layers.Select(layer => layer.Connections.IsDefault ? layer : layer with
        {
            Connections = ResolveConnections(layer, plan.Facets),
            ConnectionSkippedRows = composition ? ResolvedMarkConnection.ConnectsAcrossNulls(layer)
                ? layer.Data.Where(datum => !ResolvedMarkConnection.HasCompleteCoordinates(datum, layer.AreaRibbon)).Select(datum => datum.RowIndex).Distinct().Order().ToImmutableArray() : [] : default
        }).ToImmutableArray();
        layers = layers.Select(layer =>
        {
            if (layer.ConnectionInterpolation is null) return layer;
            var geometry = ResolvedConnectionGeometry.Resolve(layer);
            return layer with { Connections = layer.Connections.Select((connection, index) => connection with { Geometry = geometry[index] }).ToImmutableArray() };
        }).ToImmutableArray();
        var connect = layers.Any(ResolvedMarkConnection.ConnectsAcrossNulls);
        var zero = layers.Any(layer => !layer.Connections.IsDefault && ResolvedMarkConnection.FillsNullsWithZero(layer));
        var descriptions = layers.Where(layer => !layer.Connections.IsDefault).SelectMany(layer => layer.Connections.Select(connection =>
            new SemanticFallbackItem($"Row {connection.SourceRowIndex + 1}", Describe(connection, composition ? plan : null, layer),
                plan.Fallback.Items.Length + connection.SourceIndex)
            { Group = layer.Id, Detail = "outgoing connection" })).ToImmutableArray();
        var resolved = plan with
        {
            Schema = layers.Any(layer => layer.AreaConfidence) ? ChartContractVersions.TransposedConfidencePlotPlanSchema : layers.Any(layer => layer.Mark == MarkKind.Area && layer.PathInterpolation is not null) ? ChartContractVersions.OrdinaryAreaInterpolationPlotPlanSchema : layers.Any(layer => layer.PathInterpolation is not null) ? ChartContractVersions.OrdinaryInterpolationPlotPlanSchema : layers.Any(layer => layer.ConnectionInterpolation is not null) ? ChartContractVersions.InterpolatedConnectedPlotPlanSchema : layers.Any(layer => layer.ConnectionDecorations) ? ChartContractVersions.DecoratedConnectedPlotPlanSchema : plan.Coordinate?.Kind == CoordinateKind.TransposedCartesian ? ChartContractVersions.TransposedConnectedPlotPlanSchema : zero ? ChartContractVersions.ZeroConnectedPlotPlanSchema : composition ? ChartContractVersions.ConnectedCompositionPlotPlanSchema : connect ? ChartContractVersions.ConnectPlotPlanSchema : layers.Any(layer => layer.AreaRibbonScaleId is not null) ? ChartContractVersions.ScaledRibbonPlotPlanSchema : ChartContractVersions.ConnectedPlotPlanSchema,
            Version = layers.Any(layer => layer.AreaConfidence) ? ChartContractVersions.TransposedConfidencePlotPlanVersion : layers.Any(layer => layer.Mark == MarkKind.Area && layer.PathInterpolation is not null) ? ChartContractVersions.OrdinaryAreaInterpolationPlotPlanVersion : layers.Any(layer => layer.PathInterpolation is not null) ? ChartContractVersions.OrdinaryInterpolationPlotPlanVersion : layers.Any(layer => layer.ConnectionInterpolation is not null) ? ChartContractVersions.InterpolatedConnectedPlotPlanVersion : layers.Any(layer => layer.ConnectionDecorations) ? ChartContractVersions.DecoratedConnectedPlotPlanVersion : plan.Coordinate?.Kind == CoordinateKind.TransposedCartesian ? ChartContractVersions.TransposedConnectedPlotPlanVersion : zero ? ChartContractVersions.ZeroConnectedPlotPlanVersion : composition ? ChartContractVersions.ConnectedCompositionPlotPlanVersion : connect ? ChartContractVersions.ConnectPlotPlanVersion : layers.Any(layer => layer.AreaRibbonScaleId is not null) ? ChartContractVersions.ScaledRibbonPlotPlanVersion : ChartContractVersions.ConnectedPlotPlanVersion,
            Layers = layers,
            Fallback = plan.Fallback with { Items = plan.Fallback.Items.AddRange(descriptions) }
        };
        resolved.Validate();
        return resolved;
    }

    internal static string Describe(ResolvedMarkConnection connection, PlotPlan? plan = null, ResolvedMarkLayer? layer = null, bool includeOwner = true, bool includeInterpolation = true)
    {
        var owner = "";
        if (includeOwner && plan?.CartesianAxes is not null && plan.Coordinate?.Kind is (CoordinateKind.Cartesian or CoordinateKind.TransposedCartesian) && layer is not null)
        {
            var series = plan.Series.FirstOrDefault(series => series.Key == layer.SeriesKey)?.Label;
            var facet = connection.FacetId is null ? null : plan.Facets.FirstOrDefault(panel => panel.Id == connection.FacetId);
            var facetLabel = facet is null ? plan.Title : string.Join(" / ", new[] { facet.RowLabel, facet.ColumnLabel }.Where(value => value is not null));
            if (string.IsNullOrEmpty(facetLabel)) facetLabel = "(null)";
            owner = $"Layer {layer.Id}{(series is null ? "" : $", series {series}")}{(connection.FacetId is null ? "" : $", facet {facetLabel}")}; ";
            if (plan.Coordinate.Kind == CoordinateKind.TransposedCartesian) owner += "X vertical, Y horizontal; ";
        }
        var geometry = "";
        if (layer is not null && ResolvedMarkConnection.FillsNullsWithZero(layer))
        {
            if (ZeroDetail(layer.Data[connection.SourceIndex]) is { } source) geometry += $"; source {source}";
            if (ZeroDetail(layer.Data[connection.DestinationIndex]) is { } destination) geometry += $"; destination {destination}";
        }
        if (includeInterpolation && layer?.ConnectionInterpolation is { } interpolation) geometry += "; interpolation " + ResolvedConnectionGeometry.Name(interpolation);
        return owner + $"Row {connection.SourceRowIndex + 1} to row {connection.DestinationRowIndex + 1}; source-owned " +
        (connection.Encodings.IsDefaultOrEmpty ? "layer presentation" : string.Join(", ",
            connection.Encodings.Select(encoding => $"{encoding.Channel}: {PlotPlanResolver.Display(encoding.Value)}"))) + geometry;
    }

    internal static ResolvedMarkLayer SelectFacet(ResolvedMarkLayer layer, string facetId, IReadOnlySet<int> rows)
    {
        var selected = layer.Data.Select((datum, index) => (Datum: datum, Index: index)).Where(item => rows.Contains(item.Datum.RowIndex)).ToArray();
        var indices = selected.Select((item, index) => (item.Index, NewIndex: index)).ToDictionary(item => item.Index, item => item.NewIndex);
        return layer with
        {
            Data = selected.Select(item => item.Datum).ToImmutableArray(),
            Connections = layer.Connections.IsDefault ? default : layer.Connections.Where(connection => connection.FacetId == facetId).Select(connection => connection with
            { SourceIndex = indices[connection.SourceIndex], DestinationIndex = indices[connection.DestinationIndex] }).ToImmutableArray(),
            ConnectionSkippedRows = layer.ConnectionSkippedRows.IsDefault ? default : layer.ConnectionSkippedRows.Where(rows.Contains).ToImmutableArray()
        };
    }

    // Input is one already partitioned layer/facet with resolved coordinates and null policy.
    // Array indices retain identity even when row identifiers or coordinates coincide.
    internal static ImmutableArray<ResolvedMarkConnection> ResolveConnections(ResolvedMarkLayer layer, ImmutableArray<ResolvedFacetPanel> facets = default)
    {
        if (layer.Mark is not (MarkKind.Line or MarkKind.Area))
            throw new ArgumentException("Connections require a LINE or AREA layer.", nameof(layer));
        if (layer.Data.IsDefaultOrEmpty) return [];

        var connections = ImmutableArray.CreateBuilder<ResolvedMarkConnection>();
        var partitions = facets.IsDefaultOrEmpty ? new[] { (Id: (string?)null, Rows: (IReadOnlySet<int>?)null) }
            : facets.Select(facet => (Id: (string?)facet.Id, Rows: (IReadOnlySet<int>?)facet.RowIndices.ToHashSet())).ToArray();
        foreach (var partition in partitions)
            foreach (var (index, destinationIndex) in ResolvedMarkConnection.ExpectedPairs(layer, partition.Rows))
            {
                var source = layer.Data[index];
                var destination = layer.Data[destinationIndex];

                var encodings = source.Encodings.IsDefaultOrEmpty
                    ? ImmutableArray<ResolvedEncodingValue>.Empty
                    : source.Encodings.Where(encoding => encoding.Channel is
                        ConditionalEncodingChannel.Color or ConditionalEncodingChannel.Opacity).ToImmutableArray();
                connections.Add(new ResolvedMarkConnection(index, destinationIndex, source.RowIndex,
                    destination.RowIndex, encodings)
                { FacetId = partition.Id });
            }
        return connections.ToImmutable();
    }
}
