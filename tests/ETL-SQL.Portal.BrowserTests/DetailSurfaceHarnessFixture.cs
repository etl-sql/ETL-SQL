using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Chromium plus a static file server rooted at the repository, and nothing else.
///
/// <para>The detail-surface tests exercise the canonical <c>report-runtime.js</c> against a
/// hand-built manifest. They need a browser and a way to serve that file — not a Portal, a
/// catalog database, or a signed-in user. Depending on <see cref="PortalBrowserFixture"/>
/// would couple every placement assertion to Portal startup, so failures in an unrelated
/// subsystem would present as broken tooltip geometry.</para>
/// </summary>
public sealed class DetailSurfaceHarnessFixture : IAsyncLifetime
{
    private IPlaywright? playwright;
    private IBrowser? browser;
    private IHost? host;

    /// <summary>Base URL of the static server, without a trailing slash.</summary>
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var root = RepoRoot();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            ServeUnknownFileTypes = true,
        });

        host = app;
        await app.StartAsync();
        BaseUrl = app.Urls.First().TrimEnd('/');

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

    /// <summary>Opens an isolated context at desktop width, recording JavaScript failures.</summary>
    public async Task<BrowserSession> NewSessionAsync(int width = 1440, int height = 900)
    {
        var context = await (browser ?? throw new InvalidOperationException("Fixture is not initialized."))
            .NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                BaseURL = BaseUrl
            });
        return await BrowserSession.CreateAsync(context);
    }

    public async Task DisposeAsync()
    {
        if (browser is not null) await browser.CloseAsync();
        playwright?.Dispose();
        if (host is not null)
        {
            await host.StopAsync(TimeSpan.FromSeconds(10));
            host.Dispose();
        }
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

[CollectionDefinition(DetailSurfaceCollection.Name)]
public sealed class DetailSurfaceCollection : ICollectionFixture<DetailSurfaceHarnessFixture>
{
    public const string Name = "detail-surface-harness";
}
