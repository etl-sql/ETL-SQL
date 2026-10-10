using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class AdminAuditCatalogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogAndCsvRetainEventsWithoutATenantAccount(bool export)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        string token;
        int[] ids;
        const string action = "AUDIT_ACTOR_VISIBILITY";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin") ?? throw new InvalidOperationException("Seeded admin was not found.");
            admin.MustChangePassword = false;
            var foreign = new PortalUser { TenantId = "other-tenant", UserName = "foreign-account-marker" };
            db.Users.Add(foreign);
            await db.SaveChangesAsync();
            var timestamp = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
            var events = new[]
            {
                new AuditLog { UserId = admin.Id, Action = action, Detail = "known-account-event", Timestamp = timestamp },
                new AuditLog { UserId = null, ActorType = "System", Action = action, Detail = "system-event", Timestamp = timestamp },
                new AuditLog { UserId = foreign.Id, Action = action, Detail = "foreign-account-event", Timestamp = timestamp },
                new AuditLog { UserId = int.MaxValue, Action = action, Detail = "removed-account-event", Timestamp = timestamp }
            };
            db.AuditLogs.AddRange(events);
            await db.SaveChangesAsync();
            ids = events.Select(item => item.Id).OrderDescending().ToArray();
            token = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(admin, await users.GetRolesAsync(admin));
        }
        var url = export ? $"/api/admin/audit/export/csv?action={action}" : $"/api/admin/audit?action={action}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (export)
        {
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
            var csv = await response.Content.ReadAsStringAsync();
            foreach (var marker in new[] { "known-account-event", "system-event", "foreign-account-event", "removed-account-event" })
                Assert.Contains(marker, csv, StringComparison.Ordinal);
            Assert.DoesNotContain("foreign-account-marker", csv, StringComparison.Ordinal);
        }
        else
        {
            var page = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogDto>>();
            Assert.NotNull(page);
            Assert.Equal(4, page.Total);
            Assert.Equal(4, page.Items.Count);
            Assert.Equal(ids, page.Items.Select(item => item.Id).ToArray());
            Assert.Equal("admin", Assert.Single(page.Items, item => item.Detail == "known-account-event").Username);
            Assert.All(page.Items.Where(item => item.Detail != "known-account-event"), item => Assert.Null(item.Username));
        }
    }

    [Theory]
    [InlineData(-100, 1)]
    [InlineData(1, 1)]
    [InlineData(999, 4)]
    [InlineData(int.MaxValue, 4)]
    public async Task PagingClampsSafelyAndKeepsStableFilteredRows(int requestedPage, int expectedPage)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminId = await AuthenticateAsync(factory, client);
        int[] expectedIds;
        const string action = "AUDIT_PAGING";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var entries = Enumerable.Range(0, 7).Select(index => new AuditLog
            {
                UserId = index == 0 ? null : adminId,
                Action = action,
                Timestamp = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc),
                ResourceType = index % 2 == 0 ? "Report" : "Row",
                ResourceId = index < 5 ? "selected" : "other"
            }).ToArray();
            db.AuditLogs.AddRange(entries);
            db.AuditLogs.Add(new AuditLog { TenantId = "other-tenant", UserId = adminId, Action = action, Detail = "foreign-event" });
            db.AuditLogs.Add(new AuditLog { UserId = adminId, Action = "OTHER_ACTION", ResourceType = "Report", ResourceId = "selected" });
            await db.SaveChangesAsync();
            expectedIds = entries.Select(entry => entry.Id).OrderDescending().ToArray();
        }
        var page = await client.GetFromJsonAsync<PagedResult<AuditLogDto>>($"/api/admin/audit?action={action}&page={requestedPage}&pageSize=2");
        Assert.NotNull(page);
        Assert.Equal(7, page.Total);
        Assert.Equal(expectedPage, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(expectedIds.Skip((expectedPage - 1) * 2).Take(2), page.Items.Select(entry => entry.Id));

        var filtered = await client.GetFromJsonAsync<PagedResult<AuditLogDto>>(
            $"/api/admin/audit?action={action}&userId={adminId}&resourceType=Report&resourceId=selected&pageSize=500");
        Assert.NotNull(filtered);
        Assert.Equal(200, filtered.PageSize);
        Assert.Equal(2, filtered.Total);
        Assert.Equal(2, filtered.Items.Count);
        Assert.All(filtered.Items, entry =>
        {
            Assert.Equal(adminId, entry.UserId);
            Assert.Equal("Report", entry.ResourceType);
            Assert.Equal("selected", entry.ResourceId);
        });
        var empty = await client.GetFromJsonAsync<PagedResult<AuditLogDto>>("/api/admin/audit?action=NO_SUCH_ACTION&page=999&pageSize=0");
        Assert.NotNull(empty);
        Assert.Equal(0, empty.Total);
        Assert.Equal(1, empty.Page);
        Assert.Equal(1, empty.PageSize);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task CsvReturnsTheNewestBoundedWindowAndAuditsTheExport()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminId = await AuthenticateAsync(factory, client);
        const string action = "AUDIT_CSV_WINDOW";
        int[] ids;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var entries = Enumerable.Range(0, 10_001).Select(index => new AuditLog
            {
                UserId = null,
                ActorType = "System",
                Action = action,
                Timestamp = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc),
                Detail = "Window evidence " + index
            }).ToArray();
            db.AuditLogs.AddRange(entries);
            await db.SaveChangesAsync();
            ids = entries.Select(entry => entry.Id).OrderDescending().ToArray();
        }
        using var response = await client.GetAsync($"/api/admin/audit/export/csv?action={action}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd().Split('\n');
        Assert.Equal(10_001, lines.Length);
        Assert.Equal(ids.Take(10_000), lines.Skip(1).Select(line => int.Parse(line.Split(',')[0])));
        await using var verification = factory.Services.CreateAsyncScope();
        var audit = await verification.ServiceProvider.GetRequiredService<PortalDbContext>().AuditLogs
            .SingleAsync(entry => entry.Action == "EXPORT_AUDIT_LOG");
        Assert.Equal(adminId, audit.UserId);
        Assert.Contains(action, audit.Detail);
    }

    private static async Task<int> AuthenticateAsync(PortalWebFactory factory, HttpClient client)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var admin = await users.FindByNameAsync("admin") ?? throw new InvalidOperationException("Seeded admin was not found.");
        admin.MustChangePassword = false;
        await scope.ServiceProvider.GetRequiredService<PortalDbContext>().SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(admin, await users.GetRolesAsync(admin));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return admin.Id;
    }
}
