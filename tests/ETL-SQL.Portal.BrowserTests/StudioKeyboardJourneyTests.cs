using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// ST23 — Portal keyboard editing journey:
/// Create, edit, undo, and save one permitted document using only the keyboard;
/// verify modal/tab navigation and focus return.
/// </summary>
[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioKeyboardJourneyTests(StudioAuthoringFixture fixture)
{
    private static string NormalizeEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    [Fact]
    public async Task Portal_KeyboardEditingJourney_CreatesEditsUndoesAndSavesDocument()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        var authorId = await SignInAsPublisherAsync(page);
        var folderId = await CreateWritableFolderAsync(authorId);

        await page.GotoAsync("/studio.html");
        await WaitForStudioAsync(session);

        // Verify Studio opened on the Home view.
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");

        // ── 1. Modal navigation, focus trap, and focus return ──────────────────
        // Focus the "New Dashboard" action card on Studio Home.
        var createCard = page.Locator("[data-create-from-home='dashboard'].workflow-dashboard");
        await createCard.WaitForAsync(new() { Timeout = 10_000 });
        await createCard.FocusAsync();
        await Expect(createCard).ToBeFocusedAsync();

        // Open the catalog creation modal using keyboard Enter.
        await page.Keyboard.PressAsync("Enter");

        var modal = page.Locator("[data-modal-box]");
        await modal.WaitForAsync(new() { Timeout = 10_000 });
        await Expect(modal).ToBeVisibleAsync();

        var nameInput = page.Locator("[data-catalog-report-name]");
        var folderSelect = page.Locator("[data-catalog-report-folder]");
        var cancelBtn = page.Locator("[data-catalog-create-cancel]");
        var confirmBtn = page.Locator("[data-catalog-create-confirm]");

        // Initial focus in modal is directed to the document name input.
        await Expect(nameInput).ToBeFocusedAsync();

        // Forward tab cycle through modal controls.
        await page.Keyboard.PressAsync("Tab");
        await Expect(folderSelect).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Tab");
        await Expect(cancelBtn).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Tab");
        await Expect(confirmBtn).ToBeFocusedAsync();

        // Tab wrap-around (focus trap).
        await page.Keyboard.PressAsync("Tab");
        await Expect(nameInput).ToBeFocusedAsync();

        // Reverse tab cycle (Shift+Tab wrap-around).
        await page.Keyboard.PressAsync("Shift+Tab");
        await Expect(confirmBtn).ToBeFocusedAsync();

        // Escape cancels modal and returns focus to the invoking button.
        await page.Keyboard.PressAsync("Escape");
        await Expect(modal).ToBeEmptyAsync();
        await Expect(createCard).ToBeFocusedAsync();

        // ── 2. Create permitted document using only the keyboard ───────────────
        var reportName = $"Keyboard_Report_{Guid.NewGuid():N}"[..24];

        // Re-open modal via keyboard Enter.
        await page.Keyboard.PressAsync("Enter");
        await Expect(nameInput).ToBeFocusedAsync();

        // Replace default name using keyboard.
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.TypeAsync(reportName);

        // Tab to Confirm ("Create") button and submit via Enter.
        await page.Keyboard.PressAsync("Tab"); // to folder select
        await page.Keyboard.PressAsync("Tab"); // to cancel
        await page.Keyboard.PressAsync("Tab"); // to create button
        await Expect(confirmBtn).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");

        // Document is created and opened as active document.
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.length > 0 && window.__STUDIO__.state.activeDocId !== '__home__'",
            null, new() { Timeout = 20_000 });

        // ── 3. Tab list keyboard navigation ────────────────────────────────────
        var homeTab = page.Locator("#etlsql-tab-home");
        var docTab = page.Locator(".etlsql-studio-tab.active");
        await Expect(docTab).ToContainTextAsync(reportName);

        // Focus document tab.
        await docTab.FocusAsync();
        await Expect(docTab).ToBeFocusedAsync();

        // Navigate with ArrowLeft to Home tab; active document switches to '__home__'.
        await page.Keyboard.PressAsync("ArrowLeft");
        await Expect(homeTab).ToBeFocusedAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");

        // Navigate with ArrowRight back to document tab; switches back to the document.
        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(docTab).ToBeFocusedAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId !== '__home__'");

        // ── 4. Keyboard projection and editor focus ────────────────────────────
        // Switch to code projection using keyboard.
        var codeProjBtn = page.Locator("[data-projection='code']");
        await codeProjBtn.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].projection === 'code'");

        var editor = page.Locator("[data-code-stage] .cm-content");
        await editor.WaitForAsync(new() { Timeout = 10_000 });
        await editor.FocusAsync();
        await Expect(editor).ToBeFocusedAsync();

        // ── 5. Keyboard edit and undo ──────────────────────────────────────────
        await editor.PressAsync("ControlOrMeta+End");
        const string temporaryMarker = "\n-- temporary keyboard modification to undo\n";
        await page.Keyboard.InsertTextAsync(temporaryMarker);

        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === true");
        var editedWithTemp = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.Contains(temporaryMarker.Trim(), editedWithTemp);

        // Undo using keyboard shortcut.
        await page.Keyboard.PressAsync("ControlOrMeta+z");
        await page.WaitForFunctionAsync(
            "marker => !window.__STUDIO__.state.editorInstance.getValue().includes(marker)",
            temporaryMarker.Trim());
        var undoneScript = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.DoesNotContain(temporaryMarker.Trim(), undoneScript);

        // ── 6. Keyboard edit permitted document content and save ───────────────
        var validScript = $$"""
            SET REPORT TITLE = '{{reportName}}';

            CREATE CONNECTION sample_data AS MOCKDB();

            SELECT UserID, UserName INTO #users FROM sample_data.Users;

            CREATE VISUAL UsersTable AS TABLE (
              SOURCE = #users,
              MAPPINGS (USER_ID = UserID, USER_NAME = UserName)
            );

            CREATE PAGE Main AS DASHBOARD (
              LAYOUT (STRUCTURE = 'A', MAP ('A' = UsersTable))
            );
            """;

        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.InsertTextAsync(validScript);

        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === true");

        // Save using keyboard shortcut (Ctrl+S).
        await page.Keyboard.PressAsync("ControlOrMeta+s");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].isDirty === false",
            null, new() { Timeout = 20_000 });

        // ── 7. Verify persisted document, reload survival, and certification ──
        var reportId = await page.EvaluateAsync<int>("() => window.__STUDIO__.state.documents[0].reportId");
        Assert.True(reportId > 0, "Document should have a valid catalog report ID.");

        var savedContent = await page.EvaluateAsync<string>(
            """
            async id => {
                const { auth } = await import('/js/api.js');
                const response = await fetch(`/api/reports/${id}/script-content`, {
                    headers: { Authorization: `Bearer ${auth.getToken()}` }
                });
                if (!response.ok) throw new Error(`script-content returned ${response.status}`);
                const payload = await response.json();
                return payload.scriptText ?? payload.script ?? payload.content ?? '';
            }
            """, reportId);

        Assert.Equal(NormalizeEndings(validScript), NormalizeEndings(savedContent));

        // Reload the browser with the report query param and verify the document survives reload with identical script.
        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await WaitForStudioAsync(session);

        var reloadedScript = await page.EvaluateAsync<string>(
            "() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.Equal(NormalizeEndings(validScript), NormalizeEndings(reloadedScript));

        // Certify that the document parses, lints, formats, survives reload, and round-trips.
        StudioCertification.Certify(
            new CertifiedArtifact("keyboard editing", StudioHost.Portal, $"report-{reportId}.rptsql", validScript),
            savedContent);

        Assert.Empty(session.PageErrors);
    }

    private async Task<int> SignInAsPublisherAsync(IPage page)
    {
        var name = $"studio_author_{Guid.NewGuid():N}"[..28];
        var password = $"Studio!9{Guid.NewGuid():N}";
        int userId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var user = new PortalUser
            {
                UserName = name,
                Email = $"{name}@example.invalid",
                IsActive = true,
                MustChangePassword = false
            };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Publisher")).Succeeded);
            Assert.Equal("Publisher", Assert.Single(await users.GetRolesAsync(user)));
            userId = user.Id;
        }
        await page.GotoAsync("/login.html");
        await page.FillAsync("#username", name);
        await page.FillAsync("#password", password);
        await page.ClickAsync("#loginBtn");
        await page.WaitForURLAsync("**/index.html");
        return userId;
    }

    private async Task<int> CreateWritableFolderAsync(int authorId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder
        {
            Name = $"Keyboard Folder {suffix}",
            Path = $"/KeyboardFolder-{suffix}",
            OwnerId = authorId
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private static async Task WaitForStudioAsync(BrowserSession session)
    {
        try
        {
            await session.Page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
                new PageWaitForFunctionOptions { Timeout = 20_000 });
        }
        catch (TimeoutException exception)
        {
            var body = await session.Page.Locator("body").InnerTextAsync();
            throw new Xunit.Sdk.XunitException(
                $"Studio did not boot at {session.Page.Url}. Body: {body[..Math.Min(body.Length, 500)]}. "
                + $"Page errors: {string.Join(" | ", session.PageErrors)}. "
                + $"Console errors: {string.Join(" | ", session.ConsoleErrors)}. "
                + $"Failed requests: {string.Join(" | ", session.FailedRequests)}.", exception);
        }
    }
}
