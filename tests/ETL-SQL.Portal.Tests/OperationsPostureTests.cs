using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Backup custody, the restore itself, and host enrolment stay outside the running Portal — they own
/// key material and an OS-protected bootstrap the Portal deliberately does not have. What the Portal
/// can do is notice when the evidence they leave behind is missing, stale, or inconsistent, and say
/// what to run about it. These cover that reading.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OperationsPostureTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task WithNoEvidenceAtAll_SaysSoAndNamesTheCommandToRun()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var posture = await PostureAsync(client, adminToken);

        var backup = posture["backup"]!.AsObject();
        Assert.False(backup["everRun"]!.GetValue<bool>());
        Assert.False(backup["fresh"]!.GetValue<bool>());
        Assert.Contains("etl-sql admin backup",
            backup["remediation"]!.GetValue<string>(), StringComparison.Ordinal);

        // A backup nobody has restored is a hope, not a recovery plan — so this is a finding, not a blank.
        var drill = posture["restoreDrill"]!.AsObject();
        Assert.False(drill["everRun"]!.GetValue<bool>());
        Assert.Contains(drill["findings"]!.AsArray().Select(f => f!.GetValue<string>()),
            finding => finding.Contains("proven readable", StringComparison.Ordinal));
        Assert.Contains("--validate", drill["remediation"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAFreshBackupAsFresh_AndAStaleOneWithItsAge()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        await RecordBackupAsync(factory, "success", DateTime.UtcNow.AddHours(-1));
        var fresh = (await PostureAsync(client, adminToken))["backup"]!.AsObject();
        Assert.True(fresh["everRun"]!.GetValue<bool>());
        Assert.True(fresh["fresh"]!.GetValue<bool>());
        Assert.Empty(fresh["findings"]!.AsArray());

        // The freshness policy travels with the reading, so the number can be interpreted.
        var maxAge = fresh["maxAgeHours"]!.GetValue<int>();
        await RecordBackupAsync(factory, "success", DateTime.UtcNow.AddHours(-(maxAge + 5)));

        var stale = (await PostureAsync(client, adminToken))["backup"]!.AsObject();
        Assert.False(stale["fresh"]!.GetValue<bool>());
        Assert.True(stale["ageHours"]!.GetValue<int>() > maxAge);
        Assert.Contains(stale["findings"]!.AsArray().Select(f => f!.GetValue<string>()),
            finding => finding.Contains("freshness policy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsAFailedBackupEvenWhenItIsRecent()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        // Recent but failed is the trap: an age check alone would call this healthy.
        await RecordBackupAsync(factory, "failed", DateTime.UtcNow.AddMinutes(-5), exitCode: "3");

        var backup = (await PostureAsync(client, adminToken))["backup"]!.AsObject();
        Assert.False(backup["fresh"]!.GetValue<bool>());
        Assert.Equal("3", backup["lastExitCode"]!.GetValue<string>());
        Assert.Contains(backup["findings"]!.AsArray().Select(f => f!.GetValue<string>()),
            finding => finding.Contains("did not succeed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsRestoreDrillEvidenceAndItsAge()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        await RecordRestoreAsync(factory, "validate", "success", DateTime.UtcNow.AddDays(-7), problems: 0);
        var recent = (await PostureAsync(client, adminToken))["restoreDrill"]!.AsObject();
        Assert.True(recent["everRun"]!.GetValue<bool>());
        Assert.Equal("validate", recent["mode"]!.GetValue<string>());
        Assert.Equal(0, recent["problems"]!.GetValue<int>());
        Assert.Empty(recent["findings"]!.AsArray());

        await RecordRestoreAsync(factory, "validate", "success",
            DateTime.UtcNow.AddDays(-(OperationsPostureService.RestoreDrillMaxAgeDays + 10)), problems: 0);

        var stale = (await PostureAsync(client, adminToken))["restoreDrill"]!.AsObject();
        Assert.Contains(stale["findings"]!.AsArray().Select(f => f!.GetValue<string>()),
            finding => finding.Contains("days ago", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithNoEnrollment_ReportsItAndKeepsRemediationOnTheHost()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var enrollment = (await PostureAsync(client, adminToken))["hostEnrollment"]!.AsObject();

        Assert.False(enrollment["hostEnrolled"]!.GetValue<bool>());
        Assert.False(enrollment["consistent"]!.GetValue<bool>());
        // Enrollment owns an OS-protected bootstrap, so the remedy is a host command, not a button.
        Assert.Contains("etl-sql enterprise enroll",
            enrollment["remediation"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IsAdministratorOnly()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await CreateViewerAsync(client, adminToken, $"ops_deny_{suffix}");
        var viewerToken = await LoginAsync(client, $"ops_deny_{suffix}", "Ready@Test2!");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken, "/api/admin/operations/posture")).StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task RecordBackupAsync(
        PortalWebFactory factory, string status, DateTime at, string exitCode = "0")
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(OperationsPostureService.BackupJobStateName, "last_backup_status", status);
        await store.SetJobStateAsync(OperationsPostureService.BackupJobStateName, "last_backup_at", at.ToString("o"));
        await store.SetJobStateAsync(OperationsPostureService.BackupJobStateName, "last_backup_exit_code", exitCode);
    }

    private static async Task RecordRestoreAsync(
        PortalWebFactory factory, string mode, string status, DateTime at, int problems)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        var job = OperationsPostureService.RestoreJobStateName;
        await store.SetJobStateAsync(job, "last_restore_mode", mode);
        await store.SetJobStateAsync(job, "last_restore_status", status);
        await store.SetJobStateAsync(job, "last_restore_at", at.ToString("o"));
        await store.SetJobStateAsync(job, "last_restore_exit_code", status == "success" ? "0" : "1");
        await store.SetJobStateAsync(job, "last_restore_problems", problems.ToString());
    }

    private static async Task<JsonObject> PostureAsync(HttpClient client, string adminToken)
    {
        var response = await AuthGet(client, adminToken, "/api/admin/operations/posture");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task CreateViewerAsync(HttpClient client, string adminToken, string username)
    {
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var initial = await LoginAsync(client, username, "Initial@Test1!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url) =>
        SendAsync(client, HttpMethod.Get, token, url, null);

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body) =>
        SendAsync(client, HttpMethod.Post, token, url, body);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
