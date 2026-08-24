using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Projects a <see cref="TooltipManifest"/> onto surfaces that cannot be hovered —
    /// PDF, print, Markdown, email, terminal, plain text, screen-reader summaries, and
    /// offline snapshots.
    /// </summary>
    /// <remarks>
    /// Static surfaces are not required to expand interactive detail, but they must never
    /// imply that hover is available. Every projection here is therefore either the literal
    /// static text (which carries no interaction) or a concise semantic summary naming the
    /// detail that a browser reader would be able to open. One implementation keeps the
    /// wording identical across every export path.
    /// </remarks>
    public static class DetailSurfaceProjection
    {
        /// <summary>
        /// Sentence describing a persistent detail popover without implying interaction is
        /// available here. Used verbatim by every static renderer.
        /// </summary>
        public const string InteractiveDetailNotice = "Interactive detail available in browser";

        /// <summary>
        /// Returns a one-line, deterministic description of the detail surface, or
        /// <c>null</c> when the visual declares none.
        /// </summary>
        /// <param name="tooltip">The visual's tooltip manifest, may be <c>null</c>.</param>
        public static string? Describe(TooltipManifest? tooltip)
        {
            if (tooltip == null) return null;

            if (IsPopover(tooltip))
            {
                var names = ResolvedVisualNames(tooltip);
                return names.Count == 0
                    ? InteractiveDetailNotice + "."
                    : $"{InteractiveDetailNotice}: {string.Join(", ", names)}.";
            }

            // Transient text is static content; reproducing it loses nothing and adds no
            // implication of interactivity.
            var text = (tooltip.Text ?? tooltip.Markdown ?? string.Empty).Trim();
            return text.Length == 0 ? null : $"Detail: {text}";
        }

        /// <summary>
        /// True when the manifest describes a persistent, focusable detail popover rather
        /// than a transient text tooltip. Manifests published before <c>mode</c> existed are
        /// classified from <c>type</c> so older reports keep exporting correctly.
        /// </summary>
        public static bool IsPopover(TooltipManifest tooltip)
        {
            ArgumentNullException.ThrowIfNull(tooltip);

            if (string.Equals(tooltip.Mode, TooltipManifest.PopoverMode, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(tooltip.Mode, TooltipManifest.TooltipMode, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(tooltip.Type, "container", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(tooltip.Type, "inline", StringComparison.OrdinalIgnoreCase)
                && tooltip.Visuals is { Count: > 0 };
        }

        /// <summary>
        /// The visual names the surface renders. Prefers the statically resolved list, which
        /// follows a referenced container graph; falls back to the authored inline list.
        /// </summary>
        public static IReadOnlyList<string> ResolvedVisualNames(TooltipManifest tooltip)
        {
            ArgumentNullException.ThrowIfNull(tooltip);

            var names = tooltip.ResolvedVisuals ?? tooltip.Visuals;
            return names?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
    }
}
