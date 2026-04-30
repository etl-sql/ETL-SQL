using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.ReportBuilder.Renderers
{
    internal class OverlayRenderer : RendererBase
    {
        public void AppendOverlaySeries(VisualManifest v, List<object> seriesList, List<string> xLabels, bool horizontal)
        {
            if (v.Overlays == null || v.Overlays.Count == 0 || xLabels == null || xLabels.Count == 0) return;

            var xRole = horizontal ? "y" : "x";
            var yRole = horizontal ? "x" : "y";
            var xCol = FindRole(v, xRole) ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol = FindRole(v, yRole) ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int xi = xCol != null ? v.Columns.FindIndex(c => string.Equals(c, xCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int yi = yCol != null ? v.Columns.FindIndex(c => string.Equals(c, yCol, StringComparison.OrdinalIgnoreCase)) : 1;

            var xIndex = xLabels.Select((l, i) => (l, i)).ToDictionary(t => t.l, t => t.i, StringComparer.OrdinalIgnoreCase);
            var aggregated = new double[xLabels.Count];
            foreach (var row in v.Rows)
            {
                var xl = (xi >= 0 && xi < row.Count ? row[xi]?.ToString() ?? "" : "").Trim();
                if (xIndex.TryGetValue(xl, out var idx))
                {
                    aggregated[idx] += ToDouble(yi >= 0 && yi < row.Count ? row[yi] : null) ?? 0.0;
                }
            }
            var yVals = aggregated.ToList();

            var markLines = new List<object>();
            var extraSeries = new List<object>();

            foreach (var ov in v.Overlays)
            {
                var ls = EChartsLineStyle(ov.LineStyle);
                var color = ov.Color ?? "#888888";
                var label = ov.Label ?? ov.OverlayType;

                switch (ov.OverlayType)
                {
                    case "Goal":
                        var axis = horizontal ? "xAxis" : "yAxis";
                        markLines.Add(new Dictionary<string, object?>
                        {
                            [axis] = ov.Parameter ?? 0,
                            ["name"] = label,
                            ["lineStyle"] = new { type = ls, color },
                            ["label"] = new { formatter = label, color }
                        });
                        break;

                    case "Average":
                        var avg = yVals.Count > 0 ? yVals.Average() : 0.0;
                        var axisAvg = horizontal ? "xAxis" : "yAxis";
                        markLines.Add(new Dictionary<string, object?>
                        {
                            [axisAvg] = avg,
                            ["name"] = label,
                            ["lineStyle"] = new { type = ls, color },
                            ["label"] = new { formatter = label, color }
                        });
                        break;

                    case "MovingAvg":
                        int window = (int)(ov.Parameter ?? 3);
                        var maVals = ComputeMovingAverage(yVals, window);
                        extraSeries.Add(new
                        {
                            type = "line", name = label,
                            data = maVals.Select((d, i) => (object?)(d.HasValue ? d : null)).ToList(),
                            smooth = true, symbol = "none",
                            lineStyle = new { type = ls, color },
                            itemStyle = new { color },
                            tooltip = new { valueFormatter = (object?)null }
                        });
                        break;

                    case "Linear":
                        var linVals = ComputeLinearRegression(yVals);
                        extraSeries.Add(new { type = "line", name = label, data = linVals, smooth = false, symbol = "none", lineStyle = new { type = ls, color }, itemStyle = new { color } });
                        break;
                    case "Exponential":
                        var expVals = ComputeExponentialRegression(yVals);
                        extraSeries.Add(new { type = "line", name = label, data = expVals, smooth = true, symbol = "none", lineStyle = new { type = ls, color }, itemStyle = new { color } });
                        break;
                    case "Logarithmic":
                        var logVals = ComputeLogarithmicRegression(yVals);
                        extraSeries.Add(new { type = "line", name = label, data = logVals, smooth = true, symbol = "none", lineStyle = new { type = ls, color }, itemStyle = new { color } });
                        break;
                    case "Power":
                        var pwrVals = ComputePowerRegression(yVals);
                        extraSeries.Add(new { type = "line", name = label, data = pwrVals, smooth = true, symbol = "none", lineStyle = new { type = ls, color }, itemStyle = new { color } });
                        break;
                    case "Polynomial":
                        var polyVals = ComputePolynomialRegression(yVals, (int)(ov.Parameter ?? 2));
                        extraSeries.Add(new { type = "line", name = label, data = polyVals, smooth = true, symbol = "none", lineStyle = new { type = ls, color }, itemStyle = new { color } });
                        break;
                }
            }

            if (markLines.Count > 0 && seriesList.Count > 0)
            {
                var s0 = seriesList[0];
                var json = Serialize(s0);
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
                dict["markLine"] = new { symbol = "none", data = markLines };
                seriesList[0] = dict;
            }
            seriesList.AddRange(extraSeries);
        }

        private static string EChartsLineStyle(string? style) => (style?.ToLowerInvariant()) switch { "dashed" => "dashed", "dotted" => "dotted", _ => "solid" };

        private static List<double?> ComputeMovingAverage(List<double> vals, int window)
        {
            var res = new List<double?>();
            for (int i = 0; i < vals.Count; i++)
            {
                if (i < window - 1) res.Add(null);
                else res.Add(vals.Skip(i - window + 1).Take(window).Average());
            }
            return res;
        }

        private static List<double> ComputeLinearRegression(List<double> y)
        {
            int n = y.Count;
            if (n < 2) return y;
            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
            for (int i = 0; i < n; i++) { sumX += i; sumY += y[i]; sumXY += i * y[i]; sumXX += i * i; }
            double slope = (n * sumXY - sumX * sumY) / (n * sumXX - sumX * sumX);
            double intercept = (sumY - slope * sumX) / n;
            return Enumerable.Range(0, n).Select(i => slope * i + intercept).ToList();
        }

        private static List<double> ComputeExponentialRegression(List<double> y)
        {
            int n = y.Count;
            var logY = y.Select(v => Math.Log(v > 0 ? v : 0.0001)).ToList();
            var lin = ComputeLinearRegression(logY);
            return lin.Select(Math.Exp).ToList();
        }

        private static List<double> ComputeLogarithmicRegression(List<double> y)
        {
            int n = y.Count;
            if (n < 2) return y;
            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
            for (int i = 0; i < n; i++)
            {
                double lx = Math.Log(i + 1);
                sumX += lx; sumY += y[i]; sumXY += lx * y[i]; sumXX += lx * lx;
            }
            double slope = (n * sumXY - sumX * sumY) / (n * sumXX - sumX * sumX);
            double intercept = (sumY - slope * sumX) / n;
            return Enumerable.Range(0, n).Select(i => slope * Math.Log(i + 1) + intercept).ToList();
        }

        private static List<double> ComputePowerRegression(List<double> y)
        {
            int n = y.Count;
            var logY = y.Select(v => Math.Log(v > 0 ? v : 0.0001)).ToList();
            var logX = Enumerable.Range(1, n).Select(i => Math.Log(i)).ToList();
            // Reuse linear on log-log data
            double sumX = logX.Sum(), sumY = logY.Sum(), sumXY = logX.Zip(logY, (a, b) => a * b).Sum(), sumXX = logX.Sum(x => x * x);
            double b = (n * sumXY - sumX * sumY) / (n * sumXX - sumX * sumX);
            double a = Math.Exp((sumY - b * sumX) / n);
            return Enumerable.Range(1, n).Select(i => a * Math.Pow(i, b)).ToList();
        }

        private static List<double> ComputePolynomialRegression(List<double> y, int order)
        {
            // Simple approach for order=2, fall back to linear if higher (to avoid complex matrix math here)
            if (order != 2 || y.Count < 3) return ComputeLinearRegression(y);
            int n = y.Count;
            double s0 = n, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
            double sy = 0, sxy = 0, sx2y = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i, x2 = x * x, x3 = x2 * x, x4 = x3 * x;
                s1 += x; s2 += x2; s3 += x3; s4 += x4;
                sy += y[i]; sxy += x * y[i]; sx2y += x2 * y[i];
            }
            // Solve 3x3 system via Cramer's rule (simplified)
            double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s2 * s3) + s2 * (s1 * s3 - s2 * s2);
            if (Math.Abs(det) < 1e-8) return ComputeLinearRegression(y);
            double a = (sy * (s2 * s4 - s3 * s3) - s1 * (sxy * s4 - sx2y * s3) + s2 * (sxy * s3 - sx2y * s2)) / det;
            double b = (s0 * (sxy * s4 - sx2y * s3) - sy * (s1 * s4 - s2 * s3) + s2 * (s1 * sx2y - sxy * s2)) / det;
            double c = (s0 * (s2 * sx2y - sxy * s3) - s1 * (s1 * sx2y - sx2y * s2) + sy * (s1 * s3 - s2 * s2)) / det;
            return Enumerable.Range(0, n).Select(i => a + b * i + c * i * i).ToList();
        }
    }
}
