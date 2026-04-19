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
            
            var (text, isMd) = ResolveMarkdown(tooltip.PlainText);
            return new TooltipManifest { Type = "text", Text = text, IsMarkdown = isMd };
        }

        public (string? Value, bool IsMarkdown) ResolveMarkdown(string? input, bool parserFlag = false)
        {
            if (string.IsNullOrEmpty(input)) return (null, false);
            if (input.StartsWith("@"))
            {
                var val = ctx.VarContext.GetVariable(input);
                bool typeMd = false;
                if (ctx.VarContext.VariableMetadata.TryGetValue(input, out var meta))
                {
                    typeMd = meta.DataType?.Equals("MARKDOWN", StringComparison.OrdinalIgnoreCase) == true;
                }
                return (val?.ToString(), parserFlag || typeMd);
            }
            return (input, parserFlag);
        }
    }
}
