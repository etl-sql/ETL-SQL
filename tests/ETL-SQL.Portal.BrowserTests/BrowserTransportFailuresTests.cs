using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(DetailSurfaceCollection.Name)]
public sealed class BrowserTransportFailuresTests(DetailSurfaceHarnessFixture fixture)
{
    [Theory]
    [InlineData("https://user:password@example.test:8443/path?token=secret#fragment", "https://example.test:8443/path")]
    [InlineData("http://example.test/path?q=secret", "http://example.test/path")]
    [InlineData("data:text/plain,secret", "[non-http-url]")]
    [InlineData("file:///private/secret.txt", "[non-http-url]")]
    [InlineData("relative?secret", "[invalid-url]")]
    public void UrlSanitization_RemovesCredentialsQueryAndFragment(string input, string expected)
        => Assert.Equal(expected, BrowserTransportFailures.SanitizeUrl(input));

    [Fact]
    public async Task AbortedNavigation_IsRecordedWithoutAnHttpFailureResponse()
    {
        await using var session = await fixture.NewSessionAsync();
        using var failures = new BrowserTransportFailures(session.Page.Context);
        await session.Page.RouteAsync("https://example.invalid/**", route => route.AbortAsync("connectionrefused"));

        await Assert.ThrowsAsync<PlaywrightException>(() => session.Page.GotoWithDiagnosticsAsync(
            "https://example.invalid/navigation?token=must-not-survive#private-fragment"));

        var history = failures.Snapshot();
        var request = Assert.Single(history.Requests);
        Assert.True(history.NavigationFailed);
        Assert.True(request.IsNavigation);
        Assert.Equal("GET", request.Method);
        Assert.Equal("document", request.ResourceType);
        Assert.Equal("https://example.invalid/navigation", request.Url);
        Assert.Equal("net::ERR_CONNECTION_REFUSED", request.FailureCode);
        Assert.Equal(0, history.Dropped);
        Assert.Empty(session.FailedRequests);
    }

    [Fact]
    public async Task ContextRecorder_CoversAnAdditionalPage()
    {
        await using var session = await fixture.NewSessionAsync();
        using var failures = new BrowserTransportFailures(session.Page.Context);
        var extraPage = await session.Page.Context.NewPageAsync();
        await extraPage.RouteAsync("https://example.invalid/**", route => route.AbortAsync("connectionreset"));

        await Assert.ThrowsAsync<PlaywrightException>(() => extraPage.GotoWithDiagnosticsAsync("https://example.invalid/extra"));

        var request = Assert.Single(failures.Snapshot().Requests);
        Assert.Equal("https://example.invalid/extra", request.Url);
        Assert.Equal("net::ERR_CONNECTION_RESET", request.FailureCode);
    }

    [Fact]
    public async Task BoundedHistory_RetainsLatestFailuresAndRemembersEvictedNavigationFailure()
    {
        await using var session = await fixture.NewSessionAsync();
        using var failures = new BrowserTransportFailures(session.Page.Context);
        for (var index = 0; index < 101; index++)
            failures.Record("GET", "fetch", index == 0, $"https://example.invalid/{index}?secret=value",
                "net::ERR_FAILED https://user:password@example.invalid/?secret=value");

        var history = failures.Snapshot();
        Assert.Equal(100, history.Requests.Count);
        Assert.Equal(1, history.Dropped);
        Assert.True(history.NavigationFailed);
        Assert.Equal("https://example.invalid/1", history.Requests[0].Url);
        Assert.Equal("https://example.invalid/100", history.Requests[^1].Url);
        Assert.All(history.Requests, request => Assert.Equal("net::ERR_FAILED", request.FailureCode));
        Assert.DoesNotContain(history.Requests, request => request.IsNavigation);
    }

    [Fact]
    public async Task RecordedText_IsBoundedBeforeWritingArtifacts()
    {
        await using var session = await fixture.NewSessionAsync();
        using var failures = new BrowserTransportFailures(session.Page.Context);
        failures.Record(new string('M', 1000), new string('R', 1000), false,
            "https://example.invalid/" + new string('p', 4000) + "?secret=value", "net::ERR_FAILED");
        var request = Assert.Single(failures.Snapshot().Requests);
        Assert.Equal(24, request.Method.Length);
        Assert.Equal(32, request.ResourceType.Length);
        Assert.Equal(2048, request.Url.Length);
        Assert.DoesNotContain("secret", request.Url);
    }

    [Fact]
    public async Task UnrecognizedProviderMessage_IsNotRetained()
    {
        await using var session = await fixture.NewSessionAsync();
        using var failures = new BrowserTransportFailures(session.Page.Context);
        failures.Record("GET", "fetch", false, "https://example.invalid/", "private-password-and-token");

        Assert.Equal("unavailable", Assert.Single(failures.Snapshot().Requests).FailureCode);
    }
}
