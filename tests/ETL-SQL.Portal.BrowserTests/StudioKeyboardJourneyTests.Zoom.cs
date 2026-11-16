using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// ST26 and ST27 — zoom and focus.
///
/// <para><b>Declared zoom settings:</b> 100%, 150% and 200% of a 1440 × 900 window. 200% is the
/// level WCAG 1.4.4 asks text to survive; beyond it Studio's two-pane workbench is not claimed.</para>
///
/// <para>At each setting a dialog and the split controls are driven from the keyboard, and three
/// things are held to: every control the keyboard lands on is inside the window, focus never falls
/// to the page, and a closed dialog hands focus back to the control that opened it.</para>
/// </summary>
public sealed partial class StudioKeyboardJourneyTests
{
    public static TheoryData<double> DeclaredZoomSettings => new() { 1.0, 1.5, 2.0 };

    /// <summary>ST26 — Portal zoom/focus journey.</summary>
    [Theory]
    [MemberData(nameof(DeclaredZoomSettings))]
    public async Task Portal_ZoomFocusJourney_DialogsAndSplitControlsStayUsable(double zoom)
    {
        await using var session = await fixture.NewSessionAsync(zoom);
        var page = session.Page;

        var authorId = await SignInAsPublisherAsync(page);
        await CreateWritableFolderAsync(authorId);

        await page.GotoWithDiagnosticsAsync("/studio.html");
        await WaitForStudioAsync(session);
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");

        // ── Dialog: the catalog create dialog, opened from Home ────────────────
        var createCard = page.Locator("[data-create-from-home='dashboard'].workflow-dashboard");
        await createCard.WaitForAsync(new() { Timeout = 10_000 });
        await createCard.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var nameInput = page.Locator("[data-catalog-report-name]");
        await Expect(nameInput).ToBeFocusedAsync();
        await AssertDialogControlsStayInsideTheWindowAsync(page, "[data-modal-box]", zoom);

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("[data-modal-box]")).ToBeEmptyAsync();
        await Expect(createCard).ToBeFocusedAsync();
        await AssertFocusedControlIsInsideTheWindowAsync(page, "the control that opened the dialog", zoom);

