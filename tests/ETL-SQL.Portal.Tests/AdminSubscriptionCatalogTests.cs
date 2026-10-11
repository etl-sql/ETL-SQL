using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The administrator's subscription catalog pages and searches on the server. Recipients are
/// encrypted at rest, so neither may be left to the database for that column: it sorted and
/// matched ciphertext, which paged in an arbitrary order and never found a recipient.
/// </summary>
[Trait("Category", "Portal")]
public sealed class AdminSubscriptionCatalogTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PagesInNameOrderAndFindsASubscriptionByItsRecipient()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin") ?? throw new InvalidOperationException("Seeded admin is missing.");
            admin.MustChangePassword = false;
            var folder = new Folder { Name = "Catalog fixture", Path = "/catalog-fixture" };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            var report = new Report { Name = "Catalog fixture", ScriptPath = "catalog-fixture.rptsql", FolderId = folder.Id, CreatedBy = admin.Id };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            // Saved in reverse so insertion order cannot stand in for the sort.
            foreach (var index in Enumerable.Range(1, 12).Reverse())
            {
                db.Subscriptions.Add(new Subscription
                {
                    ReportId = report.Id,
                    UserId = admin.Id,
                    SmtpAlias = "mail",
                    Schedule = "Daily",
                    Name = $"Delivery {index:D2}",
                    Recipients = $"reader{index:D2}@example.test"
                });
                await db.SaveChangesAsync();
            }
            // The premise: what the database holds for a recipient is not the address.
            var stored = await db.Database.SqlQueryRaw<string>("SELECT recipients AS \"Value\" FROM subscriptions").ToListAsync();
            Assert.DoesNotContain(stored, value => value.Contains("example.test", StringComparison.OrdinalIgnoreCase));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(admin, await users.GetRolesAsync(admin)));
        }

        var first = await PageAsync(client, "q=Catalog%20fixture&page=1&pageSize=5");
        var second = await PageAsync(client, "q=Catalog%20fixture&page=2&pageSize=5");
        Assert.Equal(12, first.Total);
        Assert.Equal(["Delivery 01", "Delivery 02", "Delivery 03", "Delivery 04", "Delivery 05"], first.Items.Select(item => item.Name));
        Assert.Equal(["Delivery 06", "Delivery 07", "Delivery 08", "Delivery 09", "Delivery 10"], second.Items.Select(item => item.Name));

        var byRecipient = await PageAsync(client, "q=READER07%40example");
        Assert.Equal("Delivery 07", Assert.Single(byRecipient.Items).Name);
        Assert.Equal(1, byRecipient.Total);
        Assert.Empty((await PageAsync(client, "q=nobody%40example")).Items);
    }

    private static async Task<PagedResult<SubscriptionDto>> PageAsync(HttpClient client, string query) =>
        await client.GetFromJsonAsync<PagedResult<SubscriptionDto>>($"/api/admin/subscriptions/catalog?{query}", Json)
        ?? throw new InvalidOperationException("The catalog answered with no body.");
}
