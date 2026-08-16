using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.Portal.Models;
using Xunit;

namespace ETL_SQL.Portal.Tests;

public class OrchestratorAdminUiIntegrationTests : IClassFixture<OrchestratorGrantAdministrationFixture>
{
    private readonly OrchestratorGrantAdministrationFixture _fixture;

    public OrchestratorAdminUiIntegrationTests(OrchestratorGrantAdministrationFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _fixture.Client.SendAsync(request);
    }

    [Fact]
    public async Task ScheduleCatalogCrudLifecycle()
    {
        const string scheduleName = "test_sched_nightly_admin";

        // 1. Create schedule
        using (var createRes = await SendAsync(HttpMethod.Post, "/api/orchestrator/schedules", _fixture.AdminToken, new
        {
            name = scheduleName,
            displayName = "Test Nightly Admin Schedule",
            description = "Runs every night at 2am",
            cron = "0 2 * * *",
            timeZone = "UTC",
            isEnabled = true
        }))
        {
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        }

        // 2. List schedules and verify present
        using (var listRes = await SendAsync(HttpMethod.Get, "/api/orchestrator/schedules", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
            var schedules = await listRes.Content.ReadFromJsonAsync<List<ScheduleDefinitionDto>>();
            Assert.NotNull(schedules);
            Assert.Contains(schedules, s => s.Name == scheduleName && s.DisplayName == "Test Nightly Admin Schedule");
        }

        // 3. Update schedule
        using (var updateRes = await SendAsync(HttpMethod.Put, $"/api/orchestrator/schedules/{scheduleName}", _fixture.AdminToken, new
        {
            displayName = "Updated Nightly Schedule",
            cron = "0 3 * * *",
            isEnabled = false
        }))
        {
            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        }

        // 4. Delete schedule
        using (var deleteRes = await SendAsync(HttpMethod.Delete, $"/api/orchestrator/schedules/{scheduleName}", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);
        }
    }

    [Fact]
    public async Task NotificationCatalogCrudAndDispatchLifecycle()
    {
        const string notifName = "test_notif_slack_ops";

        // 1. Create notification
        using (var createRes = await SendAsync(HttpMethod.Post, "/api/orchestrator/notifications", _fixture.AdminToken, new
        {
            name = notifName,
            displayName = "Ops Slack Channel",
            description = "Broadcasts critical alerts to #ops-data",
            connectionName = "mock_slack_webhook",
            recipient = "#ops-data",
            isEnabled = true
        }))
        {
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        }

        // 2. List notifications
        using (var listRes = await SendAsync(HttpMethod.Get, "/api/orchestrator/notifications", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
            var notifs = await listRes.Content.ReadFromJsonAsync<List<NotificationDefinitionDto>>();
            Assert.NotNull(notifs);
            Assert.Contains(notifs, n => n.Name == notifName && n.DisplayName == "Ops Slack Channel");
        }

        // 3. Update notification
        using (var updateRes = await SendAsync(HttpMethod.Put, $"/api/orchestrator/notifications/{notifName}", _fixture.AdminToken, new
        {
            displayName = "Updated Ops Channel",
            recipient = "#ops-alerts"
        }))
        {
            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        }

        // 4. Test dispatch
        using (var dispRes = await SendAsync(HttpMethod.Post, $"/api/orchestrator/notifications/{notifName}/dispatch", _fixture.AdminToken, new
        {
            sourceKind = "JOB",
            title = "Test Dispatch Title",
            text = "Pipeline finished successfully",
            trigger = "Completion"
        }))
        {
            Assert.True(dispRes.StatusCode == HttpStatusCode.OK || dispRes.StatusCode == HttpStatusCode.NoContent || dispRes.StatusCode == HttpStatusCode.Accepted);
        }

        // 5. Delete notification
        using (var deleteRes = await SendAsync(HttpMethod.Delete, $"/api/orchestrator/notifications/{notifName}", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);
        }
    }

    [Fact]
    public async Task JobScheduleAndNotificationAttachmentLifecycle()
    {
        const string jobName = "test_job_attached_wiring";
        const string schedName = "test_sched_for_wiring";
        const string notifName = "test_notif_for_wiring";

        // Create job
        using (var jobRes = await SendAsync(HttpMethod.Post, "/api/orchestrator/jobs", _fixture.AdminToken, new
        {
            name = jobName,
            displayName = "Wiring Test Job",
            scriptText = "SELECT 1 AS TestVal;",
            interval = 60,
            unit = "MINUTE"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, jobRes.StatusCode);
        }

        // Create schedule
        using (var schedRes = await SendAsync(HttpMethod.Post, "/api/orchestrator/schedules", _fixture.AdminToken, new
        {
            name = schedName,
            cron = "0 4 * * *",
            timeZone = "UTC"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, schedRes.StatusCode);
        }

        // Create notification
        using (var notifRes = await SendAsync(HttpMethod.Post, "/api/orchestrator/notifications", _fixture.AdminToken, new
        {
            name = notifName,
            connectionName = "smtp_conn",
            recipient = "alerts@example.com"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, notifRes.StatusCode);
        }

        // Attach schedule to job
        using (var attachSched = await SendAsync(HttpMethod.Post, $"/api/orchestrator/jobs/{jobName}/schedules/{schedName}", _fixture.AdminToken))
        {
            Assert.True(attachSched.StatusCode == HttpStatusCode.OK || attachSched.StatusCode == HttpStatusCode.Created || attachSched.StatusCode == HttpStatusCode.NoContent);
        }

        // Get job schedules
        using (var getScheds = await SendAsync(HttpMethod.Get, $"/api/orchestrator/jobs/{jobName}/schedules", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, getScheds.StatusCode);
            var links = await getScheds.Content.ReadFromJsonAsync<List<JobScheduleLinkDto>>();
            Assert.NotNull(links);
            Assert.Contains(links, l => l.ScheduleName == schedName || l.ScheduleId.Value.Length > 0);
        }

        // Attach notification to job
        using (var attachNotif = await SendAsync(HttpMethod.Post, $"/api/orchestrator/jobs/{jobName}/notifications/{notifName}", _fixture.AdminToken, new
        {
            trigger = "Failure"
        }))
        {
            Assert.True(attachNotif.StatusCode == HttpStatusCode.OK || attachNotif.StatusCode == HttpStatusCode.Created || attachNotif.StatusCode == HttpStatusCode.NoContent);
        }

        // Get job notifications
        using (var getNotifs = await SendAsync(HttpMethod.Get, $"/api/orchestrator/jobs/{jobName}/notifications", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, getNotifs.StatusCode);
            var links = await getNotifs.Content.ReadFromJsonAsync<List<JobNotificationLinkDto>>();
            Assert.NotNull(links);
            Assert.Contains(links, l => l.NotificationName == notifName || l.NotificationId.Value.Length > 0);
        }

        // Detach notification
        using (var detachNotif = await SendAsync(HttpMethod.Delete, $"/api/orchestrator/jobs/{jobName}/notifications/{notifName}", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, detachNotif.StatusCode);
        }

        // Detach schedule
        using (var detachSched = await SendAsync(HttpMethod.Delete, $"/api/orchestrator/jobs/{jobName}/schedules/{schedName}", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, detachSched.StatusCode);
        }

        // Clean up
        await SendAsync(HttpMethod.Delete, $"/api/orchestrator/jobs/{jobName}", _fixture.AdminToken);
        await SendAsync(HttpMethod.Delete, $"/api/orchestrator/schedules/{schedName}", _fixture.AdminToken);
        await SendAsync(HttpMethod.Delete, $"/api/orchestrator/notifications/{notifName}", _fixture.AdminToken);
    }

    [Fact]
    public async Task WatermarkStateInspectionAndResetLifecycle()
    {
        const string jobName = "test_job_watermark_state";
        const string keyName = "last_order_id";

        // Create job
        using (var jobRes = await SendAsync(HttpMethod.Post, "/api/orchestrator/jobs", _fixture.AdminToken, new
        {
            name = jobName,
            scriptText = "SELECT 1;",
            interval = 60,
            unit = "MINUTE"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, jobRes.StatusCode);
        }

        // Set watermark key
        using (var setRes = await SendAsync(HttpMethod.Put, $"/api/orchestrator/jobs/{jobName}/state/{keyName}", _fixture.AdminToken, new
        {
            value = "987654"
        }))
        {
            Assert.Equal(HttpStatusCode.OK, setRes.StatusCode);
        }

        // Get state list
        using (var getRes = await SendAsync(HttpMethod.Get, $"/api/orchestrator/jobs/{jobName}/state", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
            var states = await getRes.Content.ReadFromJsonAsync<List<JobStateEntryDto>>();
            Assert.NotNull(states);
            Assert.Contains(states, s => s.StateKey == keyName && s.StateValue == "987654");
        }

        // Clear watermark key (reset)
        using (var delRes = await SendAsync(HttpMethod.Delete, $"/api/orchestrator/jobs/{jobName}/state/{keyName}", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);
        }

        // Verify cleared
        using (var verifyRes = await SendAsync(HttpMethod.Get, $"/api/orchestrator/jobs/{jobName}/state", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);
            var states = await verifyRes.Content.ReadFromJsonAsync<List<JobStateEntryDto>>();
            Assert.NotNull(states);
            Assert.DoesNotContain(states, s => s.StateKey == keyName && s.StateValue == "987654");
        }

        // Clean up
        await SendAsync(HttpMethod.Delete, $"/api/orchestrator/jobs/{jobName}", _fixture.AdminToken);
    }

    [Fact]
    public async Task JobCreationWithSandboxProfileAndDependenciesQuery()
    {
        const string jobName = "test_job_with_sandbox_options";

        using (var jobRes = await SendAsync(HttpMethod.Post, "/api/orchestrator/jobs", _fixture.AdminToken, new
        {
            name = jobName,
            displayName = "Hardened Pipeline",
            description = "Runs inside zero-trust isolated container",
            scriptText = "SELECT * INTO #stg FROM mssql_conn.Orders; SELECT * INTO pg_conn.FactOrders FROM #stg;",
            interval = 30,
            unit = "MINUTE",
            options = new Dictionary<string, string>
            {
                ["SandboxProfile"] = "Hardened"
            }
        }))
        {
            Assert.Equal(HttpStatusCode.Created, jobRes.StatusCode);
        }

        // Verify dependencies endpoint
        using (var depRes = await SendAsync(HttpMethod.Get, $"/api/orchestrator/jobs/{jobName}/dependencies", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, depRes.StatusCode);
            var depChain = await depRes.Content.ReadFromJsonAsync<JobDependencyChainDto>();
            Assert.NotNull(depChain);
            Assert.Equal(jobName, depChain.JobName);
            Assert.NotEmpty(depChain.Nodes);
        }

        // Verify audit log query
        using (var auditRes = await SendAsync(HttpMethod.Get, $"/api/orchestrator/jobs/{jobName}/audit", _fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
        }

        // Clean up
        await SendAsync(HttpMethod.Delete, $"/api/orchestrator/jobs/{jobName}", _fixture.AdminToken);
    }
}
