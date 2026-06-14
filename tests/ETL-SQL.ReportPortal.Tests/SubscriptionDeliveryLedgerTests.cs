using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// Subscription delivery idempotency and failure modes. Deliveries are claimed in the durable
/// <see cref="SubscriptionDelivery"/> ledger keyed on (subscription, trigger, recipient key),
/// giving at-most-once delivery per recipient and scheduler completion.
/// </summary>
[Trait("Category", "Portal")]
public sealed class SubscriptionDeliveryLedgerTests
{
    /// <summary>A runner whose result is configurable per scenario, recording every invocation.</summary>
    private sealed class ConfigurableRunner : ISubscriptionScriptRunner
    {
        private readonly Func<int, (bool, string?)> _result;
        public int CallCount { get; private set; }

        public ConfigurableRunner(Func<int, (bool, string?)> result) => _result = result;
        public static ConfigurableRunner Succeeds() => new(_ => (true, null));
        public static ConfigurableRunner Fails(string error) => new(_ => (false, error));
        public static ConfigurableRunner Throws(string message) =>
            new(_ => throw new InvalidOperationException(message));

        public Task<(bool Success, string? Error)> RunAsync(string scriptText, string sessionId, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result(CallCount));
        }
    }

    private sealed record Harness(
        PortalDbContext Db, SubscriptionDeliveryService Service, int SubscriptionId, string Suffix);

    private static async Task<Harness> SeedAsync(
        IServiceScope scope,
        ConfigurableRunner runner,
        string? smtpAlias = null,
        bool ownerActive = true,
        string recipients = "recipient@test.local")
    {
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var protector = scope.ServiceProvider.GetRequiredService<SmtpPasswordProtector>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var owner = new PortalUser
        {
            UserName = $"owner_{suffix}",
            Email = $"owner_{suffix}@test.local",
            IsActive = ownerActive
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var folder = new Folder { Name = $"f_{suffix}", Path = $"/f_{suffix}", OwnerId = owner.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var reportScriptPath = Path.Combine(config.ScriptRootPath, $"ledger_{suffix}.rptsql");
        await File.WriteAllTextAsync(reportScriptPath, "SELECT 1 AS Value INTO #data;");
        var report = new Report
        {
            FolderId = folder.Id,
            Name = $"Report {suffix}",
            ScriptPath = reportScriptPath,
            CreatedBy = owner.Id
        };
        db.Reports.Add(report);

        var alias = $"smtp_{suffix}";
        db.SmtpConnections.Add(new SmtpConnection
        {
            Alias = alias,
            Host = "smtp.test.local",
            Port = 2525,
            EncryptedPassword = protector.Protect("pw-marker"),
            FromAddress = "portal@test.local",
            UseSsl = false
        });
        await db.SaveChangesAsync();

        var subscription = new Subscription
        {
            ReportId = report.Id,
            UserId = owner.Id,
            Format = SubscriptionFormat.CSV,
            SmtpAlias = smtpAlias ?? alias,
            Recipients = recipients,
            IsActive = true
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var service = new SubscriptionDeliveryService(
            db, config, protector, new FolderPermissionService(db),
            new AuditService(db, new HttpContextAccessor()), runner,
            NullLogger<SubscriptionDeliveryService>.Instance);

        return new Harness(db, service, subscription.Id, suffix);
    }

    [Fact]
    public async Task DuplicateTrigger_IsSuppressed_AtMostOnce()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = ConfigurableRunner.Succeeds();
        var h = await SeedAsync(scope, runner);

        var first = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-A");
        var second = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-A");

        Assert.Equal(SubscriptionDeliveryOutcome.Delivered, first.Outcome);
        Assert.Equal(SubscriptionDeliveryOutcome.Skipped, second.Outcome);
        Assert.Equal(1, runner.CallCount); // the duplicate never re-ran the delivery
        Assert.Equal(1, await h.Db.SubscriptionDeliveries
            .CountAsync(d => d.SubscriptionId == h.SubscriptionId));
        var ledger = await h.Db.SubscriptionDeliveries.SingleAsync(d => d.SubscriptionId == h.SubscriptionId);
        Assert.Equal("Delivered", ledger.Outcome);
        Assert.False(string.IsNullOrEmpty(ledger.DeliveryId));
    }

    [Fact]
    public async Task DistinctTriggers_EachDeliver()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = ConfigurableRunner.Succeeds();
        var h = await SeedAsync(scope, runner);

        await h.Service.DeliverAsync(h.SubscriptionId, "trigger-A");
        await h.Service.DeliverAsync(h.SubscriptionId, "trigger-B");

        Assert.Equal(2, runner.CallCount);
        Assert.Equal(2, await h.Db.SubscriptionDeliveries.CountAsync(d => d.SubscriptionId == h.SubscriptionId));
    }

    [Fact]
    public async Task SmtpTimeoutAfterAcceptance_RecordsFailed_Observable()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = ConfigurableRunner.Fails("Delivery timed out.");
        var h = await SeedAsync(scope, runner);

        var result = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-timeout");

        Assert.Equal(SubscriptionDeliveryOutcome.Failed, result.Outcome);
        var ledger = await h.Db.SubscriptionDeliveries.SingleAsync(d => d.SubscriptionId == h.SubscriptionId);
        Assert.Equal("Failed", ledger.Outcome);
        Assert.Contains("timed out", ledger.Detail);
        Assert.NotNull(ledger.CompletedAt);
        var sub = await h.Db.Subscriptions.SingleAsync(s => s.Id == h.SubscriptionId);
        Assert.Equal(1, sub.FailCount);
    }

    [Fact]
    public async Task AttachmentGenerationFailure_RecordsFailed()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = ConfigurableRunner.Fails("EXPORT REPORT failed: could not render PDF.");
        var h = await SeedAsync(scope, runner);

        var result = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-attach");

        Assert.Equal(SubscriptionDeliveryOutcome.Failed, result.Outcome);
        var ledger = await h.Db.SubscriptionDeliveries.SingleAsync(d => d.SubscriptionId == h.SubscriptionId);
        Assert.Equal("Failed", ledger.Outcome);
        Assert.Contains("EXPORT REPORT failed", ledger.Detail);
    }

    [Fact]
    public async Task UnknownOutcome_RunnerThrows_RecordedFailed_NotReclaimed()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = ConfigurableRunner.Throws("connection reset mid-send");
        var h = await SeedAsync(scope, runner);

        var result = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-crash");
        Assert.Equal(SubscriptionDeliveryOutcome.Failed, result.Outcome);

        // The same trigger is never retried — at-most-once holds even for an unknown outcome.
        var retry = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-crash");
        Assert.Equal(SubscriptionDeliveryOutcome.Skipped, retry.Outcome);
        Assert.Equal(1, runner.CallCount);
        var ledger = await h.Db.SubscriptionDeliveries.SingleAsync(d => d.SubscriptionId == h.SubscriptionId);
        Assert.Equal("Failed", ledger.Outcome);
    }

    [Fact]
    public async Task MissingSmtpAlias_RecordsFailed()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = ConfigurableRunner.Succeeds();
        var h = await SeedAsync(scope, runner, smtpAlias: "no-such-alias");

        var result = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-nosmtp");

        Assert.Equal(SubscriptionDeliveryOutcome.Failed, result.Outcome);
        Assert.Equal(0, runner.CallCount); // never composed/ran without an SMTP connection
        var ledger = await h.Db.SubscriptionDeliveries.SingleAsync(d => d.SubscriptionId == h.SubscriptionId);
        Assert.Equal("Failed", ledger.Outcome);
        Assert.Contains("no-such-alias", ledger.Detail);
    }

    [Fact]
    public async Task DeniedDelivery_IsIsolated_FromAHealthyDelivery()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = ConfigurableRunner.Succeeds();

        var denied = await SeedAsync(scope, runner, ownerActive: false); // disabled owner ⇒ denied
        var healthy = await SeedAsync(scope, runner);

        var deniedResult = await denied.Service.DeliverAsync(denied.SubscriptionId, "t");
        var healthyResult = await healthy.Service.DeliverAsync(healthy.SubscriptionId, "t");

        Assert.Equal(SubscriptionDeliveryOutcome.Denied, deniedResult.Outcome);
        Assert.Equal(SubscriptionDeliveryOutcome.Delivered, healthyResult.Outcome);

        var deniedLedger = await denied.Db.SubscriptionDeliveries
            .SingleAsync(d => d.SubscriptionId == denied.SubscriptionId);
        Assert.Equal("Denied", deniedLedger.Outcome);
        var healthyLedger = await healthy.Db.SubscriptionDeliveries
            .SingleAsync(d => d.SubscriptionId == healthy.SubscriptionId);
        Assert.Equal("Delivered", healthyLedger.Outcome);
    }

    [Fact]
    public async Task MixedValidAndInvalidRecipients_AreIsolated()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = ConfigurableRunner.Succeeds();
        var h = await SeedAsync(
            scope,
            runner,
            recipients: "GOOD@test.local; not-an-address; second@test.local");

        var result = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-mixed");

        Assert.Equal(SubscriptionDeliveryOutcome.Failed, result.Outcome);
        Assert.Equal(2, runner.CallCount);
        var rows = await h.Db.SubscriptionDeliveries
            .Where(d => d.SubscriptionId == h.SubscriptionId)
            .ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows.Count(row => row.Outcome == "Delivered"));
        Assert.Single(rows, row => row.Outcome == "Failed");
        Assert.Contains(rows, row => row.Recipients == "good@test.local");
        Assert.Contains(rows, row => row.Recipients == "second@test.local");
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.RecipientKey)));
    }

    [Fact]
    public async Task PartialSmtpRejection_DoesNotBlockOtherRecipients()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = new ConfigurableRunner(call =>
            call == 1
                ? (false, "SMTP rejected rejected@test.local.")
                : (true, null));
        var h = await SeedAsync(
            scope,
            runner,
            recipients: "rejected@test.local; accepted@test.local");

        var result = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-partial");

        Assert.Equal(SubscriptionDeliveryOutcome.Failed, result.Outcome);
        Assert.Equal(2, runner.CallCount);
        var rows = await h.Db.SubscriptionDeliveries
            .Where(d => d.SubscriptionId == h.SubscriptionId)
            .OrderBy(d => d.Id)
            .ToListAsync();
        Assert.Equal(["Failed", "Delivered"], rows.Select(row => row.Outcome));
        Assert.DoesNotContain("rejected@test.local", rows[0].Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateTrigger_IsSuppressedForEveryRecipient()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = ConfigurableRunner.Succeeds();
        var h = await SeedAsync(
            scope,
            runner,
            recipients: "one@test.local; two@test.local");

        var first = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-multi");
        var duplicate = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-multi");

        Assert.Equal(SubscriptionDeliveryOutcome.Delivered, first.Outcome);
        Assert.Equal(SubscriptionDeliveryOutcome.Skipped, duplicate.Outcome);
        Assert.Equal(2, runner.CallCount);
        Assert.Equal(2, await h.Db.SubscriptionDeliveries.CountAsync(
            row => row.SubscriptionId == h.SubscriptionId));
    }

    [Fact]
    public async Task UnknownOutcome_CanRetryOnANewTrigger()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var runner = new ConfigurableRunner(call =>
            call == 1
                ? throw new InvalidOperationException("connection reset mid-send")
                : (true, null));
        var h = await SeedAsync(scope, runner);

        var unknown = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-unknown");
        var retry = await h.Service.DeliverAsync(h.SubscriptionId, "trigger-retry");

        Assert.Equal(SubscriptionDeliveryOutcome.Failed, unknown.Outcome);
        Assert.Equal(SubscriptionDeliveryOutcome.Delivered, retry.Outcome);
        Assert.Equal(2, runner.CallCount);
        Assert.Equal(2, await h.Db.SubscriptionDeliveries.CountAsync(
            row => row.SubscriptionId == h.SubscriptionId));
    }
}
