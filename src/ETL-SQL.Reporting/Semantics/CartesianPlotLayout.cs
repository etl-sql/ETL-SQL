using System;
using System.Collections.Immutable;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>
/// Computes the physical <c>CartesianPlotArea</c> rectangle shared between
/// <see cref="PlotPlanResolver"/> (for offset/jitter/nudge sizing) and
/// <see cref="Renderers.PlotPlanSvgRenderer"/>.
/// </summary>
internal static class CartesianPlotLayout
{
    internal const decimal BaseLeftMargin = 60m;
    internal const decimal BaseRightMargin = 20m;
    internal const decimal TopMargin = 40m;
    internal const decimal BottomMargin = 60m;
    internal const decimal MinimumPlotWidth = 100m;

    internal static PlotBounds ResolveCartesianArea(
        PlotBounds frame,
        ImmutableArray<StyleToken> style,
        ImmutableArray<ResolvedMarkLayer> layers,
        ImmutableArray<ResolvedSeries> series,
        ResolvedScale? xScale,
        int legendCount,
        decimal topMargin = TopMargin,
        decimal bottomMargin = BottomMargin,
        string? legendPositionOverride = null,
        decimal leftMargin = BaseLeftMargin) =>
        ResolveCartesianMetrics(frame, style, layers, series, xScale, legendCount, topMargin, bottomMargin, legendPositionOverride, leftMargin).Area;

    internal static (PlotBounds Area, decimal SideLabelsGutter, decimal SideLabelsRight) ResolveCartesianMetrics(
        PlotBounds frame,
        ImmutableArray<StyleToken> style,
        ImmutableArray<ResolvedMarkLayer> layers,
        ImmutableArray<ResolvedSeries> series,
        ResolvedScale? xScale,
        int legendCount,
        decimal topMargin = TopMargin,
        decimal bottomMargin = BottomMargin,
        string? legendPositionOverride = null,
        decimal leftMargin = BaseLeftMargin)
    {
        var totalWidth = frame.Width;
        var minPlotWidth = totalWidth >= 180m ? MinimumPlotWidth : Math.Max(30m, totalWidth - 40m);

        var legendEnabled = LegendEnabled(style);
        var legendPos = legendPositionOverride ?? LegendPosition(style);
        var hasRightLegend = legendEnabled && legendPos == "RIGHT" && legendCount > 1;
        var legendWidth = hasRightLegend ? 120m : 0m;
        var hasLeftLegend = legendEnabled && legendPos == "LEFT" && legendCount > 1;
        var leftLegendWidth = hasLeftLegend ? 120m : 0m;

        var seriesLabelsEnabled = IsEnabled(style, "SERIES_LABELS");
        var seriesLabelsPos = (Token(style, "SERIES_LABELS:POSITION") ?? "END").Trim().ToUpperInvariant();
        var isStartPos = seriesLabelsPos == "START";

        var maxSeriesLabelWidth = 0m;
        if (seriesLabelsEnabled)
        {
            var lineCandidates = layers
                .Where(layer => layer.Mark == MarkKind.Line && Token(layer.Style, "overlayType") is null && HasRenderablePoints(layer, xScale))
                .Select(layer =>
                {
                    var match = series.FirstOrDefault(item => item.Key.Equals(layer.SeriesKey ?? layer.Id, StringComparison.Ordinal));
                    return match?.Label ?? layer.SeriesKey ?? layer.Id;
                })
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => Math.Max(30m, label!.Length * 6.0m + 14m))
                .ToList();
            if (lineCandidates.Count > 0)
                maxSeriesLabelWidth = lineCandidates.Max();
        }

        var maxOverlayWidth = layers
            .Select(layer => Token(layer.Style, "overlayType") is null ? null : Token(layer.Style, "label"))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => Math.Min(140m, label!.Length * 5.4m + 8m))
            .DefaultIfEmpty(0m)
            .Max();

        var remainingWidth = Math.Max(0m, totalWidth - minPlotWidth - leftMargin - BaseRightMargin - legendWidth - leftLegendWidth);

        var plotLeft = leftMargin + leftLegendWidth;
        if (seriesLabelsEnabled && isStartPos && maxSeriesLabelWidth > 0m)
        {
            var startGutter = Math.Min(maxSeriesLabelWidth, remainingWidth);
            plotLeft = leftMargin + startGutter;
            remainingWidth = Math.Max(0m, remainingWidth - startGutter);
        }

        var desiredRightSide = 0m;
        if (seriesLabelsEnabled && !isStartPos && maxSeriesLabelWidth > 0m)
            desiredRightSide = Math.Max(desiredRightSide, maxSeriesLabelWidth);
        if (maxOverlayWidth > 0m)
            desiredRightSide = Math.Max(desiredRightSide, maxOverlayWidth + 14m);

        var sideLabelsGutter = Math.Min(desiredRightSide, remainingWidth);
        var plotRight = BaseRightMargin + legendWidth + sideLabelsGutter;

        var plotWidth = Math.Max(minPlotWidth, totalWidth - plotLeft - plotRight);
        var plotHeight = Math.Max(20m, frame.Height - topMargin - bottomMargin);
        var sideLabelsRight = totalWidth - BaseRightMargin - legendWidth;
        return (new PlotBounds(plotLeft, topMargin, plotWidth, plotHeight), sideLabelsGutter, sideLabelsRight);
    }

    internal static bool HasRenderablePoints(ResolvedMarkLayer layer, ResolvedScale? xScale)
    {
        if (layer.Data.IsDefaultOrEmpty) return false;
        for (var i = 0; i < layer.Data.Length; i++)
        {
            var d = layer.Data[i];
            if (d.IsGap) continue;
            var v = PlotPlanResolver.Number(Channel(d, FieldChannel.Y) ?? Channel(d, FieldChannel.Y2) ?? ChartValue.Null());
            if (!v.HasValue) continue;
            if (xScale is not null && xScale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic or ScaleKind.Time)
            {
                var xv = PlotPlanResolver.PositionNumber(Channel(d, FieldChannel.X));
                if (!xv.HasValue) continue;
            }
            return true;
        }
        return false;
    }

    internal static bool LegendEnabled(ImmutableArray<StyleToken> tokens)
    {
        var value = Token(tokens, "LEGEND");
        return value is null || (!value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0");
    }

    internal static string LegendPosition(ImmutableArray<StyleToken> tokens)
    {
        var position = Token(tokens, "LEGEND_POSITION");
        if (!string.IsNullOrWhiteSpace(position)) return position.ToUpperInvariant();
        position = Token(tokens, "LEGEND");
        return position is "TOP" or "BOTTOM" or "LEFT" or "RIGHT" or "INSIDE"
            ? position.ToUpperInvariant()
            : "BOTTOM";
    }

    internal static bool IsEnabled(ImmutableArray<StyleToken> tokens, string name)
    {
        var value = Token(tokens, name);
        return value is not null && !value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0";
    }

    internal static string? Token(ImmutableArray<StyleToken> tokens, string name)
    {
        if (tokens.IsDefault) return null;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return tokens[i].Value;
        }
        return null;
    }

    private static ChartValue? Channel(ResolvedDatum datum, FieldChannel channel)
    {
        for (var i = 0; i < datum.Channels.Length; i++)
        {
            if (datum.Channels[i].Channel == channel)
                return datum.Channels[i].Value;
        }
        return null;
    }
}
