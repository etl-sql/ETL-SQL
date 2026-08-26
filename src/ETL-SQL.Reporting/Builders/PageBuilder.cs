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
            var titleExpr = pStmt.TitleDefinition?.Text ?? pStmt.Title;
            var titleIsMd = pStmt.TitleDefinition?.IsMarkdown ?? pStmt.TitleIsMarkdown;
            var (title, titleMd) = await styleBuilder.ResolveMarkdownAsync(titleExpr, titleIsMd);

            var subtitleExpr = pStmt.SubtitleDefinition?.Text ?? pStmt.Subtitle;
            var subtitleIsMd = pStmt.SubtitleDefinition?.IsMarkdown ?? pStmt.SubtitleIsMarkdown;
            var (subtitle, subtitleMd) = await styleBuilder.ResolveMarkdownAsync(subtitleExpr, subtitleIsMd);

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
                Tooltip = await styleBuilder.BuildTooltipManifestAsync(pStmt.Tooltip, pStmt.Name),
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

            // Title & Subtitle block typography overrides
            if (pStmt.TitleDefinition != null)
            {
                if (pStmt.TitleDefinition.Color != null) resolvedStyles["TITLE_COLOR"] = pStmt.TitleDefinition.Color;
                if (pStmt.TitleDefinition.Size != null) resolvedStyles["TITLE_SIZE"] = pStmt.TitleDefinition.Size;
                if (pStmt.TitleDefinition.Weight != null) resolvedStyles["TITLE_WEIGHT"] = pStmt.TitleDefinition.Weight;
                if (pStmt.TitleDefinition.Font != null) resolvedStyles["TITLE_FONT"] = pStmt.TitleDefinition.Font;
                if (pStmt.TitleDefinition.Align != null) resolvedStyles["TITLE_ALIGN"] = pStmt.TitleDefinition.Align;
            }
            if (pStmt.SubtitleDefinition != null)
            {
                if (pStmt.SubtitleDefinition.Color != null) resolvedStyles["SUBTITLE_COLOR"] = pStmt.SubtitleDefinition.Color;
                if (pStmt.SubtitleDefinition.Size != null) resolvedStyles["SUBTITLE_SIZE"] = pStmt.SubtitleDefinition.Size;
                if (pStmt.SubtitleDefinition.Weight != null) resolvedStyles["SUBTITLE_WEIGHT"] = pStmt.SubtitleDefinition.Weight;
                if (pStmt.SubtitleDefinition.Font != null) resolvedStyles["SUBTITLE_FONT"] = pStmt.SubtitleDefinition.Font;
                if (pStmt.SubtitleDefinition.Align != null) resolvedStyles["SUBTITLE_ALIGN"] = pStmt.SubtitleDefinition.Align;
            }

            if (resolvedStyles.Count > 0)
                pm.Styles = resolvedStyles;

            return pm;
        }
    }
}
