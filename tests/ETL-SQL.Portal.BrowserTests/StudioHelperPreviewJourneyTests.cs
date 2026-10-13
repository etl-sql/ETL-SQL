using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Trait("CompatBreak", "0.21.0")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioHelperPreviewJourneyTests(StudioAuthoringFixture fixture) : IDisposable
{
    public void Dispose() => SqliteConnection.ClearAllPools();
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSqlitePreviewRunsOnlyItsQueryAndAcceptsAnEmptyResult(bool desktop)
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(page, workspace, desktop);
        var path = Path.Combine(desktop ? workspace.Root : fixture.Factory.TempDir, "native-" + Guid.NewGuid().ToString("N") + ".db");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Users (Id INTEGER PRIMARY KEY); INSERT INTO Users VALUES (1), (2);";
            await command.ExecuteNonQueryAsync();
        }
        var target = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
        var alias = "native_" + Guid.NewGuid().ToString("N");
        if (!desktop)
        {
            var status = await page.EvaluateAsync<int>("""
                async data => {
                    const {auth} = await import('/js/api.js');
                    const r = await fetch('/api/admin/connections/' + data.alias, {method:'PUT',
                        headers: {Authorization: 'Bearer ' + auth.getToken(), 'Content-Type':'application/json'},
                        body: JSON.stringify({connectorType:'SQLITE', target:data.target, options:{}})});
                    return r.status;
                }
                """, new { alias, target });
            Assert.InRange(status, 200, 299);
        }
        var declaration = $"CREATE CONNECTION {alias} AS SQLITE('{(desktop ? target : "SHARED:" + alias).Replace("'", "''")}');";
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, declaration + "\nWAITFOR DELAY '00:00:40';\nSELECT 99;");
        await MountAsync(page, "SELECT sqlite_version() AS Version, Id FROM Users ORDER BY Id", alias, "remote");
        var result = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/designer/run", StringComparison.Ordinal));
        await page.Locator("[data-workbench-run]").ClickAsync();
        var payload = await (await result).JsonAsync();
        Assert.True(!payload!.Value.TryGetProperty("success", out var succeeded) || succeeded.GetBoolean(), payload.ToString());
        Assert.Equal(2, payload.Value.GetProperty("rows").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(payload.Value.GetProperty("rows")[0].GetProperty("Version").GetString()));
        await Expect(page.Locator("[data-workbench-output]")).ToContainTextAsync("2 rows sampled");
        var submitted = (await result).Request.PostData!;
        Assert.Contains("EXECUTE", submitted);
        Assert.DoesNotContain("WAITFOR", submitted);
        Assert.DoesNotContain("SELECT 99", submitted);

        await MountAsync(page, "SELECT sqlite_version() AS Version FROM Users WHERE Id = 99", alias, "remote");
        await page.Locator("[data-workbench-run]").ClickAsync();
        await Expect(page.Locator("[data-workbench-output]")).ToContainTextAsync("0 rows");

        var joins = string.Join(" ", Enumerable.Range(1, 11).Select(index => $"CROSS JOIN Users u{index}"));
        await MountAsync(page, "SELECT a.Id FROM Users a " + joins, alias, "remote");
        var cappedResponse = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/designer/run", StringComparison.Ordinal));
        await page.Locator("[data-workbench-run]").ClickAsync();
        var capped = await (await cappedResponse).JsonAsync();
        Assert.InRange(capped!.Value.GetProperty("rows").GetArrayLength(), 1, desktop ? 100 : 1000);

        if (desktop)
        {
            await MountAsync(page, "SELECT hex(zeroblob(600000)) AS Payload FROM Users WHERE Id = 1", alias, "remote");
            var largeResponse = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/designer/run", StringComparison.Ordinal));
            await page.Locator("[data-workbench-run]").ClickAsync();
            var large = await (await largeResponse).JsonAsync();
            Assert.True(large!.Value.GetProperty("success").GetBoolean());
            Assert.True(large.Value.GetProperty("capped").GetBoolean());
            Assert.Equal(0, large.Value.GetProperty("rows").GetArrayLength());
            await Expect(page.Locator("[data-workbench-output]")).ToContainTextAsync("1,048,576 bytes");
            await Expect(page.Locator("[data-workbench-output]")).Not.ToContainTextAsync("returned 0 rows");

            await MountAsync(page, "SELECT Id, hex(zeroblob(300000)) AS Payload FROM Users ORDER BY Id", alias, "remote");
            var combinedResponse = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/designer/run", StringComparison.Ordinal));
            await page.Locator("[data-workbench-run]").ClickAsync();
            var combined = await (await combinedResponse).JsonAsync();
            Assert.True(combined!.Value.GetProperty("capped").GetBoolean());
            var included = combined.Value.GetProperty("rows");
            Assert.Equal(1, included.GetArrayLength());
            Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(included.GetRawText()), 1, 1024 * 1024);
            await Expect(page.Locator("[data-workbench-output]")).ToContainTextAsync("1 row sampled");
            await Expect(page.Locator("[data-workbench-output]")).ToContainTextAsync("1,048,576 bytes");
        }

        await MountAsync(page, "DELETE FROM Users", alias, "remote");
        await page.Locator("[data-workbench-run]").ClickAsync();
        await Expect(page.Locator("[data-workbench-output]")).ToContainTextAsync("read-only SELECT");
        await using var verify = new SqliteConnection(target);
        await verify.OpenAsync();
        await using var count = verify.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Users";
        Assert.Equal(2L, await count.ExecuteScalarAsync());
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnginePreviewUsesExplicitVariableAndStageContextAndRefusesUnavailablePredecessors(bool desktop)
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(page, workspace, desktop);
        const string parent = "DECLARE @minimum INT = 2;\nSELECT 2 AS Id INTO #stage;\nSELECT 99 AS Later;";
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, parent);
        await MountAsync(page, "SELECT Id FROM #stage WHERE Id >= @minimum", null, "engine");
        await page.Locator("[data-preview-context]").CheckAsync();
        await page.Locator("[data-preview-context-line]").FillAsync("3");
        var response = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/designer/run", StringComparison.Ordinal));
        await page.Locator("[data-workbench-run]").ClickAsync();
        var result = await (await response).JsonAsync();
        Assert.True(!result!.Value.TryGetProperty("success", out var succeeded) || succeeded.GetBoolean(), result.ToString());
        Assert.Equal(2m, result.Value.GetProperty("rows")[0].GetProperty("Id").GetDecimal());
        await Expect(page.Locator("[data-workbench-output]")).ToContainTextAsync("1 row sampled");

        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, "WAITFOR DELAY '00:00:40';\n" + parent);
        await page.Locator("[data-preview-context-line]").FillAsync("4");
        await page.Locator("[data-workbench-run]").ClickAsync();
        await Expect(page.Locator("[data-workbench-output]")).ToContainTextAsync("Preview context is unavailable");
        await Expect(page.Locator("[data-workbench-output]")).ToContainTextAsync("Nothing was executed");

        await page.Locator("[data-preview-context]").UncheckAsync();
        await page.Locator("[data-workbench-run]").ClickAsync();
        await Expect(page.Locator("[data-workbench-output]")).Not.ToContainTextAsync("Running…");
        await Expect(page.Locator("[data-workbench-output]")).Not.ToContainTextAsync("1 row sampled");
        Assert.Empty(session.PageErrors);
    }

    private static Task MountAsync(IPage page, string query, string? connection, string context) => page.EvaluateAsync("""
        async data => {
            window.previewWorkbench?.dispose();
            document.querySelector('#section7-helper')?.remove();
            const host = document.createElement('section'); host.id = 'section7-helper'; document.body.appendChild(host);
            const {createQueryWorkbench} = await import('/designer/studio-query-workbench.js');
            const {STUDIO_ROUTES} = await import('/designer/studio-contracts.js');
            const portal = location.pathname.includes('studio.html');
            const fetcher = async (url, init) => {
                if (portal) { const {auth} = await import('/js/api.js'); init.headers = {...init.headers, Authorization: 'Bearer ' + auth.getToken()}; }
                else { init.headers = {...init.headers, 'X-ETLSQL-EDITOR-TOKEN': 'scope-token'}; }
                return fetch(url, init);
            };
            window.previewWorkbench = await createQueryWorkbench(host, {
                value:data.query, connection:data.connection, context:data.context, routes:STUDIO_ROUTES,
                scriptText:() => window.__STUDIO__.state.editorInstance.getValue(),
                request: async (route, options) => {
                    const r = await fetcher(route, {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(options.body)});
                    const result = await r.json(); if (!r.ok) throw new Error(result.error); return result;
                }, editorTransport: {url:route => route, authFetch:fetcher}
            });
        }
        """, new { query, connection, context });
}
