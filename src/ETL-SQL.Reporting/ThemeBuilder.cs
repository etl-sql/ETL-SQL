using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace ETL_SQL.ReportBuilder
{
    public static class ThemeBuilder
    {
        /// <summary>
        /// Translates Report-SQL theme properties to the ECharts theme JSON structure.
        /// </summary>
        public static JsonObject BuildEChartsTheme(Dictionary<string, string> props)
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

            if (textColor != null)
            {
                theme["textStyle"] = new JsonObject { ["color"] = textColor };
                theme["title"] = new JsonObject
                {
                    ["textStyle"]    = new JsonObject { ["color"] = textColor },
                    ["subtextStyle"] = new JsonObject { ["color"] = textColor }
                };
                theme["legend"] = new JsonObject
                {
                    ["textStyle"] = new JsonObject { ["color"] = textColor }
                };
            }

            if (axisColor != null || gridColor != null)
            {
                var axisObj = BuildAxisObject(axisColor, gridColor);
                theme["categoryAxis"] = axisObj.DeepClone();
                theme["valueAxis"]    = axisObj.DeepClone();
                theme["timeAxis"]     = axisObj.DeepClone();
                theme["logAxis"]      = axisObj.DeepClone();
            }

            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "COLORS", "ACCENT_COLOR", "BACKGROUND", "TEXT_COLOR", "AXIS_COLOR", "GRID_COLOR", "BORDER_COLOR" };

            foreach (var kv in props)
            {
                if (!handled.Contains(kv.Key))
                    theme[kv.Key.ToLowerInvariant()] = kv.Value;
            }

            return theme;
        }

        private static JsonObject BuildAxisObject(string? axisColor, string? gridColor)
        {
            var obj = new JsonObject();
            if (axisColor != null)
            {
                obj["axisLine"]  = new JsonObject { ["lineStyle"] = new JsonObject { ["color"] = axisColor } };
                obj["axisTick"]  = new JsonObject { ["lineStyle"] = new JsonObject { ["color"] = axisColor } };
                obj["axisLabel"] = new JsonObject { ["textStyle"] = new JsonObject { ["color"] = axisColor } };
            }
            if (gridColor != null)
                obj["splitLine"] = new JsonObject { ["lineStyle"] = new JsonObject { ["color"] = new JsonArray { gridColor } } };
            return obj;
        }
    }
}
