using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Service;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// List endpoints page over the rows the caller may see, not over the store's rows; and an update
/// to a scheduled job obeys the same schedule rules as its creation.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OrchestratorListPagingAndUpdateValidationTests
{
    private const string ApiKey = "test-orch-key-12345";

    [Fact]
    public async Task ScheduleAndNotificationLists_FillThePageFromRowsTheCallerCanSee()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IJobCatalogStore>();
            // Rows the caller cannot see sort ahead of the one it can.
            foreach (var name in new[] { "a_other_1", "a_other_2", "a_other_3" })
            {
                await catalog.SaveScheduleAsync(new ScheduleDefinition(
                    name, "0 2 * * *", "UTC", CreatedBy: "user:1", ModifiedBy: "owner"));
                await catalog.SaveNotificationAsync(new NotificationDefinition(
                    name, "mail", CreatedBy: "user:1", ModifiedBy: "owner"));
            }
            await catalog.SaveScheduleAsync(new ScheduleDefinition(
                "z_mine", "0 3 * * *", "UTC", CreatedBy: "user:2", ModifiedBy: "me"));
            await catalog.SaveNotificationAsync(new NotificationDefinition(
                "z_mine", "mail", CreatedBy: "user:2", ModifiedBy: "me"));
        }
        var me = new OrchestratorCaller("user", "2", "me", ["OrchestratorManager"], []);

        using (var schedules = Request(HttpMethod.Get, "/api/schedules?limit=1", me))
        {
            var page = await (await client.SendAsync(schedules)).Content.ReadFromJsonAsync<ScheduleDefinition[]>();
            Assert.Equal("z_mine", Assert.Single(page!).Name);
        }
        using (var notifications = Request(HttpMethod.Get, "/api/notifications?limit=1", me))
        {
            var page = await (await client.SendAsync(notifications)).Content.ReadFromJsonAsync<NotificationDefinition[]>();
            Assert.Equal("z_mine", Assert.Single(page!).Name);
        }
        // The offset counts visible rows too: past the caller's only row there is nothing.
        using (var past = Request(HttpMethod.Get, "/api/schedules?limit=1&offset=1", me))
            Assert.Empty(await (await client.SendAsync(past)).Content.ReadFromJsonAsync<ScheduleDefinition[]>() ?? []);
    }

    [Fact]
    public async Task VisiblePrefix_ReadsFurtherWhenTheNewestRowsAreHidden()
    {
        // History and data-quality stores can only be asked for their newest N rows.
        var rows = Enumerable.Range(0, 500).Select(i => i < 450 ? "hidden" : $"mine-{i}").ToList();
        var reads = new List<int>();

        var visible = await JobApiEndpoints.ReadVisiblePrefixAsync(
            take =>
            {
                reads.Add(take);
                return Task.FromResult<IReadOnlyList<string>>(rows.Take(take).ToList());
            },
            batch => Task.FromResult<IReadOnlyList<string>>(batch.Where(row => row != "hidden").ToList()),
            limit: 10);

        Assert.Equal(Enumerable.Range(450, 10).Select(i => $"mine-{i}"), visible);
        Assert.True(reads.Count > 1);
    }

    [Fact]
    public async Task UpdateScheduledJob_RefusesAnInvalidSchedule()
    {
        using var factory = new OrchestratorWebFactory();
        using var client = factory.CreateClient();
        using (var create = Request(HttpMethod.Post, "/api/scheduled-jobs", null, new
        {
            name = "nightly_load",
            scriptText = "SELECT 1 AS Value;",
            interval = 1,
            unit = "DAY"
        }))
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);

        var version = (await ReadJobAsync(client, "nightly_load")).GetProperty("version").GetInt64();

        foreach (var invalid in new object[]
                 {
                     new { interval = -1 },
                     new { interval = 0 },
                     new { unit = "INVALID" },
                     new { atTime = "25:99" }
                 })
        {
            using var update = Request(HttpMethod.Put, "/api/scheduled-jobs/nightly_load", null, invalid);
            update.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(update)).StatusCode);
        }

        var job = await ReadJobAsync(client, "nightly_load");
        Assert.Equal(1, job.GetProperty("interval").GetInt32());
        Assert.Equal("DAY", job.GetProperty("unit").GetString());
    }

    private static async Task<JsonElement> ReadJobAsync(HttpClient client, string name)
    {
        using var list = Request(HttpMethod.Get, "/api/scheduled-jobs", null);
        var jobs = await (await client.SendAsync(list)).Content.ReadFromJsonAsync<JsonElement[]>();
        return Assert.Single(jobs!, job => job.GetProperty("name").GetString() == name);
    }

    private static HttpRequestMessage Request(
        HttpMethod method, string path, OrchestratorCaller? caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", ApiKey);
        if (caller is not null)
            request.Headers.Add(
                OrchestratorIdentityAssertion.HeaderName,
                OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
