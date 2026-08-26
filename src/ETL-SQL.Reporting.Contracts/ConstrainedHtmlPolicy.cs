using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ETL_SQL.Reporting.Semantics;

/// <summary>
/// Renderer-neutral security and resource policy shared by authoring diagnostics and manifest builds.
/// It validates authored markup; renderers still treat manifest payloads as untrusted input.
/// </summary>
public static class ConstrainedHtmlPolicy
{
    public const int DefaultMaxRows = 500;
    public const int MaxTemplateNodes = 200;
    public const int MaxOutputNodes = 10_000;
    public const int MaxTemplateBytes = 64 * 1024;
    public const int MaxCssBytes = 32 * 1024;
    public const int MaxOutputBytes = 2 * 1024 * 1024;
    public const int MaxVisualRenderWork = 20_000;
    public const int MaxReportOutputNodes = 50_000;
    public const int MaxReportOutputBytes = 8 * 1024 * 1024;
    public const int MaxReportRenderWork = 100_000;
    public const int MaxEmbeddedVisualQueries = 100;
    public const int MaxConditionalDepth = 4;
    public const int MaxEmbedDepth = 2;

    private static readonly HashSet<string> AllowedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "div", "span", "section", "article", "aside", "header", "footer", "nav", "main",
        "h1", "h2", "h3", "h4", "h5", "h6", "p", "br", "hr", "pre", "code", "blockquote",
        "em", "strong", "i", "b", "u", "s", "small", "sub", "sup", "mark", "abbr", "time",
        "cite", "q", "dfn", "var", "kbd", "samp", "ul", "ol", "li", "dl", "dt", "dd",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "colgroup", "col",
        "img", "figure", "figcaption", "picture", "source", "a", "button", "details", "summary",
        "data", "meter", "progress", "output"
    };

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
        ["details"] = new(StringComparer.OrdinalIgnoreCase) { "open" }
    };

    private static readonly HashSet<string> SafeSchemes = new(StringComparer.OrdinalIgnoreCase) { "https", "http", "mailto", "tel" };
    private static readonly string[] SafeDataImages = ["data:image/png", "data:image/jpeg", "data:image/gif", "data:image/webp"];
    private static readonly Regex TagPattern = new(@"<(?<closing>/)?(?<tag>[a-zA-Z][a-zA-Z0-9-]*)(?<attrs>[^>]*)(?<selfclose>/)?>", RegexOptions.Compiled);
    private static readonly Regex AttributePattern = new("""(?<name>[a-zA-Z][a-zA-Z0-9_-]*)\s*(?:=\s*(?:"(?<dval>[^"]*)"|'(?<sval>[^']*)'|(?<uval>\S+)))?""", RegexOptions.Compiled);
    private static readonly Regex CssUnsafe = new("""@import|@font-face|expression\s*\(|-moz-binding|behavior\s*:|javascript\s*:|url\s*\(\s*['"]?\s*(?:https?:|//)|url\s*\(\s*['"]?\s*data:(?!image/)|var\s*\(\s*--(?!etl-)|\\""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BindingPattern = new(@"\{\{\s*(?![#/])(?<name>@?[A-Za-z_][A-Za-z0-9_]*)(?:\s+FORMAT\s+'[^']*')?\s*\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ConditionalBindingPattern = new(@"\{\{\s*#IF\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmbedPattern = new(@"\{\{\s*VISUAL\s*\(\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TemplateExpressionPattern = new(@"\{\{(?<body>[^}]*)\}\}", RegexOptions.Compiled);
    private static readonly Regex ValidEmbedBodyPattern = new(
        @"^\s*VISUAL\s*\(\s*[A-Za-z_][A-Za-z0-9_]*(?:\s*,\s*PARAMETERS\s*\(\s*(?:@[A-Za-z_][A-Za-z0-9_]*\s*=\s*(?:@?[A-Za-z_][A-Za-z0-9_]*|'(?:''|[^'])*'|-?\d+(?:\.\d+)?)(?:\s*,\s*@[A-Za-z_][A-Za-z0-9_]*\s*=\s*(?:@?[A-Za-z_][A-Za-z0-9_]*|'(?:''|[^'])*'|-?\d+(?:\.\d+)?))*\s*)?\s*\))?\s*\)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ParameterPattern = new(@"@[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly Regex EmbedValuePattern = new(@"=\s*(?<value>@?[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    public static IReadOnlyList<ConstrainedHtmlViolation> ValidateTemplate(string template)
    {
        var violations = new List<ConstrainedHtmlViolation>();
        foreach (Match tag in TagPattern.Matches(template))
        {
            if (tag.Groups["closing"].Success) continue;
            var name = tag.Groups["tag"].Value;
            if (!AllowedElements.Contains(name))
            {
                violations.Add(new("Element", $"Element <{name}> is not allowed in constrained HTML.", tag.Index));
                continue;
            }
            var hasAlt = false;
            foreach (Match attribute in AttributePattern.Matches(tag.Groups["attrs"].Value))
            {
                var attr = attribute.Groups["name"].Value;
                var value = attribute.Groups["dval"].Success ? attribute.Groups["dval"].Value
                    : attribute.Groups["sval"].Success ? attribute.Groups["sval"].Value
                    : attribute.Groups["uval"].Success ? attribute.Groups["uval"].Value : null;
                hasAlt |= attr.Equals("alt", StringComparison.OrdinalIgnoreCase);
                if (attr.StartsWith("on", StringComparison.OrdinalIgnoreCase) || attr.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new("Attribute", $"Attribute '{attr}' is not allowed in constrained HTML.", tag.Index));
                    continue;
                }
                if (!IsAttributeAllowed(name, attr))
                {
                    violations.Add(new("Attribute", $"Attribute '{attr}' is not allowed on <{name}>.", tag.Index));
                    continue;
                }
                if (value is not null && (attr.Equals("href", StringComparison.OrdinalIgnoreCase)
                    || attr.Equals("src", StringComparison.OrdinalIgnoreCase)
                    || attr.Equals("cite", StringComparison.OrdinalIgnoreCase)
                    || attr.Equals("srcset", StringComparison.OrdinalIgnoreCase)))
                {
                    var urlError = ValidateUrl(value);
                    if (urlError is not null) violations.Add(new("Url", urlError, tag.Index));
                }
                if (name.Equals("button", StringComparison.OrdinalIgnoreCase) && attr.Equals("type", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "button", StringComparison.OrdinalIgnoreCase))
                    violations.Add(new("Attribute", "Only type='button' is allowed on HTML visual buttons.", tag.Index));
            }
            if (name.Equals("img", StringComparison.OrdinalIgnoreCase) && !hasAlt)
                violations.Add(new("Attribute", "HTML visual images require an alt attribute.", tag.Index));
        }
        var withoutExpressions = TemplateExpressionPattern.Replace(template, string.Empty);
        var residual = TagPattern.Replace(withoutExpressions, string.Empty);
        var malformed = Regex.Match(residual, @"<\s*(?:[!/]|[A-Za-z])");
        if (malformed.Success)
            violations.Add(new("Markup", "Malformed or non-element HTML markup is not allowed in constrained HTML.", malformed.Index));
        return violations;
    }

    public static IReadOnlyList<ConstrainedHtmlViolation> ValidateCss(string css)
    {
        var normalized = Regex.Replace(css, @"/\*[\s\S]*?\*/", string.Empty);
        var violations = CssUnsafe.Matches(normalized)
            .Select(match => new ConstrainedHtmlViolation("Css", $"CSS pattern '{match.Value}' is not allowed.", match.Index)).ToList();
        violations.AddRange(Regex.Matches(normalized, @"@(?<name>[A-Za-z-]+)")
            .Where(match => match.Groups["name"].Value is not ("media" or "keyframes")
                && !match.Groups["name"].Value.Equals("media", StringComparison.OrdinalIgnoreCase)
                && !match.Groups["name"].Value.Equals("keyframes", StringComparison.OrdinalIgnoreCase))
            .Select(match => new ConstrainedHtmlViolation("Css", $"CSS at-rule '@{match.Groups["name"].Value}' is not allowed.", match.Index)));
        return violations;
    }

    public static int CountElementNodes(string template) => TagPattern.Matches(template).Count(match => !match.Groups["closing"].Success);
    public static IReadOnlyList<string> Bindings(string template) => BindingPattern.Matches(template)
        .Concat(ConditionalBindingPattern.Matches(template))
        .Select(match => match.Groups["name"].Value)
        .ToList();
    public static IReadOnlyList<string> EmbeddedVisuals(string template) => EmbedPattern.Matches(template).Select(match => match.Groups["name"].Value).ToList();
    public static IReadOnlyList<string> EmbeddedParameters(string template) => TemplateExpressionPattern.Matches(template)
        .Where(match => match.Groups["body"].Value.TrimStart().StartsWith("VISUAL", StringComparison.OrdinalIgnoreCase))
        .SelectMany(match => ParameterPattern.Matches(match.Groups["body"].Value).Select(parameter => parameter.Value))
        .ToList();
    public static IReadOnlyList<string> EmbeddedFields(string template) => TemplateExpressionPattern.Matches(template)
        .Where(match => match.Groups["body"].Value.TrimStart().StartsWith("VISUAL", StringComparison.OrdinalIgnoreCase))
        .SelectMany(match => EmbedValuePattern.Matches(match.Groups["body"].Value)
            .Select(value => value.Groups["value"].Value)
            .Where(value => !value.StartsWith('@')))
        .ToList();
    public static IReadOnlyList<ConstrainedHtmlViolation> ValidateEmbedSyntax(string template) => TemplateExpressionPattern.Matches(template)
        .Where(match => match.Groups["body"].Value.TrimStart().StartsWith("VISUAL", StringComparison.OrdinalIgnoreCase)
            && !ValidEmbedBodyPattern.IsMatch(match.Groups["body"].Value))
        .Select(match => new ConstrainedHtmlViolation("Embed", "Invalid VISUAL(...) helper syntax.", match.Index))
        .ToList();

    public static string ScopeCss(string css, string containerId)
    {
        var keyframes = Regex.Matches(css, @"@keyframes\s+(?<name>[A-Za-z_][A-Za-z0-9_-]*)", RegexOptions.IgnoreCase)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => $"{containerId}-{name}", StringComparer.Ordinal);
        foreach (var keyframe in keyframes)
        {
            css = Regex.Replace(css, $@"(?i)(@keyframes\s+){Regex.Escape(keyframe.Key)}\b", $"$1{keyframe.Value}");
            css = Regex.Replace(css, $@"(?i)(animation-name\s*:\s*){Regex.Escape(keyframe.Key)}\b", $"$1{keyframe.Value}");
            css = Regex.Replace(css, $@"(?i)(animation\s*:\s*){Regex.Escape(keyframe.Key)}\b", $"$1{keyframe.Value}");
        }
        return ScopeCssBlock(css, $"#{containerId}", inKeyframes: false);
    }

    private static string ScopeCssBlock(string css, string scope, bool inKeyframes)
    {
        var result = new StringBuilder(css.Length + 32);
        var position = 0;
        while (position < css.Length)
        {
            var open = css.IndexOf('{', position);
            if (open < 0)
            {
                result.Append(css, position, css.Length - position);
                break;
            }
            var close = FindClosingBrace(css, open);
            if (close < 0)
            {
                result.Append(css, position, css.Length - position);
                break;
            }
            var header = css[position..open];
            var body = css[(open + 1)..close];
            var trimmed = header.Trim();
            if (trimmed.StartsWith("@media", StringComparison.OrdinalIgnoreCase))
            {
                result.Append(header).Append('{').Append(ScopeCssBlock(body, scope, inKeyframes: false)).Append('}');
            }
            else if (trimmed.StartsWith("@keyframes", StringComparison.OrdinalIgnoreCase))
            {
                result.Append(header).Append('{').Append(ScopeCssBlock(body, scope, inKeyframes: true)).Append('}');
            }
            else if (inKeyframes)
            {
                result.Append(header).Append('{').Append(body).Append('}');
            }
            else
            {
                var leadingLength = header.Length - header.TrimStart().Length;
                result.Append(header.AsSpan(0, leadingLength));
                result.Append(string.Join(", ", trimmed.Split(',').Select(selector => $"{scope} {selector.Trim()}")));
                result.Append(" {").Append(body).Append('}');
            }
            position = close + 1;
        }
        return result.ToString();
    }

    private static int FindClosingBrace(string css, int open)
    {
        var depth = 1;
        var quote = '\0';
        for (var index = open + 1; index < css.Length; index++)
        {
            var character = css[index];
            if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                continue;
            }
            if (character is '\'' or '"') quote = character;
            else if (character == '{') depth++;
            else if (character == '}' && --depth == 0) return index;
        }
        return -1;
    }

    private static bool IsAttributeAllowed(string element, string attribute) =>
        GlobalAttributes.Contains(attribute) || attribute.StartsWith("aria-", StringComparison.OrdinalIgnoreCase)
        || attribute.StartsWith("data-etl-", StringComparison.OrdinalIgnoreCase)
            && !attribute.Equals("data-etl-embed-id", StringComparison.OrdinalIgnoreCase)
        || ElementAttributes.TryGetValue(element, out var attributes) && attributes.Contains(attribute);

    private static string? ValidateUrl(string value)
    {
        var url = WebUtility.HtmlDecode(value).Trim();
        if (url.Any(character => character < ' ' || character == '\u007f'))
            return "Control characters are not allowed in HTML visual URLs.";
        if (url.Length == 0 || url.StartsWith('#')) return null;
        if (url.Contains("{{", StringComparison.Ordinal))
        {
            var prefix = url[..url.IndexOf("{{", StringComparison.Ordinal)];
            return SafeSchemes.Any(scheme => prefix.StartsWith(scheme + ":", StringComparison.OrdinalIgnoreCase))
                ? null : "URL substitutions cannot control the URL scheme.";
        }
        if (url.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase))
            return ValidateSvgDataUrl(url) ? null : "Only script-free data:image URLs are allowed.";
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return SafeDataImages.Any(prefix => url.StartsWith(prefix + ";", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith(prefix + ",", StringComparison.OrdinalIgnoreCase))
                ? null : "Only script-free data:image URLs are allowed.";
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && SafeSchemes.Contains(uri.Scheme)
            ? null : "Only http:, https:, mailto:, tel:, fragment, and approved data:image URLs are allowed.";
    }

    private static bool ValidateSvgDataUrl(string url)
    {
        var comma = url.IndexOf(',');
        if (comma < 0) return false;
        try
        {
            var header = url[..comma];
            var payload = url[(comma + 1)..];
            var svg = header.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetString(Convert.FromBase64String(payload))
                : Uri.UnescapeDataString(payload);
            return !Regex.IsMatch(svg,
                """<\s*(?:script|foreignObject)\b|\bon[a-z]+\s*=|(?:href|src)\s*=\s*['"]?\s*javascript:""",
                RegexOptions.IgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record ConstrainedHtmlViolation(string Category, string Message, int Position);
