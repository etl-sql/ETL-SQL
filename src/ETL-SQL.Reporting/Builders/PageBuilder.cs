using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Reporting.Builders
{
    public class PageBuilder(StyleBuilder styleBuilder)
    {
        public async Task<PageManifest> BuildAsync(
            string name,
            CreatePageStatement pStmt,
            IExecutionContext? ctx,
            IReadOnlyDictionary<string, string>? inheritedStyles)
        {
            var (title, titleMd) = await styleBuilder.ResolveMarkdownAsync(pStmt.Title, pStmt.TitleIsMarkdown);
            var (subtitle, subtitleMd) = await styleBuilder.ResolveMarkdownAsync(pStmt.Subtitle, pStmt.SubtitleIsMarkdown);

            bool isHidden = false;
            if (pStmt.Visibility != null && ctx != null)
            {
                if (pStmt.Visibility.StartsWith("@"))
                {
                    var val = ctx.VarContext.GetVariable(pStmt.Visibility);
                    var s = val?.ToString()?.ToUpperInvariant();
                    isHidden = s is "OFF" or "FALSE" or "0";
                }
                else
                {
                    isHidden = pStmt.Visibility.ToUpperInvariant() is "OFF" or "FALSE" or "0";
                }
            }

            var pm = new PageManifest
            {
                Name = name,
                Mode = pStmt.PageMode.ToString().ToUpperInvariant(),
                Structure = pStmt.Structure,
                IsHidden = isHidden,
                RefreshIntervalSeconds = pStmt.RefreshIntervalSeconds,
                SlotMap = pStmt.SlotMap.ToDictionary(kv => kv.Key, kv => kv.Value),
                Title = title,
                TitleIsMarkdown = titleMd,
                Subtitle = subtitle,
                SubtitleIsMarkdown = subtitleMd,
                Tooltip = await styleBuilder.BuildTooltipManifestAsync(pStmt.Tooltip),
                PrintLayout = pStmt.PrintLayout == null ? null : new PageLayoutDefinitionManifest
                {
                    PageSize = pStmt.PrintLayout.PageSize,
                    CustomWidth = pStmt.PrintLayout.CustomWidth,
                    CustomHeight = pStmt.PrintLayout.CustomHeight,
                    Orientation = pStmt.PrintLayout.Orientation,
                    Units = pStmt.PrintLayout.Units,
                    MarginTop = pStmt.PrintLayout.MarginTop,
                    MarginRight = pStmt.PrintLayout.MarginRight,
                    MarginBottom = pStmt.PrintLayout.MarginBottom,
                    MarginLeft = pStmt.PrintLayout.MarginLeft,
                    Overflow = pStmt.PrintLayout.Overflow
                }
            };


            // Styles
            var resolvedStyles = styleBuilder.ResolveStyles(pStmt.StyleName, pStmt.Styles, inheritedStyles);
            if (resolvedStyles.Count > 0)
                pm.Styles = resolvedStyles;

            return pm;
        }
    }
}
