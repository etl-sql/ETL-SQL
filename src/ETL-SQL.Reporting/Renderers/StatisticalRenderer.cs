using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Reporting.Renderers
{
    internal class StatisticalRenderer : RendererBase
    {
        public string RenderBoxPlot(VisualManifest v)
        {
            var xCol = FindRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var minC = FindRole(v, "min");
            var q1C = FindRole(v, "q1");
            var medC = FindRole(v, "median");
            var q3C = FindRole(v, "q3");
            var maxC = FindRole(v, "max");
            var yCol = FindRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int xi = xCol != null ? v.Columns.FindIndex(c => string.Equals(c, xCol, StringComparison.OrdinalIgnoreCase)) : 0;
            bool hasStats = minC != null && q1C != null && medC != null && q3C != null && maxC != null;

            List<string> categories;
            List<double[]> boxData;

            if (hasStats)
            {
                int minI = v.Columns.FindIndex(c => string.Equals(c, minC, StringComparison.OrdinalIgnoreCase));
                int q1I = v.Columns.FindIndex(c => string.Equals(c, q1C, StringComparison.OrdinalIgnoreCase));
                int medI = v.Columns.FindIndex(c => string.Equals(c, medC, StringComparison.OrdinalIgnoreCase));
                int q3I = v.Columns.FindIndex(c => string.Equals(c, q3C, StringComparison.OrdinalIgnoreCase));
                int maxI = v.Columns.FindIndex(c => string.Equals(c, maxC, StringComparison.OrdinalIgnoreCase));

                categories = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "").ToList();
                boxData = v.Rows.Select(r => new[]
                {
                    ToDouble(r.ElementAtOrDefault(minI)) ?? 0,
                    ToDouble(r.ElementAtOrDefault(q1I)) ?? 0,
                    ToDouble(r.ElementAtOrDefault(medI)) ?? 0,
                    ToDouble(r.ElementAtOrDefault(q3I)) ?? 0,
                    ToDouble(r.ElementAtOrDefault(maxI)) ?? 0
                }).ToList();
            }
            else
            {
                int yi = yCol != null ? v.Columns.IndexOf(yCol) : 1;
                var groups = v.Rows
                    .GroupBy(r => xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "")
                    .ToList();

                categories = groups.Select(g => g.Key).ToList();
                boxData = groups.Select(g =>
                {
                    var vals = g.Select(r => ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null))
                                .Where(d => d.HasValue).Select(d => d!.Value)
                                .OrderBy(x => x).ToArray();
                    if (vals.Length == 0) return new[] { 0.0, 0.0, 0.0, 0.0, 0.0 };
                    return new[]
                    {
                        vals[0],
                        Percentile(vals, 25),
                        Percentile(vals, 50),
                        Percentile(vals, 75),
                        vals[^1]
                    };
                }).ToList();
            }

            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { trigger = "item" },
                xAxis = new { type = "category", data = categories },
                yAxis = new { type = "value" },
                series = new[] { new { type = "boxplot", data = boxData } }
            });
        }

        private static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            double idx = (p / 100.0) * (sorted.Length - 1);
            int lo = (int)idx;
            int hi = Math.Min(lo + 1, sorted.Length - 1);
            return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
        }
    }
}
