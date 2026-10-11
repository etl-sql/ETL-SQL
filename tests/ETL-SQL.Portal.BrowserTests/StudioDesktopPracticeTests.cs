using System.Text.Json;
using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioDesktopPracticeTests(StudioAuthoringFixture fixture)
{
    private const string Exercise = """
        CREATE CONNECTION sample_data AS MOCKDB();
        SELECT UserID INTO #practice_stage FROM sample_data.Users WHERE UserID BETWEEN 1 AND 3;
        SELECT UserID, UserID * 10 AS Score INTO #practice_transform FROM #practice_stage WHERE UserID < 3;
        ASSERT (SELECT COUNT(*) FROM #practice_transform) = 3, 'Expected three users: repair the transformation filter.';
        SELECT UserID, Score INTO #practice_load FROM #practice_transform;
        ASSERT (SELECT COUNT(*) FROM #practice_load) = 3, 'Load must contain exactly three rows.';
        ASSERT (SELECT COUNT(*) FROM #practice_load WHERE UserID = 1 AND Score = 10) = 1;
        ASSERT (SELECT COUNT(*) FROM #practice_load WHERE UserID = 2 AND Score = 20) = 1;
        ASSERT (SELECT COUNT(*) FROM #practice_load WHERE UserID = 3 AND Score = 30) = 1;
        ASSERT (SELECT COUNT(*) FROM eng.columns WHERE table_name IN ('#practice_stage', '#practice_transform', '#practice_load')) > 0;
        DROP TABLE #practice_load;
        DROP TABLE #practice_transform;
        DROP TABLE #practice_stage;
        SELECT COUNT(*) AS RemainingColumns FROM eng.columns WHERE table_name IN ('#practice_stage', '#practice_transform', '#practice_load');
        """;

    [Fact]
    public async Task Desktop_FailsRepairsLoadsAndCleansPracticeData()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "practice.etlsql");
        await File.WriteAllTextAsync(file, Exercise);
        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "practice-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=practice-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)");
        await page.Locator("[data-projection='split']").ClickAsync();

        var failed = await RunAsync(page);
        Assert.False(failed.GetProperty("success").GetBoolean());
        Assert.Contains("Expected three users", failed.GetProperty("message").GetString());
        await Microsoft.Playwright.Assertions.Expect(page.GetByText(
            "Expected three users: repair the transformation filter.", new() { Exact = false }).First).ToBeVisibleAsync();

        // A new run must not inherit the staging table left by the failed ASSERT.
        await ReplaceScriptAsync(page,
            "SELECT COUNT(*) AS RemainingColumns FROM eng.columns WHERE table_name IN ('#practice_stage', '#practice_transform', '#practice_load');");
        var isolated = await RunAsync(page);
        Assert.True(isolated.GetProperty("success").GetBoolean(), isolated.ToString());
        Assert.Equal(0m, Assert.Single(isolated.GetProperty("rows").EnumerateArray())
            .GetProperty("RemainingColumns").GetDecimal());

        var repaired = Exercise.Replace("WHERE UserID < 3", "WHERE UserID <= 3", StringComparison.Ordinal);
        await ReplaceScriptAsync(page, repaired);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await RunAsync(page);
            Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
            var row = Assert.Single(result.GetProperty("rows").EnumerateArray());
            Assert.Equal(0m, row.GetProperty("RemainingColumns").GetDecimal());
        }

        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");
        Assert.Equal(repaired.Replace("\r\n", "\n"), (await File.ReadAllTextAsync(file)).Replace("\r\n", "\n"));
        Assert.Empty(session.PageErrors);
    }

    private static async Task ReplaceScriptAsync(IPage page, string script)
    {
        var editor = page.Locator(".cm-content").First;
        await editor.ClickAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.InsertTextAsync(script);
    }

    private static async Task<JsonElement> RunAsync(IPage page)
    {
        var responseTask = page.WaitForResponseAsync(response =>
            response.Url.EndsWith("/api/designer/run", StringComparison.Ordinal) && response.Request.Method == "POST");
        await page.Locator("[data-action='run']").ClickAsync();
        var response = await responseTask;
        Assert.True(response.Ok, await response.TextAsync());
        var result = await response.JsonAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].studioContext.runActive === false");
        return result!.Value;
    }
}
