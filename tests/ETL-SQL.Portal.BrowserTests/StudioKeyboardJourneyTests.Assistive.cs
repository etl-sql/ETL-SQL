using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The groundwork for ST28 and ST29: the keyboard document journey again, this time recording what
/// assistive technology is given at each step.
///
/// <para>These tests hold three things: every control the keyboard stops on has a role and a name,
/// a refused action says why in an alert tied to the field it is about, and a change the author
/// cannot see land — a save — is announced. They do not replace a screen reader pass; they are what
/// makes one worth running, and their transcript is what that pass is checked against.</para>
/// </summary>
public sealed partial class StudioKeyboardJourneyTests
{
    private const string DocumentOpened = "() => document.activeElement?.matches('.etlsql-studio-tab.active, [data-code-stage] .cm-content') === true";

    [Fact]
    public async Task Portal_AssistiveJourney_EveryStopIsNamedAndChangesAreAnnounced()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        var authorId = await SignInAsPublisherAsync(page);
        await CreateWritableFolderAsync(authorId);

        await page.GotoAsync("/studio.html");
        await WaitForStudioAsync(session);
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");
        var heard = await AssistiveTranscript.StartAsync(page);

        // ── Create, with one refusal on the way ────────────────────────────────
        var createCard = page.Locator("[data-create-from-home='dashboard'].workflow-dashboard");
        await createCard.WaitForAsync(new() { Timeout = 10_000 });
        await createCard.FocusAsync();
        await heard.RecordAsync("Focus the new dashboard card on Home");

        await page.Keyboard.PressAsync("Enter");
        var nameInput = page.Locator("[data-catalog-report-name]");
        await Expect(nameInput).ToBeFocusedAsync();
        var opened = await heard.RecordAsync("Enter: the create dialog opens");
        Assert.Equal("Create catalog report", opened.Dialog);

        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.PressAsync("Backspace");
        await KeyboardWalk.TabToAsync(page, "[data-catalog-create-confirm]", maxPresses: 6);
        await heard.RecordAsync("Clear the name, Tab to Create");
        await page.Keyboard.PressAsync("Enter");
        await heard.WaitForAnnouncementAsync("Enter a name for the report.");
        await Expect(nameInput).ToBeFocusedAsync();
        var refused = await heard.RecordAsync("Enter: Create is refused");
        Assert.Contains("invalid", refused.Detail, StringComparison.Ordinal);
        Assert.Contains("Enter a name for the report.", refused.Detail, StringComparison.Ordinal);

        var reportName = $"Heard_Report_{Guid.NewGuid():N}"[..22];
        await page.Keyboard.TypeAsync(reportName);
        await Expect(nameInput).Not.ToHaveAttributeAsync("aria-invalid", "true");
        await page.Keyboard.PressAsync("Tab");
        await heard.RecordAsync("Type a name, Tab to the folder");
        await page.Keyboard.PressAsync("Tab");
        await heard.RecordAsync("Tab to Cancel");
        await page.Keyboard.PressAsync("Tab");
        await heard.RecordAsync("Tab to Create");
        await page.Keyboard.PressAsync("Enter");

        // ── The open document: its tab, the view toggles, the editor ───────────
        await page.WaitForFunctionAsync(DocumentOpened, null, new() { Timeout = 20_000 });
        var tab = await heard.RecordAsync("Enter: the document opens");
        Assert.Equal("tab", tab.Role);
        Assert.Contains(reportName, tab.Name, StringComparison.Ordinal);
        Assert.Contains("selected", tab.Detail, StringComparison.Ordinal);

        await page.Keyboard.PressAsync("ArrowLeft");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");
        await heard.RecordAsync("Left arrow: the Home tab");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId !== '__home__'");
        await page.WaitForFunctionAsync(DocumentOpened, null, new() { Timeout = 20_000 });
        await heard.RecordAsync("Right arrow: back to the document tab");

        await KeyboardWalk.TabToAsync(page, "[data-projection='code']", maxPresses: 12);
        var toggle = await heard.RecordAsync("Tab to the Code view toggle");
        Assert.DoesNotContain("pressed", toggle.Detail, StringComparison.Ordinal);
        await page.Keyboard.PressAsync("Enter");
        var editor = page.Locator("[data-code-stage] .cm-content");
        await Expect(editor).ToBeFocusedAsync();
        var editing = await heard.RecordAsync("Enter: Code view, focus in the editor");
        Assert.Equal("textbox", editing.Role);
        await Expect(page.Locator("[data-projection='code']")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("[data-projection='canvas']")).ToHaveAttributeAsync("aria-pressed", "false");

        // ── Edit, undo, save ───────────────────────────────────────────────────
        await page.Keyboard.PressAsync("ControlOrMeta+End");
        await page.Keyboard.InsertTextAsync("\n-- a line to undo\n");
        var docTab = page.Locator(".etlsql-studio-tab.active");
        await Expect(docTab).ToHaveAttributeAsync("aria-label", new System.Text.RegularExpressions.Regex(@"\(unsaved changes\)$"));
        await page.Keyboard.PressAsync("ControlOrMeta+z");
        await page.WaitForFunctionAsync("() => !window.__STUDIO__.state.editorInstance.getValue().includes('a line to undo')");

