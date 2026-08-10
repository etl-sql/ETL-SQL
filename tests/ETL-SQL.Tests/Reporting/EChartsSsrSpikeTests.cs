using System;
using System.IO;
using ETL_SQL.Reporting;
using Microsoft.ClearScript.V8;
using Xunit;

namespace ETL_SQL.Tests
{
    /// <summary>
    /// SPIKE: proves the ECharts-SSR-for-exports pipeline end to end —
    /// real echarts runs headless in ClearScript/V8, renders to an SVG string,
    /// and the existing Svg.Skia rasterizer (PdfExporter.SvgToPng) turns it into a PNG.
    /// If this passes, the full integration is viable.
    /// </summary>
    public class EChartsSsrSpikeTests
    {
        private static string LoadEcharts()
        {
            var asm = typeof(PdfExporter).Assembly;
            var name = Array.Find(asm.GetManifestResourceNames(),
                           n => n.EndsWith("echarts.min.js", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("echarts.min.js embedded resource not found");
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        [Theory]
        [InlineData("bar", "{\"xAxis\":{\"type\":\"category\",\"data\":[\"A\",\"B\",\"C\",\"D\"]},\"yAxis\":{\"type\":\"value\"},\"series\":[{\"type\":\"bar\",\"data\":[5,20,36,10]}]}")]
        [InlineData("gauge", "{\"series\":[{\"type\":\"gauge\",\"data\":[{\"value\":70,\"name\":\"Score\"}]}]}")]
        public void EChartsSsr_InV8_ProducesSvg_ThatRasterizes(string label, string optionJson)
        {
            using var engine = new V8ScriptEngine();
            // Bare V8 has no host timer APIs; echarts uses them for animation scheduling,
            // which SSR doesn't need — no-op shims suffice.
            engine.Execute(@"
                globalThis.setTimeout    = function(fn){ return 0; };
                globalThis.clearTimeout  = function(){};
                globalThis.setInterval   = function(){ return 0; };
                globalThis.clearInterval = function(){};");
            engine.Execute(LoadEcharts());
            engine.Execute(@"
                globalThis.__renderSvg = function(optionJson, w, h) {
                    var chart = echarts.init(null, null, { renderer: 'svg', ssr: true, width: w, height: h });
                    chart.setOption(JSON.parse(optionJson));
                    var svg = chart.renderToSVGString();
                    chart.dispose();
                    return svg;
                };");

            var svg = (string)engine.Script.__renderSvg(optionJson, 600, 350);

            // Goal A: echarts produced a real chart SVG (not blank).
            Assert.False(string.IsNullOrWhiteSpace(svg));
            Assert.Contains("<svg", svg);
            Assert.Contains("</svg>", svg);
            Assert.True(svg.Length > 500, $"{label}: SVG implausibly small ({svg.Length} chars)");

            // Goal B: the existing Svg.Skia rasterizer handles echarts' SVG.
            var png = PdfExporter.SvgToPng(svg);
            Assert.True(png.Length > 100, $"{label}: PNG empty");
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]); // PNG magic
        }
    }
}
