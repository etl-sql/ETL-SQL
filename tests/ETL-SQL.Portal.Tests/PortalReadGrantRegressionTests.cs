using System.Net;
using System.Net.Http.Headers;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Read surfaces must resolve access the way <see cref="FolderPermissionService"/> enforces it:
/// folder ownership and per-report grants count, not only group folder ACLs.
/// </summary>
[Trait("Category", "Portal")]
public sealed class PortalReadGrantRegressionTests
{
    [Fact]
    public async Task FolderOwnerWithoutAGroupGrant_SeesTheFolderInTreeAndCatalogSearch()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var folderName = $"owned-{Guid.NewGuid():N}";
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var (owner, ownerToken) = await CreateUserAsync(scope.ServiceProvider, "Publisher");
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Folders.Add(new Folder { Name = folderName, Path = "/" + folderName, OwnerId = owner.Id });
            await db.SaveChangesAsync();
            token = ownerToken;
        }

        using var tree = await GetAsync(client, token, "/api/folders");
        Assert.Equal(HttpStatusCode.OK, tree.StatusCode);
        Assert.Contains(folderName, await tree.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var search = await GetAsync(client, token, $"/api/catalog/search?q={folderName}");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Contains(folderName, await search.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerReportGranteeIsNotRefusedExport()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        int reportId;
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var (viewer, viewerToken) = await CreateUserAsync(scope.ServiceProvider, "Viewer");
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = db.Users.Single(user => user.UserName == "admin");
            var report = new Report
            {
                Folder = new Folder { Name = "Granted", Path = $"/granted-{Guid.NewGuid():N}", OwnerId = admin.Id },
                Name = "Granted report",
                ScriptPath = "granted/report.rptsql",
                CreatedBy = admin.Id
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            db.ReportAcls.Add(new ReportAcl
            {
                ReportId = report.Id,
                UserId = viewer.Id,
                Permission = FolderPermission.Read
            });
            await db.SaveChangesAsync();
            reportId = report.Id;
            token = viewerToken;
        }

        // The grant passes the permission gate; the report then has nothing to export, which is a
        // 404 — not the 403 a grantee used to receive before the snapshot was ever looked at.
        using var export = await GetAsync(client, token, $"/api/reports/{reportId}/export/csv");
        Assert.Equal(HttpStatusCode.NotFound, export.StatusCode);
    }

    private static async Task<(PortalUser User, string Token)> CreateUserAsync(
        IServiceProvider services, string role)
    {
        var users = services.GetRequiredService<UserManager<PortalUser>>();
        var user = new PortalUser
        {
            UserName = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@test.local",
            IsActive = true,
            MustChangePassword = false
        };
        Assert.True((await users.CreateAsync(user, "Grant@Test99!")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var token = services.GetRequiredService<TokenService>().GenerateJwt(user, await users.GetRolesAsync(user));
        return (user, token);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string token, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
