using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Common;
using ETL_SQL.Connectors.ReportPortal;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// P1.9 deterministic import: replaying the bootstrap admin statements through the PORTAL
/// connector is idempotent (create-or-skip — safe to rerun), fails closed on a missing reference
/// or unsubstituted secret placeholder before any mutation, and offers a read-only validating
/// dry-run under <c>SET WHAT_IF ON</c>. Driven against the in-memory portal via the connector's
/// injectable-HttpClient constructor.
/// </summary>
[Trait("Category", "Portal")]
public sealed class ScriptedPortalImportTests
{
    /// <summary>Resolves string-literal secret expressions so the connector can read placeholders.</summary>
    private sealed class LiteralEvalContext : SystemExecutionContext
    {
        public override ValueTask<object?> EvaluateValue(Expression? expr, Row context, bool decryptSensitive = false) =>
            new(expr is LiteralExpression lit ? lit.Value : null);
    }

    private static LiteralExpression Secret(string value) =>
        new(value, ETL_SQL.Core.Parser.TokenType.STRING);

    /// <summary>Changes the seeded admin password, then builds a connector authenticated as admin
    /// over the factory's in-memory server.</summary>
    private static async Task<ReportPortalDataSource> ConnectAsAdminAsync(PortalWebFactory factory)
    {
        using var setup = factory.CreateClient();
        var login = await setup.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })
        };
        change.Headers.Authorization = new("Bearer", token);
        (await setup.SendAsync(change)).EnsureSuccessStatusCode();

        var http = factory.CreateClient();
        return new ReportPortalDataSource(http, "admin", "Admin@Tests99!",
            SystemExecutionContext.Instance.Logger);
    }

    [Fact]
    public async Task Replay_IsIdempotent_CreateOrSkip()
    {
        using var factory = new PortalWebFactory();
        var connector = await ConnectAsAdminAsync(factory);
        var ctx = new LiteralEvalContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var statements = new Statement[]
        {
            new CreatePortalGroupStatement($"grp_{suffix}", "Import group", null, null),
            new CreatePortalUserStatement($"user_{suffix}", $"user_{suffix}@test.local",
                Secret("Imported@12345!"), "Publisher", null, null, null),
            new AddUserToPortalGroupStatement($"user_{suffix}", $"grp_{suffix}"),
            new CreatePortalFolderStatement($"/folder_{suffix}"),
            new GrantPortalPermissionStatement($"/folder_{suffix}", $"grp_{suffix}",
                PortalFolderPermission.Read),
        };

        // Two full passes: the second must skip every existing object without error.
        foreach (var _ in Enumerable.Range(0, 2))
            foreach (var stmt in statements)
                await connector.ExecuteAdminStatementAsync(stmt, ctx);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal(1, await db.Groups.CountAsync(g => g.Name == $"grp_{suffix}"));
        Assert.Equal(1, await db.Users.CountAsync(u => u.UserName == $"user_{suffix}"));
        Assert.Equal(1, await db.Folders.CountAsync(f => f.Path == $"/folder_{suffix}"));
        var group = await db.Groups.SingleAsync(g => g.Name == $"grp_{suffix}");
        var user = await db.Users.SingleAsync(u => u.UserName == $"user_{suffix}");
        Assert.Equal(1, await db.UserGroups.CountAsync(ug => ug.GroupId == group.Id && ug.UserId == user.Id));
        var folder = await db.Folders.SingleAsync(f => f.Path == $"/folder_{suffix}");
        Assert.Equal(1, await db.FolderAcls.CountAsync(a => a.FolderId == folder.Id && a.GroupId == group.Id));
    }

    [Fact]
    public async Task DryRun_ReportsPlan_WithoutMutating()
    {
        using var factory = new PortalWebFactory();
        var connector = await ConnectAsAdminAsync(factory);
        var ctx = new LiteralEvalContext { IsWhatIf = true };
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = new CreatePortalGroupStatement($"plan_{suffix}", null, null, null);

        var plan = await connector.PlanAdminStatementAsync(create, ctx);
        Assert.Contains("would create group", plan);

        // Dry-run must not have created anything.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.False(await db.Groups.AnyAsync(g => g.Name == $"plan_{suffix}"));
        }

        // After a real create, the plan reports a skip.
        await connector.ExecuteAdminStatementAsync(create, new LiteralEvalContext());
        var plan2 = await connector.PlanAdminStatementAsync(create, ctx);
        Assert.Contains("already exists", plan2);
    }

    [Fact]
    public async Task MissingSecret_FailsClosed_BeforeMutation()
    {
        using var factory = new PortalWebFactory();
        var connector = await ConnectAsAdminAsync(factory);
        var ctx = new LiteralEvalContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var stmt = new CreatePortalUserStatement($"nosecret_{suffix}", $"nosecret_{suffix}@test.local",
            Secret("${PORTAL_USER_NOSECRET_PASSWORD}"), "Viewer", null, null, null);

        var ex = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(
            () => connector.ExecuteAdminStatementAsync(stmt, ctx));
        Assert.Contains("Required secret", ex.Message);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.UserName == $"nosecret_{suffix}"));
    }

    [Fact]
    public async Task MissingReference_FailsClosed_WithClearError()
    {
        using var factory = new PortalWebFactory();
        var connector = await ConnectAsAdminAsync(factory);
        var ctx = new LiteralEvalContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Add an existing admin to a group that does not exist.
        var stmt = new AddUserToPortalGroupStatement("admin", $"ghost_grp_{suffix}");
        var ex = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(
            () => connector.ExecuteAdminStatementAsync(stmt, ctx));
        Assert.Contains("group", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscriptionAndAlert_ReplayIsIdempotent_AndUpdatesByNaturalKey()
    {
        using var factory = new PortalWebFactory();
        var connector = await ConnectAsAdminAsync(factory);
        var context = new LiteralEvalContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportName = $"Import Report {suffix}";
        var reportPath = $"/import_{suffix}/{reportName}";
        var subscriptionName = $"Import Subscription {suffix}";
        var alertName = $"Import Alert {suffix}";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
            var adminId = await db.Users
                .Where(user => user.UserName == "admin")
                .Select(user => user.Id)
                .SingleAsync();
            var folder = new Folder
            {
                Name = $"import_{suffix}",
                Path = $"/import_{suffix}",
                OwnerId = adminId
            };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            db.Reports.Add(new Report
            {
                FolderId = folder.Id,
                Name = reportName,
                ScriptPath = Path.Combine(config.ScriptRootPath, $"import_{suffix}.rptsql"),
                CreatedBy = adminId
            });
            db.SmtpConnections.Add(new SmtpConnection
            {
                Alias = $"smtp_{suffix}",
                Host = "smtp.test.local",
                Port = 2525,
                FromAddress = "reports@test.local",
                UseSsl = false
            });
            await db.SaveChangesAsync();
        }

        var subscription = new CreatePortalSubscriptionStatement(
            reportPath,
            "recipient@test.local",
            false,
            "Daily",
            false,
            PortalSubscriptionFormat.Pdf,
            $"smtp_{suffix}",
            subscriptionName,
            [new SubscriptionParameter("@region", "North")],
            IsActive: false);
        var alert = new CreatePortalAlertStatement(
            reportPath,
            alertName,
            "Revenue",
            ">=",
            1000m,
            "recipient@test.local",
            $"smtp_{suffix}",
            IsActive: false);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            await connector.ExecuteAdminStatementAsync(subscription, context);
            await connector.ExecuteAdminStatementAsync(alert, context);
        }

        var subscriptionUpdate = subscription with
        {
            Recipient = "updated-recipient@test.local",
            Schedule = "Weekly",
            Parameters = [new SubscriptionParameter("@region", "South")],
            IsActive = true
        };
        var alertUpdate = alert with { Threshold = 2000m, IsActive = true };
        Assert.Contains("would update", await connector.PlanAdminStatementAsync(subscriptionUpdate, context));
        Assert.Contains("would update", await connector.PlanAdminStatementAsync(alertUpdate, context));
        await connector.ExecuteAdminStatementAsync(subscriptionUpdate, context);
        await connector.ExecuteAdminStatementAsync(alertUpdate, context);
        Assert.Contains("would skip", await connector.PlanAdminStatementAsync(subscriptionUpdate, context));
        Assert.Contains("would skip", await connector.PlanAdminStatementAsync(alertUpdate, context));

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var storedSubscription = await verifyDb.Subscriptions.SingleAsync(value => value.Name == subscriptionName);
        var storedAlert = await verifyDb.ReportAlerts.SingleAsync(value => value.Name == alertName);
        Assert.Equal("Weekly", storedSubscription.Schedule);
        Assert.Equal("updated-recipient@test.local", storedSubscription.Recipients);
        Assert.True(storedSubscription.IsActive);
        Assert.Contains("South", storedSubscription.ParametersJson);
        Assert.Equal(2000m, storedAlert.Threshold);
        Assert.True(storedAlert.IsActive);
    }
}
