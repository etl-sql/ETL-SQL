using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ETL_SQL.ReportBuilder
{
    /// <summary>
    /// Generates static SVG chart images from a <see cref="VisualManifest"/>.
    /// Used by <see cref="MarkdownRenderer"/> to embed charts in exported Markdown documents
    /// without requiring a headless browser.
    /// </summary>
    public class SvgChartRenderer
    {
        private const int W  = 600;
        private const int H  = 350;
        private const int PL = 60;   // pad left
        private const int PR = 20;   // pad right
        private const int PT = 40;   // pad top
        private const int PB = 60;   // pad bottom

        private static readonly string[] Colors =
            { "#5470c6", "#91cc75", "#fac858", "#ee6666", "#73c0de", "#3ba272", "#fc8452" };

        public string? Render(VisualManifest v) =>
            v.VisualType.ToUpperInvariant() switch
            {
                "BAR"    => RenderBar(v, false),
                "HBAR"   => RenderBar(v, true),
                "LINE"   => RenderLine(v),
                "PIE"    => RenderPie(v, false),
                "DONUT"  => RenderPie(v, true),
                "CARD"   => null,    // rendered as text in Markdown
                "TABLE"  => null,    // rendered as GFM table
                "SLICER" => null,    // interactive-only
                "TEXT"   => null,    // rendered as markdown content
                _        => RenderPlaceholder(v)
            };

        // ── Bar / Horizontal Bar ───────────────────────────────────────────────

        private string RenderBar(VisualManifest v, bool horizontal)
        {
            int xi = ColIdx(v, "x", 0), yi = ColIdx(v, "y", 1);
            var labels = v.Rows.Select(r => CellStr(r, xi)).ToList();
            var values = v.Rows.Select(r => ParseDouble(CellStr(r, yi)) ?? 0.0).ToList();
            if (labels.Count == 0) return RenderPlaceholder(v);

            double maxVal = Math.Max(values.Count > 0 ? values.Max() : 1, 0.001);
            int cW = W - PL - PR, cH = H - PT - PB;

            var sb = new StringBuilder();
            OpenSvg(sb); Title(sb, v);
            Axes(sb, horizontal, cW, cH);
            YTicks(sb, maxVal, cH, horizontal);

            int n = labels.Count;
            double slot = horizontal ? (double)cH / n : (double)cW / n;
            double barThick = slot * 0.7;
            double gap      = slot * 0.15;

            for (int i = 0; i < n; i++)
            {
                string color = GetColor(v, labels[i]) ?? Colors[i % Colors.Length];
                double frac  = values[i] / maxVal;
                string label = Truncate(labels[i], 12);

                if (!horizontal)
                {
                    int bH = (int)(cH * frac);
                    int bX = PL + (int)(i * slot + gap);
                    int bY = PT + cH - bH;
                    sb.AppendLine($"<rect x='{bX}' y='{bY}' width='{(int)barThick}' height='{bH}' fill='{Esc(color)}' />");
                    sb.AppendLine($"<text x='{bX + (int)(barThick / 2)}' y='{PT + cH + 14}' text-anchor='middle' font-size='10' fill='#444'>{EscXml(label)}</text>");
                }
                else
                {
                    int bW = (int)(cW * frac);
                    int bY = PT + (int)(i * slot + gap);
                    sb.AppendLine($"<rect x='{PL}' y='{bY}' width='{bW}' height='{(int)barThick}' fill='{Esc(color)}' />");
                    sb.AppendLine($"<text x='{PL - 4}' y='{bY + (int)(barThick / 2) + 4}' text-anchor='end' font-size='10' fill='#444'>{EscXml(label)}</text>");
                }
            }

            CloseSvg(sb);
            return sb.ToString();
        }

        // ── Line ───────────────────────────────────────────────────────────────

        private string RenderLine(VisualManifest v)
        {
            int xi = ColIdx(v, "x", 0), yi = ColIdx(v, "y", 1);
            var values = v.Rows.Select(r => ParseDouble(CellStr(r, yi)) ?? 0.0).ToList();
            var labels = v.Rows.Select(r => CellStr(r, xi)).ToList();
            if (values.Count == 0) return RenderPlaceholder(v);

            double maxVal = Math.Max(values.Max(), 0.001);
            int cW = W - PL - PR, cH = H - PT - PB;

            var sb = new StringBuilder();
            OpenSvg(sb); Title(sb, v);
            Axes(sb, false, cW, cH);
            YTicks(sb, maxVal, cH, false);

            var pts = values.Select((val, i) =>
            {
                int px = PL + (values.Count > 1 ? (int)((double)cW * i / (values.Count - 1)) : cW / 2);
                int py = PT + cH - (int)(cH * val / maxVal);
                return (px, py);
            }).ToList();

            if (pts.Count > 1)
            {
                var poly = string.Join(" ", pts.Select(p => $"{p.px},{p.py}"));
                sb.AppendLine($"<polyline points='{poly}' fill='none' stroke='{Colors[0]}' stroke-width='2' />");
            }

            foreach (var (px, py) in pts)
                sb.AppendLine($"<circle cx='{px}' cy='{py}' r='3' fill='{Colors[0]}' />");

            // X labels (up to 10 evenly spaced)
            int step = Math.Max(1, labels.Count / 10);
            for (int i = 0; i < labels.Count; i += step)
                sb.AppendLine($"<text x='{pts[i].px}' y='{PT + cH + 14}' text-anchor='middle' font-size='9' fill='#666'>{EscXml(Truncate(labels[i], 8))}</text>");

            CloseSvg(sb);
            return sb.ToString();
        }

        // ── Pie / Donut ────────────────────────────────────────────────────────

        private string RenderPie(VisualManifest v, bool donut)
        {
            int li = ColIdx(v, "label", 0), vi = ColIdx(v, "value", 1);
            var items = v.Rows
                .Select(r => (label: CellStr(r, li), value: ParseDouble(CellStr(r, vi)) ?? 0.0))
                .Where(x => x.value > 0).ToList();
            if (items.Count == 0) return RenderPlaceholder(v);

            double total = items.Sum(x => x.value);
            int cx = W / 2, cy = H / 2 - 5;
            int outerR = Math.Min(W, H) / 2 - 50;
            int innerR = donut ? outerR / 2 : 0;

            var sb = new StringBuilder();
            OpenSvg(sb); Title(sb, v);

            double angle = -Math.PI / 2;
            for (int i = 0; i < items.Count; i++)
            {
                double sweep = 2 * Math.PI * items[i].value / total;
                double end   = angle + sweep;
                int large    = sweep > Math.PI ? 1 : 0;

                int ox1 = cx + (int)(outerR * Math.Cos(angle)), oy1 = cy + (int)(outerR * Math.Sin(angle));
                int ox2 = cx + (int)(outerR * Math.Cos(end)),   oy2 = cy + (int)(outerR * Math.Sin(end));

                string d = donut
                    ? $"M {ox1} {oy1} A {outerR} {outerR} 0 {large} 1 {ox2} {oy2} L {cx + (int)(innerR * Math.Cos(end))} {cy + (int)(innerR * Math.Sin(end))} A {innerR} {innerR} 0 {large} 0 {cx + (int)(innerR * Math.Cos(angle))} {cy + (int)(innerR * Math.Sin(angle))} Z"
                    : $"M {cx} {cy} L {ox1} {oy1} A {outerR} {outerR} 0 {large} 1 {ox2} {oy2} Z";

                string color = GetColor(v, items[i].label) ?? Colors[i % Colors.Length];
                sb.AppendLine($"<path d='{d}' fill='{Esc(color)}' stroke='white' stroke-width='2' />");

                // Percentage label at arc midpoint
                double mid = angle + sweep / 2;
                int lx = cx + (int)((outerR * 0.65) * Math.Cos(mid));
                int ly = cy + (int)((outerR * 0.65) * Math.Sin(mid));
                string pct = (items[i].value / total * 100).ToString("N0") + "%";
                sb.AppendLine($"<text x='{lx}' y='{ly + 4}' text-anchor='middle' font-size='10' fill='white'>{EscXml(pct)}</text>");

                angle = end;
            }

            CloseSvg(sb);
            return sb.ToString();
        }

        // ── Placeholder ────────────────────────────────────────────────────────

        private static string RenderPlaceholder(VisualManifest v)
        {
            var sb = new StringBuilder();
            OpenSvg(sb);
            sb.AppendLine($"<rect x='1' y='1' width='{W - 2}' height='{H - 2}' fill='#f8f9fa' rx='6' stroke='#dee2e6' />");
            sb.AppendLine($"<text x='{W / 2}' y='{H / 2 - 8}' text-anchor='middle' font-size='14' fill='#888'>{EscXml(v.VisualType)} chart</text>");
            sb.AppendLine($"<text x='{W / 2}' y='{H / 2 + 14}' text-anchor='middle' font-size='11' fill='#aaa'>{EscXml(v.Name)}</text>");
            CloseSvg(sb);
            return sb.ToString();
        }

        // ── Shared helpers ─────────────────────────────────────────────────────

        private static void OpenSvg(StringBuilder sb) =>
            sb.AppendLine($"<svg xmlns='http://www.w3.org/2000/svg' width='{W}' height='{H}' viewBox='0 0 {W} {H}' font-family='sans-serif'><rect width='{W}' height='{H}' fill='white'/>");

        private static void CloseSvg(StringBuilder sb) => sb.AppendLine("</svg>");

        private static void Title(StringBuilder sb, VisualManifest v)
        {
            string text = v.Options.TryGetValue("title", out var t) ? t : v.Name;
            if (!string.IsNullOrEmpty(text))
                sb.AppendLine($"<text x='{W / 2}' y='22' text-anchor='middle' font-size='13' font-weight='bold' fill='#333'>{EscXml(text)}</text>");
        }

        private static void Axes(StringBuilder sb, bool horizontal, int cW, int cH)
        {
            sb.AppendLine($"<line x1='{PL}' y1='{PT}' x2='{PL}' y2='{PT + cH}' stroke='#ddd' />");
            sb.AppendLine($"<line x1='{PL}' y1='{PT + cH}' x2='{PL + cW}' y2='{PT + cH}' stroke='#ddd' />");
        }

        private static void YTicks(StringBuilder sb, double maxVal, int cH, bool horizontal)
        {
            for (int i = 0; i <= 4; i++)
            {
                double val = maxVal * i / 4;
                int y = PT + cH - (int)(cH * i / 4.0);
                sb.AppendLine($"<line x1='{PL - 4}' y1='{y}' x2='{PL}' y2='{y}' stroke='#ddd' />");
                sb.AppendLine($"<text x='{PL - 6}' y='{y + 4}' text-anchor='end' font-size='9' fill='#888'>{TickLabel(val)}</text>");
            }
        }

        private static string TickLabel(double v)
        {
            if (Math.Abs(v) >= 1_000_000) return (v / 1_000_000).ToString("N1") + "M";
            if (Math.Abs(v) >= 1_000)     return (v / 1_000).ToString("N1") + "K";
            return v.ToString("N1");
        }

        private static int ColIdx(VisualManifest v, string role, int fallback)
        {
            if (v.Options.TryGetValue("mapping:" + role, out var col))
            {
                int idx = v.Columns.IndexOf(col);
                return idx >= 0 ? idx : fallback;
            }
            return fallback < v.Columns.Count ? fallback : -1;
        }

        private static string CellStr(List<string?> row, int idx) =>
            idx >= 0 && idx < row.Count ? row[idx] ?? "" : "";

        private static double? ParseDouble(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null :
            double.TryParse(s, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

        private static string? GetColor(VisualManifest v, string key) =>
            v.Options.TryGetValue("color:" + key, out var c) ? c : null;

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s[..max] + "…";

        private static string EscXml(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string Esc(string s) => EscXml(s).Replace("'", "&apos;");
    }
}
