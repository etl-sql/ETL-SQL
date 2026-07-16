using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// P2.2 multi-instance coordination. The Portal now supports multiple processes over shared state
/// (see <c>ExecutionJobServiceTests.StartAsync_AllowsMultiplePortalInstancesUsingSharedState</c>);
/// the Orchestrator job lease proves two scheduler instances run a due job exactly once
/// (<c>JobExecutionLeaseTests</c>). This suite covers two independent subscription delivery executors —
/// separate services with separate database connections, a faithful proxy for two poller processes —
/// coordinating through the durable delivery ledger over one shared portal database, rather than through
/// shared memory.
/// </summary>
[Trait("Category", "Portal")]
public sealed class MultiInstanceCoordinationTests
{
    private sealed class CountingRunner : ISubscriptionScriptRunner
    {
        private int _calls;
        public int CallCount => _calls;
        public Task<(bool Success, string? Error)> RunAsync(string scriptText, string sessionId, CancellationToken ct,
            ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    private sealed record Fixture(
        PortalConfig Config, SmtpPasswordProtector Protector, PortalPiiProtector PiiProtector,
        string DbPath, int SubscriptionId);

    /// <summary>Seeds a deliverable subscription, then yields the pieces needed to build independent
    /// delivery executors over fresh connections to the same portal database.</summary>
    private static async Task<Fixture> SeedAsync(PortalWebFactory factory, string suffix)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var protector = scope.ServiceProvider.GetRequiredService<SmtpPasswordProtector>();
        var piiProtector = scope.ServiceProvider.GetRequiredService<PortalPiiProtector>();

        var owner = new PortalUser
        {
            UserName = $"owner_{suffix}",
            Email = $"owner_{suffix}@test.local",
            IsActive = true
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var folder = new Folder { Name = $"f_{suffix}", Path = $"/f_{suffix}", OwnerId = owner.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var scriptPath = Path.Combine(config.ScriptRootPath, $"mi_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SELECT 1 AS Value INTO #data;");
        var report = new Report
        {
            FolderId = folder.Id,
            Name = $"Report {suffix}",
            ScriptPath = scriptPath,
            CreatedBy = owner.Id
        };
        db.Reports.Add(report);
        db.SmtpConnections.Add(new SmtpConnection
        {
            Alias = $"smtp_{suffix}",
            Host = "smtp.test.local",
            Port = 2525,
            EncryptedPassword = protector.Protect("pw"),
            FromAddress = "portal@test.local",
            UseSsl = false
        });
        await db.SaveChangesAsync();

        var subscription = new Subscription
        {
            ReportId = report.Id,
            UserId = owner.Id,
            Format = SubscriptionFormat.CSV,
            SmtpAlias = $"smtp_{suffix}",
            Recipients = "r@test.local",
            IsActive = true
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return new Fixture(config, protector, piiProtector, config.DatabasePath, subscription.Id);
    }

    /// <summary>Builds an independent delivery executor over a fresh connection (its own DbContext,
    /// WAL + busy timeout) to the shared portal database — a stand-in for a second poller process.</summary>
    private static (SubscriptionDeliveryService Service, CountingRunner Runner, PortalDbContext Db) NewInstance(Fixture f)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={f.DbPath}")
            .UsePortalEncryption(f.PiiProtector)
            .Options;
        var db = new PortalDbContext(options);
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000;");
        var runner = new CountingRunner();
        var service = new SubscriptionDeliveryService(
            db, f.Config, f.Protector, new FolderPermissionService(db),
            new AuditService(db, new HttpContextAccessor()), runner,
            NullLogger<SubscriptionDeliveryService>.Instance);
        return (service, runner, db);
    }

    /// <summary>
    /// A second executor observing a completion the first already delivered sees the durable ledger
    /// row over its own connection and suppresses — exactly one delivery for the completion.
    /// </summary>
    [Fact]
    public async Task SecondInstance_SeesFirstDelivery_AndSuppresses()
    {
        using var factory = new PortalWebFactory();
        using var _ = factory.CreateClient();
        var f = await SeedAsync(factory, Guid.NewGuid().ToString("N")[..8]);

        var (svc1, run1, db1) = NewInstance(f);
        var (svc2, run2, db2) = NewInstance(f);
        await using var _1 = db1;
        await using var _2 = db2;

        var first = await svc1.DeliverAsync(f.SubscriptionId, "completion-T");
        var second = await svc2.DeliverAsync(f.SubscriptionId, "completion-T");

        Assert.Equal(SubscriptionDeliveryOutcome.Delivered, first.Outcome);
        Assert.Equal(SubscriptionDeliveryOutcome.Skipped, second.Outcome);
        Assert.Equal(1, run1.CallCount);
        Assert.Equal(0, run2.CallCount);
        Assert.Equal(1, await db1.SubscriptionDeliveries.CountAsync(d => d.SubscriptionId == f.SubscriptionId));
    }

    /// <summary>
    /// Two executors racing the same completion simultaneously: the ledger's unique index over the
    /// shared database lets exactly one win, the other is suppressed — no double-send.
    /// </summary>
    [Fact]
    public async Task ConcurrentInstances_SameCompletion_ExactlyOneDelivers()
    {
        using var factory = new PortalWebFactory();
        using var _ = factory.CreateClient();
        var f = await SeedAsync(factory, Guid.NewGuid().ToString("N")[..8]);

        var (svc1, run1, db1) = NewInstance(f);
        var (svc2, run2, db2) = NewInstance(f);
        await using var _1 = db1;
        await using var _2 = db2;

        var results = await Task.WhenAll(
            svc1.DeliverAsync(f.SubscriptionId, "race-T"),
            svc2.DeliverAsync(f.SubscriptionId, "race-T"));

        Assert.Single(results, r => r.Outcome == SubscriptionDeliveryOutcome.Delivered);
        Assert.Single(results, r => r.Outcome == SubscriptionDeliveryOutcome.Skipped);
        Assert.Equal(1, run1.CallCount + run2.CallCount); // the report ran exactly once
        Assert.Equal(1, await db1.SubscriptionDeliveries.CountAsync(d => d.SubscriptionId == f.SubscriptionId));
    }

    /// <summary>
    /// A scheduler/poller burst can cause many nodes to observe the same durable completion at once.
    /// The delivery ledger still permits exactly one recipient delivery.
    /// </summary>
    [Fact]
    public async Task ConcurrentInstances_SameCompletionBurst_ExactlyOneDelivers()
    {
        using var factory = new PortalWebFactory();
        using var _ = factory.CreateClient();
        var f = await SeedAsync(factory, Guid.NewGuid().ToString("N")[..8]);

        var instances = Enumerable.Range(0, 8)
            .Select(_ => NewInstance(f))
            .ToList();

        try
        {
            var results = await Task.WhenAll(instances.Select(instance =>
                instance.Service.DeliverAsync(f.SubscriptionId, "burst-T")));

            Assert.Single(results, r => r.Outcome == SubscriptionDeliveryOutcome.Delivered);
            Assert.Equal(7, results.Count(r => r.Outcome == SubscriptionDeliveryOutcome.Skipped));
            Assert.Equal(1, instances.Sum(instance => instance.Runner.CallCount));
            Assert.Equal(1, await instances[0].Db.SubscriptionDeliveries.CountAsync(
                d => d.SubscriptionId == f.SubscriptionId));
        }
        finally
        {
            foreach (var instance in instances)
                await instance.Db.DisposeAsync();
        }
    }

    /// <summary>Distinct completions are not falsely contended: each executor delivers its own.</summary>
    [Fact]
    public async Task ConcurrentInstances_DistinctCompletions_BothDeliver()
    {
        using var factory = new PortalWebFactory();
        using var _ = factory.CreateClient();
        var f = await SeedAsync(factory, Guid.NewGuid().ToString("N")[..8]);

        var (svc1, run1, db1) = NewInstance(f);
        var (svc2, run2, db2) = NewInstance(f);
        await using var _1 = db1;
        await using var _2 = db2;

        var results = await Task.WhenAll(
            svc1.DeliverAsync(f.SubscriptionId, "completion-A"),
            svc2.DeliverAsync(f.SubscriptionId, "completion-B"));

        Assert.All(results, r => Assert.Equal(SubscriptionDeliveryOutcome.Delivered, r.Outcome));
        Assert.Equal(2, await db1.SubscriptionDeliveries.CountAsync(d => d.SubscriptionId == f.SubscriptionId));
    }
}
