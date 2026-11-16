using System;
using System.Linq;
using System.Text;
using ETL_SQL.Reporting.Semantics;
using Xunit;

namespace ETL_SQL.Tests.Reporting.HtmlVisual;

/// <summary>
/// Sanitizer bypasses reported by the 2026-11-01 review: a quoted '>' ending the tag early,
/// unchecked srcset candidates after the first, and SVG data: URLs that hide script behind XML
/// entities or SMIL animation.
/// </summary>
public class ConstrainedHtmlPolicyBypassTests
{
    [Fact]
    public void QuotedGreaterThanDoesNotHideAnEventHandler()
    {
        var violations = ConstrainedHtmlPolicy.ValidateTemplate(
            "<img alt=\"a > b\" src=\"https://ok.example/x.png\" onerror=\"alert(1)\">");

        Assert.Contains(violations, v => v.Category == "Attribute" && v.Message.Contains("onerror"));
    }

    [Fact]
    public void QuotedGreaterThanInASafeAttributeStillPasses()
    {
        var violations = ConstrainedHtmlPolicy.ValidateTemplate(
            "<img alt=\"a > b\" src=\"https://ok.example/x.png\"><span title='1 > 0'>ok</span>");

        Assert.Empty(violations);
    }

    [Fact]
    public void UnterminatedQuoteIsMalformed()
    {
        var violations = ConstrainedHtmlPolicy.ValidateTemplate("<img alt=\"x onerror=alert(1)>");

        Assert.NotEmpty(violations);
    }

    [Theory]
    [InlineData("https://ok.example/a.png 1x, //evil.example/b.png 2x")]
    [InlineData("https://ok.example/a.png 1x, javascript:alert(1) 2x")]
    [InlineData("https://ok.example/a.png 1x,data:text/html,x 2x")]
    [InlineData("https://ok.example/a.png, relative.png 2x")]
    public void EverySrcsetCandidateIsChecked(string srcset)
    {
        var violations = ConstrainedHtmlPolicy.ValidateTemplate($"<picture><source srcset=\"{srcset}\"></picture>");

        Assert.Contains(violations, v => v.Category == "Url");
    }

    [Theory]
    [InlineData("https://ok.example/a.png")]
    [InlineData("https://ok.example/a.png 1x, https://ok.example/b.png 2x")]
    [InlineData("https://ok.example/a.png 480w,https://ok.example/b.png 800w")]
    public void SafeSrcsetPasses(string srcset)
    {
        var violations = ConstrainedHtmlPolicy.ValidateTemplate($"<picture><source srcset=\"{srcset}\"></picture>");

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><a href='&#x6a;avascript:alert(1)'><text>x</text></a></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><a href='java&#x09;script:alert(1)'><text>x</text></a></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><a><set attributeName='href' to='javascript:alert(1)'/><text>x</text></a></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><a><animate attributeName='href' values='javascript:alert(1)'/></a></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    public void SvgDataUrlHidingScriptIsRejected(string svg)
    {
        var plain = "data:image/svg+xml," + Uri.EscapeDataString(svg);
        var base64 = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

        Assert.Contains(ConstrainedHtmlPolicy.ValidateTemplate($"<img alt=\"x\" src=\"{plain}\">"), v => v.Category == "Url");
        Assert.Contains(ConstrainedHtmlPolicy.ValidateTemplate($"<img alt=\"x\" src=\"{base64}\">"), v => v.Category == "Url");
    }

    [Fact]
    public void PlainSvgDataUrlPasses()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><rect width='4' height='4' fill='#0a0'/></svg>";
        var url = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

        Assert.Empty(ConstrainedHtmlPolicy.ValidateTemplate($"<img alt=\"x\" src=\"{url}\">"));
    }
}
