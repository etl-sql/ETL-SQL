using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Server-rendered chart and micro-chart SVG is imported into report pages that do not all carry
/// the Portal's CSP (exported HTML, the desktop Player). The runtime's own parsing must therefore
/// leave nothing that can run script, and must never write text that failed to parse as markup.
///
/// <para>Asserted on the resulting DOM rather than on whether a handler fired: the Portal's CSP
/// already blocks inline handlers, so a "nothing ran" check would pass here regardless.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(AdminUsersCollection.Name)]
public sealed class RuntimeSvgSanitizationTests(AdminUsersFixture fixture)
{
    private const string HostileSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" onload="window.__svgRan=1">
          <script>window.__svgRan=2</script>
          <rect id="bar" width="10" height="10" fill="#336699" onclick="window.__svgRan=3"/>
          <a href="javascript:window.__svgRan=4"><text>label</text></a>
          <a xlink:href="https://example.invalid/"><text>external</text></a>
          <use href="#bar"/>
          <foreignObject><div xmlns="http://www.w3.org/1999/xhtml"><img src="x" onerror="window.__svgRan=5"/></div></foreignObject>
          <set attributeName="href" to="javascript:window.__svgRan=6"/>
          <animate attributeName="width" to="20"/>
        </svg>
        """;

    [Fact]
    public async Task ChartSvg_KeepsItsShapesAndLosesEverythingThatCouldRunScript()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync("/login.html");

        var result = await page.EvaluateAsync<string[]>("""
            async svg => {
                const { renderSafeSvgInto } = await import('/js/rt-util.js');
                const target = document.createElement('div');
                document.body.appendChild(target);
                if (!renderSafeSvgInto(target, svg)) return ['not rendered'];
                const problems = [];
                for (const element of target.querySelectorAll('*')) {
                    const name = element.localName.toLowerCase();
                    if (['script', 'foreignobject', 'set', 'animate'].includes(name)) problems.push('element ' + name);
                    for (const attribute of element.attributes) {
                        const attr = attribute.name.toLowerCase();
                        if (attr.startsWith('on')) problems.push('handler ' + attr + ' on ' + name);
                        if ((attr === 'href' || attr.endsWith(':href')) && !attribute.value.startsWith('#'))
                            problems.push('link ' + attribute.value);
                    }
                }
                if (!target.querySelector('rect#bar')) problems.push('shape removed');
                if (target.querySelector('use')?.getAttribute('href') !== '#bar') problems.push('internal reference removed');
                if (target.querySelectorAll('text').length !== 2) problems.push('text removed');
                return problems;
            }
            """, HostileSvg);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("<img src=x onerror=\"window.__svgRan=7\">")]
    [InlineData("<div><svg onload=\"window.__svgRan=8\"></svg></div>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY x \"y\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect></svg>")]
    public async Task TextThatIsNotSvg_IsNeverWrittenAsMarkup(string payload)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync("/login.html");

        var rendered = await page.EvaluateAsync<string>("""
            async payload => {
                const { renderSafeSvgInto } = await import('/js/rt-util.js');
                const target = document.createElement('div');
                const ok = renderSafeSvgInto(target, payload);
                return ok + ':' + target.innerHTML;
            }
            """, payload);

        Assert.Equal("false:", rendered);
    }
}
