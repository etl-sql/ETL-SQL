using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

public class SubscriptionDeliverySecurityTests
{
    [Theory]
    [InlineData(true, true, SubscriptionDeliveryOutcome.Delivered, true)]
    [InlineData(false, true, SubscriptionDeliveryOutcome.Denied, false)]
    [InlineData(true, false, SubscriptionDeliveryOutcome.Denied, false)]
    public async Task Delivery_ReauthorizesOwnerAndFolderPermission(
        bool ownerIsActive,
        bool retainFolderRead,
        SubscriptionDeliveryOutcome expectedOutcome,
        bool runnerShouldExecute)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var protector = scope.ServiceProvider.GetRequiredService<SmtpPasswordProtector>();
        var suffix = Guid.NewGuid().ToString("N");
        const string smtpPassword = "delivery-password-marker";

        var owner = new PortalUser
        {
            UserName = $"subscription-owner-{suffix}",
            Email = $"subscription-owner-{suffix}@test.local",
            IsActive = ownerIsActive
        };
        var group = new Group { Name = $"subscription-group-{suffix}" };
        db.Users.Add(owner);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var folder = new Folder
        {
            Name = $"Subscription Folder {suffix}",
            Path = $"/subscription-{suffix}",
            OwnerId = owner.Id
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        db.UserGroups.Add(new UserGroup { UserId = owner.Id, GroupId = group.Id });
        if (retainFolderRead)
        {
            db.FolderAcls.Add(new FolderAcl
            {
                FolderId = folder.Id,
                GroupId = group.Id,
                Permission = FolderPermission.Read
            });
        }

        var reportScriptPath = Path.Combine(config.ScriptRootPath, $"delivery-{suffix}.rptsql");
        await File.WriteAllTextAsync(reportScriptPath, "SELECT 1 AS Value INTO #data;");
        var report = new Report
        {
            FolderId = folder.Id,
            Name = $"Delivery Report {suffix}",
            ScriptPath = reportScriptPath,
            CreatedBy = owner.Id
        };
        db.Reports.Add(report);

        var smtp = new SmtpConnection
        {
            Alias = $"smtp-{suffix}",
            Host = "smtp.test.local",
            Port = 2525,
            Username = "smtp-user",
            EncryptedPassword = protector.Protect(smtpPassword),
            FromAddress = "portal@test.local",
            UseSsl = false
        };
        db.SmtpConnections.Add(smtp);
        await db.SaveChangesAsync();

        var subscription = new Subscription
        {
            ReportId = report.Id,
            UserId = owner.Id,
            Format = SubscriptionFormat.Link,
            SmtpAlias = smtp.Alias,
            Recipients = "recipient@test.local",
            IsActive = true
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var runner = new RecordingSubscriptionRunner();
        var service = new SubscriptionDeliveryService(
            db,
            config,
            protector,
            new FolderPermissionService(db),
            new AuditService(db),
            runner,
            NullLogger<SubscriptionDeliveryService>.Instance);

        var result = await service.DeliverAsync(subscription.Id);

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(runnerShouldExecute, runner.CallCount == 1);

        if (runnerShouldExecute)
        {
            Assert.Contains(smtpPassword, runner.Script, StringComparison.Ordinal);
            Assert.Contains("recipient@test.local", runner.Script, StringComparison.Ordinal);
            Assert.NotNull(subscription.LastSentAt);
            Assert.Equal(0, subscription.FailCount);
        }
        else
        {
            Assert.Null(runner.Script);
            Assert.Null(subscription.LastSentAt);
            Assert.Equal(0, subscription.FailCount);
            Assert.True(await db.AuditLogs.AnyAsync(a =>
                a.Action == "SUBSCRIPTION_DELIVERY_DENIED"
                && a.ResourceId == subscription.Id.ToString()));
        }

        var persistedTrigger = SubscriptionTriggerScript.Compose(subscription.Id);
        Assert.DoesNotContain(smtpPassword, persistedTrigger, StringComparison.Ordinal);
        Assert.DoesNotContain(subscription.Recipients, persistedTrigger, StringComparison.Ordinal);
        Assert.DoesNotContain(smtp.Alias, persistedTrigger, StringComparison.Ordinal);
    }

    private sealed class RecordingSubscriptionRunner : ISubscriptionScriptRunner
    {
        public int CallCount { get; private set; }
        public string? Script { get; private set; }

        public Task<(bool Success, string? Error)> RunAsync(
            string scriptText,
            string sessionId,
            CancellationToken ct)
        {
            CallCount++;
            Script = scriptText;
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }
}
