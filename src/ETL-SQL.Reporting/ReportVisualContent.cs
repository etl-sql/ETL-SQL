#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Shared resolution of report-visual content that every static renderer (PDF, Markdown, …)
    /// must agree on. Centralizing it prevents the renderers from drifting — e.g. a TEXT visual
    /// whose content lives under <c>CONTENT</c> must not export non-empty in PDF but blank in
    /// Markdown.
    /// </summary>
    internal static class ReportVisualContent
    {
        /// <summary>
        /// Resolve a TEXT visual's content across every option-key variant: static TEXT
        /// (<c>CONTENT</c>/<c>content</c>/<c>VALUE</c>/<c>value</c>/<c>DefaultValue</c>) and dynamic
        /// TEXT bound to a source column via <c>mapping:content</c>.
        /// </summary>
        public static string? ResolveTextContent(VisualManifest v)
        {
            if (v.Options.TryGetValue("CONTENT", out var content) && !string.IsNullOrWhiteSpace(content))
                return content;
            if (v.Options.TryGetValue("content", out content) && !string.IsNullOrWhiteSpace(content))
                return content;
            if (v.Options.TryGetValue("VALUE", out content) && !string.IsNullOrWhiteSpace(content))
                return content;
            if (v.Options.TryGetValue("value", out content) && !string.IsNullOrWhiteSpace(content))
                return content;
            if (!string.IsNullOrWhiteSpace(v.DefaultValue))
                return v.DefaultValue;

            // Dynamic TEXT uses SOURCE + MAPPINGS (content = col); the source data is in Columns/Rows.
            var contentColumn = v.Options.GetValueOrDefault("mapping:content")
                ?? v.Options.GetValueOrDefault("MAPPING:CONTENT");
            if (string.IsNullOrWhiteSpace(contentColumn) || v.Rows.Count == 0)
                return null;

            var contentIndex = v.Columns.FindIndex(c =>
                string.Equals(c, contentColumn, StringComparison.OrdinalIgnoreCase));
            if (contentIndex < 0 || contentIndex >= v.Rows[0].Count)
                return null;

            return v.Rows[0][contentIndex];
        }

        /// <summary>
        /// The parameter a filter/input control binds to: a <c>SET_PARAMETER</c> action, else an
        /// options key (<c>PARAMETER</c>/<c>parameter</c>/<c>data-parameter</c>).
        /// </summary>
        public static string? ResolveFilterParameterName(VisualManifest v)
        {
            var paramName = v.Actions
                .FirstOrDefault(a => string.Equals(a.Type, "SET_PARAMETER", StringComparison.OrdinalIgnoreCase))
                ?.ParameterName;
            if (string.IsNullOrEmpty(paramName))
                paramName = v.Options.GetValueOrDefault("PARAMETER")
                         ?? v.Options.GetValueOrDefault("parameter")
                         ?? v.Options.GetValueOrDefault("data-parameter");
            return paramName;
        }

        /// <summary>
        /// The filter's selected value at export time, or <c>"(all)"</c> when nothing is selected.
        /// </summary>
        public static string ResolveFilterDisplay(VisualManifest v, ReportManifest manifest)
        {
            var paramName = ResolveFilterParameterName(v);
            string? value = null;
            if (!string.IsNullOrEmpty(paramName))
                manifest.Parameters.TryGetValue(paramName, out value);
            return string.IsNullOrWhiteSpace(value) ? "(all)" : value!;
        }
    }
}
