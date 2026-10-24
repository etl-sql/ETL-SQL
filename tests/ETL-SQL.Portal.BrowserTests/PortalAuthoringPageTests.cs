using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(StudioSurfaceCollection.Name)]
public sealed class PortalAuthoringPageTests(StudioSurfaceFixture fixture)
{
    private const string Script = """
        -- Original author comment must survive opening and saving in Designer.
        SELECT  'North'  AS Region, 120 AS Revenue INTO #data;
        CREATE VISUAL Revenue AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Revenue));
        CREATE PAGE Main AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Revenue)));
        """;

    [Fact]
    public async Task Designer_CodeAndSave_PreserveTheOriginalScriptAfterTokenRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var id = await CreateReportAsync(page);
        await page.GotoWithDiagnosticsAsync($"/designer.html?id={id}");
        await Expect(page.Locator("#dsgn-save")).ToBeEnabledAsync();
        await Expect(page.Locator(".etlsql-feedback-toast", new() { HasText = "No snapshot available" })).ToHaveCountAsync(0);
        await page.EvaluateAsync("() => sessionStorage.setItem('etlsql_token', 'expired.access.token')");
        var generating = page.WaitForRequestAsync(request => request.Url.EndsWith("/api/designer/generate", StringComparison.Ordinal));
        await page.Locator("#dsgn-code-mode").ClickAsync();
        var generate = await generating;
        using var body = JsonDocument.Parse(generate.PostData!);
        Assert.Equal(Script, body.RootElement.GetProperty("script").GetString());
        await Expect(page.Locator(".cm-content")).ToContainTextAsync("Original author comment");
        Assert.NotEqual("expired.access.token", await page.EvaluateAsync<string>("() => sessionStorage.getItem('etlsql_token')"));
        await page.Locator(".etlsql-designer-script-overlay [data-close]").ClickAsync();
        var saving = page.WaitForResponseAsync(response => response.Url.EndsWith("/api/designer/save", StringComparison.Ordinal));
        await page.Locator("#dsgn-save").ClickAsync();
        Assert.Equal(200, (await saving).Status);
        await page.WaitForURLWithDiagnosticsAsync("**/studio.html");
        var saved = await page.EvaluateAsync<string>("""
            async id => {
              const { apiFetch } = await import('/js/api.js');
              return (await (await apiFetch(`/api/reports/${id}/script-content`)).json()).scriptText;
            }
            """, id);
        Assert.Contains("Original author comment", saved, StringComparison.Ordinal);
        Assert.Contains("SELECT  'North'  AS Region", saved, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Designer_UsesColumnsFromARealCompiledSnapshot()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var id = await CreateReportAsync(page);
        var jobId = await page.EvaluateAsync<string>("""
            async id => {
              const { reportsApi } = await import('/js/api.js');
              return (await reportsApi.execute(id, {})).jobId;
            }
            """, id);
        await ETL_SQL.TestSupport.LoadAwareWait.UntilAsync(
            "the compiled report job to complete",
            cancellationToken => page.EvaluateAsync<JsonElement>("""
                async id => {
                  const { reportsApi } = await import('/js/api.js');
                  return reportsApi.pollJob(id);
                }
                """, jobId).WaitAsync(cancellationToken),
            job => job.GetProperty("status").GetString() switch
            {
                "Completed" => true,
                "Failed" or "Cancelled" => throw new InvalidOperationException(job.ToString()),
                _ => false
            }, TimeSpan.FromSeconds(30), describe: job => job.ToString());
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var stored = await db.ReportSnapshots.Where(snapshot => snapshot.ReportId == id).ToListAsync();
            Assert.Single(stored);
            var config = scope.ServiceProvider.GetRequiredService<ETL_SQL.Portal.PortalConfig>();
            var key = ETL_SQL.Portal.Services.PortalPathGuard.ToSnapshotKey(config, stored[0].ManifestPath);
            Assert.NotNull(key);
            var packages = scope.ServiceProvider.GetRequiredService<ETL_SQL.Portal.Services.SnapshotPackageService>();
            var manifest = await packages.LoadAsync(key, keyScope: "portal-host");
            Assert.NotNull(manifest);
            Assert.NotEmpty(manifest.Visuals);
            Assert.True(manifest.Visuals.Any(visual => visual.Rows.Count > 0),
                JsonSerializer.Serialize(manifest.Visuals.Select(visual => new { visual.Name, visual.VisualType, visual.Columns, visual.Rows, visual.NativeSvg })));
        }
        var snapshotResponse = page.WaitForResponseAsync(response => response.Url.EndsWith($"/api/designer/snapshot/{id}", StringComparison.Ordinal));
        await page.GotoWithDiagnosticsAsync($"/designer.html?id={id}");
        var snapshot = await snapshotResponse;
        Assert.True(snapshot.Status == 200, $"Designer snapshot returned {snapshot.Status}: {await snapshot.TextAsync()}");
        using var payload = JsonDocument.Parse(await snapshot.TextAsync());
        Assert.Equal("Region", payload.RootElement.GetProperty("columns").GetProperty("Revenue")[0].GetString());
        await Expect(page.Locator("#designerHost svg").First).ToBeVisibleAsync();
        await Expect(page.Locator("#designerHost")).ToContainTextAsync("North");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("script-content")]
    [InlineData("parse")]
    public async Task Designer_MalformedLoadingResponse_OffersRetryWithoutAnEmptyEditor(string endpoint)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var id = await CreateReportAsync(page);
        var pattern = endpoint == "parse" ? "**/api/designer/parse" : $"**/api/reports/{id}/script-content";
        await page.RouteAsync(pattern, route => route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" }));
        await page.GotoWithDiagnosticsAsync($"/designer.html?id={id}");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Invalid");
        await Expect(page.Locator("#dsgn-save")).ToHaveCountAsync(0);
        await page.UnrouteAsync(pattern);
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry Connection" }).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#dsgn-save")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("catalog")]
    public async Task Studio_MalformedCatalogResponse_OffersRetryWithoutMountingTheWorkbench(string endpoint)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        // Startup reads the first bounded catalog page, /api/studio/catalog?query=&afterId=0.
        var pattern = endpoint == "catalog" ? "**/api/studio/catalog?*" : $"**/api/studio/{endpoint}";
        await page.RouteAsync(pattern, route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = endpoint == "catalog" ? """{"items":[{}],"nextAfterId":null}""" : "{}"
        }));
        await page.GotoWithDiagnosticsAsync("/studio.html");
        await Expect(page.Locator("#studioHost [role=alert]")).ToContainTextAsync("Invalid Studio");
        Assert.False(await page.EvaluateAsync<bool>("() => Boolean(window.__STUDIO__)"));
        await page.UnrouteAsync(pattern);
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry Connection" }).ClickAsync();
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)");
        Assert.Empty(session.PageErrors);
    }

    private async Task<int> CreateReportAsync(IPage page)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var owner = await db.Users.Where(user => user.UserName == PortalBrowserFixture.AdminUsername)
            .Select(user => user.Id).SingleAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder { Name = $"Authoring {suffix}", Path = $"/Authoring-{suffix}", OwnerId = owner };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return await page.EvaluateAsync<int>("""
            async request => {
              const { studioApi } = await import('/js/api.js');
              return (await studioApi.createReport(request)).id;
            }
            """, new { folderId = folder.Id, name = $"Authoring {suffix}", scriptText = Script });
    }
}
