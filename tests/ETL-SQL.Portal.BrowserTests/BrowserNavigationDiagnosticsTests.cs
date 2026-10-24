using System.Text.Json;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(DetailSurfaceCollection.Name)]
public sealed class BrowserNavigationDiagnosticsTests(DetailSurfaceHarnessFixture fixture)
{
    [Fact]
    public async Task DesktopShutdownTimeout_RecordsTransportHistoryAndPreservesException()
    {
        await using var session = await fixture.NewSessionAsync();
        await using var context = await session.Page.Context.Browser!.NewContextAsync();
        var directory = EvidenceDirectory();
        var original = new TimeoutException("Injected application-stopping timeout.");
        await using (var diagnostics = new BrowserNavigationDiagnostics(context, directory))
        {
            var page = await context.NewPageAsync();
            await page.RouteAsync("https://example.invalid/**", route => route.FulfillAsync(new()
            { Status = 200, ContentType = "text/html", Body = "<title>Desktop host</title>" }));
            await page.GotoWithDiagnosticsAsync("https://example.invalid/studio?token=must-not-survive");
            await page.RouteAsync("**/api/studio/heartbeat", route => route.AbortAsync("connectionrefused"));
            Assert.True(await page.EvaluateAsync<bool>("async () => { try { await fetch('/api/studio/heartbeat'); return false; } catch { return true; } }"));
            var observed = await Assert.ThrowsAsync<TimeoutException>(() => DesktopStudioJourneyTests.WaitForStoppingAsync(
                Task.FromException(original), page));
            Assert.Same(original, observed);
        }
        var snapshot = Assert.Single(ReadSnapshots(directory));
        Assert.Equal("desktop-shutdown-failure", snapshot.GetProperty("trigger").GetString());
        Assert.Equal(nameof(DesktopShutdownTimeout_RecordsTransportHistoryAndPreservesException), snapshot.GetProperty("caller").GetString());
        Assert.Equal("https://example.invalid/api/studio/shutdown", snapshot.GetProperty("targetUrl").GetString());
        var request = Assert.Single(snapshot.GetProperty("requests").GetProperty("Requests").EnumerateArray());
        Assert.Equal("https://example.invalid/api/studio/heartbeat", request.GetProperty("Url").GetString());
        Assert.Equal("net::ERR_CONNECTION_REFUSED", request.GetProperty("FailureCode").GetString());
        Assert.NotEmpty(snapshot.GetProperty("processes").EnumerateArray());
        Assert.DoesNotContain("must-not-survive", snapshot.GetRawText());
    }

    [Fact]
    public async Task AbortedNavigation_RetainsSanitizedRequestAndOwnProcessResources()
    {
        await using var session = await fixture.NewSessionAsync();
        await using var context = await session.Page.Context.Browser!.NewContextAsync();
        var directory = EvidenceDirectory();
        await using (var diagnostics = new BrowserNavigationDiagnostics(context, directory))
        {
            var page = await context.NewPageAsync();
            await page.RouteAsync("https://example.invalid/**", route => route.AbortAsync("connectionrefused"));
            var exception = await Assert.ThrowsAsync<PlaywrightException>(() => page.GotoWithDiagnosticsAsync(
                "https://example.invalid/navigation?token=must-not-survive#private-fragment"));
            Assert.Contains("net::ERR_CONNECTION_REFUSED", exception.Message);
        }

        var snapshots = ReadSnapshots(directory);
        Assert.Contains(snapshots, item => item.GetProperty("trigger").GetString() == "transport-navigation");
        var failure = Assert.Single(snapshots, item => item.GetProperty("trigger").GetString() == "goto-failure");
        Assert.Equal(nameof(AbortedNavigation_RetainsSanitizedRequestAndOwnProcessResources), failure.GetProperty("caller").GetString());
        var request = Assert.Single(failure.GetProperty("requests").GetProperty("Requests").EnumerateArray());
        Assert.Equal("https://example.invalid/navigation", request.GetProperty("Url").GetString());
        Assert.Equal("net::ERR_CONNECTION_REFUSED", request.GetProperty("FailureCode").GetString());
        var processes = failure.GetProperty("processes").EnumerateArray().ToArray();
        Assert.Contains(processes, process => process.GetProperty("Role").GetString() == "test-host"
            && process.GetProperty("ProcessId").GetInt32() == Environment.ProcessId);
        Assert.Contains(processes, process => process.GetProperty("Role").GetString() == "browser");
        if (OperatingSystem.IsWindows())
        {
            foreach (var process in processes)
            {
                if (process.GetProperty("HandleCount").ValueKind == JsonValueKind.Null)
                    Assert.NotEmpty(process.GetProperty("Unavailable").EnumerateArray());
                else
                    Assert.True(process.GetProperty("HandleCount").GetInt32() > 0);
            }
            var host = Assert.Single(processes, process => process.GetProperty("Role").GetString() == "test-host");
            Assert.Equal(JsonValueKind.Number, host.GetProperty("TcpV4Endpoints").ValueKind);
        }
        Assert.DoesNotContain("must-not-survive", JsonSerializer.Serialize(snapshots));
        Assert.DoesNotContain("private-fragment", JsonSerializer.Serialize(snapshots));
    }

