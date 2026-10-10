using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(AdminUsersCollection.Name)]
public sealed class AdminUsersPageTests(AdminUsersFixture fixture)
{
    [Fact]
    public async Task Users_ServerRolesAndUpdatedVersionsPersistThroughProfileAndPasswordChanges()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/admin.html");
        await Expect(page.Locator("#userTableWrap")).ToHaveAttributeAsync("data-users-state", "ready");
        await page.Locator("#newUserBtn").ClickAsync();
        await Expect(page.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        await Expect(page.Locator("#nu-role option[value='StewardshipManager']")).ToHaveCountAsync(1);
        var username = "browser_steward_" + Guid.NewGuid().ToString("N")[..8];
        await page.Locator("#nu-username").FillAsync(username);
        await page.Locator("#nu-email").FillAsync(username + "@example.test");
        await page.Locator("#nu-password").FillAsync("Str0ng@Passw0rd!");
        await page.Locator("#nu-role").SelectOptionAsync("DataSteward");
        var created = await page.RunAndWaitForResponseAsync(() => page.Locator("#nu-saveBtn").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/admin/users", StringComparison.Ordinal));
        Assert.Equal(201, created.Status);
        await Expect(page.Locator("#newUserForm")).ToBeHiddenAsync();
        await Expect(page.Locator("#nu-password")).ToHaveValueAsync("");
        await page.Locator("#userFilter").FillAsync(username);
        await Expect(page.Locator("#userTableWrap tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("#userTableWrap")).ToContainTextAsync(username);
        int id;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync(user => user.UserName == username);
            id = user.Id;
            Assert.Equal("DataSteward", await db.UserRoles.Where(link => link.UserId == id)
                .Join(db.Roles, link => link.RoleId, role => role.Id, (_, role) => role.Name).SingleAsync());
        }
        await page.Locator($"[data-action='edit-user'][data-id='{id}']").ClickAsync();
        await Expect(page.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        await Expect(page.Locator("#nu-role")).ToHaveValueAsync("DataSteward");
        await page.Locator("#nu-role").SelectOptionAsync("StewardshipManager");
        await page.Locator("#nu-email").FillAsync("changed@example.test");
        await page.Locator("#nu-password").FillAsync("An0ther@Password!");
        var reset = await page.RunAndWaitForResponseAsync(() => page.Locator("#nu-saveBtn").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith($"/api/admin/users/{id}/reset-password", StringComparison.Ordinal));
        Assert.Equal(204, reset.Status);
        await Expect(page.Locator("#newUserForm")).ToBeHiddenAsync();
        await Expect(page.Locator("#userTableWrap")).ToContainTextAsync("StewardshipManager");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync(user => user.Id == id);
            Assert.Equal("changed@example.test", user.Email);
            Assert.Equal("StewardshipManager", await db.UserRoles.Where(link => link.UserId == id)
                .Join(db.Roles, link => link.RoleId, role => role.Id, (_, role) => role.Name).SingleAsync());
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            Assert.True(await manager.CheckPasswordAsync(user, "An0ther@Password!"));
            Assert.True(user.MustChangePassword);
            Assert.Equal(1, await db.AuditLogs.CountAsync(log => log.Action == "RESET_PASSWORD" && log.ResourceId == id.ToString()));
        }
        await page.Locator($"[data-select-id='{id}']").CheckAsync();
        var bulk = await page.RunAndWaitForResponseAsync(() => page.Locator("#usersDisableBtn").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/admin/users/bulk-status", StringComparison.Ordinal));
        Assert.Equal(200, bulk.Status);
        await Expect(page.Locator("#userTableWrap")).ToContainTextAsync("Inactive");
        await Expect(page.Locator("#usersMutationStatus")).ToBeHiddenAsync();
        await page.Locator($"[data-action='toggle'][data-id='{id}']").ClickAsync();
        await Expect(page.Locator("#userTableWrap")).ToContainTextAsync("Active");
        await Expect(page.Locator("#userTableWrap")).ToHaveAttributeAsync("data-users-state", "ready");
        await page.Locator($"[data-action='edit-user'][data-id='{id}']").ClickAsync();
        await Expect(page.Locator("#nu-saveBtn")).ToBeEnabledAsync();
        await page.Locator("#nu-email").FillAsync("private-draft@example.test");
        long openedVersion;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            openedVersion = await db.Users.Where(user => user.Id == id).Select(user => user.Version).SingleAsync();
        }
        await page.EvaluateAsync("""
            async ({ id, version }) => {
              const { apiFetch } = await import('/js/api.js');
              const response = await apiFetch('/api/admin/users/' + id, {
                method: 'PUT', headers: { 'If-Match': '"' + version + '"' }, body: { email: 'external@example.test' }
              });
              if (response.status !== 200) throw new Error('External profile update failed: ' + response.status);
            }
            """, new { id, version = openedVersion });
        var conflict = await page.RunAndWaitForResponseAsync(() => page.Locator("#nu-saveBtn").ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.EndsWith($"/api/admin/users/{id}", StringComparison.Ordinal));
        Assert.Equal(409, conflict.Status);
        await Expect(page.Locator("#newUserForm")).ToBeVisibleAsync();
        await Expect(page.Locator("#nu-email")).ToHaveValueAsync("private-draft@example.test");
        await Expect(page.Locator("#nu-error")).ToBeVisibleAsync();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Equal("external@example.test", await db.Users.Where(user => user.Id == id).Select(user => user.Email).SingleAsync());
        }
        Assert.Empty(session.PageErrors);
    }
}

public sealed class AdminUsersFixture() : PortalBrowserFixture(new PortalBrowserFactory());

[CollectionDefinition(Name)]
public sealed class AdminUsersCollection : ICollectionFixture<AdminUsersFixture>
{
    public const string Name = "AdminUsers";
}
