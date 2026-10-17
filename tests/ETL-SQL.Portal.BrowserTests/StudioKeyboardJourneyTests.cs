using System.Text.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.WorkstationEditor;
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
public sealed partial class StudioKeyboardJourneyTests(StudioAuthoringFixture fixture)
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

    /// <summary>
    /// ST24 — Desktop keyboard editing journey:
    /// Prove the same sequence in the self-installed host.
    /// Create, edit, undo, and save one permitted document using only the keyboard;
    /// verify modal/tab navigation and focus return.
    /// </summary>
    [Fact]
    public async Task Desktop_KeyboardEditingJourney_CreatesEditsUndoesAndSavesDocument()
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

        // Verify Studio opened on the Home view.
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");

        // ── 1. Create permitted document from Home action card via keyboard ───
        var createCard = page.Locator("[data-create-from-home='dashboard'].workflow-dashboard");
        await createCard.WaitForAsync(new() { Timeout = 10_000 });
        await createCard.FocusAsync();
        await Expect(createCard).ToBeFocusedAsync();

        // Create document using keyboard Enter.
        await page.Keyboard.PressAsync("Enter");

        // Document is created and opened as active document.
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.length > 0 && window.__STUDIO__.state.activeDocId !== '__home__'",
            null, new() { Timeout = 20_000 });

        // ── 2. Tab list keyboard navigation ────────────────────────────────────
        var homeTab = page.Locator("#etlsql-tab-home");
        var docTab = page.Locator(".etlsql-studio-tab.active");
        await Expect(docTab).ToBeVisibleAsync();

        // Focus document tab.
        await docTab.FocusAsync();
        await Expect(docTab).ToBeFocusedAsync();

        // Navigate with ArrowLeft to Home tab; active document switches to '__home__'.
        await page.Keyboard.PressAsync("ArrowLeft");
        await Expect(homeTab).ToBeFocusedAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId === '__home__'");

        // Navigate with ArrowRight back to document tab; switches back to document.
        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(docTab).ToBeFocusedAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.activeDocId !== '__home__'");

        // ── 3. Keyboard projection and editor focus ────────────────────────────
        var codeProjBtn = page.Locator("[data-projection='code']");
        await codeProjBtn.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].projection === 'code'");

        var editor = page.Locator("[data-code-stage] .cm-content");
        await editor.WaitForAsync(new() { Timeout = 10_000 });
        await editor.FocusAsync();
        await Expect(editor).ToBeFocusedAsync();

        // ── 4. Keyboard edit and undo ──────────────────────────────────────────
        await editor.PressAsync("ControlOrMeta+End");
        const string temporaryMarker = "\n-- temporary desktop keyboard modification to undo\n";
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

        // ── 5. Keyboard edit permitted document content ────────────────────────
        const string reportName = "desktop-keyboard.rptsql";
        var validScript = $$"""
            SET REPORT TITLE = 'Desktop Keyboard Report';

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

        // ── 6. Modal navigation, focus trap, and focus return on Save As ────────
        // For an untitled document on desktop, Ctrl+S opens the Save As prompt modal.
        await page.Keyboard.PressAsync("ControlOrMeta+s");

        var dialog = page.Locator(".etlsql-feedback-dialog");
        await dialog.WaitForAsync(new() { Timeout = 10_000 });
        await Expect(dialog).ToBeVisibleAsync();

        var filenameInput = dialog.Locator("input");
        var cancelBtn = dialog.Locator("button", new() { HasText = "Cancel" });
        var saveBtn = dialog.Locator("button", new() { HasText = "Save" });

        // Initial focus is directed to the filename input.
        await Expect(filenameInput).ToBeFocusedAsync();

        // Forward tab cycle through modal controls.
        await page.Keyboard.PressAsync("Tab");
        await Expect(cancelBtn).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Tab");
        await Expect(saveBtn).ToBeFocusedAsync();

        // Focus trap wrap-around back to first control.
        await page.Keyboard.PressAsync("Tab");
        await Expect(filenameInput).ToBeFocusedAsync();

        // Reverse tab cycle (Shift+Tab wrap-around).
        await page.Keyboard.PressAsync("Shift+Tab");
        await Expect(saveBtn).ToBeFocusedAsync();

        // Escape cancels modal and returns focus to the invoking editor.
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).Not.ToBeVisibleAsync();
        await Expect(editor).ToBeFocusedAsync();

        // ── 7. Re-open Save As modal, provide filename, and save ───────────────
        await page.Keyboard.PressAsync("ControlOrMeta+s");
        await dialog.WaitForAsync(new() { Timeout = 10_000 });
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(filenameInput).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.TypeAsync(reportName);

        await page.Keyboard.PressAsync("Tab"); // to cancel
        await page.Keyboard.PressAsync("Tab"); // to save
        await Expect(saveBtn).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");

        // Document is saved.
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].isDirty === false",
            null, new() { Timeout = 20_000 });

        // ── 8. Verify persisted file on disk, reload survival, and certification
        var targetFile = Path.Combine(workspace.Root, reportName);
        Assert.True(File.Exists(targetFile), $"Expected file {targetFile} to exist on disk.");
        var diskContent = await File.ReadAllTextAsync(targetFile);
        Assert.Equal(NormalizeEndings(validScript), NormalizeEndings(diskContent));

        // Reload the browser and verify the document survives reload with identical script.
        await page.ReloadAsync();
        await WaitForStudioAsync(session);

        // Open the saved file from Studio Home using keyboard navigation.
        var openFileBtn = page.Locator($"[data-open-file='{reportName}'][data-open-proj='code']");
        await openFileBtn.WaitForAsync(new() { Timeout = 10_000 });
        await openFileBtn.FocusAsync();
        await Expect(openFileBtn).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");

        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.length > 0 && window.__STUDIO__.state.activeDocId !== '__home__'",
            null, new() { Timeout = 20_000 });
        // The tab switches before the opened file reaches the editor; on a slow runner the read below
        // landed in between and saw an empty buffer.
        await page.WaitForFunctionAsync(
            "() => (window.__STUDIO__.state.editorInstance?.getValue?.() ?? '').length > 0",
            null, new() { Timeout = 20_000 });

        var reloadedScript = await page.EvaluateAsync<string>(
            "() => window.__STUDIO__.state.editorInstance?.getValue?.() ?? ''");
        Assert.Equal(NormalizeEndings(validScript), NormalizeEndings(reloadedScript));

        StudioCertification.Certify(
            new CertifiedArtifact("desktop keyboard editing", StudioHost.Desktop, reportName, validScript),
            diskContent);

        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// ST25 — Keyboard visual move/resize, production host half:
    /// a Publisher moves one visual and resizes another without a pointer, saves from the canvas,
    /// and the reopened report holds the layout the keyboard made.
    ///
    /// <para>The three changes land on grid cells STRUCTURE can state exactly, so the placements
    /// read back after the reload are compared as equal, not as close.</para>
    /// </summary>
    [Fact]
    public async Task Portal_KeyboardLayout_MovesResizesAndPersistsAVisual()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        var authorId = await SignInAsPublisherAsync(page);
        var folderId = await CreateWritableFolderAsync(authorId);
        var report = await page.EvaluateAsync<JsonElement>(
            """
            async request => {
                const { studioApi } = await import('/js/api.js');
                return studioApi.createReport(request);
            }
            """,
            new
            {
                folderId,
                name = $"Keyboard Layout {Guid.NewGuid():N}",
                scriptText = """
                    CREATE CONNECTION sample_data AS MOCKDB();

                    SELECT UserID, UserName INTO #users FROM sample_data.Users;

                    CREATE VISUAL UserCount AS CARD (
                      SOURCE = #users,
                      MAPPINGS (VALUE = UserID)
                    );

                    CREATE VISUAL UsersTable AS TABLE (
                      SOURCE = #users,
                      MAPPINGS (USER_ID = UserID, USER_NAME = UserName)
                    );

                    CREATE PAGE Main AS DASHBOARD (
                      LAYOUT (STRUCTURE = 'A B', MAP ('A' = UserCount, 'B' = UsersTable))
                    );
                    """
            });
        var reportId = report.GetProperty("id").GetInt32();

        const string count = ".etlsql-dsgn-visual-card[data-vid*='UserCount']";
        const string table = ".etlsql-dsgn-visual-card[data-vid*='UsersTable']";

        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await WaitForStudioAsync(session);
        await page.Locator(table).WaitForAsync(new() { Timeout = 20_000 });
        Assert.Equal("1/1/6/4", await PlacementAsync(page, "UserCount"));
        Assert.Equal("7/1/6/4", await PlacementAsync(page, "UsersTable"));

        // ── 1. Move: Tab to the table's card, four rows down ───────────────────
        var heard = await AssistiveTranscript.StartAsync(page);
        await page.Locator(".etlsql-studio-tab.active").FocusAsync();
        await KeyboardWalk.TabToAsync(page, table);
        var card = await heard.RecordAsync("Tab to the table's card");
        Assert.Equal("group", card.Role);
        Assert.Contains("Arrow keys move the visual", card.Detail, StringComparison.Ordinal);
        await KeyboardWalk.PressAsync(page, "ArrowDown", 4);
        Assert.Equal("7/5/6/4", await PlacementAsync(page, "UsersTable"));
        await Expect(page.Locator(table)).ToBeFocusedAsync();
        await Expect(page.Locator("[data-canvas-status]")).ToContainTextAsync("moved: column 7, row 5");
        await heard.RecordAsync("Down arrow, four times");

        // ── 2. Resize: Shift+Tab to the card, widened to the full grid ─────────
        await KeyboardWalk.TabToAsync(page, count, backwards: true);
        await KeyboardWalk.PressAsync(page, "Shift+ArrowRight", 6);
        Assert.Equal("1/1/12/4", await PlacementAsync(page, "UserCount"));
        await Expect(page.Locator(count)).ToBeFocusedAsync();
        await heard.RecordAsync("Shift+Tab to the card visual, Shift+Right arrow six times");

        // ── 3. Place by number: the inspector's grid column, typed ─────────────
        await KeyboardWalk.TabToAsync(page, table);
        await page.Keyboard.PressAsync("Enter");
        await page.Locator("#pp-col").WaitForAsync(new() { Timeout = 10_000 });
        await KeyboardWalk.TabToAsync(page, "#pp-col", backwards: true);
        var column = await heard.RecordAsync("Enter selects the table, Shift+Tab to its grid column");
        Assert.Equal("Grid column", column.Name);
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.TypeAsync("1");
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes(\"STRUCTURE = 'A / B .'\")",
            null, new() { Timeout = 20_000 });
        Assert.Equal("1/5/6/4", await PlacementAsync(page, "UsersTable"));
        await heard.RecordAsync("Type 1, Tab to the grid row");

        // ── 4. Save from the canvas: one save, Studio's, and no second dialog ──
        await KeyboardWalk.TabToAsync(page, table);
        await page.Keyboard.PressAsync("ControlOrMeta+s");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].isDirty === false",
            null, new() { Timeout = 20_000 });
        await Expect(page.Locator(".etlsql-dsgn-modal-bg:visible")).ToHaveCountAsync(0);
        await heard.WaitForAnnouncementAsync("Saved");
        await heard.RecordAsync("Tab to the table's card, Ctrl+S");
        heard.AssertEveryStopIsNamed();
        await heard.SaveAsync("portal-keyboard-layout.md");

        // ── 5. The host's copy, and the canvas a reload draws from it ──────────
        var saved = await page.EvaluateAsync<string>(
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
        Assert.Contains("STRUCTURE = 'A / B .'", saved, StringComparison.Ordinal);

        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await WaitForStudioAsync(session);
        await page.Locator(table).WaitForAsync(new() { Timeout = 20_000 });
        Assert.Equal("1/1/12/4", await PlacementAsync(page, "UserCount"));
        Assert.Equal("1/5/6/4", await PlacementAsync(page, "UsersTable"));
        Assert.Contains("column 1, row 5, 6 columns wide, 4 rows tall",
            await page.Locator(table).GetAttributeAsync("aria-label"), StringComparison.Ordinal);

        StudioCertification.Certify(
            new CertifiedArtifact("keyboard layout", StudioHost.Portal, $"report-{reportId}.rptsql", saved),
            saved);

        Assert.Empty(session.PageErrors);
    }

    private static async Task<string> PlacementAsync(IPage page, string visualName) =>
        await page.EvaluateAsync<string>(
            """
            name => {
                const visual = window.__STUDIO__.state.designerInstance.getState().pages
                    .flatMap(page => page.visuals || [])
                    .find(item => item.name === name);
                return visual ? `${visual.gridCol}/${visual.gridRow}/${visual.gridColSpan}/${visual.gridRowSpan}` : '';
            }
            """, visualName);

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
