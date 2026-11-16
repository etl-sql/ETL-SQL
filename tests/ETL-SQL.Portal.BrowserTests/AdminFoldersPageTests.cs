using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(AdminUsersCollection.Name)]
public sealed class AdminFoldersPageTests(AdminUsersFixture fixture)
{
    [Fact]
    public async Task Folders_HierarchyPermissionsAndOpenedVersionsPersistThroughTheNativePage()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoWithDiagnosticsAsync("/admin.html");
        await Expect(page.Locator("#userTableWrap")).ToHaveAttributeAsync("data-users-state", "ready");
        await page.Locator(".admin-tab[data-tab='folders']").ClickAsync();
        await Expect(page.Locator("#folderTableWrap")).ToHaveAttributeAsync("data-folders-state", "ready");
        var folderName = "Browser folder " + Guid.NewGuid().ToString("N")[..8];
        await page.Locator("#newFolderBtn").ClickAsync();
        await page.Locator("#nf-name").FillAsync(folderName);
        var create = await page.RunAndWaitForResponseAsync(() => page.Locator("#nf-saveBtn").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/folders", StringComparison.Ordinal));
        Assert.Equal(201, create.Status);
        await Expect(page.Locator("#newFolderForm")).ToBeHiddenAsync();
        await Expect(page.Locator("#folderTableWrap")).ToContainTextAsync("/" + folderName);
        int folderId;
        using (var scope = fixture.Factory.Services.CreateScope())
            folderId = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Folders
                .Where(folder => folder.Name == folderName).Select(folder => folder.Id).SingleAsync();
        await Expect(page.Locator("#nf-saveBtn")).ToBeEnabledAsync();
        await page.Locator("#newFolderBtn").ClickAsync();
        await page.Locator("#nf-name").FillAsync("Nested child");
        await page.Locator("#nf-parent").SelectOptionAsync(folderId.ToString());
        var childCreate = await page.RunAndWaitForResponseAsync(() => page.Locator("#nf-saveBtn").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/folders", StringComparison.Ordinal));
        Assert.Equal(201, childCreate.Status);
        await Expect(page.Locator("#folderTableWrap")).ToContainTextAsync($"/{folderName}/Nested child");
        int childId;
        using (var scope = fixture.Factory.Services.CreateScope())
            childId = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Folders
                .Where(folder => folder.ParentId == folderId).Select(folder => folder.Id).SingleAsync();
        var groupId = await page.EvaluateAsync<int>("""
            async name => {
              const { apiFetch } = await import('/js/api.js');
              const response = await apiFetch('/api/admin/groups', { method: 'POST', body: { name } });
              if (response.status !== 201) throw new Error('ACL group setup failed: ' + response.status);
              return (await response.json()).id;
            }
            """, folderName + " ACL");
        await Expect(page.Locator("#nf-saveBtn")).ToBeEnabledAsync();
        await page.Locator($"[data-action='acl'][data-id='{folderId}']").ClickAsync();
        await Expect(page.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        foreach (var permission in new[] { (Value: "3", Name: "Author"), (Value: "2", Name: "Manage") })
        {
            await Expect(page.Locator("#addAclBtn")).ToBeEnabledAsync();
            await page.Locator("#addAclBtn").ClickAsync();
            await page.Locator("#aclGroupSearch").FillAsync(folderName + " ACL");
            await Expect(page.Locator($"#aclGroupSelect option[value='{groupId}']")).ToHaveCountAsync(1);
            await page.Locator("#aclGroupSelect").SelectOptionAsync(groupId.ToString());
            await page.Locator("#aclPermSelect").SelectOptionAsync(permission.Value);
            var granted = await page.RunAndWaitForResponseAsync(() => page.Locator("#acl-saveBtn").ClickAsync(),
                response => response.Request.Method == "POST" && response.Url.EndsWith($"/api/folders/{folderId}/acl", StringComparison.Ordinal));
            Assert.Equal(200, granted.Status);
            await Expect(page.Locator("#addAclForm")).ToBeHiddenAsync();
            await Expect(page.Locator("#aclTableWrap")).ToContainTextAsync(permission.Name);
            using var scope = fixture.Factory.Services.CreateScope();
            Assert.Equal(int.Parse(permission.Value), (int)await scope.ServiceProvider.GetRequiredService<PortalDbContext>().FolderAcls
                .Where(acl => acl.FolderId == folderId && acl.GroupId == groupId).Select(acl => acl.Permission).SingleAsync());
        }
        await Expect(page.Locator($"#aclTableWrap [data-gid='{groupId}']")).ToBeEnabledAsync();
        var revoked = await page.RunAndWaitForResponseAsync(async () =>
        {
            await page.Locator($"#aclTableWrap [data-gid='{groupId}']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Revoke permission", Exact = true }).ClickAsync();
        }, response => response.Request.Method == "DELETE" && response.Url.EndsWith($"/api/folders/{folderId}/acl/{groupId}", StringComparison.Ordinal));
        Assert.Equal(200, revoked.Status);
        await Expect(page.Locator("#aclTableWrap")).ToContainTextAsync("No permissions set.");
        await Expect(page.Locator("#nf-saveBtn")).ToBeEnabledAsync();
        await page.Locator($"[data-action='edit-folder'][data-id='{folderId}']").ClickAsync();
        await Expect(page.Locator($"#nf-parent option[value='{folderId}'], #nf-parent option[value='{childId}']")).ToHaveCountAsync(0);
        await page.Locator("#nf-name").FillAsync("Private folder draft");
        long openedVersion;
        using (var scope = fixture.Factory.Services.CreateScope())
            openedVersion = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Folders
                .Where(folder => folder.Id == folderId).Select(folder => folder.Version).SingleAsync();
        await page.EvaluateAsync("""
            async ({ folderId, version, name }) => {
              const { apiFetch } = await import('/js/api.js');
              const response = await apiFetch('/api/folders/' + folderId, {
                method: 'PUT', headers: { 'If-Match': '"' + version + '"' }, body: { name, parentId: null }
              });
              if (response.status !== 200) throw new Error('External folder update failed: ' + response.status);
            }
            """, new { folderId, version = openedVersion, name = folderName + " external" });
        await page.Locator("#foldersRefreshBtn").ClickAsync();
        await Expect(page.Locator("#folderTableWrap")).ToContainTextAsync($"/{folderName} external/Nested child");
        await Expect(page.Locator("#aclTableWrap")).ToHaveAttributeAsync("data-acl-state", "ready");
        var conflict = await page.RunAndWaitForResponseAsync(() => page.Locator("#nf-saveBtn").ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.EndsWith($"/api/folders/{folderId}", StringComparison.Ordinal));
        Assert.Equal(409, conflict.Status);
        await Expect(page.Locator("#nf-error")).ToBeVisibleAsync();
        await Expect(page.Locator("#nf-name")).ToHaveValueAsync("Private folder draft");
        await page.Locator("#nf-cancelBtn").ClickAsync();
        await page.Locator($"[data-action='edit-folder'][data-id='{folderId}']").ClickAsync();
        await page.Locator("#nf-name").FillAsync(folderName + " renamed");
        var rename = await page.RunAndWaitForResponseAsync(() => page.Locator("#nf-saveBtn").ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.EndsWith($"/api/folders/{folderId}", StringComparison.Ordinal));
        Assert.Equal(200, rename.Status);
        await Expect(page.Locator("#aclFolderName")).ToHaveTextAsync(folderName + " renamed");
        await Expect(page.Locator("#folderTableWrap")).ToContainTextAsync($"/{folderName} renamed/Nested child");
        await Expect(page.Locator("#nf-saveBtn")).ToBeEnabledAsync();
        var delete = await page.RunAndWaitForResponseAsync(async () =>
        {
            await page.Locator($"#folderTableWrap [data-action='delete'][data-id='{folderId}']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Delete folder", Exact = true }).ClickAsync();
        }, response => response.Request.Method == "DELETE" && response.Url.Contains($"/api/folders/{folderId}?cascade=true", StringComparison.Ordinal));
        Assert.Equal(204, delete.Status);
        await Expect(page.Locator("#aclPanel")).ToBeHiddenAsync();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.False(await db.Folders.AnyAsync(folder => folder.Id == folderId || folder.Id == childId));
            Assert.False(await db.FolderAcls.AnyAsync(acl => acl.FolderId == folderId && acl.GroupId == groupId));
            Assert.Equal(2, await db.AuditLogs.CountAsync(log => log.ResourceId == folderId.ToString() && log.Action == "GRANT_PERMISSION"));
            Assert.Equal(1, await db.AuditLogs.CountAsync(log => log.ResourceId == folderId.ToString() && log.Action == "REVOKE_PERMISSION"));
        }
        Assert.Empty(session.PageErrors);
    }
}
