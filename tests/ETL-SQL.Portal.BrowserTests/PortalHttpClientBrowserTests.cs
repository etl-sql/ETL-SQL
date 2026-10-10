using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(PortalAdminCollection.Name)]
public sealed class PortalHttpClientBrowserTests(PortalAdminFixture fixture)
{
    [Fact]
    public async Task LogoutWithAnExpiredAccessToken_StillInvalidatesServerSessions()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/docs.html", new() { WaitUntil = WaitUntilState.NetworkIdle });
        string? previousStamp;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            previousStamp = await db.Users.Where(user => user.UserName == PortalBrowserFixture.AdminUsername)
                .Select(user => user.SecurityStamp).SingleAsync();
        }
        var refreshes = 0;
        page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/auth/refresh", StringComparison.Ordinal))
                Interlocked.Increment(ref refreshes);
        };
        await page.EvaluateAsync("""
            async () => {
              const { authApi } = await import('/js/api.js');
              sessionStorage.setItem('etlsql_token', 'expired.access.token');
              void authApi.logout();
            }
            """);
        await page.WaitForURLAsync("**/login.html");
        Assert.Equal(1, refreshes);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => sessionStorage.getItem('etlsql_token') === null && sessionStorage.getItem('etlsql_refresh') === null
            """));
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync(user => user.UserName == PortalBrowserFixture.AdminUsername);
            Assert.True(previousStamp != user.SecurityStamp, "Logout did not invalidate the account's security stamp.");
            Assert.True(await db.RefreshTokens.Where(token => token.UserId == user.Id).AllAsync(token => token.RevokedAt != null),
                "Logout left a server refresh session active.");
        }
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConcurrentExpiredRequests_RefreshOnceAndRetryNativeInputs()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/docs.html", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var refreshes = 0;
        page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/auth/refresh", StringComparison.Ordinal))
                Interlocked.Increment(ref refreshes);
        };
        var result = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const { apiFetch, auth } = await import('/js/api.js');
              sessionStorage.setItem('etlsql_token', 'expired.access.token');
              const responses = await Promise.all([
                apiFetch(new URL('/api/folders', location.origin), { headers: new Headers({ 'X-HTTP-Client': 'url' }) }),
                apiFetch(new Request(new URL('/api/folders', location.origin), { headers: { 'X-HTTP-Client': 'request' } }))
              ]);
              return { statuses: responses.map(response => response.status),
                refreshed: !!auth.getToken() && auth.getToken() !== 'expired.access.token' };
            }
            """);
        Assert.Equal([200, 200], result.GetProperty("statuses").EnumerateArray().Select(value => value.GetInt32()).ToArray());
        Assert.True(result.GetProperty("refreshed").GetBoolean());
        Assert.Equal(1, refreshes);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task NativeHeaders_PreserveMutationVersionAndRequestCredentials()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/docs.html", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var folderName = $"HTTP client {Guid.NewGuid():N}";
        var mutation = await page.EvaluateAsync<JsonElement>("""
            async name => {
              const { foldersApi, apiFetch } = await import('/js/api.js');
              const folder = await foldersApi.create(name, null);
              const response = await apiFetch(`/api/folders/${folder.id}`, { method: 'PUT',
                headers: new Headers({ 'If-Match': `"${folder.version}"` }), body: { name: name + ' updated', parentId: null } });
              return { status: response.status, updated: response.ok ? (await response.json()).name : null };
            }
            """, folderName);
        Assert.Equal(200, mutation.GetProperty("status").GetInt32());
        Assert.Equal(folderName + " updated", mutation.GetProperty("updated").GetString());

        var observed = page.WaitForRequestAsync(request => request.Url.EndsWith("/api/folders?httpClient=explicit", StringComparison.Ordinal));
        var status = await page.EvaluateAsync<int>("""
            async () => {
              const request = new Request(new URL('/api/folders?httpClient=explicit', location.origin), {
                headers: { Authorization: 'Bearer fixture-explicit', 'If-Match': '"3"' } });
              return (await window.fetch(request)).status;
            }
            """);
        var request = await observed;
        Assert.Equal(401, status);
        Assert.Equal("Bearer fixture-explicit", await request.HeaderValueAsync("Authorization"));
        Assert.Equal("\"3\"", await request.HeaderValueAsync("If-Match"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task RefreshCompletingAfterLocalSignOut_DoesNotRestoreTheSession()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/docs.html", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/auth/refresh", async route =>
        {
            started.TrySetResult();
            await release.Task;
            var response = await route.FetchAsync();
            await route.FulfillAsync(new() { Response = response });
        });
        var pending = page.EvaluateAsync<bool>("""
            async () => {
              const { apiFetch } = await import('/js/api.js');
              sessionStorage.setItem('etlsql_token', 'expired.access.token');
              try { await apiFetch('/api/folders'); return false; }
              catch (error) { return /session changed/i.test(error.message); }
            }
            """);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await page.EvaluateAsync("async () => (await import('/js/api.js')).auth.clear()");
        }
        finally { release.TrySetResult(); }
        Assert.True(await pending);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => sessionStorage.getItem('etlsql_token') === null && sessionStorage.getItem('etlsql_refresh') === null
            """));
        Assert.EndsWith("/docs.html", page.Url, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }
}
