using System.Diagnostics;
using System.Text.Json;
using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Trait("Category", "StudioPerformance")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioDeliveryJourneyTests(StudioAuthoringFixture fixture, ITestOutputHelper output)
{
    private const string ColdDocument = "SELECT 1 AS A INTO #t; CREATE VISUAL T AS TABLE (SOURCE = #t); CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = T));";
    internal static string LargeDocument => string.Join('\n', Enumerable.Range(1, 5_000)
        .Select(index => $"-- Studio fixture line {index:D4}: abcdefgh")) + "\nSELECT 1 AS Value;";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeDocument_RecordsEditingAndRealAnalysis(bool desktop)
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(session.Page, workspace, desktop);
        var page = session.Page;
        var script = LargeDocument;
        Assert.Equal(190_018, script.Length);
        Assert.Equal(5_001, script.Split('\n').Length);
        var analyzedResponse = page.WaitForResponseAsync(response => response.Url.Contains("/api/designer/analyze", StringComparison.Ordinal)
            && response.Request.PostData?.Contains("Studio fixture line 0001", StringComparison.Ordinal) == true);
        var editingMs = await page.EvaluateAsync<double>("text => { const start = performance.now(); window.__STUDIO__.state.editorInstance.replaceAll(text); return performance.now() - start; }", script);
        var analysis = Stopwatch.StartNew();
        await page.EvaluateAsync("() => window.__STUDIO__.state.editorInstance.analyze()");
        Assert.Equal(200, (await analyzedResponse).Status);
        analysis.Stop();
        Assert.Equal(script, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
        var result = new
        {
            host = desktop ? "desktop" : "portal",
            lines = 5_001,
            characters = script.Length,
            utf8Bytes = System.Text.Encoding.UTF8.GetByteCount(script),
            editingMs,
            analysisMs = analysis.Elapsed.TotalMilliseconds,
            workload = "5000 numbered ASCII comments plus one SELECT; linear text/token workload, not a 5000-statement AST",
            environment = $"{Environment.OSVersion}; Chromium {page.Context.Browser!.Version}"
        };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        var file = Path.Combine(DetailSurfaceHarnessFixture.RepoRoot(), "artifacts", "studio-performance", $"{result.host}-large-document.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, json);
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionHost_UncachedEditorStaysWithinColdLoadBudget(bool desktop)
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = desktop ? await OpenMatchingDesktopAsync(session.Page, workspace)
            : await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(session.Page, workspace, false);
        var page = session.Page;
        var cdp = await page.Context.NewCDPSessionAsync(page);
        var samples = new List<double>();
        try
        {
            await cdp.SendAsync("Network.enable");
            await cdp.SendAsync("Network.setCacheDisabled", new Dictionary<string, object> { ["cacheDisabled"] = true });
            for (var index = 0; index < 3; index++)
            {
                await cdp.SendAsync("Network.clearBrowserCache");
                var started = Stopwatch.StartNew();
                await page.ReloadAsync();
                await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__?.state.editorInstance)", null,
                    new PageWaitForFunctionOptions { Timeout = 20_000 });
                started.Stop();
                Assert.Equal(ColdDocument, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
                samples.Add(started.Elapsed.TotalMilliseconds);
            }
        }
        finally
        {
            await cdp.DetachAsync();
        }

        var hostName = desktop ? "desktop" : "portal";
        var result = new
        {
            host = hostName,
            fixture = "Scope: matching one-table dashboard script in a catalog report or local .rptsql file; existing authenticated identity",
            cache = "Chromium Network.setCacheDisabled plus clearBrowserCache before every document reload",
            server = "already running in-process Kestrel; no server/process startup measured",
            environment = $"{Environment.OSVersion}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; Chromium {page.Context.Browser!.Version}",
            measuredAtUtc = DateTimeOffset.UtcNow,
            firstUsableEditorMs = samples
        };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        var evidence = Path.Combine(DetailSurfaceHarnessFixture.RepoRoot(), "artifacts", "studio-performance", $"{hostName}-cold-load.json");
        Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
        await File.WriteAllTextAsync(evidence, json);
        var ceiling = desktop ? 2_000 : 3_000;
        Assert.All(samples, sample => Assert.True(sample <= ceiling, // flaky-time-bound-ok: declared production cold-load regression budget
            $"{hostName} uncached editor measured {sample:0.0} ms; ceiling {ceiling} ms."));
        Assert.Empty(session.PageErrors);
    }

    private static async Task<Microsoft.AspNetCore.Builder.WebApplication> OpenMatchingDesktopAsync(IPage page, StudioTempWorkspace workspace)
    {
        var file = Path.Combine(workspace.Root, "scope.rptsql");
        await File.WriteAllTextAsync(file, ColdDocument);
        var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(workspace.Root, file, 0, false, "scope-token", StudioMode: true));
        await host.StartAsync();
        await page.GotoWithDiagnosticsAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=scope-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__?.state.editorInstance)");
        return host;
    }
}
