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

    /// <summary>
    /// Signs in as the seeded administrator, performing the forced first-run password change on the
    /// first call only.
    ///
    /// <para>The whole lane shares one Portal, so the change happens once for the lane and the
    /// original password stops working afterwards. Owning that here rather than in each test class
    /// is what keeps classes independent of the order they run in — three classes each tracking
    /// their own password against one shared account is a race that only surfaces in whichever
    /// class happens to run second.</para>
    /// </summary>
    public async Task SignInAsync(IPage page)
    {
        await signInGate.WaitAsync();
        try
        {
            await page.GotoAsync("/login.html");
            await page.FillAsync("#username", AdminUsername);
            await page.FillAsync("#password", passwordChanged ? AdminPassword : FirstRunPassword);
            await page.ClickAsync("#loginBtn");

            if (!passwordChanged)
            {
                // Waiting on the selector rather than polling visibility: the form is rendered by
                // the login script after the response lands, so an immediate check races it.
                await page.WaitForSelectorAsync("#changeForm", new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 20_000
                });
                await page.FillAsync("#currentPwd", FirstRunPassword);
                await page.FillAsync("#newPwd", AdminPassword);
                await page.FillAsync("#confirmPwd", AdminPassword);
                await page.ClickAsync("#changeBtn");
                passwordChanged = true;
            }

            await page.WaitForURLAsync("**/index.html", new PageWaitForURLOptions { Timeout = 20_000 });
        }
        finally
        {
            signInGate.Release();
        }
    }

    /// <summary>The seeded first-run administrator, before its forced password change.</summary>
    public const string AdminUsername = "admin";
    public const string FirstRunPassword = "Admin@12345!";

    /// <summary>The password the lane uses after the forced change.</summary>
    public const string AdminPassword = "Portal@Lane99!";

    private readonly SemaphoreSlim signInGate = new(1, 1);
    private bool passwordChanged;

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
        // Async, so the Kestrel host is fully stopped before the temp directory is deleted and
        // before the next fixture in this process tries to bind and open the same files.
        await Factory.DisposeAsync();
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

    private readonly List<string> consoleErrors = [];
    private readonly List<string> failedRequests = [];

    private BrowserSession(IBrowserContext context, IPage page)
    {
        this.context = context;
        Page = page;
        page.PageError += (_, error) => pageErrors.Add(error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error") consoleErrors.Add(message.Text);
        };
        page.Response += (_, response) =>
        {
            // The browser's own console error for a failed request says only "the server responded
            // with 403" — no URL. Recording the request alongside it is the difference between a
            // finding someone can act on and one they have to reproduce by hand to understand.
            if (response.Status >= 400)
                failedRequests.Add($"{response.Status} {response.Request.Method} {response.Url}");
        };
    }

    public IPage Page { get; }

    /// <summary>Unhandled JavaScript exceptions raised by the page so far.</summary>
    public IReadOnlyList<string> PageErrors => pageErrors;

    /// <summary>
    /// <c>console.error</c> output, including the browser's own reports of failed requests and
    /// unhandled promise rejections.
    ///
    /// <para>Separate from <see cref="PageErrors"/> because the two catch different failures. A
    /// thrown exception stops a code path; a console error usually does not — which is exactly why
    /// it survives review. A page that logs an error on every load has a broken path nobody has
    /// been forced to look at.</para>
    /// </summary>
    public IReadOnlyList<string> ConsoleErrors => consoleErrors;

    /// <summary>Requests that came back 4xx or 5xx, with method and URL.</summary>
    public IReadOnlyList<string> FailedRequests => failedRequests;

    internal static async Task<BrowserSession> CreateAsync(IBrowserContext context)
        => new(context, await context.NewPageAsync());

    public async ValueTask DisposeAsync() => await context.DisposeAsync();
}
