using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The scope ceiling on a service token, proved at both doors.
///
/// <para>Scopes cap what a token issued to an automation may do, below whatever its roles and grants
/// would otherwise allow. Two gaps made that untrue. Creation consulted only the caller's role, so a
/// read-scoped token could still author objects; and <c>ExecutionIdentity</c> carried no scopes at
/// all, so the engine saw every service caller as scopeless — which the ceiling reads as "may do
/// nothing" — and refused verbs the identical token was allowed over HTTP.</para>
///
/// <para>Every case here is asserted for the **same caller** through both an endpoint and an ETL-SQL
/// statement. A ceiling tested on one door only is how both gaps survived: each door looked right on
/// its own.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class OrchestratorScopeCeilingTests
{
    private const string ApiKey = "test-orch-key-12345";
    private const string ReadScope = "orchestrator.read";
    private const string PublishScope = "orchestrator.publish";

    [Fact]
    public async Task ReadScopedServiceToken_CannotCreate_AtEitherDoor()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var reader = Service("1", ReadScope);

        using (var create = Request(HttpMethod.Post, "/api/scheduled-jobs", reader, NewJob("read_scoped_job")))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(create)).StatusCode);

        var script = await RunAdHocAsync(client, reader, "CREATE SCHEDULE read_scoped_schedule ON '0 2 * * *';");
        Assert.Equal(JobRunStatus.Failed, script.Status);
        Assert.Contains("may not create", script.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // And nothing was authored on the way to being refused.
        using var scope = factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IJobCatalogStore>()
            .GetScheduleAsync(null, "read_scoped_schedule"));
    }

    [Fact]
    public async Task PublishScopedServiceToken_CanCreate_AtEitherDoor()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var publisher = Service("2", PublishScope);

        using (var create = Request(HttpMethod.Post, "/api/scheduled-jobs", publisher, NewJob("publish_scoped_job")))
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);

        var script = await RunAdHocAsync(
            client, publisher, "CREATE SCHEDULE publish_scoped_schedule ON '0 2 * * *';");
        Assert.Equal(JobRunStatus.Completed, script.Status);

        using var scope = factory.Services.CreateScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IJobCatalogStore>()
            .GetScheduleAsync(null, "publish_scoped_schedule"));
    }

    /// <summary>
    /// The regression that motivated threading scopes into <c>ExecutionIdentity</c>: a service caller
    /// altering an object it owns. Over HTTP this always worked; from script it failed with
    /// "lacks MANAGE authority", because the engine could not see the scope that permitted it.
    /// </summary>
    [Fact]
    public async Task PublishScopedServiceToken_CanAlterItsOwnObject_FromScript()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var publisher = Service("2", PublishScope);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IJobCatalogStore>().SaveScheduleAsync(
                new ScheduleDefinition(
                    "owned_schedule", "0 2 * * *", "UTC",
                    CreatedBy: "service:2", ModifiedBy: "service:2"));
        }

        var script = await RunAdHocAsync(
            client, publisher,
            "CREATE OR ALTER SCHEDULE owned_schedule ON '0 3 * * *' AT TIME ZONE 'UTC';");
        Assert.Equal(JobRunStatus.Completed, script.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<IJobCatalogStore>()
                .GetScheduleAsync(null, "owned_schedule");
            Assert.Equal("0 3 * * *", stored!.Cron);
        }
    }

    /// <summary>
    /// The ceiling still bites where it should: owning the object does not lift it. A read-scoped
    /// token that owns a schedule still cannot alter it, from either door.
    /// </summary>
    [Fact]
    public async Task ReadScopedServiceToken_CannotAlterEvenItsOwnObject_FromScript()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var reader = Service("1", ReadScope);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IJobCatalogStore>().SaveScheduleAsync(
                new ScheduleDefinition(
                    "reader_owned_schedule", "0 2 * * *", "UTC",
                    CreatedBy: "service:1", ModifiedBy: "service:1"));
        }

        var script = await RunAdHocAsync(
            client, reader,
            "CREATE OR ALTER SCHEDULE reader_owned_schedule ON '0 3 * * *' AT TIME ZONE 'UTC';");
        Assert.Equal(JobRunStatus.Failed, script.Status);
        Assert.Contains("lacks MANAGE authority", script.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<IJobCatalogStore>()
                .GetScheduleAsync(null, "reader_owned_schedule");
            Assert.Equal("0 2 * * *", stored!.Cron);
        }
    }

    /// <summary>
    /// An interactive user carries no scopes and must not be capped by their absence — the ceiling
    /// exists for tokens issued to automations, and reading "no scopes" as "no authority" for a
    /// person would lock every human out of the engine path.
    /// </summary>
    [Fact]
    public async Task InteractiveUserWithNoScopes_IsNotCappedByTheCeiling()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var person = new OrchestratorCaller("user", "7", "Grace Hopper", ["OrchestratorManager"], []);

        var script = await RunAdHocAsync(client, person, "CREATE SCHEDULE human_schedule ON '0 2 * * *';");
        Assert.Equal(JobRunStatus.Completed, script.Status);
    }

    private static OrchestratorCaller Service(string id, params string[] scopes) =>
        new("service", id, $"Automation {id}", ["OrchestratorManager"], [], null, scopes);

    private static object NewJob(string name) => new
    {
        name,
        scriptText = "SELECT 1 AS Value;",
        interval = 100,
        unit = "DAY"
    };

    private static HttpRequestMessage Request(
        HttpMethod method, string path, OrchestratorCaller caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", ApiKey);
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<JobStatusResponse> RunAdHocAsync(
        HttpClient client, OrchestratorCaller caller, string script)
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
            var status = await (await client.SendAsync(statusRequest)).Content
                .ReadFromJsonAsync<JobStatusResponse>();
            if (status?.Status is JobRunStatus.Failed or JobRunStatus.Completed) return status;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Scope-ceiling test job '{id}' did not complete.");
    }
}
