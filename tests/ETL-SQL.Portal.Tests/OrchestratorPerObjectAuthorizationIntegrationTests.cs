using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Integration")]
public sealed class OrchestratorPerObjectAuthorizationIntegrationTests
{
    [Fact]
    public async Task SignedTenantIsPersistedAndCannotBeReboundByAnotherTenant()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var tenantA = new OrchestratorCaller(
            "user", "1", "owner", ["OrchestratorManager"], [], "tenant-a");

        using (var create = Request(HttpMethod.Post, "/api/scheduled-jobs", tenantA, new
        {
            name = "tenant_bound_job",
            scriptText = "SELECT 1 AS Value;",
            interval = 100,
            unit = "DAY"
        }))
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        // Resolved in tenant-a, which is the claim under test: looking the job up in the unbound
        // scope would find nothing whether or not the binding worked, so it could only ever fail or
        // pass for the wrong reason.
        Assert.Equal("tenant-a", (await store.GetJobAsync("tenant-a", "tenant_bound_job"))!.TenantId);

        var tenantB = tenantA with { TenantId = "tenant-b" };
        using var update = Request(
            HttpMethod.Put, "/api/scheduled-jobs/tenant_bound_job", tenantB, new { isEnabled = false });
        update.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        // Not Forbidden: the name is resolved in the caller's own tenant before anything is
        // authorized, so to tenant-b this job does not exist. Answering 403 would confirm that some
        // other tenant holds the name, which is a disclosure the boundary exists to prevent — the
        // rebinding is refused *and* the attempt learns nothing.
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(update)).StatusCode);
        Assert.Equal("tenant-a", (await store.GetJobAsync("tenant-a", "tenant_bound_job"))!.TenantId);
        // And tenant-b did not acquire one of its own by trying.
        Assert.Null(await store.GetJobAsync("tenant-b", "tenant_bound_job"));
    }

    [Fact]
    public async Task ReachabilityDoesNotGrantAnotherPrincipalsJobOrPermitCreateOrAlterTakeover()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();

        using (var create = Request(HttpMethod.Post, "/api/scheduled-jobs", Caller("user", "1", "owner", "OrchestratorManager"), new
        {
            name = "owned_job",
            scriptText = "SELECT 1 AS Value;",
            interval = 100,
            unit = "DAY"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);
        }

        using (var noIdentity = new HttpRequestMessage(HttpMethod.Get, "/api/scheduled-jobs"))
        {
            noIdentity.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
            noIdentity.Headers.Add("X-Orchestrator-Actor", "1:owner");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(noIdentity)).StatusCode);
        }

        using (var strangerList = Request(HttpMethod.Get, "/api/scheduled-jobs", Caller("user", "3", "stranger", "OrchestratorManager")))
        {
            var response = await client.SendAsync(strangerList);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(await response.Content.ReadFromJsonAsync<object[]>() ?? []);
        }

        using (var takeover = Request(HttpMethod.Post, "/api/scheduled-jobs", Caller("user", "3", "stranger", "OrchestratorManager"), new
        {
            name = "owned_job",
            jobType = "SCRIPT",
            targetPath = "bundle://replacement/main.etlsql",
            mode = "CreateOrAlter"
        }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(takeover)).StatusCode);
        }

        string adHocId;
        using (var scriptTakeover = Request(HttpMethod.Post, "/jobs",
                   Caller("user", "3", "stranger", "OrchestratorManager"), new
                   {
                       scriptText = "CREATE OR ALTER JOB owned_job FOR SCRIPT 'jobs/replacement.etlsql';"
                   }))
        {
            var response = await client.SendAsync(scriptTakeover);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            adHocId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
        }

        JobStatusResponse? status = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var statusRequest = Request(HttpMethod.Get, $"/jobs/{adHocId}",
                Caller("user", "3", "stranger", "OrchestratorManager"));
            status = await (await client.SendAsync(statusRequest)).Content.ReadFromJsonAsync<JobStatusResponse>();
            if (status?.Status is JobRunStatus.Failed or JobRunStatus.Completed) break;
            await Task.Delay(25);
        }
        Assert.NotNull(status);
        Assert.Equal(JobRunStatus.Failed, status.Status);
        Assert.Contains("lacks MANAGE authority", status.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        using (var crossPrincipalStatus = Request(HttpMethod.Get, $"/jobs/{adHocId}",
                   Caller("user", "4", "other", "OrchestratorManager")))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(crossPrincipalStatus)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().GetJobAsync("owned_job");
        Assert.NotNull(stored);
        Assert.Equal("SELECT 1 AS Value;", stored.Script);
    }

    [Fact]
    public async Task ReadGrantDoesNotImplyExecuteManageOrOverride()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var owner = Caller("user", "1", "owner", "OrchestratorManager");
        var reader = Caller("user", "2", "reader", "OrchestratorManager");

        using (var create = Request(HttpMethod.Post, "/api/scheduled-jobs", owner, new
        {
            name = "scoped_job",
            scriptText = "SELECT 1 AS Value;",
            interval = 100,
            unit = "DAY"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);
        }
        using (var grant = Request(
            HttpMethod.Put,
            "/api/authorization/JOB/scoped_job/USER/2",
            owner,
            new { permission = "READ" }))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(grant)).StatusCode);
        }

        using (var history = Request(HttpMethod.Get, "/api/scheduled-jobs/scoped_job/history", reader))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(history)).StatusCode);

        using (var trigger = Request(HttpMethod.Post, "/api/scheduled-jobs/scoped_job/trigger", reader))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(trigger)).StatusCode);

        using (var overrideTrigger = Request(
            HttpMethod.Post,
            "/api/scheduled-jobs/scoped_job/trigger",
            reader,
            new { variables = new Dictionary<string, string> { ["scope"] = "all" } }))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(overrideTrigger)).StatusCode);

        using (var update = Request(HttpMethod.Put, "/api/scheduled-jobs/scoped_job", reader, new { isEnabled = false }))
        {
            update.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(update)).StatusCode);
        }
    }

    [Fact]
    public async Task ScheduleAndNotificationReadsAndScriptMutationsAreObjectScoped()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IJobCatalogStore>();
            await catalog.SaveScheduleAsync(new ScheduleDefinition(
                "private_schedule", "0 2 * * *", "UTC", CreatedBy: "user:1", ModifiedBy: "owner"));
            await catalog.SaveNotificationAsync(new NotificationDefinition(
                "private_notification", "mail", CreatedBy: "user:1", ModifiedBy: "owner"));
        }

        var stranger = Caller("user", "3", "stranger", "OrchestratorManager");
        using (var schedules = Request(HttpMethod.Get, "/api/schedules", stranger))
            Assert.Empty(await (await client.SendAsync(schedules)).Content.ReadFromJsonAsync<ScheduleDefinition[]>() ?? []);
        using (var notifications = Request(HttpMethod.Get, "/api/notifications", stranger))
            Assert.Empty(await (await client.SendAsync(notifications)).Content.ReadFromJsonAsync<NotificationDefinition[]>() ?? []);
        using (var schedule = Request(HttpMethod.Get, "/api/schedules/private_schedule", stranger))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(schedule)).StatusCode);
        using (var notification = Request(HttpMethod.Get, "/api/notifications/private_notification", stranger))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(notification)).StatusCode);

        var scheduleStatus = await RunAdHocAsync(client, stranger,
            "CREATE OR ALTER SCHEDULE private_schedule ON '0 3 * * *' AT TIME ZONE 'UTC';");
        Assert.Equal(JobRunStatus.Failed, scheduleStatus.Status);
        Assert.Contains("lacks MANAGE authority", scheduleStatus.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var notificationStatus = await RunAdHocAsync(client, stranger,
            "CREATE OR ALTER NOTIFICATION private_notification USING other_mail;");
        Assert.Equal(JobRunStatus.Failed, notificationStatus.Status);
        Assert.Contains("lacks MANAGE authority", notificationStatus.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static OrchestratorCaller Caller(string type, string id, string name, params string[] roles) =>
        new(type, id, name, roles, []);

    private static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        OrchestratorCaller caller,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<JobStatusResponse> RunAdHocAsync(
        HttpClient client,
        OrchestratorCaller caller,
        string script)
    {
        string id;
        using (var submit = Request(HttpMethod.Post, "/jobs", caller, new { scriptText = script }))
        {
            var response = await client.SendAsync(submit);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
        }

        for (var attempt = 0; attempt < 80; attempt++)
        {
            using var statusRequest = Request(HttpMethod.Get, $"/jobs/{id}", caller);
            var response = await client.SendAsync(statusRequest);
            var status = await response.Content.ReadFromJsonAsync<JobStatusResponse>();
            if (status?.Status is JobRunStatus.Failed or JobRunStatus.Completed) return status;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Ad-hoc authorization test job '{id}' did not complete.");
    }
}
