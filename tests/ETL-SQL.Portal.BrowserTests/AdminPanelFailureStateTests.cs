using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// What the administration panels show when a load fails.
///
/// <para>These are the panels an administrator reads to answer "who can reach this?", and several
/// of them swallowed the failure and left the previous answer on screen. That is worse than an
/// error: the panel is not blank, it is <em>confidently wrong</em>, and it is wrong about access
/// control specifically. An empty list at least prompts someone to look again.</para>
///
/// <para>Each test forces the panel's own request to fail while leaving the rest of the page
/// working, because that is the shape the real failure takes — one call rejected, everything else
/// fine.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class AdminPanelFailureStateTests(PortalBrowserFixture fixture)
{
    /// <summary>
    /// Opening one folder's permissions, then another's while the second load fails, must not leave
    /// the first folder's grants displayed under the second folder's name.
    ///
    /// <para>The heading is set before the load and unconditionally; the table was only written on
    /// success. So the panel could attribute one folder's access-control list to a different
    /// folder — and the Revoke buttons still carried the first folder's group ids while the revoke
    /// call sent the second folder's id.</para>
    /// </summary>
    [Fact]
    public async Task FolderAcl_DoesNotShowAnotherFoldersGrants_WhenTheLoadFails()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        var first = $"acl-first-{Guid.NewGuid():N}"[..18];
        var second = $"acl-second-{Guid.NewGuid():N}"[..18];
        await CreateFolderAsync(page, first);
        await CreateFolderAsync(page, second);

        await page.GotoAsync("/admin.html");
        await page.ClickAsync("[data-tab='folders']");
        await page.WaitForTimeoutAsync(600);

        await OpenAclAsync(page, first);
        var firstMarkup = await page.Locator("#aclTableWrap").InnerHTMLAsync();

        // Now break only the ACL read, and open the other folder's permissions.
        await page.RouteAsync("**/api/folders/*/acl", route => route.AbortAsync());
        await OpenAclAsync(page, second);
        await page.WaitForTimeoutAsync(600);

        await Expect(page.Locator("#aclFolderName")).ToContainTextAsync(second);
        var afterMarkup = await page.Locator("#aclTableWrap").InnerHTMLAsync();

        Assert.False(afterMarkup == firstMarkup && firstMarkup.Contains("<tr", StringComparison.Ordinal),
            $"The permissions panel still shows '{first}' grants under the heading '{second}'. "
            + "An administrator reading this is told the wrong folder's access-control list, and "
            + "the Revoke buttons carry the other folder's group ids.");

        // "No permissions set" would be the opposite claim: that this folder is ungoverned.
        Assert.DoesNotContain("No permissions set.", afterMarkup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-portal-state=\"failed\"", afterMarkup, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failed group-membership read must not render as a group with no members. "Nobody is in
    /// this group" and "we could not find out" lead an administrator to opposite actions.
    /// </summary>
    [Fact]
    public async Task GroupMembers_DoesNotRenderAFailedReadAsAnEmptyGroup()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        // Seeded rather than assumed: an early return on "no groups exist" would let this test
        // pass without ever reaching its assertion.
        var group = $"grp{Guid.NewGuid():N}"[..12];
        await CreateGroupAsync(page, group);

        await page.GotoAsync("/admin.html");
        await page.ClickAsync("[data-tab='groups']");
        await page.WaitForTimeoutAsync(800);

        // `*` does not cross a '/' in a Playwright glob, so `members*` never matches
        // `members/catalog` — the first version of this aborted nothing and asserted against a
        // successful empty read.
        await page.RouteAsync("**/api/admin/groups/*/members/catalog*", route => route.AbortAsync());
        await page.Locator($"tr:has-text('{group}') [data-action='members']").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var markup = await page.Locator("#memberTableWrap").InnerHTMLAsync();

        Assert.DoesNotContain("No members", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-portal-state=\"failed\"", markup, StringComparison.Ordinal);
    }

    private async Task CreateGroupAsync(IPage page, string name)
    {
        var response = await page.APIRequest.PostAsync(
            $"{fixture.BaseUrl}/api/admin/groups",
            new APIRequestContextOptions
            {
                Headers = await BearerAsync(page),
                DataObject = new { name, description = "failure-state fixture" }
            });
        Assert.True(response.Ok, $"Could not seed group '{name}': {response.Status}");
    }

    private async Task CreateFolderAsync(IPage page, string name)
    {
        var response = await page.APIRequest.PostAsync(
            $"{fixture.BaseUrl}/api/folders",
            new APIRequestContextOptions
            {
                Headers = await BearerAsync(page),
                DataObject = new { name, parentId = (int?)null }
            });
        Assert.True(response.Ok, $"Could not seed folder '{name}': {response.Status}");
    }

    private static async Task OpenAclAsync(IPage page, string folderName)
    {
        await page.Locator($"tr:has-text('{folderName}') [data-action='acl']").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    private static async Task<Dictionary<string, string>> BearerAsync(IPage page)
    {
        var token = await page.EvaluateAsync<string>("() => sessionStorage.getItem('etlsql_token')");
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }
}
