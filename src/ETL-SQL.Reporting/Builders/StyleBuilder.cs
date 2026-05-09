using System;
using System.Collections.Generic;
using ETL_SQL.Core;

namespace ETL_SQL.ReportBuilder.Builders
{
    public class StyleBuilder(IExecutionContext ctx)
    {
        public Dictionary<string, string> ResolveReportStyles()
        {
            var styles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(ctx.ReportContext.ReportTheme))
                styles["THEME"] = ctx.ReportContext.ReportTheme;
            return styles;
        }

        public Dictionary<string, string> ResolveStyles(string? styleName, Dictionary<string, string> inlineStyles)
            => ResolveStyles(styleName, inlineStyles, null);

        public Dictionary<string, string> ResolveStyles(
            string? styleName,
            Dictionary<string, string> inlineStyles,
            IReadOnlyDictionary<string, string>? inheritedStyles)
        {
            if (inheritedStyles == null && string.IsNullOrEmpty(styleName) && inlineStyles.Count == 0)
                return new Dictionary<string, string>();

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (inheritedStyles != null)
                MergeInto(merged, inheritedStyles);

            if (!string.IsNullOrEmpty(styleName))
                MergeInto(merged, ResolveNamedStyle(styleName, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

            foreach (var kv in inlineStyles)
                merged[kv.Key] = ResolveStyleValue(kv.Value);

            return merged;
        }

        private Dictionary<string, string> ResolveNamedStyle(string styleName, HashSet<string> visited)
        {
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!visited.Add(styleName) ||
                !ctx.ReportContext.StyleDefinitions.TryGetValue(styleName, out var namedStyle))
            {
                return resolved;
            }

            if (!string.IsNullOrEmpty(namedStyle.StyleName))
                MergeInto(resolved, ResolveNamedStyle(namedStyle.StyleName, visited));

            foreach (var kv in namedStyle.Styles)
                resolved[kv.Key] = ResolveStyleValue(kv.Value);

            return resolved;
        }

        private void MergeInto(
            Dictionary<string, string> target,
            IReadOnlyDictionary<string, string> source)
        {
            foreach (var kv in source)
                target[kv.Key] = ResolveStyleValue(kv.Value);
        }

        private string ResolveStyleValue(string value)
            => value.StartsWith("@", StringComparison.Ordinal)
                ? ctx.VarContext.GetVariable(value)?.ToString() ?? value
                : value;

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
