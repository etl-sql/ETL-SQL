using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting.HtmlVisual;
using Xunit;

namespace ETL_SQL.Tests.Reporting.HtmlVisual;

public class HtmlVisualParserTests
{
    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).Parse();
    }

    [Fact]
    public void ParseCreateVisual_Html_BasicSingle()
    {
        const string sql = """
            CREATE VISUAL StatusCard AS HTML (
                SOURCE = #data,
                TEMPLATE = '<div>{{Name}}</div>'
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal(VisualType.Html, stmt.VisualType);
        Assert.NotNull(stmt.HtmlTemplate);
        Assert.Equal("<div>{{Name}}</div>", stmt.HtmlTemplate!.Template);
        Assert.Equal(HtmlVisualMode.Single, stmt.HtmlTemplate.Mode);
        Assert.Null(stmt.HtmlTemplate.Css);
        Assert.Null(stmt.HtmlTemplate.Fallback);
    }

    [Fact]
    public void ParseCreateVisual_Html_RepeaterWithStyleAndFallback()
    {
        const string sql = """
            CREATE VISUAL NodeList AS HTML (
                SOURCE = #nodes,
                MODE = REPEATER,
                TEMPLATE = '<article class="card">{{HostName}}</article>',
                STYLE ( CSS = '.card { padding: 1rem; }' ),
                FALLBACK = 'Node: {{HostName}}'
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal(VisualType.Html, stmt.VisualType);
        Assert.NotNull(stmt.HtmlTemplate);
        Assert.Equal(HtmlVisualMode.Repeater, stmt.HtmlTemplate!.Mode);
        Assert.Contains("card", stmt.HtmlTemplate.Template);
        Assert.Equal(".card { padding: 1rem; }", stmt.HtmlTemplate.Css);
        Assert.Equal("Node: {{HostName}}", stmt.HtmlTemplate.Fallback);
    }

    [Fact]
    public void ParseCreateVisual_Html_SourceFree()
    {
        const string sql = """
            CREATE VISUAL Banner AS HTML (
                TEMPLATE = '<h1>Welcome</h1>'
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal(VisualType.Html, stmt.VisualType);
        Assert.NotNull(stmt.Source);
    }

    [Fact]
    public void ParseCreateVisual_Html_RepeaterWithoutSource_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS HTML (
                MODE = REPEATER,
                TEMPLATE = '<div>{{X}}</div>'
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_MissingTemplate_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS HTML (
                SOURCE = #data
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_WithMappings_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS HTML (
                SOURCE = #data,
                TEMPLATE = '<div>test</div>',
                MAPPINGS (X = Col1)
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_WithChart_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS HTML (
                SOURCE = #data,
                TEMPLATE = '<div>test</div>',
                CHART (
                    COORDINATE (TYPE = CARTESIAN),
                    LAYERS (
                        LAYER BAR (X = FIELD(Cat), Y = FIELD(Val))
                    ),
                    RESOLVE (X = SHARED, Y = SHARED, COLOR = SHARED)
                )
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_TemplateOnNonHtml_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS BAR (
                SOURCE = #data,
                TEMPLATE = '<div>test</div>',
                MAPPINGS (X = Cat, Y = Val)
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_WithActions()
    {
        const string sql = """
            CREATE VISUAL Tile AS HTML (
                SOURCE = #data,
                TEMPLATE = '<div>{{Name}}</div>',
                ACTIONS (
                    ON_CLICK = SET_PARAMETER(@selected, Name)
                )
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Single(stmt.Actions);
    }

    [Fact]
    public void ParseCreateVisual_Html_RoundTrip()
    {
        const string sql = """
            CREATE VISUAL Card AS HTML (
                SOURCE = #data,
                MODE = REPEATER,
                TEMPLATE = '<div>{{Name}}</div>',
                STYLE ( CSS = '.x { color: red; }' ),
                FALLBACK = '{{Name}}'
            );
            """;
        var script = Parse(sql);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        var formatted = stmt.ToSql();
        Assert.Contains("AS HTML", formatted);
        Assert.Contains("MODE = REPEATER", formatted);
        Assert.Contains("TEMPLATE =", formatted);
    }

    [Fact]
    public void ParseCreateVisual_Html_OrAlter()
    {
        const string sql = """
            CREATE OR ALTER VISUAL Card AS HTML (
                SOURCE = #data,
                TEMPLATE = '<p>{{Value}}</p>'
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal(ObjectCreationMode.CreateOrAlter, stmt.Mode);
        Assert.Equal(VisualType.Html, stmt.VisualType);
    }
}

public class HtmlTemplateEvaluatorTests
{
    private readonly HtmlTemplateEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_SimpleFieldSubstitution()
    {
        var row = new Dictionary<string, object?> { ["Name"] = "Alice", ["Age"] = 30m };
        var result = _evaluator.Evaluate("<p>{{Name}} is {{Age}}</p>", row, null);
        Assert.Equal("<p>Alice is 30</p>", result);
    }

    [Fact]
    public void Evaluate_ParameterSubstitution()
    {
        var parms = new Dictionary<string, object?> { ["@region"] = "West" };
        var result = _evaluator.Evaluate("<p>Region: {{@region}}</p>", null, parms);
        Assert.Equal("<p>Region: West</p>", result);
    }

    [Fact]
    public void Evaluate_HtmlEncodes_FieldValues()
    {
        var row = new Dictionary<string, object?> { ["Name"] = "<script>alert(1)</script>" };
        var result = _evaluator.Evaluate("<p>{{Name}}</p>", row, null);
        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    [Fact]
    public void Evaluate_HtmlEncodes_SpecialChars()
    {
        var row = new Dictionary<string, object?> { ["Val"] = "a&b<c>d\"e'f/g" };
        var result = _evaluator.Evaluate("{{Val}}", row, null);
        Assert.Equal("a&amp;b&lt;c&gt;d&quot;e&#x27;f&#x2F;g", result);
    }

    [Fact]
    public void Evaluate_Conditional_Equals()
    {
        var row = new Dictionary<string, object?> { ["Status"] = "Critical" };
        var template = "{{#IF Status = 'Critical'}}<b>ALERT</b>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("<b>ALERT</b>", result);
    }

    [Fact]
    public void Evaluate_Conditional_NotEquals()
    {
        var row = new Dictionary<string, object?> { ["Status"] = "OK" };
        var template = "{{#IF Status = 'Critical'}}<b>ALERT</b>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.DoesNotContain("ALERT", result);
    }

    [Fact]
    public void Evaluate_Conditional_IsNull()
    {
        var row = new Dictionary<string, object?> { ["Status"] = null };
        var template = "{{#IF Status IS NULL}}<i>N/A</i>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("<i>N/A</i>", result);
    }

    [Fact]
    public void Evaluate_Conditional_IsNotNull()
    {
        var row = new Dictionary<string, object?> { ["Status"] = "Active" };
        var template = "{{#IF Status IS NOT NULL}}<span>{{Status}}</span>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("<span>Active</span>", result);
    }

    [Fact]
    public void Evaluate_Conditional_NumericComparison()
    {
        var row = new Dictionary<string, object?> { ["Pct"] = 95m };
        var template = "{{#IF Pct >= 90}}<span>HIGH</span>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("HIGH", result);
    }

    [Fact]
    public void Evaluate_Conditional_NestedDepth4()
    {
        var row = new Dictionary<string, object?> { ["A"] = "1", ["B"] = "1", ["C"] = "1", ["D"] = "1" };
        var template = "{{#IF A = '1'}}{{#IF B = '1'}}{{#IF C = '1'}}{{#IF D = '1'}}OK{{/IF}}{{/IF}}{{/IF}}{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("OK", result);
    }

    [Fact]
    public void Evaluate_Conditional_NestedDepth5_Throws()
    {
        var row = new Dictionary<string, object?> { ["A"] = "1", ["B"] = "1", ["C"] = "1", ["D"] = "1", ["E"] = "1" };
        var template = "{{#IF A = '1'}}{{#IF B = '1'}}{{#IF C = '1'}}{{#IF D = '1'}}{{#IF E = '1'}}X{{/IF}}{{/IF}}{{/IF}}{{/IF}}{{/IF}}";
        Assert.Throws<HtmlTemplateException>(() => _evaluator.Evaluate(template, row, null));
    }

    [Fact]
    public void Evaluate_UnmatchedConditional_Throws()
    {
        var template = "{{#IF Status = 'X'}}content without closing";
        Assert.Throws<HtmlTemplateException>(() => _evaluator.Evaluate(template,
            new Dictionary<string, object?> { ["Status"] = "X" }, null));
    }

    [Fact]
    public void Evaluate_NullFieldValue_EmptyOutput()
    {
        var row = new Dictionary<string, object?> { ["Name"] = null };
        var result = _evaluator.Evaluate("<p>{{Name}}</p>", row, null);
        Assert.Equal("<p></p>", result);
    }

    [Fact]
    public void Evaluate_CaseInsensitive_FieldLookup()
    {
        var row = new Dictionary<string, object?> { ["HostName"] = "srv1" };
        var result = _evaluator.Evaluate("{{hostname}}", row, null);
        Assert.Equal("srv1", result);
    }

    [Fact]
    public void EvaluateRepeater_MultipleRows()
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Name"] = "A" },
            new Dictionary<string, object?> { ["Name"] = "B" },
            new Dictionary<string, object?> { ["Name"] = "C" },
        };
        var result = _evaluator.EvaluateRepeater("<li>{{Name}}</li>", rows, null, 500);
        Assert.Equal("<li>A</li><li>B</li><li>C</li>", result);
    }

    [Fact]
    public void EvaluateRepeater_RespectsMaxRows()
    {
        var rows = Enumerable.Range(1, 100)
            .Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["N"] = i.ToString() })
            .ToList();
        var result = _evaluator.EvaluateRepeater("{{N}} ", rows, null, 5);
        Assert.Equal(5, result.Split(" ", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void EvaluateFallback_PlainText_NoEncoding()
    {
        var row = new Dictionary<string, object?> { ["Name"] = "A<B" };
        var result = _evaluator.EvaluateFallback("Name: {{Name}}", row, null);
        Assert.Equal("Name: A<B", result);
    }

    [Fact]
    public void Evaluate_FormatSpec()
    {
        var row = new Dictionary<string, object?> { ["Pct"] = 0.142m };
        var result = _evaluator.Evaluate("{{Pct FORMAT '0.0%'}}", row, null,
            (val, fmt) => val is decimal d ? d.ToString(fmt) : val?.ToString() ?? "");
        Assert.Equal("14.2%", result);
    }
}

public class HtmlSanitizerTests
{
    private readonly HtmlSanitizer _sanitizer = new();

    // ── Element allowlist ────────────────────────────────────────────────

    [Theory]
    [InlineData("<div>safe</div>")]
    [InlineData("<span class=\"x\">ok</span>")]
    [InlineData("<article><h1>Title</h1><p>Text</p></article>")]
    [InlineData("<table><tr><td>cell</td></tr></table>")]
    [InlineData("<ul><li>item</li></ul>")]
    [InlineData("<a href=\"https://example.com\">link</a>")]
    [InlineData("<img src=\"https://example.com/img.png\" alt=\"photo\">")]
    [InlineData("<button type=\"button\">Click</button>")]
    [InlineData("<details><summary>More</summary><p>info</p></details>")]
    [InlineData("<meter min=\"0\" max=\"100\" value=\"75\"></meter>")]
    public void ValidateTemplate_AllowedElements_NoViolations(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.Empty(violations);
    }

    // ── Script injection (T-1) ───────────────────────────────────────────

    [Fact]
    public void ValidateTemplate_ScriptElement_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<script>alert(1)</script>");
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Element);
    }

    [Fact]
    public void ValidateTemplate_StyleElement_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<style>.x{color:red}</style>");
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Element);
    }

    // ── Event handler injection (T-2) ────────────────────────────────────

    [Theory]
    [InlineData("<div onclick=\"alert(1)\">x</div>")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">")]
    [InlineData("<div onmousedown=\"alert(1)\">x</div>")]
    [InlineData("<div onmouseover=\"steal()\">hover</div>")]
    public void ValidateTemplate_EventHandlers_Rejected(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Attribute);
    }

    // ── JavaScript URL injection (T-3) ───────────────────────────────────

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    [InlineData("<a href=\"vbscript:msgbox\">click</a>")]
    [InlineData("<a href=\"blob:http://evil\">click</a>")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">click</a>")]
    public void ValidateTemplate_UnsafeUrls_Rejected(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Url);
    }

    [Fact]
    public void ValidateTemplate_DataImageUrl_Allowed()
    {
        var violations = _sanitizer.ValidateTemplate("<img src=\"data:image/png;base64,abc\" alt=\"icon\">");
        Assert.Empty(violations);
    }

    // ── Iframe/embed escape (T-5) ────────────────────────────────────────

    [Theory]
    [InlineData("<iframe src=\"https://evil.com\"></iframe>")]
    [InlineData("<object data=\"evil.swf\"></object>")]
    [InlineData("<embed src=\"evil.swf\">")]
    [InlineData("<applet code=\"Evil.class\"></applet>")]
    [InlineData("<form action=\"https://evil.com\"><input></form>")]
    public void ValidateTemplate_FrameAndEmbed_Rejected(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Element);
    }

    // ── DOM mutation (T-6) ───────────────────────────────────────────────

    [Theory]
    [InlineData("<base href=\"https://evil.com\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=evil\">")]
    [InlineData("<link rel=\"stylesheet\" href=\"evil.css\">")]
    public void ValidateTemplate_DocumentMutation_Rejected(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.NotEmpty(violations);
    }

    // ── SVG script injection (T-7) ───────────────────────────────────────

    [Fact]
    public void ValidateTemplate_InlineSvg_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<svg onload=\"alert(1)\"><circle r=\"10\"/></svg>");
        Assert.NotEmpty(violations);
    }

    // ── Inline style attribute (T-11) ────────────────────────────────────

    [Fact]
    public void ValidateTemplate_InlineStyleAttribute_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<div style=\"background:url(evil)\">x</div>");
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Message.Contains("style"));
    }

    // ── Button type validation (T-13) ────────────────────────────────────

    [Fact]
    public void ValidateTemplate_ButtonTypeSubmit_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<button type=\"submit\">Go</button>");
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void ValidateTemplate_ButtonTypeButton_Allowed()
    {
        var violations = _sanitizer.ValidateTemplate("<button type=\"button\">Click</button>");
        Assert.Empty(violations);
    }

    // ── CSS sanitization ─────────────────────────────────────────────────

    [Fact]
    public void ValidateCss_SafeCss_NoViolations()
    {
        var css = ".card { padding: 1rem; color: var(--etl-text); border: 1px solid var(--etl-border); }";
        Assert.Empty(_sanitizer.ValidateCss(css));
    }

    [Theory]
    [InlineData("@import url('evil.css');")]
    [InlineData("@font-face { font-family: x; src: url('evil.woff'); }")]
    [InlineData(".x { background: expression(alert(1)); }")]
    [InlineData(".x { -moz-binding: url('evil.xml#xbl'); }")]
    [InlineData(".x { behavior: url(evil.htc); }")]
    public void ValidateCss_UnsafePatterns_Rejected(string css)
    {
        var violations = _sanitizer.ValidateCss(css);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void ValidateCss_ExternalUrl_Rejected()
    {
        var violations = _sanitizer.ValidateCss(".x { background: url(https://evil.com/img.png); }");
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void ValidateCss_NonEtlVar_Rejected()
    {
        var violations = _sanitizer.ValidateCss(".x { color: var(--portal-secret); }");
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void ValidateCss_EtlVar_Allowed()
    {
        var violations = _sanitizer.ValidateCss(".x { color: var(--etl-accent); }");
        Assert.Empty(violations);
    }

    // ── CSS scoping ──────────────────────────────────────────────────────

    [Fact]
    public void ScopeCss_PrefixesSelectors()
    {
        var css = ".card { padding: 1rem; }";
        var scoped = _sanitizer.ScopeCss(css, "etl-v-myvisual");
        Assert.Contains("#etl-v-myvisual .card", scoped);
    }

    // ── HTML encoding ────────────────────────────────────────────────────

    [Fact]
    public void HtmlEncode_AllDangerousChars()
    {
        var encoded = HtmlTemplateEvaluator.HtmlEncode("&<>\"'/");
        Assert.Equal("&amp;&lt;&gt;&quot;&#x27;&#x2F;", encoded);
    }
}
