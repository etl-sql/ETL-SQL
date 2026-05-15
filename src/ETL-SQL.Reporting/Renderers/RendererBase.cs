using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Renderers
{
    internal abstract class RendererBase
    {
        protected static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        protected string Serialize(object option) => JsonSerializer.Serialize(option, JsonOptions);

        protected static object TitleOpt(VisualManifest v)
        {
            var text = v.Options.GetValueOrDefault("title") ?? v.Name;
            return new { text };
        }

        protected static Dictionary<string, object> LegendOpt(VisualManifest v)
        {
            v.Options.TryGetValue("LEGEND_POSITION", out var pos);
            return (pos ?? "bottom").ToLowerInvariant() switch
            {
                "left" => new Dictionary<string, object> { ["orient"] = "vertical", ["left"] = "left", ["top"] = "middle" },
                "right" => new Dictionary<string, object> { ["orient"] = "vertical", ["right"] = "right", ["top"] = "middle" },
                "top" => new Dictionary<string, object> { ["orient"] = "horizontal", ["top"] = "top" },
                _ => new Dictionary<string, object> { ["orient"] = "horizontal", ["bottom"] = "bottom" }
            };
        }

        protected static Dictionary<string, object?> BuildAxisOpts(VisualManifest v, string axis, string type, object? data = null)
        {
            var opts = new Dictionary<string, object?> { ["type"] = type };
            if (data != null) opts["data"] = data;
            var axisLower = axis.ToLowerInvariant();
            if (v.Options.TryGetValue($"axis:{axisLower}:label", out var label))
                opts["name"] = label;
            if (v.Options.TryGetValue($"axis:{axisLower}:min", out var min))
                opts["min"] = ParseAxisBound(min);
            if (v.Options.TryGetValue($"axis:{axisLower}:max", out var max))
                opts["max"] = ParseAxisBound(max);
            return opts;
        }

        protected static object ParseAxisBound(string s) =>
            double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? (object)d : s;

        protected static bool IsOn(string? val) =>
            val?.ToUpperInvariant() is "ON" or "TRUE" or "1";

        protected static List<object> ApplyCommonSeriesOptions(VisualManifest v, List<object> series, bool stacked, bool smooth)
        {
            bool showLabels = IsOn(v.Options.GetValueOrDefault("DATA_LABELS"));
            object? labelObj = null;

            if (showLabels)
            {
                var labelDict = new Dictionary<string, object> { ["show"] = true };
                if (v.Options.TryGetValue("DATA_LABELS:POSITION", out var pos)) labelDict["position"] = pos.ToLowerInvariant();
                if (v.Options.TryGetValue("DATA_LABELS:COLOR", out var color)) labelDict["color"] = color;
                if (v.Options.TryGetValue("DATA_LABELS:FONT_SIZE", out var size)) labelDict["fontSize"] = double.TryParse(size, out var d) ? d : size;
                if (v.Options.TryGetValue("DATA_LABELS:FONT_FAMILY", out var family)) labelDict["fontFamily"] = family;
                if (v.Options.TryGetValue("DATA_LABELS:FONT_WEIGHT", out var weight)) labelDict["fontWeight"] = weight;
                if (v.Options.TryGetValue("DATA_LABELS:FORMAT", out var fmt)) labelDict["formatter"] = "{c}";
                labelObj = labelDict;
            }

            return series.Select(s =>
            {
                var json = JsonSerializer.Serialize(s, JsonOptions);
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
                if (stacked) dict["stack"] = "total";
                if (smooth) dict["smooth"] = (object)true;
                if (labelObj != null) dict["label"] = labelObj;
                return (object)dict;
            }).ToList();
        }

        protected static string? GetColor(VisualManifest v, string key) =>
            v.Options.TryGetValue("COLOR:" + key.ToUpperInvariant(), out var c) ? c : null;

        protected static string? FindRole(VisualManifest v, string role) =>
            v.Options.TryGetValue("mapping:" + role, out var col) ? col : null;

        protected static object? FormatValue(object? s)
        {
            if (s is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss");
            if (s is string str && DateTime.TryParse(str, out var dt2)) return dt2.ToString("yyyy-MM-dd HH:mm:ss");
            return s;
        }

        protected static double? ToDouble(object? s)
        {
            if (s == null) return null;
            if (s is double d1) return d1;
            if (s is decimal dc) return (double)dc;
            if (s is int i) return (double)i;
            if (s is long l) return (double)l;
            string? str = s.ToString();
            if (string.IsNullOrWhiteSpace(str)) return null;
            return double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        protected static List<string> SortXLabels(List<string> labels)
        {
            if (labels == null || labels.Count <= 1) return labels ?? new List<string>();
            var distinct = labels.Select(l => l?.Trim() ?? "").Distinct().ToList();
            var validLabels = distinct.Where(l => !string.IsNullOrEmpty(l)).ToList();

            if (validLabels.Count == 0) return distinct;

            if (validLabels.All(l => DateTime.TryParse(l, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)))
            {
                return distinct
                    .OrderBy(l => string.IsNullOrEmpty(l) ? DateTime.MinValue : DateTime.Parse(l, System.Globalization.CultureInfo.InvariantCulture))
                    .ToList();
            }

            if (validLabels.All(l => double.TryParse(l, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)))
                return distinct.OrderBy(l => string.IsNullOrEmpty(l) ? double.MinValue : double.Parse(l, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            
            return distinct.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
