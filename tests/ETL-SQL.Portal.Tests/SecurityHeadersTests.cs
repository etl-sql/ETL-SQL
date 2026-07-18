using System.Net;
using System.Text.RegularExpressions;
using ETL_SQL.Portal.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public class SecurityHeadersTests(PortalWebFactory factory) : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task ApiResponse_HasRestrictiveSecurityHeaders()
    {
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self' 'nonce-", csp);
        Assert.Contains("script-src-attr 'none'", csp);
        Assert.Contains("frame-ancestors 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("SAMEORIGIN", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("camera=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")));
    }

    [Fact]
    public async Task HtmlResponse_UsesCspNonceOnEveryScriptAndStyle()
    {
        var response = await client.GetAsync("/index.html");
        var html = await response.Content.ReadAsStringAsync();
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        var nonce = Regex.Match(csp, @"script-src 'self' 'nonce-([^']+)'").Groups[1].Value;

        Assert.False(string.IsNullOrWhiteSpace(nonce));
        Assert.DoesNotContain("<script type=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" onclick=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" onchange=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<script nonce=\"{nonce}\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            $"<script nonce=\"{nonce}\" src=\"/js/report-runtime.js",
            html,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HtmlResponses_UseDifferentNonces()
    {
        var first = await client.GetAsync("/login.html");
        var second = await client.GetAsync("/login.html");

        var firstCsp = Assert.Single(first.Headers.GetValues("Content-Security-Policy"));
        var secondCsp = Assert.Single(second.Headers.GetValues("Content-Security-Policy"));

        Assert.NotEqual(firstCsp, secondCsp);
    }

    [Fact]
    public async Task ExternalFrameAncestor_IsExactAndOmitsLegacyFrameHeader()
    {
        var config = new PortalConfig
        {
            Security = new PortalSecurityConfig
            {
                FrameAncestors = ["https://analytics.example.com/"]
            }
        };
        var middleware = new SecurityHeadersMiddleware(
            _ => Task.CompletedTask,
            config,
            NullLogger<SecurityHeadersMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context);

        Assert.Contains(
            "frame-ancestors 'self' https://analytics.example.com",
            context.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.False(context.Response.Headers.ContainsKey("X-Frame-Options"));
    }
}
