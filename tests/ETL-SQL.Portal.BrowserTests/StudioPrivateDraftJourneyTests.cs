using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// A learner keeps unpublished work in Portal Studio. The learner is a Viewer whose group grants
/// Studio access, script reading and preview: no publish right, no save right on the catalog, and no
/// grant on any folder. Save keeps the document as a private draft only they can open.
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalJourneyCollection.Name)]
public sealed class StudioPrivateDraftJourneyTests(PortalJourneyFixture fixture)
{
    private const string Password = "Learner@Journey99!";
    private const string ActiveDraft = "window.__STUDIO__.state.documents.find(d => d.id === window.__STUDIO__.state.activeDocId)";

    [Fact]
    public async Task ALearnerKeepsEditsAndReopensAPrivateDraftNobodyElseSees()
    {
        var learner = await CreateLearnerAsync();
        var colleague = await CreateLearnerAsync();
        var catalogDocuments = await CatalogDocumentCountAsync();
        var name = $"Practice {Guid.NewGuid():N}"[..20];

        await using (var session = await fixture.NewSessionAsync())
        {
            var page = session.Page;
            await SignInAsync(page, learner.UserName);
            await page.GotoAsync("/studio.html");
            await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null, new() { Timeout = 20_000 });

            // The learner cannot publish, cannot save to the catalog, and has no folder to write to.
            Assert.False(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.capabilities.has('ReportPublish')"));
            Assert.False(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.capabilities.has('ScriptSave')"));
            Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__STUDIO__.state.catalogFolders.length"));
            await Expect(page.Locator("[data-private-drafts]")).ToContainTextAsync("Nothing kept yet");

            // Create a dashboard and save it: Studio keeps it as a private draft.
            await page.Locator("[data-create-from-home='dashboard']:not([data-seed-sample])").ClickAsync();
            await page.WaitForFunctionAsync($"() => {ActiveDraft}?.path?.startsWith('untitled_dashboard')", null, new() { Timeout = 20_000 });
            await page.EvaluateAsync("() => window.__STUDIO__.setProjection('code')");
            await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__.state.editorInstance)", null, new() { Timeout = 20_000 });
            await page.Locator("[data-action='save']").ClickAsync();
            var dialog = page.Locator(".etlsql-feedback-dialog");
            await Expect(dialog).ToContainTextAsync("Only you can open a private draft");
            await dialog.Locator("input").FillAsync(name);
            await dialog.Locator(".etlsql-feedback-btn-primary").ClickAsync();
            await page.WaitForFunctionAsync($"() => {ActiveDraft}?.privateDraftId > 0 && {ActiveDraft}.isDirty === false",
                null, new() { Timeout = 20_000 });
            var draftId = await page.EvaluateAsync<int>($"() => {ActiveDraft}.privateDraftId");
            Assert.Equal(1, await page.EvaluateAsync<int>($"() => {ActiveDraft}.version"));

            // Edit and save again: the same draft takes a second version.
            var edited = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()") + "\n-- a second thought\n";
            await page.EvaluateAsync("body => window.__STUDIO__.state.editorInstance.setValue(body)", edited);
            await page.WaitForFunctionAsync($"() => {ActiveDraft}.isDirty === true", null, new() { Timeout = 15_000 });
            await page.Locator("[data-action='save']").ClickAsync();
            await page.WaitForFunctionAsync($"() => {ActiveDraft}.isDirty === false && {ActiveDraft}.version === 2",
                null, new() { Timeout = 15_000 });

            var stored = await StoredDraftAsync(draftId);
            Assert.Equal(name, stored.Name);
            Assert.Equal(2, stored.Version);
            Assert.Equal(Normalize(edited), Normalize(stored.Content));
            Assert.Equal(learner.UserId, stored.OwnerUserId);
            // Nothing reached the catalog.
            Assert.Equal(catalogDocuments, await CatalogDocumentCountAsync());

            // A fresh page: the draft is listed on Studio Home and reopens with what was saved.
            await page.GotoAsync("/studio.html");
            var card = page.Locator($"[data-private-draft='{draftId}']");
            await Expect(card).ToContainTextAsync(name, new() { Timeout = 20_000 });
            await card.Locator("[data-open-private-draft][data-open-proj='code']").ClickAsync();
            // The draft becomes active before its text reaches the editor; on a slow runner the read
            // landed in between. Wait for the text itself, then compare it exactly.
            await page.WaitForFunctionAsync(
                $"id => {ActiveDraft}?.privateDraftId === id && (window.__STUDIO__.state.editorInstance?.getValue?.() ?? '').length > 0",
                draftId, new() { Timeout = 20_000 });
            Assert.Equal(Normalize(edited), Normalize(await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()")));
            Assert.Equal(2, await page.EvaluateAsync<int>($"() => {ActiveDraft}.version"));
            Assert.False(await page.EvaluateAsync<bool>($"() => {ActiveDraft}.isDirty"));

            Assert.Empty(session.PageErrors);
        }

        // A colleague with the same rights sees no draft at all.
        await using var other = await fixture.NewSessionAsync();
        await SignInAsync(other.Page, colleague.UserName);
        await other.Page.GotoAsync("/studio.html");
        await Expect(other.Page.Locator("[data-private-drafts]")).ToContainTextAsync("Nothing kept yet", new() { Timeout = 20_000 });
        await Expect(other.Page.Locator("[data-private-draft]")).ToHaveCountAsync(0);
        await Expect(other.Page.Locator("body")).Not.ToContainTextAsync(name);
        Assert.Empty(other.PageErrors);
    }

    private const string Edit = "-- unsaved edit that must survive";

    [Fact]
    public async Task UnsavedEditsToADraftSurviveACrashAndARenewedSignIn()
    {
        var learner = await CreateLearnerAsync();
        int draftId;
        await using (var crashed = await fixture.NewSessionAsync())
        {
            var page = crashed.Page;
            await SignInAsync(page, learner.UserName);
            draftId = await KeepNewDraftAsync(page);

            // The access token stops being accepted mid-session; the refresh token is still good.
            await page.EvaluateAsync("() => sessionStorage.setItem('etlsql_token', 'expired.access.token')");
            await TypeAndWaitForUnsavedEditsAsync(page, draftId, Edit);
            Assert.Contains("/studio.html", page.Url, StringComparison.Ordinal);
            Assert.NotEqual("expired.access.token", await page.EvaluateAsync<string>("() => sessionStorage.getItem('etlsql_token')"));
            Assert.Empty(crashed.PageErrors);
            // Disposing the context closes the page without beforeunload: nothing gets a last chance.
        }
        var stored = await StoredDraftAsync(draftId);
        Assert.Equal(1, stored.Version);
        Assert.DoesNotContain(Edit, stored.Content);
        Assert.Contains(Edit, stored.UnsavedContent);

        // A new browser and a new sign-in: the edits are offered back, with no conflict to report.
        await using var session = await fixture.NewSessionAsync();
        var restored = session.Page;
        await SignInAsync(restored, learner.UserName);
        await ReopenDraftAsync(restored, draftId);
        var offer = restored.Locator(".etlsql-feedback-toast", new() { HasText = "from a previous session were kept" });
        await Expect(offer).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(offer).Not.ToContainTextAsync("changed since");
        await offer.Locator(".etlsql-feedback-action", new() { HasText = "Restore" }).ClickAsync();
        await restored.WaitForFunctionAsync("edit => window.__STUDIO__.state.editorInstance.getValue().includes(edit)", Edit);
        Assert.True(await restored.EvaluateAsync<bool>($"() => {ActiveDraft}.isDirty"));

        // Saved, the edits become version 2 and there is nothing left to recover.
        await restored.Locator("[data-action='save']").ClickAsync();
        await restored.WaitForFunctionAsync($"() => {ActiveDraft}.isDirty === false && {ActiveDraft}.version === 2",
            null, new() { Timeout = 15_000 });
        await WaitForAsync(async () => (await StoredDraftAsync(draftId)).UnsavedContent is null);
        var saved = await StoredDraftAsync(draftId);
        Assert.Contains(Edit, saved.Content);
        Assert.Equal(2, saved.Version);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ADraftThatMovedOnIsShownAsAConflictAndAStaleSaveIsRefused()
    {
        var learner = await CreateLearnerAsync();
        int draftId;
        await using (var first = await fixture.NewSessionAsync())
        {
            await SignInAsync(first.Page, learner.UserName);
            draftId = await KeepNewDraftAsync(first.Page);
            await TypeAndWaitForUnsavedEditsAsync(first.Page, draftId, Edit);

            // The same learner saves the draft from somewhere else while this tab is open.
            // A page makes the text a dashboard, so reopening it does not stop to ask which kind it is.
            await SaveElsewhereAsync(draftId,
                "-- Saved elsewhere\nCREATE TABLE #t (A INT);\nCREATE VISUAL T AS TABLE (SOURCE = #t);\n"
                + "CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = T));\n");

            // This tab still thinks it is editing version 1. Its save is refused, out loud, and
            // neither the newer text nor the edits in this tab are lost.
            await first.Page.Locator("[data-action='save']").ClickAsync();
            var refusal = first.Page.Locator(".etlsql-feedback-toast", new() { HasText = "saved somewhere else" });
            await Expect(refusal).ToBeVisibleAsync(new() { Timeout = 15_000 });
            Assert.True(await first.Page.EvaluateAsync<bool>($"() => {ActiveDraft}.isDirty"));
            Assert.Contains(Edit, await first.Page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
            var afterRefusal = await StoredDraftAsync(draftId);
            Assert.Equal(2, afterRefusal.Version);
            Assert.Contains("Saved elsewhere", afterRefusal.Content);
            Assert.Empty(first.PageErrors);
        }

        // Reopened, the kept edits are offered with the conflict stated before anything is restored.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await SignInAsync(page, learner.UserName);
        await ReopenDraftAsync(page, draftId);
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.editorInstance.getValue().includes('Saved elsewhere')",
            null, new() { Timeout = 15_000 });
        var offer = page.Locator(".etlsql-feedback-toast", new() { HasText = "from a previous session were kept" });
        await Expect(offer).ToContainTextAsync("the draft has changed since", new() { Timeout = 15_000 });
        await Expect(offer).ToContainTextAsync("stays among the draft's saved versions");

        // Restoring and saving is the learner's explicit choice; the newer text is kept as version 2.
        await offer.Locator(".etlsql-feedback-action", new() { HasText = "Restore" }).ClickAsync();
        await page.WaitForFunctionAsync("edit => window.__STUDIO__.state.editorInstance.getValue().includes(edit)", Edit);
        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync($"() => {ActiveDraft}.isDirty === false && {ActiveDraft}.version === 3",
            null, new() { Timeout = 15_000 });
        using var scope = fixture.Factory.Services.CreateScope();
        var revisions = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().StudioPrivateDraftRevisions
            .AsNoTracking().Where(revision => revision.DraftId == draftId).OrderBy(revision => revision.Version).ToListAsync();
        Assert.Equal([1L, 2L, 3L], revisions.Select(revision => revision.Version).ToArray());
        Assert.Contains("Saved elsewhere", revisions[1].Content);
        Assert.Contains(Edit, revisions[2].Content);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task UnsavedEditsHoldingAPlaintextCredentialAreRefusedAndTheLearnerIsTold()
    {
        var learner = await CreateLearnerAsync();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await SignInAsync(page, learner.UserName);
        var draftId = await KeepNewDraftAsync(page);

        await page.EvaluateAsync(
            "() => { const editor = window.__STUDIO__.state.editorInstance; editor.replaceAll(editor.getValue() + \"\\nCREATE CONNECTION db AS MSSQL(SERVER = 'h', PASSWORD = 'hunter2');\\n\"); }");
        var notice = page.Locator(".etlsql-feedback-toast", new() { HasText = "holds a plaintext credential" });
        await Expect(notice).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(notice).ToContainTextAsync("not kept as a recovery draft");

        var stored = await StoredDraftAsync(draftId);
        Assert.Null(stored.UnsavedContent);
        Assert.DoesNotContain("hunter2", stored.Content);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AnAuthorPublishesAPrivateDraftAndALearnerCannot()
    {
        // An ordinary author: a Publisher who owns one folder. No administrator signs in.
        var author = await CreateAuthorAsync();
        var name = $"Published {Guid.NewGuid():N}"[..20];
        await using (var session = await fixture.NewSessionAsync())
        {
            var page = session.Page;
            await SignInAsync(page, author.UserName);
            await page.GotoAsync("/studio.html");

            // The sample dashboard opens unsaved. Save offers the catalog or a private draft.
            await page.Locator("[data-create-from-home='dashboard'][data-seed-sample]").ClickAsync();
            await page.WaitForFunctionAsync($"() => {ActiveDraft}?.path?.startsWith('untitled_dashboard')", null, new() { Timeout = 20_000 });
            await page.EvaluateAsync("() => window.__STUDIO__.setProjection('code')");
            await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__.state.editorInstance)", null, new() { Timeout = 20_000 });
            await page.Locator("[data-action='save']").ClickAsync();
            var choice = page.Locator(".etlsql-feedback-dialog", new() { HasText = "keep it as a private draft" });
            await choice.Locator(".etlsql-feedback-btn", new() { HasText = "Keep Private Draft" }).ClickAsync();
            var prompt = page.Locator(".etlsql-feedback-dialog", new() { HasText = "Only you can open a private draft" });
            await prompt.Locator("input").FillAsync(name);
            await prompt.Locator(".etlsql-feedback-btn-primary").ClickAsync();
            await page.WaitForFunctionAsync($"() => {ActiveDraft}?.privateDraftId > 0 && {ActiveDraft}.isDirty === false",
                null, new() { Timeout = 20_000 });
            var draftId = await page.EvaluateAsync<int>($"() => {ActiveDraft}.privateDraftId");

            // Edit without saving, then publish: the edit is saved to the draft first, as version 2.
            await page.EvaluateAsync(
                "edit => { const editor = window.__STUDIO__.state.editorInstance; editor.replaceAll(editor.getValue() + '\\n' + edit + '\\n'); }", Edit);
            await page.WaitForFunctionAsync($"() => {ActiveDraft}.isDirty === true", null, new() { Timeout = 15_000 });
            await page.Locator("[data-action='publish']").ClickAsync();
            var nameInput = page.Locator("[data-catalog-report-name]");
            await Expect(nameInput).ToHaveValueAsync(name, new() { Timeout = 15_000 });
            var draft = await StoredDraftAsync(draftId);
            Assert.Equal(2, draft.Version);
            Assert.Contains(Edit, draft.Content);
            await page.Locator("[data-catalog-report-folder]").SelectOptionAsync(author.FolderId.ToString());
            await page.Locator("[data-catalog-create-confirm]").ClickAsync();
            await page.WaitForFunctionAsync($"() => {ActiveDraft}?.reportId > 0 && !{ActiveDraft}.privateDraftId && {ActiveDraft}.lease?.acquired === true",
                null, new() { Timeout = 20_000 });
            var reportId = await page.EvaluateAsync<int>($"() => {ActiveDraft}.reportId");

            // One catalog document in the author's folder; the draft and its saves are gone; the
            // audit trail names both saved versions.
            using (var scope = fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var report = await db.Reports.AsNoTracking().SingleAsync(item => item.Id == reportId);
                Assert.Equal(name, report.Name);
                Assert.Equal(author.FolderId, report.FolderId);
                Assert.Equal(author.UserId, report.CreatedBy);
                Assert.False(await db.StudioPrivateDrafts.AnyAsync(item => item.Id == draftId));
                Assert.False(await db.StudioPrivateDraftRevisions.AnyAsync(item => item.DraftId == draftId));
                var entry = await db.AuditLogs.AsNoTracking().SingleAsync(log =>
                    log.Action == "PUBLISH_PRIVATE_DRAFT" && log.ResourceId == reportId.ToString());
                Assert.Contains($"draft={draftId}", entry.Detail);
                Assert.Contains("v1@", entry.Detail);
                Assert.Contains("v2@", entry.Detail);
            }

            // It is now an ordinary catalog document: it saves as one, and reopens with the text.
            await page.EvaluateAsync(
                "() => { const editor = window.__STUDIO__.state.editorInstance; editor.replaceAll(editor.getValue() + '\\n-- after publishing\\n'); }");
            await page.WaitForFunctionAsync($"() => {ActiveDraft}.isDirty === true", null, new() { Timeout = 15_000 });
            await page.Locator("[data-action='save']").ClickAsync();
            await page.WaitForFunctionAsync($"() => {ActiveDraft}.isDirty === false && {ActiveDraft}.version === 2",
                null, new() { Timeout = 15_000 });
            await page.GotoAsync($"/studio.html?reportId={reportId}");
            await page.WaitForFunctionAsync(
                "edit => { const editor = window.__STUDIO__?.state?.editorInstance; return Boolean(editor) && editor.getValue().includes(edit) && editor.getValue().includes('-- after publishing'); }",
                Edit, new() { Timeout = 30_000 });
            await Expect(page.Locator("[data-private-draft]")).ToHaveCountAsync(0);
            Assert.Empty(session.PageErrors);
        }

        // A learner has no publish right. Publish says so and the draft stays as it was.
        var learner = await CreateLearnerAsync();
        await using var learnerSession = await fixture.NewSessionAsync();
        var learnerPage = learnerSession.Page;
        await SignInAsync(learnerPage, learner.UserName);
        var learnerDraft = await KeepNewDraftAsync(learnerPage);
        var before = await StoredDraftAsync(learnerDraft);
        await learnerPage.Locator("[data-action='publish']").ClickAsync();
        await Expect(learnerPage.Locator(".etlsql-feedback-toast", new() { HasText = "Publish Restricted" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.Equal(learnerDraft, await learnerPage.EvaluateAsync<int>($"() => {ActiveDraft}.privateDraftId"));
        var after = await StoredDraftAsync(learnerDraft);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.Content, after.Content);
        Assert.Empty(learnerSession.PageErrors);
    }

    private sealed record Author(string UserName, int UserId, int FolderId);

    /// <summary>A Publisher who owns one folder. No administrative role.</summary>
    private async Task<Author> CreateAuthorAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new PortalUser
        {
            UserName = $"draftauthor_{suffix}",
            Email = $"draftauthor_{suffix}@test.local",
            IsActive = true,
            MustChangePassword = false,
            Provider = "Local"
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        Assert.True((await users.AddToRoleAsync(user, "Publisher")).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var folder = new Folder { Name = $"Drafts {suffix}", Path = $"/Drafts-{suffix}", OwnerId = user.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return new Author(user.UserName, user.Id, folder.Id);
    }

    /// <summary>Creates a dashboard, keeps it as a private draft and leaves it open in the code view.</summary>
    private static async Task<int> KeepNewDraftAsync(IPage page)
    {
        await page.GotoAsync("/studio.html");
        await page.Locator("[data-create-from-home='dashboard']:not([data-seed-sample])").ClickAsync();
        await page.WaitForFunctionAsync($"() => {ActiveDraft}?.path?.startsWith('untitled_dashboard')", null, new() { Timeout = 20_000 });
        await page.EvaluateAsync("() => window.__STUDIO__.setProjection('code')");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__.state.editorInstance)", null, new() { Timeout = 20_000 });
        await page.Locator("[data-action='save']").ClickAsync();
        var dialog = page.Locator(".etlsql-feedback-dialog");
        await dialog.Locator("input").FillAsync($"Recover {Guid.NewGuid():N}"[..20]);
        await dialog.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        await page.WaitForFunctionAsync($"() => {ActiveDraft}?.privateDraftId > 0 && {ActiveDraft}.isDirty === false",
            null, new() { Timeout = 20_000 });
        return await page.EvaluateAsync<int>($"() => {ActiveDraft}.privateDraftId");
    }

    private static async Task ReopenDraftAsync(IPage page, int draftId)
    {
        await page.GotoAsync("/studio.html");
        var open = page.Locator($"[data-private-draft='{draftId}'] [data-open-private-draft][data-open-proj='code']");
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await open.ClickAsync();
        await page.WaitForFunctionAsync(
            $"id => {ActiveDraft}?.privateDraftId === id && Boolean(window.__STUDIO__.state.editorInstance)",
            draftId, new() { Timeout = 20_000 });
    }

    /// <summary>Types the edit and waits for the Portal to accept it as unsaved edits to the draft.</summary>
    private static async Task TypeAndWaitForUnsavedEditsAsync(IPage page, int draftId, string edit)
    {
        var kept = page.WaitForResponseAsync(response =>
            response.Url.EndsWith($"/api/studio/private-drafts/{draftId}/unsaved", StringComparison.Ordinal)
            && response.Request.Method == "PUT" && response.Status == 204,
            new() { Timeout = 20_000 });
        await page.EvaluateAsync(
            "edit => { const editor = window.__STUDIO__.state.editorInstance; editor.replaceAll(editor.getValue() + '\\n' + edit + '\\n'); }",
            edit);
        await kept;
    }

    /// <summary>The draft's owner saves a new version outside this browser, as a second tab would.</summary>
    private async Task SaveElsewhereAsync(int draftId, string content)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var draft = await db.StudioPrivateDrafts.SingleAsync(item => item.Id == draftId);
        draft.Content = content;
        draft.Version += 1;
        draft.UpdatedAt = DateTime.UtcNow;
        db.StudioPrivateDraftRevisions.Add(new StudioPrivateDraftRevision
        {
            DraftId = draftId,
            Version = draft.Version,
            Content = content,
            ContentHash = "sha256:elsewhere"
        });
        await db.SaveChangesAsync();
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("The expected state was not reached.");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

    private static async Task SignInAsync(IPage page, string userName)
    {
        await page.GotoAsync("/login.html");
        await page.FillAsync("#username", userName);
        await page.FillAsync("#password", Password);
        await page.ClickAsync("#loginBtn");
        await page.WaitForURLAsync("**/index.html", new PageWaitForURLOptions { Timeout = 20_000 });
    }

    private sealed record Learner(string UserName, int UserId);

    /// <summary>A Viewer whose group grants Studio access, reading and preview, and nothing else.</summary>
    private async Task<Learner> CreateLearnerAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new PortalUser
        {
            UserName = $"learner_{suffix}",
            Email = $"learner_{suffix}@test.local",
            IsActive = true,
            MustChangePassword = false,
            Provider = "Local"
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        Assert.True((await users.AddToRoleAsync(user, "Viewer")).Succeeded);

        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var group = new Group { Name = $"Learners {suffix}", Description = "Studio practice" };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        foreach (var capability in new[] { StudioCapabilities.StudioAccess, StudioCapabilities.ScriptRead, StudioCapabilities.ScriptPreview })
            db.GroupStudioCapabilities.Add(new GroupStudioCapability { GroupId = group.Id, Capability = capability });
        await db.SaveChangesAsync();
        return new Learner(user.UserName, user.Id);
    }

    private async Task<StudioPrivateDraft> StoredDraftAsync(int id)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .StudioPrivateDrafts.AsNoTracking().SingleAsync(draft => draft.Id == id);
    }

    private async Task<int> CatalogDocumentCountAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Reports.CountAsync();
    }
}