        await page.Keyboard.PressAsync("ControlOrMeta+End");
        await page.Keyboard.InsertTextAsync("\n-- kept\n");
        await page.Keyboard.PressAsync("ControlOrMeta+s");
        await heard.WaitForAnnouncementAsync("Saved");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");
        await heard.RecordAsync("Edit, undo, edit, Ctrl+S: saved");
        await Expect(docTab).Not.ToHaveAttributeAsync("aria-label", new System.Text.RegularExpressions.Regex(@"unsaved"));

        heard.AssertEveryStopIsNamed();
        await heard.SaveAsync("portal-keyboard-journey.md");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Desktop_AssistiveJourney_EveryStopIsNamedAndChangesAreAnnounced()
    {
        using var workspace = new StudioTempWorkspace();
        var token = $"desktop-{Guid.NewGuid():N}";
        await using var host = WorkstationEditorApp.Create(
            [],
            new WorkstationEditorOptions(workspace.Root, null, 0, false, token,
                StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token={token}");
        await WaitForStudioAsync(session);
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");
        var heard = await AssistiveTranscript.StartAsync(page);

        // ── Create: the desktop host opens an untitled document, no dialog ─────
        var createCard = page.Locator("[data-create-from-home='dashboard'].workflow-dashboard");
        await createCard.WaitForAsync(new() { Timeout = 10_000 });
        await createCard.FocusAsync();
        await heard.RecordAsync("Focus the new dashboard card on Home");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(DocumentOpened, null, new() { Timeout = 20_000 });
        var tab = await heard.RecordAsync("Enter: an untitled document opens");
        Assert.Equal("tab", tab.Role);
        Assert.Contains("selected", tab.Detail, StringComparison.Ordinal);

        await page.Keyboard.PressAsync("ArrowLeft");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");
        await heard.RecordAsync("Left arrow: the Home tab");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId !== '__home__'");
        await page.WaitForFunctionAsync(DocumentOpened, null, new() { Timeout = 20_000 });
        await heard.RecordAsync("Right arrow: back to the document tab");

        await KeyboardWalk.TabToAsync(page, "[data-projection='code']", maxPresses: 12);
        await heard.RecordAsync("Tab to the Code view toggle");
        await page.Keyboard.PressAsync("Enter");
        var editor = page.Locator("[data-code-stage] .cm-content");
        await Expect(editor).ToBeFocusedAsync();
        var editing = await heard.RecordAsync("Enter: Code view, focus in the editor");
        Assert.Equal("textbox", editing.Role);
        await Expect(page.Locator("[data-projection='code']")).ToHaveAttributeAsync("aria-pressed", "true");

        // ── Edit, undo, then Save As with one refusal on the way ───────────────
        await page.Keyboard.PressAsync("ControlOrMeta+End");
        await page.Keyboard.InsertTextAsync("\n-- a line to undo\n");
        await page.Keyboard.PressAsync("ControlOrMeta+z");
        await page.WaitForFunctionAsync("() => !window.__STUDIO__.state.editorInstance.getValue().includes('a line to undo')");
        await page.Keyboard.PressAsync("ControlOrMeta+End");
        await page.Keyboard.InsertTextAsync("\n-- kept\n");

        await page.Keyboard.PressAsync("ControlOrMeta+s");
        var dialog = page.Locator(".etlsql-feedback-dialog");
        var filename = dialog.Locator("input");
        await Expect(filename).ToBeFocusedAsync();
        var saveAs = await heard.RecordAsync("Ctrl+S: the Save as dialog opens");
        Assert.Equal("Save as", saveAs.Dialog);

        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.PressAsync("Tab");
        await heard.RecordAsync("Clear the filename, Tab to Cancel");
        await page.Keyboard.PressAsync("Tab");
        await heard.RecordAsync("Tab to Save");
        await page.Keyboard.PressAsync("Enter");
        await heard.WaitForAnnouncementAsync("This field is required.");
        await Expect(filename).ToBeFocusedAsync();
        var refused = await heard.RecordAsync("Enter: Save is refused");
        Assert.Contains("invalid", refused.Detail, StringComparison.Ordinal);
        Assert.Contains("This field is required.", refused.Detail, StringComparison.Ordinal);

        await page.Keyboard.TypeAsync("heard-report.rptsql");
        await Expect(filename).Not.ToHaveAttributeAsync("aria-invalid", "true");
        await KeyboardWalk.TabToAsync(page, ".etlsql-feedback-dialog .etlsql-feedback-btn-primary", maxPresses: 4);
        await page.Keyboard.PressAsync("Enter");
        await heard.WaitForAnnouncementAsync("Saved heard-report.rptsql");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");
        await Expect(editor).ToBeFocusedAsync();
        await heard.RecordAsync("Type a filename, Save: saved, focus back in the editor");
        Assert.True(File.Exists(Path.Combine(workspace.Root, "heard-report.rptsql")));

        heard.AssertEveryStopIsNamed();
        await heard.SaveAsync("desktop-keyboard-journey.md");
        Assert.Empty(session.PageErrors);
    }
}
