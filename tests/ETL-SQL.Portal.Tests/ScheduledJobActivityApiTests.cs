using System.Net;
using System.Net.Http.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Integration")]
public sealed class ScheduledJobActivityApiTests
{
    private static readonly DateTimeOffset AsOf = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
    private static readonly OrchestratorCaller Owner = new("user", "1", "owner", ["OrchestratorManager"], [], "tenant-a");

    [Fact]
    public async Task FiltersAndCountsUseAuthorizedJobsBeforePagination()
    {
        await using var factory = new ActivityFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SQLiteJobHistoryStore>();
        await AddJobAsync(store, "a-hidden", "tenant-a", "user:other", ScheduledJobRunState.Running);
        await AddJobAsync(store, "b-running", "tenant-a", Owner.PrincipalKey, ScheduledJobRunState.Running);
        await AddJobAsync(store, "c-queued", "tenant-a", Owner.PrincipalKey, ScheduledJobRunState.Queued);
        var complete = await AddJobAsync(store, "d-complete", "tenant-a", Owner.PrincipalKey);
        var failed = await AddJobAsync(store, "e-failed", "tenant-a", Owner.PrincipalKey);
        await AddJobAsync(store, "f-idle", "tenant-a", Owner.PrincipalKey);
        await AddJobAsync(store, "b-running", "tenant-b", Owner.PrincipalKey, ScheduledJobRunState.Running);
        await AddJobAsync(store, "unbound", null, Owner.PrincipalKey, ScheduledJobRunState.Running);
        await AddOutcomeAsync(store, factory, complete, "SUCCESS", "2026-10-06T20:00:00-04:00");
        await AddOutcomeAsync(store, factory, complete, "SUCCESS", "2026-10-07T08:00:00Z");
        await AddOutcomeAsync(store, factory, failed, "FAILURE", "2026-10-07T09:00:00Z");
        await AddOutcomeAsync(store, factory, failed, "SUCCESS", "2026-10-07T10:00:00Z");

        var all = await GetAsync(client, Owner, "limit=1");
        Assert.Equal(5, all.Total);
        Assert.Equal("b-running", Assert.Single(all.Items).Job.Name);
        Assert.Equal(new ScheduledJobActivityCounts(1, 1, 2, 1), all.Counts);
        Assert.Equal(AsOf, all.AsOfUtc);
        Assert.Equal(AsOf.Date, all.DayStartUtc.UtcDateTime);
        var next = await GetAsync(client, Owner, "limit=1&offset=1");
        Assert.Equal("c-queued", Assert.Single(next.Items).Job.Name);
        Assert.Equal(all.Counts, next.Counts);
        foreach (var (filter, expected) in new[]
        {
            ("active", "b-running"), ("queued", "c-queued"), ("failedToday", "e-failed")
        })
        {
            var page = await GetAsync(client, Owner, $"filter={filter}");
            Assert.Equal(expected, Assert.Single(page.Items).Job.Name);
            Assert.Equal(all.Counts, page.Counts);
        }
        Assert.Equal(2, (await GetAsync(client, Owner, "filter=completedToday")).Total);
        var search = await GetAsync(client, Owner, "filter=completedToday&search=e-failed");
        Assert.Equal("e-failed", Assert.Single(search.Items).Job.Name);
        Assert.Equal(all.Counts, search.Counts);
        Assert.Empty((await GetAsync(client, Owner, "offset=99")).Items);

        var denied = await GetAsync(client, Owner with { SubjectId = "denied" }, "");
        Assert.Equal(0, denied.Total);
        Assert.Equal(new ScheduledJobActivityCounts(0, 0, 0, 0), denied.Counts);
        Assert.Empty(denied.Items);
        var admin = await GetAsync(client, Owner with { Roles = ["Admin"] }, "filter=active");
        Assert.Equal(2, admin.Total);
        Assert.Equal(2, admin.Counts.Active);
        Assert.All(admin.Items, item => Assert.Equal("tenant-a", item.Job.TenantId));
    }

