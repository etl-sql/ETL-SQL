using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.ReportBuilder.Builders
{
    public class PageBuilder(StyleBuilder styleBuilder)
    {
        public PageManifest Build(string name, CreatePageStatement pStmt)
        {
            var (title, titleMd) = styleBuilder.ResolveMarkdown(pStmt.Title, pStmt.TitleIsMarkdown);
            var (subtitle, subtitleMd) = styleBuilder.ResolveMarkdown(pStmt.Subtitle, pStmt.SubtitleIsMarkdown);

            var pm = new PageManifest
            {
                Name               = name,
                Structure          = pStmt.Structure,
                IsHidden           = pStmt.IsHidden,
                SlotMap            = pStmt.SlotMap.ToDictionary(kv => kv.Key, kv => kv.Value),
                Title              = title,
                TitleIsMarkdown    = titleMd,
                Subtitle           = subtitle,
                SubtitleIsMarkdown = subtitleMd,
                Tooltip            = styleBuilder.BuildTooltipManifest(pStmt.Tooltip)
            };


            // Styles
            var resolvedStyles = styleBuilder.ResolveStyles(pStmt.StyleName, pStmt.Styles);
            if (resolvedStyles.Count > 0)
                pm.Styles = resolvedStyles;

            return pm;
        }
    }
}
