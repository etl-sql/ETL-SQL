using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>Owns Chromium for sandbox checks without starting a Portal or opening a catalog database.</summary>
public sealed class SandboxStoryFixture : IAsyncLifetime
{
    private IPlaywright? playwright;
    private IBrowser? browser;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("ETLSQL_PLAYWRIGHT_SKIP_INSTALL") != "1")
        {
            var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exitCode != 0)
                throw new InvalidOperationException($"Playwright could not install Chromium (exit code {exitCode}).");
        }
        playwright = await Playwright.CreateAsync();
        try { browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }); }
        catch { playwright.Dispose(); throw; }
    }

    public async Task<BrowserSession> NewSessionAsync()
    {
        var context = await (browser ?? throw new InvalidOperationException("Sandbox fixture is not initialized."))
            .NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
            });
        return await BrowserSession.CreateAsync(context);
    }

    public async Task DisposeAsync()
    {
        if (browser is not null) await browser.CloseAsync();
        playwright?.Dispose();
    }
}
