using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ETL_SQL.Reporting.HtmlVisual;

/// <summary>
/// Validates and sanitizes HTML visual templates and CSS against closed allowlists.
/// Operates at analysis/build time — invalid content is rejected with diagnostics,
/// not silently stripped at render time.
/// </summary>
public sealed class HtmlSanitizer
{
    // ── Element allowlist ─────────────────────────────────────────────────

    private static readonly HashSet<string> AllowedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        // Structure
        "div", "span", "section", "article", "aside", "header", "footer", "nav", "main",
        // Headings
        "h1", "h2", "h3", "h4", "h5", "h6",
        // Text
        "p", "br", "hr", "pre", "code", "blockquote", "em", "strong", "i", "b", "u", "s",
        "small", "sub", "sup", "mark", "abbr", "time", "cite", "q", "dfn", "var", "kbd", "samp",
        // Lists
        "ul", "ol", "li", "dl", "dt", "dd",
        // Tables
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "colgroup", "col",
        // Media
        "img", "figure", "figcaption", "picture", "source",
        // Interactive
        "a", "button", "details", "summary",
        // Data
        "data", "meter", "progress", "output"
    };

    // ── Rejected elements (explicitly named for clarity in diagnostics) ──

    private static readonly HashSet<string> ExplicitlyRejectedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "link", "meta", "base", "iframe", "frame", "frameset",
        "object", "embed", "applet", "form", "input", "select", "textarea",
        "svg", "math", "template", "slot", "dialog", "canvas", "audio", "video",
        "track", "map", "area", "portal", "noscript"
    };

    // ── Attribute allowlists ─────────────────────────────────────────────

    private static readonly HashSet<string> GlobalAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "class", "id", "title", "lang", "dir", "role", "tabindex", "hidden"
    };

    private static readonly Dictionary<string, HashSet<string>> ElementAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = new(StringComparer.OrdinalIgnoreCase) { "href", "target", "rel" },
        ["img"] = new(StringComparer.OrdinalIgnoreCase) { "src", "alt", "width", "height", "loading" },
        ["button"] = new(StringComparer.OrdinalIgnoreCase) { "type", "disabled", "data-action", "data-param", "data-value" },
        ["td"] = new(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan", "scope", "headers" },
        ["th"] = new(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan", "scope", "headers" },
        ["col"] = new(StringComparer.OrdinalIgnoreCase) { "span" },
        ["colgroup"] = new(StringComparer.OrdinalIgnoreCase) { "span" },
        ["ol"] = new(StringComparer.OrdinalIgnoreCase) { "start", "type", "reversed" },
        ["time"] = new(StringComparer.OrdinalIgnoreCase) { "datetime" },
        ["meter"] = new(StringComparer.OrdinalIgnoreCase) { "min", "max", "low", "high", "optimum", "value" },
        ["progress"] = new(StringComparer.OrdinalIgnoreCase) { "max", "value" },
        ["data"] = new(StringComparer.OrdinalIgnoreCase) { "value" },
        ["abbr"] = new(StringComparer.OrdinalIgnoreCase) { "title" },
        ["blockquote"] = new(StringComparer.OrdinalIgnoreCase) { "cite" },
        ["q"] = new(StringComparer.OrdinalIgnoreCase) { "cite" },
        ["source"] = new(StringComparer.OrdinalIgnoreCase) { "srcset", "type", "media" },
        ["details"] = new(StringComparer.OrdinalIgnoreCase) { "open" },
    };

    // ── URL policy ───────────────────────────────────────────────────────

    private static readonly HashSet<string> SafeUrlSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "https", "http", "mailto", "tel"
    };

    private static readonly HashSet<string> SafeDataImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "data:image/png", "data:image/jpeg", "data:image/gif",
        "data:image/svg+xml", "data:image/webp"
    };

    private static readonly HashSet<string> UrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "cite", "srcset"
    };

    // ── CSS rejection patterns ───────────────────────────────────────────

    private static readonly Regex CssUnsafePatterns = new(
        @"@import|@font-face|expression\s*\(|-moz-binding|behavior\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CssExternalUrl = new(
        @"url\s*\(\s*['""]?\s*(?:https?:|\/\/)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CssUnsafeDataUrl = new(
        @"url\s*\(\s*['""]?\s*data:(?!image\/)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CssUnsafeVarPattern = new(
        @"var\s*\(\s*--(?!etl-)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── HTML element/attribute parsing ────────────────────────────────────

    private static readonly Regex HtmlTagPattern = new(
        @"<(?<closing>/)?(?<tag>[a-zA-Z][a-zA-Z0-9-]*)(?<attrs>[^>]*)(?<selfclose>/)?>",
        RegexOptions.Compiled);

    private static readonly Regex AttributePattern = new(
        @"(?<name>[a-zA-Z][a-zA-Z0-9_-]*)\s*(?:=\s*(?:""(?<dval>[^""]*)""|'(?<sval>[^']*)'|(?<uval>\S+)))?",
        RegexOptions.Compiled);

    private static readonly Regex EventHandlerPattern = new(
        @"^on[a-z]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates an HTML template against the element, attribute, and URL allowlists.
    /// Returns a list of violations. An empty list means the template is safe.
    /// </summary>
    public IReadOnlyList<SanitizationViolation> ValidateTemplate(string template)
    {
        var violations = new List<SanitizationViolation>();

        foreach (Match tagMatch in HtmlTagPattern.Matches(template))
        {
            var tagName = tagMatch.Groups["tag"].Value;
            var isClosing = tagMatch.Groups["closing"].Success;
            var attrsText = tagMatch.Groups["attrs"].Value;

            if (isClosing) continue;

            if (!AllowedElements.Contains(tagName))
            {
                var reason = ExplicitlyRejectedElements.Contains(tagName)
                    ? $"Element <{tagName}> is explicitly rejected for security."
                    : $"Element <{tagName}> is not in the HTML visual allowlist.";
                violations.Add(new SanitizationViolation(SanitizationCategory.Element, reason, tagMatch.Index));
                continue;
            }

            foreach (Match attrMatch in AttributePattern.Matches(attrsText))
            {
                var attrName = attrMatch.Groups["name"].Value;
                var attrValue = attrMatch.Groups["dval"].Success ? attrMatch.Groups["dval"].Value
                    : attrMatch.Groups["sval"].Success ? attrMatch.Groups["sval"].Value
                    : attrMatch.Groups["uval"].Success ? attrMatch.Groups["uval"].Value
                    : null;

                if (EventHandlerPattern.IsMatch(attrName))
                {
                    violations.Add(new SanitizationViolation(
                        SanitizationCategory.Attribute,
                        $"Event handler attribute '{attrName}' is rejected.",
                        tagMatch.Index));
                    continue;
                }

                if (string.Equals(attrName, "style", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new SanitizationViolation(
                        SanitizationCategory.Attribute,
                        "Inline 'style' attribute is rejected. Use STYLE(CSS=...) instead.",
                        tagMatch.Index));
                    continue;
                }

                if (!IsAttributeAllowed(tagName, attrName))
                {
                    violations.Add(new SanitizationViolation(
                        SanitizationCategory.Attribute,
                        $"Attribute '{attrName}' is not allowed on <{tagName}>.",
                        tagMatch.Index));
                    continue;
                }

                if (UrlAttributes.Contains(attrName) && attrValue != null)
                {
                    var urlViolation = ValidateUrl(attrValue, attrName, tagName);
                    if (urlViolation != null)
                        violations.Add(urlViolation with { Position = tagMatch.Index });
                }

                if (string.Equals(tagName, "button", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(attrName, "type", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(attrValue, "button", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new SanitizationViolation(
                        SanitizationCategory.Attribute,
                        $"Button type='{attrValue}' is rejected. Only type='button' is allowed.",
                        tagMatch.Index));
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Validates CSS content against the CSS security policy.
    /// Returns a list of violations.
    /// </summary>
    public IReadOnlyList<SanitizationViolation> ValidateCss(string css)
    {
        var violations = new List<SanitizationViolation>();

        foreach (Match match in CssUnsafePatterns.Matches(css))
        {
            violations.Add(new SanitizationViolation(
                SanitizationCategory.Css,
                $"CSS pattern '{match.Value}' is rejected.",
                match.Index));
        }

        foreach (Match match in CssExternalUrl.Matches(css))
        {
            violations.Add(new SanitizationViolation(
                SanitizationCategory.Css,
                "CSS url() with external host is rejected.",
                match.Index));
        }

        foreach (Match match in CssUnsafeDataUrl.Matches(css))
        {
            violations.Add(new SanitizationViolation(
                SanitizationCategory.Css,
                "CSS url() with non-image data: URI is rejected.",
                match.Index));
        }

        foreach (Match match in CssUnsafeVarPattern.Matches(css))
        {
            violations.Add(new SanitizationViolation(
                SanitizationCategory.Css,
                "CSS var() must reference --etl-* tokens only.",
                match.Index));
        }

        return violations;
    }

    /// <summary>
    /// Scopes CSS selectors under a visual container ID.
    /// </summary>
    public string ScopeCss(string css, string visualContainerId)
    {
        var scopePrefix = $"#{visualContainerId}";
        return Regex.Replace(css, @"([^{}@]+)\{", match =>
        {
            var selector = match.Groups[1].Value.Trim();
            if (selector.StartsWith("@")) return match.Value;
            var selectors = selector.Split(',')
                .Select(s => $"{scopePrefix} {s.Trim()}")
                .ToArray();
            return string.Join(", ", selectors) + " {";
        });
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private static bool IsAttributeAllowed(string tagName, string attrName)
    {
        if (GlobalAttributes.Contains(attrName)) return true;
        if (attrName.StartsWith("aria-", StringComparison.OrdinalIgnoreCase)) return true;
        if (attrName.StartsWith("data-etl-", StringComparison.OrdinalIgnoreCase)) return true;

        if (ElementAttributes.TryGetValue(tagName, out var elementAttrs))
            return elementAttrs.Contains(attrName);

        return false;
    }

    private static SanitizationViolation? ValidateUrl(string url, string attrName, string tagName)
    {
        var trimmed = url.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        if (trimmed.Contains("{{"))
        {
            var beforeSubst = trimmed.Substring(0, trimmed.IndexOf("{{", StringComparison.Ordinal));
            if (beforeSubst.Length == 0)
                return new SanitizationViolation(SanitizationCategory.Url,
                    "URL attributes must start with a static scheme (https:, http:, mailto:, tel:, or #). " +
                    "Template substitutions cannot control the URL scheme.", 0);
            var hasStaticScheme = SafeUrlSchemes.Any(s =>
                beforeSubst.StartsWith(s + ":", StringComparison.OrdinalIgnoreCase)
                || beforeSubst.StartsWith(s + "://", StringComparison.OrdinalIgnoreCase));
            if (!hasStaticScheme && !beforeSubst.StartsWith("#"))
                return new SanitizationViolation(SanitizationCategory.Url,
                    $"URL prefix '{beforeSubst}' before template substitution is not a safe scheme.", 0);
            return null;
        }

        if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return new SanitizationViolation(SanitizationCategory.Url, "javascript: URLs are rejected.", 0);

        if (trimmed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
            return new SanitizationViolation(SanitizationCategory.Url, "vbscript: URLs are rejected.", 0);

        if (trimmed.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            return new SanitizationViolation(SanitizationCategory.Url, "blob: URLs are rejected.", 0);

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var isImageData = SafeDataImageTypes.Any(prefix =>
                trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!isImageData)
                return new SanitizationViolation(SanitizationCategory.Url,
                    $"data: URLs are only allowed for images (data:image/*). Got: {trimmed.Substring(0, Math.Min(50, trimmed.Length))}...", 0);
            return null;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (!SafeUrlSchemes.Contains(uri.Scheme))
                return new SanitizationViolation(SanitizationCategory.Url,
                    $"URL scheme '{uri.Scheme}:' is not allowed.", 0);
            return null;
        }

        // Reject relative URLs — templates have no base URL
        if (!trimmed.StartsWith("#"))
            return new SanitizationViolation(SanitizationCategory.Url,
                "Relative URLs are not allowed in HTML visual templates.", 0);

        return null;
    }
}

public enum SanitizationCategory
{
    Element,
    Attribute,
    Url,
    Css
}

public sealed record SanitizationViolation(
    SanitizationCategory Category,
    string Message,
    int Position);
