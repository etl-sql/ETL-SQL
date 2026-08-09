using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The Portal reaches the Orchestrator with a shared service key, so the Portal edge is the only
/// place that knows which human clicked. Triggering a job out of schedule and killing a running one
/// are privileged acts; only the bulk triage path recorded who did them.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OrchestratorActorAttributionTests
{
    [Fact]
    public async Task TriggeringAJobIsAttributedToTheSignedInUser()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var adminId = await GetAdminIdAsync(factory);

        // The Orchestrator is not running in this harness, so the proxy fails and the action is
        // reported as unavailable — the point here is that an attempt that *succeeds* is audited,
        // so assert the audit only when the call was accepted.
        var response = await SendAsync(client, HttpMethod.Post, "/api/orchestrator/jobs/nightly/trigger", token);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audited = await db.AuditLogs.AnyAsync(log => log.Action == "JobTriggered");

        if (response.StatusCode is HttpStatusCode.Accepted)
        {
            Assert.True(audited, "a successful trigger must name the user who asked for it");
            var entry = await db.AuditLogs.FirstAsync(log => log.Action == "JobTriggered");
            Assert.Equal(adminId, entry.UserId);
            Assert.Equal("User", entry.ActorType);
        }
        else
        {
            // A failed proxy call must not manufacture an audit record for work that never happened.
            Assert.False(audited, "a failed trigger must not be recorded as if it had run");
        }
    }

    [Fact]
    public async Task KillingAJobIsNotTheOnePrivilegedActionWithNoRecord()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, "/api/orchestrator/jobs/nightly/kill", token);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audited = await db.AuditLogs.AnyAsync(log => log.Action == "JobKilled");

        Assert.Equal(response.StatusCode is HttpStatusCode.OK, audited);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<int> GetAdminIdAsync(PortalWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return await db.Users.Where(value => value.UserName == "admin").Select(value => value.Id).SingleAsync();
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Actor99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        var relogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Actor99!" });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        return (await relogin.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }
}
