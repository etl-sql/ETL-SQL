using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

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
        // The folder belongs to a different user: folder ownership implies Manage (P1.5), and
        // this test exercises ACL-derived read loss, which only applies to non-owners.
        var folderOwner = new PortalUser
        {
            UserName = $"folder-owner-{suffix}",
            Email = $"folder-owner-{suffix}@test.local",
            IsActive = true
        };
        var group = new Group { Name = $"subscription-group-{suffix}" };
        db.Users.Add(owner);
        db.Users.Add(folderOwner);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var folder = new Folder
        {
            Name = $"Subscription Folder {suffix}",
            Path = $"/subscription-{suffix}",
            OwnerId = folderOwner.Id
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

        var smtp = SmtpCatalogSeed.Add(db, $"smtp-{suffix}", username: "smtp-user");
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
            new PortalConnectionCatalogService(db),
            new FolderPermissionService(db),
            new AuditService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor()),
            runner,
            NullLogger<SubscriptionDeliveryService>.Instance);

        var result = await service.DeliverAsync(subscription.Id);

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(runnerShouldExecute, runner.CallCount == 1);

        if (runnerShouldExecute)
        {
            // The delivery script must carry the SECRET: reference, never a resolved credential:
            // the Portal no longer holds the plaintext, and the engine resolves it on connect.
            // This assertion was previously the inverse — it required the password to appear.
            Assert.Contains("PASSWORD = 'SECRET:", runner.Script, StringComparison.Ordinal);
            Assert.DoesNotContain(smtpPassword, runner.Script, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("recipient-rls@test.local", true)]   // resolvable portal user → runs under their identity
    [InlineData("stranger@external.test", false)]     // not a portal user → cannot filter → fails clearly
    public async Task RlsReport_DeliversUnderRecipientIdentity_OrFailsForUnknownRecipient(
        string recipientEmail, bool recipientIsKnownUser)
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var protector = scope.ServiceProvider.GetRequiredService<SmtpPasswordProtector>();
        var suffix = Guid.NewGuid().ToString("N");

        var owner = new PortalUser { UserName = $"owner-{suffix}", Email = $"owner-{suffix}@test.local", IsActive = true };
        db.Users.Add(owner);
        var ownerGroup = new Group { Name = $"owner-grp-{suffix}" };
        db.Groups.Add(ownerGroup);
        await db.SaveChangesAsync();

        var folder = new Folder { Name = $"F {suffix}", Path = $"/f-{suffix}", OwnerId = owner.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = owner.Id, GroupId = ownerGroup.Id });
        db.FolderAcls.Add(new FolderAcl { FolderId = folder.Id, GroupId = ownerGroup.Id, Permission = FolderPermission.Read });

        // Recipient is a portal user in a region group only in the resolvable case.
        var region = new Group { Name = $"Region:East {suffix}" };
        db.Groups.Add(region);
        await db.SaveChangesAsync();
        if (recipientIsKnownUser)
        {
            var recipient = new PortalUser
            {
                UserName = $"rls-recipient-{suffix}",
                Email = recipientEmail,
                NormalizedEmail = recipientEmail.ToUpperInvariant(),
                IsActive = true
            };
            db.Users.Add(recipient);
            await db.SaveChangesAsync();
            db.UserGroups.Add(new UserGroup { UserId = recipient.Id, GroupId = region.Id });
        }

        // Identity-sensitive report (references HAS_GROUP).
        var scriptPath = Path.Combine(config.ScriptRootPath, $"rls-delivery-{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SELECT 1 AS Value INTO #data WHERE HAS_GROUP('Region:East');");
        var report = new Report { FolderId = folder.Id, Name = $"R {suffix}", ScriptPath = scriptPath, CreatedBy = owner.Id };
        db.Reports.Add(report);

        var smtp = SmtpCatalogSeed.Add(db, $"smtp-{suffix}", username: "u");
        await db.SaveChangesAsync();

        var subscription = new Subscription
        {
            ReportId = report.Id,
            UserId = owner.Id,
            Format = SubscriptionFormat.Link,
            SmtpAlias = smtp.Alias,
            Recipients = recipientEmail,
            IsActive = true
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var runner = new RecordingSubscriptionRunner();
        var service = new SubscriptionDeliveryService(db, config, new PortalConnectionCatalogService(db), new FolderPermissionService(db),
            new AuditService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor()),
            runner, NullLogger<SubscriptionDeliveryService>.Instance);

        var result = await service.DeliverAsync(subscription.Id);

        var ledgerDetail = (await db.SubscriptionDeliveries
            .Where(d => d.SubscriptionId == subscription.Id)
            .OrderByDescending(d => d.Id)
            .FirstOrDefaultAsync())?.Detail;

        if (recipientIsKnownUser)
        {
            Assert.True(runner.CallCount == 1, $"runner not called; ledger detail: {ledgerDetail}");
            Assert.NotNull(runner.LastIdentity);
            Assert.Equal($"rls-recipient-{suffix}", runner.LastIdentity!.EffectiveUser);
            Assert.Contains($"Region:East {suffix}", runner.LastIdentity.Groups);
        }
        else
        {
            // Unknown recipient: never executed, and the failure reason is explicit in the ledger.
            Assert.Equal(0, runner.CallCount);
            Assert.Equal(SubscriptionDeliveryOutcome.Failed, result.Outcome);
            Assert.Contains("not a known portal user", ledgerDetail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Reconcile_RewritesLegacySecretScriptsAndRemovesOrphans()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var suffix = Guid.NewGuid().ToString("N");
        const string secretMarker = "LEGACY_SMTP_PASSWORD_MARKER";

        var owner = new PortalUser
        {
            UserName = $"reconcile-owner-{suffix}",
            Email = $"reconcile-owner-{suffix}@test.local",
            IsActive = true
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var folder = new Folder
        {
            Name = $"Reconcile Folder {suffix}",
            Path = $"/reconcile-{suffix}",
            OwnerId = owner.Id
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var report = new Report
        {
            FolderId = folder.Id,
            Name = $"Reconcile Report {suffix}",
            ScriptPath = Path.Combine(config.ScriptRootPath, $"reconcile-{suffix}.rptsql"),
            CreatedBy = owner.Id
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        var subscription = new Subscription
        {
            ReportId = report.Id,
            UserId = owner.Id,
            Format = SubscriptionFormat.CSV,
            SmtpAlias = "legacy",
            Recipients = "legacy@test.local",
            IsActive = true
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var subscriptionDir = Path.Combine(config.ScriptRootPath, "subscriptions");
        Directory.CreateDirectory(subscriptionDir);

        // A pre-upgrade script that embedded the decrypted SMTP credential.
        var legacyPath = Path.Combine(subscriptionDir, $"sub_{subscription.Id}_Legacy.etlsql");
        await File.WriteAllTextAsync(legacyPath,
            $"CREATE CONNECTION __sub_smtp AS SMTP(HOST = 'smtp', PASSWORD = '{secretMarker}');");
        subscription.ScriptPath = legacyPath;
        await db.SaveChangesAsync();

        // A generated script whose subscription no longer exists, and an unrelated file.
        var orphanPath = Path.Combine(subscriptionDir, "sub_999999_Deleted.etlsql");
        await File.WriteAllTextAsync(orphanPath, $"PASSWORD = '{secretMarker}';");
        var unrelatedPath = Path.Combine(subscriptionDir, "notes.etlsql");
        await File.WriteAllTextAsync(unrelatedPath, "PRINT 'operator-authored file';");

        await SubscriptionScriptMaintenance.ReconcileAsync(
            db, config, orchestratorDbPath: null, NullLogger.Instance);

        var rewritten = await File.ReadAllTextAsync(legacyPath);
        Assert.Equal(SubscriptionTriggerScript.Compose(subscription.Id), rewritten);
        Assert.DoesNotContain(secretMarker, rewritten, StringComparison.Ordinal);
        Assert.False(File.Exists(orphanPath));
        Assert.True(File.Exists(unrelatedPath));

        // Idempotent on a clean tree: a second pass leaves the trigger untouched.
        var beforeSecondPass = File.GetLastWriteTimeUtc(legacyPath);
        await SubscriptionScriptMaintenance.ReconcileAsync(
            db, config, orchestratorDbPath: null, NullLogger.Instance);
        Assert.Equal(beforeSecondPass, File.GetLastWriteTimeUtc(legacyPath));
    }

    private sealed class RecordingSubscriptionRunner : ISubscriptionScriptRunner
    {
        public int CallCount { get; private set; }
        public string? Script { get; private set; }
        public ETL_SQL.Core.Governance.ExecutionIdentity? LastIdentity { get; private set; }

        public Task<(bool Success, string? Error)> RunAsync(
            string scriptText,
            string sessionId,
            CancellationToken ct,
            ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null)
        {
            CallCount++;
            Script = scriptText;
            LastIdentity = executionIdentity;
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }
}