    [Fact]
    public async Task GroupReadGrantAndStatusSearchApplyWithoutDisclosingHiddenJobs()
    {
        await using var factory = new ActivityFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SQLiteJobHistoryStore>();
        var job = await AddJobAsync(store, "shared", "tenant-a", "user:owner");
        await store.SaveJobAsync(job with { IsEnabled = false, Description = "Nightly warehouse" });
        await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(job.Id.Value, OrchestratorObjectKind.Job,
            OrchestratorPrincipalKind.Group, "finance", OrchestratorObjectPermission.Read, "user:owner"));
        var caller = Owner with { SubjectId = "reader", GroupIds = ["finance"] };
        var page = await GetAsync(client, caller, "status=disabled&search=WAREHOUSE");
        Assert.Equal(job.Id, Assert.Single(page.Items).Job.Id);
        Assert.Empty((await GetAsync(client, caller, "status=enabled")).Items);
        Assert.Empty((await GetAsync(client, caller with { TenantId = "tenant-b" }, "")).Items);
        var serviceCaller = caller with { SubjectType = "service", Scopes = [] };
        Assert.Empty((await GetAsync(client, serviceCaller, "")).Items);
        Assert.Equal(job.Id, Assert.Single((await GetAsync(client,
            serviceCaller with { Scopes = ["orchestrator.read"] }, "")).Items).Job.Id);
    }

    [Theory]
    [InlineData("filter=unknown")]
    [InlineData("status=unknown")]
    [InlineData("limit=0")]
    [InlineData("limit=1001")]
    [InlineData("offset=-1")]
    public async Task InvalidQueriesAreRejected(string query)
    {
        await using var factory = new ActivityFactory();
        using var client = factory.CreateClient();
        using var request = Request(Owner, query);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequestCannotReadActivity()
    {
        await using var factory = new ActivityFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/scheduled-jobs/activity");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidHistoryReturnsUnavailableInsteadOfFabricatedZeroCounts()
    {
        await using var factory = new ActivityFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SQLiteJobHistoryStore>();
        var job = await AddJobAsync(store, "invalid-evidence", "tenant-a", Owner.PrincipalKey);
        await AddOutcomeAsync(store, factory, job, "SUCCESS", "2026-10-07Tinvalid");
        using var request = Request(Owner, "");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("invalid-evidence", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PaginationCanReachJobsBeyondThePreviousThousandRowCatalogLimit()
    {
        await using var factory = new ActivityFactory();
        using var client = factory.CreateClient();
        await factory.Services.GetRequiredService<SQLiteJobHistoryStore>().InitializeAsync();
        using var connection = new SqliteConnection($"Data Source={Path.Combine(factory.TempDir, "etlsql.db")}");
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO jobs (id, name, script, interval, unit, is_enabled, created_by, tenant_id)
            VALUES ($id, $name, 'SELECT 1;', 0, 'HOUR', 0, $owner, 'tenant-a');
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        command.Parameters.AddWithValue("$owner", Owner.PrincipalKey);
        for (var index = 0; index < 1005; index++)
        {
            id.Value = JobId.New().Value;
            name.Value = $"job_{index:0000}";
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        var page = await GetAsync(client, Owner, "limit=10&offset=1000");
        Assert.Equal(1005, page.Total);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal("job_1000", page.Items[0].Job.Name);
    }

    private static async Task<JobDefinition> AddJobAsync(
        SQLiteJobHistoryStore store, string name, string? tenant, string owner,
        ScheduledJobRunState state = ScheduledJobRunState.Idle)
    {
        await store.SaveJobAsync(new JobDefinition(name, "SELECT 1;", 0, "HOUR", null, null, null, true,
            CreatedBy: owner, TenantId: tenant));
        var job = (await store.GetJobAsync(tenant, name))!;
        if (state != ScheduledJobRunState.Idle)
        {
            var token = (await store.AcquireJobLeaseAsync(job.Id, "fixture", TimeSpan.FromDays(3650)))!.Value;
            Assert.True(await store.TrySetScheduledJobRunStateAsync(job.Id, "fixture", token, state));
        }
        return job;
    }

    private static async Task AddOutcomeAsync(SQLiteJobHistoryStore store, ActivityFactory factory,
        JobDefinition job, string status, string ended)
    {
        var id = await store.LogJobStartAsync(job.Id);
        using var connection = new SqliteConnection($"Data Source={Path.Combine(factory.TempDir, "etlsql.db")}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE job_history SET status = $status, end_time = $ended WHERE id = $id";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$ended", ended);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static HttpRequestMessage Request(OrchestratorCaller caller, string query)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/scheduled-jobs/activity?{query}");
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        return request;
    }

    private static async Task<ScheduledJobActivityPage> GetAsync(HttpClient client, OrchestratorCaller caller, string query)
    {
        using var request = Request(caller, query);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<ScheduledJobActivityPage>())!;
    }

    private sealed class ActivityFactory() : OrchestratorWebFactory(requireFederatedIdentity: true)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new ActivityClock());
            });
        }
    }

    private sealed class ActivityClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => AsOf;
    }
}
