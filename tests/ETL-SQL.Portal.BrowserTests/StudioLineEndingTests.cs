using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// A file keeps the line endings it was written with.
///
/// <para>CodeMirror normalises every buffer it holds to a bare LF. That is the editor's business and
/// nobody's problem until the buffer is written back to disk: a CRLF file opened in Studio and saved
/// came back entirely LF, so the first save rewrote every line of a file the author had changed one
/// line of. The diff that produces is the whole file — in Studio's own Git view, which is the thing
/// the author would open next to check what they had changed.</para>
///
/// <para>Asserted on the bytes on disk, because that is the only place the claim means anything. The
/// desktop host is used because it is the host that owns a file: the Portal stores a script through
/// the catalog, where the same rule holds but the artifact is a row rather than a path.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioLineEndingTests(PortalBrowserFixture fixture)
{
    private static readonly string[] ScriptLines =
    [
        "-- the author's own preparation",
        "SELECT Region, Amount INTO #sales FROM sample_data.Users;",
        "",
        "CREATE VISUAL SalesBar AS BAR (",
        "    SOURCE = #sales,",
        "    MAPPINGS (X = Region, Y = Amount)",
        ");",
        "",
        "CREATE PAGE [Main] AS DASHBOARD (",
        "    LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesBar))",
        ");",
    ];

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public async Task Saving_KeepsTheLineEndingsTheFileWasWrittenWith(string lineEnding)
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "endings.rptsql");
        await File.WriteAllTextAsync(file, string.Join(lineEnding, ScriptLines));

        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "endings-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=endings-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });

        // One appended line. The point is what happens to the ten lines that were not touched.
        await page.EvaluateAsync(
            "() => window.__STUDIO__.state.editorInstance.setValue("
            + "window.__STUDIO__.state.editorInstance.getValue() + '\\n-- one appended line\\n')");
        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");

        var saved = await File.ReadAllTextAsync(file);
        Assert.Contains("-- one appended line", saved, StringComparison.Ordinal);

        var crlf = CountCrLf(saved);
        var bareLf = CountBareLf(saved);
        if (lineEnding == "\r\n")
        {
            Assert.True(bareLf == 0,
                $"Saving rewrote {bareLf} of a CRLF file's line endings to LF, so every line of the "
                + $"file reads as changed. CRLF endings left: {crlf}.");
            Assert.True(crlf > 0, "The saved file has no line endings at all.");
        }
        else
        {
            Assert.True(crlf == 0,
                $"Saving introduced {crlf} CRLF endings into an LF file, so every line of the file "
                + "reads as changed.");
        }

        Assert.Empty(session.PageErrors);
    }

    private static int CountCrLf(string text)
    {
        var count = 0;
        for (var index = 0; index + 1 < text.Length; index++)
            if (text[index] == '\r' && text[index + 1] == '\n') count++;
        return count;
    }

    private static int CountBareLf(string text)
    {
        var count = 0;
        for (var index = 0; index < text.Length; index++)
            if (text[index] == '\n' && (index == 0 || text[index - 1] != '\r')) count++;
        return count;
    }
}
