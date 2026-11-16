using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// EXPORT REPORT ... WITH (BROWSER_PATH, HOST) comes from a script, so it must not be able to start
/// an arbitrary binary or point the headless browser at a local file or a fenced host.
/// </summary>
public class ExportReportPdfOptionSecurityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "etl-export-opt-" + Guid.NewGuid().ToString("N"));

    public ExportReportPdfOptionSecurityTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "r.rptsql"), "SELECT 1 AS A INTO #T;");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<Exception> RunAsync(string options)
    {
        var report = Path.Combine(_dir, "r.rptsql").Replace("'", "''");
        var output = Path.Combine(_dir, "r.pdf").Replace("'", "''");
        var sql = $"EXPORT REPORT '{report}' FORMAT PDF TO '{output}' WITH ({options});";
        await using var provider = DashboardTestHelper.BuildScopeProvider();
        var evaluator = provider.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        return await Record.ExceptionAsync(() => evaluator.Evaluate(script));
    }

    [Fact]
    public async Task BrowserPathToAnUnrecognisedFileIsRefusedBeforeAnyProcessStarts()
    {
        // A script-writable file given a browser's name: the old code launched it.
        var planted = Path.Combine(_dir, OperatingSystem.IsWindows() ? "chrome.exe" : "chrome");
        File.WriteAllText(planted, "not a browser");

        var error = await RunAsync($"PDF_MODE = BROWSER, HOST = 'http://localhost:5080', BROWSER_PATH = '{planted.Replace("'", "''")}'");

        Assert.NotNull(error);
        Assert.Contains("BROWSER_PATH", error!.ToString());
        Assert.False(File.Exists(Path.Combine(_dir, "r.pdf")));
    }

    [Fact]
    public void ADiscoveredBrowserIsAcceptedAndAnyOtherPathIsNot()
    {
        foreach (var discovered in BrowserReportPdfExporter.DiscoverInstalledBrowsers())
            Assert.True(BrowserReportPdfExporter.IsInstalledBrowser(discovered));

        Assert.False(BrowserReportPdfExporter.IsInstalledBrowser(Path.Combine(_dir, "chrome.exe")));
        Assert.False(BrowserReportPdfExporter.IsInstalledBrowser(""));
    }

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("chrome://settings")]
    [InlineData("not a url")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task HostMustBeAnAllowedHttpUrl(string host)
    {
        var error = await RunAsync($"PDF_MODE = BROWSER, HOST = '{host}'");

        Assert.NotNull(error);
        Assert.Contains("HOST", error!.ToString());
        Assert.False(File.Exists(Path.Combine(_dir, "r.pdf")));
    }

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("about:blank")]
    public async Task ExporterRefusesNonHttpNavigationForEveryCaller(string host)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BrowserReportPdfExporter().ExportAsync(new ReportManifest(), new PdfExportOptions { Mode = PdfExportMode.Browser, Host = host }));

        Assert.Contains("http", error.Message);
    }
}