    [Fact]
    public async Task UrlWaitTimeout_RecordsResourcesWithoutTransportFailure()
    {
        await using var session = await fixture.NewSessionAsync();
        await using var context = await session.Page.Context.Browser!.NewContextAsync();
        var directory = EvidenceDirectory();
        await using (var diagnostics = new BrowserNavigationDiagnostics(context, directory))
        {
            var page = await context.NewPageAsync();
            // flaky-wait-budget-ok: deliberately time out a URL that this page never navigates to.
            var exception = await Assert.ThrowsAsync<TimeoutException>(() => page.WaitForURLWithDiagnosticsAsync(
                "https://example.invalid/never", new() { Timeout = 100 }));
            Assert.Contains("Timeout 100ms exceeded", exception.Message);
        }

        var snapshot = Assert.Single(ReadSnapshots(directory));
        Assert.Equal("url-wait-failure", snapshot.GetProperty("trigger").GetString());
        Assert.Empty(snapshot.GetProperty("requests").GetProperty("Requests").EnumerateArray());
        Assert.NotEmpty(snapshot.GetProperty("processes").EnumerateArray());
    }

    [Fact]
    public async Task NavigationTimeout_RecordsSnapshotAndPreservesTimeoutException()
    {
        await using var session = await fixture.NewSessionAsync();
        await using var context = await session.Page.Context.Browser!.NewContextAsync();
        var directory = EvidenceDirectory();
        await using (var diagnostics = new BrowserNavigationDiagnostics(context, directory))
        {
            var page = await context.NewPageAsync();
            await page.RouteAsync("https://example.invalid/**", _ => Task.CompletedTask);
            // flaky-wait-budget-ok: the intercepted navigation is deliberately never completed.
            var exception = await Assert.ThrowsAsync<TimeoutException>(() => page.GotoWithDiagnosticsAsync(
                "https://example.invalid/never?private-token=value", new() { Timeout = 100 }));
            Assert.Contains("Timeout 100ms exceeded", exception.Message);
        }
        var snapshot = Assert.Single(ReadSnapshots(directory));
        Assert.Equal("goto-failure", snapshot.GetProperty("trigger").GetString());
        Assert.Equal("https://example.invalid/never", snapshot.GetProperty("targetUrl").GetString());
        Assert.NotEmpty(snapshot.GetProperty("processes").EnumerateArray());
        Assert.DoesNotContain("private-token", snapshot.GetRawText());
    }

    [Fact]
    public async Task SuccessfulNavigation_PreservesResponseAndDoesNotWriteDiagnostics()
    {
        await using var session = await fixture.NewSessionAsync();
        await using var context = await session.Page.Context.Browser!.NewContextAsync();
        var directory = EvidenceDirectory();
        await using (var diagnostics = new BrowserNavigationDiagnostics(context, directory))
        {
            var page = await context.NewPageAsync();
            await page.RouteAsync("https://example.invalid/**", route => route.FulfillAsync(new()
            { Status = 201, ContentType = "text/html", Body = "<title>navigation succeeded</title>" }));
            var response = await page.GotoWithDiagnosticsAsync("https://example.invalid/created", new() { WaitUntil = WaitUntilState.Load });
            Assert.Equal(201, response!.Status);
            Assert.Equal("navigation succeeded", await page.TitleAsync());
        }
        Assert.Empty(ReadSnapshots(directory));
    }

