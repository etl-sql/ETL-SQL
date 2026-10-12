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
            await Expect(page.Locator("[data-action='publish']")).ToBeDisabledAsync();

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
            await page.WaitForFunctionAsync(
                $"id => {ActiveDraft}?.privateDraftId === id && Boolean(window.__STUDIO__.state.editorInstance)",
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
