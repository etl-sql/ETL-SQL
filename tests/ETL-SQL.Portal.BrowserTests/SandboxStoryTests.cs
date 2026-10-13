using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Drives every UI-sandbox story and fixture through a real browser.
///
/// <para>The sandbox already imports the <b>canonical</b> component sources directly, which is what
/// makes it worth automating: mounting a story exercises the same file the Portal ships, without a
/// Portal, a database, or a login. It has only ever been run by a person clicking through it, so a
/// story that throws on mount stays broken until someone happens to open that fixture — and the
/// fixtures people open least are the failure states, which is precisely where a rendering bug is
/// least likely to be noticed and most likely to matter.</para>
///
/// <para>Generic fixture checks cover mounting, page errors, and overflow. Focused regressions also
/// verify response contracts, failure states, request ordering, disposal, and keyboard interactions
/// through the canonical components and controlled API responses.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(SandboxStoryCollection.Name)]
public sealed class SandboxStoryTests(SandboxStoryFixture fixture) : IAsyncLifetime
{
    private IHost? host;
    private string baseUrl = "";

    /// <summary>
    /// Serves the repository root, because the sandbox's ES module imports reach back into
    /// <c>src/</c> with absolute paths — the same arrangement <c>tools/ui-sandbox/serve.ps1</c> uses.
    /// </summary>
    public async Task InitializeAsync()
    {
        var root = RepoRoot();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var provider = new PhysicalFileProvider(root);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = provider,
            ServeUnknownFileTypes = true,
        });

        var mapsPath = Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "maps");
        if (Directory.Exists(mapsPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(mapsPath),
                RequestPath = "/maps",
                ServeUnknownFileTypes = true,
            });
        }

        host = app;
        await app.StartAsync();
        baseUrl = app.Urls.First().TrimEnd('/');
    }

    public async Task DisposeAsync()
    {
        if (host is null) return;
        await host.StopAsync(TimeSpan.FromSeconds(10));
        host.Dispose();
    }

    [Fact]
    public async Task AdminUsers_EditingRetainsTheExistingStewardshipRole()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='admin-page']").ClickAsync();
        var frame = page.FrameLocator("#stage iframe");
        await Expect(frame.Locator("body")).ToHaveAttributeAsync("data-admin-sandbox-ready", "true");
        await frame.Locator("#userFilter").FillAsync("data");
        await Expect(frame.Locator("#userTableWrap")).ToContainTextAsync("data-steward");
        await frame.Locator("[data-action='edit-user'][data-id='2']").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "stewardship-role.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await frame.Locator("#newUserForm").ScreenshotAsync(new() { Path = path });
        await Expect(frame.Locator("#nu-role")).ToHaveValueAsync("DataSteward");
        await Expect(frame.Locator("#nu-role option[value='StewardshipManager']")).ToHaveCountAsync(1);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminUsers_FailedPasswordResetDoesNotAnnounceSuccess()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='admin-page']").ClickAsync();
        var frame = page.FrameLocator("#stage iframe");
        await Expect(frame.Locator("body")).ToHaveAttributeAsync("data-admin-sandbox-ready", "true");
        await frame.Locator("#userFilter").FillAsync("data");
        await Expect(frame.Locator("#userTableWrap")).ToContainTextAsync("data-steward");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('resetFailed')");
        await frame.Locator("[data-action='reset'][data-id='2']").ClickAsync();
        await frame.GetByLabel("Temporary password", new() { Exact = true }).FillAsync("Str0ng@Passw0rd!");
        await frame.GetByRole(AriaRole.Button, new() { Name = "Reset password", Exact = true }).ClickAsync();
        await Expect(frame.Locator("body")).ToContainTextAsync("The requested change was rejected.");
        await frame.Locator("body").EvaluateAsync("() => new Promise(requestAnimationFrame)");
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>(
            "() => window.__adminFeedbackMessages.filter(item => item.options.tone === 'success').length"));
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "reset-rejected.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = path });
        await Expect(frame.Locator("body")).Not.ToContainTextAsync("The temporary password was set.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminUsers_InitialLoadAndUnassignedEditPreserveRoles()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await OpenAdminUsersSandboxAsync(page);
        await Expect(frame.Locator("#userTableWrap")).ToContainTextAsync("unassigned");
        await frame.Locator("[data-action='edit-user'][data-id='3']").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        await Expect(frame.Locator("#nu-role")).ToHaveValueAsync("");
        await frame.Locator("#nu-email").FillAsync("changed@example.test");
        await frame.Locator("#nu-saveBtn").ClickAsync();
        await Expect(frame.Locator("#newUserForm")).ToBeHiddenAsync();
        var body = await frame.Locator("body").EvaluateAsync<JsonElement>(
            "() => window.__adminMock.calls.find(item => item.method === 'PUT').body");
        Assert.False(body.TryGetProperty("role", out _));
        Assert.Equal("changed@example.test", body.GetProperty("email").GetString());
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setUserRoles(2, ['DataSteward', 'Publisher'])");
        await frame.Locator("#usersRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#userTableWrap")).ToContainTextAsync("Publisher");
        await frame.Locator("[data-action='edit-user'][data-id='2']").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#nu-email").FillAsync("multiple@example.test");
        await frame.Locator("#nu-saveBtn").ClickAsync();
        await Expect(frame.Locator("#newUserForm")).ToBeHiddenAsync();
        await Expect(frame.Locator("#userTableWrap")).ToContainTextAsync("Publisher");
        var multipleBody = await frame.Locator("body").EvaluateAsync<JsonElement>(
            "() => window.__adminMock.calls.filter(item => item.method === 'PUT').at(-1).body");
        Assert.False(multipleBody.TryGetProperty("role", out _));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminUsers_PendingCreationIsSingleAndCannotCloseANewDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await OpenAdminUsersSandboxAsync(page);
        await frame.Locator("#newUserBtn").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#nu-username").FillAsync("new-steward");
        await frame.Locator("#nu-password").FillAsync("Str0ng@Passw0rd!");
        await frame.Locator("#nu-role").SelectOptionAsync("DataSteward");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#nu-saveBtn").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("() => document.getElementById('nu-saveBtn').click()");
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>(
            "() => window.__adminMock.calls.filter(item => item.method === 'POST' && item.route === '/api/admin/users').length"));
        await frame.Locator("#nu-cancelBtn").ClickAsync();
        await Expect(frame.Locator("#nu-password")).ToHaveValueAsync("");
        await frame.Locator("#newUserBtn").ClickAsync();
        await frame.Locator("#nu-username").FillAsync("next-draft");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.releaseWrites()");
        await Expect(frame.Locator("#userTableWrap")).ToContainTextAsync("new-steward");
        await Expect(frame.Locator("#newUserForm")).ToBeVisibleAsync();
        await Expect(frame.Locator("#nu-username")).ToHaveValueAsync("next-draft");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminUsers_UnconfirmedCreationRequiresSuccessfulExplicitRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await OpenAdminUsersSandboxAsync(page);
        await frame.Locator("#newUserBtn").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#nu-username").FillAsync("uncertain-user");
        await frame.Locator("#nu-password").FillAsync("Str0ng@Passw0rd!");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('invalidReceipt')");
        await frame.Locator("#nu-saveBtn").ClickAsync();
        await Expect(frame.Locator("#usersMutationStatus")).ToContainTextAsync("unconfirmed");
        await Expect(frame.Locator("#nu-saveBtn")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('usersFailed')");
        await frame.Locator("#usersRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#userTableWrap")).ToContainTextAsync("Failed to load users.");
        await Expect(frame.Locator("#nu-saveBtn")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#userFilter").FillAsync("uncertain");
        await Expect(frame.Locator("#userTableWrap")).ToContainTextAsync("uncertain-user");
        await Expect(frame.Locator("#nu-saveBtn")).ToBeDisabledAsync();
        await frame.Locator("#usersRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>(
            "() => window.__adminMock.calls.filter(item => item.method === 'POST' && item.route === '/api/admin/users').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminUsers_FilterOrderingCannotRestoreAnOlderCatalog()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await OpenAdminUsersSandboxAsync(page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextRead()");
        await frame.Locator("#userFilter").FillAsync("data");
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingReads ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("#userFilter").FillAsync("unassigned");
        await Expect(frame.Locator("#userTableWrap")).ToContainTextAsync("unassigned");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseReads(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#userTableWrap")).Not.ToContainTextAsync("data-steward");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("rolesFailed")]
    [InlineData("rolesInvalid")]
    public async Task AdminUsers_UnavailableRolesKeepDraftsAndBlockSaving(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await OpenAdminUsersSandboxAsync(page);
        await frame.Locator("body").EvaluateAsync("(_, mode) => window.__adminMock.setFailure(mode)", failure);
        await frame.Locator("#newUserBtn").ClickAsync();
        await frame.Locator("#nu-username").FillAsync("retained-draft");
        await Expect(frame.Locator("#nu-error")).ToContainTextAsync("Roles could not be loaded.");
        await Expect(frame.Locator("#nu-saveBtn")).ToBeDisabledAsync();
        await Expect(frame.Locator("#nu-username")).ToHaveValueAsync("retained-draft");
        await frame.Locator("#nu-password").FillAsync("Retained@TestPassword!");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#nu-reloadRolesBtn").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        await Expect(frame.Locator("#nu-username")).ToHaveValueAsync("retained-draft");
        await Expect(frame.Locator("#nu-password")).ToHaveValueAsync("Retained@TestPassword!");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminUsers_DisposalClearsPasswordsAndRejectsLateMutationRendering()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await OpenAdminUsersSandboxAsync(page);
        await frame.Locator("#newUserBtn").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#nu-username").FillAsync("disposed-create");
        await frame.Locator("#nu-password").FillAsync("Str0ng@Passw0rd!");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#nu-saveBtn").ClickAsync();
        await Expect(frame.Locator("#nu-saveBtn")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.dispatchEvent(new PageTransitionEvent('pagehide'))");
        await Expect(frame.Locator("#nu-password")).ToHaveValueAsync("");
        await Expect(frame.Locator("#newUserForm")).ToBeHiddenAsync();
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseWrites(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#userTableWrap")).Not.ToContainTextAsync("disposed-create");
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminFeedbackMessages.length"));
        Assert.Empty(session.PageErrors);
    }

    private async Task<IFrameLocator> OpenAdminUsersSandboxAsync(IPage page)
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='admin-page']").ClickAsync();
        var frame = page.FrameLocator("#stage iframe");
        await Expect(frame.Locator("body")).ToHaveAttributeAsync("data-admin-sandbox-ready", "true");
        await Expect(frame.Locator("#userTableWrap")).ToHaveAttributeAsync("data-users-state", "ready");
        return frame;
    }

    [Fact]
    public async Task AdminGroups_SupersededMembershipReadCannotRenderUnderAnotherGroup()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await OpenAdminUsersSandboxAsync(page);
        await frame.Locator(".admin-tab[data-tab='groups']").ClickAsync();
        await Expect(frame.Locator("#groupTableWrap")).ToContainTextAsync("Finance");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextMemberRead()");
        await frame.Locator("[data-action='members'][data-id='10']").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingMembers ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("[data-action='members'][data-id='11']").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("unassigned");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseMembers(); await new Promise(requestAnimationFrame); }");
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "groups-member-ordering.png");
        await frame.Locator("#membersPanel").ScreenshotAsync(new() { Path = path });
        await Expect(frame.Locator("#memberGroupName")).ToHaveTextAsync("Operations");
        await Expect(frame.Locator("#memberTableWrap")).Not.ToContainTextAsync("data-steward");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminGroups_RejectedMembershipWriteKeepsItsPicker()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await OpenAdminUsersSandboxAsync(page);
        await frame.Locator(".admin-tab[data-tab='groups']").ClickAsync();
        await Expect(frame.Locator("#groupTableWrap")).ToContainTextAsync("Finance");
        await frame.Locator("[data-action='members'][data-id='10']").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("data-steward");
        await frame.Locator("#addMemberBtn").ClickAsync();
        await Expect(frame.Locator("#addMemberSelect option[value='1']")).ToHaveCountAsync(1);
        await frame.Locator("#addMemberSelect").SelectOptionAsync("1");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('writeFailed')");
        await frame.Locator("#am-saveBtn").ClickAsync();
        await Expect(frame.Locator("body")).ToContainTextAsync("The requested change was rejected.");
        await frame.Locator("body").EvaluateAsync("() => new Promise(requestAnimationFrame)");
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "groups-picker-rejected.png");
        await frame.Locator("#membersPanel").ScreenshotAsync(new() { Path = path });
        await Expect(frame.Locator("#addMemberForm")).ToBeVisibleAsync();
        await Expect(frame.Locator("#addMemberSelect")).ToHaveValuesAsync(["1"]);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminGroups_LateConfirmationKeepsItsOriginalGroup()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminGroupsSandboxAsync(session.Page);
        await frame.Locator("[data-action='members'][data-id='10']").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("data-steward");
        await frame.Locator("#memberTableWrap [data-select-id='2']").CheckAsync();
        await frame.Locator("body").EvaluateAsync("() => { window.ETLSQLFeedback.confirm = () => new Promise(resolve => { window.__finishGroupConfirm = resolve; }); }");
        await frame.Locator("#membersRemoveBtn").ClickAsync();
        Assert.True(await frame.Locator("body").EvaluateAsync<bool>("() => !!window.__finishGroupConfirm"));
        await frame.Locator("[data-action='members'][data-id='11']").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("unassigned");
        await frame.Locator("body").EvaluateAsync("() => window.__finishGroupConfirm(true)");
        await Expect(frame.Locator("#ng-saveBtn")).ToBeEnabledAsync();
        await Expect(frame.Locator("#groupTableWrap")).ToHaveAttributeAsync("data-groups-state", "ready");
        await Expect(frame.Locator("#memberGroupName")).ToHaveTextAsync("Operations");
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("unassigned");
        Assert.Equal("/api/admin/groups/10/members/bulk-remove", await frame.Locator("body").EvaluateAsync<string>(
            "() => window.__adminMock.calls.find(item => item.method === 'POST' && item.route.endsWith('/bulk-remove')).route"));
        await frame.Locator("[data-action='members'][data-id='10']").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("No members.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminGroups_PendingCreationIsSingleAndCannotCloseANewDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminGroupsSandboxAsync(session.Page);
        await frame.Locator("#newGroupBtn").ClickAsync();
        await frame.Locator("#ng-name").FillAsync("First group");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#ng-saveBtn").EvaluateAsync("button => { button.click(); button.click(); }");
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingWrites ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("#ng-cancelBtn").ClickAsync();
        await frame.Locator("#newGroupBtn").ClickAsync();
        await frame.Locator("#ng-name").FillAsync("Later draft");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.releaseWrites()");
        await Expect(frame.Locator("#groupTableWrap")).ToContainTextAsync("First group");
        await Expect(frame.Locator("#newGroupForm")).ToBeVisibleAsync();
        await Expect(frame.Locator("#ng-name")).ToHaveValueAsync("Later draft");
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>(
            "() => window.__adminMock.calls.filter(item => item.method === 'POST' && item.route === '/api/admin/groups').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminGroups_PickerSearchCannotRestoreAnOlderDirectory()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminGroupsSandboxAsync(session.Page);
        await frame.Locator("[data-action='members'][data-id='10']").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToHaveAttributeAsync("data-members-state", "ready");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextRead()");
        await frame.Locator("#addMemberBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingReads ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("#addMemberSearch").FillAsync("unassigned");
        await Expect(frame.Locator("#addMemberSelect option")).ToHaveCountAsync(1);
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseReads(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#addMemberSelect option")).ToHaveCountAsync(1);
        await Expect(frame.Locator("#addMemberSelect")).Not.ToContainTextAsync("data-steward");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("membersFailed")]
    [InlineData("membersInvalid")]
    public async Task AdminGroups_UnavailableMembershipCannotRetainMutationActions(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminGroupsSandboxAsync(session.Page);
        await frame.Locator("[data-action='members'][data-id='10']").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("data-steward");
        await frame.Locator("#addMemberBtn").ClickAsync();
        await Expect(frame.Locator("#addMemberSelect option[value='1']")).ToHaveCountAsync(1);
        await frame.Locator("#addMemberSelect").SelectOptionAsync("1");
        await frame.Locator("body").EvaluateAsync("(_, mode) => window.__adminMock.setFailure(mode)", failure);
        await frame.Locator("#membersRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToHaveAttributeAsync("data-members-state", "failed");
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("Group members could not be loaded.");
        await Expect(frame.Locator("#memberTableWrap")).Not.ToContainTextAsync("No members.");
        await Expect(frame.Locator("#am-saveBtn")).ToBeDisabledAsync();
        await Expect(frame.Locator("#addMemberBtn")).ToBeDisabledAsync();
        await Expect(frame.Locator("#addMemberSelect")).ToHaveValuesAsync(["1"]);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminGroups_UnconfirmedMembershipRequiresSuccessfulExplicitRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminGroupsSandboxAsync(session.Page);
        await frame.Locator("[data-action='members'][data-id='10']").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("data-steward");
        await frame.Locator("#addMemberBtn").ClickAsync();
        await Expect(frame.Locator("#addMemberSelect option[value='1']")).ToHaveCountAsync(1);
        await frame.Locator("#addMemberSelect").SelectOptionAsync("1");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('groupsInvalidReceipt')");
        await frame.Locator("#am-saveBtn").ClickAsync();
        await Expect(frame.Locator("#groupsMutationStatus")).ToBeVisibleAsync();
        await Expect(frame.Locator("#addMemberForm")).ToBeVisibleAsync();
        await frame.Locator("#groupFilter").FillAsync("Finance");
        await Expect(frame.Locator("#groupTableWrap")).ToHaveAttributeAsync("data-groups-state", "ready");
        await Expect(frame.Locator("#am-saveBtn")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('membersFailed')");
        await frame.Locator("#groupsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToHaveAttributeAsync("data-members-state", "failed");
        await Expect(frame.Locator("#groupsMutationStatus")).ToBeVisibleAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#groupsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#groupsMutationStatus")).ToBeHiddenAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToContainTextAsync("sandbox-admin");
        await Expect(frame.Locator("#am-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#am-saveBtn").ClickAsync();
        await Expect(frame.Locator("#addMemberForm")).ToBeHiddenAsync();
        await Expect(frame.Locator("body")).ToContainTextAsync("0 users added. 1 users were not added");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminGroups_DisposalRejectsLateMembershipAndMutationRendering()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminGroupsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextMemberRead()");
        await frame.Locator("[data-action='members'][data-id='10']").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingMembers ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("#newGroupBtn").ClickAsync();
        await frame.Locator("#ng-name").FillAsync("Disposed group");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#ng-saveBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingWrites ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("body").EvaluateAsync("async () => { window.dispatchEvent(new Event('pagehide')); window.__adminMock.releaseMembers(); window.__adminMock.releaseWrites(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#newGroupForm")).ToBeHiddenAsync();
        await Expect(frame.Locator("#groupTableWrap")).Not.ToContainTextAsync("Disposed group");
        await Expect(frame.Locator("#memberTableWrap")).Not.ToContainTextAsync("data-steward");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminGroups_RefreshResolvesAnUnconfirmedDeletionOfTheSelectedGroup()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminGroupsSandboxAsync(session.Page);
        await frame.Locator("#newGroupBtn").ClickAsync();
        await frame.Locator("#ng-name").FillAsync("Unconfirmed deletion");
        await frame.Locator("#ng-saveBtn").ClickAsync();
        await Expect(frame.Locator("#groupTableWrap")).ToContainTextAsync("Unconfirmed deletion");
        await Expect(frame.Locator("#ng-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("[data-action='members'][data-id='12']").ClickAsync();
        await Expect(frame.Locator("#memberTableWrap")).ToHaveAttributeAsync("data-members-state", "ready");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('groupsDeleteUnconfirmed')");
        await frame.Locator("#groupTableWrap [data-action='delete'][data-id='12']").ClickAsync();
        await frame.GetByRole(AriaRole.Button, new() { Name = "Delete group", Exact = true }).ClickAsync();
        await Expect(frame.Locator("#groupsMutationStatus")).ToBeVisibleAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#groupsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#groupsMutationStatus")).ToBeHiddenAsync();
        await Expect(frame.Locator("#membersPanel")).ToBeHiddenAsync();
        await Expect(frame.Locator("#groupTableWrap")).Not.ToContainTextAsync("Unconfirmed deletion");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_NestedFoldersRemainVisibleAndAvailableAsParents()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("#newFolderBtn").ClickAsync();
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "folders-hierarchy.png");
        await frame.Locator("#panel-folders").ScreenshotAsync(new() { Path = path });
        await Expect(frame.Locator("#folderTableWrap")).ToContainTextAsync("/Reports/Child");
        await Expect(frame.Locator("#nf-parent option[value='21']")).ToHaveCountAsync(1);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_SupersededPermissionReadCannotRenderUnderAnotherFolder()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextAclRead()");
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingAcls ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("[data-action='acl'][data-id='30']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToContainTextAsync("Operations");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseAcls(); await new Promise(requestAnimationFrame); }");
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "folders-acl-ordering.png");
        await frame.Locator("#aclPanel").ScreenshotAsync(new() { Path = path });
        await Expect(frame.Locator("#aclFolderName")).ToHaveTextAsync("Operations");
        await Expect(frame.Locator("#aclTableWrap")).Not.ToContainTextAsync("Finance");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_GrantOpensAnUsableEditor()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToContainTextAsync("Finance");
        await frame.Locator("#addAclBtn").ClickAsync();
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "folders-grant.png");
        await frame.Locator("#aclPanel").ScreenshotAsync(new() { Path = path });
        await Expect(frame.Locator("#addAclForm")).ToBeVisibleAsync();
        await Expect(frame.Locator("#aclGroupSelect option")).Not.ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_PermissionsShowTheirNamesAndPreserveStorageValues()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToContainTextAsync("Finance");
        await Expect(frame.Locator("#aclTableWrap")).ToContainTextAsync("Manage");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_RejectedGrantKeepsItsSelectionAndPermissionForRetry()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        await frame.Locator("#addAclBtn").ClickAsync();
        await Expect(frame.Locator("#aclGroupSelect option[value='10']")).ToHaveCountAsync(1);
        await frame.Locator("#aclGroupSelect").SelectOptionAsync("10");
        await frame.Locator("#aclPermSelect").SelectOptionAsync("3");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('writeFailed')");
        await frame.Locator("#acl-saveBtn").ClickAsync();
        await Expect(frame.Locator("#acl-error")).ToContainTextAsync("rejected");
        await Expect(frame.Locator("#addAclForm")).ToBeVisibleAsync();
        await Expect(frame.Locator("#aclGroupSelect")).ToHaveValueAsync("10");
        await Expect(frame.Locator("#aclPermSelect")).ToHaveValueAsync("3");
        await Expect(frame.Locator("#acl-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#acl-saveBtn").ClickAsync();
        await Expect(frame.Locator("#addAclForm")).ToBeHiddenAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToContainTextAsync("Author");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_CatalogRefreshCannotSilentlyUpgradeAnOpenedGrant()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        await frame.Locator("#addAclBtn").ClickAsync();
        await Expect(frame.Locator("#aclGroupSelect option[value='10']")).ToHaveCountAsync(1);
        await frame.Locator("#aclGroupSelect").SelectOptionAsync("10");
        await frame.Locator("#aclPermSelect").SelectOptionAsync("3");
        await frame.Locator("body").EvaluateAsync("""
            async () => {
              const response = await window.__adminMock.fetch('/api/folders/20', { method: 'PUT',
                headers: { 'If-Match': '"1"' }, body: JSON.stringify({ name: 'Reports external', parentId: null }) });
              if (response.status !== 200) throw new Error('External update failed');
            }
            """);
        await frame.Locator("#foldersRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#aclFolderName")).ToHaveTextAsync("Reports external");
        await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        await frame.Locator("#acl-saveBtn").ClickAsync();
        await Expect(frame.Locator("#acl-error")).ToContainTextAsync("changed");
        Assert.Equal("\"1\"", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminMock.calls.find(item => item.route === '/api/folders/20/acl' && item.method === 'POST').ifMatch"));
        await Expect(frame.Locator("#aclPermSelect")).ToHaveValueAsync("3");
        await Expect(frame.Locator("#foldersMutationStatus")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_LateRevokeConfirmationKeepsItsOriginalFolder()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        await frame.Locator("body").EvaluateAsync("() => { window.ETLSQLFeedback.confirm = () => new Promise(resolve => { window.__folderConfirm = resolve; }); }");
        await frame.Locator("#aclTableWrap [data-gid='10']").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__folderConfirm ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("[data-action='acl'][data-id='30']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToContainTextAsync("Operations");
        await frame.Locator("body").EvaluateAsync("() => window.__folderConfirm(true)");
        await Expect(frame.Locator("#nf-saveBtn")).ToBeEnabledAsync();
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(item => item.route === '/api/folders/20/acl/10' && item.method === 'DELETE').length"));
        await Expect(frame.Locator("#aclFolderName")).ToHaveTextAsync("Operations");
        await Expect(frame.Locator("#aclTableWrap")).ToContainTextAsync("Operations");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_PendingCreationIsSingleAndCannotCloseANewDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("#newFolderBtn").ClickAsync();
        await frame.Locator("#nf-name").FillAsync("First folder");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#nf-saveBtn").ClickAsync();
        await WaitForAdminWriteAsync(frame);
        await frame.Locator("#nf-saveBtn").EvaluateAsync("button => button.click()");
        await frame.Locator("#newFolderBtn").ClickAsync();
        await frame.Locator("#nf-name").FillAsync("Keep this folder draft");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.releaseWrites()");
        await Expect(frame.Locator("#folderTableWrap")).ToContainTextAsync("First folder");
        await Expect(frame.Locator("#nf-saveBtn")).ToBeEnabledAsync();
        await Expect(frame.Locator("#newFolderForm")).ToBeVisibleAsync();
        await Expect(frame.Locator("#nf-name")).ToHaveValueAsync("Keep this folder draft");
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(item => item.route === '/api/folders' && item.method === 'POST').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_UnconfirmedGrantRequiresSuccessfulExplicitRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        await frame.Locator("#addAclBtn").ClickAsync();
        await Expect(frame.Locator("#aclGroupSelect option[value='10']")).ToHaveCountAsync(1);
        await frame.Locator("#aclGroupSelect").SelectOptionAsync("10");
        await frame.Locator("#aclPermSelect").SelectOptionAsync("3");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('foldersInvalidReceipt')");
        await frame.Locator("#acl-saveBtn").ClickAsync();
        await Expect(frame.Locator("#foldersMutationStatus")).ToBeVisibleAsync();
        await Expect(frame.Locator("#acl-saveBtn")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('aclFailed')");
        await frame.Locator("#aclRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "failed");
        await Expect(frame.Locator("#foldersMutationStatus")).ToBeVisibleAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#aclRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#foldersMutationStatus")).ToBeHiddenAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToContainTextAsync("Author");
        await Expect(frame.Locator("#acl-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#acl-saveBtn").ClickAsync();
        await Expect(frame.Locator("#addAclForm")).ToBeHiddenAsync();
        Assert.Equal("\"2\"", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminMock.calls.filter(item => item.route === '/api/folders/20/acl' && item.method === 'POST').at(-1).ifMatch"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("aclFailed")]
    [InlineData("aclInvalid")]
    [InlineData("foldersFailed")]
    [InlineData("foldersInvalid")]
    public async Task AdminFolders_UnavailableCatalogsCannotRetainMutationActions(string mode)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        await frame.Locator("#newFolderBtn").ClickAsync();
        await frame.Locator("#nf-name").FillAsync("Retain this draft");
        await frame.Locator("body").EvaluateAsync("(_, value) => window.__adminMock.setFailure(value)", mode);
        await frame.Locator("#foldersRefreshBtn").ClickAsync();
        if (mode.StartsWith("acl", StringComparison.Ordinal))
        {
            await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "failed");
            await Expect(frame.Locator("#aclTableWrap [data-gid]")).ToHaveCountAsync(0);
            await Expect(frame.Locator("#addAclBtn")).ToBeDisabledAsync();
        }
        else
        {
            await Expect(frame.Locator("#folderTableWrap")).ToHaveAttributeAsync("data-folders-state", "failed");
            await Expect(frame.Locator("#nf-saveBtn")).ToBeDisabledAsync();
            await Expect(frame.Locator("#folderTableWrap")).Not.ToContainTextAsync("No folders.");
        }
        await Expect(frame.Locator("#nf-name")).ToHaveValueAsync("Retain this draft");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_GroupSearchCannotRestoreAnOlderCatalog()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        await frame.Locator("#addAclBtn").ClickAsync();
        await Expect(frame.Locator("#aclGroupSelect option[value='10']")).ToHaveCountAsync(1);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextRead()");
        await frame.Locator("#aclGroupSearch").FillAsync("Finance");
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingReads ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("#aclGroupSearch").FillAsync("Operations");
        await Expect(frame.Locator("#aclGroupSelect option[value='11']")).ToHaveCountAsync(1);
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseReads(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#aclGroupSelect option[value='10']")).ToHaveCountAsync(0);
        await Expect(frame.Locator("#aclGroupSearch")).ToHaveValueAsync("Operations");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_DisposalRejectsLatePermissionAndMutationRendering()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#newFolderBtn").ClickAsync();
        await frame.Locator("#nf-name").FillAsync("Disposed folder");
        await frame.Locator("#nf-saveBtn").ClickAsync();
        await WaitForAdminWriteAsync(frame);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextAclRead()");
        await frame.Locator("[data-action='acl'][data-id='20']").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingAcls ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("body").EvaluateAsync("async () => { window.dispatchEvent(new Event('pagehide')); window.__adminMock.releaseAcls(); window.__adminMock.releaseWrites(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#newFolderForm")).ToBeHiddenAsync();
        await Expect(frame.Locator("#folderTableWrap")).Not.ToContainTextAsync("Disposed folder");
        await Expect(frame.Locator("#aclTableWrap")).Not.ToContainTextAsync("Finance");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminFolders_RefreshResolvesUnconfirmedCascadeDeletionOfTheSelectedChild()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminFoldersSandboxAsync(session.Page);
        await frame.Locator("[data-action='acl'][data-id='21']").ClickAsync();
        await Expect(frame.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('foldersDeleteUnconfirmed')");
        await frame.Locator("#folderTableWrap [data-action='delete'][data-id='20']").ClickAsync();
        await frame.GetByRole(AriaRole.Button, new() { Name = "Delete folder", Exact = true }).ClickAsync();
        await Expect(frame.Locator("#foldersMutationStatus")).ToBeVisibleAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#foldersRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#foldersMutationStatus")).ToBeHiddenAsync();
        await Expect(frame.Locator("#aclPanel")).ToBeHiddenAsync();
        await Expect(frame.Locator("#folderTableWrap")).Not.ToContainTextAsync("/Reports");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_SupersededCatalogCannotReplaceNewerRows()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextReportRead()");
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingReports ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setReportVersion(40, 2, 'Finance current')");
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#reportsTableWrap")).ToContainTextAsync("Finance current");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseReports(); await new Promise(requestAnimationFrame); }");
        await frame.Locator("#panel-reports").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "reports-ordering-after.png") });
        await Expect(frame.Locator("#reportsTableWrap")).ToContainTextAsync("Finance current");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_RefreshCannotUpgradeAnOpenedEditorVersion()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-report'][data-id='40']").ClickAsync();
        await Expect(frame.Locator("#pr-folder option[value='20']")).ToHaveCountAsync(1);
        await frame.Locator("#pr-desc").FillAsync("Private report draft");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setReportVersion(40, 2, 'Finance external')");
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#reportsTableWrap")).ToContainTextAsync("Finance external");
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.calls.some(item => item.route === '/api/reports/40' && item.method === 'PUT') ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("#panel-reports").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "reports-opened-version-after.png") });
        Assert.Equal("\"1\"", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminMock.calls.find(item => item.route === '/api/reports/40' && item.method === 'PUT').ifMatch"));
        await Expect(frame.Locator("#pr-desc")).ToHaveValueAsync("Private report draft");
        await Expect(frame.Locator("#publishReportForm")).ToBeVisibleAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_LateValidationCannotReplaceAnotherPathResult()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("#openPublishBtn").ClickAsync();
        await Expect(frame.Locator("#pr-folder option[value='20']")).ToHaveCountAsync(1);
        await frame.Locator("#pr-path").FillAsync("reports/finance.rptsql");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextValidation()");
        await frame.Locator("#pr-validateBtn").ClickAsync();
        await WaitForAdminValidationAsync(frame);
        await frame.Locator("#pr-path").FillAsync("reports/operations.rptsql");
        await frame.Locator("#pr-validateBtn").ClickAsync();
        await Expect(frame.Locator("#pr-validation")).ToContainTextAsync(new string('b', 64));
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseValidations(); await new Promise(requestAnimationFrame); }");
        await frame.Locator("#publishReportForm").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "reports-validation-after.png") });
        await Expect(frame.Locator("#pr-validation")).ToContainTextAsync(new string('b', 64));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_CancelledSaveCannotCloseOrPublishOverANewDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-report'][data-id='40']").ClickAsync();
        await Expect(frame.Locator("#pr-folder option[value='20']")).ToHaveCountAsync(1);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextValidation()");
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await WaitForAdminValidationAsync(frame);
        await frame.Locator("#pr-cancelBtn").ClickAsync();
        await frame.Locator("#openPublishBtn").ClickAsync();
        await frame.Locator("#pr-name").FillAsync("Keep this report draft");
        await frame.Locator("#pr-path").FillAsync("reports/operations.rptsql");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseValidations(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#panel-reports").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "reports-draft-after.png") });
        await Expect(frame.Locator("#publishReportForm")).ToBeVisibleAsync();
        await Expect(frame.Locator("#pr-name")).ToHaveValueAsync("Keep this report draft");
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(item => item.route === '/api/reports/40' && item.method === 'PUT').length"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("reportsFailed")]
    [InlineData("reportsInvalid")]
    [InlineData("sessionFailed")]
    [InlineData("sessionInvalid")]
    public async Task AdminReports_UnavailableCatalogClearsRowsAndBlocksSaving(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-report'][data-id='40']").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#pr-desc").FillAsync("Retained report draft");
        await frame.Locator("body").EvaluateAsync("(_, value) => window.__adminMock.setFailure(value)", failure);
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#reportsTableWrap")).ToHaveAttributeAsync("data-reports-state", "failed");
        await Expect(frame.Locator("#reportsTableWrap")).Not.ToContainTextAsync("Finance report");
        await Expect(frame.Locator("#pr-saveBtn")).ToBeDisabledAsync();
        await Expect(frame.Locator("#pr-desc")).ToHaveValueAsync("Retained report draft");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#reportsTableWrap")).ToHaveAttributeAsync("data-reports-state", "ready");
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("scriptsFailed")]
    [InlineData("scriptsInvalid")]
    public async Task AdminReports_ScriptPickerRetriesAndHonorsFilteringBeforeTheAnswer(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("#openPublishBtn").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#pr-path").FillAsync("keep-this.rptsql");
        await frame.Locator("body").EvaluateAsync("(_, value) => window.__adminMock.setFailure(value)", failure);
        await frame.Locator("#pr-browseBtn").ClickAsync();
        await Expect(frame.Locator("#sb-list")).ToContainTextAsync("Scripts could not be loaded.");
        await Expect(frame.Locator("#sb-list [data-path]")).ToHaveCountAsync(0);
        await frame.Locator("body").EvaluateAsync("() => { window.__adminMock.setFailure('healthy'); window.__adminMock.pauseNextScriptRead(); }");
        await frame.Locator("#sb-retryBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingScripts ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("#sb-filter").FillAsync("operations");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.releaseScripts()");
        await Expect(frame.Locator("#sb-list [data-path]")).ToHaveCountAsync(1);
        await Expect(frame.Locator("#sb-list")).Not.ToContainTextAsync("finance.rptsql");
        await frame.Locator("#scriptBrowserModal").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "reports-script-picker-after.png") });
        await frame.Locator("#sb-list [data-path]").FocusAsync();
        await frame.Locator("#sb-list [data-path]").PressAsync("Enter");
        await Expect(frame.Locator("#pr-path")).ToHaveValueAsync("reports/operations.rptsql");
        await Expect(frame.Locator("#pr-path")).ToBeFocusedAsync();
        await Expect(frame.Locator("#scriptBrowserModal")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_AnEditedPendingPublicationUsesItsCompletedIdentityForTheNextSave()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("#openPublishBtn").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#pr-name").FillAsync("A single publication");
        await frame.Locator("#pr-path").FillAsync("reports/finance.rptsql");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await WaitForAdminWriteAsync(frame);
        await frame.Locator("#pr-saveBtn").EvaluateAsync("button => button.click()");
        await frame.Locator("#pr-desc").FillAsync("Edit made while publishing");
        await frame.Locator("#pr-id").EvaluateAsync("input => { input.value = '999'; }");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.releaseWrites()");
        await Expect(frame.Locator("#pr-saveBtn")).ToHaveTextAsync("Save Changes");
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await Expect(frame.Locator("#pr-desc")).ToHaveValueAsync("Edit made while publishing");
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await Expect(frame.Locator("#publishReportForm")).ToBeHiddenAsync();
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.route === '/api/reports' && call.method === 'POST').length"));
        Assert.Equal("\"1\"", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminMock.calls.find(call => call.route === '/api/reports/42' && call.method === 'PUT').ifMatch"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("reportsInvalidReceipt")]
    [InlineData("reportsServerFailure")]
    public async Task AdminReports_UnconfirmedUpdateKeepsTheDraftAndRequiresSuccessfulRefresh(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-report'][data-id='40']").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#pr-desc").FillAsync("Unconfirmed report draft");
        await frame.Locator("body").EvaluateAsync("(_, value) => window.__adminMock.setFailure(value)", failure);
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await Expect(frame.Locator("#reportsMutationStatus")).ToBeVisibleAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeDisabledAsync();
        await Expect(frame.Locator("#pr-desc")).ToHaveValueAsync("Unconfirmed report draft");
        await frame.Locator("#panel-reports").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "reports-unconfirmed-after.png") });
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminFeedbackMessages.filter(message => message.options?.tone === 'success').length"));
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('reportsFailed')");
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#reportsTableWrap")).ToHaveAttributeAsync("data-reports-state", "failed");
        await Expect(frame.Locator("#reportsMutationStatus")).ToBeVisibleAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#reportsMutationStatus")).ToBeHiddenAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await Expect(frame.Locator("#pr-error")).ToContainTextAsync("The report has changed.");
        Assert.Equal("\"1\"", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminMock.calls.filter(call => call.route === '/api/reports/40' && call.method === 'PUT').at(-1).ifMatch"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_UnconfirmedPublicationCannotCreateADuplicateAfterRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("#openPublishBtn").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#pr-name").FillAsync("Unconfirmed publication");
        await frame.Locator("#pr-path").FillAsync("reports/finance.rptsql");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('reportsInvalidReceipt')");
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await Expect(frame.Locator("#reportsMutationStatus")).ToBeVisibleAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#reportsMutationStatus")).ToBeHiddenAsync();
        await Expect(frame.Locator("#reportsTableWrap")).ToContainTextAsync("Unconfirmed publication");
        await Expect(frame.Locator("#pr-saveBtn")).ToBeDisabledAsync();
        await Expect(frame.Locator("#pr-error")).ToContainTextAsync("Reopen a published report");
        await Expect(frame.Locator("#pr-name")).ToHaveValueAsync("Unconfirmed publication");
        await frame.Locator("#pr-cancelBtn").ClickAsync();
        await frame.Locator("[data-action='edit-report'][data-id='42']").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.route === '/api/reports' && call.method === 'POST').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_AcceptedDeleteRequiresRefreshAndUsesCascadeWithItsOpenedVersion()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('reportsDeleteUnconfirmed')");
        await frame.Locator("[data-action='delete-report'][data-id='40']").ClickAsync();
        await frame.GetByRole(AriaRole.Button, new() { Name = "Delete report", Exact = true }).ClickAsync();
        await Expect(frame.Locator("#reportsMutationStatus")).ToBeVisibleAsync();
        Assert.Equal("?cascade=true", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminMock.calls.find(call => call.route === '/api/reports/40' && call.method === 'DELETE').query"));
        Assert.Equal("\"1\"", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminMock.calls.find(call => call.route === '/api/reports/40' && call.method === 'DELETE').ifMatch"));
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#reportsMutationStatus")).ToBeHiddenAsync();
        await Expect(frame.Locator("#reportsTableWrap")).Not.ToContainTextAsync("Finance report");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_DisposalDuringAPreflightCannotWriteOrRestoreTheDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-report'][data-id='40']").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextValidation()");
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await WaitForAdminValidationAsync(frame);
        await frame.Locator("body").EvaluateAsync("async () => { window.dispatchEvent(new PageTransitionEvent('pagehide')); window.__adminMock.releaseValidations(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#publishReportForm")).ToBeHiddenAsync();
        await Expect(frame.Locator("#pr-path")).ToHaveValueAsync("");
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.route === '/api/reports/40' && call.method === 'PUT').length"));
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminFeedbackMessages.length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_InlineFolderCreationRetainsTheReportDraftAndSelectsTheNestedDestination()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("#openPublishBtn").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#pr-name").FillAsync("Nested destination draft");
        await frame.Locator("#pr-folder").SelectOptionAsync("21");
        await frame.Locator("#pr-newFolderToggle").ClickAsync();
        await frame.Locator("#pr-newFolderName").FillAsync("Published");
        await frame.Locator("#pr-newFolderCreate").ClickAsync();
        await Expect(frame.Locator("#pr-newFolderRow")).ToBeHiddenAsync();
        await Expect(frame.Locator("#pr-folder option:checked")).ToHaveTextAsync("/Reports/Child/Published");
        await Expect(frame.Locator("#pr-name")).ToHaveValueAsync("Nested destination draft");
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        Assert.Equal(21, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.find(call => call.route === '/api/folders' && call.method === 'POST').body.parentId"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminReports_ACompletedWriteCannotCloseOrRetargetANewerPublicationDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-report'][data-id='40']").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await WaitForAdminWriteAsync(frame);
        await frame.Locator("#pr-cancelBtn").ClickAsync();
        await frame.Locator("#openPublishBtn").ClickAsync();
        await frame.Locator("#pr-name").FillAsync("A newer publication draft");
        await frame.Locator("#pr-path").FillAsync("reports/operations.rptsql");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.releaseWrites()");
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await Expect(frame.Locator("#publishReportForm")).ToBeVisibleAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToHaveTextAsync("Publish");
        await Expect(frame.Locator("#pr-name")).ToHaveValueAsync("A newer publication draft");
        await frame.Locator("#pr-saveBtn").ClickAsync();
        await Expect(frame.Locator("#publishReportForm")).ToBeHiddenAsync();
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.route === '/api/reports/40' && call.method === 'PUT').length"));
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.route === '/api/reports' && call.method === 'POST').length"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("foldersFailed")]
    [InlineData("foldersInvalid")]
    public async Task AdminReports_UnavailableDestinationsPreserveSelectionAndPreventPublication(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-report'][data-id='40']").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await frame.Locator("#pr-desc").FillAsync("Retained destination draft");
        await frame.Locator("body").EvaluateAsync("(_, value) => window.__adminMock.setFailure(value)", failure);
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#pr-error")).ToContainTextAsync("Destination folders could not be loaded.");
        await Expect(frame.Locator("#pr-folder")).ToHaveValueAsync("20");
        await Expect(frame.Locator("#pr-folder")).ToBeDisabledAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeDisabledAsync();
        await Expect(frame.Locator("#pr-desc")).ToHaveValueAsync("Retained destination draft");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('healthy')");
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await Expect(frame.Locator("#pr-folder")).ToHaveValueAsync("20");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("CatalogOnly", true)]
    [InlineData("Disabled", false)]
    public async Task AdminReports_DeploymentPolicyControlsSourceExposureAndPublication(string mode, bool studioEnabled)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminReportsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("(_, value) => window.__adminMock.setStudioSession(value.mode, value.studioEnabled ? ['StudioAccess'] : [])",
            new { mode, studioEnabled });
        await frame.Locator("#reportsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#reportsTableWrap")).Not.ToContainTextAsync("Script Path");
        await Expect(frame.Locator("#reportsTableWrap")).Not.ToContainTextAsync("reports/finance.rptsql");
        if (studioEnabled)
        {
            await Expect(frame.Locator("#openPublishBtn")).ToHaveTextAsync("Open Studio");
            await Expect(frame.Locator("#reportsTableWrap [data-action='edit-report']")).ToHaveTextAsync(new[] { "Open in Studio", "Open in Studio" });
        }
        else
        {
            await Expect(frame.Locator("#openPublishBtn")).ToBeHiddenAsync();
            await Expect(frame.Locator("#reportsTableWrap [data-action='edit-report']")).ToHaveCountAsync(0);
        }
        await Expect(frame.Locator("#publishReportForm")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminAudit_PaginationReachesEvidenceBeyondTheFirstFiftyRows()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        await frame.Locator("#auditPager").ScrollIntoViewIfNeededAsync();
        await session.Page.Locator("#stage iframe").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "audit-pagination-after.png") });
        await Expect(frame.Locator("#auditPager")).ToContainTextAsync("525");
        await frame.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
        await Expect(frame.Locator("#auditTableWrap")).ToContainTextAsync("Audit evidence 475");
        for (var page = 3; page <= 11; page++)
        {
            await frame.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
            await Expect(frame.Locator("#auditPager")).ToContainTextAsync($"Page {page} of 11");
        }
        await Expect(frame.Locator("#auditTableWrap tbody tr")).ToHaveCountAsync(25);
        await Expect(frame.Locator("#auditTableWrap tbody tr").First).ToContainTextAsync("Audit evidence 25");
        await Expect(frame.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true })).ToBeDisabledAsync();
        await frame.GetByRole(AriaRole.Button, new() { Name = "Previous", Exact = true }).PressAsync("Enter");
        await Expect(frame.Locator("#auditPager")).ToContainTextAsync("Page 10 of 11");
        await Expect(frame.GetByRole(AriaRole.Button, new() { Name = "Previous", Exact = true })).ToBeFocusedAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminAudit_SupersededReadCannotReplaceTheCurrentEvidence()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextAuditRead()");
        await frame.Locator("#auditRefreshBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingAuditReads ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setAuditLabel('Current evidence')");
        await frame.Locator("#auditRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#auditTableWrap")).ToContainTextAsync("Current evidence 525");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseAuditReads(); await new Promise(requestAnimationFrame); }");
        await session.Page.Locator("#stage iframe").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "audit-ordering-after.png") });
        await Expect(frame.Locator("#auditTableWrap")).ToContainTextAsync("Current evidence 525");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminAudit_FailedExportCannotDownloadAnErrorBody()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('auditExportFailed')");
        await frame.Locator("#auditExportBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.downloads.length || window.__adminFeedbackMessages.length ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await session.Page.Locator("#stage iframe").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "audit-export-after.png") });
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.downloads.length"));
        Assert.Equal("error", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminFeedbackMessages.at(-1).options.tone"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("auditFailed")]
    [InlineData("auditInvalid")]
    public async Task AdminAudit_UnavailableCatalogClearsEvidenceAndRecovers(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("(_, value) => window.__adminMock.setFailure(value)", failure);
        await frame.Locator("#auditRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#auditTableWrap")).ToHaveAttributeAsync("data-audit-state", "failed");
        await Expect(frame.Locator("#auditTableWrap tbody tr")).ToHaveCountAsync(0);
        await Expect(frame.Locator("#auditPager")).ToBeEmptyAsync();
        await Expect(frame.Locator("#auditExportBtn")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('audit')");
        await frame.Locator("#auditRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#auditTableWrap")).ToContainTextAsync("Audit evidence 525");
        await Expect(frame.Locator("#auditExportBtn")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminAudit_FilterSnapshotsIgnoreTheLateUnfilteredReadAndConfirmEmptyResults()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextAuditRead()");
        await frame.Locator("#auditRefreshBtn").ClickAsync();
        await WaitForAdminAuditPendingAsync(frame, false);
        await frame.Locator("#auditFilter").FillAsync("RUN_REPORT");
        await Expect(frame.Locator("#auditTableWrap")).ToHaveAttributeAsync("data-audit-state", "pending");
        await Expect(frame.Locator("#auditExportBtn")).ToBeDisabledAsync();
        await frame.Locator("#auditFilter").PressAsync("Enter");
        await Expect(frame.Locator("#auditTableWrap tbody tr").First).ToContainTextAsync("Audit evidence 524");
        await Expect(frame.Locator("#auditPager")).ToContainTextAsync("of 262");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseAuditReads(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#auditTableWrap tbody tr").First).ToContainTextAsync("Audit evidence 524");
        await frame.Locator("#auditFilter").FillAsync("NO_SUCH_ACTION");
        await frame.Locator("#auditFilter").PressAsync("Enter");
        await Expect(frame.Locator("#auditTableWrap")).ToHaveAttributeAsync("data-audit-state", "ready");
        await Expect(frame.Locator("#auditTableWrap")).ToContainTextAsync("No entries match");
        await Expect(frame.Locator("#auditPager")).ToContainTextAsync("0-0 of 0");
        await Expect(frame.Locator("#auditExportBtn")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("auditExportInvalid")]
    [InlineData("auditExportEmpty")]
    public async Task AdminAudit_InvalidCsvCannotStartADownload(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("(_, value) => window.__adminMock.setFailure(value)", failure);
        await frame.Locator("#auditExportBtn").ClickAsync();
        await Expect(frame.Locator("#auditExportStatus")).ToHaveAttributeAsync("role", "alert");
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.downloads.length"));
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.createdUrls.length"));
        await Expect(frame.Locator("#auditExportBtn")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminAudit_ExportUsesTheConfirmedFilterAndReleasesItsDownloadUrl()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        await frame.Locator("#auditFilter").FillAsync("RUN_REPORT");
        await frame.Locator("#auditFilter").PressAsync("Enter");
        await Expect(frame.Locator("#auditExportBtn")).ToBeEnabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextAuditExport()");
        await frame.Locator("#auditExportBtn").ClickAsync();
        await WaitForAdminAuditPendingAsync(frame, true);
        await Expect(frame.Locator("#auditExportBtn")).ToBeDisabledAsync();
        await frame.Locator("#auditExportBtn").EvaluateAsync("button => { button.click(); button.click(); }");
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.route.endsWith('/export/csv')).length"));
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseAuditExports(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#auditExportStatus")).ToContainTextAsync("download started");
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.revokedUrls.length ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        Assert.True(await frame.Locator("body").EvaluateAsync<bool>("""
            () => {
              const mock = window.__adminMock;
              return mock.downloads.length === 1 && mock.downloads[0].fileName.endsWith('.csv')
                && mock.createdUrls.length === 1 && mock.revokedUrls[0] === mock.createdUrls[0]
                && new URLSearchParams(mock.calls.find(call => call.route.endsWith('/export/csv')).query).get('action') === 'RUN_REPORT';
            }
            """));
        await Expect(frame.Locator("#auditExportLimit")).ToContainTextAsync("10,000 newest");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AdminAudit_ChangedContextCannotDownloadALateExport(bool dispose, bool changeIdentity)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextAuditExport()");
        await frame.Locator("#auditExportBtn").ClickAsync();
        await WaitForAdminAuditPendingAsync(frame, true);
        if (dispose) await frame.Locator("body").EvaluateAsync("() => window.dispatchEvent(new Event('pagehide'))");
        else if (changeIdentity) await ChangeAdminAuditIdentityAsync(frame);
        else await frame.Locator("#auditFilter").FillAsync("RUN_REPORT");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseAuditExports(); await new Promise(requestAnimationFrame); }");
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.downloads.length"));
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminFeedbackMessages.length"));
        await Expect(frame.Locator("#auditExportStatus")).ToBeHiddenAsync();
        await Expect(frame.Locator("#auditExportBtn")).ToBeDisabledAsync();
        if (dispose) await Expect(frame.Locator("#auditTableWrap")).ToBeEmptyAsync();
        else if (!changeIdentity)
        {
            await frame.Locator("#auditFilter").PressAsync("Enter");
            await Expect(frame.Locator("#auditExportBtn")).ToBeEnabledAsync();
        }
        Assert.Empty(session.PageErrors);
    }

    private static Task WaitForAdminAuditPendingAsync(IFrameLocator frame, bool export) => frame.Locator("body").EvaluateAsync("""
        (_, isExport) => new Promise(resolve => {
          const poll = () => (isExport ? window.__adminMock.pendingAuditExports : window.__adminMock.pendingAuditReads) ? resolve() : requestAnimationFrame(poll);
          poll();
        })
        """, export);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminAudit_DisposalOrIdentityChangeCannotRevealALateRead(bool dispose)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => { window.__adminMock.setAuditLabel('Late identity evidence'); window.__adminMock.pauseNextAuditRead(); }");
        await frame.Locator("#auditRefreshBtn").ClickAsync();
        await WaitForAdminAuditPendingAsync(frame, false);
        if (dispose) await frame.Locator("body").EvaluateAsync("() => window.dispatchEvent(new Event('pagehide'))");
        else await ChangeAdminAuditIdentityAsync(frame);
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseAuditReads(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#auditTableWrap tbody tr")).ToHaveCountAsync(0);
        await Expect(frame.Locator("#auditExportBtn")).ToBeDisabledAsync();
        if (dispose) await Expect(frame.Locator("#auditTableWrap")).ToBeEmptyAsync();
        else await Expect(frame.Locator("#auditTableWrap")).ToContainTextAsync("Portal session changed");
        Assert.Empty(session.PageErrors);
    }

    private static Task ChangeAdminAuditIdentityAsync(IFrameLocator frame) => frame.Locator("body").EvaluateAsync("""
        () => {
          const parts = sessionStorage.getItem('etlsql_token').split('.');
          const claims = JSON.parse(atob(parts[1])); claims.sub = 'other-account';
          parts[1] = btoa(JSON.stringify(claims)); sessionStorage.setItem('etlsql_token', parts.join('.'));
        }
        """);

    [Fact]
    public async Task AdminAudit_UntrustedDetailsRenderAsText()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminAuditSandboxAsync(session.Page);
        const string detail = "<img src=x onerror=\"window.__auditInjected=true\">";
        await frame.Locator("body").EvaluateAsync("(_, value) => window.__adminMock.setAuditLabel(value)", detail);
        await frame.Locator("#auditRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#auditTableWrap tbody tr").First).ToContainTextAsync(detail);
        await Expect(frame.Locator("#auditTableWrap img")).ToHaveCountAsync(0);
        Assert.False(await frame.Locator("body").EvaluateAsync<bool>("() => window.__auditInjected === true"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSubscriptions_LateParameterReadCannotReplaceTheNewDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextParameterRead()");
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await WaitForSubscriptionPendingAsync(frame, false);
        await frame.Locator("#editParamsCancelBtn").ClickAsync();
        await frame.Locator("[data-action='edit-params'][data-id='61']").ClickAsync();
        await Expect(frame.Locator("#editParamsFields input")).ToHaveValueAsync("South");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseParameters(); await new Promise(requestAnimationFrame); }");
        await session.Page.Locator("#stage iframe").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "subscriptions-parameters-after.png") });
        await Expect(frame.Locator("#editParamsTitle")).ToContainTextAsync("Operations delivery");
        await Expect(frame.Locator("#editParamsFields input")).ToHaveValueAsync("South");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSubscriptions_LateHistoryCannotReplaceTheCurrentAttempts()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextSubscriptionHistory()");
        await frame.Locator("[data-action='history'][data-id='60']").ClickAsync();
        await WaitForSubscriptionPendingAsync(frame, true);
        await frame.Locator("#subscriptionHistoryCloseBtn").ClickAsync();
        await frame.Locator("[data-action='history'][data-id='61']").ClickAsync();
        await Expect(frame.Locator("#subscriptionHistoryBody")).ToContainTextAsync("610");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseSubscriptionHistories(); await new Promise(requestAnimationFrame); }");
        await session.Page.Locator("#stage iframe").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "subscriptions-history-after.png") });
        await Expect(frame.Locator("#subscriptionHistoryTitle")).ToContainTextAsync("Operations delivery");
        await Expect(frame.Locator("#subscriptionHistoryBody")).ToContainTextAsync("610");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSubscriptions_ParameterSaveRetainsItsDraftAcrossALaterEditor()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await Expect(frame.Locator("#editParamsFields input")).ToHaveValueAsync("North");
        await frame.Locator("#editParamsFields input").FillAsync("East");
        await frame.Locator("body").EvaluateAsync("() => { window.__adminMock.pauseNextParameterRead(); window.__adminMock.pauseNextWrite(); }");
        await frame.Locator("#editParamsSaveBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingParameters || window.__adminMock.pendingWrites ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("body").EvaluateAsync("() => { if (!window.__adminMock.pendingParameters) window.__adminMock.releaseParameters(); }");
        await frame.Locator("#editParamsCancelBtn").ClickAsync();
        await frame.Locator("[data-action='edit-params'][data-id='61']").ClickAsync();
        await Expect(frame.Locator("#editParamsFields input")).ToHaveValueAsync("South");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseParameters(); await new Promise(requestAnimationFrame); }");
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingWrites ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseWrites(); await new Promise(requestAnimationFrame); }");
        await session.Page.Locator("#stage iframe").ScreenshotAsync(new() { Path = Path.Combine(RepoRoot(), "artifacts", "section1", "admin", "subscriptions-save-after.png") });
        Assert.Equal("East", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminMock.calls.find(call => call.method === 'PUT' && call.route === '/api/subscriptions/60').body.parameters['@Region']"));
        await Expect(frame.Locator("#editParamsModal")).ToBeVisibleAsync();
        await Expect(frame.Locator("#editParamsFields input")).ToHaveValueAsync("South");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("subscriptionsParametersUnavailable")]
    [InlineData("subscriptionsParametersMalformed")]
    public async Task AdminSubscriptions_UnknownParameterDefinitionsBlockSaveAndCanBeRetried(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("(_, failure) => window.__adminMock.setFailure(failure)", failure);
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await Expect(frame.Locator("#editParamsFields")).ToContainTextAsync("could not be loaded");
        await Expect(frame.Locator("#editParamsSaveBtn")).ToBeDisabledAsync();
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.method === 'PUT' && call.route.startsWith('/api/subscriptions/')).length"));
        await frame.Locator("#editParamsCancelBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('')");
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await Expect(frame.Locator("#editParamsFields input")).ToHaveValueAsync("North");
        await Expect(frame.Locator("#editParamsSaveBtn")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSubscriptions_RequiredValuesBlockSaveAndEmptyOverridesCanBeCleared()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await frame.Locator("#editParamsFields input").FillAsync("");
        await frame.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(frame.Locator("#editParamsError")).ToContainTextAsync("requires a value");
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.method === 'PUT').length"));
        await frame.Locator("#editParamsCancelBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setParameterDefinitions([{ name: '@Region', type: 'STRING', required: false, default: null, description: null }])");
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await frame.Locator("#editParamsFields input").FillAsync("");
        await frame.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(frame.Locator("#editParamsModal")).ToBeHiddenAsync();
        Assert.Equal("{}", await frame.Locator("body").EvaluateAsync<string>("() => JSON.stringify(window.__adminMock.calls.find(call => call.method === 'PUT').body.parameters)"));
        Assert.Equal(2, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.route.endsWith('/parameters')).length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSubscriptions_ConflictAndCatalogRefreshRetainTheOpenedVersionAndDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await frame.Locator("#editParamsFields input").FillAsync("East");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setSubscriptionVersion(60, 2)");
        await frame.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(frame.Locator("#editParamsError")).ToContainTextAsync("changed");
        await frame.Locator("#editParamsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#subsTableWrap")).ToHaveAttributeAsync("data-subscriptions-state", "ready");
        await Expect(frame.Locator("#editParamsFields input")).ToHaveValueAsync("East");
        await frame.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(frame.Locator("#editParamsError")).ToContainTextAsync("changed");
        var versions = await frame.Locator("body").EvaluateAsync<string[]>("() => window.__adminMock.calls.filter(call => call.method === 'PUT' && call.route === '/api/subscriptions/60').map(call => call.ifMatch)");
        Assert.Equal(["\"1\"", "\"1\""], versions);
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("subscriptionsWriteUnavailable")]
    [InlineData("subscriptionsReceiptMalformed")]
    public async Task AdminSubscriptions_UnconfirmedSaveLocksWritesUntilTheExactOffPageTargetIsRefreshed(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await frame.Locator("#editParamsFields input").FillAsync("East");
        await frame.Locator("body").EvaluateAsync("(_, failure) => window.__adminMock.setFailure(failure)", failure);
        await frame.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(frame.Locator("#editParamsError")).ToContainTextAsync("unconfirmed");
        await Expect(frame.Locator("#editParamsSaveBtn")).ToBeDisabledAsync();
        await Expect(frame.Locator("[data-action='toggle'][data-id='60']")).ToBeDisabledAsync();
        await frame.Locator("#subsFilter").FillAsync("Operations");
        await Expect(frame.Locator("[data-action='toggle'][data-id='60']")).ToHaveCountAsync(0);
        await Expect(frame.Locator("[data-action='toggle'][data-id='61']")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('')");
        await frame.Locator("#editParamsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#editParamsSaveBtn")).ToBeEnabledAsync();
        await Expect(frame.Locator("#editParamsFields input")).ToHaveValueAsync("East");
        Assert.True(await frame.Locator("body").EvaluateAsync<bool>("() => window.__adminMock.calls.some(call => call.method === 'GET' && call.route === '/api/subscriptions/60')"));
        await frame.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(frame.Locator("#editParamsModal")).ToBeHiddenAsync();
        Assert.Equal("\"2\"", await frame.Locator("body").EvaluateAsync<string>("() => window.__adminMock.calls.filter(call => call.method === 'PUT').at(-1).ifMatch"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSubscriptions_DuplicateSavesAreBlockedWhileTheCapturedWriteIsPending()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await frame.Locator("#editParamsFields input").FillAsync("East");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(frame.Locator("#editParamsSaveBtn")).ToBeDisabledAsync();
        await frame.Locator("#editParamsSaveBtn").EvaluateAsync("button => button.dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.method === 'PUT').length"));
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.releaseWrites()");
        await Expect(frame.Locator("#editParamsModal")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSubscriptions_BulkStatusReportsPartialResultsAndUsesSelectedSnapshots()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('subscriptionsBulkPartial')");
        await frame.Locator("#subsTableWrap [data-select-all]").CheckAsync();
        await frame.Locator("#subsPauseBtn").ClickAsync();
        await Expect(frame.Locator("#subsMutationStatus")).ToContainTextAsync("1 subscriptions paused. 1 were not changed");
        await Expect(frame.Locator("[data-action='toggle'][data-id='60']")).ToHaveTextAsync("Resume");
        await Expect(frame.Locator("[data-action='toggle'][data-id='61']")).ToHaveTextAsync("Pause");
        Assert.Equal("[{\"id\":60,\"version\":1},{\"id\":61,\"version\":1}]", await frame.Locator("body").EvaluateAsync<string>("() => JSON.stringify(window.__adminMock.calls.find(call => call.route.endsWith('/bulk-status')).body.subscriptions)"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("subscriptionsCatalogMalformed")]
    [InlineData("subscriptionsCatalogUnavailable")]
    public async Task AdminSubscriptions_UnavailableCatalogIsNotAnEmptySuccess(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("(_, failure) => window.__adminMock.setFailure(failure)", failure);
        await frame.Locator("#subsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#subsTableWrap")).ToHaveAttributeAsync("data-subscriptions-state", "failed");
        await Expect(frame.Locator("#subsTableWrap")).ToContainTextAsync("could not be loaded");
        await Expect(frame.Locator("#subsPauseBtn")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('')");
        await frame.Locator("#subsRefreshBtn").ClickAsync();
        await Expect(frame.Locator("#subsTableWrap")).ToHaveAttributeAsync("data-subscriptions-state", "ready");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("subscriptionsHistoryMalformed")]
    [InlineData("subscriptionsHistoryUnavailable")]
    public async Task AdminSubscriptions_UnavailableHistoryIsNotAnEmptySuccess(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("(_, failure) => window.__adminMock.setFailure(failure)", failure);
        await frame.Locator("[data-action='history'][data-id='60']").ClickAsync();
        await Expect(frame.Locator("#subscriptionHistoryBody")).ToContainTextAsync("could not be loaded");
        await Expect(frame.Locator("#subscriptionHistoryBody .empty-state")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSubscriptions_LateCatalogCannotReplaceTheCurrentFilter()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextSubscriptionRead()");
        await frame.Locator("#subsRefreshBtn").ClickAsync();
        await frame.Locator("body").EvaluateAsync("""
            () => new Promise(resolve => {
              const poll = () => window.__adminMock.pendingSubscriptions ? resolve() : requestAnimationFrame(poll);
              poll();
            })
            """);
        await frame.Locator("#subsFilter").FillAsync("Operations");
        await Expect(frame.Locator("#subsTableWrap tbody tr")).ToHaveCountAsync(1);
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseSubscriptions(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#subsTableWrap tbody tr")).ToHaveCountAsync(1);
        await Expect(frame.Locator("#subsTableWrap")).ToContainTextAsync("Operations delivery");
        await Expect(frame.Locator("[data-action='edit-params'][data-id='60']")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSubscriptions_DisposalPreventsLateWriteCompletionFromUpdatingThePage()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSubscriptionsSandboxAsync(session.Page);
        await frame.Locator("[data-action='edit-params'][data-id='60']").ClickAsync();
        await frame.Locator("#editParamsFields input").FillAsync("East");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(frame.Locator("#editParamsSaveBtn")).ToBeDisabledAsync();
        await frame.Locator("body").EvaluateAsync("async () => { window.dispatchEvent(new Event('pagehide')); window.__adminMock.releaseWrites(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#editParamsModal")).ToBeHiddenAsync();
        await Expect(frame.Locator("#subsMutationStatus")).ToBeHiddenAsync();
        await Expect(frame.Locator("#subsRefreshBtn")).ToBeDisabledAsync();
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.method === 'PUT').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSettings_LateInitialReadPreservesEditedConnectionAndKey()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page, "settingsLateRead");
        await WaitForSettingsPendingAsync(frame, "pendingSettings");
        await frame.Locator("#orch-url").FillAsync("https://newer.example.test");
        await frame.Locator("#orch-key").FillAsync("newer-fixture-key");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseSettings(); await new Promise(requestAnimationFrame); }");
        await SaveSettingsScreenshotAsync(session.Page, "late-read");
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://newer.example.test");
        await Expect(frame.Locator("#orch-key")).ToHaveValueAsync("newer-fixture-key");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminSettings_SavePreservesNewerDraftsInBothForms(bool orchestrator)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page);
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://orchestrator.example.test");
        await Expect(frame.Locator("#brand-footer")).ToHaveValueAsync("Sandbox footer");
        await frame.Locator("#orch-url").FillAsync("https://captured.example.test");
        await frame.Locator("#brand-name").FillAsync("Captured branding");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator(orchestrator ? "#orchSaveBtn" : "#brandSaveBtn").ClickAsync();
        await WaitForSettingsPendingAsync(frame, "pendingWrites");
        await frame.Locator("#orch-url").FillAsync("https://newer.example.test");
        await frame.Locator("#orch-key").FillAsync("newer-fixture-key");
        await frame.Locator("#brand-name").FillAsync("Newer branding draft");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseWrites(); await new Promise(requestAnimationFrame); }");
        await SaveSettingsScreenshotAsync(session.Page, orchestrator ? "orchestrator-save" : "branding-save");
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://newer.example.test");
        await Expect(frame.Locator("#orch-key")).ToHaveValueAsync("newer-fixture-key");
        await Expect(frame.Locator("#brand-name")).ToHaveValueAsync("Newer branding draft");
        var route = orchestrator ? "/api/admin/settings/orchestrator" : "/api/admin/settings/branding";
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("(_, route) => window.__adminMock.calls.filter(call => call.method === 'PUT' && call.route === route).length", route));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("settingsStatusForbidden")]
    [InlineData("settingsStatusMalformed")]
    public async Task AdminSettings_InvalidStatusDoesNotAnnounceConfirmedOffline(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page);
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://orchestrator.example.test");
        await frame.Locator("body").EvaluateAsync("(_, failure) => window.__adminMock.setFailure(failure)", failure);
        await frame.Locator("#orchTestBtn").ClickAsync();
        await SaveSettingsScreenshotAsync(session.Page, failure);
        await Expect(frame.Locator("#orch-test-result")).ToContainTextAsync("could not be confirmed");
        await Expect(frame.Locator("#orch-test-result")).Not.ToContainTextAsync("Offline");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("settingsUnavailable", true)]
    [InlineData("settingsMalformed", true)]
    [InlineData("brandingUnavailable", false)]
    [InlineData("brandingMalformed", false)]
    public async Task AdminSettings_UnknownReadsBlockWritesAndRefreshPreservesDrafts(string failure, bool orchestrator)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page);
        await Expect(frame.Locator("#brand-footer")).ToHaveValueAsync("Sandbox footer");
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://orchestrator.example.test");
        var field = orchestrator ? "#orch-url" : "#brand-name";
        var draft = orchestrator ? "https://draft.example.test" : "Draft branding";
        var refresh = orchestrator ? "#orchRefreshBtn" : "#brandRefreshBtn";
        var save = orchestrator ? "#orchSaveBtn" : "#brandSaveBtn";
        await frame.Locator(field).FillAsync(draft);
        await frame.Locator("body").EvaluateAsync("(_, failure) => window.__adminMock.setFailure(failure)", failure);
        await frame.Locator(refresh).ClickAsync();
        await Expect(frame.Locator(orchestrator ? "#orch-error" : "#brand-error")).ToContainTextAsync("could not be loaded");
        await Expect(frame.Locator(save)).ToBeDisabledAsync();
        await Expect(frame.Locator(field)).ToHaveValueAsync(draft);
        if (orchestrator) await Expect(frame.Locator("#orch-key-status")).ToContainTextAsync("unavailable");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('')");
        await frame.Locator(refresh).ClickAsync();
        await Expect(frame.Locator(save)).ToBeEnabledAsync();
        await Expect(frame.Locator(field)).ToHaveValueAsync(draft);
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("settingsWriteUnavailable", true)]
    [InlineData("settingsWriteUnconfirmed", true)]
    [InlineData("brandingWriteUnavailable", false)]
    [InlineData("brandingWriteUnconfirmed", false)]
    public async Task AdminSettings_UnconfirmedWriteRequiresExplicitRefreshWithoutLosingTheDraft(string failure, bool orchestrator)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page);
        await Expect(frame.Locator("#brand-footer")).ToHaveValueAsync("Sandbox footer");
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://orchestrator.example.test");
        var field = orchestrator ? "#orch-url" : "#brand-name";
        var draft = orchestrator ? "https://draft.example.test" : "Draft branding";
        var refresh = orchestrator ? "#orchRefreshBtn" : "#brandRefreshBtn";
        var save = orchestrator ? "#orchSaveBtn" : "#brandSaveBtn";
        await frame.Locator(field).FillAsync(draft);
        await frame.Locator("body").EvaluateAsync("(_, failure) => window.__adminMock.setFailure(failure)", failure);
        await frame.Locator(save).ClickAsync();
        await Expect(frame.Locator(orchestrator ? "#orch-error" : "#brand-error")).ToContainTextAsync("outcome is unconfirmed");
        await Expect(frame.Locator(save)).ToBeDisabledAsync();
        await Expect(frame.Locator(field)).ToHaveValueAsync(draft);
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.method === 'PUT').length"));
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.setFailure('')");
        await frame.Locator(refresh).ClickAsync();
        await Expect(frame.Locator(save)).ToBeEnabledAsync();
        await Expect(frame.Locator(field)).ToHaveValueAsync(draft);
        await Expect(frame.Locator(orchestrator ? "#orch-save-result" : "#brand-save-result")).ToContainTextAsync("remains unconfirmed");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSettings_LateConnectionStatusCannotDescribeANewerDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page);
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://orchestrator.example.test");
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextStatusRead()");
        await frame.Locator("#orchTestBtn").ClickAsync();
        await WaitForSettingsPendingAsync(frame, "pendingStatus");
        await frame.Locator("#orch-url").FillAsync("https://newer.example.test");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releaseStatus(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator("#orch-test-result")).ToBeEmptyAsync();
        await Expect(frame.Locator("#orchTestBtn")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminSettings_PendingWriteCannotBeDuplicatedOrRepaintAfterDisposal(bool orchestrator)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page);
        await Expect(frame.Locator("#brand-footer")).ToHaveValueAsync("Sandbox footer");
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://orchestrator.example.test");
        var save = orchestrator ? "#orchSaveBtn" : "#brandSaveBtn";
        var result = orchestrator ? "#orch-save-result" : "#brand-save-result";
        await frame.Locator("body").EvaluateAsync("() => window.__adminMock.pauseNextWrite()");
        await frame.Locator(save).ClickAsync();
        await WaitForSettingsPendingAsync(frame, "pendingWrites");
        await Expect(frame.Locator(save)).ToBeDisabledAsync();
        await frame.Locator(save).EvaluateAsync("button => button.click()");
        await frame.Locator("body").EvaluateAsync("async () => { window.dispatchEvent(new Event('pagehide')); window.__adminMock.releaseWrites(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator(result)).ToHaveTextAsync("Saving…");
        await Expect(frame.Locator(save)).ToBeDisabledAsync();
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.method === 'PUT').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminSettings_LateInitialBrandingCannotReplaceAcceptedBranding()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page, "settingsLatePublicBranding");
        await WaitForSettingsPendingAsync(frame, "pendingPublicBranding");
        await Expect(frame.Locator("#brand-footer")).ToHaveValueAsync("Sandbox footer");
        await frame.Locator("#brand-name").FillAsync("Accepted portal name");
        await frame.Locator("#brandSaveBtn").ClickAsync();
        await Expect(frame.Locator("#brand-save-result")).ToHaveTextAsync("Saved.");
        await Expect(frame.Locator(".portal-brand-name")).ToHaveTextAsync("Accepted portal name");
        await frame.Locator("body").EvaluateAsync("async () => { window.__adminMock.releasePublicBranding(); await new Promise(requestAnimationFrame); }");
        await Expect(frame.Locator(".portal-brand-name")).ToHaveTextAsync("Accepted portal name");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminPage_TabSwitchPreservesSettingsDraftsAndLoadsEachPanelOnce()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page);
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://orchestrator.example.test");
        await Expect(frame.Locator("#brand-footer")).ToHaveValueAsync("Sandbox footer");
        await frame.Locator("#orch-url").FillAsync("https://draft.example.test");
        await frame.Locator("#brand-name").FillAsync("Private branding draft");
        await frame.Locator(".admin-tab[data-tab='users']").ClickAsync();
        await Expect(frame.Locator(".admin-tab[data-tab='users']")).ToHaveAttributeAsync("aria-selected", "true");
        await frame.Locator(".admin-tab[data-tab='settings']").ClickAsync();
        await Expect(frame.Locator(".admin-tab[data-tab='settings']")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(frame.Locator(".admin-tab[data-tab='users']")).ToHaveAttributeAsync("aria-selected", "false");
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://draft.example.test");
        await Expect(frame.Locator("#brand-name")).ToHaveValueAsync("Private branding draft");
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.route === '/api/admin/settings/orchestrator').length"));
        Assert.Equal(1, await frame.Locator("body").EvaluateAsync<int>("() => window.__adminMock.calls.filter(call => call.route === '/api/admin/settings/branding').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AdminPage_DisposalRemovesTabHandlersAndCannotMountAnotherOwner()
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await OpenAdminSettingsSandboxAsync(session.Page);
        await Expect(frame.Locator("#orch-url")).ToHaveValueAsync("https://orchestrator.example.test");
        var additional = await frame.Locator("body").EvaluateAsync<int>("""
            async () => {
              const before = window.__adminMock.calls.length;
              window.dispatchEvent(new Event('pagehide'));
              document.querySelector(".admin-tab[data-tab='datasets']").click();
              document.querySelector(".admin-tab[data-tab='operations']").click();
              await new Promise(requestAnimationFrame);
              return window.__adminMock.calls.length - before;
            }
            """);
        Assert.Equal(0, additional);
        await Expect(frame.Locator(".admin-tab[data-tab='settings']")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Empty(session.PageErrors);
    }

    private async Task<IFrameLocator> OpenAdminSettingsSandboxAsync(IPage page, string fixtureId = "settings")
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='admin-page']").ClickAsync();
        await page.Locator("#fixtureSel").SelectOptionAsync(fixtureId);
        var frame = page.FrameLocator("#stage iframe");
        await Expect(frame.Locator("body")).ToHaveAttributeAsync("data-admin-sandbox-ready", "true");
        return frame;
    }

    private static Task WaitForSettingsPendingAsync(IFrameLocator frame, string field) => frame.Locator("body").EvaluateAsync("""
        (_, field) => new Promise(resolve => {
          const poll = () => window.__adminMock[field] ? resolve() : requestAnimationFrame(poll);
          poll();
        })
        """, field);

    private static async Task SaveSettingsScreenshotAsync(IPage page, string scenario)
    {
        var directory = Path.Combine(RepoRoot(), "artifacts", "section1", "admin");
        Directory.CreateDirectory(directory);
        await page.Locator("#stage iframe").ScreenshotAsync(new() { Path = Path.Combine(directory, $"settings-{scenario}-after.png") });
    }

    private static Task WaitForSubscriptionPendingAsync(IFrameLocator frame, bool history) => frame.Locator("body").EvaluateAsync("""
        (_, history) => new Promise(resolve => {
          const poll = () => (history ? window.__adminMock.pendingSubscriptionHistories : window.__adminMock.pendingParameters) ? resolve() : requestAnimationFrame(poll);
          poll();
        })
        """, history);

    private async Task<IFrameLocator> OpenAdminSubscriptionsSandboxAsync(IPage page)
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='admin-page']").ClickAsync();
        await page.Locator("#fixtureSel").SelectOptionAsync("subscriptions");
        var frame = page.FrameLocator("#stage iframe");
        await Expect(frame.Locator("body")).ToHaveAttributeAsync("data-admin-sandbox-ready", "true");
        await Expect(frame.Locator("#subsTableWrap")).ToContainTextAsync("Finance delivery");
        return frame;
    }

    private async Task<IFrameLocator> OpenAdminAuditSandboxAsync(IPage page)
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='admin-page']").ClickAsync();
        await page.Locator("#fixtureSel").SelectOptionAsync("audit");
        var frame = page.FrameLocator("#stage iframe");
        await Expect(frame.Locator("body")).ToHaveAttributeAsync("data-admin-sandbox-ready", "true");
        await Expect(frame.Locator("#auditTableWrap")).ToContainTextAsync("Audit evidence 525");
        return frame;
    }

    private async Task<IFrameLocator> OpenAdminReportsSandboxAsync(IPage page)
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='admin-page']").ClickAsync();
        await page.Locator("#fixtureSel").SelectOptionAsync("reports");
        var frame = page.FrameLocator("#stage iframe");
        await Expect(frame.Locator("body")).ToHaveAttributeAsync("data-admin-sandbox-ready", "true");
        await Expect(frame.Locator("#reportsTableWrap")).ToContainTextAsync("Finance report");
        return frame;
    }

    private static Task WaitForAdminValidationAsync(IFrameLocator frame) => frame.Locator("body").EvaluateAsync("""
        () => new Promise(resolve => {
          const poll = () => window.__adminMock.pendingValidations ? resolve() : requestAnimationFrame(poll);
          poll();
        })
        """);

    private static Task WaitForAdminWriteAsync(IFrameLocator frame) => frame.Locator("body").EvaluateAsync("""
        () => new Promise(resolve => {
          const poll = () => window.__adminMock.pendingWrites ? resolve() : requestAnimationFrame(poll);
          poll();
        })
        """);

    private async Task<IFrameLocator> OpenAdminFoldersSandboxAsync(IPage page)
    {
        var frame = await OpenAdminUsersSandboxAsync(page);
        await frame.Locator(".admin-tab[data-tab='folders']").ClickAsync();
        await Expect(frame.Locator("#folderTableWrap")).ToContainTextAsync("/Reports");
        await Expect(frame.Locator("#folderTableWrap")).ToHaveAttributeAsync("data-folders-state", "ready");
        return frame;
    }

    private async Task<IFrameLocator> OpenAdminGroupsSandboxAsync(IPage page)
    {
        var frame = await OpenAdminUsersSandboxAsync(page);
        await frame.Locator(".admin-tab[data-tab='groups']").ClickAsync();
        await Expect(frame.Locator("#groupTableWrap")).ToHaveAttributeAsync("data-groups-state", "ready");
        return frame;
    }

    [Fact]
    public async Task Governance_UnconfirmedScanCannotAnnounceSuccess()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-governance']").ClickAsync();
        await Expect(page.Locator("[data-gov-state='scanned']")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => window.__governance.governanceApi.setMode('bad-receipt')");
        await page.Locator("#btnRunScan").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__governance.messages.length > 0");
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "governance", "scan-unconfirmed.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = path });
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__governance.messages.filter(item => item.options.tone === 'success').length"));
        await Expect(page.Locator("#stage")).ToContainTextAsync("Change outcome is unconfirmed");
        await Expect(page.Locator("#btnRunScan")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_PendingWriteCannotBeSubmittedTwice()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-governance']").ClickAsync();
        await Expect(page.Locator("[data-gov-state='scanned']")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => window.__governance.governanceApi.pause('scan')");
        await page.Locator("#btnRunScan").ClickAsync();
        await page.EvaluateAsync("() => window.__governance.governanceApi.whenPaused()");
        await page.EvaluateAsync("() => document.querySelector('#btnRunScan').click()");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__governance.governanceApi.calls.filter(item => item.method === 'scan').length"));
        await page.EvaluateAsync("async () => { window.__governance.governanceApi.release(); await new Promise(requestAnimationFrame); }");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_GlossaryEditPreservesStewardAndDisabledState()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-governance']").ClickAsync();
        await page.Locator("#fixtureSel").SelectOptionAsync("glossary");
        await page.WaitForFunctionAsync("() => window.__governance?.portal.state.tab === 'glossary' && window.__governance.portal.state.load === 'ready'");
        await page.EvaluateAsync("async () => { window.__governance.governanceApi.setGlossaryFlags('revenue', { disabled: true }); await window.__governance.portal.reload(); }");
        await page.Locator("[data-edit-term='revenue']").ClickAsync();
        await page.Locator("#glossaryDesc").FillAsync("Updated definition.");
        await page.Locator("#btnConfirmGlossaryModal").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__governance.governanceApi.calls.some(item => item.method === 'saveGlossaryTerm')");
        var payload = await page.EvaluateAsync<System.Text.Json.JsonElement>("() => window.__governance.governanceApi.calls.find(item => item.method === 'saveGlossaryTerm').payload");
        Assert.True(payload.TryGetProperty("steward", out var steward));
        Assert.Equal("chuck", steward.GetString());
        Assert.True(payload.GetProperty("disabled").GetBoolean());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_CancelRestoresDialogSemanticsAndFocus()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-governance']").ClickAsync();
        await page.Locator("#fixtureSel").SelectOptionAsync("settings");
        await page.Locator("#btnAddNewCategory").ClickAsync();
        await Expect(page.Locator("#catLabel")).ToBeFocusedAsync();
        await page.Locator("#btnCancelCategoryModal").ClickAsync();
        await Expect(page.Locator("#categoryModalBackdrop")).ToHaveAttributeAsync("aria-hidden", "true");
        await Expect(page.Locator("#btnAddNewCategory")).ToBeFocusedAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_UnconfirmedWriteNeedsSuccessfulExplicitRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenGovernanceSandboxAsync(page);
        await page.EvaluateAsync("() => window.__governance.governanceApi.setMode('write-unavailable')");
        await page.Locator("#btnRunScan").ClickAsync();
        await Expect(page.Locator("#govMutationStatus")).ToContainTextAsync("unconfirmed");
        await page.EvaluateAsync("async () => { await window.__governance.portal.setTab('settings'); await window.__governance.portal.setTab('overview'); window.__governance.governanceApi.setMode('failed'); }");
        await Expect(page.Locator("#btnRunScan")).ToBeDisabledAsync();
        await page.Locator("#btnGovRefresh").ClickAsync();
        await Expect(page.Locator("[data-gov-state='failed']")).ToBeVisibleAsync();
        await Expect(page.Locator("#govMutationStatus")).ToContainTextAsync("unconfirmed");
        await page.EvaluateAsync("() => window.__governance.governanceApi.setMode('live')");
        await page.Locator("#btnGovRefresh").ClickAsync();
        await Expect(page.Locator("#btnRunScan")).ToBeEnabledAsync();
        await Expect(page.Locator("#govMutationStatus")).ToBeHiddenAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__governance.governanceApi.calls.filter(item => item.method === 'scan').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_LateSettingsSaveKeepsAnotherCategoryDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenGovernanceSandboxAsync(page, "settings");
        await page.EvaluateAsync("() => window.__governance.governanceApi.pause('saveSettings')");
        await page.Locator("#settingsDeductMeta").FillAsync("7");
        await page.Locator("#btnSaveScoringSettings").ClickAsync();
        await page.EvaluateAsync("() => window.__governance.governanceApi.whenPaused()");
        await page.Locator("#btnAddNewCategory").ClickAsync();
        await page.Locator("#catLabel").FillAsync("Draft while saving");
        await page.EvaluateAsync("() => window.__governance.governanceApi.release()");
        await page.WaitForFunctionAsync("() => window.__governance.portal.state.mutation === 'idle'");
        await Expect(page.Locator("#categoryModalBackdrop")).ToBeVisibleAsync();
        await Expect(page.Locator("#catLabel")).ToHaveValueAsync("Draft while saving");
        await Expect(page.Locator("#catLabel")).ToBeFocusedAsync();
        await Expect(page.Locator("#govMutationStatus")).ToContainTextAsync("recorded");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_LateConfirmationCannotDeleteFromAnotherView()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenGovernanceSandboxAsync(page, "glossary");
        await page.EvaluateAsync("() => window.__governance.governanceApi.pause('confirm')");
        await page.Locator("[data-delete-term='revenue']").ClickAsync();
        await page.EvaluateAsync("() => window.__governance.governanceApi.whenPaused()");
        await page.EvaluateAsync("() => window.__governance.portal.setTab('settings')");
        await page.EvaluateAsync("async () => { window.__governance.governanceApi.release(); await new Promise(requestAnimationFrame); }");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__governance.governanceApi.calls.filter(item => item.method === 'deleteGlossaryTerm').length"));
        await Expect(page.Locator("#btnAddNewCategory")).ToBeVisibleAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_RedrawPreservesSettingsAndDialogDraftsWithKeyboardFocus()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenGovernanceSandboxAsync(page, "settings");
        await page.Locator("#settingsDeductMeta").FillAsync("7");
        await page.Locator("#btnAddNewCategory").ClickAsync();
        await page.Locator("#catLabel").FillAsync("Keep this category draft");
        await page.EvaluateAsync("() => window.__governance.portal.render()");
        await Expect(page.Locator("#catLabel")).ToHaveValueAsync("Keep this category draft");
        await Expect(page.Locator("#catLabel")).ToBeFocusedAsync();
        await page.Locator("#catLabel").PressAsync("Shift+Tab");
        await Expect(page.Locator("#btnConfirmCategoryModal")).ToBeFocusedAsync();
        await page.Locator("#btnConfirmCategoryModal").PressAsync("Escape");
        await Expect(page.Locator("#categoryModalBackdrop")).ToHaveAttributeAsync("aria-hidden", "true");
        await Expect(page.Locator("#btnAddNewCategory")).ToBeFocusedAsync();
        await Expect(page.Locator("#settingsDeductMeta")).ToHaveValueAsync("7");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_RunningScanAndPublicTabFallbackRemainHonest()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenGovernanceSandboxAsync(page, "scan-running");
        await Expect(page.Locator("[data-gov-state='scan-running']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-gov-state='scanned']")).ToHaveCountAsync(0);
        await Expect(page.Locator("#btnRunScan")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => window.__governance.portal.setTab('__proto__')");
        await Expect(page.Locator(".gov-kpis")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => window.__governance.portal.setTab('badges')");
        await Expect(page.Locator(".gov-kpis")).ToBeVisibleAsync();
        await page.Locator("#govScope").SelectOptionAsync("mine");
        await Expect(page.Locator(".gov-kpi").Filter(new() { HasText = "Governed assets" })).ToContainTextAsync("1/1");
        await Expect(page.Locator(".gov-kpi").Filter(new() { HasText = "Open findings" })).ToContainTextAsync("2");
        await Expect(page.Locator(".gov-body")).ToContainTextAsync("Finding totals cover the entire estate");
        Assert.Empty(session.PageErrors);
    }

    private async Task OpenGovernanceSandboxAsync(IPage page, string fixtureId = "overview")
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-governance']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__governance?.portal.state.load === 'ready'");
        if (fixtureId != "overview")
        {
            await page.Locator("#fixtureSel").SelectOptionAsync(fixtureId);
            await page.WaitForFunctionAsync("id => window.__governance?.portal.state.load === 'ready' && (id === 'scan-running' ? window.__governance.portal.state.dashboard?.lastScan?.status === 'running' : window.__governance.portal.state.tab === id)", fixtureId);
        }
    }

    [Fact]
    public async Task SandboxReload_RefreshesTransitiveModulesAndRetainsTheSelectedFixtureAndTheme()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var revision = "before";
        var reads = 0;
        var source = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot", "js", "governance-view.js"));
        Assert.Contains("<h1>Governance</h1>", source);
        await page.RouteAsync("**/js/governance-view.js*", route =>
        {
            Interlocked.Increment(ref reads);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "text/javascript",
                Body = source.Replace("<h1>Governance</h1>", $"<h1>Sandbox dependency {revision}</h1>", StringComparison.Ordinal)
            });
        });
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html#story=portal-governance&fixture=empty&theme=dark&sidebar=collapsed");
        await Expect(page.Locator("#stage h1")).ToHaveTextAsync("Sandbox dependency before");
        await page.WaitForFunctionAsync("() => window.__governance?.portal.state.load === 'ready'");
        var originalUrl = page.Url;
        var evidence = Path.Combine(RepoRoot(), "artifacts", "section1", "sandbox-reload");
        Directory.CreateDirectory(evidence);
        await page.ScreenshotAsync(new() { Path = Path.Combine(evidence, "transitive-before.png"), Animations = ScreenshotAnimations.Disabled });

        revision = "after";
        await page.Locator("#reloadBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__governance?.portal.state.load === 'ready'");
        await Expect(page.Locator("#stage h1")).ToHaveTextAsync("Sandbox dependency after");
        await page.ScreenshotAsync(new() { Path = Path.Combine(evidence, "transitive-after.png"), Animations = ScreenshotAnimations.Disabled });
        Assert.Equal(2, reads);
        await Expect(page.Locator("#fixtureSel")).ToHaveValueAsync("empty");
        await Expect(page.Locator("body")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("stage-theme-dark"));
        await Expect(page.Locator("body")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("sidebar-collapsed"));
        Assert.Equal(originalUrl, page.Url);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task SandboxReload_RecompiledDependencySurvivesCompilerFailureAndRecoversWithoutChangingTheEntry()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var root = Path.Combine(RepoRoot(), "artifacts", "section1", "sandbox-reload", "typed-" + Guid.NewGuid().ToString("N"));
        var sources = Path.Combine(root, "src", "ETL-SQL.Portal", "BrowserSources");
        var delivery = Path.Combine(root, "src", "ETL-SQL.Portal", "wwwroot", "js");
        Directory.CreateDirectory(sources);
        await File.WriteAllTextAsync(Path.Combine(root, "tsconfig.browser-build.json"),
            await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "tsconfig.browser-build.json")));
        var dependency = Path.Combine(sources, "dependency.ts");
        await File.WriteAllTextAsync(dependency, "export const message: string = 'before';\n");
        await File.WriteAllTextAsync(Path.Combine(sources, "entry.ts"), """
            import { message } from './dependency.js';
            export function mount(host: HTMLElement, fixture: string) {
              const result = document.createElement('h1');
              result.id = 'typed-result'; result.textContent = `${message}: ${fixture}`; host.append(result);
              const button = document.createElement('button'); button.id = 'typed-action';
              button.textContent = 'Exercise current listener'; host.append(button);
              const action = () => { result.dataset.actions = String(Number(result.dataset.actions ?? '0') + 1); };
              button.addEventListener('click', action);
              return { dispose() {
                button.removeEventListener('click', action);
                sessionStorage.setItem('__typedDisposals', String(Number(sessionStorage.getItem('__typedDisposals') ?? '0') + 1));
              } };
            }
            """);
        var first = await CompileSandboxFixtureAsync(root);
        Assert.Equal(0, first.ExitCode);
        await File.WriteAllTextAsync(Path.Combine(root, "compile-before.log"), first.Output);
        var entry = await File.ReadAllBytesAsync(Path.Combine(delivery, "entry.js"));
        var compiledDependency = await File.ReadAllBytesAsync(Path.Combine(delivery, "dependency.js"));
        var relative = "/" + Path.GetRelativePath(RepoRoot(), delivery).Replace('\\', '/');
        var registry = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "tools", "ui-sandbox", "stories", "index.js"));
        registry = "import { importFresh } from '../util.js';\n" + registry + $$"""
            stories.push({ id: 'typed-reload-proof', title: 'TypeScript dependency reload', category: 'Other Surfaces',
              fixtures: [{ id: 'one', label: 'One' }, { id: 'two', label: 'Two' }],
              async mount(stage, fixture) { const mod = await importFresh('{{relative}}/entry.js'); return mod.mount(stage, fixture); }
            });
            """;
        await page.RouteAsync("**/tools/ui-sandbox/stories/index.js", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "text/javascript",
            Body = registry
        }));
        var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await page.RouteAsync("**" + relative + "/*.js*", async route =>
        {
            var name = Path.GetFileName(new Uri(route.Request.Url).LocalPath);
            Assert.True(name is "entry.js" or "dependency.js");
            requests.Enqueue(name);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "text/javascript",
                Headers = new Dictionary<string, string> { ["Cache-Control"] = "no-store" },
                Body = await File.ReadAllTextAsync(Path.Combine(delivery, name))
            });
        });
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html#story=typed-reload-proof&fixture=two&theme=dark&sidebar=collapsed");
        await Assertions.Expect(page.Locator("#typed-result")).ToHaveTextAsync("before: two");
        await page.ScreenshotAsync(new() { Path = Path.Combine(root, "before.png"), Animations = ScreenshotAnimations.Disabled });

        await File.WriteAllTextAsync(dependency, "export const message: string = 123;\n");
        var failed = await CompileSandboxFixtureAsync(root);
        Assert.NotEqual(0, failed.ExitCode);
        Assert.Contains("not assignable", failed.Output);
        await File.WriteAllTextAsync(Path.Combine(root, "compile-failed.log"), failed.Output);
        Assert.Equal(entry, await File.ReadAllBytesAsync(Path.Combine(delivery, "entry.js")));
        Assert.Equal(compiledDependency, await File.ReadAllBytesAsync(Path.Combine(delivery, "dependency.js")));
        await page.Locator("#reloadBtn").ClickAsync();
        await Assertions.Expect(page.Locator("#typed-result")).ToHaveTextAsync("before: two");
        Assert.Equal("1", await page.EvaluateAsync<string>("() => sessionStorage.getItem('__typedDisposals')"));

        foreach (var value in new[] { "after", "latest" })
        {
            await File.WriteAllTextAsync(dependency, $"export const message: string = '{value}';\n");
            var recovered = await CompileSandboxFixtureAsync(root);
            Assert.Equal(0, recovered.ExitCode);
            await File.WriteAllTextAsync(Path.Combine(root, "compile-" + value + ".log"), recovered.Output);
            Assert.Equal(entry, await File.ReadAllBytesAsync(Path.Combine(delivery, "entry.js")));
            await page.Locator("#reloadBtn").ClickAsync();
            await Assertions.Expect(page.Locator("#typed-result")).ToHaveTextAsync(value + ": two");
            await page.Locator("#typed-action").ClickAsync();
            await Assertions.Expect(page.Locator("#typed-result")).ToHaveAttributeAsync("data-actions", "1");
        }
        await page.ScreenshotAsync(new() { Path = Path.Combine(root, "after.png"), Animations = ScreenshotAnimations.Disabled });
        Assert.Equal("3", await page.EvaluateAsync<string>("() => sessionStorage.getItem('__typedDisposals')"));
        Assert.Equal(4, requests.Count(name => name == "dependency.js"));
        Assert.Equal(4, requests.Count(name => name == "entry.js"));
        await Assertions.Expect(page.Locator("#fixtureSel")).ToHaveValueAsync("two");
        await Assertions.Expect(page.Locator("body")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("stage-theme-dark.*sidebar-collapsed"));
        Assert.Empty(session.PageErrors);
    }

    private static async Task<(int ExitCode, string Output)> CompileSandboxFixtureAsync(string root)
    {
        var compiler = new Uri(Path.Combine(RepoRoot(), "scripts", "compile-browser.mjs")).AbsoluteUri;
        var script = $"import {{ compileBrowser }} from {JsonSerializer.Serialize(compiler)}; "
            + $"try {{ console.log(process.version); compileBrowser({{ root: {JsonSerializer.Serialize(root)}, toolchain: {JsonSerializer.Serialize(RepoRoot())} }}); }} "
            + "catch (error) { console.error(error.message); process.exitCode = 1; }";
        var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("ETLSQL_BROWSER_BUILD_NODE") ?? "node")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepoRoot()
        };
        start.ArgumentList.Add("--input-type=module"); start.ArgumentList.Add("--eval"); start.ArgumentList.Add(script);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Browser compiler could not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output + await errors);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [Fact]
    public async Task Governance_SandboxReloadFetchesANewControllerAndDisposesThePreviousOne()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var modules = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.Request += (_, request) => { if (request.Url.Contains("/js/governance-portal.js?t=", StringComparison.Ordinal)) modules.Enqueue(request.Url); };
        await OpenGovernanceSandboxAsync(page);
        await page.EvaluateAsync("""
            () => {
              const previous = window.__governance;
              window.__previousGovernance = previous;
              window.addEventListener('pagehide', () => sessionStorage.setItem('__sandboxDisposed',
                String(previous.portal.state.dashboard === null)), { once: true });
            }
            """);
        await page.Locator("#reloadBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__governance !== window.__previousGovernance && window.__governance?.portal.state.load === 'ready'");
        Assert.Equal(2, modules.Distinct().Count());
        Assert.Equal("true", await page.EvaluateAsync<string>("() => sessionStorage.getItem('__sandboxDisposed')"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_LatestScopeWinsAgainstHeldDashboard()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-governance']").ClickAsync();
        await Expect(page.Locator("[data-gov-state='scanned']")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => { window.__governance.governanceApi.pause('dashboard'); window.__governance.pending = window.__governance.portal.reload(); }");
        await page.EvaluateAsync("() => window.__governance.governanceApi.whenPaused()");
        await page.Locator("#govScope").SelectOptionAsync("mine");
        await Expect(page.Locator(".gov-kpi").Filter(new() { HasText = "Governed assets" }).Locator(".gov-kpi-value")).ToHaveTextAsync("1/1");
        await page.EvaluateAsync("async () => { window.__governance.governanceApi.release(); await window.__governance.pending; }");
        await Expect(page.Locator("#govScope")).ToHaveValueAsync("mine");
        await Expect(page.Locator(".gov-kpi").Filter(new() { HasText = "Governed assets" }).Locator(".gov-kpi-value")).ToHaveTextAsync("1/1");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_ClosedTrendCannotReplaceAnotherJob()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenGovernanceSandboxAsync(page, "quality");
        await page.EvaluateAsync("() => window.__governance.dataQualityApi.pauseTrend('nightly_import')");
        await page.Locator("[data-view-dq-trend='nightly_import']").ClickAsync();
        await page.EvaluateAsync("() => window.__governance.dataQualityApi.whenPaused()");
        await page.Locator("#govDqTrendCloseBtn").ClickAsync();
        await Expect(page.Locator("[data-view-dq-trend='nightly_import']")).ToBeFocusedAsync();
        await page.Locator("[data-view-dq-trend='finance_load']").ClickAsync();
        await Expect(page.Locator("#govTrendModalTitle")).ToHaveTextAsync("finance_load");
        await Expect(page.Locator(".dq-trend-stats")).ToBeVisibleAsync();
        await page.EvaluateAsync("async () => { window.__governance.dataQualityApi.release(); await new Promise(requestAnimationFrame); }");
        await Expect(page.Locator("#govTrendModalTitle")).ToHaveTextAsync("finance_load");
        await page.Locator("#govDqTrendCloseBtn").PressAsync("Escape");
        await Expect(page.Locator("#govDqTrendDialog")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-view-dq-trend='finance_load']")).ToBeFocusedAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_LoadsAllJobTrendsAndKeepsFailedTrendsUnavailable()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenGovernanceSandboxAsync(page, "quality");
        await page.EvaluateAsync("async () => { window.__governance.dataQualityApi.setJobNames(['one','two','three','four','five','__proto__']); await window.__governance.portal.reload(); }");
        var row = page.Locator("tr").Filter(new() { Has = page.Locator("[data-view-dq-trend='__proto__']") });
        await Expect(row).ToContainTextAsync("Completed");
        await Expect(row).ToContainTextAsync("1,000");
        await page.EvaluateAsync("async () => { window.__governance.dataQualityApi.failTrend('__proto__'); await window.__governance.portal.reload(); }");
        await Expect(row).ToContainTextAsync("Unavailable");
        await Expect(row).Not.ToContainTextAsync("1,000");
        await Expect(page.Locator(".gov-kpi").Filter(new() { HasText = "Avg Failure Rate" }).Locator(".gov-kpi-value")).ToHaveTextAsync("—");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_UnavailableQualityCannotRetainCountsOrClaimEmpty()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-governance']").ClickAsync();
        await page.Locator("#fixtureSel").SelectOptionAsync("quality");
        await Expect(page.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Jobs Monitored" })).ToHaveAttributeAsync("aria-label", "Jobs Monitored: 2. Configured orchestrator pipelines");
        await page.EvaluateAsync("async () => { window.__governance.dataQualityApi.setUnavailable(true); await window.__governance.portal.reload(); }");
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "governance", "quality-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = path });
        await Expect(page.Locator("#stage")).ToContainTextAsync("Data quality evidence is unavailable");
        await Expect(page.Locator("#stage .gov-kpi").Filter(new() { HasText = "Jobs Monitored" }).Locator(".gov-kpi-value")).ToHaveTextAsync("—");
        await Expect(page.Locator("#stage")).Not.ToContainTextAsync("No matching jobs found.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_MalformedDashboardCannotClaimAnEmptyEstate()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-governance']").ClickAsync();
        await Expect(page.Locator("[data-gov-state='scanned']")).ToBeVisibleAsync();
        await page.EvaluateAsync("async () => { window.__governance.governanceApi.setMode('malformed'); await window.__governance.portal.reload(); }");
        await Expect(page.Locator("[data-gov-state='failed']")).ToBeVisibleAsync();
        await Expect(page.Locator("#stage .gov-kpis")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_DisposedReadCannotReplaceAnotherHost()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-governance']").ClickAsync();
        await Expect(page.Locator("[data-gov-state='scanned']")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => { window.__governance.governanceApi.pause('dashboard'); window.__governance.pending = window.__governance.portal.reload(); }");
        await page.EvaluateAsync("() => window.__governance.governanceApi.whenPaused()");
        await page.EvaluateAsync("async () => { const view = window.__governance; view.portal.dispose(); view.host.textContent = 'Replacement host'; view.governanceApi.release(); await view.pending; }");
        await Expect(page.Locator("#stage")).ToHaveTextAsync("Replacement host");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LineageCatalog_FailedProtectedSourcesCannotClaimEmptyInventory()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='lineage-catalog']").ClickAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await page.Locator("#fixtureSel").SelectOptionAsync("protected-unavailable");
        await Expect(page.Locator("#stage h2").First).ToHaveTextAsync("Protected Data & Tags");
        await Expect(page.Locator("#lineageResults .loading-state")).ToHaveCountAsync(0);
        var path = Path.Combine(RepoRoot(), "artifacts", "section1", "lineage", "protected-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = path });
        await Expect(page.Locator("#lineageResults")).ToContainTextAsync("Protected inventory unavailable");
        await Expect(page.Locator("#lineageResults")).Not.ToContainTextAsync("No protected data or sensitive columns recorded.");
        await Expect(page.Locator("#lineageResults .stewardship-metric").Filter(new() { HasText = "Protected observations" }).Locator("strong")).ToHaveTextAsync("—");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LineageCatalog_LateHistoryCannotOverwriteAnotherModeOrDisposedHost()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='lineage-catalog']").ClickAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await page.EvaluateAsync("() => window.__lineage.api.pause('lineage')");
        await page.Locator("#lineageQuery").FillAsync("ExecutiveKpis");
        await page.Locator("#lineageSearchForm button[type='submit']").ClickAsync();
        await page.EvaluateAsync("() => window.__lineage.api.whenPaused()");
        await page.Locator("[data-lineage-mode='stewardship']").ClickAsync();
        await Expect(page.Locator("#lineageResults .stewardship-overview")).ToBeVisibleAsync();
        await page.EvaluateAsync("async () => { await window.__lineage.api.release(); await new Promise(requestAnimationFrame); }");
        await Expect(page.Locator("#lineageResults .stewardship-overview")).ToBeVisibleAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(0);
        await page.EvaluateAsync("() => { window.__lineage.api.pause('lineage'); window.__lineage.catalog.setMode('history'); }");
        await page.EvaluateAsync("() => window.__lineage.api.whenPaused()");
        await page.EvaluateAsync("async () => { window.__lineage.catalog.dispose(); window.__lineage.host.textContent = 'Replacement host'; await window.__lineage.api.release(); await new Promise(requestAnimationFrame); }");
        await Expect(page.Locator("#stage")).ToHaveTextAsync("Replacement host");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LineageCatalog_ProtectedCountsUseTheActualServerShape()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='lineage-catalog']").ClickAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await page.Locator("[data-lineage-mode='protected']").ClickAsync();
        await Expect(page.Locator("#lineageResults .stewardship-metric").Filter(new() { HasText = "Protected observations" }).Locator("strong")).ToHaveTextAsync("1");
        await Expect(page.Locator("#lineageResults .stewardship-metric").Filter(new() { HasText = "Sensitive" }).Locator("strong")).ToHaveTextAsync("1");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LineageCatalog_MalformedRowsClearExportAndEditedFiltersInvalidatePendingHistory()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='lineage-catalog']").ClickAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await page.EvaluateAsync("() => window.__lineage.api.setMode('malformed')");
        await page.Locator("#lineageSearchForm button[type='submit']").ClickAsync();
        await Expect(page.Locator("#lineageResults")).ToContainTextAsync("Lineage query unavailable");
        await Expect(page.Locator("#lineageExportBtn")).ToBeDisabledAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(0);
        await page.EvaluateAsync("() => { window.__lineage.api.setMode('healthy'); window.__lineage.api.pause('lineage'); }");
        await page.Locator("#lineageSearchForm button[type='submit']").ClickAsync();
        await page.EvaluateAsync("() => window.__lineage.api.whenPaused()");
        await page.Locator("#lineageQuery").FillAsync("Changed draft");
        await page.EvaluateAsync("async () => { await window.__lineage.api.release(); await new Promise(requestAnimationFrame); }");
        await Expect(page.Locator("#lineageQuery")).ToHaveValueAsync("Changed draft");
        await Expect(page.Locator("#lineageResults")).ToContainTextAsync("Run the query to inspect these filters.");
        await Expect(page.Locator("#lineageExportBtn")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LineageCatalog_KeyboardModesFacetsAndImpactRespectTheirContracts()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='lineage-catalog']").ClickAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await page.Locator("[data-lineage-mode='history']").FocusAsync();
        await page.Locator("[data-lineage-mode='history']").PressAsync("ArrowRight");
        await Expect(page.Locator("[data-lineage-mode='stewardship']")).ToBeFocusedAsync();
        await Expect(page.Locator("#stewardshipSteward option[value='Maria Chen']")).ToHaveCountAsync(1);
        await Expect(page.Locator("#lineageSearchForm")).ToBeHiddenAsync();
        await Expect(page.Locator("#stewardshipSearchForm")).ToBeVisibleAsync();
        await page.Locator("[data-lineage-mode='stewardship']").PressAsync("ArrowRight");
        await Expect(page.Locator("[data-lineage-mode='protected']")).ToBeFocusedAsync();
        await Expect(page.Locator("#protectedSteward option[value='Maria Chen']")).ToHaveCountAsync(1);
        await page.Locator("[data-lineage-mode='protected']").PressAsync("End");
        await Expect(page.Locator("[data-lineage-mode='impact']")).ToBeFocusedAsync();
        await page.Locator("#impactName").FillAsync("edw.Sales");
        await page.Locator("#impactSearchForm button[type='submit']").ClickAsync();
        await Expect(page.Locator("#lineageResults .impact-sections")).ToContainTextAsync("Protected data alert");
        await page.EvaluateAsync("() => window.__lineage.api.setMode('wrong-impact')");
        await page.Locator("#impactSearchForm button[type='submit']").ClickAsync();
        await Expect(page.Locator("#lineageResults")).ToContainTextAsync("Impact query unavailable");
        await Expect(page.Locator("#lineageResults .impact-overview")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LineageCatalog_PartialAuditKeepsUnavailableSourcesDistinctFromEmpty()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='lineage-catalog']").ClickAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await page.EvaluateAsync("() => window.__lineage.api.setMode('unavailable')");
        await page.Locator("[data-lineage-mode='audit']").ClickAsync();
        await Expect(page.Locator("#lineageResults")).ToContainTextAsync("Protected inventory unavailable");
        await Expect(page.Locator("#lineageResults .stewardship-metric").Filter(new() { HasText = "Outbox pending" }).Locator("strong")).ToHaveTextAsync("—");
        await Expect(page.Locator("#lineageResults .stewardship-metric").Filter(new() { HasText = "Impact events" }).Locator("strong")).ToHaveTextAsync("2");
        await Expect(page.Locator("#lineageResults")).Not.ToContainTextAsync("No protected lineage is currently recorded.");
        await page.Locator("#fixtureSel").SelectOptionAsync("reader");
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await page.EvaluateAsync("() => window.__lineage.catalog.setMode('audit')");
        await Expect(page.Locator("[data-lineage-mode='audit']")).ToHaveCountAsync(0);
        Assert.False(await page.EvaluateAsync<bool>("() => window.__lineage.api.calls.some(call => call.kind === 'auditLog' || call.kind === 'operationalMetrics')"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LineageCatalog_GraphNamesCannotCollideWithObjectPropertiesOrEdgeSeparators()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='lineage-catalog']").ClickAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await page.EvaluateAsync("() => window.__lineage.api.setMode('graph-names')");
        await page.Locator("#lineageSearchForm button[type='submit']").ClickAsync();
        await Expect(page.Locator("#lineageResults")).ToContainTextAsync("__proto__");
        await page.Locator("#lineageViewToggle").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => { const graph = window.__lineage.api.graphs.at(-1); return graph.nodes.length === 5 && graph.nodes.some(node => node.label === '__proto__') && graph.edges.length === 3 && graph.edges.every(edge => graph.nodes.some(node => node.id === edge.source) && graph.nodes.some(node => node.id === edge.target)); }"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LineageCatalog_SavedViewsKeepVisibleFiltersAndStayWithTheirIdentity()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.AddInitScriptAsync("() => sessionStorage.setItem('etlsql_token', 'x.' + btoa(JSON.stringify({iss:'sandbox', tenant_id:'tenant-a', sub:'alice'})) + '.x')");
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='lineage-catalog']").ClickAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await page.Locator("#lineageQuery").FillAsync("mart.ExecutiveKpis");
        await page.Locator("#lineageSaveViewBtn").ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Save lineage view" });
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByLabel("View name", new() { Exact = true }).FillAsync("My KPI view");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save view", Exact = true }).ClickAsync();
        await Expect(page.Locator("#lineageSavedView")).ToHaveValueAsync("My KPI view");
        await Expect(page.Locator("#lineageQuery")).ToHaveValueAsync("mart.ExecutiveKpis");
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(1);
        await page.EvaluateAsync("() => sessionStorage.setItem('etlsql_token', 'x.' + btoa(JSON.stringify({iss:'sandbox', tenant_id:'tenant-a', sub:'bob'})) + '.x')");
        await page.Locator("#reloadBtn").ClickAsync();
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(4);
        await Expect(page.Locator("#lineageSavedView option[value='My KPI view']")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DatasetsAdmin_UnavailableAndMalformedRowsCannotRetainCountsOrClaimEmpty()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='datasets-admin']").ClickAsync();
        await Expect(page.Locator("#datasetsTableWrap tbody tr")).ToHaveCountAsync(3);
        await page.Locator("[data-action='view'][data-id='1']").ClickAsync();
        await Expect(page.Locator("#dv-totalCount")).ToHaveTextAsync("48");
        await page.EvaluateAsync("() => window.__datasets.api.setMode('data-unavailable')");
        await page.Locator("#dv-search").FillAsync("unavailable");
        await page.Locator("#dv-search").PressAsync("Enter");
        await Expect(page.Locator("#dv-tableWrap")).ToContainTextAsync("Dataset cache temporarily unavailable.");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "datasets-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#datasetViewerModal").ScreenshotAsync(new() { Path = imagePath });
        await Expect(page.Locator("#dv-totalCount")).ToHaveTextAsync("—");
        await Expect(page.Locator("#dv-filteredCount")).ToHaveTextAsync("—");
        await Expect(page.Locator("#dv-nextBtn")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => window.__datasets.api.setMode('bad-data')");
        await page.Locator("#dv-search").PressAsync("Enter");
        await Expect(page.Locator("#dv-tableWrap")).ToContainTextAsync("Dataset rows are unavailable or incomplete.");
        await Expect(page.Locator("#dv-tableWrap")).Not.ToContainTextAsync("No rows match");
        await Expect(page.Locator("#dv-filteredCount")).ToHaveTextAsync("—");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DatasetsAdmin_ClosedViewerCannotReplaceTheNextDatasetsRows()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='datasets-admin']").ClickAsync();
        await Expect(page.Locator("#datasetsTableWrap tbody tr")).ToHaveCountAsync(3);
        await page.EvaluateAsync("() => { window.__datasets.api.setMode('target-distinct'); window.__datasets.api.pause('data'); }");
        await page.Locator("[data-action='view'][data-id='1']").ClickAsync();
        await page.EvaluateAsync("() => window.__datasets.api.whenPaused('data')");
        await page.Locator("#dv-closeBtn").ClickAsync();
        await page.Locator("[data-action='view'][data-id='2']").ClickAsync();
        await Expect(page.Locator("#dv-title")).ToHaveTextAsync("customer_360");
        await Expect(page.Locator("#dv-tableWrap tbody tr").First).ToContainTextAsync("Customer snapshot");
        await page.EvaluateAsync("() => window.__datasets.api.release()");
        await Expect(page.Locator("#dv-tableWrap tbody tr").First).ToContainTextAsync("Customer snapshot");
        await page.EvaluateAsync("() => { window.__datasets.api.pause('list'); window.__oldDatasetLoad = window.__datasets.surface.load(); }");
        await page.EvaluateAsync("() => window.__datasets.api.whenPaused('list')");
        await page.EvaluateAsync("async () => { window.__datasets.surface.dispose(); window.__datasets.api.release(); await window.__oldDatasetLoad; }");
        await Expect(page.Locator("#datasetsTableWrap")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DatasetsAdmin_SaveKeepsTheNextFormAndUnconfirmedWritesRequireRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='datasets-admin']").ClickAsync();
        await Expect(page.Locator("#datasetsTableWrap tbody tr")).ToHaveCountAsync(3);
        await page.Locator("[data-action='edit'][data-id='1']").ClickAsync();
        await page.Locator("#ds-ttl").FillAsync("30m");
        await page.EvaluateAsync("() => window.__datasets.api.pause('update')");
        await page.Locator("#ds-saveBtn").ClickAsync();
        await page.EvaluateAsync("() => window.__datasets.api.whenPaused('update')");
        await page.EvaluateAsync("() => document.querySelector('#ds-saveBtn').dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__datasets.api.calls.filter(call => call.kind === 'update').length"));
        await page.Locator("#ds-cancelBtn").ClickAsync();
        await page.Locator("[data-action='edit'][data-id='2']").ClickAsync();
        await page.Locator("#ds-ttl").FillAsync("2h");
        await page.EvaluateAsync("() => window.__datasets.api.release()");
        await Expect(page.Locator("[data-action='edit'][data-id='1']")).ToBeVisibleAsync();
        await Expect(page.Locator("#editDatasetForm")).ToBeVisibleAsync();
        await Expect(page.Locator("#ds-id")).ToHaveValueAsync("2");
        await Expect(page.Locator("#ds-ttl")).ToHaveValueAsync("2h");
        await page.Locator("#ds-cancelBtn").ClickAsync();
        await page.EvaluateAsync("() => window.__datasets.api.setMode('invalid-update')");
        await page.Locator("[data-action='edit'][data-id='1']").ClickAsync();
        await page.Locator("#ds-ttl").FillAsync("45m");
        await page.Locator("#ds-saveBtn").ClickAsync();
        await Expect(page.Locator("#datasetsStatus")).ToContainTextAsync("outcome is unconfirmed");
        await Expect(page.Locator("#ds-saveBtn")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => window.__datasets.api.setMode('healthy')");
        await page.Locator("#datasetsRefreshBtn").ClickAsync();
        await page.Locator("[data-action='edit'][data-id='1']").ClickAsync();
        await Expect(page.Locator("#ds-ttl")).ToHaveValueAsync("45m");
        await Expect(page.Locator("#ds-saveBtn")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DatasetsAdmin_PagingSortFiltersAndExportsKeepTheViewerQuery()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='datasets-admin']").ClickAsync();
        await Expect(page.Locator("#datasetsTableWrap tbody tr")).ToHaveCountAsync(3);
        await page.Locator("[data-action='view'][data-id='1']").ClickAsync();
        await Expect(page.Locator("#dv-totalCount")).ToHaveTextAsync("48");
        await page.Locator("#dv-pageSize").SelectOptionAsync("25");
        await Expect(page.Locator("#dv-pageInfo")).ToHaveTextAsync("Page 1 of 2");
        await page.Locator("#dv-nextBtn").ClickAsync();
        await Expect(page.Locator("#dv-pageInfo")).ToHaveTextAsync("Page 2 of 2");
        await Expect(page.Locator("#dv-tableWrap tbody tr")).ToHaveCountAsync(23);
        await page.Locator(".dv-col-header[data-col='units']").ClickAsync();
        await Expect(page.Locator("#dv-pageInfo")).ToHaveTextAsync("Page 1 of 2");
        await page.Locator(".dv-filter-op[data-col='units']").SelectOptionAsync("gte");
        await page.Locator(".dv-filter-val[data-col='units']").FillAsync("100");
        await page.Locator(".dv-filter-val[data-col='units']").PressAsync("Enter");
        await Expect(page.Locator("#dv-filter-badge")).ToHaveTextAsync("1 filter");
        Assert.True(await page.EvaluateAsync<bool>("() => [...document.querySelectorAll('#dv-tableWrap tbody tr')].every(row => Number(row.children[2].textContent) >= 100)"));
        await page.Locator("#dv-exportBtn").ClickAsync();
        await page.Locator("#dv-exportXlsxBtn").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => ['exportCsv', 'exportXlsx'].every(kind => {
              const call = window.__datasets.api.calls.find(call => call.kind === kind);
              return call.args[0] === 1 && call.args[2].sort === 'units' && call.args[2].dir === 'asc'
                && call.args[2].filters[0].col === 'units' && call.args[2].filters[0].op === 'gte' && call.args[2].filters[0].val === '100';
            })
            """));
        await page.Locator("#dv-closeBtn").PressAsync("Escape");
        await Expect(page.Locator("#datasetViewerModal")).ToBeHiddenAsync();
        await Expect(page.Locator("[data-action='view'][data-id='1']")).ToBeFocusedAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DatasetsAdmin_ValuePickerKeyboardAndLateSearchStayWithTheCurrentColumn()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='datasets-admin']").ClickAsync();
        await page.Locator("[data-action='view'][data-id='1']").ClickAsync();
        await Expect(page.Locator("#dv-totalCount")).ToHaveTextAsync("48");
        var regionPicker = page.Locator(".dv-picker-btn[data-col='region']");
        await regionPicker.ClickAsync();
        await Expect(page.Locator("#dv-pickerList input")).ToHaveCountAsync(4);
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "datasets", "value-picker.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#datasetViewerModal").ScreenshotAsync(new() { Path = imagePath });
        await Expect(page.Locator("#dv-pickerSearch")).ToBeFocusedAsync();
        await page.Locator("#dv-pickerSearch").PressAsync("Escape");
        await Expect(page.Locator("#dv-pickerPopover")).ToBeHiddenAsync();
        await Expect(page.Locator("#datasetViewerModal")).ToBeVisibleAsync();
        await Expect(regionPicker).ToBeFocusedAsync();
        await regionPicker.ClickAsync();
        await page.EvaluateAsync("() => window.__datasets.api.pause('columnValues')");
        await page.Locator("#dv-pickerSearch").FillAsync("North");
        await page.EvaluateAsync("() => window.__datasets.api.whenPaused('columnValues')");
        await page.Locator("#dv-pickerSearch").FillAsync("South");
        await Expect(page.Locator("#dv-pickerList")).ToHaveTextAsync("South");
        await page.EvaluateAsync("() => window.__datasets.api.release()");
        await Expect(page.Locator("#dv-pickerList")).ToHaveTextAsync("South");
        await page.Locator("#dv-pickerList input").CheckAsync();
        await page.Locator("#dv-pickerApply").ClickAsync();
        await Expect(page.Locator("#dv-filteredCount")).ToHaveTextAsync("12");
        Assert.True(await page.EvaluateAsync<bool>("() => [...document.querySelectorAll('#dv-tableWrap tbody tr')].every(row => row.children[0].textContent === 'South')"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DatasetsAdmin_IncompleteStatisticsClearTheFooterAndValuesStayLiteral()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='datasets-admin']").ClickAsync();
        await page.Locator("[data-action='view'][data-id='1']").ClickAsync();
        await Expect(page.Locator("#dv-tableWrap tfoot td")).ToHaveCountAsync(7);
        await page.EvaluateAsync("() => window.__datasets.api.setMode('bad-stats')");
        await page.Locator("#dv-resetBtn").ClickAsync();
        await Expect(page.Locator("#dv-statsStatus")).ToContainTextAsync("Statistics unavailable");
        await Expect(page.Locator("#dv-tableWrap tfoot")).ToHaveCountAsync(0);
        await Expect(page.Locator("#dv-totalCount")).ToHaveTextAsync("48");
        await page.EvaluateAsync("() => window.__datasets.api.setMode('stats-literal')");
        await page.Locator("#dv-resetBtn").ClickAsync();
        var units = page.Locator("#dv-tableWrap tfoot td").Nth(2);
        await Expect(units).ToContainTextAsync("<img src=x onerror=");
        await Expect(units).ToContainTextAsync("Max —");
        await Expect(units).ToContainTextAsync("Avg —");
        await Expect(units.Locator("img")).ToHaveCountAsync(0);
        Assert.False(await page.EvaluateAsync<bool>("() => window.__datasetInjected === true"));
        await page.EvaluateAsync("() => window.__datasets.api.setMode('stats-unavailable')");
        await page.Locator("#dv-resetBtn").ClickAsync();
        await Expect(page.Locator("#dv-statsStatus")).ToContainTextAsync("temporarily unavailable");
        await Expect(page.Locator("#dv-tableWrap tfoot")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DatasetsAdmin_ClosedPermissionsAndLineageIgnoreLateResponses()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='datasets-admin']").ClickAsync();
        await Expect(page.Locator("#datasetsTableWrap tbody tr")).ToHaveCountAsync(3);
        await page.EvaluateAsync("() => window.__datasets.catalog.pause('lineage')");
        await page.Locator("[data-action='lineage'][data-id='1']").ClickAsync();
        await page.EvaluateAsync("() => window.__datasets.catalog.whenPaused('lineage')");
        await page.Locator("#dsLineageCloseBtn").PressAsync("Escape");
        await Expect(page.Locator("#datasetLineageModal")).ToBeHiddenAsync();
        await page.EvaluateAsync("() => window.__datasets.catalog.release()");
        await Expect(page.Locator("#dsLineageDag svg")).ToHaveCountAsync(0);
        await page.EvaluateAsync("() => window.__datasets.api.pause('listAcl')");
        await page.Locator("[data-action='acl'][data-id='1']").ClickAsync();
        await page.EvaluateAsync("() => window.__datasets.api.whenPaused('listAcl')");
        await page.Locator("#dsAclCloseBtn").ClickAsync();
        await page.EvaluateAsync("() => window.__datasets.api.release()");
        await Expect(page.Locator("#dsAclTableWrap tr")).ToHaveCountAsync(0);
        await page.EvaluateAsync("() => window.__datasets.api.setMode('bad-acl')");
        await page.Locator("[data-action='acl'][data-id='2']").ClickAsync();
        await Expect(page.Locator("#dsAclTableWrap")).ToContainTextAsync("Permissions unavailable");
        await Expect(page.Locator("#dsAclTableWrap [data-uid], #dsAclTableWrap [data-gid]")).ToHaveCountAsync(0);
        await page.EvaluateAsync("() => window.__datasets.catalog.setMode('bad-lineage')");
        await page.Locator("[data-action='lineage'][data-id='2']").ClickAsync();
        await Expect(page.Locator("#dsLineageDag")).ToContainTextAsync("Lineage unavailable");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DataQualityQueue_FailedSourcesCannotClaimEmptyOrRetainCounts()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='data-quality-queue']").ClickAsync();
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(5);
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('trend-unavailable')");
        await page.Locator("[data-trend-job='nightly_import']").First.ClickAsync();
        await Expect(page.Locator("#stage [role='dialog']")).ToContainTextAsync("Quality trend temporarily unavailable.");
        await Expect(page.Locator("#stage [role='dialog']")).ToContainTextAsync("Quality trend unavailable");
        await Expect(page.Locator("#stage [role='dialog']")).Not.ToContainTextAsync("No recorded runs");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "data-quality-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage [role='dialog']").ScreenshotAsync(new() { Path = imagePath });
        await page.Locator("#dqTrendClose").ClickAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('missing-queue')");
        await page.Locator("#dqRefreshBtn").ClickAsync();
        await Expect(page.Locator("#stage .dq-summary")).ToContainTextAsync("— targets");
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(0);
        await Expect(page.Locator("#stage")).ToContainTextAsync("Quarantine queue unavailable");
        await Expect(page.Locator("#stage")).Not.ToContainTextAsync("No quarantine manifests");
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('jobs-unavailable')");
        await page.Locator("#dqRefreshBtn").ClickAsync();
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(5);
        await Expect(page.Locator("#stage")).ToContainTextAsync("Job inventory temporarily unavailable.");
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('rules-unavailable')");
        await page.Locator("[data-trend-job='nightly_import']").First.ClickAsync();
        await Expect(page.Locator("#stage [role='dialog']")).ToContainTextAsync("Average over 6 run(s)");
        await Expect(page.Locator("#stage [role='dialog']")).ToContainTextAsync("Rule definitions are unavailable.");
        await Expect(page.Locator("#stage [role='dialog']")).Not.ToContainTextAsync("No readable rule definitions");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DataQualityQueue_ClosedAndSupersededPanelsKeepTheCurrentTargetAndFilter()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='data-quality-queue']").ClickAsync();
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(5);
        await page.EvaluateAsync("() => window.__dataQuality.api.pause('quarantineRows')");
        await page.Locator("[data-review-target='warehouse.quarantine_users']").ClickAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.whenPaused('quarantineRows')");
        await page.Locator("#dqCloseRows").PressAsync("Escape");
        await Expect(page.Locator("[data-review-target='warehouse.quarantine_users']")).ToBeFocusedAsync();
        await page.Locator("[data-review-target='warehouse.quarantine_orders']").ClickAsync();
        await Expect(page.Locator("#dqRowsModalTitle")).ToHaveTextAsync("warehouse.quarantine_orders");
        await Expect(page.Locator("[data-edit-row='a1'][data-edit-column='Email']")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.release()");
        await page.Locator("[data-edit-row='a1'][data-edit-column='Email']").FillAsync("retained@example.com");
        await Expect(page.Locator("#dqRowsModalTitle")).ToHaveTextAsync("warehouse.quarantine_orders");
        await Expect(page.Locator("[data-edit-row='a1'][data-edit-column='Email']")).ToHaveValueAsync("retained@example.com");
        await page.EvaluateAsync("() => window.__dataQuality.api.pause('quarantineRows')");
        await page.Locator("#dqRowsStatus").SelectOptionAsync("all");
        await page.EvaluateAsync("() => window.__dataQuality.api.whenPaused('quarantineRows')");
        await page.Locator("#dqRowsStatus").SelectOptionAsync("released");
        await Expect(page.Locator("[data-edit-row='d4']")).ToHaveCountAsync(3);
        await page.EvaluateAsync("() => window.__dataQuality.api.release()");
        await Expect(page.Locator("#dqRowsStatus")).ToHaveValueAsync("released");
        await Expect(page.Locator("[data-edit-row='a1']")).ToHaveCountAsync(0);
        await page.Locator("#dqCloseRows").ClickAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.pause('qualityTrend')");
        await page.Locator("[data-trend-job='nightly_import']").First.ClickAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.whenPaused('qualityTrend')");
        await page.Locator("#dqTrendClose").ClickAsync();
        await page.Locator("[data-trend-job='finance_load']").ClickAsync();
        await Expect(page.Locator("#dqTrendModalTitle")).ToHaveTextAsync("finance_load");
        await Expect(page.Locator("#stage [role='dialog']")).ToContainTextAsync("Average over 6 run(s)");
        await page.EvaluateAsync("() => window.__dataQuality.api.release()");
        await Expect(page.Locator("#dqTrendModalTitle")).ToHaveTextAsync("finance_load");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DataQualityQueue_UnconfirmedWritesRequireRefreshAndCannotDuplicate()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='data-quality-queue']").ClickAsync();
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(5);
        await page.EvaluateAsync("() => { window.__dataQuality.api.setMode('invalid-receipt'); window.__dataQuality.api.pause('replayQuarantine'); }");
        await page.Locator("[data-replay-target='warehouse.quarantine_users']").ClickAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.whenPaused('replayQuarantine')");
        await page.EvaluateAsync("() => document.querySelector('[data-replay-target=\"warehouse.quarantine_users\"]').dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__dataQuality.api.calls.filter(call => call.kind === 'replayQuarantine').length"));
        await page.EvaluateAsync("() => window.__dataQuality.api.release()");
        await Expect(page.Locator("#stage")).ToContainTextAsync("Replay outcome is unconfirmed.");
        await Expect(page.Locator("#stage .dq-job-row")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-replay-target='warehouse.quarantine_users']")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('healthy')");
        await page.Locator("#dqRefreshBtn").ClickAsync();
        await Expect(page.Locator("[data-replay-target='warehouse.quarantine_users']")).ToBeEnabledAsync();
        await page.Locator("[data-review-target='warehouse.quarantine_users']").ClickAsync();
        await Expect(page.Locator("[data-edit-row='a1'][data-edit-column='Email']")).ToBeVisibleAsync();
        await page.Locator("[data-edit-row='a1'][data-edit-column='Email']").FillAsync("fixed@example.com");
        await page.Locator("[data-note-row='a1']").FillAsync("Reviewed source evidence");
        await page.Locator("[data-release-row='a1']").ClickAsync();
        await Expect(page.Locator("#stage .dq-job-row .dq-status")).ToHaveTextAsync("Completed");
        Assert.True(await page.EvaluateAsync<bool>("() => { const body = window.__dataQuality.api.calls.find(call => call.kind === 'updateQuarantineDisposition').args[0]; return body.quarantineTarget === 'warehouse.quarantine_users' && body.rowIds[0] === 'a1' && body.changes.Email === 'fixed@example.com' && body.note === 'Reviewed source evidence'; }"));
        await Expect(page.Locator("[data-edit-row='a1'][data-edit-column='Email']")).ToHaveValueAsync("fixed@example.com");
        await Expect(page.Locator("[data-note-row='a1']")).ToHaveValueAsync("Reviewed source evidence");
        await Expect(page.Locator("#stage [role='dialog']")).ToContainTextAsync("Reload rows to inspect the result");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DataQualityQueue_WrongJobStatusAndDisposedResponsesCannotAnnounceCompletion()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='data-quality-queue']").ClickAsync();
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(5);
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('invalid-status')");
        await page.Locator("[data-replay-target='warehouse.quarantine_users']").ClickAsync();
        await Expect(page.Locator("#stage .dq-job-row")).ToContainTextAsync("Job status is unavailable or does not match this submission.");
        await Expect(page.Locator("#stage .dq-job-row .dq-status")).ToHaveTextAsync("Status unavailable");
        await Expect(page.Locator("[data-replay-target='warehouse.quarantine_users']")).ToBeDisabledAsync();
        await page.Locator("#dqQueueSearch").FillAsync("draft filter");
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('healthy')");
        await Expect(page.Locator("#stage .dq-job-row .dq-status")).ToHaveTextAsync("Completed");
        await Expect(page.Locator("#dqQueueSearch")).ToHaveValueAsync("draft filter");
        await Expect(page.Locator("#dqQueueSearch")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.pause('qualityTrend')");
        await page.Locator("[data-trend-job='nightly_import']").First.ClickAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.whenPaused('qualityTrend')");
        await page.EvaluateAsync("async () => { window.__dataQuality.queue.dispose(); window.__dataQuality.api.release(); await window.__dataQuality.queue.show(); }");
        await Expect(page.Locator("#stage .dq-page")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DataQualityQueue_RowAndColumnNamesCannotChangeObjectPrototypes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='data-quality-queue']").ClickAsync();
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(5);
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('prototype-keys')");
        await page.Locator("[data-review-target='warehouse.quarantine_users']").ClickAsync();
        var source = page.Locator("[data-edit-row='__proto__'][data-edit-column='__proto__']");
        await Expect(source).ToBeVisibleAsync();
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "data-quality-source-keys.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage [role='dialog']").ScreenshotAsync(new() { Path = imagePath });
        await Expect(source).ToHaveValueAsync("Original source value");
        await page.Locator("[data-edit-row='__proto__'][data-edit-column='Email']").FillAsync("safe@example.com");
        await source.FillAsync("Corrected source value");
        await page.Locator("[data-note-row='__proto__']").FillAsync("Reviewed unusual column names");
        Assert.True(await page.EvaluateAsync<bool>("() => !Object.hasOwn(Object.prototype, 'Email') && !Object.hasOwn(Object.prototype, '__dq_row_id')"));
        await page.Locator("[data-release-row='__proto__']").ClickAsync();
        await Expect(page.Locator("#stage .dq-job-row .dq-status")).ToHaveTextAsync("Completed");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
              const body = window.__dataQuality.api.calls.find(call => call.kind === 'updateQuarantineDisposition').args[0];
              return body.rowIds[0] === '__proto__' && body.changes.Email === 'safe@example.com'
                && Object.hasOwn(body.changes, '__proto__') && body.changes.__proto__ === 'Corrected source value'
                && body.note === 'Reviewed unusual column names' && body.expectedStatus === 'quarantined';
            }
            """));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DataQualityQueue_RevokedPreviewCannotRetainRowsOrApplyALateResponse()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='data-quality-queue']").ClickAsync();
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(5);
        await page.Locator("[data-review-target='warehouse.quarantine_users']").ClickAsync();
        await Expect(page.Locator("[data-edit-row='a1'][data-edit-column='Email']")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.pause('quarantineRows')");
        await page.Locator("#dqReloadRows").ClickAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.whenPaused('quarantineRows')");
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('rows-revoked')");
        await page.EvaluateAsync("async () => await window.__dataQuality.queue.show()");
        await Expect(page.Locator("#stage [data-dq-dialog='rows']")).ToContainTextAsync("Preview permission has been revoked.");
        await Expect(page.Locator("#dqReloadRows")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.release()");
        await Expect(page.Locator("[data-edit-row='a1']")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-release-row]")).ToHaveCountAsync(0);
        await Expect(page.Locator("#stage [data-dq-dialog='rows']")).ToContainTextAsync("rows unavailable");
        await page.EvaluateAsync("() => window.__dataQuality.api.setMode('healthy')");
        await page.EvaluateAsync("async () => await window.__dataQuality.queue.show()");
        await Expect(page.Locator("#dqReloadRows")).ToBeEnabledAsync();
        await page.Locator("#dqReloadRows").ClickAsync();
        await Expect(page.Locator("[data-edit-row='a1'][data-edit-column='Email']")).ToBeVisibleAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DataQualityQueue_RestoredJobsAreRevalidatedAndIsolatedByIdentity()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='data-quality-queue']").ClickAsync();
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(5);
        await page.EvaluateAsync("""
            async () => {
              const host = document.querySelector('#stage .dq-page').parentElement;
              window.__dataQuality.queue.dispose();
              sessionStorage.setItem('etlsql_token', 'header.' + btoa(JSON.stringify({ sub: 'steward-a', iss: 'fixture', tenant_id: 'tenant-a' })) + '.signature');
              const { createDataQualityQueue } = await import('/src/ETL-SQL.Portal/wwwroot/js/data-quality-queue.js');
              window.__qualityHost = host;
              window.__qualityFactory = createDataQualityQueue;
              window.__dataQuality.queue = createDataQualityQueue({ host, dataQualityApi: window.__dataQuality.api });
              await window.__dataQuality.queue.show();
            }
            """);
        await page.Locator("[data-replay-target='warehouse.quarantine_users']").ClickAsync();
        await Expect(page.Locator("#stage .dq-job-row .dq-status")).ToHaveTextAsync("Completed");
        await page.EvaluateAsync("""
            async () => {
              window.__dataQuality.queue.dispose();
              window.__dataQuality.api.setMode('invalid-status');
              window.__dataQuality.queue = window.__qualityFactory({ host: window.__qualityHost, dataQualityApi: window.__dataQuality.api });
              await window.__dataQuality.queue.show();
            }
            """);
        await Expect(page.Locator("#stage .dq-job-row .dq-status")).ToHaveTextAsync("Status unavailable");
        await Expect(page.Locator("#stage .dq-job-row")).ToContainTextAsync("does not match this submission");
        await page.EvaluateAsync("""
            async () => {
              sessionStorage.setItem('etlsql_token', 'header.' + btoa(JSON.stringify({ sub: 'steward-b', iss: 'fixture', tenant_id: 'tenant-a' })) + '.signature');
              window.__dataQuality.queue = window.__qualityFactory({ host: window.__qualityHost, dataQualityApi: window.__dataQuality.api });
              await window.__dataQuality.queue.show();
            }
            """);
        await Expect(page.Locator("#stage .dq-row")).ToHaveCountAsync(5);
        await Expect(page.Locator("#stage .dq-job-row")).ToHaveCountAsync(0);
        await page.EvaluateAsync("() => window.__dataQuality.api.pause('quarantineQueue')");
        await page.Locator("#dqRefreshBtn").ClickAsync();
        await page.EvaluateAsync("() => window.__dataQuality.api.whenPaused('quarantineQueue')");
        await page.EvaluateAsync("""
            () => {
              sessionStorage.setItem('etlsql_token', 'header.' + btoa(JSON.stringify({ sub: 'steward-b', iss: 'fixture', tenant_id: 'tenant-b' })) + '.signature');
              window.__dataQuality.api.release();
            }
            """);
        await Expect(page.Locator("#stage .dq-page")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConnectionsAdmin_RetainsGatewayBindingAndRejectsIncompleteDiagnostics()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='connections-admin']").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync("4 shared connection(s).");
        var row = page.Locator("#conn-tableWrap tr[data-alias='gateway_sales']");
        await row.GetByRole(AriaRole.Button, new() { Name = "Detail", Exact = true }).ClickAsync();
        await Expect(page.Locator("#conn-detailBody")).ToContainTextAsync("hq-gateway");
        await page.Locator("#conn-saveBtn").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToContainTextAsync("saved.");
        Assert.True(await page.EvaluateAsync<bool>("() => { const body = window.__connections.api.calls.find(call => call.kind === 'set').args[1]; return body.gateway.gatewayId === 'hq-gateway' && body.gateway.resourceId === 'sales_read' && !body.target && Object.keys(body.options).length === 0; }"));
        await page.EvaluateAsync("() => window.__connections.api.setMode('invalid-diagnostic')");
        await row.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true }).ClickAsync();
        await Expect(page.Locator("#conn-detailBody")).ToHaveTextAsync("Connection diagnostic unavailable.");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "connections-diagnostic.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConnectionsAdmin_SupersededReadsAndClosedPanelsCannotReplaceCurrentEdits()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='connections-admin']").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync("4 shared connection(s).");
        await page.EvaluateAsync("() => window.__connections.api.pause('detail')");
        await page.Locator("tr[data-alias='sales_dw'] button[data-act='detail']").ClickAsync();
        await page.EvaluateAsync("() => window.__connections.api.whenPaused('detail')");
        await page.Locator("#conn-detailCloseBtn").ClickAsync();
        await page.Locator("#conn-alias").FillAsync("my_new_definition");
        await page.EvaluateAsync("() => window.__connections.api.release()");
        await Expect(page.Locator("#conn-detailCard")).ToBeHiddenAsync();
        await Expect(page.Locator("#conn-alias")).ToHaveValueAsync("my_new_definition");
        await page.EvaluateAsync("() => window.__connections.api.pause('detail')");
        await page.Locator("tr[data-alias='sales_dw'] button[data-act='detail']").ClickAsync();
        await page.EvaluateAsync("() => window.__connections.api.whenPaused('detail')");
        await page.Locator("#conn-alias").FillAsync("editing_while_loading");
        await page.EvaluateAsync("() => window.__connections.api.release()");
        await Expect(page.Locator("#conn-detailBody")).ToContainTextAsync("sql01");
        await Expect(page.Locator("#conn-alias")).ToHaveValueAsync("editing_while_loading");
        await page.EvaluateAsync("() => { const mock = window.__connections; mock.api.pause('list'); window.__oldConnectionRead = mock.surface.load(); }");
        await page.EvaluateAsync("() => window.__connections.api.whenPaused('list')");
        await page.EvaluateAsync("async () => { window.__connections.api.setMode('unavailable'); await window.__connections.surface.load(); }");
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync("Connection catalog unavailable.");
        await page.EvaluateAsync("async () => { window.__connections.api.release(); await window.__oldConnectionRead; }");
        await Expect(page.Locator("#conn-tableWrap")).ToContainTextAsync("connection catalog is unavailable");
        await Expect(page.Locator("#conn-tableWrap")).ToContainTextAsync("catalog could not be loaded or validated");
        await Expect(page.Locator("#conn-saveBtn")).ToBeDisabledAsync();
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "connections-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConnectionsAdmin_DuplicateSavesAndUnknownImportOutcomesNeedExplicitRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='connections-admin']").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync("4 shared connection(s).");
        await page.Locator("#conn-alias").FillAsync("new_connection");
        await page.Locator("#conn-options").FillAsync("SERVER=sql01");
        await page.EvaluateAsync("() => window.__connections.api.pause('set')");
        await page.Locator("#conn-saveBtn").ClickAsync();
        await page.EvaluateAsync("() => window.__connections.api.whenPaused('set')");
        await Expect(page.Locator("#conn-saveBtn")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => document.getElementById('conn-saveBtn').click()");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__connections.api.calls.filter(call => call.kind === 'set').length"));
        await page.EvaluateAsync("() => window.__connections.api.release()");
        await Expect(page.Locator("#conn-status")).ToContainTextAsync("new_connection' saved.");
        await page.Locator("#conn-importBtn").ClickAsync();
        await page.Locator("#conn-importJson").FillAsync("[{\"alias\":\"imported\",\"connectorType\":\"MOCKDB\",\"target\":null,\"options\":{},\"disabled\":true,\"environmentScope\":null}]");
        await page.EvaluateAsync("() => window.__connections.api.setMode('invalid-import')");
        await page.Locator("#conn-importRunBtn").ClickAsync();
        await Expect(page.Locator("#conn-error")).ToContainTextAsync("outcome is unconfirmed");
        await Expect(page.Locator("#conn-importCard")).ToBeVisibleAsync();
        await Expect(page.Locator("#conn-saveBtn")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => window.__connections.api.setMode('healthy')");
        await page.Locator("#conn-refreshBtn").ClickAsync();
        await Expect(page.Locator("#conn-saveBtn")).ToBeEnabledAsync();
        await Expect(page.Locator("tr[data-alias='imported']")).ToContainTextAsync("Disabled");
        await page.Locator("tr[data-alias='sales_dw'] button[data-act='verify']").ClickAsync();
        await Expect(page.Locator("tr[data-alias='sales_dw'] .conn-row-status")).ToContainTextAsync("Verified");
        await page.EvaluateAsync("() => window.__connections.api.setMode('invalid-verify')");
        await page.Locator("tr[data-alias='sales_dw'] button[data-act='verify']").ClickAsync();
        await Expect(page.Locator("tr[data-alias='sales_dw'] .conn-row-status")).ToHaveTextAsync("Verification unavailable");
        await Expect(page.Locator("#conn-saveBtn")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConnectionsAdmin_MaskedValuesAndMalformedOptionsDoNotRoundTripOrLeak()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='connections-admin']").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync("4 shared connection(s).");
        await page.Locator("tr[data-alias='legacy_ftp'] button[data-act='detail']").ClickAsync();
        await Expect(page.Locator("#conn-target")).ToHaveValueAsync("Host=ftp01;Password=********");
        await page.Locator("#conn-saveBtn").ClickAsync();
        await Expect(page.Locator("#conn-error")).ToContainTextAsync("Replace masked display values");
        await page.Locator("#conn-target").FillAsync("");
        await page.Locator("#conn-options").FillAsync("private-fixture-token-without-separator");
        await page.Locator("#conn-saveBtn").ClickAsync();
        await Expect(page.Locator("#conn-error")).ToHaveTextAsync("Option line 1 must use KEY=VALUE.");
        Assert.DoesNotContain("private-fixture-token", await page.Locator("#conn-error").InnerTextAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__connections.api.calls.filter(call => call.kind === 'set').length"));
        await page.Locator("#conn-type").SelectOptionAsync("MSSQL");
        await Expect(page.Locator("#conn-help-content")).ToContainTextAsync("Microsoft SQL Server");
        await page.Locator("#conn-help-expand-btn").FocusAsync(); await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#conn-help-modal-close")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#conn-help-modal")).ToBeHiddenAsync();
        await Expect(page.Locator("#conn-help-expand-btn")).ToBeFocusedAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConnectionsAdmin_WizardPreflightAndDisposedRequestsCannotOpenLateUi()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='connections-admin']").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync("4 shared connection(s).");
        await page.EvaluateAsync("() => window.__connections.wizardApi.setInvalid(true)");
        await page.Locator("#conn-wizardBtn").ClickAsync();
        await Expect(page.Locator("#conn-error")).ToContainTextAsync("binding is unavailable");
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Connection Wizard" })).ToHaveCountAsync(0);
        await page.EvaluateAsync("() => window.__connections.wizardApi.setInvalid(false)");
        await page.Locator("#conn-wizardBtn").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Connection Wizard" })).ToBeVisibleAsync();
        await page.Locator("#etlsql-cw-close").ClickAsync();
        await page.EvaluateAsync("() => { window.__connections.api.pause('set'); }");
        await page.Locator("#conn-alias").FillAsync("disposed_definition");
        await page.Locator("#conn-saveBtn").ClickAsync();
        await page.EvaluateAsync("() => window.__connections.api.whenPaused('set')");
        var message = await page.Locator("#conn-status").InnerTextAsync();
        await page.EvaluateAsync("() => { window.__connections.surface.dispose(); window.__connections.api.release(); }");
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync(message);
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Connection Wizard" })).ToHaveCountAsync(0);
        await Expect(page.Locator("#conn-options")).ToHaveValueAsync("");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TriageBoard_IncompleteEvidenceIsUnavailableAndQuietBoardsKeepRunningJobs()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='triage-board']").ClickAsync();
        await page.SelectOptionAsync("#fixtureSel", "quiet");
        await Expect(page.Locator("#stage .triage-empty")).ToContainTextAsync("Nothing to triage");
        await Expect(page.Locator("#stage")).ToContainTextAsync("hourly_stock");
        await Expect(page.Locator("#stage")).ToContainTextAsync("In flight (1)");
        await page.SelectOptionAsync("#fixtureSel", "invalid");
        await Expect(page.Locator("#triage-host")).ToContainTextAsync("Triage is unavailable");
        await Expect(page.Locator("#triage-host")).Not.ToContainTextAsync("No failures");
        await page.SelectOptionAsync("#fixtureSel", "invalid-detail");
        await Expect(page.Locator("#stage .triage-evidence-row")).ToContainTextAsync("Evidence unavailable");
        await Expect(page.Locator("#stage .triage-evidence-row")).Not.ToContainTextAsync("No normalized quality-rule failures");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "triage-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task PolicyAuthority_ChangedScopeInvalidatesActionsAndLateValidationCannotLabelNewJson()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='policy-authority-admin']").ClickAsync();
        await Expect(page.Locator("#pa-versionWrap tbody tr")).ToHaveCountAsync(4);
        await page.Locator("#pa-tenant").FillAsync("other-tenant");
        await Expect(page.Locator("[data-pa-act='activate']")).ToHaveCountAsync(0);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__policyAuthority.api.calls.filter(call => call.kind === 'activate').length"));
        await page.Locator("#pa-loadVersionsBtn").ClickAsync();
        await Expect(page.Locator("#pa-versionWrap")).ToContainTextAsync("No policy versions");
        await page.EvaluateAsync("window.__policyAuthority.api.pause('validate')");
        await page.Locator("#pa-validateBtn").ClickAsync();
        await page.EvaluateAsync("window.__policyAuthority.api.whenPaused('validate')");
        await page.Locator("#pa-policyJson").FillAsync("{ broken");
        await page.EvaluateAsync("window.__policyAuthority.api.release()");
        await Expect(page.Locator("#pa-validateResult")).ToHaveTextAsync("");
        await page.Locator("#pa-validateBtn").ClickAsync();
        await Expect(page.Locator("#pa-validateResult")).ToContainTextAsync("Invalid:");
        await page.EvaluateAsync("window.__policyAuthority.api.setMode('invalid-validation')");
        await page.Locator("#pa-validateBtn").ClickAsync();
        await Expect(page.Locator("#pa-validateResult")).ToHaveTextAsync("Policy validation unavailable.");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "policy-authority-validation.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task PolicyAuthority_MissingAndMalformedMetadataAreUnavailableAndLateReadsStayInScope()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='policy-authority-admin']").ClickAsync();
        await page.SelectOptionAsync("#fixtureSel", "unavailable");
        await Expect(page.Locator("#pa-status")).ToHaveTextAsync("Signer status unavailable");
        await Expect(page.Locator("#pa-versionWrap")).ToContainTextAsync("unavailable or incomplete");
        await Expect(page.Locator("#pa-machineWrap")).ToContainTextAsync("unavailable or incomplete");
        await Expect(page.Locator("#pa-machineWrap")).Not.ToContainTextAsync("No machines registered");
        await page.SelectOptionAsync("#fixtureSel", "invalid");
        await Expect(page.Locator("#pa-status")).ToHaveTextAsync("Signer status unavailable");
        await Expect(page.Locator("#pa-publishBtn")).ToBeDisabledAsync();
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "policy-authority-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage .card").First.ScreenshotAsync(new() { Path = imagePath });
        await page.SelectOptionAsync("#fixtureSel", "configured");
        await Expect(page.Locator("#pa-versionWrap tbody tr")).ToHaveCountAsync(4);
        await page.EvaluateAsync("() => { const mock = window.__policyAuthority; mock.api.pause('versions'); window.__oldPolicyRead = mock.surface.load(); }");
        await page.EvaluateAsync("window.__policyAuthority.api.whenPaused('versions')");
        await page.Locator("#pa-env").FillAsync("staging");
        await page.Locator("#pa-loadVersionsBtn").ClickAsync();
        await Expect(page.Locator("#pa-versionWrap")).ToContainTextAsync("No policy versions");
        await page.EvaluateAsync("async () => { window.__policyAuthority.api.release(); await window.__oldPolicyRead; }");
        await Expect(page.Locator("#pa-versionWrap tbody tr")).ToHaveCountAsync(0);
        var unchanged = await page.EvaluateAsync<bool>("""
            async () => {
              const mock = window.__policyAuthority;
              mock.api.pause('versions'); const pending = mock.surface.load();
              await mock.api.whenPaused('versions'); mock.surface.dispose();
              const before = document.querySelector('#stage').innerHTML;
              mock.api.release(); await pending;
              return before === document.querySelector('#stage').innerHTML;
            }
            """);
        Assert.True(unchanged);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task PolicyAuthority_DuplicateAndUnconfirmedPublishesRequireExplicitRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='policy-authority-admin']").ClickAsync();
        await Expect(page.Locator("#pa-versionWrap tbody tr")).ToHaveCountAsync(4);
        await page.Locator("#pa-version").FillAsync("fixture-new-policy");
        await page.EvaluateAsync("() => { window.__policyAuthority.api.setMode('invalid-receipt'); window.__policyAuthority.api.pause('publish'); }");
        await page.Locator("#pa-publishBtn").ClickAsync();
        await page.EvaluateAsync("window.__policyAuthority.api.whenPaused('publish')");
        await page.EvaluateAsync("document.querySelector('#pa-publishBtn').dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__policyAuthority.api.calls.filter(call => call.kind === 'publish').length"));
        await page.EvaluateAsync("window.__policyAuthority.api.release()");
        await Expect(page.Locator("#pa-error")).ToContainTextAsync("unconfirmed");
        await Expect(page.Locator("#pa-publishBtn")).ToBeDisabledAsync();
        await Expect(page.Locator("#pa-actionResult")).Not.ToContainTextAsync("published");
        await page.EvaluateAsync("window.__policyAuthority.api.setMode('healthy')");
        await page.Locator("#pa-refreshBtn").ClickAsync();
        await Expect(page.Locator("#pa-actionResult")).ToContainTextAsync("Scope refreshed");
        await Expect(page.Locator("#pa-publishBtn")).ToBeEnabledAsync();
        await Expect(page.Locator("#pa-versionWrap tbody tr[data-version='fixture-new-policy']")).ToHaveCountAsync(1);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task PolicyAuthority_ConfirmationAndSentMutationCannotCrossChangedScope()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='policy-authority-admin']").ClickAsync();
        await Expect(page.Locator("#pa-versionWrap tbody tr")).ToHaveCountAsync(4);
        await page.Locator("[data-pa-act='promote']").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Promote policy canary" })).ToBeVisibleAsync();
        await page.EvaluateAsync("() => { const input = document.querySelector('#pa-tenant'); input.value = 'other'; input.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.GetByRole(AriaRole.Button, new() { Name = "Promote canary", Exact = true }).ClickAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__policyAuthority.api.calls.filter(call => call.kind === 'promoteCanary').length"));
        await page.Locator("#pa-tenant").FillAsync("acme");
        await page.Locator("#pa-refreshBtn").ClickAsync();
        await Expect(page.Locator("#pa-versionWrap tbody tr")).ToHaveCountAsync(4);
        await page.Locator("#pa-version").FillAsync("old-scope-policy");
        await page.EvaluateAsync("window.__policyAuthority.api.pause('publish')");
        await page.Locator("#pa-publishBtn").ClickAsync();
        await page.EvaluateAsync("window.__policyAuthority.api.whenPaused('publish')");
        await page.Locator("#pa-env").FillAsync("staging");
        await page.EvaluateAsync("window.__policyAuthority.api.release()");
        await Expect(page.Locator("#pa-actionResult")).Not.ToContainTextAsync("old-scope-policy");
        await page.Locator("#pa-env").FillAsync("prod");
        await Expect(page.Locator("#pa-publishBtn")).ToBeDisabledAsync();
        await page.Locator("#pa-refreshBtn").ClickAsync();
        await Expect(page.Locator("#pa-actionResult")).ToContainTextAsync("Scope refreshed");
        await Expect(page.Locator("#pa-versionWrap tbody tr[data-version='old-scope-policy']")).ToHaveCountAsync(1);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task PolicyAuthority_CanaryAndMachineInputsAreValidatedAndDisposedMutationIsSilent()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='policy-authority-admin']").ClickAsync();
        await Expect(page.Locator("#pa-versionWrap tbody tr")).ToHaveCountAsync(4);
        await page.Locator("#pa-canaryVersion").FillAsync("fractional-cohort");
        await page.Locator("#pa-canaryPercentage").FillAsync("1.5");
        await page.Locator("#pa-publishCanaryBtn").ClickAsync();
        await Expect(page.Locator("#pa-canaryError")).ToContainTextAsync("whole percentage");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__policyAuthority.api.calls.filter(call => call.kind === 'publishCanary').length"));
        await page.Locator("#pa-machineId").FillAsync("bad-guid");
        await page.Locator("#pa-registerMachineBtn").ClickAsync();
        await Expect(page.Locator("#pa-machineError")).ToContainTextAsync("32-character GUIDs");
        await page.Locator("#pa-version").FillAsync("disposed-policy");
        await page.EvaluateAsync("window.__policyAuthority.api.pause('publish')");
        await page.Locator("#pa-publishBtn").ClickAsync();
        await page.EvaluateAsync("window.__policyAuthority.api.whenPaused('publish')");
        await page.EvaluateAsync("() => { window.__policyAuthority.surface.dispose(); window.__policyAuthority.api.release(); }");
        await Expect(page.Locator("#pa-actionResult")).ToHaveTextAsync("");
        await Expect(page.Locator("#pa-publishBtn")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task GatewayAdmin_MissingFleetIsUnavailableAndDismissalClearsCredentials()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='gateways-admin']").ClickAsync();
        await page.SelectOptionAsync("#fixtureSel", "unavailable");
        await Expect(page.Locator("#gw-tableWrap")).ToContainTextAsync("unavailable or incomplete");
        await Expect(page.Locator("#gw-tableWrap")).Not.ToContainTextAsync("No Data Gateways Enrolled");
        await Expect(page.Locator("#gw-status")).ToHaveTextAsync("Gateway fleet unavailable");
        await page.SelectOptionAsync("#fixtureSel", "invalid");
        await Expect(page.Locator("#gw-status")).ToHaveTextAsync("Gateway fleet unavailable");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "gateway-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        await page.SelectOptionAsync("#fixtureSel", "healthy-fleet");
        await page.Locator("#gw-input-id").FillAsync("test-gateway");
        await page.Locator("#gw-enrollSubmitBtn").ClickAsync();
        await Expect(page.Locator("#gw-result-token")).ToHaveValueAsync(new string('a', 64));
        await page.Locator("#gw-enrollDoneBtn").ClickAsync();
        await Expect(page.Locator("#gw-result-token")).ToHaveValueAsync("");
        await Expect(page.Locator("#gw-result-cmd")).ToHaveValueAsync("");
        await page.SelectOptionAsync("#fixtureSel", "invalid-token");
        await page.Locator("#gw-input-id").FillAsync("unconfirmed-gateway");
        await page.Locator("#gw-enrollSubmitBtn").ClickAsync();
        await Expect(page.Locator("#gw-enroll-error")).ToContainTextAsync("unconfirmed");
        await Expect(page.Locator("#gw-enrollSubmitBtn")).ToBeDisabledAsync();
        await Expect(page.Locator("#gw-result-token")).ToHaveValueAsync("");
        await page.EvaluateAsync("async () => { window.__gateways.api.setMode('healthy'); await window.__gateways.surface.load(); }");
        await Expect(page.Locator("#gw-enrollSubmitBtn")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task GatewayAdmin_SupersededAndDisposedReadsCannotRestoreFleet()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='gateways-admin']").ClickAsync();
        await Expect(page.Locator("#gw-status")).ToContainTextAsync("4 Gateways");
        await page.EvaluateAsync("""
            async () => {
              const { api, surface } = window.__gateways;
              api.pauseNext(); const old = surface.load();
              api.setMode('unavailable'); await surface.load();
              api.release(); await old;
            }
            """);
        await Expect(page.Locator("#gw-status")).ToHaveTextAsync("Gateway fleet unavailable");
        var unchanged = await page.EvaluateAsync<bool>("""
            async () => {
              const { api, surface } = window.__gateways;
              api.setMode('healthy'); api.pauseNext(); const old = surface.load();
              surface.dispose(); const before = document.querySelector('#stage').innerHTML;
              api.release(); await old;
              return before === document.querySelector('#stage').innerHTML;
            }
            """);
        Assert.True(unchanged);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task GatewayAdmin_EnrollmentValidatesExpiryPreventsDuplicatesAndIgnoresDisposedReceipt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='gateways-admin']").ClickAsync();
        await page.Locator("#gw-input-id").FillAsync("branch'; Write-Host ignored; '");
        await page.Locator("#gw-input-expiry").FillAsync("5.5");
        await page.Locator("#gw-enrollSubmitBtn").ClickAsync();
        await Expect(page.Locator("#gw-enroll-error")).ToContainTextAsync("whole number");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__gateways.api.calls.filter(call => call.kind === 'enroll').length"));
        await page.Locator("#gw-input-expiry").FillAsync("60");
        await page.EvaluateAsync("window.__gateways.api.pauseNext()");
        await page.Locator("#gw-enrollSubmitBtn").ClickAsync();
        await page.EvaluateAsync("document.querySelector('#gw-enrollSubmitBtn').dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__gateways.api.calls.filter(call => call.kind === 'enroll').length"));
        await Expect(page.Locator("#gw-enrollBtn")).ToBeDisabledAsync();
        await page.EvaluateAsync("window.__gateways.api.release()");
        await Expect(page.Locator("#gw-enrollResult")).ToBeVisibleAsync();
        Assert.Contains("--gateway-id 'branch''; Write-Host ignored; '''", await page.Locator("#gw-result-cmd").InputValueAsync());
        await page.Locator("#gw-enrollDoneBtn").ClickAsync();
        await Expect(page.Locator("#stage")).ToHaveAttributeAsync("aria-busy", "false");
        await page.Locator("#gw-input-id").FillAsync("late-gateway");
        await page.EvaluateAsync("window.__gateways.api.pauseNext()");
        await page.Locator("#gw-enrollSubmitBtn").ClickAsync();
        await page.EvaluateAsync("() => { window.__gateways.surface.dispose(); window.__gateways.api.release(); }");
        await Expect(page.Locator("#gw-result-token")).ToHaveValueAsync("");
        await Expect(page.Locator("#gw-result-cmd")).ToHaveValueAsync("");
        await Expect(page.Locator("#gw-enrollResult")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task GatewayAdmin_UnconfirmedRevocationRequiresRefreshAndStaleConfirmationCannotWrite()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='gateways-admin']").ClickAsync();
        await page.SelectOptionAsync("#fixtureSel", "invalid-revoke");
        var row = page.Locator("#gw-tableWrap tr[data-gw-id='corp-onprem-gw']");
        await row.GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Revoke gateway", Exact = true }).ClickAsync();
        await Expect(page.Locator("#gw-action-status")).ToContainTextAsync("unconfirmed");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true })).ToBeDisabledAsync();
        await Expect(page.Locator("#gw-action-status")).Not.ToContainTextAsync("revoked.");
        await page.Locator("#gw-refreshBtn").ClickAsync();
        await Expect(row).ToContainTextAsync("Revoked");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true })).ToHaveCountAsync(0);
        await page.SelectOptionAsync("#fixtureSel", "healthy-fleet");
        await row.GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Revoke Gateway", Exact = true })).ToBeVisibleAsync();
        await page.EvaluateAsync("window.__gateways.surface.load()");
        await page.GetByRole(AriaRole.Button, new() { Name = "Revoke gateway", Exact = true }).ClickAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__gateways.api.calls.filter(call => call.kind === 'revoke').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task GatewayAdmin_ClusterHistoryDoesNotInflateCountsAndNodesSupportKeyboardDismissal()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='gateways-admin']").ClickAsync();
        await Expect(page.Locator("#gw-status")).ToContainTextAsync("4 Gateways");
        await page.EvaluateAsync("""
            async () => {
              const { api, surface } = window.__gateways;
              const fleet = api.snapshot();
              fleet[0].nodes[1].isActive = false; fleet[0].activeNodes = 1;
              const history = { ...structuredClone(fleet[0]), enrollmentId: 'pending-history', state: 'Pending', createdUtc: '2026-10-07T12:00:00Z' };
              api.setFleet([history, ...fleet]); await surface.load();
            }
            """);
        await Expect(page.Locator("#gw-status")).ToHaveTextAsync("4 Gateways • 2 Active Nodes");
        var opener = page.Locator("#gw-tableWrap tr[data-gw-id='corp-onprem-gw'] .gw-view-nodes-btn");
        await opener.FocusAsync(); await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#gw-nodesModal")).ToBeVisibleAsync();
        await Expect(page.Locator("#gw-nodesTableWrap")).ToContainTextAsync("PROD-GW-01");
        await Expect(page.Locator("#gw-nodesTableWrap")).ToContainTextAsync("Disconnected");
        await Expect(page.Locator("#gw-nodesCloseBtn")).ToBeFocusedAsync();
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "gateway-nodes.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#gw-nodesModal").ScreenshotAsync(new() { Path = imagePath });
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#gw-nodesModal")).ToBeHiddenAsync();
        await Expect(opener).ToBeFocusedAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task GatewayAdmin_BindingRequiresValidMetadataAndCannotOpenAfterDisposal()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='gateways-admin']").ClickAsync();
        await Expect(page.Locator("#gw-status")).ToContainTextAsync("4 Gateways");
        var bind = page.Locator("#gw-tableWrap tr[data-gw-id='corp-onprem-gw'] .gw-create-conn-btn");
        await page.EvaluateAsync("window.__gateways.wizardApi.setInvalid(true)");
        await bind.ClickAsync();
        await Expect(page.Locator("#gw-action-status")).ToContainTextAsync("binding is unavailable");
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Connection Wizard" })).ToHaveCountAsync(0);
        await page.EvaluateAsync("window.__gateways.wizardApi.setInvalid(false)");
        await bind.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Connection Wizard" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Connection Wizard" })).ToContainTextAsync("sales_read");
        await page.Locator("#etlsql-cw-close").ClickAsync();
        await page.EvaluateAsync("window.__gateways.wizardApi.pauseNext()");
        await bind.ClickAsync();
        await page.EvaluateAsync("() => { window.__gateways.surface.dispose(); window.__gateways.wizardApi.release(); }");
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Connection Wizard" })).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TriageBoard_SelectionAndKeyboardFocusFollowIncidentIdentityDuringRefresh()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='triage-board']").ClickAsync();
        var checks = page.Locator("#triage-host .triage-incident-check");
        await Expect(checks).ToHaveCountAsync(3);
        await checks.Nth(0).FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Expect(checks.Nth(0)).ToBeCheckedAsync();
        await Expect(checks.Nth(0)).ToBeFocusedAsync();
        await page.EvaluateAsync("async () => { window.__triage.reverse(); await window.__triage.controller.reload(); }");
        await Expect(checks.Nth(2)).ToBeCheckedAsync();
        await Expect(checks.Nth(2)).ToBeFocusedAsync();
        await Expect(checks.Nth(0)).Not.ToBeCheckedAsync();
        await checks.Nth(0).CheckAsync(); // Same job in another incident must not duplicate a rerun.
        await page.Locator("#triage-host .triage-rerun-selected").ClickAsync();
        await Expect(page.Locator("#triage-log")).ToContainTextAsync("5 of 5 jobs triggered");
        var submitted = await page.EvaluateAsync<string[]>("window.__triage.calls.find(call => call.kind === 'rerun').names");
        Assert.Equal(5, submitted.Length);
        Assert.Equal(1, submitted.Count(name => name == "nightly_sales"));
        Assert.Contains("finance_load", submitted);
        Assert.DoesNotContain("finance_merge", submitted);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TriageBoard_SupersededReadsAndDisposedControllersCannotRestoreStaleData()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='triage-board']").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-incident")).ToHaveCountAsync(3);
        await page.EvaluateAsync("""
            async () => {
              const mock = window.__triage;
              mock.pause('board'); const old = mock.controller.reload();
              mock.setBoard(null); await mock.controller.reload();
              mock.release('board'); await old;
            }
            """);
        await Expect(page.Locator("#triage-host")).ToContainTextAsync("Triage is unavailable");
        await Expect(page.Locator("#triage-host .triage-incident")).ToHaveCountAsync(0);
        await page.EvaluateAsync("window.__triage.restore()");
        await page.Locator("#triage-host .triage-retry").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-incident")).ToHaveCountAsync(3);
        await page.EvaluateAsync("""
            async () => {
              const mock = window.__triage;
              mock.pause('board'); const old = mock.controller.reload();
              mock.controller.dispose(); mock.release('board'); await old;
            }
            """);
        await Expect(page.Locator("#triage-host")).ToBeEmptyAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TriageBoard_PartialAndUnconfirmedRerunsRetainTheFailedSelectionAndPreventDuplicates()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='triage-board']").ClickAsync();
        var checks = page.Locator("#triage-host .triage-incident-check");
        await Expect(checks).ToHaveCountAsync(3);
        await checks.Nth(1).CheckAsync(); await checks.Nth(2).CheckAsync();
        await page.EvaluateAsync("() => { window.__triage.setRerunMode('partial'); window.__triage.pause('rerun'); }");
        await page.Locator("#triage-host .triage-rerun-selected").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-rerun-selected")).ToBeDisabledAsync();
        await page.Locator("#triage-host .triage-rerun-selected").DispatchEventAsync("click");
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__triage.calls.filter(call => call.kind === 'rerun').length"));
        await page.EvaluateAsync("window.__triage.release('rerun')");
        await Expect(page.Locator("#triage-log")).ToContainTextAsync("1 of 2 jobs triggered. Not triggered: nightly_sales");
        await Expect(checks.Nth(1)).Not.ToBeCheckedAsync();
        await Expect(checks.Nth(2)).ToBeCheckedAsync();
        await checks.Nth(1).CheckAsync();
        await page.EvaluateAsync("window.__triage.setRerunMode('invalid')");
        await page.Locator("#triage-host .triage-rerun-selected").ClickAsync();
        await Expect(page.Locator("#triage-log")).ToContainTextAsync("outcome is unconfirmed");
        await Expect(page.Locator("#triage-host .triage-rerun-selected")).ToBeDisabledAsync();
        await page.EvaluateAsync("window.__triage.controller.reload()"); // A poll cannot authorize another mutation.
        await Expect(page.Locator("#triage-host .triage-rerun-selected")).ToBeDisabledAsync();
        await page.Locator("#triage-refresh").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-rerun-selected")).ToBeEnabledAsync();
        Assert.Equal(2, await page.EvaluateAsync<int>("window.__triage.calls.filter(call => call.kind === 'rerun').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TriageBoard_EvidenceMatchesTheRequestedRunAndLateResponsesCannotReopenIt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='triage-board']").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-incident-toggle")).ToHaveCountAsync(3);
        await page.Locator("#triage-host .triage-incident-toggle").Nth(2).ClickAsync();
        await page.Locator("#triage-host [data-run='14355']").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-evidence-row")).ToContainTextAsync("Quality failures (2)");
        await page.Locator("#triage-host [data-run='14355']").ClickAsync();
        await page.EvaluateAsync("""
            async () => {
              const { _fixtures } = await import('/tools/ui-sandbox/stories/triage-board.story.js');
              const wrong = structuredClone(_fixtures.runDetails.get(14355)); wrong.run.id++;
              window.__triage.setDetail(14355, wrong);
            }
            """);
        await page.Locator("#triage-host [data-run='14355']").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-evidence-row")).ToContainTextAsync("Evidence unavailable");
        await page.Locator("#triage-host [data-run='14355']").ClickAsync();
        await page.EvaluateAsync("window.__triage.pause('detail')");
        await page.Locator("#triage-host [data-run='14355']").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-evidence-row")).ToContainTextAsync("Loading durable run evidence");
        await page.Locator("#triage-host [data-run='14355']").ClickAsync();
        await page.EvaluateAsync("window.__triage.release('detail')");
        await Expect(page.Locator("#triage-host .triage-evidence-row")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TriageBoard_WindowChangesInvalidateReadsAndDisposalSuppressesPendingMutationOutcomes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='triage-board']").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-incident")).ToHaveCountAsync(3);
        Assert.True(await page.EvaluateAsync<bool>("""
            async () => {
              const { createTriageController } = await import('/src/ETL-SQL.Portal/wwwroot/js/triage-controller.js');
              return createTriageController(window.__triage.options) === window.__triage.controller;
            }
            """));
        await page.Locator("#triage-host .triage-incident-check").Nth(0).CheckAsync();
        await page.EvaluateAsync("() => { window.__triage.pause('board'); window.__oldTriageRead = window.__triage.controller.reload(); }");
        await page.SelectOptionAsync("#triage-window", "48");
        await Expect(page.Locator("#triage-host .triage-meta")).ToContainTextAsync("Last 48h");
        await page.EvaluateAsync("async () => { window.__triage.release('board'); await window.__oldTriageRead; }");
        await Expect(page.Locator("#triage-host .triage-meta")).ToContainTextAsync("Last 48h");
        await Expect(page.Locator("#triage-host .triage-incident-check").Nth(0)).Not.ToBeCheckedAsync();
        await page.Locator("#triage-host .triage-incident-check").Nth(1).CheckAsync();
        await page.Locator("#triage-host .triage-incident-check").Nth(2).CheckAsync();
        await page.EvaluateAsync("window.__triage.pause('rerun')");
        await page.Locator("#triage-host .triage-rerun-selected").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-rerun-selected")).ToBeDisabledAsync();
        await page.SelectOptionAsync("#triage-window", "24");
        await Expect(page.Locator("#triage-host")).ToContainTextAsync("Re-run outcome is unconfirmed");
        await page.EvaluateAsync("window.__triage.release('rerun')");
        await Expect(page.Locator("#triage-log")).Not.ToContainTextAsync("jobs triggered");
        await page.Locator("#triage-host .triage-incident-check").Nth(1).CheckAsync();
        await page.Locator("#triage-host .triage-incident-check").Nth(2).CheckAsync();
        await Expect(page.Locator("#triage-host .triage-rerun-selected")).ToBeDisabledAsync();
        await page.Locator("#triage-refresh").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-rerun-selected")).ToBeEnabledAsync();
        await page.EvaluateAsync("window.__triage.pause('rerun')");
        await page.Locator("#triage-host .triage-rerun-selected").ClickAsync();
        await Expect(page.Locator("#triage-host .triage-rerun-selected")).ToBeDisabledAsync();
        await page.EvaluateAsync("async () => { window.__triage.controller.dispose(); await window.__triage.release('rerun'); }");
        await Expect(page.Locator("#triage-host")).ToBeEmptyAsync();
        await Expect(page.Locator("#triage-log")).Not.ToContainTextAsync("jobs triggered");
        Assert.Equal(2, await page.EvaluateAsync<int>("window.__triage.calls.filter(call => call.kind === 'rerun').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ControlPlaneDashboard_UnavailableSources_ShowErrorsAndRecover()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='control-plane-dashboard']").ClickAsync();
        await page.SelectOptionAsync("#fixtureSel", "unavailable");
        var frame = page.FrameLocator("#stage iframe");
        await Expect(frame.Locator("#tenantTableBody")).ToContainTextAsync("Tenant inventory unavailable.");
        await Expect(frame.Locator("#kpiTenants")).ToHaveTextAsync("3 / 3");
        await Expect(frame.Locator("#btnOpenProvision")).ToBeDisabledAsync();
        await frame.Locator("[data-tab='audit']").ClickAsync();
        await Expect(frame.Locator("#auditTableBody")).ToContainTextAsync("Platform audit trail unavailable.");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "control-plane-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        await page.EvaluateAsync("document.querySelector('#stage iframe').contentWindow.__cpMock.setFailure('healthy')");
        await frame.Locator("#btnRefreshControlPlane").ClickAsync();
        await frame.Locator("[data-tab='tenants']").ClickAsync();
        await Expect(frame.Locator("#tenantTableBody tr")).ToHaveCountAsync(3);
        await frame.Locator("#tenantSearch").FillAsync("globex");
        await frame.Locator("#btnRefreshControlPlane").ClickAsync();
        await Expect(frame.Locator("#tenantTableBody tr")).ToHaveCountAsync(1);
        await Expect(frame.Locator("#tenantTableBody")).ToContainTextAsync("globex-inc");
        await Expect(frame.Locator("#btnOpenProvision")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    private async Task<IFrameLocator> MountControlPlaneAsync(IPage page, string fixtureId = "healthy")
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='control-plane-dashboard']").ClickAsync();
        if (fixtureId != "healthy") await page.SelectOptionAsync("#fixtureSel", fixtureId);
        var frame = page.FrameLocator($"#stage iframe[title='Control Plane — {fixtureId}']");
        await Expect(frame.Locator("#controlPlaneStatus")).Not.ToContainTextAsync("Loading");
        return frame;
    }

    [Theory]
    [InlineData("invalid", "Invalid control-plane overview response.")]
    [InlineData("unauthorized", "Platform management credentials required.")]
    [InlineData("disabled", "unavailable in this deployment")]
    public async Task ControlPlaneDashboard_InvalidOrDeniedSources_DisableMutations(string fixtureId, string error)
    {
        await using var session = await fixture.NewSessionAsync();
        var frame = await MountControlPlaneAsync(session.Page, fixtureId);
        await Expect(frame.Locator("#controlPlaneStatus")).ToContainTextAsync(error);
        await Expect(frame.Locator("#kpiAuditOutbox")).ToHaveTextAsync("Unavailable");
        await Expect(frame.Locator("#tenantTableBody")).ToContainTextAsync("unavailable");
        await Expect(frame.Locator("#btnOpenProvision")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ControlPlaneDashboard_Quotas_KeepReportLimitAndBindOnce()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await MountControlPlaneAsync(page);
        await page.EvaluateAsync("""
            () => {
              const w = document.querySelector('#stage iframe').contentWindow;
              if (w.ControlPlaneUI.init(w.document.body) !== w.ControlPlaneUI.init(w.document.body)) throw Error('Double binding');
              const button = w.document.querySelector('tr[data-tenant="acme-corp"] .btn-quota');
              const child = w.document.createElement('span'); child.textContent = 'Quotas'; button.replaceChildren(child);
            }
            """);
        var open = frame.Locator("tr[data-tenant='acme-corp'] .btn-quota");
        await open.Locator("span").ClickAsync();
        await Expect(frame.Locator("#quotaTenantId")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(frame.Locator("#modalQuotas")).Not.ToBeVisibleAsync();
        await Expect(open).ToBeFocusedAsync();
        await open.ClickAsync();
        await frame.GetByLabel("Concurrent Jobs", new() { Exact = true }).FillAsync("17");
        await frame.Locator("#quotaStorageMb").FillAsync("40960");
        await frame.Locator("#quotaOperator").FillAsync("operator@platform.test");
        await frame.Locator("#quotaAuthRef").FillAsync("CHG-fixture");
        await frame.Locator("#quotaReason").FillAsync("Increase headroom");
        await frame.GetByRole(AriaRole.Button, new() { Name = "Save Quotas" }).ClickAsync();
        await Expect(frame.Locator("#receiptAlert")).ToContainTextAsync("UpdateQuotas — Completed");
        await Expect(frame.Locator("tr[data-tenant='acme-corp']")).ToContainTextAsync("17 max");
        Assert.Equal(1, await page.EvaluateAsync<int>("document.querySelector('#stage iframe').contentWindow.__cpMock.calls.filter(c => c.method === 'POST').length"));
        var inventory = await page.EvaluateAsync<int>("""
            async () => {
              const w = document.querySelector('#stage iframe').contentWindow;
              const response = await w.__cpMock.fetch('/api/platform/control-plane/tenants');
              return (await response.json()).find(t => t.tenantId === 'acme-corp').maxReportSessions;
            }
            """);
        Assert.Equal(50, inventory);
        await frame.Locator("[data-tab='tenants']").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(frame.Locator("[data-tab='audit']")).ToBeFocusedAsync();
        await Expect(frame.Locator("#auditTableBody")).ToContainTextAsync("CHG-fixture");
        await frame.Locator("[data-tab='tenants']").ClickAsync();
        await open.ClickAsync();
        await frame.Locator("#quotaMaxJobs").FillAsync("19");
        await frame.Locator("#quotaOperator").FillAsync("operator@platform.test");
        await frame.Locator("#quotaAuthRef").FillAsync("CHG-unconfirmed");
        await frame.Locator("#quotaReason").FillAsync("Increase headroom");
        await page.EvaluateAsync("document.querySelector('#stage iframe').contentWindow.__cpMock.setFailure('mismatchedQuotas')");
        await frame.GetByRole(AriaRole.Button, new() { Name = "Save Quotas" }).ClickAsync();
        await Expect(frame.Locator("#formQuotas [role='alert']")).ToContainTextAsync("receipt quotas do not match");
        await Expect(frame.Locator("#receiptAlert")).Not.ToBeVisibleAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ControlPlaneDashboard_SupersededLoadsAndDisposal_IgnoreLateResponses()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await MountControlPlaneAsync(page);
        await page.EvaluateAsync("""
            async () => {
              const w = document.querySelector('#stage iframe').contentWindow;
              w.__cpMock.pauseNextRead(); w.__oldRead = w.ControlPlaneUI.reload();
              w.__cpMock.setFailure('invalid'); await w.ControlPlaneUI.reload();
              w.__cpMock.releaseReads(); await w.__oldRead;
            }
            """);
        await Expect(frame.Locator("#kpiTenants")).ToHaveTextAsync("Unavailable");
        await Expect(frame.Locator("#tenantTableBody")).ToContainTextAsync("Invalid control-plane tenant inventory");
        await page.EvaluateAsync("""
            async () => {
              const w = document.querySelector('#stage iframe').contentWindow;
              w.__cpMock.setFailure('healthy'); w.__cpMock.pauseNextRead();
              const pending = w.ControlPlaneUI.reload(); w.ControlPlaneUI.dispose();
              const text = w.document.body.textContent;
              w.__cpMock.releaseReads(); await pending;
              if (w.document.body.textContent !== text) throw Error('Disposed controller rendered a late response');
            }
            """);
        await Expect(frame.Locator("#btnOpenProvision")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ControlPlaneDashboard_CancelDuringMutation_PreventsDuplicateAndLateReceipt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await MountControlPlaneAsync(page);
        await frame.Locator("#btnOpenProvision").ClickAsync();
        await frame.Locator("#provTenantId").FillAsync("new-tenant");
        await frame.Locator("#provOperator").FillAsync("operator@platform.test");
        await frame.Locator("#provAuthRef").FillAsync("CHG-new");
        await frame.Locator("#provReason").FillAsync("Onboarding");
        await page.EvaluateAsync("""
            () => {
              const w = document.querySelector('#stage iframe').contentWindow;
              w.__cpMock.pauseNextMutation();
              w.document.querySelector('#formProvision').requestSubmit();
              w.document.querySelector('#formProvision').requestSubmit();
            }
            """);
        await Expect(frame.Locator("#formProvision button[type='submit']")).ToBeDisabledAsync();
        await frame.Locator("#modalProvision .btn-cancel").ClickAsync();
        await page.EvaluateAsync("document.querySelector('#stage iframe').contentWindow.__cpMock.releaseMutations()");
        await Expect(frame.Locator("#tenantTableBody tr")).ToHaveCountAsync(4);
        await Expect(frame.Locator("#modalProvision")).Not.ToBeVisibleAsync();
        await Expect(frame.Locator("#receiptAlert")).Not.ToBeVisibleAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("document.querySelector('#stage iframe').contentWindow.__cpMock.calls.filter(c => c.method === 'POST').length"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("invalidReceipt", "Invalid control-plane audit receipt")]
    [InlineData("mismatchedReceipt", "receipt does not match")]
    [InlineData("pendingReceipt", "Action reported Pending")]
    public async Task ControlPlaneDashboard_UnconfirmedMutationReceipt_DoesNotAnnounceSuccess(string failure, string error)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var frame = await MountControlPlaneAsync(page);
        await frame.Locator("tr[data-tenant='acme-corp'] .btn-state").ClickAsync();
        await frame.Locator("#stateSelect").SelectOptionAsync("Maintenance");
        await frame.Locator("#stateOperator").FillAsync("operator@platform.test");
        await frame.Locator("#stateAuthRef").FillAsync("CHG-state");
        await frame.Locator("#stateReason").FillAsync("Maintenance window");
        await page.EvaluateAsync("failure => document.querySelector('#stage iframe').contentWindow.__cpMock.setFailure(failure)", failure);
        await frame.GetByRole(AriaRole.Button, new() { Name = "Update State" }).ClickAsync();
        await Expect(frame.Locator("#formState [role='alert']")).ToContainTextAsync(error);
        await Expect(frame.Locator("#receiptAlert")).Not.ToBeVisibleAsync();
        await Expect(frame.GetByRole(AriaRole.Button, new() { Name = "Update State" })).ToBeDisabledAsync();
        await frame.Locator("#modalState .btn-cancel").ClickAsync();
        await page.EvaluateAsync("document.querySelector('#stage iframe').contentWindow.__cpMock.setFailure('healthy')");
        await frame.Locator("#btnRefreshControlPlane").ClickAsync();
        await Expect(frame.Locator("#btnOpenProvision")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task PortalAuthoring_SnapshotColumnsAndLoadingErrors()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-authoring']").ClickAsync();
        await Expect(page.Locator("#stage svg").First).ToBeVisibleAsync();
        await Expect(page.Locator("#stage")).ToContainTextAsync("North");
        await Expect(page.Locator("#stage")).ToContainTextAsync("South");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "portal-authoring-snapshot.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        await page.SelectOptionAsync("#fixtureSel", "unavailable");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("<img src=x onerror=document.body.dataset.injected=1>");
        await Expect(page.Locator("#stage img")).ToHaveCountAsync(0);
        var retry = page.GetByRole(AriaRole.Button, new() { Name = "Retry Connection" });
        await retry.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Retry requested.");
        await page.SelectOptionAsync("#fixtureSel", "invalid-snapshot");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Invalid designer snapshot response.");
        await Expect(page.Locator("#stage .etlsql-designer")).ToHaveCountAsync(0);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = Path.Combine(Path.GetDirectoryName(imagePath)!, "portal-authoring-error.png") });
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task PortalOperationalChart_SelectsNestedGraphMarks()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-operational-charts']").ClickAsync();
        await page.SelectOptionAsync("#fixtureSel", "graph");
        await Expect(page.GetByRole(AriaRole.Img, new() { Name = "Jobs: extract, validate, load" })).ToBeVisibleAsync();
        await page.Locator("#stage svg g[data-name='validate'] circle").ClickAsync();
        await Expect(page.GetByText("Selected: validate", new() { Exact = true })).ToBeVisibleAsync();
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "portal-operational-charts.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task SecretsAdmin_SaveAndVerify_KeepValuesOutOfRenderedMetadata()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='secrets-admin']").ClickAsync();
        await Expect(page.Locator("#stage .data-table tbody tr")).ToHaveCountAsync(3);
        await page.Locator("#sec-name").FillAsync("sandbox_password");
        await page.Locator("#sec-value").FillAsync("sandbox-write-only-value");
        await page.Locator("#sec-saveBtn").ClickAsync();
        await Expect(page.Locator("#stage .data-table tbody tr")).ToHaveCountAsync(4);
        await Expect(page.Locator("#sec-value")).ToHaveValueAsync("");
        Assert.DoesNotContain("sandbox-write-only-value", await page.Locator("#stage").InnerTextAsync());
        await page.Locator("tr[data-name='old_ftp_password'] button[data-act='verify']").ClickAsync();
        await Expect(page.Locator("tr[data-name='old_ftp_password'] .badge-error")).ToHaveTextAsync("disabled");
        await page.Locator("#sec-verifyAllBtn").ClickAsync();
        await Expect(page.Locator("#sec-status")).ToHaveTextAsync("All 4 secret(s) decryptable.");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "secrets-admin.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task SecretsAdmin_InvalidResponses_ShowErrorsWithoutInventingResults()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='secrets-admin']").ClickAsync();
        await page.SelectOptionAsync("#fixtureSel", "invalid-list");
        await Expect(page.Locator("#sec-tableWrap")).ToContainTextAsync("Invalid secret-store list response.");
        await Expect(page.Locator("#sec-tableWrap tbody tr")).ToHaveCountAsync(0);
        await page.SelectOptionAsync("#fixtureSel", "invalid-check");
        await Expect(page.Locator("#stage .data-table tbody tr")).ToHaveCountAsync(3);
        await page.Locator("#sec-verifyAllBtn").ClickAsync();
        await Expect(page.Locator("#sec-status")).ToHaveTextAsync("Verify-all failed: Invalid secret-store verification response.");
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task SecretsAdmin_RefreshAndDisposal_IgnoreSupersededRequests()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        var result = await page.EvaluateAsync<string[]>("""
            async () => {
              const { createSecretsAdmin } = await import('/src/ETL-SQL.Portal/wwwroot/js/secrets-admin.js');
              const host = document.createElement('div');
              document.body.append(host);
              const requests = [];
              const surface = createSecretsAdmin({ host, secretsApi: { list: () => new Promise(resolve => requests.push(resolve)) } });
              const secret = name => ({ name, disabled: false, createdAtUtc: '2026-10-07T00:00:00Z', updatedAtUtc: '2026-10-07T00:00:00Z', version: 1 });
              const first = surface.load(), second = surface.load();
              requests[1]([secret('latest')]);
              await second;
              requests[0]([secret('superseded')]);
              await first;
              const afterRefresh = host.querySelector('tbody').textContent;
              const pending = surface.load();
              surface.dispose();
              requests[2]([secret('after-disposal')]);
              await pending;
              const afterDisposal = host.querySelector('tbody').textContent;
              host.remove();
              return [afterRefresh, afterDisposal];
            }
            """);
        Assert.All(result, text => Assert.Contains("latest", text));
        Assert.All(result, text => Assert.DoesNotContain("superseded", text));
        Assert.All(result, text => Assert.DoesNotContain("after-disposal", text));
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task OrchestratorActivity_FiltersClearSearchAndRefreshUseTheNativeControls()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='orchestrator-activity']").ClickAsync();
        var rows = page.Locator("#jobsTableWrap tbody tr");
        await Expect(rows).ToHaveCountAsync(6);
        await page.Locator("#activeFilterBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows).ToContainTextAsync("active_load");
        await Expect(page.Locator("#activeFilterBtn")).ToHaveAttributeAsync("aria-pressed", "true");
        await page.Locator("#queuedFilterBtn").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows).ToContainTextAsync("queued_load");
        await Expect(page.Locator("#activeFilterBtn")).ToHaveAttributeAsync("aria-pressed", "false");
        await page.Locator("#completedTodayFilterBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(page.Locator("#jobsTableWrap")).ToContainTextAsync("retry_succeeded");
        await page.Locator("#failedTodayFilterBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(page.Locator("#jobsTableWrap")).ToContainTextAsync("failed_load");
        await page.Locator("#clearJobsFilterBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(6);
        await Expect(page.Locator("#jobsSectionKicker")).ToHaveTextAsync("All jobs");
        await page.Locator("#jobsSearchInput").FillAsync("retry");
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(page.Locator("#completedCount")).ToHaveTextAsync("2");
        await page.Locator("#jobsSearchClearBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(6);
        await Expect(page.Locator("#jobsSearchInput")).ToBeFocusedAsync();
        await page.SelectOptionAsync("#jobsStatusSelect", "disabled");
        await Expect(rows).ToHaveCountAsync(0);
        await page.SelectOptionAsync("#jobsStatusSelect", "all");
        await Expect(rows).ToHaveCountAsync(6);
        await page.Locator("#activeFilterBtn").ClickAsync();
        await page.Locator("#refreshJobsBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(0);
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("0");
        await Expect(page.Locator("#activeFilterBtn")).ToHaveAttributeAsync("aria-pressed", "true");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "orchestrator-activity.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#clearJobsFilterBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(5);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task OrchestratorActivity_PaginationAndErrorsDoNotLeaveStaleRowsOrCounts()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='orchestrator-activity']").ClickAsync();
        await page.SelectOptionAsync("#fixtureSel", "paged");
        var rows = page.Locator("#jobsTableWrap tbody tr");
        await Expect(rows).ToHaveCountAsync(25);
        await Expect(page.Locator("#jobsPaginationInfo")).ToHaveTextAsync("Showing 1–25 of 30 jobs");
        await page.Locator("#jobsNextPageBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(5);
        await Expect(page.Locator("#jobsPageIndicator")).ToHaveTextAsync("2 / 2");
        await page.Locator("#jobsPrevPageBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(25);
        await page.SelectOptionAsync("#jobsPageSizeSelect", "50");
        await Expect(rows).ToHaveCountAsync(30);
        await page.SelectOptionAsync("#fixtureSel", "denied");
        await Expect(rows).ToHaveCountAsync(0);
        await Expect(page.Locator("#jobsTableWrap [role='alert']")).ToContainTextAsync("Access denied.");
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("—");
        await page.Locator("#activeFilterBtn").ClickAsync();
        await Expect(rows).ToHaveCountAsync(0);
        await Expect(page.Locator("#jobsTableWrap [role='alert']")).ToContainTextAsync("Access denied.");
        await page.SelectOptionAsync("#fixtureSel", "invalid");
        await Expect(page.Locator("#jobsTableWrap [role='alert']")).ToContainTextAsync("Invalid scheduled-job activity response.");
        await Expect(page.Locator("#completedCount")).ToHaveTextAsync("—");
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task EveryStoryAndFixture_MountsWithoutThrowing()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.WaitForSelectorAsync(".story-link", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });

        var stories = await page.EvaluateAsync<StoryInfo[]>("""
            async () => {
              const { stories } = await import('/tools/ui-sandbox/stories/index.js');
              return stories.map(s => ({
                id: s.id,
                title: s.title,
                fixtures: (Array.isArray(s.fixtures)
                  ? s.fixtures
                  : (s.fixtures && typeof s.fixtures === 'object' ? Object.entries(s.fixtures).map(([k, v]) => typeof v === 'object' ? { id: v?.id ?? k } : { id: k }) : [{ id: '' }])
                ).map(f => (typeof f === 'object' ? (f?.id ?? '') : String(f))),
              }));
            }
            """);

        Assert.NotEmpty(stories);
        var failures = new List<string>();

        foreach (var story in stories)
        {
            for (var i = 0; i < story.Fixtures.Length; i++)
            {
                var before = session.PageErrors.Count;
                var consoleBefore = session.ConsoleErrors.Count;
                var failedReqsBefore = session.FailedRequests.Count;

                // Clicking through the shell rather than calling mount() directly: the shell's
                // dispose/re-mount path is part of what breaks, and a story that leaks between
                // fixtures only shows up when one is mounted after another.
                await DismissOpenOverlaysAsync(page);
                await page.ClickAsync($"button.story-link[data-story-id='{story.Id}']");
                await page.WaitForTimeoutAsync(150);

                if (story.Fixtures.Length > 1 && story.Fixtures[i].Length > 0)
                {
                    await page.SelectOptionAsync("#fixtureSel", story.Fixtures[i]);
                    await page.WaitForTimeoutAsync(250);
                }

                var label = $"{story.Id}/{(story.Fixtures[i].Length == 0 ? "(default)" : story.Fixtures[i])}";

                var thrown = session.PageErrors.Skip(before).ToList();
                if (thrown.Count > 0)
                    failures.Add($"{label} threw: {string.Join(" | ", thrown)}");

                var logged = session.ConsoleErrors.Skip(consoleBefore).ToList();
                if (logged.Count > 0)
                {
                    var failedReqs = session.FailedRequests.Skip(failedReqsBefore).ToList();
                    failures.Add($"{label} logged: {string.Join(" | ", logged)}" + (failedReqs.Count > 0 ? $" (Failed requests: {string.Join(", ", failedReqs)})" : ""));
                }

                if (await page.Locator("#stage *").CountAsync() == 0)
                    failures.Add($"{label} mounted nothing into the stage.");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} sandbox story fixtures failed to mount cleanly:\n  "
            + string.Join("\n  ", failures));
    }

    [Fact]
    public async Task Sidebar_CollapseAndExpand_TogglesPanelVisibilityAndResizesStage()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.WaitForSelectorAsync(".story-link", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });

        var sidebar = page.Locator("#sidebar");
        var stage = page.Locator("#stage");
        var toggleBtn = page.Locator("#sidebarToggleBtn");
        var collapseBtn = page.Locator("#sidebarCollapseBtn");
        var searchInput = page.Locator("#searchInput");

        // 1. Initially expanded
        Assert.True(await sidebar.IsVisibleAsync());
        Assert.Equal("true", await toggleBtn.GetAttributeAsync("aria-expanded"));
        var initialStageBox = await stage.BoundingBoxAsync();
        Assert.NotNull(initialStageBox);

        // 2. Collapse via sidebar button
        await collapseBtn.ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.False(await sidebar.IsVisibleAsync());
        Assert.Equal("false", await toggleBtn.GetAttributeAsync("aria-expanded"));
        var collapsedStageBox = await stage.BoundingBoxAsync();
        Assert.NotNull(collapsedStageBox);
        Assert.True(collapsedStageBox.Width > initialStageBox.Width + 200,
            $"Stage width ({collapsedStageBox.Width}) did not expand when sidebar collapsed (was {initialStageBox.Width})");

        // 3. Expand via header toggle button
        await toggleBtn.ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.True(await sidebar.IsVisibleAsync());
        Assert.Equal("true", await toggleBtn.GetAttributeAsync("aria-expanded"));

        // 4. Toggle with Ctrl+B shortcut
        await page.Keyboard.PressAsync("Control+b");
        await page.WaitForTimeoutAsync(100);
        Assert.False(await sidebar.IsVisibleAsync());

        // 5. Toggle with [ shortcut
        await page.Keyboard.PressAsync("[");
        await page.WaitForTimeoutAsync(100);
        Assert.True(await sidebar.IsVisibleAsync());

        // 6. Search hotkey '/' auto-expands sidebar when collapsed
        await page.Keyboard.PressAsync("Control+b");
        await page.WaitForTimeoutAsync(100);
        Assert.False(await sidebar.IsVisibleAsync());
        await page.Keyboard.PressAsync("/");
        await page.WaitForTimeoutAsync(100);
        Assert.True(await sidebar.IsVisibleAsync());
        Assert.True(await searchInput.EvaluateAsync<bool>("el => document.activeElement === el"));

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    /// <summary>
    /// The Report Builder's bookmark panel, driven in a real browser against the canonical
    /// <c>designer.js</c>.
    ///
    /// <para>Bookmarks reached the builder as data before they reached it as a surface: the designer
    /// state round-tripped them through parse/patch while offering no way to see or change one. The
    /// property asserted here is that the panel lists what the script declares and that the
    /// at-most-one-default rule the parser enforces is enforced in the editor too — authoring a second
    /// default in the builder would produce a script that no longer parses.</para>
    /// </summary>
    [Fact]
    public async Task DesignerBookmarkPanel_ListsDeclaredBookmarksAndKeepsOneDefault()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='designer']");
        await page.WaitForSelectorAsync("#dsgn-bookmark-list", new PageWaitForSelectorOptions { Timeout = 30_000 });

        var rows = page.Locator("#dsgn-bookmark-list .etlsql-dsgn-ds-block");
        Assert.Equal(2, await rows.CountAsync());
        Assert.Contains("West, Q4", await rows.Nth(0).InnerTextAsync());

        // The report default is marked, and exactly one of them carries it.
        Assert.Equal(1, await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_0']").CountAsync());
        Assert.Equal("★", (await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_0']").InnerTextAsync()).Trim());
        Assert.Equal("☆", (await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_1']").InnerTextAsync()).Trim());

        // Promoting the second must demote the first rather than leaving two defaults behind.
        await page.ClickAsync("#dsgn-bookmark-list [data-bmdefault='bm_1']");
        await page.WaitForTimeoutAsync(150);
        Assert.Equal("☆", (await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_0']").InnerTextAsync()).Trim());
        Assert.Equal("★", (await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_1']").InnerTextAsync()).Trim());

        // Deleting one leaves the other; the panel is the only place a bookmark can be removed
        // without hand-editing the script.
        await page.ClickAsync("#dsgn-bookmark-list [data-bmid='bm_0']");
        await page.WaitForTimeoutAsync(150);
        Assert.Equal(1, await rows.CountAsync());
        Assert.DoesNotContain("West, Q4", await page.Locator("#dsgn-bookmark-list").InnerTextAsync());

        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Proves that transient syntax errors in split-screen script editing retain the last valid
    /// canvas state instead of clearing or corrupting the designer canvas, while displaying
    /// the diagnostic badge. Restoring valid script clears the badge and updates state.
    /// </summary>
    [Fact]
    public async Task Designer_TransientSyntaxError_RetainsCanvasCardsAndDisplaysDiagnosticBadge()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        // Open the fixture directly so it cannot race the default story's asynchronous mount.
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html#story=designer&fixture=syntax-resilience");
        var badge = page.Locator("#dsgn-diagnostic-badge");
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal(1, await badge.CountAsync());
        Assert.True(await badge.IsVisibleAsync());
        Assert.Contains("Script syntax warning", await badge.InnerTextAsync());

        // 3. Canvas visual cards must remain intact (3 cards from sample state)
        var cards = page.Locator(".etlsql-dsgn-visual-card");
        Assert.Equal(3, await cards.CountAsync());

        // 4. Recover by applying a valid script
        await page.EvaluateAsync("""
            async () => {
                const stage = document.querySelector('#stage');
                if (stage && stage.__designerInstance) {
                    await stage.__designerInstance.applyScriptText(`
                        SELECT 'A' AS Date, 'V' AS Vendor, 100 AS total INTO #sales;
                        CREATE VISUAL salesBar AS BAR ( TITLE = 'Recovered Bar', SOURCE = #sales, MAPPINGS (X = Date, Y = total) );
                        CREATE PAGE Overview AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ('A' = salesBar) ) );
                    `);
                }
            }
        """);
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });

        // 5. Diagnostic badge must be hidden after recovery
        Assert.False(await badge.IsVisibleAsync());
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Proves that CUSTOM visual cards render dedicated Grammar-of-Graphics controls
    /// in the properties panel and live SVG GoG layered mark preview on the canvas.
    /// </summary>
    [Fact]
    public async Task Designer_CustomChartVisual_RendersGrammarOfGraphicsControlsAndPreview()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='designer']");
        await page.WaitForSelectorAsync("#fixtureSel", new PageWaitForSelectorOptions { Timeout = 30_000 });

        // 1. Mount the custom-chart fixture
        // Let the initial asynchronous mount finish before requesting another fixture.
        await page.Locator(".etlsql-dsgn-visual-card[data-vid='salesBar']").WaitForAsync();
        await page.SelectOptionAsync("#fixtureSel", "custom-chart");
        await page.Locator(".etlsql-dsgn-visual-card[data-vid='customGog'] svg").First.WaitForAsync();

        // 2. Visual card on canvas must be rendered with SVG preview
        var card = page.Locator(".etlsql-dsgn-visual-card");
        Assert.Equal(1, await card.CountAsync());
        var svg = card.Locator("svg");
        Assert.True(await svg.CountAsync() >= 1);
        Assert.Contains("CUSTOM CHART", await card.InnerTextAsync());

        // 3. Click the visual to select and open Properties panel
        await card.ClickAsync();
        await page.Locator("#pp-chart-coord").WaitForAsync();

        // 4. Grammar of Graphics (CHART) controls must be visible in properties panel
        var chartSection = page.Locator(".etlsql-dsgn-chart-editor-section");
        Assert.Equal(1, await chartSection.CountAsync());
        Assert.True(await chartSection.IsVisibleAsync());

        var coordSelect = page.Locator("#pp-chart-coord");
        Assert.True(await coordSelect.IsVisibleAsync());
        Assert.Equal("CARTESIAN", await coordSelect.InputValueAsync());

        var chartCode = page.Locator("#pp-chart-code");
        Assert.True(await chartCode.IsVisibleAsync());
        var codeVal = await chartCode.InputValueAsync();
        Assert.Contains("COORDINATE (TYPE = CARTESIAN)", codeVal);
        Assert.Contains("bars = RECT", codeVal);

        // 5. Modify coordinate to POLAR
        await coordSelect.SelectOptionAsync("POLAR");
        await page.WaitForFunctionAsync("() => document.querySelector('#pp-chart-code')?.value.includes('COORDINATE (TYPE = POLAR)')");
        var updatedCode = await chartCode.InputValueAsync();
        Assert.Contains("COORDINATE (TYPE = POLAR)", updatedCode);

        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Proves that HTML visual cards render dedicated Constrained HTML Component controls
    /// in the properties panel and live sanitized preview on the canvas.
    /// </summary>
    [Fact]
    public async Task Designer_ConstrainedHtmlVisual_RendersHtmlComponentControlsAndPreview()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='designer']");
        await page.WaitForSelectorAsync("#fixtureSel", new PageWaitForSelectorOptions { Timeout = 30_000 });

        // 1. Mount the custom-html fixture
        await page.SelectOptionAsync("#fixtureSel", "custom-html");

        // 2. Visual card on canvas must be rendered with HTML preview. Waited for rather than counted
        // after a fixed 300 ms: a slow remount still showed the previous fixture's three cards.
        var card = page.Locator(".etlsql-dsgn-visual-card");
        await Assertions.Expect(card).ToHaveCountAsync(1, new() { Timeout = 15_000 });
        var htmlPreview = card.Locator(".etlsql-html-visual-preview");
        await Assertions.Expect(htmlPreview).ToHaveCountAsync(1, new() { Timeout = 15_000 });
        Assert.Contains("CPU: CpuPercent", await htmlPreview.EvaluateAsync<string>(
            "element => element.shadowRoot?.textContent || ''"));

        // 3. Click the visual to select and open Properties panel
        await card.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // 4. Constrained HTML Component controls must be visible in properties panel
        var htmlSection = page.Locator(".etlsql-dsgn-html-editor-section");
        Assert.Equal(1, await htmlSection.CountAsync());
        Assert.True(await htmlSection.IsVisibleAsync());

        var modeSelect = page.Locator("#pp-html-mode");
        Assert.True(await modeSelect.IsVisibleAsync());
        Assert.Equal("REPEATER", await modeSelect.InputValueAsync());

        var templateInput = page.Locator("#pp-html-template");
        Assert.True(await templateInput.IsVisibleAsync());
        var templateVal = await templateInput.InputValueAsync();
        Assert.Contains("<article class=\"node-card\">", templateVal);
        Assert.Contains("{{HostName}}", templateVal);

        var styleInput = page.Locator("#pp-html-style");
        Assert.True(await styleInput.IsVisibleAsync());
        Assert.Contains(".node-card", await styleInput.InputValueAsync());

        var fallbackInput = page.Locator("#pp-html-fallback");
        Assert.True(await fallbackInput.IsVisibleAsync());
        Assert.Contains("Node: {{HostName}}", await fallbackInput.InputValueAsync());

        // 5. Modify mode to SINGLE
        await modeSelect.SelectOptionAsync("SINGLE");
        await page.WaitForTimeoutAsync(200);
        Assert.Equal("SINGLE", await modeSelect.InputValueAsync());

        // 6. Hostile authored markup fails closed in the live preview and cannot execute.
        await page.EvaluateAsync("() => window.__designerHtmlScriptExecuted = false");
        await templateInput.FillAsync(
            "<article>safe</article><img src='x' onerror='window.__designerHtmlScriptExecuted=true'>");
        await templateInput.PressAsync("Tab");
        await page.Locator(".etlsql-html-preview-error").WaitForAsync();
        Assert.False(await page.EvaluateAsync<bool>("() => window.__designerHtmlScriptExecuted"));
        Assert.Equal(0, await htmlPreview.Locator("img").CountAsync());

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConnectionWizard_CanonicalComponent_CoversSqlFilesDiagnosticsSecurityAndThemes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
              document.body.innerHTML = '<div id="canonical-connection-wizard"></div>';
              const { createConnectionWizard } = await import('/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/connection-wizard.js');
              createConnectionWizard({
                host: document.getElementById('canonical-connection-wizard'),
                schemas: [
                  {
                    connectorType: 'MSSQL', description: 'SQL Server', isFileBased: false,
                    options: [
                      { name: 'SERVER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'sql.test' },
                      { name: 'DATABASE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'warehouse' },
                      { name: 'PASSWORD', type: 3, category: 'Auth', mutuallyExclusiveGroup: 'Credentials' }
                    ]
                  },
                  {
                    connectorType: 'FLATFILE', description: 'Delimited file', isFileBased: true,
                    options: [
                      { name: 'PATH', type: 4, isMandatory: true, category: 'Basic', defaultValue: 'uploads/default.csv' },
                      { name: 'HEADER', type: 2, category: 'Basic', defaultValue: 'ON' }
                    ]
                  },
                  {
                    connectorType: 'MOCKDB', description: 'Generated test data', isFileBased: false,
                    options: []
                  }
                ],
                secrets: ['WAREHOUSE_PASSWORD'],
                gateways: [{ id: 'gateway-a', name: 'gateway-a', status: 'Active', region: 'On-Premises' }],
                stagedFiles: [{ name: 'sales.csv', path: 'uploads/sales.csv' }],
                existingNames: ['existing_connection'],
                onTest: async req => req.target.startsWith('fail')
                  ? { succeeded: false, error: 'Provider rejected the endpoint.', steps: [{ layer: 'TCP', status: 'failed', detail: 'Connection refused.' }] }
                  : { succeeded: true, steps: [
                      { layer: 'POLICY', status: 'ok', detail: 'Allowed.' },
                      { layer: 'DNS', status: 'ok', detail: 'Resolved.' },
                      { layer: 'TCP', status: 'ok', detail: 'Connected.' },
                      { layer: 'AUTH', status: 'ok', detail: 'Authenticated.' }
                    ] }
              });
            }
            """);

        await page.Locator("#etlsql-cw-alias-input").FillAsync("warehouse_reader");
        await page.Locator("#etlsql-cw-secret-key").FillAsync("WAREHOUSE_PASSWORD");
        await page.Locator("#etlsql-cw-gateway-select").SelectOptionAsync("gateway-a");

        var sql = await page.Locator(".etlsql-cw-sql-box").InnerTextAsync();
        Assert.Contains("CREATE CONNECTION warehouse_reader AS MSSQL", sql);
        Assert.Contains("PASSWORD = SECRET:WAREHOUSE_PASSWORD", sql);
        Assert.Contains("GATEWAY = 'gateway-a'", sql);

        await page.Locator("#etlsql-cw-test-btn").ClickAsync();
        await page.Locator(".etlsql-cw-diag-badge.badge-ok").WaitForAsync();
        Assert.Equal(4, await page.Locator(".etlsql-cw-step-item").CountAsync());

        await page.Locator("#etlsql-cw-opt-server").FillAsync("fail.internal");
        await page.Locator("#etlsql-cw-test-btn").ClickAsync();
        await page.Locator(".etlsql-cw-diag-badge.badge-fail").WaitForAsync();
        Assert.Contains("Provider rejected", await page.Locator(".etlsql-cw-diag-result").InnerTextAsync());

        await page.Locator("button[data-cat='testdata']").ClickAsync();
        Assert.Equal(1, await page.Locator("button[data-type='MOCKDB']").CountAsync());
        Assert.Equal(0, await page.Locator("button[data-type='MSSQL']").CountAsync());
        Assert.Contains("MOCKDB", await page.Locator(".etlsql-cw-sql-box").InnerTextAsync());

        await page.Locator("button[data-cat='files']").ClickAsync();
        await page.Locator("button[data-type='FLATFILE']").ClickAsync();
        await page.Locator("button[data-filepath='uploads/sales.csv']").ClickAsync();
        sql = await page.Locator(".etlsql-cw-sql-box").InnerTextAsync();
        Assert.Contains("FLATFILE", sql);
        Assert.Contains("PATH = 'uploads/sales.csv'", sql);

        await page.Locator("#etlsql-cw-opt-path").FillAsync("../secrets.sql");
        await page.Locator(".etlsql-cw-security-alert").WaitForAsync();
        Assert.True(await page.Locator("#etlsql-cw-submit-btn").IsDisabledAsync());

        foreach (var theme in new[] { "light", "dark" })
        {
            await page.EvaluateAsync("theme => document.documentElement.dataset.theme = theme", theme);
            var colors = await page.Locator(".etlsql-cw-modal").EvaluateAsync<string[]>(
                "el => [getComputedStyle(el).color, getComputedStyle(el).backgroundColor]");
            Assert.All(colors, color => Assert.DoesNotContain("rgba(0, 0, 0, 0)", color));
        }

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task ConnectionWizard_DiscoversAndBindsApprovedGatewayResources()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
              document.body.innerHTML = '<div id="canonical-connection-wizard"></div>';
              const { createConnectionWizard } = await import('/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/connection-wizard.js');
              window.__savedEntry = null;
              window.__gatewayResourceFetches = 0;
              createConnectionWizard({
                host: document.getElementById('canonical-connection-wizard'),
                mode: 'admin',
                schemas: [
                  {
                    connectorType: 'MSSQL', description: 'SQL Server', isFileBased: false,
                    options: [
                      { name: 'SERVER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'sql.test' },
                      { name: 'DATABASE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'warehouse' }
                    ]
                  },
                  {
                    connectorType: 'POSTGRES', description: 'PostgreSQL', isFileBased: false,
                    options: [
                      { name: 'HOST', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'pg.test' }
                    ]
                  }
                ],
                gateways: [{ id: 'corp-gw', name: 'corp-gw', status: 'Online', region: 'On-Premises' }],
                fetchGatewayResources: async (gwId) => {
                  window.__gatewayResourceFetches++;
                  return [
                  {
                    resourceId: 'finance-dw',
                    connectorType: 'MSSQL',
                    allowedOperations: 'Read, Write',
                    state: 'Approved',
                    isOnline: true,
                    lastSeenUtc: '2026-08-27T12:00:00Z'
                  },
                  {
                    resourceId: 'audit-pg',
                    connectorType: 'POSTGRES',
                    allowedOperations: 'Read',
                    state: 'Approved',
                    isOnline: true,
                    lastSeenUtc: '2026-08-27T12:05:00Z'
                  }
                  ];
                },
                onSave: async (entry) => {
                  window.__savedEntry = entry;
                }
              });
            }
            """);

        await page.Locator("#etlsql-cw-alias-input").FillAsync("corp_finance_conn");
        await page.Locator("#etlsql-cw-gateway-select").SelectOptionAsync("corp-gw");

        // Verify resource picker appeared with approved resources
        var resourceCard = page.Locator(".etlsql-cw-resource-card[data-resource-id='finance-dw']");
        await resourceCard.WaitForAsync();
        Assert.Contains("finance-dw", await resourceCard.InnerTextAsync());
        Assert.Contains("Approved", await resourceCard.InnerTextAsync());
        Assert.Contains("Read, Write", await resourceCard.InnerTextAsync());

        // Click to bind resource
        await resourceCard.ClickAsync();
        await page.Locator(".etlsql-cw-gateway-bound-banner").WaitForAsync();
        Assert.Contains("Gateway Resource Bound", await page.Locator(".etlsql-cw-gateway-bound-banner").InnerTextAsync());

        // Verify preview SQL
        var preview = await page.Locator(".etlsql-cw-sql-box").InnerTextAsync();
        Assert.Contains("Gateway: corp-gw", preview);
        Assert.Contains("Resource: finance-dw", preview);

        // Clear only the resource binding; keep the selected Gateway available for another choice.
        await page.Locator("[data-unbind-gateway-resource]").ClickAsync();
        Assert.Equal("corp-gw", await page.Locator("#etlsql-cw-gateway-select").InputValueAsync());
        Assert.Equal(0, await page.Locator(".etlsql-cw-gateway-bound-banner").CountAsync());
        Assert.Contains("SERVER", await page.Locator(".etlsql-cw-content").InnerTextAsync());

        // Manual refresh re-runs discovery without changing the selected Gateway.
        await page.Locator("[data-refresh-gateway-resources]").ClickAsync();
        await page.Locator(".etlsql-cw-resource-card[data-resource-id='finance-dw']").WaitForAsync();
        Assert.Equal(2, await page.EvaluateAsync<int>("() => window.__gatewayResourceFetches"));
        await page.Locator(".etlsql-cw-resource-card[data-resource-id='finance-dw']").ClickAsync();

        // Submit and verify payload contains gateway binding without physical target
        await page.Locator("#etlsql-cw-submit-btn").ClickAsync();
        var savedEntry = await page.EvaluateAsync<System.Text.Json.JsonElement>("() => window.__savedEntry");
        Assert.Equal("corp_finance_conn", savedEntry.GetProperty("alias").GetString());
        Assert.Equal("MSSQL", savedEntry.GetProperty("connectorType").GetString());
        var gatewayObj = savedEntry.GetProperty("gateway");
        Assert.Equal("corp-gw", gatewayObj.GetProperty("gatewayId").GetString());
        Assert.Equal("finance-dw", gatewayObj.GetProperty("resourceId").GetString());

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task ConnectionWizard_KeepsModalOpenWhenCatalogSaveFails()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
              document.body.innerHTML = '<div id="canonical-connection-wizard"></div>';
              const { createConnectionWizard } = await import('/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/connection-wizard.js');
              window.__saveAttempts = 0;
              createConnectionWizard({
                host: document.getElementById('canonical-connection-wizard'),
                mode: 'admin',
                schemas: [{ connectorType: 'MSSQL', description: 'SQL Server', options: [] }],
                gateways: [{ id: 'corp-gw', name: 'corp-gw', status: 'Online' }],
                fetchGatewayResources: async () => [{
                  resourceId: 'finance-dw', connectorType: 'MSSQL',
                  allowedOperations: 'Read', state: 'Approved', isOnline: true
                }],
                onSave: async () => {
                  window.__saveAttempts++;
                  throw new Error('sensitive upstream detail');
                }
              });
            }
            """);

        await page.Locator("#etlsql-cw-alias-input").FillAsync("corp_finance_conn");
        await page.Locator("#etlsql-cw-gateway-select").SelectOptionAsync("corp-gw");
        await page.Locator(".etlsql-cw-resource-card[data-resource-id='finance-dw']").ClickAsync();
        await page.Locator("#etlsql-cw-submit-btn").ClickAsync();

        var error = page.Locator(".etlsql-cw-save-error");
        await error.WaitForAsync();
        Assert.Contains("could not be saved", await error.InnerTextAsync());
        Assert.DoesNotContain("sensitive upstream detail", await error.InnerTextAsync());
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__saveAttempts"));
        Assert.Equal(1, await page.Locator(".etlsql-cw-modal").CountAsync());
        Assert.True(await page.Locator("#etlsql-cw-submit-btn").IsEnabledAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task OperationsAdmin_UnavailableSourcesNeverReportAllClearAndRetryRecovers()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.Locator("button.story-link[data-story-id='portal-operations']").ClickAsync();
        await page.SelectOptionAsync("#fixtureSel", "unavailable");
        var surface = page.FrameLocator("#stage iframe");
        await Expect(surface.Locator("#ops-error")).ToContainTextAsync("unavailable");
        var imagePath = Path.Combine(RepoRoot(), "artifacts", "section1", "qa", "operations-unavailable.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await page.Locator("#stage").ScreenshotAsync(new() { Path = imagePath });
        await Expect(surface.Locator("#ops-signals button[data-target='ops-authority']")).ToContainTextAsync("Unavailable");
        await Expect(surface.Locator("#ops-ambiguous-writes")).ToContainTextAsync("unavailable");
        Assert.DoesNotContain("Nothing pending", await surface.Locator("#ops-signals").InnerTextAsync());
        Assert.DoesNotContain("No unresolved", await surface.Locator("#ops-signals").InnerTextAsync());
        await surface.Locator("#operations").EvaluateAsync("""
            async () => {
              const { makeOperationsApi } = await import('/tools/ui-sandbox/stories/portal-operations.story.js');
              Object.assign(window.__operationsApi, makeOperationsApi('steady'));
            }
            """);
        await surface.GetByRole(AriaRole.Button, new() { Name = "Refresh snapshot" }).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(surface.Locator("#ops-signals")).ToContainTextAsync("Nothing pending");
        await Expect(surface.Locator("#ops-services")).ToContainTextAsync("ops@example.test;oncall@example.test");
        await page.SelectOptionAsync("#fixtureSel", "invalid");
        await Expect(surface.Locator("#ops-accounts")).ToContainTextAsync("unavailable");
        await Expect(surface.Locator("#ops-now-grid")).ToContainTextAsync("Unavailable");
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task OperationsAdmin_RefreshIgnoresLateResultsAndClearsFailedSources()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        var results = await page.EvaluateAsync<string[]>("""
            async () => {
              const { createOperationsAdmin } = await import('/src/ETL-SQL.Portal/wwwroot/js/operations-admin.js');
              const { makeOperationsApi } = await import('/tools/ui-sandbox/stories/portal-operations.story.js');
              const host = document.createElement('div'); document.body.append(host);
              const api = makeOperationsApi('steady'), requests = [];
              api.pendingAccessRequests = () => new Promise(resolve => requests.push(resolve));
              const surface = createOperationsAdmin({ host, adminApi: api });
              const first = surface.load(), second = surface.load();
              await Promise.resolve(); await Promise.resolve();
              requests[1]([]); await second;
              const latest = host.querySelector('#ops-signals').textContent;
              requests[0]([{ id: 42, reportTitle: 'Superseded', requesterUserName: 'old', createdAt: '2026-10-07T00:00:00Z' }]);
              await first;
              const afterLate = host.querySelector('#ops-signals').textContent;
              Object.assign(api, makeOperationsApi('attention')); await surface.load();
              const attention = host.querySelector('#ops-signals').textContent;
              api.pendingAccessRequests = async () => { throw new Error('Offline'); };
              api.gatewayAmbiguousWrites = async () => { throw new Error('Offline'); };
              await surface.load();
              const failed = host.querySelector('#ops-signals').textContent;
              api.pendingAccessRequests = () => new Promise(resolve => requests.push(resolve));
              const pending = surface.load(); await Promise.resolve(); await Promise.resolve();
              surface.dispose(); requests[2]([]); await pending;
              const afterDisposal = host.querySelector('#ops-signals').textContent;
              host.remove(); return [latest, afterLate, attention, failed, afterDisposal];
            }
            """);
        Assert.Equal(results[0], results[1]);
        Assert.Contains("Nothing pending", results[0]);
        Assert.Contains("Awaiting a decision", results[2]);
        Assert.Contains("Retry blocked", results[2]);
        Assert.Contains("Unavailable", results[3]);
        Assert.DoesNotContain("Awaiting a decision", results[3]);
        Assert.DoesNotContain("No unresolved", results[3]);
        Assert.DoesNotContain("Nothing pending", results[4]);
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task OperationsAdmin_OneTimeCredentialsClearAndLateMutationsDoNotReopenDialogs()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync("""
            async () => {
              document.body.innerHTML = '<main id="operations"></main>';
              const { createOperationsAdmin } = await import('/src/ETL-SQL.Portal/wwwroot/js/operations-admin.js');
              const { makeOperationsApi } = await import('/tools/ui-sandbox/stories/portal-operations.story.js');
              const api = makeOperationsApi('steady'), account = (await api.listServiceAccounts())[0];
              window.__rotationCount = 0;
              api.rotateServiceAccount = async (id, version) => {
                window.__rotationCount++;
                window.__rotationFence = { id, version };
                return new Promise(resolve => { window.__finishRotation = async () => {
                  resolve({ account, clientSecret: 'fixture-one-time-value' });
                  await new Promise(requestAnimationFrame);
                }; });
              };
              window.__operations = createOperationsAdmin({ host: document.getElementById('operations'), adminApi: api });
              await window.__operations.load();
            }
            """);
        await page.GetByRole(AriaRole.Button, new() { Name = "Rotate", Exact = true }).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#ops-modal [data-dialog-close]")).ToBeFocusedAsync();
        await page.Locator("#ops-modal-submit").ClickAsync();
        await Expect(page.Locator("#ops-modal-submit")).ToBeDisabledAsync();
        await page.Locator("#ops-modal-submit").EvaluateAsync("button => button.click()");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__rotationCount"));
        await page.EvaluateAsync("() => window.__finishRotation()");
        await Expect(page.Locator("#ops-secret-value")).ToHaveTextAsync("fixture-one-time-value");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#ops-modal")).ToBeHiddenAsync();
        Assert.Equal(0, await page.Locator("#ops-secret-value").CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Rotate", Exact = true }).ClickAsync();
        await page.Locator("#ops-modal-submit").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await page.EvaluateAsync("() => window.__finishRotation()");
        await Expect(page.Locator("#ops-modal")).ToBeHiddenAsync();
        Assert.Equal(0, await page.Locator("#ops-secret-value").CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Rotate", Exact = true }).ClickAsync();
        await page.Locator("#ops-modal-submit").ClickAsync();
        await page.EvaluateAsync("async () => { window.__operations.dispose(); await window.__finishRotation(); }");
        await Expect(page.Locator("#ops-modal")).ToBeHiddenAsync();
        Assert.Equal(0, await page.Locator("#ops-secret-value").CountAsync());
        var fence = await page.EvaluateAsync<JsonElement>("() => window.__rotationFence");
        Assert.Equal("machine-1", fence.GetProperty("id").GetString());
        Assert.Equal(1, fence.GetProperty("version").GetInt32());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task OperationsAdmin_ReconcilesAmbiguousGatewayWriteWithImmutableEvidence()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
              document.body.innerHTML = '<div id="operations-admin"></div>';
              const { createOperationsAdmin } = await import('/src/ETL-SQL.Portal/wwwroot/js/operations-admin.js');
              let item = {
                id: 7, operationId: 'op-write-7f3a', tenantId: 'tenant-a', gatewayId: 'hq-gateway',
                resourceId: 'corp-sql-sales', correlationId: 'corr-98d1',
                executedAtUtc: '2026-08-27T14:42:00Z', state: 'Acknowledged', priority: 'High',
                owner: 'database-operations', resolution: null, version: 2,
                events: [
                  { eventType: 'Detected', actor: 'Gateway', note: 'Ambiguous outcome.', createdAtUtc: '2026-08-27T14:42:00Z' },
                  { eventType: 'Acknowledged', actor: 'admin', note: 'Investigating.', createdAtUtc: '2026-08-27T14:45:00Z' }
                ]
              };
              const empty = async () => [];
              const admin = createOperationsAdmin({
                host: document.getElementById('operations-admin'),
                adminApi: {
                  operationalMetrics: async () => ({ queuedExecutions: 0, activeExecutions: 1, executionCap: 8, auditOutboxPending: 0, auditOutboxFailed: 0, auditOutboxOldestPendingAgeSeconds: 0, windowHours: 24, recentExecutionFailures: 0, recentExecutions: 1, staleDatasets: 0, datasetStorageBytes: 0, securityEventPending: 0, securityEventFailed: 0, nodeId: 'test-node', schemaUpToDate: true }),
                  fleetStatus: async () => ({ status: 'Healthy', environment: 'Test', storage: 'Ready', inventory: { nodeId: 'test-node', installedVersion: 'test', upgradeReadiness: { ready: true, findings: [] } } }),
                  gatewayAmbiguousWrites: async () => [item],
                  pendingAccessRequests: empty, listServiceAccounts: empty, anonymousReportAccess: empty,
                  listAdminServices: empty, listUsers: empty,
                  resolveGatewayAmbiguousWrite: async (id, body) => {
                    window.__resolutionRequest = { id, body };
                    item = { ...item, state: 'Resolved', resolution: body.resolution, version: item.version + 1,
                      events: [...item.events, { eventType: 'Resolved', actor: 'admin', note: body.note,
                        evidenceReference: body.evidenceReference, resolution: body.resolution,
                        createdAtUtc: '2026-08-27T15:00:00Z' }] };
                    return item;
                  }
                }
              });
              await admin.load();
            }
            """);

        Assert.Contains("Retry blocked", await page.Locator("#ops-signals").InnerTextAsync());
        Assert.Contains("op-write-7f3a", await page.Locator("#ops-ambiguous-writes").InnerTextAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Review case" }).ClickAsync();
        Assert.Contains("Immutable event history", await page.Locator("#ops-modal-body").InnerTextAsync());
        Assert.Contains("Detected", await page.Locator("#ops-modal-body").InnerTextAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Record verified outcome" }).ClickAsync();
        await page.Locator("#ops-case-resolution").SelectOptionAsync("confirmed committed");
        await page.Locator("#ops-case-evidence").FillAsync("INC-2042/query-17");
        await page.Locator("#ops-case-note").FillAsync("Target row and transaction log verified externally.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Record outcome" }).ClickAsync();

        await page.Locator("#ops-ambiguous-writes").GetByText("Resolved", new() { Exact = true }).WaitForAsync();
        var request = await page.EvaluateAsync<System.Text.Json.JsonElement>("() => window.__resolutionRequest");
        Assert.Equal(7, request.GetProperty("id").GetInt32());
        Assert.Equal("confirmed committed", request.GetProperty("body").GetProperty("resolution").GetString());
        Assert.Equal("INC-2042/query-17", request.GetProperty("body").GetProperty("evidenceReference").GetString());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_ScriptPane_KeepsHandAuthoredTextAndInsertedConnections()
    {
        // Typing into the script pane used to erase itself. The canvas regenerates its script from
        // the design state alone, and updating the canvas *from* the editor let that regeneration
        // run and overwrite the author's text ~800ms later -- so anything the design state does not
        // model, most visibly a CREATE CONNECTION, vanished as it was typed. That made the
        // Connection Wizard look broken too: its inserted statement disappeared moments later.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        var survived = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            async () => {
                const studio = window.__STUDIO_INSTANCE__;
                const editor = studio.state.editorInstance;
                const marker = 'CREATE CONNECTION demo AS MOCKDB();';
                editor.setValue(marker + String.fromCharCode(10) + editor.getValue());

                // Long enough for both debounces to fire: editor->canvas (400ms) and the
                // canvas->script regeneration (400ms) that used to clobber the buffer.
                await new Promise(resolve => setTimeout(resolve, 2000));
                return { keptConnection: editor.getValue().includes(marker) };
            }
            """);

        Assert.True(survived.GetProperty("keptConnection").GetBoolean(),
            "The script pane discarded a hand-authored CREATE CONNECTION after the canvas re-rendered.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_CanvasEdits_StillWriteBackToTheScript()
    {
        // The guard above must stay surgical: suppressing the write-back only while ingesting
        // script text, never for a genuine canvas edit. Without this, fixing the clobber would
        // silently sever canvas-to-code sync.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        var result = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            async () => {
                const studio = window.__STUDIO_INSTANCE__;
                const editor = studio.state.editorInstance;
                const before = editor.getValue();
                studio.state.designerInstance.addVisual('BAR');
                await new Promise(resolve => setTimeout(resolve, 2000));
                return { grew: editor.getValue().length > before.length };
            }
            """);

        Assert.True(result.GetProperty("grew").GetBoolean(),
            "A canvas edit no longer writes back to the script.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_DatasetEdits_RefreshTheRightSampleAndIgnoreStaleOrInvalidResults()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        var result = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            async () => {
                const studio = window.__STUDIO_INSTANCE__;
                const editor = studio.state.editorInstance;
                window.__STUDIO_API_DELAY__ = ({ url, body }) =>
                    url.endsWith('/api/designer/data-sample') && body?.script?.includes('stale_metric') ? 1400 : 0;

                editor.setValue(editor.getValue().replace(
                    'SELECT order_date, total_amount, region',
                    'SELECT order_date, total_amount AS stale_metric, region'));
                await new Promise(resolve => setTimeout(resolve, 650));
                editor.setValue(editor.getValue().replace('stale_metric', 'fresh_metric'));
                await new Promise(resolve => setTimeout(resolve, 2200));

                const document = studio.state.documents.find(item => item.id === 'doc-report');
                const sampleRequestsBeforeInvalid = window.__STUDIO_API_REQUESTS__.filter(request =>
                    request.url.endsWith('/api/designer/data-sample') && request.body?.sourceKind === 'dataset').length;
                const validSnapshot = JSON.stringify(document.studioContext.snapshot);
                const visualCount = document.querySelectorAll?.('.designer-card')?.length
                    ?? window.document.querySelectorAll('.designer-card').length;

                editor.setValue(editor.getValue() + String.fromCharCode(10) + '>>> INVALID <<<');
                await new Promise(resolve => setTimeout(resolve, 1100));

                const sampleRequestsAfterInvalid = window.__STUDIO_API_REQUESTS__.filter(request =>
                    request.url.endsWith('/api/designer/data-sample') && request.body?.sourceKind === 'dataset').length;
                return {
                    sampleRequestsBeforeInvalid,
                    sampleRequestsAfterInvalid,
                    columns: document.studioContext.snapshot.columns,
                    snapshotPreserved: JSON.stringify(document.studioContext.snapshot) === validSnapshot,
                    visualCount,
                    visualCountAfterInvalid: window.document.querySelectorAll('.designer-card').length,
                };
            }
            """);

        Assert.Equal(2, result.GetProperty("sampleRequestsBeforeInvalid").GetInt32());
        Assert.Equal(2, result.GetProperty("sampleRequestsAfterInvalid").GetInt32());
        var columns = result.GetProperty("columns").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("fresh_metric", columns);
        Assert.DoesNotContain("stale_metric", columns);
        Assert.True(result.GetProperty("snapshotPreserved").GetBoolean());
        Assert.Equal(result.GetProperty("visualCount").GetInt32(), result.GetProperty("visualCountAfterInvalid").GetInt32());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_SandboxStory_DoesNotOfferAHostExitAction()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        Assert.Equal(0, await page.Locator("[data-action='exit']").CountAsync());
        Assert.Null(await page.EvaluateAsync<object?>("() => window.__STUDIO_EXIT_REQUESTS__"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_HelperContextControlsExplainUnavailableContext()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html#story=studio&fixture=helper-context");
        var helper = page.GetByRole(AriaRole.Region, new() { Name = "Helper preview context example" });
        await Expect(helper.Locator("[data-preview-context]")).ToBeCheckedAsync();
        await helper.Locator("[data-workbench-run]").ClickAsync();
        await Expect(helper.Locator("[data-workbench-output]")).ToContainTextAsync("1 row sampled");
        await helper.GetByRole(AriaRole.Spinbutton, new() { Name = "Preview context ends before line" }).FillAsync("4");
        await helper.Locator("[data-workbench-run]").ClickAsync();
        await Expect(helper.Locator("[data-workbench-output]")).ToContainTextAsync("Nothing was executed");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_DegradedEditorStoryRetriesWithTheLatestTypedText()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.RouteAsync("**/codemirror-bundle.min.js*", route => route.AbortAsync());
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html#story=studio&fixture=degraded-editor");
        var textarea = page.GetByRole(AriaRole.Textbox, new() { Name = "Script editor (basic mode)" });
        await textarea.FillAsync("-- kept in the recovery story\nSELECT 8;");
        await page.UnrouteAsync("**/codemirror-bundle.min.js*");
        await page.Locator("[data-action='retry-cm']").ClickAsync();
        await Expect(page.Locator(".cm-content").First).ToContainTextAsync("kept in the recovery story");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_CatalogPagingStorySearchesAndNavigates()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html#story=studio&fixture=catalog-paging");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.catalogReports[0].id === 51");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Search catalog", Exact = true }).FillAsync("125");
        await page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.catalogReports.length === 1 && window.__STUDIO_INSTANCE__.state.catalogReports[0].id === 125");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_LargeDocumentStoryExplainsPausedServices()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html#story=studio&fixture=large-document");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state.editorInstance)");
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.analyze()");
        await page.Locator(".etlsql-script-results [data-tab='messages']").ClickAsync();
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync("All text is kept and editable");
        Assert.Equal(200_001, await page.EvaluateAsync<int>("() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue().length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_ConnectionWizard_OffersMockDbUnderTestData()
    {
        // MOCKDB is the only connector a new author can use with no database, and it backs Studio
        // Home's "Start with sample data". The wizard falls back to a built-in connector list when
        // discovery fails, and that list had no MOCKDB -- so the zero-dependency on-ramp went
        // missing exactly when the environment could not reach a real server.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        await page.ClickAsync(".etlsql-studio-rail-btn[data-activity='catalog']");
        await page.ClickAsync("[data-action='wizard']");
        await page.Locator(".etlsql-cw-overlay").WaitForAsync();
        await page.ClickAsync(".etlsql-cw-overlay [data-cat='testdata']");

        await page.Locator(".etlsql-cw-overlay [data-type='MOCKDB']").WaitForAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_ConnectionWizard_PrefillsAUsableAliasAndBlocksInvalidOnes()
    {
        // The alias was marked required but nothing enforced it, and generateSql substitutes the
        // literal `<alias>` placeholder when it is blank -- so confirming the dialog wrote
        // `CREATE CONNECTION <alias> AS ...` into the script, which does not parse. It is also the
        // one field a newcomer has no basis to fill in before knowing what the connection is for,
        // so it is prefilled with a free name rather than left as a wall.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        await page.ClickAsync(".etlsql-studio-rail-btn[data-activity='catalog']");
        await page.ClickAsync("[data-action='wizard']");
        await page.Locator(".etlsql-cw-overlay").WaitForAsync();

        var alias = page.Locator("#etlsql-cw-alias-input");
        var submit = page.Locator("#etlsql-cw-submit-btn");

        // Prefilled and immediately usable.
        Assert.False(string.IsNullOrWhiteSpace(await alias.InputValueAsync()));
        Assert.True(await submit.IsEnabledAsync());

        // The generated SQL shows a real alias, never the placeholder.
        Assert.DoesNotContain("<alias>", await page.Locator(".etlsql-cw-overlay").InnerTextAsync(), StringComparison.Ordinal);

        // Cleared: blocked.
        await alias.FillAsync("");
        Assert.True(await submit.IsDisabledAsync());

        // Not an identifier: blocked, and the hint says why rather than just "required".
        await alias.FillAsync("9 bad-name");
        Assert.True(await submit.IsDisabledAsync());
        Assert.Contains("must start with a letter",
            await page.Locator(".etlsql-cw-missing-hint").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        // Valid again: re-enabled.
        await alias.FillAsync("good_name");
        Assert.True(await submit.IsEnabledAsync());

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_SavedPipelineRun_ShowsOnlyWhatTheHostReportedForThatRun()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.SelectOptionAsync("#fixtureSel", "pipeline-run");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__?.state.activeDocId === 'doc-etl'");

        const string trace = "window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').studioContext.resultsTrace";
        async Task<string> Messages() => await page.EvaluateAsync<string>(
            $"() => {trace}.filter(e => e.type === 'message').map(e => String(e.text)).join('\\n')");
        Task Done(string outcome) => page.WaitForFunctionAsync(
            $"outcome => {trace}.some(e => e.type === 'done' && e.status === outcome)", outcome,
            new PageWaitForFunctionOptions { Timeout = 20_000 });

        // A completed run: its id, its statements, and no request to the read-only run endpoint.
        await page.Locator("[data-action='run']").ClickAsync();
        await Done("Complete");
        var completed = await Messages();
        Assert.Contains("Run 101 accepted by the Orchestrator for saved version 3", completed);
        Assert.Contains("Ran: SELECT id, product_id", completed);
        Assert.Contains("Run 101 finished with status SUCCESS. 128 rows processed.", completed);
        Assert.False(await page.EvaluateAsync<bool>(
            "() => window.__STUDIO_API_REQUESTS__.some(r => r.url.endsWith('/api/designer/run'))"));
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync("Run 101 finished with status SUCCESS");

        // A failed run is a failure, with the failing statement and the error, under its own id.
        await page.EvaluateAsync("() => { window.__STUDIO_PIPELINE_RUN__ = { outcome: 'FAILURE' }; }");
        await page.Locator("[data-action='run']").ClickAsync();
        await Done("Failed");
        var failed = await Messages();
        Assert.Contains("Failed: ASSERT", failed);
        Assert.Contains("Assertion failed: No clean sales rows were staged.", failed);
        Assert.Contains("Run 102 finished with status FAILURE", failed);
        Assert.DoesNotContain("Run 101", failed);

        // A refused submission ran nothing and says so.
        await page.EvaluateAsync("() => { window.__STUDIO_PIPELINE_RUN__ = { outcome: 'refuse' }; }");
        await page.Locator("[data-action='run']").ClickAsync();
        await Done("Not run");
        var refused = await Messages();
        Assert.Contains("holds no EXECUTE grant", refused);
        Assert.Contains("Nothing was run.", refused);
        Assert.DoesNotContain("accepted by the Orchestrator", refused);

        // Unsaved edits are refused before the host is asked at all.
        await page.EvaluateAsync("() => { window.__STUDIO_PIPELINE_RUN__ = null; const e = window.__STUDIO_INSTANCE__.state.editorInstance; e.setValue(e.getValue() + '\\n-- edit'); }");
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').isDirty === true");
        await page.Locator("[data-action='run']").ClickAsync();
        await page.WaitForFunctionAsync($"() => {trace}.some(e => e.type === 'message' && String(e.text).includes('unsaved edits'))");
        Assert.DoesNotContain("Run 103", await Messages());

        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Studio_PipelineCanvasUsesEngineDagAndPreservesScriptBytes(bool initialCrlfProjection)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        if (initialCrlfProjection)
            await page.SelectOptionAsync("#fixtureSel", "crlf-pipeline");
        await WaitForStudioAsync(page);
        if (initialCrlfProjection)
            await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__?.state.activeDocId === 'doc-etl'");
        else
            await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");

        var status = page.Locator("[data-dag-status]");
        await status.WaitForAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");

        var sourceBefore = await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content");
        var editorBefore = await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()");
        if (initialCrlfProjection) Assert.Contains("\r\n", sourceBefore);
        var dagNodes = page.Locator("[data-dag-node]");
        // Six projected stages plus the one labelled task the sample script declares.
        Assert.Equal(7, await dagNodes.CountAsync());
        Assert.Contains("IF", await page.Locator("[data-dag-node='quality_branch']").InnerTextAsync());
        Assert.Contains("ASSERT", await page.Locator("[data-dag-node='quality_gate']").InnerTextAsync());

        // The map runs left to right and does not shrink below a readable zoom to fit its full width,
        // so the author pans along it. It must still sit within the pane vertically and open on its
        // first stage; readability itself is Studio_PipelineMap_ConnectorsAreReadableAtTheZoomItOpensAt.
        var canvasBox = await page.Locator(".etlsql-dag-canvas").BoundingBoxAsync();
        Assert.NotNull(canvasBox);
        for (var i = 0; i < await dagNodes.CountAsync(); i++)
        {
            var nodeBox = await dagNodes.Nth(i).BoundingBoxAsync();
            Assert.NotNull(nodeBox);
            Assert.True(nodeBox!.Y >= canvasBox!.Y - 1);
            Assert.True(nodeBox.Y + nodeBox.Height <= canvasBox.Y + canvasBox.Height + 1);
        }

        var firstBox = await page.Locator("[data-dag-node='staging_db']").BoundingBoxAsync();
        Assert.True(firstBox!.X >= canvasBox!.X - 1 && firstBox.X + firstBox.Width <= canvasBox.X + canvasBox.Width + 1);

        var trueEdge = page.Locator("[data-dag-source='quality_branch'][data-dag-target='#ready_sales'][data-dag-label='TRUE']");
        var elseEdge = page.Locator("[data-dag-source='quality_branch'][data-dag-target='#quarantine_sales'][data-dag-label='ELSE']");
        await AssertEdgeDrawnAsync(trueEdge, "TRUE");
        await AssertEdgeDrawnAsync(elseEdge, "ELSE");

        await page.Locator("[data-dag-node='quality_branch']").ClickAsync();
        var sourceAfterNavigation = await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content");
        Assert.Equal(sourceBefore, sourceAfterNavigation);

        var requestScript = await page.EvaluateAsync<string>("""
            () => [...window.__STUDIO_API_REQUESTS__]
              .reverse()
              .find(request => request.url.endsWith('/api/designer/dag'))
              ?.body?.script
            """);
        Assert.Equal(editorBefore, requestScript);

        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.setValue('>>> INVALID <<<')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Last valid flow')");
        Assert.Equal(7, await page.Locator("[data-dag-node]").CountAsync());
        Assert.Contains("Unexpected token", await status.InnerTextAsync());

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    /// <summary>
    /// Waits for a DAG edge and asserts it was drawn, <b>without</b> waiting for it to be visible.
    ///
    /// <para>Edges are SVG <c>path</c> elements, and <c>Locator.WaitForAsync()</c> defaults to
    /// waiting for visibility. Playwright decides visibility from <c>getClientRects()</c>, which
    /// for a path reports the <b>geometry</b> box and ignores the stroke — so an edge that lays out
    /// axis-aligned, a straight vertical or horizontal run between two stages, measures exactly
    /// zero on one axis and is never visible, however plainly a reader can see the line.</para>
    ///
    /// <para>Measured on this graph: the two conditional edges lay out diagonally and report
    /// 9.55 × 22.95, so the old wait passed. Forcing one of them straight gives client rects of
    /// <c>[0, 41.06]</c> — while <c>BoundingBoxAsync</c> still says 0.39 wide, because that one
    /// does count the stroke — <c>IsVisibleAsync</c> answers false, and the visibility wait times
    /// out. Nothing about the product differs between those two runs, which is what made this read
    /// as a flake rather than as a wait watching the wrong thing.</para>
    ///
    /// <para>Attached is the honest wait, and <c>d</c> is the mechanism worth asserting: an edge in
    /// the document with no path data is an edge nobody can see either.</para>
    /// </summary>
    private static async Task AssertEdgeDrawnAsync(ILocator edge, string label)
    {
        await edge.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000,
        });

        var geometry = await edge.GetAttributeAsync("d");
        Assert.False(
            string.IsNullOrWhiteSpace(geometry),
            $"The {label} edge is in the document but carries no path data, so nothing was drawn.");
    }

    /// <summary>
    /// The pipeline map reads as a flow: step, arrow, step — at the zoom it opens at.
    ///
    /// <para>The test above proves every edge has path data. That was true while the map fitted a
    /// seven-stage pipeline into the pane at 0.305 zoom: cards 79 × 11 px, 13 px between them, and a
    /// 0.55 px grey stroke with no arrowhead. Every edge existed; a reader saw a row of unconnected
    /// pills. So this measures what reaches the screen: legible cards, a gap a connector can be seen
    /// in, a stroke that does not thin out with zoom, and a head that says which way it runs.</para>
    ///
    /// <para>Fixing that exposed the older defect underneath: the SVG layer is placed at -10000px by
    /// CSS and the paths were never offset to match, so no connector on any map had ever been on
    /// screen. Hence <c>spansGap</c> — the line must land between the two cards it joins.</para>
    /// </summary>
    [Fact]
    public async Task Studio_PipelineMap_ConnectorsAreReadableAtTheZoomItOpensAt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.etlsql-dag-svg path[data-dag-source]').length >= 7");

        var measured = await page.EvaluateAsync<JsonElement>("""
            () => {
              const canvas = document.querySelector('[data-dag-canvas] .etlsql-dag-canvas').getBoundingClientRect();
              const box = id => document.querySelector(`[data-dag-node="${CSS.escape(id)}"]`).getBoundingClientRect();
              const edges = [...document.querySelectorAll('.etlsql-dag-svg path[data-dag-source]')].map(path => {
                const from = box(path.dataset.dagSource);
                const to = box(path.dataset.dagTarget);
                const marker = (path.getAttribute('marker-end') || '').match(/url\(#(.+)\)/)?.[1];
                const line = path.getBoundingClientRect();
                return {
                  name: `${path.dataset.dagSource} -> ${path.dataset.dagTarget}`,
                  gap: to.left - from.right,
                  // Where the stroke actually lands. For a long time every path had data and none
                  // was on screen: the SVG layer was offset by CSS and the paths were not.
                  spansGap: line.left <= from.right + 8 && line.right >= to.left - 8
                    && line.top >= Math.min(from.top, to.top) - 8 && line.bottom <= Math.max(from.bottom, to.bottom) + 8,
                  vectorEffect: getComputedStyle(path).vectorEffect,
                  strokeWidth: Number(path.getAttribute('stroke-width')),
                  hasHead: Boolean(marker && document.getElementById(marker)),
                };
              });
              const header = document.querySelector('[data-dag-node="staging_db"] .etlsql-dag-card-header').getBoundingClientRect();
              const first = box('staging_db');
              return {
                headerHeight: header.height,
                firstVisible: first.left >= canvas.left - 1 && first.right <= canvas.right + 1,
                edges,
              };
            }
            """);

        Assert.True(measured.GetProperty("headerHeight").GetDouble() >= 24,
            $"Card headers render {measured.GetProperty("headerHeight").GetDouble():0.#} px tall; their text cannot be read.");
        Assert.True(measured.GetProperty("firstVisible").GetBoolean(),
            "The first step of the pipeline is off screen, so the flow has no visible start.");

        foreach (var edge in measured.GetProperty("edges").EnumerateArray())
        {
            var name = edge.GetProperty("name").GetString();
            Assert.True(edge.GetProperty("gap").GetDouble() >= 48,
                $"{name}: {edge.GetProperty("gap").GetDouble():0.#} px between the cards leaves no room to see the connector.");
            Assert.True(edge.GetProperty("spansGap").GetBoolean(),
                $"{name}: the connector is drawn, but not between the two cards it joins.");
            Assert.Equal("non-scaling-stroke", edge.GetProperty("vectorEffect").GetString());
            Assert.True(edge.GetProperty("strokeWidth").GetDouble() >= 1.5, $"{name}: connector stroke is too thin.");
            Assert.True(edge.GetProperty("hasHead").GetBoolean(), $"{name}: connector has no arrowhead, so its direction is unreadable.");
        }

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }


    [Fact]
    public async Task Studio_PipelineTasks_AreEditableOnTheCanvasAndLeaveTheRestOfTheScriptAlone()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");

        string Script() => page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content").Result;

        var before = Script();

        // Only the labelled statement is editable. Everything else on the map is projection-only.
        var task = page.Locator("[data-task-key='load_orders']");
        await task.WaitForAsync();
        Assert.Equal(1, await page.Locator("[data-task-key]").CountAsync());
        Assert.True(await task.EvaluateAsync<bool>("element => element.draggable"));
        Assert.False(await page.Locator("[data-dag-node='quality_gate']").EvaluateAsync<bool>("element => element.draggable"));

        // The palette offers every kind that passed its emission gate, and none of them is a dead
        // control — a chip that cannot write its statement would be worse than no chip. The count is
        // deliberately exact: it is the vocabulary the canvas claims to teach, and a chip quietly
        // disappearing from a drawer is the kind of loss nothing else here would notice.
        Assert.Equal(31, await page.Locator("[data-task-kind]").CountAsync());
        Assert.Equal(0, await page.Locator("[data-task-kind][disabled]").CountAsync());

        // Grouped, and every chip is a drag source: the drag is the gesture the canvas exists for.
        Assert.Equal(10, await page.Locator("[data-palette-group]").CountAsync());
        Assert.Equal(31, await page.Locator("[data-task-kind][draggable='true']").CountAsync());

        // Renaming goes through the task editor, and writes only the label.
        await task.ClickAsync();
        await page.ClickAsync("[data-task-edit]");
        var labelField = page.Locator("[data-task-id]");
        await labelField.WaitForAsync();

        // The execution editor carries the shared query workbench, not a bare textarea: that is what
        // gives the SQL a task runs the same completions, diagnostics, run, and results as the script.
        await page.Locator("[data-workbench-run]").WaitForAsync();

        await labelField.FillAsync("load_orders_v2");
        await page.ClickAsync("[data-dialog-action='save']");
        await page.WaitForFunctionAsync(
            """() => !!document.querySelector("[data-task-key='load_orders_v2']")""");

        var renamed = Script();
        Assert.Equal(before.Replace("load_orders:", "load_orders_v2:", StringComparison.Ordinal), renamed);

        // A validation task is authored as fields, and writes one ASSERT under its label.
        await page.ClickAsync("[data-task-kind='validation']");
        await page.Locator("[data-task-field='condition']").WaitForAsync();
        await page.Locator("[data-task-id]").FillAsync("orders_arrived");
        await page.Locator("[data-task-field='condition']").FillAsync("(SELECT COUNT(*) FROM #ready_sales) > 0");
        await page.Locator("[data-task-field='message']").FillAsync("No clean sales rows were staged.");
        await page.ClickAsync("[data-dialog-action='save']");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-task-key]').length === 2");

        var added = Script();
        Assert.Contains("orders_arrived:", added, StringComparison.Ordinal);
        Assert.Contains("ASSERT (SELECT COUNT(*) FROM #ready_sales) > 0", added, StringComparison.Ordinal);
        Assert.StartsWith(renamed.TrimEnd(), added.TrimEnd()[..renamed.TrimEnd().Length], StringComparison.Ordinal);

        // A half-filled task is refused before anything is written, with a sentence about the field
        // that is missing rather than a parse error about syntax the author never typed.
        await page.ClickAsync("[data-task-kind='copyfile']");
        await page.Locator("[data-task-field='source']").WaitForAsync();
        await page.Locator("[data-task-id]").FillAsync("half_filled");
        await page.ClickAsync("[data-dialog-action='save']");
        Assert.Contains("needed before this task can be written",
            await page.Locator("[data-dialog-body]").InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal(added, Script());
        await page.ClickAsync("[data-dialog-action='cancel']");

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    /// <summary>
    /// The loop the canvas exists for: drag a statement onto the map, fill in a form, and read the
    /// ETL-SQL it produced.
    ///
    /// <para>The third step is the one that was missing. A canvas that quietly edits a file you
    /// cannot see is something you operate; one that shows you the statement it just wrote is
    /// something you learn the language from, which is the whole reason Studio has a palette.</para>
    /// </summary>
    [Fact]
    public async Task Studio_DraggingAPaletteChipWritesAStatementAndShowsTheAuthorTheCode()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");

        string Script() => page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content").Result;

        // ── Dropped onto a task: written after that task, not at the end of the file ──────────
        await DragChipAsync(page, "movefile", "[data-task-key='load_orders']");
        await page.Locator("[data-task-field='source']").WaitForAsync();

        // The dialog says where the statement is about to land, because the drop decided it.
        Assert.Contains("after load_orders",
            await page.Locator("[data-dialog-body]").InnerTextAsync(), StringComparison.Ordinal);

        await page.Locator("[data-task-id]").FillAsync("stage_extract");
        await page.Locator("[data-task-field='source']").FillAsync(@"C:\data\incoming\orders.csv");
        await page.Locator("[data-task-field='target']").FillAsync(@"C:\data\working\orders.csv");
        await page.ClickAsync("[data-dialog-action='save']");
        await page.WaitForFunctionAsync("""() => !!document.querySelector("[data-task-key='stage_extract']")""");

        var written = Script();
        Assert.Contains("stage_extract:", written, StringComparison.Ordinal);
        Assert.Contains(@"MOVE FILE 'C:\data\incoming\orders.csv' TO 'C:\data\working\orders.csv';",
            written, StringComparison.Ordinal);

        // The statement it wrote is the statement the editor is now showing, selected. This is the
        // half of the loop that teaches: the author sees the ETL-SQL their gesture produced.
        var selected = await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getSelection()");
        Assert.Contains("stage_extract:", selected, StringComparison.Ordinal);
        Assert.Contains("MOVE FILE", selected, StringComparison.Ordinal);

        // Exactly the statement, and nothing else in the file: a selection that ran to the end of
        // the script would contain these two strings too and teach the author nothing about which
        // lines were theirs.
        Assert.DoesNotContain("load_orders:", selected, StringComparison.Ordinal);

        // ── Dropped onto a block: written inside it, in one edit ─────────────────────────────
        await page.ClickAsync("[data-task-kind='parallel']");
        await page.Locator("[data-task-id]").FillAsync("load_both");
        await page.ClickAsync("[data-dialog-action='save']");
        await page.WaitForFunctionAsync("""() => !!document.querySelector("[data-task-key='load_both']")""");

        var beforeNesting = Script();

        await DragChipAsync(page, "createdirectory", "[data-task-key='load_both']");
        await page.Locator("[data-task-field='source']").WaitForAsync();
        Assert.Contains("inside load_both",
            await page.Locator("[data-dialog-body]").InnerTextAsync(), StringComparison.Ordinal);
        await page.Locator("[data-task-id]").FillAsync("make_archive");
        await page.Locator("[data-task-field='source']").FillAsync(@"C:\data\archive");
        await page.ClickAsync("[data-dialog-action='save']");
        await page.WaitForFunctionAsync("""() => !!document.querySelector("[data-task-key='make_archive']")""");

        // Inside the block, and one edit rather than an add followed by a move: the author's undo
        // goes back to the script as it was before the drop, not to a half-done state.
        var nested = Script();
        Assert.Matches(@"PARALLEL BEGIN\s*\r?\n\s*make_archive:", nested);
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.undo()");
        await page.WaitForFunctionAsync(
            """() => !document.querySelector("[data-task-key='make_archive']")""");
        Assert.Equal(beforeNesting.Trim(), Script().Trim());

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    /// <summary>
    /// Drags a palette chip onto a target, the way a pointer does.
    ///
    /// <para>Playwright's own drag helpers move a mouse; the palette is an HTML5 drag source, so the
    /// gesture has to be the drag events themselves — with the same `DataTransfer` throughout, since
    /// what the chip puts on it during `dragstart` is the only thing the drop has to go on.</para>
    /// </summary>
    private static Task DragChipAsync(IPage page, string kind, string targetSelector) =>
        page.EvaluateAsync($$"""
            () => {
              const chip = document.querySelector("[data-task-kind='{{kind}}']");
              const target = document.querySelector("{{targetSelector}}");
              if (!chip) throw new Error("No palette chip for {{kind}}.");
              if (!target) throw new Error("No drop target {{targetSelector}}.");
              const dt = new DataTransfer();
              chip.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
              target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt }));
              target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
              chip.dispatchEvent(new DragEvent('dragend', { bubbles: true, dataTransfer: dt }));
            }
            """);

    [Fact]
    public async Task Studio_PipelineDependencies_AreDeclaredInTheScriptAndNeverImplyConcurrency()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");

        string Script() => page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content").Result;

        // Two more tasks to depend on each other.
        foreach (var (id, condition) in new[] { ("fetch_rates", "1 = 1"), ("merge_all", "2 = 2") })
        {
            await page.ClickAsync("[data-task-kind='validation']");
            await page.Locator("[data-task-field='condition']").WaitForAsync();
            await page.Locator("[data-task-id]").FillAsync(id);
            await page.Locator("[data-task-field='condition']").FillAsync(condition);
            await page.Locator("[data-task-field='message']").FillAsync($"{id} failed.");
            await page.ClickAsync("[data-dialog-action='save']");
            await page.WaitForFunctionAsync(
                $$"""() => !!document.querySelector("[data-task-key='{{id}}']")""");
        }

        // Every editable card carries a connector handle; dragging it declares a dependency.
        Assert.Equal(3, await page.Locator("[data-task-connector]").CountAsync());

        await page.EvaluateAsync("""
            () => {
              const drag = (fromKey, toKey) => {
                const handle = document.querySelector(`[data-task-connector='${fromKey}']`);
                const target = document.querySelector(`[data-task-key='${toKey}']`);
                const dt = new DataTransfer();
                handle.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
                target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
              };
              drag('load_orders', 'merge_all');
            }
            """);
        await page.WaitForFunctionAsync(
            """() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content.includes('@after: load_orders')""");

        await page.EvaluateAsync("""
            () => {
              const handle = document.querySelector("[data-task-connector='fetch_rates']");
              const target = document.querySelector("[data-task-key='merge_all']");
              const dt = new DataTransfer();
              handle.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
              target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
            }
            """);
        await page.WaitForFunctionAsync(
            """() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content.includes('@after: load_orders, fetch_rates')""");

        var joined = Script();

        // The join is one declaration line, and it says nothing about running anything concurrently:
        // concurrency in ETL-SQL is only ever a PARALLEL block, and the canvas wrote none.
        Assert.Equal(1, joined.Split("-- @after:", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("PARALLEL", joined, StringComparison.OrdinalIgnoreCase);

        // Both dependencies run before the task that waits for them.
        var mergeAt = joined.IndexOf("merge_all:", StringComparison.Ordinal);
        Assert.True(joined.IndexOf("load_orders:", StringComparison.Ordinal) < mergeAt);
        Assert.True(joined.IndexOf("fetch_rates:", StringComparison.Ordinal) < mergeAt);

        // The inspector lists both prerequisites, and one can be removed without touching the other.
        await page.Locator("[data-task-key='merge_all']").ClickAsync();
        await page.Locator("[data-task-disconnect]").First.WaitForAsync();
        Assert.Equal(2, await page.Locator("[data-task-disconnect]").CountAsync());
        Assert.Contains("Waits for all 2", await page.Locator("[data-task-inspector]").InnerTextAsync(), StringComparison.Ordinal);

        await page.ClickAsync("[data-task-disconnect='load_orders']");
        await page.WaitForFunctionAsync(
            """() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content.includes('@after: fetch_rates')""");

        var trimmed = Script();
        Assert.DoesNotContain("@after: load_orders", trimmed, StringComparison.Ordinal);
        Assert.Contains("load_orders:", trimmed, StringComparison.Ordinal);

        // A cycle is refused with a visible reason. A linear script can never execute one, so a
        // canvas that drew it would be claiming something the engine cannot do.
        await page.EvaluateAsync("""
            () => {
              const handle = document.querySelector("[data-task-connector='merge_all']");
              const target = document.querySelector("[data-task-key='fetch_rates']");
              const dt = new DataTransfer();
              handle.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
              target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
            }
            """);
        await page.Locator(".etlsql-feedback-toast", new() { HasText = "cycle" }).First.WaitForAsync();
        Assert.Equal(trimmed, Script());

        // An edge carries a condition, and choosing one rewrites the declaration in place rather
        // than adding a second prerequisite naming the same task.
        await page.SelectOptionAsync("[data-task-edge='fetch_rates']", "onfailure");
        await page.WaitForFunctionAsync(
            """() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content.includes('@after: fetch_rates on failure')""");

        var conditional = Script();
        Assert.Equal(1, conditional.Split("-- @after:", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("PARALLEL", conditional, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    /// <summary>
    /// Control-flow containers: a task dropped into one goes inside it, and the block the canvas
    /// writes is the only thing in the script that means concurrency.
    /// </summary>
    [Fact]
    public async Task Studio_PipelineContainers_HoldTasksAndOnlyParallelMeansConcurrency()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");

        string Script() => page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content").Result;

        var before = Script();
        Assert.DoesNotContain("PARALLEL", before, StringComparison.OrdinalIgnoreCase);

        // A container is added empty and named, then filled by dragging into it.
        await page.ClickAsync("[data-task-kind='parallel']");
        await page.Locator("[data-task-id]").WaitForAsync();
        await page.Locator("[data-task-id]").FillAsync("load_all");
        await page.ClickAsync("[data-dialog-action='save']");
        await page.WaitForFunctionAsync("""() => !!document.querySelector("[data-task-key='load_all']")""");

        Assert.Contains("PARALLEL BEGIN", Script(), StringComparison.Ordinal);
        Assert.True(await page.Locator("[data-task-key='load_all']")
            .EvaluateAsync<bool>("element => element.classList.contains('is-container-task')"));

        // Dropping a task onto the container puts it inside — the gesture matches the picture.
        await page.EvaluateAsync("""
            () => {
              const source = document.querySelector("[data-task-key='load_orders']");
              const target = document.querySelector("[data-task-key='load_all']");
              const dt = new DataTransfer();
              source.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
              target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
            }
            """);
        await page.WaitForFunctionAsync(
            """() => /PARALLEL BEGIN[\s\S]*load_orders:/.test(window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content)""");

        var nested = Script();

        // The canvas wrote exactly one PARALLEL block, and only because the author asked for one.
        Assert.Equal(1, nested.Split("PARALLEL", StringSplitOptions.None).Length - 1);

        // Selected through the DOM: a nested card can sit under the map's own toolbar, and this test
        // is about what the canvas writes, not about where the layout happens to put a box.
        await page.EvaluateAsync(
            """() => document.querySelector("[data-task-key='load_all']").dispatchEvent(new MouseEvent('click', { bubbles: true }))""");
        await page.Locator("[data-task-inspector]").WaitForAsync();
        var inspector = await page.Locator("[data-task-inspector]").InnerTextAsync();
        Assert.Contains("starts at the same time", inspector, StringComparison.Ordinal);

        // And a nested task offers its way back out.
        await page.EvaluateAsync(
            """() => document.querySelector("[data-task-key='load_orders']").dispatchEvent(new MouseEvent('click', { bubbles: true }))""");
        await page.Locator("[data-task-unnest]").WaitForAsync();
        await page.ClickAsync("[data-task-unnest]");
        await page.WaitForFunctionAsync("""() => !document.querySelector('[data-task-unnest]')""");

        // Out of the block, and still in the script: moving a task out is a relocation, never a delete.
        var freed = Script();
        Assert.Contains("load_orders:", freed, StringComparison.Ordinal);
        Assert.Matches(@"PARALLEL BEGIN\s*END;", freed);

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_TabName_DoubleClickRenamesAndEscapeCancels()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        var activeTitle = page.Locator(".etlsql-studio-tab.active .etlsql-tab-title");
        await activeTitle.DispatchEventAsync("dblclick");
        var input = page.Locator(".etlsql-tab-rename-input");
        await input.FillAsync("discarded-name");
        await input.PressAsync("Escape");
        Assert.Contains("sales_overview.rptsql", await page.Locator(".etlsql-studio-tab.active").InnerTextAsync());

        await page.Locator(".etlsql-studio-tab.active .etlsql-tab-title").DispatchEventAsync("dblclick");
        input = page.Locator(".etlsql-tab-rename-input");
        await input.FillAsync("quarterly_sales");
        await input.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.documents[0].name === 'quarterly_sales.rptsql'");
        Assert.Contains("quarterly_sales.rptsql", await page.Locator(".etlsql-studio-tab.active").InnerTextAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Explorer_ManagesFoldersAndMovesFiles()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.ClickAsync("button.etlsql-studio-rail-btn[data-activity='explorer']");

        await page.Locator("[data-explorer-new-folder='']").ClickAsync();
        await page.Locator(".etlsql-feedback-field input").FillAsync("archive-new");
        await page.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        var archive = page.Locator("[data-explorer-folder='archive-new']");
        await archive.WaitForAsync();

        var reportsToggle = page.Locator("[data-explorer-toggle='reports']");
        if (await reportsToggle.IsVisibleAsync() && !await page.Locator("[data-explorer-file='reports/sales_overview.rptsql']").IsVisibleAsync())
        {
            await reportsToggle.ClickAsync();
        }

        await page.Locator("[data-explorer-file='reports/sales_overview.rptsql']").DragToAsync(archive);
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.workspaceFiles.some(file => file.path === 'archive-new/sales_overview.rptsql')");
        await page.Locator("[data-explorer-file='archive-new/sales_overview.rptsql']")
            .DragToAsync(page.Locator("[data-explorer-root-drop]"));
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.workspaceFiles.some(file => file.path === 'sales_overview.rptsql')");

        var etlToggle = page.Locator("[data-explorer-toggle='etl']");
        if (await etlToggle.IsVisibleAsync() && !await page.Locator("[data-explorer-file='etl/ingest_orders.etlsql']").IsVisibleAsync())
        {
            await etlToggle.ClickAsync();
        }

        var etlFile = page.Locator("[data-explorer-file='etl/ingest_orders.etlsql']");
        await etlFile.HoverAsync();
        await etlFile.Locator("[data-explorer-rename]").ClickAsync();
        var prompt = page.Locator(".etlsql-feedback-field input");
        await prompt.FillAsync("import_orders");
        await page.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.workspaceFiles.some(file => file.path === 'etl/import_orders.etlsql')");

        etlFile = page.Locator("[data-explorer-file='etl/import_orders.etlsql']");
        await etlFile.HoverAsync();
        await etlFile.Locator("[data-explorer-delete]").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await page.WaitForFunctionAsync("() => !window.__STUDIO_INSTANCE__.state.workspaceFiles.some(file => file.path === 'etl/import_orders.etlsql')");

        archive = page.Locator("[data-explorer-folder='archive-new']");
        await archive.HoverAsync();
        await archive.Locator("[data-explorer-rename]").ClickAsync();
        await page.Locator(".etlsql-feedback-field input").FillAsync("archive-final");
        await page.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        var renamedArchive = page.Locator("[data-explorer-folder='archive-final']");
        await renamedArchive.WaitForAsync();
        await renamedArchive.HoverAsync();
        await renamedArchive.Locator("[data-explorer-delete]").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await page.WaitForFunctionAsync("() => !window.__STUDIO_INSTANCE__.state.workspaceFolders.some(folder => folder.path === 'archive-final')");

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Mounts_SwitchesProjections_AndScansSecrets()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");

        // Wait for studio shell to mount
        var studioShell = page.Locator(".etlsql-studio-shell");
        await studioShell.WaitForAsync();
        await page.Locator(".etlsql-studio-tab").First.WaitForAsync();

        // 1. Verify tabs rendered (Home + 3 documents)
        Assert.Equal(4, await page.Locator(".etlsql-studio-tab").CountAsync());
        Assert.Contains("sales_overview.rptsql", await page.Locator(".etlsql-studio-tab.active").InnerTextAsync());
        var overflowBtn = page.Locator("[data-studio-overflow-btn]");
        Assert.True(await overflowBtn.IsVisibleAsync());

        // 2. Test projection switching
        await page.Locator("button[data-projection='canvas']").ClickAsync();
        Assert.True(await page.Locator("[data-visual-stage]").IsVisibleAsync());
        Assert.False(await page.Locator("[data-code-stage]").IsVisibleAsync());

        await page.Locator("button[data-projection='code']").ClickAsync();
        Assert.False(await page.Locator("[data-visual-stage]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-code-stage]").IsVisibleAsync());

        await page.Locator("button[data-projection='split']").ClickAsync();
        Assert.True(await page.Locator("[data-visual-stage]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-code-stage]").IsVisibleAsync());

        // 3. Test Activity Rail switching and Filter Pane
        await page.Locator("button.etlsql-studio-rail-btn[data-activity='catalog']").ClickAsync();
        Assert.Contains("New connection", await page.Locator("[data-sidebar-content]").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connections", await page.Locator("[data-sidebar-content]").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await page.Locator("button.etlsql-studio-rail-btn[data-activity='filters']").ClickAsync();
        Assert.Equal("Data", await page.Locator("[data-sidebar-title]").InnerTextAsync(), ignoreCase: true);
        Assert.True(await page.Locator("[data-studio-sidebar]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-filter-sidebar]").IsVisibleAsync());
        await page.Locator("[data-studio-sidebar] [data-field='region']").ClickAsync();
        var filterDialog = page.Locator(".etlsql-studio-filter-dialog");
        await filterDialog.WaitForAsync();
        Assert.Equal("region", await filterDialog.Locator("[data-filter-dialog-field]").InputValueAsync());
        await filterDialog.Locator("[data-filter-dialog-field]").PressAsync("Escape");
        Assert.True(await page.Locator("[data-modal-backdrop]").IsHiddenAsync());

        // 4. Assert Live Visual Canvas cards
        var kpiCard = page.Locator(".etlsql-studio-canvas-card[data-visual-id='v_RevenueCard_0']");
        await kpiCard.WaitForAsync();
        Assert.Contains("Total Revenue", await kpiCard.InnerTextAsync());

        var barCard = page.Locator(".etlsql-studio-canvas-card[data-visual-id='v_SalesByRegion_1']");
        await barCard.WaitForAsync();
        Assert.True(await barCard.IsVisibleAsync());

        // 5. Test Visual Card Click-to-Code and Surgical AST Patching
        await kpiCard.ClickAsync();
        Assert.True(await page.Locator(".etlsql-studio-canvas-card[data-visual-id='v_RevenueCard_0']").EvaluateAsync<bool>("el => el.classList.contains('selected')"));

        // Trigger surgical option update on RevenueCard
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__?.surgicalPatchVisualOption('RevenueCard', 'TITLE', 'Executive Net Revenue')");
        Assert.Contains("Executive Net Revenue", await kpiCard.InnerTextAsync());

        // 7. Switch to ETL tab via overflow dropdown and assert Pipeline DAG rendering
        await overflowBtn.ClickAsync();
        var tabDropdown = page.Locator("[data-studio-tab-dropdown]");
        Assert.False(await tabDropdown.IsHiddenAsync());
        Assert.Contains("ingest_orders.etlsql", await tabDropdown.InnerTextAsync());
        await tabDropdown.Locator(".etlsql-studio-tab-dropdown-item", new() { HasText = "ingest_orders.etlsql" }).ClickAsync();
        Assert.True(await tabDropdown.IsHiddenAsync());
        Assert.Contains("ingest_orders.etlsql", await page.Locator(".etlsql-studio-tab.active").InnerTextAsync());
        var dagView = page.Locator("[data-dag-view]");
        await dagView.WaitForAsync();
        Assert.True(await page.Locator("[data-dag-node]").CountAsync() >= 2);
        Assert.Contains("staging_db", await page.Locator("[data-dag-node='staging_db']").InnerTextAsync());
        Assert.Contains("#raw_sales", await page.Locator("[data-dag-node='#raw_sales']").InnerTextAsync());

        // 8. Switch to secret-containing tab and test save secret detection modal
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-secret-test')");
        await page.Locator("button[data-action='save']").ClickAsync();

        // Modal should appear with secret warning
        var modalBackdrop = page.Locator("[data-modal-backdrop]");
        await modalBackdrop.WaitForAsync();
        Assert.False(await modalBackdrop.IsHiddenAsync());
        Assert.Contains("Plaintext Secret Detected", await page.Locator("[data-modal-box]").InnerTextAsync());
        Assert.Contains("(value hidden)", await page.Locator("[data-modal-box]").InnerTextAsync());
        Assert.DoesNotContain("SuperSecretPassword123!", await page.Locator("[data-modal-box]").InnerTextAsync());
        Assert.False(await page.Locator("[data-modal-box]").EvaluateAsync<bool>(
            "element => element.innerHTML.includes('SuperSecretPassword123!')"));

        // Cancel modal
        await page.Locator("[data-modal-box] button[data-modal-close]").First.ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.True(await modalBackdrop.IsHiddenAsync());

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_VisualFormattingInspector_PatchesReportSqlAndKeepsAuthoredValues()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        var chart = page.Locator(".etlsql-studio-canvas-card[data-visual-id='v_SalesByRegion_1']");
        await chart.WaitForAsync();
        await chart.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var inspector = page.Locator(".etlsql-format-inspector");
        await inspector.WaitForAsync();
        Assert.Contains("BAR · Auto", await inspector.InnerTextAsync());
        Assert.Equal("Segoe UI", await page.Locator("#pp-title-font").InputValueAsync());
        Assert.False(await page.Locator("#pp-format-legend").IsCheckedAsync());
        Assert.Equal("Revenue", await page.Locator("[data-axis='y'][data-axis-key='LABEL']").InputValueAsync());
        Assert.Equal("$#,##0", await page.Locator("[data-axis='y'][data-axis-key='FORMAT']").InputValueAsync());
        Assert.True(await page.Locator("[data-axis='y'][data-axis-key='INCLUDE_ZERO']").IsCheckedAsync());
        Assert.Equal("6", await page.Locator("[data-axis='y'][data-axis-key='MAJOR_TICK_COUNT']").InputValueAsync());
        Assert.True(await page.Locator("[data-axis='y'][data-axis-key='MINOR_TICKS']").IsCheckedAsync());
        Assert.Equal(3, await page.Locator("[data-palette-text]").CountAsync());
        Assert.DoesNotContain("Optional", await inspector.InnerTextAsync(), StringComparison.Ordinal);

        await page.Locator("#pp-format-subtitle").FillAsync("FY26 booked revenue");
        await page.Locator("#pp-format-subtitle").DispatchEventAsync("change");
        await page.WaitForFunctionAsync("() => document.querySelector('.cm-content')?.textContent.includes(\"SUBTITLE = 'FY26 booked revenue'\")");
        await page.WaitForTimeoutAsync(300);
        await inspector.Locator("summary", new() { HasText = "Axes & legend" }).ClickAsync();
        await page.Locator("#pp-format-legend").CheckAsync();
        await page.Locator("#pp-format-legend-position").SelectOptionAsync("RIGHT");
        await page.Locator("#pp-format-grid-lines").UncheckAsync();
        await page.Locator("#pp-format-grid-color").FillAsync("#123456");
        await page.Locator("#pp-format-grid-color").DispatchEventAsync("input");
        await page.Locator("#pp-format-grid-dash").SelectOptionAsync("DASHED");
        await page.Locator("#pp-format-grid-width").FillAsync("2");
        await page.Locator("#pp-format-grid-width").DispatchEventAsync("change");
        await page.Locator("#pp-format-minor-grid-lines").CheckAsync();
        await page.Locator("#pp-format-zero-line").CheckAsync();
        await page.Locator("#pp-format-zero-line-color").FillAsync("#654321");
        await page.Locator("#pp-format-zero-line-color").DispatchEventAsync("input");
        await page.Locator("#pp-format-zero-line-dash").SelectOptionAsync("DOTTED");
        await page.Locator("#pp-format-zero-line-width").FillAsync("2.5");
        await page.Locator("#pp-format-zero-line-width").DispatchEventAsync("change");
        await page.Locator("#pp-format-zoom-slider").CheckAsync();
        await page.Locator("[data-axis='y'][data-axis-key='MAX']").FillAsync("500000");
        await page.Locator("[data-axis='y'][data-axis-key='MAX']").DispatchEventAsync("change");
        await page.Locator("[data-axis='y'][data-axis-key='REVERSE']").CheckAsync();
        await page.Locator("[data-axis='x'][data-axis-key='LABEL_ROTATION']").SelectOptionAsync("45");
        await page.Locator("[data-axis='x'][data-axis-key='LABEL_SKIP']").FillAsync("1");
        await page.Locator("[data-axis='x'][data-axis-key='LABEL_SKIP']").DispatchEventAsync("change");
        await page.Locator("[data-axis='x'][data-axis-key='AXIS_LINE']").UncheckAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.cm-content')?.textContent.includes('LEGEND = ON')");
        await page.WaitForFunctionAsync("() => document.querySelector('.cm-content')?.textContent.includes('MAX = 500000')");
        await page.WaitForTimeoutAsync(300);
        await inspector.Locator("summary", new() { HasText = "Series palette" }).ClickAsync();
        await page.Locator("[data-palette-add]").ClickAsync();
        // The group stays open although its count changed. It used to close here, because an open
        // group was remembered by its whole heading, count included, and this step clicked it again.
        await page.Locator("[data-named-color-add]").ClickAsync();
        await inspector.Locator("summary", new() { HasText = "Marks & labels" }).ClickAsync();
        await page.Locator("#pp-format-data-labels").CheckAsync();
        await page.Locator("#pp-format-data-label-position").SelectOptionAsync("INSIDE MIDDLE");
        await page.Locator("#pp-format-stacked").SelectOptionAsync("100PCT");
        await page.Locator("#pp-format-band-size").FillAsync("0.5");
        await page.Locator("#pp-format-band-size").DispatchEventAsync("input");
        await page.Locator("#pp-format-series-gap").FillAsync("0.25");
        await page.Locator("#pp-format-series-gap").DispatchEventAsync("input");
        await page.Locator("#pp-format-outer-padding").FillAsync("0.4");
        await page.Locator("#pp-format-outer-padding").DispatchEventAsync("input");
        // Overlays used to be a textarea, and this step typed a clause that did not parse
        // (LABEL outside WITH). Nothing checked it. They are chosen from controls now.
        await inspector.Locator("summary", new() { HasText = "Analytics" }).ClickAsync();
        // Picked in the same turn as the click that opened the group: the rebuild this triggers
        // must keep the group open although the browser has not dispatched `toggle` yet.
        await page.Locator("#pp-overlay-add").SelectOptionAsync("GOAL");
        await page.Locator("[data-overlay-row='0'] [data-overlay-field='value']").FillAsync("100000");
        await page.Locator("[data-overlay-row='0'] [data-overlay-field='value']").DispatchEventAsync("change");
        await page.Locator("[data-overlay-row='0'] [data-overlay-field='label']").FillAsync("Target");
        await page.Locator("[data-overlay-row='0'] [data-overlay-field='label']").DispatchEventAsync("change");
        await inspector.Locator("summary", new() { HasText = "Conditional formatting" }).ClickAsync();
        await page.Locator("[data-rule-add]").ClickAsync();

        await page.WaitForTimeoutAsync(1500);

        var activeScript = await page.EvaluateAsync<string>(
            """
            () => window.__STUDIO_INSTANCE__.state.documents.find(
                item => item.id === window.__STUDIO_INSTANCE__.state.activeDocId).content
            """);
        Assert.Contains("TITLE (TEXT = 'Sales by Region'", activeScript, StringComparison.Ordinal);
        Assert.Contains("SUBTITLE = 'FY26 booked revenue'", activeScript, StringComparison.Ordinal);
        Assert.Contains("LEGEND = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("LEGEND_POSITION = 'RIGHT'", activeScript, StringComparison.Ordinal);
        Assert.Contains("GRID_LINES = OFF", activeScript, StringComparison.Ordinal);
        Assert.Contains("GRID_LINE_COLOR = '#123456'", activeScript, StringComparison.Ordinal);
        Assert.Contains("GRID_LINE_DASH = 'DASHED'", activeScript, StringComparison.Ordinal);
        Assert.Contains("GRID_LINE_WIDTH = 2", activeScript, StringComparison.Ordinal);
        Assert.Contains("MINOR_GRID_LINES = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZERO_LINE = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZERO_LINE_COLOR = '#654321'", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZERO_LINE_DASH = 'DOTTED'", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZERO_LINE_WIDTH = 2.5", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZOOM_SLIDER = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("DATA_LABELS = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("DATA_LABELS:POSITION = 'INSIDE_MIDDLE'", activeScript, StringComparison.Ordinal);
        Assert.Contains("BAND_SIZE = 0.5", activeScript, StringComparison.Ordinal);
        Assert.Contains("STACKED = '100PCT'", activeScript, StringComparison.Ordinal);
        Assert.Contains("SERIES_GAP = 0.25", activeScript, StringComparison.Ordinal);
        Assert.Contains("OUTER_PADDING = 0.4", activeScript, StringComparison.Ordinal);
        Assert.Contains("OVERLAYS (GOAL(100000) AS DASHED WITH (LABEL = 'Target'))", activeScript, StringComparison.Ordinal);
        Assert.Contains("X_AXIS (LABEL = 'Region'", activeScript, StringComparison.Ordinal);
        Assert.Contains("LABEL_ROTATION = 45", activeScript, StringComparison.Ordinal);
        Assert.Contains("LABEL_SKIP = 1", activeScript, StringComparison.Ordinal);
        Assert.Contains("AXIS_LINE = OFF", activeScript, StringComparison.Ordinal);
        Assert.Contains("Y_AXIS (LABEL = 'Revenue'", activeScript, StringComparison.Ordinal);
        Assert.Contains("INCLUDE_ZERO = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("MAJOR_TICK_COUNT = 6", activeScript, StringComparison.Ordinal);
        Assert.Contains("MINOR_TICKS = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("REVERSE = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("MAX = 500000", activeScript, StringComparison.Ordinal);
        Assert.Contains("COLOR:Series1 = '#2563eb'", activeScript, StringComparison.Ordinal);
        Assert.Contains("STYLE (PALETTE = ('#58a6ff', '#2ea043', '#d29922', '#dc2626'))", activeScript, StringComparison.Ordinal);
        Assert.Contains("FORMATTING (WHEN", activeScript, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_SourceControl_ShowsLocalHistoryAndSideBySideDiff()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.Locator("button[data-activity='git']").ClickAsync();

        var headComparison = page.Locator("[data-git-revision='HEAD']");
        await headComparison.WaitForAsync();
        Assert.Contains("Includes unsaved editor changes", await headComparison.InnerTextAsync());
        Assert.Equal(2, await page.Locator(".etlsql-studio-git-history [data-git-revision]").CountAsync());

        await headComparison.ClickAsync();
        var diff = page.Locator(".etlsql-studio-git-diff-modal");
        await diff.WaitForAsync();
        Assert.Contains("HEAD a12bc34d", await diff.InnerTextAsync());
        Assert.Contains("Working tree", await diff.InnerTextAsync());
        Assert.Equal(1, await diff.Locator(".etlsql-studio-git-diff-row.is-change").CountAsync());
        Assert.Contains(await diff.Locator(".etlsql-studio-git-diff-cell.is-left").AllInnerTextsAsync(),
            text => text.Contains("TITLE = 'Revenue'", StringComparison.Ordinal));
        Assert.Contains(await diff.Locator(".etlsql-studio-git-diff-cell.is-right").AllInnerTextsAsync(),
            text => text.Contains("TITLE = 'Total Revenue'", StringComparison.Ordinal));

        await diff.Locator("[data-git-diff-close]").ClickAsync();
        Assert.True(await diff.IsHiddenAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_FilterPane_RendersCategoricalNumericAndDateControls()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.Locator("button.etlsql-studio-rail-btn[data-activity='catalog']").ClickAsync();
        await page.Locator("button.etlsql-studio-rail-btn[data-activity='filters']").ClickAsync();

        var dataSidebar = page.Locator("[data-studio-sidebar]");
        var filterSidebar = page.Locator("[data-filter-sidebar]");
        Assert.True(await dataSidebar.IsVisibleAsync());
        Assert.True(await filterSidebar.IsVisibleAsync());
        Assert.DoesNotContain("Active filters", await dataSidebar.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Connections", await filterSidebar.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await dataSidebar.Locator("[data-field='region']").DragToAsync(filterSidebar.Locator("[data-filter-drop]"));
        var setup = page.Locator(".etlsql-studio-filter-dialog");
        await setup.WaitForAsync();
        Assert.Equal("region", await setup.Locator("[data-filter-dialog-field]").InputValueAsync());
        await setup.Locator("[data-filter-dialog-apply]").ClickAsync();

        await filterSidebar.Locator("[data-new-filter]").ClickAsync();
        await setup.Locator("[data-filter-dialog-field]").SelectOptionAsync("total_amount");
        await setup.Locator("[data-filter-dialog-apply]").ClickAsync();

        await filterSidebar.Locator("[data-new-filter]").ClickAsync();
        await setup.Locator("[data-filter-dialog-field]").SelectOptionAsync("order_date");
        await setup.Locator("[data-filter-dialog-apply]").ClickAsync();

        Assert.Equal(3, await filterSidebar.Locator(".etlsql-filter-card").CountAsync());
        Assert.True(await filterSidebar.Locator("[data-date-preset='order_date']").IsVisibleAsync());
        Assert.True(await filterSidebar.Locator("[data-filter-date-min='order_date']").IsVisibleAsync());
        Assert.True(await filterSidebar.Locator("[data-filter-date-max='order_date']").IsVisibleAsync());
        Assert.True(await filterSidebar.Locator("[data-filter-min='total_amount']").IsVisibleAsync());
        Assert.True(await filterSidebar.Locator("[data-filter-max='total_amount']").IsVisibleAsync());
        Assert.Equal(4, await filterSidebar.Locator("[data-filter-value='region']").CountAsync());
        Assert.Equal(3, await filterSidebar.Locator("[data-filter-scope]").CountAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_NewScript_CreatesAnEtlSqlDocument()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.Locator("[data-studio-new-tab]").ClickAsync();

        var newScript = page.Locator("[data-new-type='sql']");
        Assert.Contains("New Script (.etlsql)", await newScript.InnerTextAsync());
        await newScript.ClickAsync();

        var activeTab = page.Locator(".etlsql-studio-tab.active");
        Assert.Contains("untitled_query_", await activeTab.InnerTextAsync());
        Assert.Contains(".etlsql", await activeTab.InnerTextAsync());
        Assert.DoesNotContain(".sql", await activeTab.InnerTextAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Home_CreatesDistinctDashboardAndPaginatedReportWorkflows()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('__home__')");

        var dashboardAction = page.Locator("[data-create-from-home='dashboard']:not([data-seed-sample])");
        var paginatedAction = page.Locator("[data-create-from-home='paginated']");
        Assert.Contains("New Dashboard", await dashboardAction.InnerTextAsync());
        Assert.Contains("New Paginated Report", await paginatedAction.InnerTextAsync());

        await paginatedAction.ClickAsync();
        await page.Locator(".etlsql-studio-visual-stage.is-paginated-workflow").WaitForAsync();
        Assert.Contains("Physical page authoring", await page.Locator("[data-workflow-bar]").InnerTextAsync());
        Assert.Equal(8, await page.Locator(".etlsql-paginated-steps > li").CountAsync());
        var paginated = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const doc = window.__STUDIO_INSTANCE__.state.documents.find(item => item.id === window.__STUDIO_INSTANCE__.state.activeDocId);
                return { name: doc.name, content: doc.content, workflow: doc.reportWorkflow };
            }
            """);
        Assert.EndsWith(".rptsql", paginated.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal("paginated", paginated.GetProperty("workflow").GetString());
        Assert.Contains("AS PAGINATED", paginated.GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Contains("PRINT_LAYOUT", paginated.GetProperty("content").GetString(), StringComparison.Ordinal);

        await page.SelectOptionAsync("[data-page-setup='orientation']", "LANDSCAPE");
        await page.WaitForFunctionAsync(
            """
            () => window.__STUDIO_API_REQUESTS__.some(request =>
                request.url.endsWith('/api/designer/patch') &&
                request.body?.designState?.pages?.[0]?.printLayout?.orientation === 'LANDSCAPE')
            """);

        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('__home__')");
        await dashboardAction.ClickAsync();
        await page.Locator(".etlsql-studio-visual-stage.is-dashboard-workflow").WaitForAsync();
        Assert.Contains("Responsive visual canvas", await page.Locator("[data-workflow-bar]").InnerTextAsync());
        var dashboard = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const doc = window.__STUDIO_INSTANCE__.state.documents.find(item => item.id === window.__STUDIO_INSTANCE__.state.activeDocId);
                return { name: doc.name, content: doc.content, workflow: doc.reportWorkflow };
            }
            """);
        Assert.EndsWith(".rptsql", dashboard.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal("dashboard", dashboard.GetProperty("workflow").GetString());
        Assert.Contains("AS DASHBOARD", dashboard.GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_ExistingReportWorkflowInference_PreservesAmbiguousAndInvalidScriptBytes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        Assert.Equal("dashboard", await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(doc => doc.id === 'doc-report').reportWorkflow"));
        var validCanvasCount = await page.Locator(".designer-card").CountAsync();
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.setValue('>>> INVALID <<<')");
        await page.WaitForTimeoutAsync(550);
        Assert.Equal("dashboard", await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(doc => doc.id === 'doc-report').reportWorkflow"));
        Assert.Equal(validCanvasCount, await page.Locator(".designer-card").CountAsync());
        Assert.Equal(">>> INVALID <<<", await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()"));

        const string ambiguous = "-- hand-authored report\nCREATE VISUAL note AS TEXT (TITLE = 'Keep me', SOURCE = #rows);";
        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO_INSTANCE__;
                studio.state.documents.push({ id: 'ambiguous-report', path: 'ambiguous.rptsql', name: 'ambiguous.rptsql', content: script, isDirty: false, projection: 'split' });
                void studio.switchDoc('ambiguous-report');
            }
            """, ambiguous);
        await page.Locator("[data-choose-workflow='dashboard']").WaitForAsync();
        Assert.Contains("the script is not edited either way", await page.Locator(".etlsql-workflow-choice").InnerTextAsync());
        await page.Locator("[data-choose-workflow='paginated']").ClickAsync();
        await page.Locator(".etlsql-studio-visual-stage.is-paginated-workflow").WaitForAsync();

        var selected = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const doc = window.__STUDIO_INSTANCE__.state.documents.find(item => item.id === 'ambiguous-report');
                return { content: doc.content, workflow: doc.reportWorkflow, dirty: doc.isDirty };
            }
            """);
        Assert.Equal(ambiguous, selected.GetProperty("content").GetString());
        Assert.Equal("paginated", selected.GetProperty("workflow").GetString());
        Assert.False(selected.GetProperty("dirty").GetBoolean());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Home_CanDismissAWorkspaceFileWithoutClosingItsDocument()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('__home__')");

        var cards = page.Locator(".etlsql-studio-recent-card");
        await cards.First.WaitForAsync();
        Assert.Equal(3, await cards.CountAsync());
        await page.Locator("[data-dismiss-file='reports/sales_overview.rptsql']").ClickAsync();

        Assert.Equal(2, await cards.CountAsync());
        Assert.Equal(0, await page.Locator("[data-dismiss-file='reports/sales_overview.rptsql']").CountAsync());
        Assert.Equal(1, await page.Locator(".etlsql-studio-tab", new() { HasText = "sales_overview.rptsql" }).CountAsync());
        Assert.Contains("file was not deleted", await page.Locator("body").InnerTextAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Header_OmitsConnectionAndFormatWhileContextualActionsRemain()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        var header = page.Locator(".etlsql-studio-header");
        Assert.Equal(0, await header.Locator("[data-action='wizard']").CountAsync());
        Assert.Equal(0, await header.Locator("[data-action='format']").CountAsync());
        Assert.True(await page.Locator("[data-action='code-format']").IsVisibleAsync());

        await page.Locator("[data-activity='catalog']").ClickAsync();
        Assert.True(await page.Locator(".etlsql-studio-sidebar [data-action='wizard']").IsVisibleAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_DecorativeWorkbenchControls_ReportUnavailableCapabilities()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync("() => window.__STUDIO_NO_GIT__ = true");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        await page.Locator("[data-activity='git']").ClickAsync();
        var gitState = page.Locator("[data-capability-state='git']");
        Assert.Contains("Source control is unavailable", await gitState.InnerTextAsync());
        Assert.DoesNotContain("Branch:", await gitState.InnerTextAsync());
        Assert.DoesNotContain("Working tree clean", await gitState.InnerTextAsync());

        await page.Locator("[data-activity='settings']").ClickAsync();
        var settingsState = page.Locator("[data-capability-state='settings']");
        Assert.Contains("Settings are unavailable", await settingsState.InnerTextAsync());
        Assert.Equal(0, await page.Locator("[data-sidebar-content] input").CountAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task ScriptEditor_MouseSelectsSingleLineRangesAndWholeEtlSqlTokens()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
                document.body.innerHTML = '<div id="selection-editor" style="width:700px;height:240px"></div>';
                const module = await import('/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js?selection-test');
                window.__selectionEditor = await module.createScriptEditor(
                    document.getElementById('selection-editor'),
                    { value: 'SELECT #temp, @declare, plain_word, commas;' });
            }
            """);
        await page.Locator(".cm-content").WaitForAsync();

        async Task<(float X, float Y)> CharacterPositionAsync(int offset)
        {
            var point = await page.EvaluateAsync<System.Text.Json.JsonElement>(
                """
                offset => {
                    const line = document.querySelector('.cm-line');
                    const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
                    let remaining = offset;
                    let node;
                    while ((node = walker.nextNode())) {
                        if (remaining <= node.textContent.length) {
                            const range = document.createRange();
                            range.setStart(node, remaining);
                            range.setEnd(node, remaining);
                            const rect = range.getBoundingClientRect();
                            const lineRect = line.getBoundingClientRect();
                            return { x: rect.left, y: lineRect.top + lineRect.height / 2 };
                        }
                        remaining -= node.textContent.length;
                    }
                    throw new Error(`Offset ${offset} is outside the editor line.`);
                }
                """, offset);
            return ((float)point.GetProperty("x").GetDouble(), (float)point.GetProperty("y").GetDouble());
        }

        var dragStart = await CharacterPositionAsync(7);
        var dragEnd = await CharacterPositionAsync(12);
        await page.Mouse.MoveAsync(dragStart.X, dragStart.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(dragEnd.X, dragEnd.Y, new() { Steps = 5 });
        await page.Mouse.UpAsync();
        Assert.Equal("#temp", await page.EvaluateAsync<string>("() => window.__selectionEditor.getSelection()"));

        var variable = await CharacterPositionAsync(16);
        await page.Mouse.DblClickAsync(variable.X, variable.Y);
        Assert.Equal("@declare", await page.EvaluateAsync<string>("() => window.__selectionEditor.getSelection()"));
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_AuditsResponsivenessAndLayoutShiftAcrossResolutions()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");

        var studioShell = page.Locator(".etlsql-studio-shell");
        await studioShell.WaitForAsync();

        // Audit viewport geometries from 1024x768 up to 4K UHD
        (int width, int height)[] viewports = [
            (1024, 768),
            (1366, 768),
            (1920, 1080),
            (2560, 1440),
            (3840, 2160)
        ];

        foreach (var (w, h) in viewports)
        {
            await page.SetViewportSizeAsync(w, h);
            await page.WaitForTimeoutAsync(50);

            // 1. Verify no unwanted horizontal scroll overflow on the shell
            var isOverflowing = await studioShell.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 2");
            Assert.False(isOverflowing, $"Studio shell unexpectedly overflowed horizontally at {w}x{h}");

            // 2. Verify Activity Rail buttons maintain minimum accessible hitboxes (>= 24px)
            var railButtons = page.Locator("button.etlsql-studio-rail-btn");
            var buttonCount = await railButtons.CountAsync();
            for (var i = 0; i < buttonCount; i++)
            {
                var box = await railButtons.Nth(i).BoundingBoxAsync();
                Assert.NotNull(box);
                Assert.True(box.Width >= 24, $"Rail button {i} width {box.Width} < 24px at {w}x{h}");
                Assert.True(box.Height >= 24, $"Rail button {i} height {box.Height} < 24px at {w}x{h}");
            }

            // 3. Verify Canvas visual cards are rendered and accessible
            var canvasCards = page.Locator(".etlsql-studio-canvas-card");
            Assert.True(await canvasCards.CountAsync() >= 2);
            for (var j = 0; j < await canvasCards.CountAsync(); j++)
            {
                var cardBox = await canvasCards.Nth(j).BoundingBoxAsync();
                Assert.NotNull(cardBox);
                Assert.True(cardBox.Width >= 200, $"Canvas card {j} width {cardBox.Width} is too narrow at {w}x{h}");
            }
        }

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task ConstrainedHtmlRuntime_SanitizesEmbedsActsAndPrintsAccessibly()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='constrained-html-runtime']");
        var frame = page.FrameLocator("iframe[title='Constrained HTML runtime fixture']");
        await frame.Locator(".html-visual-content").WaitForAsync();

        Assert.Equal("Cluster status: 3 healthy nodes",
            await frame.Locator(".html-visual-content").GetAttributeAsync("aria-label"));
        Assert.Equal(0, await frame.Locator(".html-visual-content script").CountAsync());
        Assert.Equal(0, await frame.Locator(".html-visual-content img").CountAsync());
        Assert.Contains("42", await frame.Locator("[data-etl-embed-id]").InnerTextAsync());
        Assert.Contains("#etl-v-statuspanel", await frame.Locator(".html-visual-scoped-style").TextContentAsync());
        Assert.Equal(2, await frame.Locator(".html-inline-microchart svg").CountAsync());
        Assert.Equal("Service trend", await frame.Locator("[data-etl-microchart-id='StatusPanel-micro-0']")
            .GetAttributeAsync("aria-label"));
        Assert.Equal(0, await frame.Locator("[data-etl-microchart-id='forged'] svg").CountAsync());
        Assert.Contains("Error", await frame.Locator("[data-etl-microchart-id='forged']").InnerTextAsync());

        await frame.GetByRole(AriaRole.Button, new() { Name = "Undeclared refresh" }).ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>(
            "() => window.__htmlVisualActionRequests.length"));

        await frame.GetByRole(AriaRole.Button, new() { Name = "Show West" }).ClickAsync();
        await page.WaitForTimeoutAsync(100);
        var requests = await frame.Locator("body").EvaluateAsync<int>(
            "() => window.__htmlVisualActionRequests.length");
        Assert.Equal(1, requests);

        await page.EmulateMediaAsync(new() { Media = Media.Print });
        Assert.True(await frame.Locator(".html-visual-content").IsVisibleAsync());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task InputControls_ClearSearchValidateNumbersAndRetainControlState()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/filter-controls.html");
        var search = page.GetByRole(AriaRole.Searchbox, new() { Name = "CustomerSearch", Exact = true });
        await search.WaitForAsync();
        Assert.Equal("Quarterly sales", await search.InputValueAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear Customer search", Exact = true }).ClickAsync();
        Assert.Equal("", await search.InputValueAsync());
        Assert.True(await search.EvaluateAsync<bool>("element => element === document.activeElement"));

        var note = page.GetByRole(AriaRole.Textbox, new() { Name = "AnalystNote", Exact = true });
        Assert.Equal("12", await note.GetAttributeAsync("maxlength"));
        var quantity = page.GetByRole(AriaRole.Spinbutton, new() { Name = "Quantity", Exact = true });
        await quantity.FillAsync("20");
        await quantity.DispatchEventAsync("change");
        Assert.Equal("8", await quantity.InputValueAsync());
        var enabled = page.GetByRole(AriaRole.Checkbox, new() { Name = "Enabled", Exact = true });
        Assert.True(await enabled.IsCheckedAsync());
        await enabled.UncheckAsync();
        Assert.False(await enabled.IsCheckedAsync());
        var slider = page.GetByRole(AriaRole.Slider, new() { Name = "Threshold", Exact = true });
        Assert.Equal("8", await slider.InputValueAsync());
        await slider.FillAsync("24");
        await slider.DispatchEventAsync("input");
        Assert.Equal("30", await page.Locator(".visual-card[data-name='Threshold'] .range-value").InnerTextAsync());
        Assert.Contains("interactive in ReportPlayer", await page.Locator(".visual-card[data-name='Region'] .slicer-note").InnerTextAsync());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DateControls_ValidateRangesAndRelativeExpressions()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/date-controls.html");
        var range = page.Locator(".visual-card[data-name='DateRange']");
        var start = range.Locator("input[type=text]").Nth(0);
        var end = range.Locator("input[type=text]").Nth(1);
        await start.FillAsync("2026-09-14");
        await start.DispatchEventAsync("change");
        Assert.Contains("Start date is disabled", await range.Locator(".filter-error").InnerTextAsync());
        await start.FillAsync("2026-09-20");
        await start.DispatchEventAsync("change");
        Assert.Contains("Start date cannot be after end date", await range.Locator(".filter-error").InnerTextAsync());
        await end.FillAsync("2026-09-21");
        await end.DispatchEventAsync("change");
        Assert.False(await range.Locator(".filter-error").IsVisibleAsync());

        var relative = page.Locator(".visual-card[data-name='RelativeDate']");
        var expression = relative.Locator("input[type=text]");
        await expression.FillAsync("invalid");
        await expression.DispatchEventAsync("change");
        Assert.True(await relative.Locator(".filter-error").IsVisibleAsync());
        await relative.GetByRole(AriaRole.Button, new() { Name = "D-1", Exact = true }).ClickAsync();
        Assert.Equal("D-1", await expression.InputValueAsync());
        Assert.False(await relative.Locator(".filter-error").IsVisibleAsync());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DesignTokenRuntime_CascadesSafelyAcrossCustomLayerAndMaximizedScopes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='design-tokens']");
        var frame = page.FrameLocator("iframe[title='Design tokens runtime fixture']");

        var inheritedCard = frame.Locator("#etl-v-inheritedcard");
        var inheritedPanel = inheritedCard.Locator(".token-panel");
        await inheritedPanel.WaitForAsync();

        Assert.Equal("#38bdf8", await CssVariableAsync(inheritedCard, "--etl-accent"));
        Assert.Equal("#94a3b8", await CssVariableAsync(inheritedCard, "--etl-border"));
        Assert.Equal("12px", await CssVariableAsync(inheritedCard, "--etl-radius-md"));
        Assert.Equal("solid", await inheritedPanel.EvaluateAsync<string>("element => getComputedStyle(element).borderStyle"));
        Assert.Equal("rgb(148, 163, 184)", await inheritedPanel.EvaluateAsync<string>("element => getComputedStyle(element).borderColor"));

        var layer = frame.Locator(".container-layer[data-name='LayerContainer']");
        var layerCard = frame.Locator("#etl-v-layercard");
        Assert.Equal("#10b981", await CssVariableAsync(layer, "--etl-accent"));
        Assert.Equal("#059669", await CssVariableAsync(layerCard, "--etl-border"));
        Assert.Equal("rgb(5, 150, 105)", await layerCard.Locator(".token-panel")
            .EvaluateAsync<string>("element => getComputedStyle(element).borderColor"));

        var overrideCard = frame.Locator("#etl-v-overridecard");
        Assert.Equal("", await InlineCssVariableAsync(overrideCard, "--etl-unknown"));
        Assert.Equal("", await InlineCssVariableAsync(overrideCard, "--portal-private"));

        await inheritedCard.Locator(":scope > .visual-toolbar .visual-tool-btn").ClickAsync();
        Assert.Equal("12px", await CssVariableAsync(inheritedCard, "--etl-radius-md"));
        Assert.Equal("#94a3b8", await CssVariableAsync(inheritedCard, "--etl-border"));
        await inheritedCard.Locator(":scope > .visual-toolbar .visual-tool-btn").ClickAsync();
        Assert.Equal("12px", await CssVariableAsync(inheritedCard, "--etl-radius-md"));
        Assert.Equal("#94a3b8", await CssVariableAsync(inheritedCard, "--etl-border"));

        Assert.Empty(session.PageErrors);
    }

    private static Task<string> CssVariableAsync(ILocator locator, string name) =>
        locator.EvaluateAsync<string>("(element, token) => getComputedStyle(element).getPropertyValue(token).trim()", name);

    private static Task<string> InlineCssVariableAsync(ILocator locator, string name) =>
        locator.EvaluateAsync<string>("(element, token) => element.style.getPropertyValue(token).trim()", name);

    /// <summary>
    /// Drives the five <c>vscode-webviews</c> fixtures and asserts each one renders from files the
    /// repository actually tracks.
    ///
    /// <para>The results fixture used to fetch <c>src/etl-sql-vscode/ui/dist/index.html</c> — a Vite
    /// build output, gitignored — and fall back to a stub when the fetch 404'd. That made the story
    /// render one thing for whoever had built the UI and another thing for everyone else, and it is
    /// exactly the kind of difference nobody notices, because the fallback looks like a working
    /// panel. Asserting "mounts cleanly" would not have caught it; asserting that every byte the
    /// story loads is committed does.</para>
    ///
    /// <para>Each fixture is also checked for having rendered something recognisable inside its
    /// frame, because an iframe that loaded nothing still satisfies the shell-level mount check in
    /// <see cref="EveryStoryAndFixture_MountsWithoutThrowing"/>.</para>
    /// </summary>
    [Theory]
    [InlineData("results", "#tables table, #progress .node")]
    [InlineData("preview", ".visual-card")]
    [InlineData("preview-sink", ".visual-card")]
    [InlineData("designer", "#designerRoot *")]
    [InlineData("visual-flow", ".etlsql-dag-card")]
    public async Task VsCodeWebviewFixtures_RenderFromTrackedSourcesOnly(string fixtureId, string frameSelector)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        var requested = new List<string>();
        page.Request += (_, request) => { lock (requested) requested.Add(request.Url); };

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='vscode-webviews']");
        await page.SelectOptionAsync("#fixtureSel", fixtureId);

        // The frames stream (the results replay) or render asynchronously (the preview fetches a
        // sample snapshot), so wait on the content rather than on a fixed delay.
        var frame = page.FrameLocator("iframe.vscode-webview-frame");
        await frame.Locator(frameSelector).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });

        Assert.Empty(session.PageErrors);

        // The browser asks for /favicon.ico on its own; the sandbox declares none, and that 404 says
        // nothing about whether the story's own dependencies are present.
        var failed = session.FailedRequests
            .Where(r => !r.Contains("/favicon.ico", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(failed.Count == 0,
            $"fixture '{fixtureId}' made requests that failed:\n  {string.Join("\n  ", failed)}");

        List<string> paths;
        lock (requested)
            paths = [.. requested.Where(u => u.StartsWith(baseUrl, StringComparison.Ordinal))
                                 .Select(ToRepoRelativePath)
                                 .Where(p => p is not null)
                                 .Select(p => p!)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)];

        Assert.NotEmpty(paths);

        var root = RepoRoot();
        var missing = paths.Where(p => !File.Exists(Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar))))
                           .ToList();
        Assert.True(missing.Count == 0,
            $"fixture '{fixtureId}' requested files that do not exist:\n  {string.Join("\n  ", missing)}");

        var ignored = GitIgnoredPaths(root, paths);
        Assert.True(ignored.Count == 0,
            $"fixture '{fixtureId}' depends on generated files the repository does not track, so it "
            + $"renders differently on a clean checkout:\n  {string.Join("\n  ", ignored)}");
    }

    /// <summary>
    /// Maps a request URL back to a repository-relative path, or null for anything not served out of
    /// the tree (<c>/maps/*</c> is remapped onto the shared runtime directory, exactly as the host in
    /// <see cref="InitializeAsync"/> and <c>serve.ps1</c> do).
    /// </summary>
    private static string? ToRepoRelativePath(string url)
    {
        var rel = Uri.UnescapeDataString(new Uri(url).AbsolutePath).TrimStart('/');
        if (rel.Length == 0) return null;
        if (rel.Equals("favicon.ico", StringComparison.OrdinalIgnoreCase)) return null;
        if (rel.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
            rel = "src/ETL-SQL.ReportRuntime/Resources/Shared/" + rel;
        return rel;
    }

    /// <summary>
    /// Returns the subset of <paramref name="paths"/> that <c>.gitignore</c> excludes.
    ///
    /// <para>Existence is not the question — the file exists on the machine that built it. The
    /// question is whether a fresh clone would have it, and <c>git check-ignore</c> is the only thing
    /// that answers that without re-implementing ignore-rule precedence.</para>
    /// </summary>
    private static List<string> GitIgnoredPaths(string root, IReadOnlyList<string> paths)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("check-ignore");
        psi.ArgumentList.Add("--stdin");

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException(
                "git is required to prove the sandbox runs from a clean checkout.");

        foreach (var path in paths) proc.StandardInput.WriteLine(path);
        proc.StandardInput.Close();

        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        // 0 = some paths are ignored, 1 = none are. Anything else means git itself failed, and a
        // silently empty result would turn this assertion into a no-op.
        if (proc.ExitCode > 1)
            throw new InvalidOperationException($"git check-ignore failed: {proc.StandardError.ReadToEnd()}");

        return [.. output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];
    }

    // A narrow-viewport assertion was tried here and removed. The sandbox stage is
    // `overflow: auto`, so a component wider than it scrolls inside its own container -- which is
    // the correct pattern, not a defect, and the check flagged six components for doing the right
    // thing. Page-level overflow is asserted where it actually reaches users, on the shipped pages
    // in PortalAccessibilityTests.

    /// <summary>
    /// Closes anything a previous fixture left covering the page.
    ///
    /// <para>A story that opens a modal as its fixture is doing its job -- the dataset viewer is
    /// one -- but a <c>position: fixed</c> overlay covers the sandbox's own navigation, so the next
    /// click lands on the modal instead of the story link. Escape first, because that is what a
    /// person would press and it exercises the shared dialog behaviour; hiding directly is the
    /// fallback for overlays that predate it.</para>
    /// </summary>
    // -- Guided authoring and beginner recovery ---------------------------------------------------

    [Fact]
    public async Task Studio_AWizardWrite_OffersAnUndoThatPutsTheScriptBack()
    {
        // A GUI write the author cannot take back is a write they have to read the script to check.
        // The offer is backed by the editor's own history, so taking it is exactly Ctrl+Z.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        var before = await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()");

        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.addVisualToCanvas('BAR')");
        // The offer leads with "Show what changed" (the script-learning panel), so Undo is found by
        // its name. This took the first action and read it as Undo, and failed every run once the
        // learning action was added in front of it.
        var undo = page.Locator(".etlsql-feedback-action", new() { HasText = "Undo" });
        await undo.WaitForAsync();
        Assert.Equal(1, await undo.CountAsync());
        Assert.Equal(1, await page.Locator(".etlsql-feedback-action", new() { HasText = "Show what changed" }).CountAsync());

        var written = await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()");
        Assert.NotEqual(before, written);

        await undo.ClickAsync();
        await page.WaitForFunctionAsync(
            "expected => window.__STUDIO_INSTANCE__.state.editorInstance.getValue() === expected", before);

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_AVisualEditThatLandsInAnotherTab_KeepsThatTabsHistory()
    {
        // The edit's patch is held until the author has moved to another tab, so it lands on a tab
        // that is not showing. Its undo history, caret, and earlier steps must all survive that.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        var original = await page.EvaluateAsync<string>("() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()");
        // An edit of the author's own, so the tab has history from before the canvas edit.
        var typed = await page.EvaluateAsync<string>(
            """
            () => {
                const editor = window.__STUDIO_INSTANCE__.state.editorInstance;
                editor.replaceAll(editor.getValue() + '\n-- my note\n');
                editor.gotoLine(2);
                return editor.getValue();
            }
            """);
        var caretLine = await page.EvaluateAsync<int>("() => window.__STUDIO_INSTANCE__.state.editorInstance.getCursorLine()");

        await page.EvaluateAsync(
            """
            () => {
                window.__heldPatch = false;
                window.__STUDIO_API_DELAY__ = ({ url }) => {
                    if (!url.endsWith('/api/designer/patch')) return 0;
                    window.__heldPatch = true;
                    return 1500;
                };
                window.__STUDIO_INSTANCE__.addVisualToCanvas('BAR');
            }
            """);
        await page.WaitForFunctionAsync("() => window.__heldPatch === true");
        var ids = await page.EvaluateAsync<string[]>("() => window.__STUDIO_INSTANCE__.state.documents.map(d => d.id)");
        await page.Locator($".etlsql-studio-tab[data-doc-id='{ids[1]}']").DispatchEventAsync("click");
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.activeDocId === window.__STUDIO_INSTANCE__.state.documents[1].id");
        await page.WaitForFunctionAsync(
            "typed => window.__STUDIO_INSTANCE__.state.documents[0].content !== typed", typed,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        var patched = await page.EvaluateAsync<string>("() => window.__STUDIO_INSTANCE__.state.documents[0].content");
        var otherTab = await page.EvaluateAsync<string>("() => window.__STUDIO_INSTANCE__.state.documents[1].content");

        await page.Locator($".etlsql-studio-tab[data-doc-id='{ids[0]}']").DispatchEventAsync("click");
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.activeDocId === window.__STUDIO_INSTANCE__.state.documents[0].id");
        string Value() => "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()";

        Assert.Equal(patched, await page.EvaluateAsync<string>(Value()));
        Assert.Contains("-- my note", patched, StringComparison.Ordinal);
        Assert.DoesNotContain(otherTab.Trim(), patched, StringComparison.Ordinal);
        Assert.Equal(caretLine, await page.EvaluateAsync<int>("() => window.__STUDIO_INSTANCE__.state.editorInstance.getCursorLine()"));

        // The canvas edit is one undo step, and the author's own edit is still behind it.
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.undo()");
        Assert.Equal(typed, await page.EvaluateAsync<string>(Value()));
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.undo()");
        Assert.Equal(original, await page.EvaluateAsync<string>(Value()));
        await page.EvaluateAsync("() => { const e = window.__STUDIO_INSTANCE__.state.editorInstance; e.redo(); e.redo(); }");
        Assert.Equal(patched, await page.EvaluateAsync<string>(Value()));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("403", "Access denied (403)")]
    [InlineData("404", "File not found (404)")]
    [InlineData("disconnect", "Network error")]
    public async Task Studio_AFileThatFailsToOpen_OpensNoTabAndLeavesDirtyWork_ThenRetryOpensIt(string failure, string message)
    {
        // An empty tab for a file that could not be read would save over the real file on the next
        // Save. The failure must open nothing, disturb nothing, and offer a Retry that works.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        const string path = "scripts/direct_connect_test.sql";

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        // Close the unmodified tab for the file, so opening it goes to the host.
        var closeId = await page.EvaluateAsync<string>(
            "path => window.__STUDIO_INSTANCE__.state.documents.find(d => d.path === path).id", path);
        await page.Locator($".etlsql-studio-tab[data-doc-id='{closeId}'] .etlsql-tab-close").DispatchEventAsync("click");
        // Sandbox documents start unsaved, so closing asks whether to save; this one is thrown away.
        var dontSave = page.Locator(".etlsql-feedback-dialog button", new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^No$") });
        await dontSave.ClickAsync(new() { Timeout = 5_000 });
        await page.WaitForFunctionAsync(
            "path => !window.__STUDIO_INSTANCE__.state.documents.some(d => d.path === path)", path);

        // Unsaved work in the tab that is showing.
        var dirty = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const studio = window.__STUDIO_INSTANCE__;
                const editor = studio.state.editorInstance;
                editor.replaceAll(editor.getValue() + '\n-- unsaved work\n');
                return { id: studio.state.activeDocId, text: editor.getValue(), tabs: studio.state.documents.length };
            }
            """);
        var dirtyId = dirty.GetProperty("id").GetString()!;
        var dirtyText = dirty.GetProperty("text").GetString()!;
        var tabs = dirty.GetProperty("tabs").GetInt32();

        await page.EvaluateAsync(
            """
            ([failure, path]) => {
                window.__STUDIO_API_RESPONSE__ = ({ url }) => {
                    if (!url.includes('/api/files?path=' + encodeURIComponent(path))) return null;
                    if (failure === 'disconnect') return 'disconnect';
                    return new Response('{}', { status: Number(failure), headers: { 'Content-Type': 'application/json' } });
                };
            }
            """, new[] { failure, path });

        await page.ClickAsync("button.etlsql-studio-rail-btn[data-activity='explorer']");
        var scriptsToggle = page.Locator("[data-explorer-toggle='scripts']");
        var fileRow = page.Locator($"[data-explorer-file='{path}']");
        if (await scriptsToggle.IsVisibleAsync() && !await fileRow.IsVisibleAsync())
            await scriptsToggle.ClickAsync();
        await fileRow.ClickAsync();

        var toast = page.Locator(".etlsql-feedback-toast", new() { HasText = "Open File Failed" });
        await Assertions.Expect(toast).ToContainTextAsync(message);
        await Assertions.Expect(toast).ToContainTextAsync("was not opened");

        var after = await page.EvaluateAsync<JsonElement>(
            """
            ([id, path]) => {
                const studio = window.__STUDIO_INSTANCE__;
                const doc = studio.state.documents.find(d => d.id === id);
                return {
                    active: studio.state.activeDocId,
                    tabs: studio.state.documents.length,
                    opened: studio.state.documents.some(d => d.path === path),
                    dirty: Boolean(doc.isDirty),
                    text: studio.state.editorInstance.getValue(),
                };
            }
            """, new[] { dirtyId, path });
        Assert.Equal(dirtyId, after.GetProperty("active").GetString());
        Assert.Equal(tabs, after.GetProperty("tabs").GetInt32());
        Assert.False(after.GetProperty("opened").GetBoolean(), "A file that could not be read must not open as a tab.");
        Assert.True(after.GetProperty("dirty").GetBoolean());
        Assert.Equal(dirtyText, after.GetProperty("text").GetString());

        // The host answers now; Retry opens the file with what the host holds.
        await page.EvaluateAsync(
            """
            path => {
                window.__STUDIO_API_RESPONSE__ = ({ url }) => url.includes('/api/files?path=' + encodeURIComponent(path))
                    ? new Response(JSON.stringify({ content: '-- regional margin\n', sourceRevision: 'r1' }),
                        { status: 200, headers: { 'Content-Type': 'application/json' } })
                    : null;
            }
            """, path);
        await toast.Locator(".etlsql-feedback-action", new() { HasText = "Retry" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "path => window.__STUDIO_INSTANCE__.state.documents.find(d => d.path === path)?.id === window.__STUDIO_INSTANCE__.state.activeDocId",
            path);
        // The active id changes before switchDoc loads the text, so wait for the text itself.
        await page.WaitForFunctionAsync(
            "text => window.__STUDIO_INSTANCE__.state.editorInstance.getValue() === text", "-- regional margin\n",
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        // The dirty tab still holds its unsaved work.
        var kept = await page.EvaluateAsync<JsonElement>(
            "id => { const d = window.__STUDIO_INSTANCE__.state.documents.find(x => x.id === id); return { dirty: Boolean(d.isDirty), text: d.content }; }",
            dirtyId);
        Assert.True(kept.GetProperty("dirty").GetBoolean());
        Assert.Equal(dirtyText, kept.GetProperty("text").GetString());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_DismissingTheGuidedRail_LeavesARestoreActionOnTheCanvas()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.Locator("[data-workflow-step='palette']").WaitForAsync();

        await page.ClickAsync("[data-dismiss-rail]");
        await page.Locator("[data-workflow-step='palette']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // Hidden steps, visible way back - on the canvas, not only in a collapsed sidebar panel.
        var restore = page.Locator("[data-workflow-bar] [data-show-rail]");
        await restore.WaitForAsync();
        await restore.ClickAsync();
        await page.Locator("[data-workflow-step='palette']").WaitForAsync();

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_TheSurfaceQuestion_CanBeDeclinedAndAnsweredLaterFromTheCanvas()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        const string ambiguous = "-- hand-authored report\nCREATE VISUAL note AS TEXT (TITLE = 'Keep me', SOURCE = #rows);";
        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO_INSTANCE__;
                studio.state.documents.push({ id: 'undecided-report', path: 'undecided.rptsql', name: 'undecided.rptsql', content: script, isDirty: false, projection: 'split' });
                void studio.switchDoc('undecided-report');
            }
            """, ambiguous);

        await page.Locator("[data-choose-workflow-cancel]").WaitForAsync();
        await page.ClickAsync("[data-choose-workflow-cancel]");
        await page.Locator("[data-choose-workflow='dashboard']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // Declining once must not cost the author the tools for the rest of the session, and must
        // not re-ask on every switch back.
        var chooseLater = page.Locator("[data-workflow-bar] [data-choose-surface]");
        await chooseLater.WaitForAsync();
        Assert.Null(await page.EvaluateAsync<string?>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(item => item.id === 'undecided-report').reportWorkflow ?? null"));

        await chooseLater.ClickAsync();
        await page.ClickAsync("[data-choose-workflow='dashboard']");
        await page.Locator(".etlsql-studio-visual-stage.is-dashboard-workflow").WaitForAsync();
        Assert.Equal(ambiguous, await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(item => item.id === 'undecided-report').content"));

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_StartWithSampleData_OpensAWorkingDashboardOnTheCanvas()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('__home__')");

        await page.ClickAsync("[data-create-from-home='dashboard'][data-seed-sample]");
        await page.Locator(".etlsql-studio-visual-stage.is-dashboard-workflow").WaitForAsync();

        var seeded = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const studio = window.__STUDIO_INSTANCE__;
                const doc = studio.state.documents.find(item => item.id === studio.state.activeDocId);
                return { projection: doc.projection, content: doc.content };
            }
            """);

        // The canvas, not a split with a script the author has not written yet.
        Assert.Equal("canvas", seeded.GetProperty("projection").GetString());
        var content = seeded.GetProperty("content").GetString() ?? "";
        Assert.Contains("AS CARD", content, StringComparison.Ordinal);
        Assert.Contains("AS BAR", content, StringComparison.Ordinal);
        Assert.Contains("AS TABLE", content, StringComparison.Ordinal);
        Assert.Contains("AS DASHBOARD", content, StringComparison.Ordinal);

        // Three tiles on the canvas, not an empty page that happens to parse.
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.etlsql-dsgn-visual-card').length >= 3");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_Rail_ReachesTheDesignersOwnBookmarkAndReportStyleEditors()
    {
        // Both surfaces already existed in designer.js, inside a sidebar Studio hides. The rail
        // mounts the same elements rather than growing a second implementation of either.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);

        await page.ClickAsync("[data-activity='palette']");
        await page.Locator("[data-bookmark-host] #dsgn-add-bookmark").WaitForAsync();

        await page.ClickAsync("[data-report-style]");
        await page.Locator("#pp-report-theme").WaitForAsync();
        await page.SelectOptionAsync("#pp-report-theme", "dark");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO_INSTANCE__.state.designerInstance.getState().reportStyle?.theme === 'dark'");

        Assert.Empty(session.PageErrors);
    }

    // -- Cross-visual interaction and cascading slicers --------------------------------------------

    /// <summary>Opens a collapsed inspector group by its summary text.</summary>
    private static async Task OpenInspectorGroupAsync(IPage page, string summary)
    {
        await page.EvaluateAsync(
            """
            text => {
                const group = [...document.querySelectorAll('.etlsql-format-group')]
                    .find(item => item.querySelector('summary')?.textContent.trim() === text);
                if (group) group.open = true;
            }
            """, summary);
    }

    [Fact]
    public async Task Studio_CrossVisualInteraction_IsChosenFromControlsAndLandsInTheScript()
    {
        // On Select used to be a free-text box whose placeholder was the documentation, and whose
        // handler wrote to the in-memory visual and never to the script.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.designerInstance)");

        await page.EvaluateAsync(
            """
            () => {
                const studio = window.__STUDIO_INSTANCE__;
                const chart = studio.state.designerInstance.getState().pages
                    .flatMap(page => page.visuals || [])
                    .find(visual => visual.type === 'BAR');
                studio.state.designerInstance.selectVisual(chart.id);
            }
            """);
        await page.Locator("#pp-interaction-on-select").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
        await OpenInspectorGroupAsync(page, "Actions & Interactions");
        await page.Locator("#pp-interaction-on-select").WaitForAsync();

        await page.SelectOptionAsync("#pp-interaction-on-select", "FILTER");
        await page.Locator("#pp-interaction-matching").WaitForAsync();
        await page.FillAsync("#pp-interaction-matching", "region");
        await page.Locator("#pp-interaction-matching").BlurAsync();

        await page.WaitForFunctionAsync(
            """
            () => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()
                .includes('INTERACTIONS (ON_SELECT = FILTER, MATCHING = region)')
            """);

        // The sentence under the control describes the reader's experience, not the dialect.
        var note = await page.Locator(".etlsql-dsgn-interaction-note").First.TextContentAsync() ?? "";
        Assert.Contains("region", note, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_CascadingSlicer_IsAuthoredFromTheInspectorAndNamesItsParent()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        const string script = """
            CREATE CONNECTION corp_db AS MSSQL(CONNECTION_STRING = 'SHARED:corp_sales_gw');
            DECLARE @Region VARCHAR = 'North';
            SELECT CityCode, RegionCode INTO #city_options FROM corp_db.cities;
            CREATE VISUAL CityFilter AS SLICER (
              TITLE = 'City',
              SOURCE = #city_options,
              MAPPINGS (VALUE = CityCode)
            );
            CREATE PAGE [Filters] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = CityFilter)));
            """;
        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO_INSTANCE__;
                studio.state.documents.push({ id: 'cascade-report', path: 'cascade.rptsql', name: 'cascade.rptsql', content: script, isDirty: false, projection: 'split', reportWorkflow: 'dashboard' });
                void studio.switchDoc('cascade-report');
            }
            """, script);

        await page.WaitForFunctionAsync(
            """
            () => (window.__STUDIO_INSTANCE__.state.designerInstance?.getState?.()?.pages || [])
                .flatMap(page => page.visuals || []).some(visual => visual.type === 'SLICER')
            """);
        await page.EvaluateAsync(
            """
            () => {
                const studio = window.__STUDIO_INSTANCE__;
                const slicer = studio.state.designerInstance.getState().pages
                    .flatMap(page => page.visuals || [])
                    .find(visual => visual.type === 'SLICER');
                studio.state.designerInstance.selectVisual(slicer.id);
            }
            """);

        await page.Locator("#pp-cascade-mode").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
        await OpenInspectorGroupAsync(page, "Cascading options");
        await page.Locator("#pp-cascade-mode").WaitForAsync();
        await page.SelectOptionAsync("#pp-cascade-mode", "LOCAL");

        // A LOCAL cascade with no parent filters nothing, and the panel says so rather than
        // pretending the control is finished.
        await page.Locator("#pp-cascade-add-parent").WaitForAsync();
        await page.ClickAsync("#pp-cascade-add-parent");
        await page.Locator("[data-cascade-parameter='0']").WaitForAsync();
        await page.FillAsync("[data-cascade-column='0']", "RegionCode");
        await page.Locator("[data-cascade-column='0']").BlurAsync();

        await page.WaitForFunctionAsync(
            """
            () => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()
                .includes('CASCADE ( MODE = LOCAL, PARENTS (@Region = RegionCode)')
            """);

        var written = await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()");
        Assert.Contains("INVALID = CLEAR, NULL = ALL, ALL_VALUE = '*', MULTISELECT = ANY", written, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    // -- The filter pane past its first twelve values ----------------------------------------------

    [Fact]
    public async Task Studio_FilterPane_SearchesSelectsAndPagesThroughEveryValue()
    {
        // The pane used to cut a categorical list at twelve, with no search, no count and no way to
        // reach the thirteenth: values that existed in the data could not be filtered on at all.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        // A column with more values than one card can show. The behaviour under test only begins
        // past the first page, and the sandbox's default sample has four regions.
        await page.AddInitScriptAsync("window.__STUDIO_SAMPLE_ROWS__ = 60;");
        await page.ReloadAsync();
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.ClickAsync("button.etlsql-studio-rail-btn[data-activity='catalog']");
        await page.ClickAsync("button.etlsql-studio-rail-btn[data-activity='filters']");

        var filterSidebar = page.Locator("[data-filter-sidebar]");
        await filterSidebar.Locator("[data-new-filter]").ClickAsync();
        var setup = page.Locator(".etlsql-studio-filter-dialog");
        await setup.WaitForAsync();
        await setup.Locator("[data-filter-dialog-field]").SelectOptionAsync("region");
        await setup.Locator("[data-filter-dialog-apply]").ClickAsync();
        await filterSidebar.Locator("[data-filter-value='region']").First.WaitForAsync();

        Assert.Equal(25, await filterSidebar.Locator("[data-filter-value='region']").CountAsync());
        Assert.Contains("25 of 60", await filterSidebar.Locator(".etlsql-filter-value-footer").InnerTextAsync());

        // The undo offer from the write sits over the bottom-right corner, which is where this pane
        // is; dismissing it is what a reader would do before carrying on.
        await DismissToastsAsync(page);
        await filterSidebar.Locator("[data-filter-show-more='region']").ClickAsync();
        Assert.Equal(50, await filterSidebar.Locator("[data-filter-value='region']").CountAsync());

        await filterSidebar.Locator("[data-filter-search='region']").FillAsync("region_4");
        await page.WaitForFunctionAsync(
            """
            () => document.querySelectorAll("[data-filter-value='region']").length === 10
            """);

        // Select all acts on what the search narrowed to, and adds to the selection rather than
        // replacing it: the search changed the view, not the filter.
        var selectedBefore = await page.EvaluateAsync<int>(
            """
            () => {
                const studio = window.__STUDIO_INSTANCE__;
                const doc = studio.state.documents.find(item => item.id === studio.state.activeDocId);
                return (doc.studioContext.activeFilters.region?.values || []).length;
            }
            """);

        await DismissToastsAsync(page);
        await filterSidebar.Locator("[data-filter-select-all='region']").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            expected => {
                const studio = window.__STUDIO_INSTANCE__;
                const doc = studio.state.documents.find(item => item.id === studio.state.activeDocId);
                return (doc.studioContext.activeFilters.region?.values || []).length === expected;
            }
            """, selectedBefore + 10);

        // Inverting what is shown takes exactly those ten back off again, and leaves the values the
        // search is hiding alone.
        await DismissToastsAsync(page);
        await filterSidebar.Locator("[data-filter-invert='region']").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            expected => {
                const studio = window.__STUDIO_INSTANCE__;
                const doc = studio.state.documents.find(item => item.id === studio.state.activeDocId);
                return (doc.studioContext.activeFilters.region?.values || []).length === expected;
            }
            """, selectedBefore);

        // Clear empties the whole selection, not only what is on screen.
        await DismissToastsAsync(page);
        await filterSidebar.Locator("[data-filter-select-none='region']").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const studio = window.__STUDIO_INSTANCE__;
                const doc = studio.state.documents.find(item => item.id === studio.state.activeDocId);
                return (doc.studioContext.activeFilters.region?.values || []).length === 0;
            }
            """);

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_NumericFilter_OffersComparisonsAndNullChecksAndSendsThem()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.ClickAsync("button.etlsql-studio-rail-btn[data-activity='catalog']");
        await page.ClickAsync("button.etlsql-studio-rail-btn[data-activity='filters']");

        var filterSidebar = page.Locator("[data-filter-sidebar]");
        await filterSidebar.Locator("[data-new-filter]").ClickAsync();
        var setup = page.Locator(".etlsql-studio-filter-dialog");
        await setup.WaitForAsync();
        await setup.Locator("[data-filter-dialog-field]").SelectOptionAsync("total_amount");
        await setup.Locator("[data-filter-dialog-apply]").ClickAsync();

        // A range is the default, and both bounds are shown.
        await filterSidebar.Locator("[data-filter-operator='total_amount']").WaitForAsync();
        Assert.True(await filterSidebar.Locator("[data-filter-max='total_amount']").IsVisibleAsync());

        await filterSidebar.Locator("[data-filter-operator='total_amount']").SelectOptionAsync("greater");
        await filterSidebar.Locator("[data-filter-min='total_amount']").WaitForAsync();
        // One comparison, one value: the second bound would describe nothing.
        Assert.Equal(0, await filterSidebar.Locator("[data-filter-max='total_amount']").CountAsync());
        await filterSidebar.Locator("[data-filter-min='total_amount']").FillAsync("500");
        await filterSidebar.Locator("[data-filter-min='total_amount']").BlurAsync();

        await page.WaitForFunctionAsync(
            """
            () => window.__STUDIO_API_REQUESTS__.some(request =>
                request.url.endsWith('/api/designer/query-filter') &&
                (request.body?.filters || []).some(filter =>
                    filter.column === 'total_amount' && filter.operator === 'greater' && filter.minimum === '500'))
            """);

        // A null check needs no value at all, and must not keep the one the comparison had.
        await filterSidebar.Locator("[data-filter-operator='total_amount']").SelectOptionAsync("isnull");
        await page.WaitForFunctionAsync(
            """
            () => window.__STUDIO_API_REQUESTS__.some(request =>
                request.url.endsWith('/api/designer/query-filter') &&
                (request.body?.filters || []).some(filter =>
                    filter.column === 'total_amount' && filter.operator === 'isnull' && !filter.minimum))
            """);
        Assert.Equal(0, await filterSidebar.Locator("[data-filter-min='total_amount']").CountAsync());

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_PaginatedExportStep_ExportsThePdfItselfRatherThanExplainingWhereToFindOne()
    {
        // Step 8 used to be a page of instructions pointing at an export elsewhere — which, for a
        // report that only exists in an unsaved buffer, was nowhere.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        // A paginated report, so the export step is the one under test.
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('__home__')");
        await page.Locator("[data-create-from-home='paginated']").ClickAsync();
        await page.Locator("[data-workflow-step='export']").WaitForAsync();

        // Downloads are inert in this context, so the click is observed as the request Studio makes
        // and the anchor it creates rather than as a file on disk.
        await page.EvaluateAsync(
            """
            () => {
                window.__STUDIO_DOWNLOADS__ = [];
                const create = document.createElement.bind(document);
                document.createElement = tag => {
                    const element = create(tag);
                    if (String(tag).toLowerCase() === 'a') {
                        const click = element.click.bind(element);
                        element.click = () => {
                            if (element.download) window.__STUDIO_DOWNLOADS__.push(element.download);
                            else click();
                        };
                    }
                    return element;
                };
            }
            """);

        await page.ClickAsync("[data-workflow-step='export']");
        await page.Locator("[data-dialog-action='export']").WaitForAsync();
        await page.ClickAsync("[data-dialog-action='export']");

        await page.WaitForFunctionAsync(
            """
            () => window.__STUDIO_API_REQUESTS__.some(request =>
                request.url.endsWith('/api/designer/preview/pdf')
                && typeof request.body?.script === 'string'
                && request.body.script.includes('AS PAGINATED'))
            """);
        await page.WaitForFunctionAsync(
            "() => (window.__STUDIO_DOWNLOADS__ || []).some(name => name.endsWith('.pdf'))");

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_AReportThatPrompts_AsksBeforeExportingAndSendsTheAnswers()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        const string script = """
            DECLARE @region VARCHAR INPUT = 'North';

            SELECT region, total_amount INTO #rows FROM &orders;

            CREATE VISUAL detail AS TABLE ( TITLE = 'Detail', SOURCE = #rows );

            CREATE PAGE [Detail] AS PAGINATED (
              LAYOUT (STRUCTURE = 'A', MAP ('A' = detail)),
              PRINT_LAYOUT (PAGE_SIZE = 'Letter', ORIENTATION = 'PORTRAIT')
            );
            """;
        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO_INSTANCE__;
                studio.state.documents.push({ id: 'prompted-report', path: 'prompted.rptsql', name: 'prompted.rptsql', content: script, isDirty: false, projection: 'split', reportWorkflow: 'paginated' });
                void studio.switchDoc('prompted-report');
            }
            """, script);
        await page.Locator("[data-workflow-step='export']").WaitForAsync();

        await page.EvaluateAsync("() => { window.__STUDIO_DOWNLOADS__ = []; }");
        await page.ClickAsync("[data-workflow-step='export']");
        await page.Locator("[data-dialog-action='export']").WaitForAsync();
        await page.ClickAsync("[data-dialog-action='export']");

        // The prompt comes before the export, not after it: an INPUT parameter is a question, and
        // exporting the declared default would answer it on the reader's behalf.
        await page.Locator("[data-parameter-answer='@region']").WaitForAsync();
        Assert.Equal("North", await page.Locator("[data-parameter-answer='@region']").InputValueAsync());
        await page.FillAsync("[data-parameter-answer='@region']", "South");
        await page.ClickAsync("[data-dialog-action='accept']");

        await page.WaitForFunctionAsync(
            """
            () => window.__STUDIO_API_REQUESTS__.some(request =>
                request.url.endsWith('/api/designer/preview/pdf')
                && request.body?.parameters?.['@region'] === 'South')
            """);

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_PaginationPreview_ListsThePagesTheExportWillProduce()
    {
        // The canvas draws a page-width sheet, which says "paper" and not "how many pages, and what
        // lands on each" — the question a paginated report exists to answer. The step now reads the
        // engine's own compiled breakdown rather than leaving the sheet to imply one.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        const string script = """
            SELECT region, total_amount INTO #rows FROM &orders;

            CREATE VISUAL detail_rows AS TABLE ( TITLE = 'Detail', SOURCE = #rows );

            CREATE PAGE [Detail] AS PAGINATED (
              LAYOUT (STRUCTURE = 'A', MAP ('A' = detail_rows)),
              PRINT_LAYOUT (PAGE_SIZE = 'Letter', ORIENTATION = 'PORTRAIT')
            );
            """;
        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO_INSTANCE__;
                studio.state.documents.push({ id: 'paginated-report', path: 'paginated.rptsql', name: 'paginated.rptsql', content: script, isDirty: false, projection: 'split', reportWorkflow: 'paginated' });
                void studio.switchDoc('paginated-report');
            }
            """, script);
        await page.Locator("[data-workflow-step='preview']").WaitForAsync();

        await DismissToastsAsync(page);
        await page.ClickAsync("[data-workflow-step='preview']");

        // Every page is run for the breakdown: a deferred page would report itself as empty.
        await page.WaitForFunctionAsync(
            """
            () => window.__STUDIO_API_REQUESTS__.some(request =>
                request.url.endsWith('/api/designer/preview') && request.body?.runEveryPage === true)
            """);

        var breakdown = page.Locator(".etlsql-studio-guided-body");
        await breakdown.Locator("text=physical page").First.WaitForAsync();
        var text = await breakdown.InnerTextAsync();
        Assert.Contains("Page 1", text, StringComparison.Ordinal);
        Assert.Contains("Page 2", text, StringComparison.Ordinal);
        // The row ranges are what make the breakdown actionable rather than a page count.
        Assert.Contains("rows 1", text, StringComparison.Ordinal);

        Assert.Empty(session.PageErrors);
    }

    // -- Document outline and layer tree -----------------------------------------------------------

    [Fact]
    public async Task Studio_Outline_ListsPagesAndRowsAndSelectsBothWaysWithTheCanvas()
    {
        // The visual library already listed what was on the page. It could not act on the list, and
        // it never told the canvas anything: the tree was one-way. This is the panel that closes
        // both halves — a row selects the tile, and a tile selects the row.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.designerInstance)");

        await page.ClickAsync("[data-activity='outline']");
        await page.Locator(".etlsql-studio-outline-tree").WaitForAsync();

        var tree = await page.Locator(".etlsql-studio-outline-tree").InnerTextAsync();
        Assert.Contains("Executive Overview", tree, StringComparison.Ordinal);
        Assert.Contains("RevenueCard", tree, StringComparison.Ordinal);
        Assert.Contains("SalesByRegion", tree, StringComparison.Ordinal);

        // The page places both tiles side by side, so they belong to one row band. Splitting them
        // into two would mean the panel is reading its own stacking rather than the page's layout.
        Assert.Equal(1, await page.Locator(".etlsql-studio-outline-band").CountAsync());

        // Outline → canvas.
        await page.ClickAsync("[data-outline-select$='SalesByRegion_1'], .etlsql-studio-outline-select:has-text('SalesByRegion')");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO_INSTANCE__.state.selectedVisualId?.includes('SalesByRegion')");
        await page.Locator(".etlsql-studio-outline-item.is-selected:has-text('SalesByRegion')").WaitForAsync();

        // Canvas → outline. The rail stays on the outline: switching it to the visual library on
        // every selection is what used to close the panel the author was working in.
        await page.EvaluateAsync(
            """
            () => {
                const studio = window.__STUDIO_INSTANCE__;
                const card = studio.state.designerInstance.getState().pages
                    .flatMap(item => item.visuals || [])
                    .find(visual => visual.name === 'RevenueCard');
                studio.state.designerInstance.selectVisual(card.id);
            }
            """);
        await page.Locator(".etlsql-studio-outline-item.is-selected:has-text('RevenueCard')").WaitForAsync();
        Assert.Equal("outline", await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.activeActivity"));

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_Outline_MoveAndHideWriteToTheScript()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        await page.ClickAsync("[data-activity='outline']");
        await page.Locator(".etlsql-studio-outline-tree").WaitForAsync();
        await DismissToastsAsync(page);

        // Move: the panel swaps grid placement, and the patcher regenerates STRUCTURE from it. The
        // assertion is on the layout the reader gets, not on the coordinates the panel wrote.
        Assert.Contains("STRUCTURE = 'A B'", await ScriptTextAsync(page), StringComparison.Ordinal);
        await page.ClickAsync(".etlsql-studio-outline-item:has-text('RevenueCard') [data-outline-down]");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue().includes(\"STRUCTURE = 'B A'\")");

        // Hide: VISIBLE = OFF is the property the report runtime already reads, and the row stays in
        // the outline — a tile the reader cannot see is exactly the one the author needs listed.
        await DismissToastsAsync(page);
        await page.ClickAsync(".etlsql-studio-outline-item:has-text('SalesByRegion') [data-outline-visible]");
        await page.WaitForFunctionAsync(
            "() => /VISIBLE\\s*=\\s*OFF/i.test(window.__STUDIO_INSTANCE__.state.editorInstance.getValue())");
        await page.Locator(".etlsql-studio-outline-item.is-hidden:has-text('SalesByRegion')").WaitForAsync();

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_OutlineLock_StopsTheCanvasFromMovingAVisualWithoutWritingToTheScript()
    {
        // Lock is the one outline control that writes nothing: the report language has no LOCKED,
        // and inventing an option so the button could pretend to persist would put a word in the
        // author's file that nothing else reads. So the claim to prove is the other half — that the
        // canvas honours a lock that lives only in the browser.
        //
        // Both drags start from the same cell and use the same gesture, and the unlocked one has to
        // actually move the card. Asserting only that a locked card stayed put would be satisfied
        // just as well by a gesture that never moved anything — which is what an earlier version of
        // this test was in fact asserting.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.designerInstance)");

        await page.ClickAsync("[data-activity='outline']");
        await page.Locator(".etlsql-studio-outline-tree").WaitForAsync();
        await DismissToastsAsync(page);

        var scriptBeforeLock = await ScriptTextAsync(page);
        await page.ClickAsync(".etlsql-studio-outline-item:has-text('RevenueCard') [data-outline-lock]");
        await page.Locator(".etlsql-studio-outline-item.is-locked:has-text('RevenueCard')").WaitForAsync();
        Assert.Equal(scriptBeforeLock, await ScriptTextAsync(page));

        var lockedCard = page.Locator(".etlsql-dsgn-visual-card.is-locked");
        await lockedCard.WaitForAsync();
        var placement = await CardPlacementAsync(page, "RevenueCard");
        await DragCardAsync(page, lockedCard);
        Assert.Equal(placement, await CardPlacementAsync(page, "RevenueCard"));

        // Unlocking hands the canvas back, and the same drag from the same cell now lands.
        await DismissToastsAsync(page);
        await page.ClickAsync(".etlsql-studio-outline-item:has-text('RevenueCard') [data-outline-lock]");
        await page.Locator(".etlsql-dsgn-visual-card.is-locked").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await DragCardAsync(page, page.Locator(".etlsql-dsgn-visual-card[data-vid*='RevenueCard']"));
        await page.WaitForFunctionAsync(
            """
            before => {
                const visual = window.__STUDIO_INSTANCE__.state.designerInstance.getState().pages
                    .flatMap(page => page.visuals || [])
                    .find(item => item.name === 'RevenueCard');
                return visual && `${visual.gridCol}/${visual.gridRow}/${visual.gridColSpan}/${visual.gridRowSpan}` !== before;
            }
            """, placement);

        Assert.Empty(session.PageErrors);
    }

    // -- Keyboard move and resize -------------------------------------------------------------------

    /// <summary>
    /// ST25, shared sandbox half: a visual is moved and resized without a pointer, and the layout
    /// the keyboard produced is the layout in the script.
    ///
    /// <para>Three controls, each the only way one of the three changes is made here: arrow keys on
    /// the focused card move it, Shift with an arrow resizes it, and the inspector's grid fields
    /// place it by typed number. Focus is walked to each with Tab rather than set by script.</para>
    /// </summary>
    [Fact]
    public async Task Studio_Canvas_MovesAndResizesAVisualFromTheKeyboardAndWritesTheLayout()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.designerInstance)");
        await page.Locator(".etlsql-dsgn-visual-card[data-vid*='SalesByRegion']").WaitForAsync();
        await DismissToastsAsync(page);

        const string revenue = ".etlsql-dsgn-visual-card[data-vid*='RevenueCard']";
        const string sales = ".etlsql-dsgn-visual-card[data-vid*='SalesByRegion']";
        Assert.Contains("STRUCTURE = 'A B'", await ScriptTextAsync(page), StringComparison.Ordinal);
        Assert.Equal("1/1/6/4", await CardPlacementAsync(page, "RevenueCard"));
        Assert.Equal("7/1/6/4", await CardPlacementAsync(page, "SalesByRegion"));

        // The card says where it sits, so the result of a move can be read without seeing the grid.
        await page.Locator(".etlsql-studio-tab.active").FocusAsync();
        await KeyboardWalk.TabToAsync(page, sales);
        Assert.Contains("column 7, row 1, 6 columns wide, 4 rows tall",
            await page.Locator(sales).GetAttributeAsync("aria-label"), StringComparison.Ordinal);

        // Move: four rows down, with nothing selected first. Focus stays on the card through every
        // repaint, or the second keypress would land on the page instead of the visual.
        await KeyboardWalk.PressAsync(page, "ArrowDown", 4);
        Assert.Equal("7/5/6/4", await CardPlacementAsync(page, "SalesByRegion"));
        await Expect(page.Locator(sales)).ToBeFocusedAsync();
        await Expect(page.Locator("[data-canvas-status]")).ToContainTextAsync("moved: column 7, row 5");
        await WaitForStructureAsync(page, "A . / . B");

        // A step off the grid is refused and said so, and costs nothing.
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("7/5/6/4", await CardPlacementAsync(page, "SalesByRegion"));
        await Expect(page.Locator("[data-canvas-status]")).ToContainTextAsync("cannot move further");

        // Resize: the other card, reached by Shift+Tab, widened to the full grid.
        await KeyboardWalk.TabToAsync(page, revenue, backwards: true);
        await KeyboardWalk.PressAsync(page, "Shift+ArrowRight", 6);
        Assert.Equal("1/1/12/4", await CardPlacementAsync(page, "RevenueCard"));
        await Expect(page.Locator(revenue)).ToBeFocusedAsync();
        await Expect(page.Locator("[data-canvas-status]")).ToContainTextAsync("resized: column 1, row 1, 12 columns wide");
        await WaitForStructureAsync(page, "A / . B");

        // Place by number: the inspector's grid fields, typed and committed with Tab.
        await KeyboardWalk.TabToAsync(page, sales);
        await page.Keyboard.PressAsync("Enter");
        await page.Locator("#pp-col").WaitForAsync();
        await KeyboardWalk.TabToAsync(page, "#pp-col", backwards: true);
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.TypeAsync("1");
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForFunctionAsync(
            """
            () => {
                const visual = window.__STUDIO_INSTANCE__.state.designerInstance.getState().pages
                    .flatMap(page => page.visuals || [])
                    .find(item => item.name === 'SalesByRegion');
                return visual?.gridCol === 1;
            }
            """);
        Assert.Equal("1/5/6/4", await CardPlacementAsync(page, "SalesByRegion"));
        await WaitForStructureAsync(page, "A / B .");

        Assert.Empty(session.PageErrors);
    }

    private static async Task WaitForStructureAsync(IPage page, string structure)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "structure => window.__STUDIO_INSTANCE__.state.editorInstance.getValue().includes(`STRUCTURE = '${structure}'`)",
                structure, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"The script never said STRUCTURE = '{structure}'.{Environment.NewLine}{await ScriptTextAsync(page)}", exception);
        }
    }

    // -- Data-model (ER) view ----------------------------------------------------------------------

    [Fact]
    public async Task Studio_ModelProjection_DrawsWhatTheScriptSaysAndAdmitsWhatItCannotState()
    {
        // The claim under test is not "a diagram appeared". It is that the diagram says where its
        // relationships came from — a view that renders a plausible cardinality it cannot support is
        // worse than no view, because the author will design around it.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        const string script = """
            CREATE CONNECTION corp AS MOCKDB();

            SELECT o.order_id, c.name
            INTO #enriched
            FROM corp.orders o
            JOIN corp.customers c ON o.customer_id = c.customer_id;
            """;
        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO_INSTANCE__;
                studio.state.documents.push({ id: 'model-doc', path: 'model.rptsql', name: 'model.rptsql', content: script, isDirty: false, projection: 'split', reportWorkflow: 'dashboard' });
                void studio.switchDoc('model-doc');
            }
            """, script);
        await DismissToastsAsync(page);

        await page.ClickAsync("[data-projection='model']");
        await page.Locator("[data-model-view] .etlsql-studio-model-summary").WaitForAsync();

        var summary = await page.Locator(".etlsql-studio-model-summary").InnerTextAsync();
        Assert.Contains("2 tables", summary, StringComparison.Ordinal);
        Assert.Contains("1 join", summary, StringComparison.Ordinal);
        // No database was asked, and the banner has to say that rather than leave the reader to
        // assume the blank cardinality is a finding.
        Assert.Contains("No database keys were available", summary, StringComparison.Ordinal);
        Assert.Equal("Script evidence only", await page.Locator("[data-model-status]").InnerTextAsync());

        // Every entity the script names is on the diagram.
        foreach (var entity in new[] { "orders", "customers", "#enriched" })
            await page.Locator($"[data-model-canvas] >> text={entity}").First.WaitForAsync();

        // And the join edge carries the columns it was written on plus an explicitly unstated
        // cardinality. The label lives on the edge as data, which is where the shared renderer keeps
        // it — asserting on rendered text here would be asserting on the renderer, not on the claim.
        var edgeLabel = await page.Locator("[data-model-canvas] [data-dag-label]").First
            .GetAttributeAsync("data-dag-label");
        Assert.Equal("customer_id = customer_id · not stated", edgeLabel);

        // Switching away and back repaints: the stage is shared with the canvas and the pipeline map.
        await page.ClickAsync("[data-projection='split']");
        await page.Locator("[data-model-view]").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await page.ClickAsync("[data-projection='model']");
        await page.Locator("[data-model-view] .etlsql-studio-model-summary").WaitForAsync();

        Assert.Empty(session.PageErrors);
    }

    // -- Engine state and visual EXPLAIN -----------------------------------------------------------

    [Fact]
    public async Task Studio_EnginePanel_ScopesToTheCursorAndPlansTheStatementUnderIt()
    {
        // Two claims, and the first is the one that is easy to get wrong: scope is positional. A
        // panel listing every name in the file would offer the author a #temp that does not exist
        // yet, which is wrong only at run time — the most expensive place to find out.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        const string script = """
            CREATE CONNECTION corp AS MOCKDB();

            DECLARE @cutoff INT = 100;

            SELECT order_id, total INTO #recent FROM corp.orders WHERE total > @cutoff;

            SELECT region, SUM(total) AS revenue INTO #by_region FROM #recent GROUP BY region;

            DECLARE @after INT = 7;
            """;
        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO_INSTANCE__;
                studio.state.documents.push({ id: 'engine-doc', path: 'engine.etlsql', name: 'engine.etlsql', content: script, isDirty: false, projection: 'split' });
                void studio.switchDoc('engine-doc');
            }
            """, script);
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue().includes('#by_region')");

        // Put the caret in the statement that reads #recent and builds #by_region.
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.gotoLine(7)");
        await DismissToastsAsync(page);
        await page.ClickAsync("[data-activity='engine']");
        await page.Locator(".etlsql-studio-scope-list").First.WaitForAsync();

        var scope = await page.Locator("[data-sidebar-content]").InnerTextAsync();
        Assert.Contains("@cutoff", scope, StringComparison.Ordinal);
        Assert.Contains("#recent", scope, StringComparison.Ordinal);
        // Declared below the cursor, so not in scope; produced by this very statement, so not yet.
        Assert.DoesNotContain("@after", scope, StringComparison.Ordinal);

        // The plan is the engine's own EXPLAIN, asked for through the ordinary run route so it
        // passes the same policy and limits as any other execution.
        await page.ClickAsync("[data-explain-statement]");
        await page.Locator(".etlsql-studio-plan").WaitForAsync();

        var submitted = await page.EvaluateAsync<string[]>(
            """
            () => window.__STUDIO_API_REQUESTS__
                .filter(request => request.url.endsWith('/api/designer/run'))
                .map(request => request.body?.selection ?? '')
            """);
        // The plan is asked for through the ordinary run route, and the slice carries the statements
        // above the cursor: they are what builds the #temp the plan reads.
        var explained = Assert.Single(submitted, entry => entry.Contains("EXPLAIN", StringComparison.Ordinal));
        Assert.Contains("INTO #recent", explained, StringComparison.Ordinal);
        Assert.DoesNotContain("@after", explained, StringComparison.Ordinal);

        var plan = await page.Locator(".etlsql-studio-plan").InnerTextAsync();
        Assert.Contains("Scan", plan, StringComparison.Ordinal);
        Assert.Contains("Aggregate", plan, StringComparison.Ordinal);
        // The badge an author acts on: where the query stops streaming and starts holding rows.
        // Compared case-insensitively because the badge is uppercased by CSS, and asserting on the
        // rendered casing would be asserting on the stylesheet.
        Assert.Contains("blocking", plan, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(session.PageErrors);
    }

    // -- Beginner-facing diagnostics -----------------------------------------------------------------

    [Fact]
    public async Task Studio_ADiagnostic_ExplainsItselfNamesTheCardAndOffersAFixThatWorks()
    {
        // The parser's own message is the precise statement of the failure and stays where it was.
        // What is new is the sentence under it, the card it names, and a button that makes the edit
        // — the difference between "Unexpected token 'Revenue'" and knowing what to type.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await WaitForStudioAsync(page);
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");

        const string script = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT region, total INTO #rows FROM corp.orders;

            CREATE VISUAL RevenueCard AS CARD (
                SOURCE = #rows,
                TITLE = Total Revenue,
                MAPPINGS (VALUE = total)
            );
            """;
        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO_INSTANCE__;
                studio.state.documents.push({ id: 'guidance-doc', path: 'guidance.rptsql', name: 'guidance.rptsql', content: script, isDirty: false, projection: 'split', reportWorkflow: 'dashboard' });
                void studio.switchDoc('guidance-doc');
            }
            """, script);

        // Diagnostics live on the Messages tab, and the editor analyses on a debounce.
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.analyze()");
        await page.ClickAsync("[data-tab='messages']");
        await page.Locator(".etlsql-script-guidance").First.WaitForAsync();
        var guidance = await page.Locator(".etlsql-script-guidance").First.InnerTextAsync();
        Assert.Contains("bare word", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single quotes", guidance, StringComparison.OrdinalIgnoreCase);
        // Anchored to the card, because the canvas has no line numbers on it. Compared
        // case-insensitively: the badge is uppercased by CSS, and rendered text carries that.
        var anchor = await page.Locator(".etlsql-script-guidance-anchor").First.InnerTextAsync();
        Assert.Contains("RevenueCard", anchor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visual", anchor, StringComparison.OrdinalIgnoreCase);

        // A link into the reference tree, which is also the product's embedded help.
        Assert.Equal(
            "docs/reference/data-types.md",
            await page.Locator(".etlsql-script-guidance-doc").First.GetAttributeAsync("href"));

        // And the repair actually repairs, through the editor's own transaction — so the undo offer
        // that follows a GUI write covers it like any other.
        await DismissToastsAsync(page);
        await page.ClickAsync(".etlsql-script-guidance-fix");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue().includes(\"TITLE = 'Total Revenue'\")");

        Assert.Empty(session.PageErrors);
    }

    private static async Task<string> ScriptTextAsync(IPage page) =>
        await page.EvaluateAsync<string>("() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()");

    private static async Task<string> CardPlacementAsync(IPage page, string visualName) =>
        await page.EvaluateAsync<string>(
            """
            name => {
                const visual = window.__STUDIO_INSTANCE__.state.designerInstance.getState().pages
                    .flatMap(page => page.visuals || [])
                    .find(item => item.name === name);
                return visual ? `${visual.gridCol}/${visual.gridRow}/${visual.gridColSpan}/${visual.gridRowSpan}` : '';
            }
            """, visualName);

    /// <summary>
    /// Drags a card down a row with real mouse events, grabbing its body rather than its header —
    /// the canvas hands header clicks to the rename and action handlers and starts no drag from them.
    /// </summary>
    private static async Task DragCardAsync(IPage page, ILocator card)
    {
        var box = await card.BoundingBoxAsync();
        Assert.NotNull(box);
        var x = box!.X + box.Width / 2;
        var y = box.Y + box.Height * 0.7f;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + box.Height, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
    }

    private static async Task WaitForStudioAsync(IPage page)
    {
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state)");
    }

    /// <summary>Clears any toast covering the corner a panel under test lives in.</summary>
    private static async Task DismissToastsAsync(IPage page) =>
        await page.EvaluateAsync("() => document.querySelectorAll('.etlsql-feedback-toast').forEach(toast => toast.remove())");

    private static async Task DismissOpenOverlaysAsync(IPage page)
    {
        const string selector = ".modal-overlay, [class$=modal-backdrop], .etlsql-cw-overlay, .etlsql-designer-script-overlay";
        var open = await page.EvaluateAsync<int>(
            """
            (sel) => [...document.querySelectorAll(sel)]
                .filter(el => getComputedStyle(el).display !== 'none').length
            """, selector);
        if (open == 0) return;

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(100);

        await page.EvaluateAsync(
            """
            (sel) => [...document.querySelectorAll(sel)]
                .forEach(el => { el.style.display = 'none'; el.classList.remove('open'); el.remove(); })
            """, selector);
    }

    private sealed class StoryInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string[] Fixtures { get; set; } = [];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
