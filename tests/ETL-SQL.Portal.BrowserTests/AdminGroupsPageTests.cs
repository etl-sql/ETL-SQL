using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(AdminUsersCollection.Name)]
public sealed class AdminGroupsPageTests(AdminUsersFixture fixture)
{
    [Fact]
    public async Task Groups_MembershipReceiptsAndOpenedVersionsPersistThroughTheNativePage()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/admin.html");
        await Expect(page.Locator("#userTableWrap")).ToHaveAttributeAsync("data-users-state", "ready");
        await page.Locator(".admin-tab[data-tab='groups']").ClickAsync();
        await Expect(page.Locator("#groupTableWrap")).ToHaveAttributeAsync("data-groups-state", "ready");
        var groupName = "Browser group " + Guid.NewGuid().ToString("N")[..8];
        await page.Locator("#newGroupBtn").ClickAsync();
        await page.Locator("#ng-name").FillAsync(groupName);
        var create = await page.RunAndWaitForResponseAsync(() => page.Locator("#ng-saveBtn").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/admin/groups", StringComparison.Ordinal));
        Assert.Equal(201, create.Status);
        await Expect(page.Locator("#newGroupForm")).ToBeHiddenAsync();
        await page.Locator("#groupFilter").FillAsync(groupName);
        await Expect(page.Locator("#groupTableWrap")).ToContainTextAsync(groupName);
        int groupId;
        using (var scope = fixture.Factory.Services.CreateScope())
            groupId = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Groups
                .Where(group => group.Name == groupName).Select(group => group.Id).SingleAsync();
        var username = "group_member_" + Guid.NewGuid().ToString("N")[..8];
        var userId = await page.EvaluateAsync<int>("""
            async username => {
              const { apiFetch } = await import('/js/api.js');
              const response = await apiFetch('/api/admin/users', { method: 'POST', body: {
                username, email: username + '@example.test', password: 'Str0ng@Passw0rd!', role: 'Viewer'
              } });
              if (response.status !== 201) throw new Error('Member setup failed: ' + response.status);
              return (await response.json()).id;
            }
            """, username);
        await page.Locator($"[data-action='members'][data-id='{groupId}']").ClickAsync();
        await Expect(page.Locator("#memberTableWrap")).ToHaveAttributeAsync("data-members-state", "ready");
        await page.Locator("#addMemberBtn").ClickAsync();
        await page.Locator("#addMemberSearch").FillAsync(username);
        await Expect(page.Locator($"#addMemberSelect option[value='{userId}']")).ToHaveCountAsync(1);
        await page.Locator("#addMemberSelect").SelectOptionAsync(userId.ToString());
        var added = await page.RunAndWaitForResponseAsync(() => page.Locator("#am-saveBtn").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith($"/api/admin/groups/{groupId}/members/bulk-add", StringComparison.Ordinal));
        Assert.Equal(200, added.Status);
        await Expect(page.Locator("#addMemberForm")).ToBeHiddenAsync();
        await Expect(page.Locator("#memberTableWrap")).ToContainTextAsync(username);
        using (var scope = fixture.Factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().UserGroups
                .AnyAsync(link => link.GroupId == groupId && link.UserId == userId));
        await page.Locator($"#memberTableWrap [data-select-id='{userId}']").CheckAsync();
        var removed = await page.RunAndWaitForResponseAsync(async () =>
        {
            await page.Locator("#membersRemoveBtn").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Remove users", Exact = true }).ClickAsync();
        }, response => response.Request.Method == "POST" && response.Url.EndsWith($"/api/admin/groups/{groupId}/members/bulk-remove", StringComparison.Ordinal));
        Assert.Equal(200, removed.Status);
        await Expect(page.Locator("#memberTableWrap")).ToContainTextAsync("No members.");
        await Expect(page.Locator("#groupTableWrap")).ToHaveAttributeAsync("data-groups-state", "ready");
        await Expect(page.Locator("#ng-saveBtn")).ToBeEnabledAsync();
        await page.Locator($"[data-action='edit-group'][data-id='{groupId}']").ClickAsync();
        await page.Locator("#ng-desc").FillAsync("Private group draft");
        long openedVersion;
        using (var scope = fixture.Factory.Services.CreateScope())
            openedVersion = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Groups
                .Where(group => group.Id == groupId).Select(group => group.Version).SingleAsync();
        await page.EvaluateAsync("""
            async ({ groupId, version }) => {
              const { apiFetch } = await import('/js/api.js');
              const response = await apiFetch('/api/admin/groups/' + groupId, {
                method: 'PUT', headers: { 'If-Match': '"' + version + '"' }, body: { description: 'External group change' }
              });
              if (response.status !== 200) throw new Error('External group update failed: ' + response.status);
            }
            """, new { groupId, version = openedVersion });
        await page.Locator("#groupsRefreshBtn").ClickAsync();
        await Expect(page.Locator("#groupTableWrap")).ToContainTextAsync("External group change");
        var conflict = await page.RunAndWaitForResponseAsync(() => page.Locator("#ng-saveBtn").ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.EndsWith($"/api/admin/groups/{groupId}", StringComparison.Ordinal));
        Assert.Equal(409, conflict.Status);
        await Expect(page.Locator("#ng-error")).ToBeVisibleAsync();
        await Expect(page.Locator("#ng-desc")).ToHaveValueAsync("Private group draft");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Equal("External group change", await db.Groups.Where(group => group.Id == groupId).Select(group => group.Description).SingleAsync());
            Assert.False(await db.UserGroups.AnyAsync(link => link.GroupId == groupId && link.UserId == userId));
        }
        await page.Locator("#ng-cancelBtn").ClickAsync();
        await page.Locator($"[data-action='edit-group'][data-id='{groupId}']").ClickAsync();
        await page.Locator("#ng-name").FillAsync(groupName + " renamed");
        var renamed = await page.RunAndWaitForResponseAsync(() => page.Locator("#ng-saveBtn").ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.EndsWith($"/api/admin/groups/{groupId}", StringComparison.Ordinal));
        Assert.Equal(200, renamed.Status);
        await Expect(page.Locator("#memberGroupName")).ToHaveTextAsync(groupName + " renamed");
        await Expect(page.Locator("#memberTableWrap")).ToHaveAttributeAsync("data-members-state", "ready");
        await Expect(page.Locator("#ng-saveBtn")).ToBeEnabledAsync();
        var deleted = await page.RunAndWaitForResponseAsync(async () =>
        {
            await page.Locator($"#groupTableWrap [data-action='delete'][data-id='{groupId}']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Delete group", Exact = true }).ClickAsync();
        }, response => response.Request.Method == "DELETE" && response.Url.EndsWith($"/api/admin/groups/{groupId}", StringComparison.Ordinal));
        Assert.Equal(204, deleted.Status);
        await Expect(page.Locator("#membersPanel")).ToBeHiddenAsync();
        using (var scope = fixture.Factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Groups.AnyAsync(group => group.Id == groupId));
        Assert.Empty(session.PageErrors);
    }
}
