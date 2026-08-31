using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Reproducible Studio browser measurements on the canonical UI-sandbox fixture. The checked-in
/// ceilings are regression budgets, not product-speed claims; CI executes this same test on
/// Windows, Linux, and macOS and publishes the raw measurement document for every runner.
/// </summary>
[Trait("Category", "Browser")]
[Trait("Category", "StudioPerformance")]
[Collection(DetailSurfaceCollection.Name)]
public sealed class StudioPerformanceBudgetTests(
    DetailSurfaceHarnessFixture fixture,
    ITestOutputHelper output)
{
    private const string BudgetPath = "docs/benchmarks/studio-performance-budgets.json";
    private const string StudioStory = "/tools/ui-sandbox/index.html#story=studio&fixture=default&sidebar=collapsed";

    [Fact]
    public async Task CanonicalStudioFixture_StaysWithinPlatformBudgets()
    {
        var platform = CurrentPlatform();
        var budget = LoadBudget(platform);
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        var startup = Stopwatch.StartNew();
        await page.GotoAsync(StudioStory);
        await page.WaitForFunctionAsync(
            "() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)",
            null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        startup.Stop();

        var heapMiB = await MeasureHeapMiBAsync(page);
        var keystrokeP95Ms = await MeasureKeystrokeP95Async(page);
        var browserMeasures = await page.EvaluateAsync<JsonElement>(
            """
            async () => {
              const percentile95 = values => {
                const ordered = [...values].sort((left, right) => left - right);
                return ordered[Math.max(0, Math.ceil(ordered.length * 0.95) - 1)];
              };

              const { renderVisualSample } = await import(
                '/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/visual-preview.js?studio-perf=1');
              const host = document.createElement('div');
              host.style.cssText = 'position:fixed;left:-10000px;width:900px;height:500px';
              document.body.appendChild(host);
              const rows = Array.from({ length: 250 }, (_, index) => ({
                category: `Category ${index % 25}`,
                amount: (index % 17) + 0.25,
                series: `Series ${index % 4}`
              }));
              const visual = {
                type: 'BAR',
                mappings: { X: 'category', Y: 'amount', SERIES: 'series' }
              };
              const sample = {
                columns: ['category', 'amount', 'series'],
                rows
              };

              for (let index = 0; index < 8; index++) renderVisualSample(host, visual, sample);
              const aggregationSamples = [];
              for (let index = 0; index < 40; index++) {
                const started = performance.now();
                renderVisualSample(host, visual, sample);
                void host.offsetHeight;
                aggregationSamples.push(performance.now() - started);
              }
              host.remove();

              const designer = window.__STUDIO_INSTANCE__.state.designerInstance;
              const canvas = document.querySelector('.etlsql-dsgn-grid');
              for (let index = 0; index < 5; index++) designer.refreshSnapshot();
              const redrawSamples = [];
              for (let index = 0; index < 30; index++) {
                await new Promise(resolve => requestAnimationFrame(resolve));
                const started = performance.now();
                designer.refreshSnapshot();
                void canvas.offsetHeight;
                redrawSamples.push(performance.now() - started);
              }

              return {
                aggregationP95Ms: percentile95(aggregationSamples),
                canvasRedrawP95Ms: percentile95(redrawSamples)
              };
            }
            """);

        var result = new StudioPerformanceResult(
            SchemaVersion: 1,
            Platform: platform,
            Runner: $"{Environment.OSVersion}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            MeasuredAtUtc: DateTimeOffset.UtcNow,
            StartupMs: startup.Elapsed.TotalMilliseconds,
            JsHeapMiB: heapMiB,
            KeystrokeP95Ms: keystrokeP95Ms,
            AggregationP95Ms: browserMeasures.GetProperty("aggregationP95Ms").GetDouble(),
            CanvasRedrawP95Ms: browserMeasures.GetProperty("canvasRedrawP95Ms").GetDouble(),
            Budget: budget);

        WriteResult(result);
        Record("startup", result.StartupMs, budget.StartupMs, "ms");
        Record("post-GC JavaScript heap", result.JsHeapMiB, budget.JsHeapMiB, "MiB");
        Record("keystroke input-to-frame p95", result.KeystrokeP95Ms, budget.KeystrokeP95Ms, "ms");
        Record("250-row aggregate/render p95", result.AggregationP95Ms, budget.AggregationP95Ms, "ms");
        Record("canvas redraw/layout p95", result.CanvasRedrawP95Ms, budget.CanvasRedrawP95Ms, "ms");

        AssertWithin("startup", result.StartupMs, budget.StartupMs, "ms");
        AssertWithin("post-GC JavaScript heap", result.JsHeapMiB, budget.JsHeapMiB, "MiB");
        AssertWithin("keystroke input-to-frame p95", result.KeystrokeP95Ms, budget.KeystrokeP95Ms, "ms");
        AssertWithin("250-row aggregate/render p95", result.AggregationP95Ms, budget.AggregationP95Ms, "ms");
        AssertWithin("canvas redraw/layout p95", result.CanvasRedrawP95Ms, budget.CanvasRedrawP95Ms, "ms");
        Assert.Empty(session.PageErrors);
    }

    private static async Task<double> MeasureHeapMiBAsync(IPage page)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        try
        {
            await cdp.SendAsync("HeapProfiler.collectGarbage");
            var response = await cdp.SendAsync("Runtime.getHeapUsage")
                ?? throw new InvalidOperationException("Chromium did not return heap usage.");
            var heapBytes = response.GetProperty("usedSize").GetDouble();
            return heapBytes / 1024d / 1024d;
        }
        finally
        {
            await cdp.DetachAsync();
        }
    }

    private static async Task<double> MeasureKeystrokeP95Async(IPage page)
    {
        var editor = page.Locator(".cm-content").First;
        await editor.ClickAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+End");
        var samples = new List<double>();
        for (var index = 0; index < 24; index++)
        {
            await page.EvaluateAsync(
                """
                () => {
                  window.__studioKeystrokeMeasurement = new Promise(resolve => {
                    const editor = document.querySelector('.cm-content');
                    editor.addEventListener('beforeinput', event => {
                      requestAnimationFrame(() => resolve(performance.now() - event.timeStamp));
                    }, { once: true });
                  });
                }
                """);
            await page.Keyboard.TypeAsync(index % 2 == 0 ? " " : "x");
            samples.Add(await page.EvaluateAsync<double>("() => window.__studioKeystrokeMeasurement"));
        }

        samples.Sort();
        return samples[Math.Max(0, (int)Math.Ceiling(samples.Count * 0.95) - 1)];
    }

    private static string CurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "macos";
        throw new PlatformNotSupportedException("Studio performance budgets exist for Windows, Linux, and macOS.");
    }

    private static StudioPerformanceBudget LoadBudget(string platform)
    {
        var path = Path.Combine(DetailSurfaceHarnessFixture.RepoRoot(), BudgetPath.Replace('/', Path.DirectorySeparatorChar));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var profile = document.RootElement.GetProperty("profiles").GetProperty(platform);
        return new StudioPerformanceBudget(
            profile.GetProperty("startupMs").GetDouble(),
            profile.GetProperty("jsHeapMiB").GetDouble(),
            profile.GetProperty("keystrokeP95Ms").GetDouble(),
            profile.GetProperty("aggregationP95Ms").GetDouble(),
            profile.GetProperty("canvasRedrawP95Ms").GetDouble());
    }

    private void WriteResult(StudioPerformanceResult result)
    {
        var configuredPath = Environment.GetEnvironmentVariable("ETLSQL_STUDIO_PERF_OUTPUT");
        if (string.IsNullOrWhiteSpace(configuredPath)) return;
        var path = Path.GetFullPath(configuredPath, DetailSurfaceHarnessFixture.RepoRoot());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"studio-performance evidence: {path}");
    }

    private void Record(string name, double actual, double budget, string unit) =>
        output.WriteLine($"studio-performance {name}: {actual:0.00} {unit} (budget {budget:0.00} {unit})");

    private static void AssertWithin(string name, double actual, double budget, string unit) =>
        Assert.True(actual <= budget, // flaky-time-bound-ok: checked-in cross-platform performance budget
            $"Studio {name} measured {actual:0.00} {unit}; budget is {budget:0.00} {unit}.");

    private sealed record StudioPerformanceBudget(
        double StartupMs,
        double JsHeapMiB,
        double KeystrokeP95Ms,
        double AggregationP95Ms,
        double CanvasRedrawP95Ms);

    private sealed record StudioPerformanceResult(
        int SchemaVersion,
        string Platform,
        string Runner,
        DateTimeOffset MeasuredAtUtc,
        double StartupMs,
        double JsHeapMiB,
        double KeystrokeP95Ms,
        double AggregationP95Ms,
        double CanvasRedrawP95Ms,
        StudioPerformanceBudget Budget);
}
