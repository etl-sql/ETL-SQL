using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace ETL_SQL.Core;

public static class ReportingThemeBuilder
{
    /// <summary>
    /// Translates Report-SQL theme properties to the native theme JSON structure.
    /// </summary>
    public static JsonObject BuildNativeTheme(Dictionary<string, string> props, Dictionary<string, Dictionary<string, string>>? visualOverrides = null)
    {
        var theme = new JsonObject();

        if (props.TryGetValue("COLORS", out var colorsStr))
        {
            var arr = new JsonArray();
            foreach (var c in colorsStr.Split(','))
            {
                var hex = c.Trim();
                if (!string.IsNullOrEmpty(hex)) arr.Add(hex);
            }
            theme["color"] = arr;
        }
        else if (props.TryGetValue("ACCENT_COLOR", out var accent))
        {
            theme["color"] = new JsonArray { accent };
        }

        if (props.TryGetValue("BACKGROUND", out var bg))
            theme["backgroundColor"] = bg;

        var textColor = props.TryGetValue("TEXT_COLOR", out var tc) ? tc : null;
        var axisColor = props.TryGetValue("AXIS_COLOR", out var ac) ? ac : textColor;
        var gridColor = props.TryGetValue("GRID_COLOR", out var gc) ? gc : null;
        var fontFamily = props.TryGetValue("FONT_FAMILY", out var ff) ? ff
            : (props.TryGetValue("FONT-FAMILY", out var ff2) ? ff2 : null);

        if (textColor != null || fontFamily != null)
        {
            var ts = new JsonObject();
            if (textColor != null) ts["color"] = textColor;
            if (fontFamily != null) ts["fontFamily"] = fontFamily;
            theme["textStyle"] = ts;

            theme["title"] = new JsonObject
            {
                ["textStyle"] = new JsonObject
                {
                    ["color"] = textColor ?? "#333",
                    ["fontFamily"] = fontFamily ?? "sans-serif"
                },
                ["subtextStyle"] = new JsonObject
                {
                    ["color"] = textColor ?? "#666",
                    ["fontFamily"] = fontFamily ?? "sans-serif"
                }
            };
            theme["legend"] = new JsonObject
            {
                ["textStyle"] = new JsonObject
                {
                    ["color"] = textColor ?? "#333",
                    ["fontFamily"] = fontFamily ?? "sans-serif"
                }
            };
        }

        if (axisColor != null || gridColor != null)
        {
            var axisObj = BuildAxisObject(axisColor, gridColor);
            theme["categoryAxis"] = axisObj.DeepClone();
            theme["valueAxis"] = axisObj.DeepClone();
            theme["timeAxis"] = axisObj.DeepClone();
            theme["logAxis"] = axisObj.DeepClone();
        }

        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "COLORS", "ACCENT_COLOR", "BACKGROUND", "TEXT_COLOR", "AXIS_COLOR", "GRID_COLOR", "BORDER_COLOR", "FONT_FAMILY", "FONT-FAMILY" };

        foreach (var kv in props)
        {
            if (!handled.Contains(kv.Key) && !kv.Key.Contains(':'))
                theme[kv.Key.ToLowerInvariant()] = kv.Value;
        }

        if (visualOverrides is { Count: > 0 })
        {
            foreach (var (vType, overrides) in visualOverrides)
            {
                var vKey = vType.ToLowerInvariant();
                var vObj = new JsonObject();
                if (overrides.TryGetValue("COLORS", out var vColors))
                {
                    var arr = new JsonArray();
                    foreach (var c in vColors.Split(','))
                    {
                        var hex = c.Trim().Trim('\'', '"', '(', ')');
                        if (!string.IsNullOrEmpty(hex)) arr.Add(hex);
                    }
                    vObj["color"] = arr;
                }
                foreach (var (k, v) in overrides)
                {
                    if (!k.Equals("COLORS", StringComparison.OrdinalIgnoreCase))
                        vObj[k.ToLowerInvariant()] = v;
                }
                theme[vKey] = vObj;
            }
        }

        return theme;
    }

    private static JsonObject BuildAxisObject(string? axisColor, string? gridColor)
    {
        var obj = new JsonObject();
        if (axisColor != null)
        {
            obj["axisLine"] = new JsonObject { ["lineStyle"] = new JsonObject { ["color"] = axisColor } };
            obj["axisTick"] = new JsonObject { ["lineStyle"] = new JsonObject { ["color"] = axisColor } };
            obj["axisLabel"] = new JsonObject { ["textStyle"] = new JsonObject { ["color"] = axisColor } };
        }
        if (gridColor != null)
            obj["splitLine"] = new JsonObject { ["lineStyle"] = new JsonObject { ["color"] = new JsonArray { gridColor } } };
        return obj;
    }
}
