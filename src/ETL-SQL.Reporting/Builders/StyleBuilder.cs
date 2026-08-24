using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Reporting;

namespace ETL_SQL.Reporting.Builders
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

        /// <summary>
        /// Projects a <see cref="TooltipDefinition"/> onto the wire. The detail-surface
        /// contract is resolved statically here so every consumer receives the same
        /// <c>mode</c> and the same resolved visual list rather than re-deriving them.
        /// </summary>
        /// <exception cref="ExecutionException">
        /// Thrown when the surface violates the detail-surface contract. Detail surfaces fail
        /// closed: a report with an unresolvable, cyclic, nested, or over-budget tooltip is
        /// rejected rather than published with a surface that silently does nothing.
        /// </exception>
        public async Task<TooltipManifest?> BuildTooltipManifestAsync(TooltipDefinition? tooltip)
            => await BuildTooltipManifestAsync(tooltip, ownerObject: null);

        /// <inheritdoc cref="BuildTooltipManifestAsync(TooltipDefinition?)"/>
        public async Task<TooltipManifest?> BuildTooltipManifestAsync(
            TooltipDefinition? tooltip,
            string? ownerObject)
        {
            if (tooltip == null) return null;

            var resolved = ResolveDetailSurface(tooltip, ownerObject ?? "<visual>");

            if (tooltip.ContainerRef != null)
            {
                return WithStaticSummary(new TooltipManifest
                {
                    Type = "container",
                    Mode = TooltipManifest.PopoverMode,
                    ContainerRef = tooltip.ContainerRef,
                    ResolvedVisuals = resolved.Visuals.ToList()
                });
            }

            if (tooltip.IsInline)
            {
                return WithStaticSummary(new TooltipManifest
                {
                    Type = "inline",
                    Mode = tooltip.Kind == DetailSurfaceKind.Persistent
                        ? TooltipManifest.PopoverMode
                        : TooltipManifest.TooltipMode,
                    Markdown = tooltip.InlineMarkdown,
                    Visuals = tooltip.InlineVisuals,
                    ResolvedVisuals = resolved.Visuals.ToList()
                });
            }

            var (text, isMd) = await ResolveMarkdownAsync(tooltip.PlainText);

            // An expression-valued tooltip is only measurable once evaluated; apply the same
            // limit the resolver applies to literals so the boundary cannot be bypassed.
            if (text is { Length: > DetailSurfaceLimits.MaxTransientTextLength })
            {
                throw new ExecutionException(
                    $"[{DetailSurfaceDiagnostics.TransientTextTooLong}] The transient tooltip on " +
                    $"'{ownerObject ?? "<visual>"}' evaluated to {text.Length} characters, exceeding " +
                    $"the limit of {DetailSurfaceLimits.MaxTransientTextLength}. Shorten the text, or " +
                    "use a referenced container to present long-form detail as a focusable popover.");
            }

            return WithStaticSummary(new TooltipManifest
            {
                Type = "text",
                Mode = TooltipManifest.TooltipMode,
                Text = text,
                IsMarkdown = isMd
            });
        }

        /// <summary>
        /// Stamps the non-hoverable fallback onto the manifest so the browser's print output
        /// and the static exporters share one wording instead of each composing their own.
        /// </summary>
        private static TooltipManifest WithStaticSummary(TooltipManifest manifest)
        {
            manifest.StaticSummary = DetailSurfaceProjection.Describe(manifest);
            return manifest;
        }

        /// <summary>
        /// Runs the static detail-surface contract over the report's declared objects and
        /// converts any error diagnostic into a fail-closed <see cref="ExecutionException"/>.
        /// </summary>
        private ResolvedDetailSurface ResolveDetailSurface(TooltipDefinition tooltip, string ownerObject)
        {
            var diagnostics = new List<DetailSurfaceDiagnostic>();
            var visuals = new Dictionary<string, CreateVisualStatement>(
                ctx.ReportContext.VisualDefinitions, StringComparer.OrdinalIgnoreCase);
            var containers = new Dictionary<string, CreateContainerStatement>(
                ctx.ReportContext.ContainerDefinitions, StringComparer.OrdinalIgnoreCase);

            // Supplying the owning visual enables the row-context contract; containers and
            // buttons resolve without it because they have no row to disclose.
            visuals.TryGetValue(ownerObject, out var ownerVisual);

            var resolved = DetailSurfaceResolver.Resolve(
                ownerObject, tooltip, visuals, containers, diagnostics, ownerVisual);

            var error = diagnostics.FirstOrDefault(d => d.Severity == DetailSurfaceSeverity.Error);
            if (error != null)
                throw new ExecutionException($"[{error.Code}] {error.Message}");

            return resolved;
        }

        public async Task<(string? Value, bool IsMarkdown)> ResolveMarkdownAsync(Expression? input, bool parserFlag = false)
        {
            if (input == null) return (null, false);

            // If it's a literal string, check if it's a variable reference first (for backward compatibility)
            if (input is LiteralExpression lit && lit.Value is string s && s.StartsWith("@"))
            {
                var val = ctx.VarContext.GetVariable(s);
                bool typeMd = false;
                if (ctx.VarContext.VariableMetadata.TryGetValue(s, out var meta))
                {
                    typeMd = meta.DataType?.Equals("MARKDOWN", StringComparison.OrdinalIgnoreCase) == true;
                }
                return (val?.ToString(), parserFlag || typeMd);
            }

            // Otherwise, evaluate the expression
            var result = await ctx.EvaluateValue(input, null!);
            return (result?.ToString(), parserFlag);
        }
    }
}
