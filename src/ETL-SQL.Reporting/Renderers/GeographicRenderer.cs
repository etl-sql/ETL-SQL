using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Reporting.Renderers
{
    /// <summary>
    /// Renders MAP visuals as Apache ECharts map/geo option JSON.
    /// Supports two modes:
    ///   CHOROPLETH (default) — region-fill map driven by a REGION + VALUE column pair.
    ///   POINTS               — scatter dots on a geo base map driven by LON + LAT columns.
    ///
    /// GeoJSON is NOT embedded in the option object. A __mapKey property signals the
    /// client to fetch /maps/{key}.geojson and call echarts.registerMap() before init.
    /// For user-supplied MAP_FILE, __mapFile carries the server-resolved path instead.
    /// </summary>
    internal class GeographicRenderer : RendererBase
    {
        // Maps MAP_NAME option values to the bundled GeoJSON filename stem.
        private static readonly Dictionary<string, string> BuiltinMapKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["WORLD"]             = "world",
                ["US_STATES"]         = "us-states",
                ["US_COUNTIES"]       = "us-counties",
                ["MN_COUNTIES"]       = "mn-counties",
                ["CANADA_PROVINCES"]  = "canada-provinces",
                ["EUROPE"]            = "europe",
            };

        public string RenderMap(VisualManifest v)
        {
            var mode = v.Options.GetValueOrDefault("MODE") ?? "CHOROPLETH";
            return mode.ToUpperInvariant() == "POINTS"
                ? RenderPoints(v)
                : RenderChoropleth(v);
        }

        // ── CHOROPLETH ────────────────────────────────────────────────────────

        private string RenderChoropleth(VisualManifest v)
        {
            var mapKey  = ResolveMapKey(v);
            var matchBy = (v.Options.GetValueOrDefault("MATCH_BY") ?? "NAME").ToUpperInvariant();

            var regionCol = FindRole(v, "region") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var valueCol  = FindRole(v, "value")  ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int ri = regionCol != null ? v.Columns.FindIndex(c => string.Equals(c, regionCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int vi = valueCol  != null ? v.Columns.FindIndex(c => string.Equals(c, valueCol,  StringComparison.OrdinalIgnoreCase)) : 1;

            var data = v.Rows.Select(r =>
            {
                var region = ri >= 0 && ri < r.Count ? r[ri] ?? "" : "";
                var num    = vi >= 0 && vi < r.Count ? ToDouble(r[vi]) : null;
                return (object)new { name = region, value = num };
            }).ToList();

            var (minVal, maxVal) = DataRange(v, vi);
            var colorLow  = v.Options.GetValueOrDefault("COLOR_LOW")  ?? "#e0f3f8";
            var colorHigh = v.Options.GetValueOrDefault("COLOR_HIGH") ?? "#08306b";
            var showLabels = IsOn(v.Options.GetValueOrDefault("SHOW_LABELS"));

            var option = new Dictionary<string, object?>
            {
                ["title"]     = TitleOpt(v),
                ["tooltip"]   = new { trigger = "item", formatter = "{b}<br/>{c}" },
                ["visualMap"] = new
                {
                    min        = minVal,
                    max        = maxVal,
                    left       = "right",
                    calculable = true,
                    realtime   = false,
                    inRange    = new { color = new[] { colorLow, colorHigh } }
                },
                ["series"] = new[]
                {
                    new
                    {
                        type      = "map",
                        map       = mapKey,
                        name      = v.Name,
                        roam      = true,
                        emphasis  = new { label = new { show = true } },
                        label     = new { show = showLabels },
                        data
                    }
                },
                // Signal client to fetch + register the GeoJSON before echarts.init().
                // __matchBy tells the client whether region values are names or FIPS ids.
                ["__mapKey"]  = mapKey,
                ["__matchBy"] = matchBy,
            };

            AppendMapFile(v, option);
            return Serialize(option);
        }

        // ── POINTS ────────────────────────────────────────────────────────────

        private string RenderPoints(VisualManifest v)
        {
            var mapKey = ResolveMapKey(v);

            var lonCol   = FindRole(v, "lon")   ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var latCol   = FindRole(v, "lat")   ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var valueCol = FindRole(v, "value") ?? (v.Columns.Count > 2 ? v.Columns[2] : null);
            var labelCol = FindRole(v, "label") ?? (v.Columns.Count > 3 ? v.Columns[3] : null);

            int loni  = ColIdx(v, lonCol);
            int lati  = ColIdx(v, latCol);
            int vi    = ColIdx(v, valueCol);
            int labli = ColIdx(v, labelCol);

            double maxSize = vi >= 0
                ? v.Rows.Max(r => ToDouble(r.Count > vi ? r[vi] : null) ?? 0.0)
                : 1;
            if (maxSize == 0) maxSize = 1;

            var data = v.Rows.Select(r =>
            {
                double lon   = ToDouble(loni  >= 0 && loni  < r.Count ? r[loni]  : null) ?? 0.0;
                double lat   = ToDouble(lati  >= 0 && lati  < r.Count ? r[lati]  : null) ?? 0.0;
                double raw   = vi    >= 0 && vi    < r.Count ? ToDouble(r[vi])    ?? 0.0 : 0.0;
                double sized = vi    >= 0 ? raw / maxSize * 40 + 5 : 10.0;
                string label = labli >= 0 && labli < r.Count ? r[labli] ?? "" : "";
                return (object)new { value = new object[] { lon, lat, sized, raw }, name = label };
            }).ToList();

            var (minVal, maxVal) = DataRange(v, vi);
            var colorLow  = v.Options.GetValueOrDefault("COLOR_LOW")  ?? "#e0f3f8";
            var colorHigh = v.Options.GetValueOrDefault("COLOR_HIGH") ?? "#08306b";

            var option = new Dictionary<string, object?>
            {
                ["title"]   = TitleOpt(v),
                ["tooltip"] = new { trigger = "item", formatter = "{b}: {c}" },
                ["geo"]     = new
                {
                    map  = mapKey,
                    roam = true,
                    label = new { show = false },
                    itemStyle = new { areaColor = "#f3f4f6", borderColor = "#d1d5db" }
                },
                ["visualMap"] = new
                {
                    min        = minVal,
                    max        = maxVal,
                    left       = "right",
                    calculable = true,
                    inRange    = new { color = new[] { colorLow, colorHigh } }
                },
                ["series"] = new[]
                {
                    new
                    {
                        type             = "scatter",
                        coordinateSystem = "geo",
                        name             = v.Name,
                        data,
                        // Client reads index [2] as display size (same pattern as BUBBLE)
                        __pointsSymbolSize = true
                    }
                },
                ["__mapKey"] = mapKey,
            };

            AppendMapFile(v, option);
            return Serialize(option);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ResolveMapKey(VisualManifest v)
        {
            if (v.Options.TryGetValue("MAP_NAME", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                if (BuiltinMapKeys.TryGetValue(name, out var key)) return key;
                // Fallback: normalise whatever the user wrote (e.g. "my_map" → "my-map")
                return name.ToLowerInvariant().Replace('_', '-');
            }
            // MAP_FILE case: derive a stable key from the filename stem
            if (v.Options.TryGetValue("MAP_FILE", out var file) && !string.IsNullOrWhiteSpace(file))
                return System.IO.Path.GetFileNameWithoutExtension(file).ToLowerInvariant().Replace('_', '-');

            return "world"; // safe default
        }

        private static void AppendMapFile(VisualManifest v, Dictionary<string, object?> option)
        {
            if (v.Options.TryGetValue("MAP_FILE", out var file) && !string.IsNullOrWhiteSpace(file))
                option["__mapFile"] = file;
        }

        private static (double min, double max) DataRange(VisualManifest v, int valueColIdx)
        {
            if (valueColIdx < 0 || v.Rows.Count == 0) return (0, 100);
            var values = v.Rows
                .Select(r => ToDouble(valueColIdx < r.Count ? r[valueColIdx] : null) ?? 0.0)
                .ToList();
            double min = values.Min();
            double max = values.Max();
            if (min == max) { min = 0; max = max == 0 ? 100 : max; }
            return (min, max);
        }

        private static int ColIdx(VisualManifest v, string? col) =>
            col != null
                ? v.Columns.FindIndex(c => string.Equals(c, col, StringComparison.OrdinalIgnoreCase))
                : -1;
    }
}
