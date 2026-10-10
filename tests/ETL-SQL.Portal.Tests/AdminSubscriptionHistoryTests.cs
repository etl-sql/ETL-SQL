using System.Net;
using System.Net.Http.Headers;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class AdminSubscriptionHistoryTests
{
    [Fact]
    public async Task UnavailableHistoryReturns503WithoutProviderDetails()
    {
        using var factory = new HistoryFailureFactory();
        using var client = factory.CreateClient();
        string token;
        int subscriptionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin") ?? throw new InvalidOperationException("Seeded admin is missing.");
            admin.MustChangePassword = false;
            var folder = new Folder { Name = "History fixture", Path = "/history-fixture" };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            var report = new Report { Name = "History fixture", ScriptPath = "history-fixture.rptsql", FolderId = folder.Id, CreatedBy = admin.Id };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            var subscription = new Subscription { ReportId = report.Id, UserId = admin.Id, SmtpAlias = "mail", Recipients = "fixture@example.test" };
            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;
            token = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(admin, await users.GetRolesAsync(admin));
            await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().InitializeAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var empty = await client.GetAsync($"/api/subscriptions/{subscriptionId}/history");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal("[]", await empty.Content.ReadAsStringAsync());
        factory.Faults.FailReads = true;
        using var unavailable = await client.GetAsync($"/api/subscriptions/{subscriptionId}/history");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        var response = await unavailable.Content.ReadAsStringAsync();
        Assert.Contains("history", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(HistoryFaults.ProviderDetail, response, StringComparison.Ordinal);
    }

    private sealed class HistoryFailureFactory : PortalWebFactory
    {
        public HistoryFaults Faults => (HistoryFaults)Services.GetRequiredService<IOrchestratorStoreFactory>();
        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IOrchestratorStoreFactory>();
            services.AddSingleton<IOrchestratorStoreFactory>(sp => new HistoryFaults(
                new OrchestratorStoreFactory(sp.GetRequiredService<IConfiguration>())));
        }
    }

    private sealed class HistoryFaults(IOrchestratorStoreFactory inner) : IOrchestratorStoreFactory
    {
        public const string ProviderDetail = "provider-sensitive-detail-marker";
        public bool FailReads { get; set; }
        public DatabaseProvider Provider => inner.Provider;
        public IJobHistoryStore Create(string? dbPath = null) => FailReads
            ? throw new IOException(ProviderDetail) : inner.Create(dbPath);
    }
}
