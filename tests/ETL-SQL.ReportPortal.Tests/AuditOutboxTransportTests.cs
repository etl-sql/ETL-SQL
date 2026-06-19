using System.Net;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public sealed class AuditOutboxTransportTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "audit_transport_" + Guid.NewGuid().ToString("N")[..8]);

    public AuditOutboxTransportTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    [Fact]
    public async Task DrainOnceAsync_PostsPendingBatch_AndMarksRowsDelivered()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (provider, config) = await CreateProviderAsync("delivered.db", handler);
        await SeedAuditAsync(provider);

        var service = NewService(provider, config, handler);
        var processed = await service.DrainOnceAsync();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var outbox = await db.AuditOutboxMessages.SingleAsync();

        Assert.Equal(1, processed);
        Assert.Equal("Delivered", outbox.Status);
        Assert.NotNull(outbox.DeliveredAt);
        Assert.Contains(outbox.EventId, handler.LastBody);
        Assert.Contains("CREATE_USER", handler.LastBody);
    }

    [Fact]
    public async Task DrainOnceAsync_RetriesWithBackoff_ThenMarksFailedAtAttemptLimit()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var (provider, config) = await CreateProviderAsync("failed.db", handler);
        config.Audit.TransportMaxAttempts = 2;
        await SeedAuditAsync(provider);

        var service = NewService(provider, config, handler);
        await service.DrainOnceAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var outbox = await db.AuditOutboxMessages.SingleAsync();
            Assert.Equal("Pending", outbox.Status);
            Assert.Equal(1, outbox.Attempts);
            Assert.NotNull(outbox.NextAttemptAt);
            outbox.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        await service.DrainOnceAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var outbox = await db.AuditOutboxMessages.SingleAsync();
            Assert.Equal("Failed", outbox.Status);
            Assert.Equal(2, outbox.Attempts);
            Assert.Null(outbox.NextAttemptAt);
            Assert.Contains("503", outbox.LastError);
        }
    }

    [Fact]
    public async Task DrainOnceAsync_RejectsNonHttpsEndpoint()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (provider, config) = await CreateProviderAsync("http.db", handler);
        config.Audit.TransportEndpoint = "http://audit.example.test/events";
        await SeedAuditAsync(provider);

        var service = NewService(provider, config, handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DrainOnceAsync());
    }

    private async Task<(ServiceProvider Provider, PortalConfig Config)> CreateProviderAsync(
        string dbName,
        CapturingHandler handler)
    {
        var config = new PortalConfig
        {
            Audit =
            {
                TransportEndpoint = "https://audit.example.test/events",
                TransportBatchSize = 10,
                TransportIntervalSeconds = 1,
                TransportTimeoutSeconds = 5,
                TransportMaxAttempts = 3
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddDbContext<PortalDbContext>(options =>
            options.UseSqlite($"Data Source={Path.Combine(_scratch, dbName)}"));
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Database.MigrateAsync();
        return (provider, config);
    }

    private static async Task SeedAuditAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = new AuditService(db, new HttpContextAccessor());
        await audit.LogAsync(42, "CREATE_USER", "User", "42", "created");
    }

    private static AuditOutboxTransportService NewService(
        ServiceProvider provider,
        PortalConfig config,
        CapturingHandler handler) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) },
            TimeProvider.System,
            NullLogger<AuditOutboxTransportService>.Instance);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
