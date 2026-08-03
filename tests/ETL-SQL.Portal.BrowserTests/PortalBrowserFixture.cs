using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Owns the two expensive, reusable pieces of the browser lane: a Portal listening on a real
/// loopback port (<see cref="PortalBrowserFactory"/>) and one Chromium instance. Each test takes a
/// fresh <see cref="BrowserSession"/> — its own browser context, so storage and cookies never leak
/// between journeys — while the browser process itself is shared.
///
/// Chromium is downloaded on demand the first time the lane runs (~150 MB, cached under the
/// Playwright browsers directory). Set <c>ETLSQL_PLAYWRIGHT_SKIP_INSTALL=1</c> where the browsers
/// are provisioned separately, e.g. a CI job with a restored browser cache.
/// </summary>
public sealed class PortalBrowserFixture : IAsyncLifetime
{
    private IPlaywright? playwright;
    private IBrowser? browser;

    public PortalBrowserFactory Factory { get; } = new();

    /// <summary>Base URL of the Portal under test, without a trailing slash.</summary>
    public string BaseUrl => Factory.ServerAddress.TrimEnd('/');

    public async Task InitializeAsync()
    {
        // Resolving a client is what builds and starts the host, which is what assigns the port.
        Factory.CreateClient().Dispose();

        if (Environment.GetEnvironmentVariable("ETLSQL_PLAYWRIGHT_SKIP_INSTALL") != "1")
        {
            var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Playwright could not install Chromium (exit code {exitCode}). Install it manually with "
                    + "'pwsh tests/ETL-SQL.Portal.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium'.");
            }
        }

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>Opens an isolated browser context at desktop width and starts recording JS failures.</summary>
    public async Task<BrowserSession> NewSessionAsync()
    {
        var context = await (browser ?? throw new InvalidOperationException("Fixture is not initialized."))
            .NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                BaseURL = BaseUrl
            });
        return await BrowserSession.CreateAsync(context);
    }

    public async Task DisposeAsync()
    {
        if (browser is not null) await browser.CloseAsync();
        playwright?.Dispose();
        Factory.Dispose();
    }
}

/// <summary>
/// One browser context plus its page, recording unhandled JavaScript exceptions. A journey that
/// "passes" while the page threw is not a passing journey, so <see cref="PageErrors"/> is asserted
/// empty at the end of each test.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private readonly IBrowserContext context;
    private readonly List<string> pageErrors = [];

    private BrowserSession(IBrowserContext context, IPage page)
    {
        this.context = context;
        Page = page;
        page.PageError += (_, error) => pageErrors.Add(error);
    }

    public IPage Page { get; }

    /// <summary>Unhandled JavaScript exceptions raised by the page so far.</summary>
    public IReadOnlyList<string> PageErrors => pageErrors;

    internal static async Task<BrowserSession> CreateAsync(IBrowserContext context)
        => new(context, await context.NewPageAsync());

    public async ValueTask DisposeAsync() => await context.DisposeAsync();
}
