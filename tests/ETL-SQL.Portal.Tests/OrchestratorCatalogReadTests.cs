using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The schedule and notification catalogs must not answer "none" when the Orchestrator did not
/// answer at all. The page renders an empty list as "No shared schedules defined yet", which sends
/// an operator to recreate objects that still exist.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OrchestratorCatalogReadTests(OrchestratorGrantAdministrationFixture fixture)
    : IClassFixture<OrchestratorGrantAdministrationFixture>
{
    [Fact]
    public async Task CatalogsReturnTheOrchestratorsDefinitionsWithoutCaching()
    {
        using (var created = await fixture.SendAsync(HttpMethod.Post, "/api/orchestrator/schedules", fixture.AdminToken,
            new { name = "catalog_read_nightly", cron = "0 2 * * *", timeZone = "UTC" }))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var created = await fixture.SendAsync(HttpMethod.Post, "/api/orchestrator/notifications", fixture.AdminToken,
            new { name = "catalog_read_ops", connectionName = "smtp_ops", recipient = "ops@test.local" }))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var schedules = await fixture.SendAsync(HttpMethod.Get, "/api/orchestrator/schedules", fixture.AdminToken);
        Assert.Equal(HttpStatusCode.OK, schedules.StatusCode);
        Assert.True(schedules.Headers.CacheControl?.NoStore);
        var schedule = Assert.Single((await schedules.Content.ReadFromJsonAsync<List<ScheduleDefinitionDto>>())!,
            item => item.Name == "catalog_read_nightly");
        Assert.Equal("0 2 * * *", schedule.Cron);
        Assert.True(schedule.Version >= 1);

        using var notifications = await fixture.SendAsync(HttpMethod.Get, "/api/orchestrator/notifications", fixture.AdminToken);
        Assert.Equal(HttpStatusCode.OK, notifications.StatusCode);
        Assert.True(notifications.Headers.CacheControl?.NoStore);
        var notification = Assert.Single((await notifications.Content.ReadFromJsonAsync<List<NotificationDefinitionDto>>())!,
            item => item.Name == "catalog_read_ops");
        Assert.Equal("smtp_ops", notification.ConnectionName);
    }

    [Theory]
    [InlineData("schedules", "Schedules")]
    [InlineData("notifications", "Notifications")]
    public async Task AnUnansweredCatalogReadIsUnavailableRatherThanEmpty(string catalog, string subject)
    {
        using var portal = new FailingOrchestratorFactory(HttpStatusCode.InternalServerError);
        using var client = portal.CreateClient();
        using var response = await GetAsync(client, await AdminTokenAsync(client), catalog);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal($"{subject} are temporarily unavailable. Please retry.", body["error"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("schedules", "Schedules")]
    [InlineData("notifications", "Notifications")]
    public async Task ARefusedCatalogReadStaysARefusal(string catalog, string subject)
    {
        using var portal = new FailingOrchestratorFactory(HttpStatusCode.Forbidden);
        using var client = portal.CreateClient();
        using var response = await GetAsync(client, await AdminTokenAsync(client), catalog);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal($"{subject} are not available to this account.", body["error"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("schedules")]
    [InlineData("notifications")]
    public async Task AnUnconfiguredOrchestratorIsUnavailableRatherThanEmpty(string catalog)
    {
        using var portal = new PortalWebFactory();
        using var client = portal.CreateClient();
        using var response = await GetAsync(client, await AdminTokenAsync(client), catalog);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task JobDetailReadsAnswerForAJobWithNothingAttached()
    {
        const string name = "catalog_read_detail";
        await fixture.CreateJobAsync(name, fixture.AdminToken);
        foreach (var detail in new[] { "history", "schedules", "notifications", "state" })
        {
            using var response = await fixture.SendAsync(
                HttpMethod.Get, $"/api/orchestrator/jobs/{name}/{detail}", fixture.AdminToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Empty((await response.Content.ReadFromJsonAsync<JsonArray>())!);
        }
    }

    [Theory]
    [InlineData("history", HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable, "Run history entries are temporarily unavailable. Please retry.")]
    [InlineData("schedules", HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, "Attached schedules are temporarily unavailable. Please retry.")]
    [InlineData("notifications", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, "Attached notifications are not available to this account.")]
    [InlineData("state", HttpStatusCode.NotFound, HttpStatusCode.NotFound, "Watermarks could not be read because the job no longer exists.")]
    [InlineData("state", HttpStatusCode.Unauthorized, HttpStatusCode.ServiceUnavailable, "Watermarks are temporarily unavailable. Please retry.")]
    public async Task AFailedJobDetailReadIsNotAnEmptyList(
        string detail, HttpStatusCode service, HttpStatusCode expected, string message)
    {
        using var portal = new FailingOrchestratorFactory(service);
        using var client = portal.CreateClient();
        using var response = await GetAsync(client, await AdminTokenAsync(client), $"jobs/nightly/{detail}");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(message, (await response.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>());
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string token, string catalog)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orchestrator/{catalog}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<string> AdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "Admin@12345!");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", initial);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
        return await LoginAsync(client, "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }

    /// <summary>A Portal whose configured Orchestrator answers every call with one fixed status.</summary>
    private sealed class FailingOrchestratorFactory(HttpStatusCode status) : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
        }

        protected override void CustomizeServices(IServiceCollection services) =>
            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => new FixedStatusHandler(status));
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(new { error = "fixture" }) });
    }
}