        // ── Create the document so the split controls exist ────────────────────
        await page.Keyboard.PressAsync("Enter");
        await Expect(nameInput).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.TypeAsync($"Zoom_Report_{Guid.NewGuid():N}"[..22]);
        await KeyboardWalk.TabToAsync(page, "[data-catalog-create-confirm]", maxPresses: 6);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.length > 0 && window.__STUDIO__.state.activeDocId !== '__home__'",
            null, new() { Timeout = 20_000 });

        await AssertSplitControlsStayUsableAsync(page, zoom);

        Assert.Empty(session.PageErrors);
    }

    /// <summary>ST27 — Desktop zoom/focus journey, in the self-installed host.</summary>
    [Theory]
    [MemberData(nameof(DeclaredZoomSettings))]
    public async Task Desktop_ZoomFocusJourney_DialogsAndSplitControlsStayUsable(double zoom)
    {
        using var workspace = new StudioTempWorkspace();
        var token = $"desktop-{Guid.NewGuid():N}";
        await using var host = WorkstationEditorApp.Create(
            [],
            new WorkstationEditorOptions(workspace.Root, null, 0, false, token,
                StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync(zoom);
        var page = session.Page;
        await page.GotoWithDiagnosticsAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token={token}");
        await WaitForStudioAsync(session);
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");

        // The desktop host creates an untitled document straight from Home; its dialog is Save As.
        var createCard = page.Locator("[data-create-from-home='dashboard'].workflow-dashboard");
        await createCard.WaitForAsync(new() { Timeout = 10_000 });
        await createCard.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.length > 0 && window.__STUDIO__.state.activeDocId !== '__home__'",
            null, new() { Timeout = 20_000 });

        await AssertSplitControlsStayUsableAsync(page, zoom);

        // ── Dialog: Save As, opened from the editor ────────────────────────────
        var editor = page.Locator("[data-code-stage] .cm-content");
        await editor.FocusAsync();
        await Expect(editor).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+s");

        var dialog = page.Locator(".etlsql-feedback-dialog");
        await dialog.WaitForAsync(new() { Timeout = 10_000 });
        await Expect(dialog.Locator("input")).ToBeFocusedAsync();
        await AssertDialogControlsStayInsideTheWindowAsync(page, ".etlsql-feedback-dialog", zoom);

        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).Not.ToBeVisibleAsync();
        await Expect(editor).ToBeFocusedAsync();

        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Tabs once round the open dialog. Each stop must be inside the dialog and inside the window —
    /// a button below the fold of a dialog that cannot scroll is a button a zoomed user cannot press.
    /// </summary>
    private static async Task AssertDialogControlsStayInsideTheWindowAsync(IPage page, string dialogSelector, double zoom)
    {
        var first = await FocusDescriptionAsync(page);
        var stops = 0;
        do
        {
            Assert.True(
                await page.EvaluateAsync<bool>("selector => Boolean(document.activeElement?.closest(selector))", dialogSelector),
                $"At {zoom:P0} zoom, Tab left the dialog and landed on {await FocusDescriptionAsync(page)}.");
            await AssertFocusedControlIsInsideTheWindowAsync(page, "a dialog control", zoom);
            await page.Keyboard.PressAsync("Tab");
            stops++;
        }
        while (await FocusDescriptionAsync(page) != first && stops < 20);

        Assert.True(stops is > 1 and < 20, $"At {zoom:P0} zoom the dialog's Tab cycle had {stops} stops and never came back round.");
    }

    /// <summary>
    /// Switches projection and moves the splitter from the keyboard. Both panes must keep height,
    /// and the splitter must answer its arrow keys and keep focus while it does.
    /// </summary>
    private static async Task AssertSplitControlsStayUsableAsync(IPage page, double zoom)
    {
        // Opening a document ends with focus on its tab or in its editor, never on the page: the
        // control that opened it is hidden by then. Until that has happened the open is still
        // running, and anything focused in the meantime is about to lose it.
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.matches('.etlsql-studio-tab.active, [data-code-stage] .cm-content') === true",
            null, new() { Timeout = 20_000 });

        foreach (var projection in new[] { "code", "canvas", "split" })
        {
            var toggle = page.Locator($"[data-projection='{projection}']");
            await toggle.FocusAsync();
            await AssertFocusedControlIsInsideTheWindowAsync(page, $"the {projection} view toggle", zoom);
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForFunctionAsync(
                "projection => window.__STUDIO__.state.documents[0].projection === projection", projection);
            Assert.False(
                await page.EvaluateAsync<bool>("() => !document.activeElement || document.activeElement === document.body"),
                $"At {zoom:P0} zoom, switching to the {projection} view dropped focus to the page.");
        }

        var resizer = page.Locator("[data-stage-resizer]");
        await resizer.FocusAsync();
        await Expect(resizer).ToBeFocusedAsync();
        await AssertFocusedControlIsInsideTheWindowAsync(page, "the splitter", zoom);

        var before = int.Parse(await resizer.GetAttributeAsync("aria-valuenow") ?? "0");
        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(resizer).ToHaveAttributeAsync("aria-valuenow", (before + 5).ToString());
        Assert.True(
            await page.EvaluateAsync<bool>("() => document.activeElement?.matches('[data-stage-resizer]') === true"),
            $"At {zoom:P0} zoom, moving the splitter sent focus to {await FocusDescriptionAsync(page)}.");
        await page.Keyboard.PressAsync("ArrowUp");
        await Expect(resizer).ToHaveAttributeAsync("aria-valuenow", before.ToString());

        var paneHeights = await page.EvaluateAsync<double[]>(
            """
            () => ['[data-visual-stage]', '[data-code-stage]']
                .map(selector => document.querySelector(selector)?.getBoundingClientRect().height ?? 0)
            """);
        Assert.All(paneHeights, height => Assert.True(height >= 80,
            $"At {zoom:P0} zoom a split pane is {height:F0} CSS pixels tall, too short to work in."));
    }

    private static async Task AssertFocusedControlIsInsideTheWindowAsync(IPage page, string what, double zoom)
    {
        var box = await page.EvaluateAsync<double[]>(
            """
            () => {
                const rect = document.activeElement.getBoundingClientRect();
                return [rect.left, rect.top, rect.right, rect.bottom, window.innerWidth, window.innerHeight];
            }
            """);
        Assert.True(
            box[0] >= -0.5 && box[1] >= -0.5 && box[2] <= box[4] + 0.5 && box[3] <= box[5] + 0.5 && box[2] > box[0] && box[3] > box[1],
            $"At {zoom:P0} zoom, {what} ({await FocusDescriptionAsync(page)}) sits at "
            + $"[{box[0]:F0},{box[1]:F0}]–[{box[2]:F0},{box[3]:F0}] in a {box[4]:F0} × {box[5]:F0} window.");
    }

    private static async Task<string> FocusDescriptionAsync(IPage page) =>
        await page.EvaluateAsync<string>(
            """
            () => {
                const element = document.activeElement;
                if (!element || element === document.body) return '(page)';
                const name = element.getAttribute('aria-label') || element.id
                    || (element.textContent || '').trim().slice(0, 30) || element.className;
                return `${element.tagName.toLowerCase()}[${name}]`;
            }
            """);
}
