using System;
using System.Collections.Generic;
using ETL_SQL.Core;

namespace ETL_SQL.ReportBuilder.Builders
{
    public class StyleBuilder(IExecutionContext ctx)
    {
        public Dictionary<string, string> ResolveStyles(string? styleName, Dictionary<string, string> inlineStyles)
        {
            if (string.IsNullOrEmpty(styleName) && inlineStyles.Count == 0)
                return new Dictionary<string, string>();

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(styleName) &&
                ctx is IReportContext rc &&
                rc.StyleDefinitions.TryGetValue(styleName, out var namedStyle))
            {
                foreach (var kv in namedStyle.Styles)
                    merged[kv.Key] = kv.Value;
            }

            foreach (var kv in inlineStyles)
                merged[kv.Key] = kv.Value;

            return merged;
        }

        public TooltipManifest? BuildTooltipManifest(TooltipDefinition? tooltip)
        {
            if (tooltip == null) return null;
            if (tooltip.ContainerRef != null)
                return new TooltipManifest { Type = "container", ContainerRef = tooltip.ContainerRef };
            if (tooltip.InlineVisuals != null)
                return new TooltipManifest { Type = "inline", Markdown = tooltip.InlineMarkdown, Visuals = tooltip.InlineVisuals };
            return new TooltipManifest { Type = "text", Text = tooltip.PlainText };
        }
    }
}
