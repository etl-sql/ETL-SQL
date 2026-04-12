using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.ReportBuilder
{
    /// <summary>
    /// Converts a <see cref="VisualManifest"/> into a Chart.js configuration JSON string.
    /// Output is a self-contained JSON object suitable for <c>new Chart(ctx, config)</c>.
    /// </summary>
    public class ChartJsRenderer
    {
        private static readonly string[] _defaultColors =
        {
            "rgba(54,162,235,0.8)",  // blue
            "rgba(255,99,132,0.8)",  // red
            "rgba(75,192,192,0.8)",  // teal
            "rgba(255,159,64,0.8)",  // orange
            "rgba(153,102,255,0.8)", // purple
            "rgba(255,206,86,0.8)",  // yellow
            "rgba(201,203,207,0.8)"  // grey
        };

        /// <summary>
        /// Produces a Chart.js config JSON string for the given visual manifest.
        /// Returns null for visual types that do not use Chart.js (TABLE, CARD, SLICER).
        /// </summary>
        public string? Render(VisualManifest visual)
        {
            return visual.VisualType.ToUpperInvariant() switch
            {
                "BAR"     => RenderBar(visual),
                "LINE"    => RenderLine(visual),
                "SCATTER" => RenderScatter(visual),
                "PIE"     => RenderPie(visual),
                "TABLE"   => null,   // rendered as HTML <table>
                "CARD"    => null,   // rendered as styled <div>
                "SLICER"  => null,   // rendered as <select> / parameter control
                _         => null
            };
        }

        // ── BAR ──────────────────────────────────────────────────────────────

        private string RenderBar(VisualManifest v)
        {
            var (labels, datasets) = ExtractLabeledDatasets(v, "x", "y", "series");
            var title = v.Options.GetValueOrDefault("title", v.Name);

            return Serialize(new
            {
                type = "bar",
                data = new { labels, datasets },
                options = new
                {
                    responsive = true,
                    plugins = new { legend = new { position = "top" }, title = new { display = true, text = title } },
                    scales  = new { x = new { stacked = false }, y = new { beginAtZero = true, stacked = false } }
                }
            });
        }

        // ── LINE ─────────────────────────────────────────────────────────────

        private string RenderLine(VisualManifest v)
        {
            var (labels, datasets) = ExtractLabeledDatasets(v, "x", "y", "series");
            var title = v.Options.GetValueOrDefault("title", v.Name);

            return Serialize(new
            {
                type = "line",
                data = new { labels, datasets },
                options = new
                {
                    responsive = true,
                    plugins = new { legend = new { position = "top" }, title = new { display = true, text = title } }
                }
            });
        }

        // ── SCATTER ──────────────────────────────────────────────────────────

        private string RenderScatter(VisualManifest v)
        {
            var xCol = v.Options.GetValueOrDefault("x", FindMappingRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : "x"));
            var yCol = v.Options.GetValueOrDefault("y", FindMappingRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : "y"));

            int xIdx = v.Columns.IndexOf(xCol);
            int yIdx = v.Columns.IndexOf(yCol);

            var points = v.Rows.Select(row => new
            {
                x = ParseNumber(xIdx >= 0 && xIdx < row.Count ? row[xIdx] : null),
                y = ParseNumber(yIdx >= 0 && yIdx < row.Count ? row[yIdx] : null)
            }).ToList();

            var title = v.Options.GetValueOrDefault("title", v.Name);

            return Serialize(new
            {
                type = "scatter",
                data = new { datasets = new[] { new { label = v.Name, data = points, backgroundColor = _defaultColors[0] } } },
                options = new
                {
                    responsive = true,
                    plugins = new { title = new { display = true, text = title } }
                }
            });
        }

        // ── PIE ──────────────────────────────────────────────────────────────

        private string RenderPie(VisualManifest v)
        {
            var labelCol = FindMappingRole(v, "label") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var valueCol = FindMappingRole(v, "value") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int labelIdx = labelCol != null ? v.Columns.IndexOf(labelCol) : 0;
            int valueIdx = valueCol != null ? v.Columns.IndexOf(valueCol) : 1;

            var labels = v.Rows.Select(r => labelIdx >= 0 && labelIdx < r.Count ? r[labelIdx] ?? "" : "").ToList();
            var data   = v.Rows.Select(r => ParseNumber(valueIdx >= 0 && valueIdx < r.Count ? r[valueIdx] : null)).ToList();
            var colors = Enumerable.Range(0, v.Rows.Count).Select(i => _defaultColors[i % _defaultColors.Length]).ToList();

            var title = v.Options.GetValueOrDefault("title", v.Name);

            return Serialize(new
            {
                type = "pie",
                data = new
                {
                    labels,
                    datasets = new[]
                    {
                        new { data, backgroundColor = colors, borderWidth = 1 }
                    }
                },
                options = new
                {
                    responsive = true,
                    plugins = new { legend = new { position = "right" }, title = new { display = true, text = title } }
                }
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private (List<string> labels, List<object> datasets) ExtractLabeledDatasets(
            VisualManifest v, string xRole, string yRole, string seriesRole)
        {
            var xCol     = FindMappingRole(v, xRole)      ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol     = FindMappingRole(v, yRole)      ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var seriesCol = FindMappingRole(v, seriesRole);

            int xIdx = xCol     != null ? v.Columns.IndexOf(xCol)     : 0;
            int yIdx = yCol     != null ? v.Columns.IndexOf(yCol)     : 1;
            int sIdx = seriesCol != null ? v.Columns.IndexOf(seriesCol) : -1;

            if (sIdx < 0)
            {
                // No series column → single dataset
                var labels   = v.Rows.Select(r => xIdx >= 0 && xIdx < r.Count ? r[xIdx] ?? "" : "").ToList();
                var dataVals = v.Rows.Select(r => ParseNumber(yIdx >= 0 && yIdx < r.Count ? r[yIdx] : null)).ToList();
                var ds = new List<object>
                {
                    new { label = yCol ?? v.Name, data = dataVals, backgroundColor = _defaultColors[0], borderColor = _defaultColors[0], fill = false }
                };
                return (labels, ds);
            }
            else
            {
                // Series column → one dataset per distinct series value
                var distinctLabels  = v.Rows.Select(r => xIdx >= 0 && xIdx < r.Count ? r[xIdx] ?? "" : "").Distinct().ToList();
                var distinctSeries  = v.Rows.Select(r => sIdx < r.Count ? r[sIdx] ?? "" : "").Distinct().ToList();

                var labelIndex = distinctLabels.ToDictionary(l => l, l => distinctLabels.IndexOf(l));
                int colorIdx = 0;
                var datasets = new List<object>();

                foreach (var series in distinctSeries)
                {
                    var color  = _defaultColors[colorIdx++ % _defaultColors.Length];
                    var values = Enumerable.Repeat<double?>(null, distinctLabels.Count).ToList();

                    foreach (var row in v.Rows)
                    {
                        var rowSeries = sIdx < row.Count ? row[sIdx] ?? "" : "";
                        if (rowSeries != series) continue;
                        var rowLabel  = xIdx < row.Count ? row[xIdx] ?? "" : "";
                        if (!labelIndex.TryGetValue(rowLabel, out var li)) continue;
                        values[li] = ParseNumber(yIdx < row.Count ? row[yIdx] : null);
                    }

                    datasets.Add(new { label = series, data = values, backgroundColor = color, borderColor = color, fill = false });
                }

                return (distinctLabels, datasets);
            }
        }

        private static string? FindMappingRole(VisualManifest v, string role)
        {
            // We don't have the original AST here; options may carry role→column hints set by ManifestBuilder
            v.Options.TryGetValue("mapping:" + role, out var col);
            return col;
        }

        private static double? ParseNumber(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, out var d) ? d : null;
        }

        private static string Serialize(object obj)
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented         = false,
                PropertyNamingPolicy  = null
            });
        }
    }
}
