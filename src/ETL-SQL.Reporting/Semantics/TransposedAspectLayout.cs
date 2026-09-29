using System;
using System.Collections.Immutable;
using System.Linq;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>Physical plot area for the supported fixed-aspect POINT/TEXT and supported RULE/RECT composition.</summary>
internal static class TransposedAspectLayout
{
    internal static PlotBounds Resolve(PlotBounds frame, ImmutableArray<StyleToken> style,
        ImmutableArray<ResolvedMarkLayer> layers, int legendCount)
    {
        static string? Token(ImmutableArray<StyleToken> tokens, string name) => tokens.IsDefault ? null :
            tokens.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        var legend = Token(style, "LEGEND");
        var enabled = legend is null || (!legend.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !legend.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && legend != "0");
        var position = Token(style, "LEGEND_POSITION");
        position = !string.IsNullOrWhiteSpace(position) ? position.ToUpperInvariant() :
            legend is "TOP" or "BOTTOM" or "LEFT" or "RIGHT" or "INSIDE" ? legend : "BOTTOM";
        var leftLegend = enabled && legendCount > 1 && position == "LEFT" ? 120m : 0m;
        var rightLegend = enabled && legendCount > 1 && position == "RIGHT" ? 120m : 0m;
        var minimumWidth = frame.Width >= 180m ? 100m : Math.Max(30m, frame.Width - 40m);
        var overlayWidth = layers.Select(layer => Token(layer.Style, "overlayType") is null ? null : Token(layer.Style, "label"))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => Math.Min(140m, label!.Length * 5.4m + 8m)).DefaultIfEmpty(0m).Max();
        var remaining = Math.Max(0m, frame.Width - minimumWidth - 80m - leftLegend - rightLegend);
        var gutter = Math.Min(overlayWidth > 0m ? overlayWidth + 14m : 0m, remaining);
        var left = 60m + leftLegend;
        var width = Math.Max(minimumWidth, frame.Width - left - 20m - rightLegend - gutter);
        var height = frame.Height - 100m;
        var ratio = height / (frame.Width - 80m);
        width = Math.Min(width, frame.Width - 80m);
        width = Math.Min(width, height / ratio);
        return new PlotBounds(left, 40m, width, width * ratio);
    }
}