    [Fact]
    public async Task UnresponsiveProcessLookup_IsBoundedAndStillWritesTestHostEvidence()
    {
        await using var session = await fixture.NewSessionAsync();
        await using var context = await session.Page.Context.Browser!.NewContextAsync();
        var directory = EvidenceDirectory();
        var never = new TaskCompletionSource<IReadOnlyList<BrowserProcessIdentity>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var diagnostics = new BrowserNavigationDiagnostics(context, directory, () => never.Task))
            await diagnostics.CaptureAsync("unresponsive-lookup", "https://example.invalid/");
        var snapshot = Assert.Single(ReadSnapshots(directory));
        Assert.Contains("browser-processes: TimeoutException",
            snapshot.GetProperty("unavailable").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(Environment.ProcessId, Assert.Single(snapshot.GetProperty("processes").EnumerateArray())
            .GetProperty("ProcessId").GetInt32());
    }

    [Fact]
    public async Task ProcessLookupFailure_RetainsTestHostAndUnavailableReason()
    {
        await using var session = await fixture.NewSessionAsync();
        await using var context = await session.Page.Context.Browser!.NewContextAsync();
        var directory = EvidenceDirectory();
        await using (var diagnostics = new BrowserNavigationDiagnostics(context, directory,
            () => throw new InvalidOperationException("private-provider-message")))
        {
            await diagnostics.CaptureAsync("lookup-failure", "https://user:password@example.invalid/path?secret=value");
        }
        var snapshot = Assert.Single(ReadSnapshots(directory));
        Assert.Contains("browser-processes: InvalidOperationException",
            snapshot.GetProperty("unavailable").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(Environment.ProcessId, Assert.Single(snapshot.GetProperty("processes").EnumerateArray())
            .GetProperty("ProcessId").GetInt32());
        Assert.DoesNotContain("private-provider-message", snapshot.GetRawText());
        Assert.DoesNotContain("password", snapshot.GetRawText());
        Assert.DoesNotContain("secret", snapshot.GetRawText());
    }

    [Fact]
    public async Task UnwritableEvidence_DoesNotReplaceNavigationException()
    {
        await using var session = await fixture.NewSessionAsync();
        await using var context = await session.Page.Context.Browser!.NewContextAsync();
        var path = Path.Combine(EvidenceDirectory(), "regular-file");
        await File.WriteAllTextAsync(path, "Cannot create a directory here.");
        await using var diagnostics = new BrowserNavigationDiagnostics(context, path);
        var page = await context.NewPageAsync();
        await page.RouteAsync("https://example.invalid/**", route => route.AbortAsync("connectionreset"));
        var exception = await Assert.ThrowsAsync<PlaywrightException>(() => page.GotoWithDiagnosticsAsync("https://example.invalid/"));
        Assert.Contains("net::ERR_CONNECTION_RESET", exception.Message);
    }

    [Fact]
    public async Task SnapshotLimit_PreservesFirstFailuresWithoutUnboundedFiles()
    {
        await using var session = await fixture.NewSessionAsync();
        await using var context = await session.Page.Context.Browser!.NewContextAsync();
        var directory = EvidenceDirectory();
        await using (var diagnostics = new BrowserNavigationDiagnostics(context, directory,
            () => Task.FromResult<IReadOnlyList<BrowserProcessIdentity>>([new(Environment.ProcessId, "test-host")])))
        {
            for (var index = 0; index < 12; index++)
                await diagnostics.CaptureAsync("limit-probe", $"https://example.invalid/{index}");
        }
        Assert.Equal(8, ReadSnapshots(directory).Length);
    }

    private static string EvidenceDirectory()
    {
        var root = Environment.GetEnvironmentVariable("ETLSQL_BROWSER_DIAGNOSTICS_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        var directory = Path.Combine(root, "diagnostic-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static JsonElement[] ReadSnapshots(string directory)
        => Directory.GetFiles(directory, "*.json").Select(path =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }).ToArray();
}
