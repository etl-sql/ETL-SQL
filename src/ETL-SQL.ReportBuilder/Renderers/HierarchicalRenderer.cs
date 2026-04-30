using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.ReportBuilder.Renderers
{
    internal class HierarchicalRenderer : RendererBase
    {
        public string RenderTreemap(VisualManifest v)
        {
            var nameCol = FindRole(v, "label") ?? FindRole(v, "name") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var valueCol = FindRole(v, "value") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int ni = nameCol != null ? v.Columns.FindIndex(c => string.Equals(c, nameCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int vi = valueCol != null ? v.Columns.FindIndex(c => string.Equals(c, valueCol, StringComparison.OrdinalIgnoreCase)) : 1;

            var data = v.Rows.Select(r => (object)new
            {
                name = ni >= 0 && ni < r.Count ? r[ni]?.ToString() ?? "" : "",
                value = ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0
            }).ToList();

            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { trigger = "item" },
                series = new[]
                {
                    new { type = "treemap", name = v.Name, data,
                          label = new { show = true },
                          breadcrumb = new { show = false } }
                }
            });
        }
    }
}
