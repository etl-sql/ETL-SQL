using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class ScheduledJobActivityProxyTests(OrchestratorGrantAdministrationFixture fixture)
    : IClassFixture<OrchestratorGrantAdministrationFixture>
{
    [Fact]
    public async Task ProxyPreservesFiltersCallerGrantsAndFailureStatuses()
    {
        const string name = "scheduled_activity_proxy";
        await fixture.CreateJobAsync(name, fixture.AdminToken);
        var store = fixture.Orchestrator.Services.GetRequiredService<IJobHistoryStore>();
        var activityStore = Assert.IsAssignableFrom<IScheduledJobActivityStore>(store);
        var job = (await store.GetAllJobsAsync()).Single(j => j.Name == name);
        var fence = (await store.AcquireJobLeaseAsync(job.Id, "proxy-fixture", TimeSpan.FromDays(3650)))!.Value;
        Assert.True(await activityStore.TrySetScheduledJobRunStateAsync(job.Id, "proxy-fixture", fence,
            ScheduledJobRunState.Running));
        try
        {
            using (var response = await GetAsync(fixture.AdminToken, $"filter=active&search={name}&limit=1"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore);
                var page = (await response.Content.ReadFromJsonAsync<ScheduledJobActivityPage>())!;
                Assert.Equal(job.Id, Assert.Single(page.Items).Job.Id);
                Assert.Equal(1, page.Counts.Active);
                Assert.Equal(1, page.Limit);
            }
            using (var response = await GetAsync(fixture.AdminToken, $"filter=active&search={name}&status=disabled"))
            {
                var page = (await response.Content.ReadFromJsonAsync<ScheduledJobActivityPage>())!;
                Assert.Empty(page.Items);
                Assert.Equal(1, page.Counts.Active);
            }
            using (var response = await GetAsync(fixture.ManagerToken, "filter=active"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var page = (await response.Content.ReadFromJsonAsync<ScheduledJobActivityPage>())!;
                Assert.Empty(page.Items);
                Assert.Equal(0, page.Counts.Active);
            }
            using (var invalid = await GetAsync(fixture.AdminToken, "filter=invalid"))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var denied = await GetAsync(fixture.ViewerToken, "filter=active"))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        finally
        {
            await store.ReleaseJobLeaseAsync(job.Id, "proxy-fixture");
        }
    }

    private async Task<HttpResponseMessage> GetAsync(string token, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orchestrator/jobs/activity?{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }
}
