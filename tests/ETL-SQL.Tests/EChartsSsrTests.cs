using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests
{
    /// <summary>
    /// Verifies the production EChartsSsrRenderer turns a visual's ChartConfig into a
    /// real chart SVG for a spread of chart types (including ones the old static
    /// renderer never supported), and returns null for non-chart visuals.
    /// </summary>
    public class EChartsSsrTests
    {
        [Theory]
        [InlineData("BAR",     "{\"xAxis\":{\"type\":\"category\",\"data\":[\"A\",\"B\",\"C\"]},\"yAxis\":{\"type\":\"value\"},\"series\":[{\"type\":\"bar\",\"data\":[5,20,36]}]}")]
        [InlineData("SCATTER", "{\"xAxis\":{},\"yAxis\":{},\"series\":[{\"type\":\"scatter\",\"data\":[[1,2],[3,4],[5,9]]}]}")]
        [InlineData("PIE",     "{\"series\":[{\"type\":\"pie\",\"data\":[{\"value\":1,\"name\":\"x\"},{\"value\":2,\"name\":\"y\"}]}]}")]
        [InlineData("RADAR",   "{\"radar\":{\"indicator\":[{\"name\":\"A\",\"max\":100},{\"name\":\"B\",\"max\":100},{\"name\":\"C\",\"max\":100}]},\"series\":[{\"type\":\"radar\",\"data\":[{\"value\":[60,70,80]}]}]}")]
        [InlineData("GAUGE",   "{\"series\":[{\"type\":\"gauge\",\"data\":[{\"value\":70}]}]}")]
        [InlineData("HEATMAP", "{\"xAxis\":{\"type\":\"category\",\"data\":[\"a\",\"b\"]},\"yAxis\":{\"type\":\"category\",\"data\":[\"x\",\"y\"]},\"visualMap\":{\"min\":0,\"max\":10},\"series\":[{\"type\":\"heatmap\",\"data\":[[0,0,5],[1,1,8]]}]}")]
        public void RenderSvg_ProducesChartSvg_ForType(string type, string chartConfig)
        {
            var visual = new VisualManifest { Name = type, VisualType = type, ChartConfig = chartConfig };

            var svg = EChartsSsrRenderer.Shared.RenderSvg(visual);

            Assert.False(string.IsNullOrWhiteSpace(svg));
            Assert.Contains("<svg", svg);
            Assert.True(svg!.Length > 500, $"{type}: SVG implausibly small ({svg.Length} chars)");
        }

        [Fact]
        public void RenderSvg_RegistersMap_AndRendersMapChart()
        {
            var visual = new VisualManifest
            {
                Name = "Map",
                VisualType = "MAP",
                ChartConfig = "{\"__mapKey\":\"us-states\",\"series\":[{\"type\":\"map\",\"map\":\"us-states\",\"data\":[{\"name\":\"Minnesota\",\"value\":185000}]}]}",
            };

            var svg = EChartsSsrRenderer.Shared.RenderSvg(visual);

            Assert.False(string.IsNullOrWhiteSpace(svg));
            Assert.Contains("<svg", svg);
            Assert.Contains("<path", svg);                 // states render as vector paths
            Assert.True(svg!.Length > 2000, $"map SVG implausibly small ({svg.Length} chars)");
        }

        [Fact]
        public void RenderSvg_ReturnsNull_WhenNoChartConfig()
        {
            var visual = new VisualManifest { Name = "T", VisualType = "TABLE", ChartConfig = null };
            Assert.Null(EChartsSsrRenderer.Shared.RenderSvg(visual));
        }

        [Fact]
        public async System.Threading.Tasks.Task RenderSvg_Concurrently_RendersCorrectly()
        {
            var config = "{\"xAxis\":{\"type\":\"category\",\"data\":[\"A\",\"B\",\"C\"]},\"yAxis\":{\"type\":\"value\"},\"series\":[{\"type\":\"bar\",\"data\":[5,20,36]}]}";
            var visual = new VisualManifest { Name = "BAR", VisualType = "BAR", ChartConfig = config };

            var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task<string?>>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(System.Threading.Tasks.Task.Run(() => EChartsSsrRenderer.Shared.RenderSvg(visual)));
            }

            var svgs = await System.Threading.Tasks.Task.WhenAll(tasks);

            foreach (var svg in svgs)
            {
                Assert.False(string.IsNullOrWhiteSpace(svg));
                Assert.Contains("<svg", svg);
            }
        }
    }
}
