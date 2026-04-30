using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.ReportBuilder.Renderers
{
    internal class CircularRenderer : RendererBase
    {
        public string RenderPie(VisualManifest v, bool donut)
        {
            var labelCol = FindRole(v, "label") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var valueCol = FindRole(v, "value") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int li = labelCol != null ? v.Columns.FindIndex(c => string.Equals(c, labelCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int vi = valueCol != null ? v.Columns.FindIndex(c => string.Equals(c, valueCol, StringComparison.OrdinalIgnoreCase)) : 1;

            var data = v.Rows.Select(r =>
            {
                var name = li >= 0 && li < r.Count ? r[li]?.ToString() ?? "" : "";
                var color = GetColor(v, name);
                return color != null
                    ? (object)new { name, value = ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0, itemStyle = new { color } }
                    : (object)new { name, value = ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0 };
            }).ToList();

            object radius = donut ? (object)new[] { "40%", "70%" } : "60%";
            var series = new List<object> { new { type = "pie", name = v.Name, radius, data } };
            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { trigger = "item" },
                legend = LegendOpt(v),
                series = ApplyCommonSeriesOptions(v, series, stacked: false, smooth: false)
            });
        }
    }
}
