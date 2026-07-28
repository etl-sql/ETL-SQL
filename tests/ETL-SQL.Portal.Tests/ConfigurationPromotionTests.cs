using System.Net.Http.Json;
using ETL_SQL.Connectors.Portal;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// P1.9 promotion certification (dev → test/prod). A "dev" portal's configuration is exported and
/// promoted into a separate "prod" portal, supplying prod-specific secrets, rebinding the report
/// script root, and targeting the prod Orchestrator. The promoted assets must keep their
/// <b>logical identity</b> (group/folder/ACL/report/subscription names and structure equal dev's),
/// while every <b>environment-specific binding</b> — secrets, roots, and service accounts — is the
/// prod value, never carried over from dev. Re-promotion must be idempotent.
/// </summary>
[Trait("Category", "Portal")]
public sealed class ConfigurationPromotionTests
{
    private const string ProdSecret = "Prod@Secret#1!";
    private const string DevSmtpCipherPrefix = "DEVCIPHER-";
    private const string DevUserHashPrefix = "DEVHASH-";

    private sealed class LiteralEvalContext : SystemExecutionContext
    {
        public override ValueTask<object?> EvaluateValue(Expression? expr, Row context, bool decryptSensitive = false) =>
            new(expr is LiteralExpression lit ? lit.Value : null);
    }

    [Fact]
    public async Task Promotion_DevToProd_PreservesLogicalIdentity_AndRebindsEnvironmentSpecifics()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var dev = new PortalWebFactory();
        _ = dev.CreateClient();
        var devScriptPath = await SeedDevAsync(dev, suffix);

        // ── Export from dev, targeting the PROD orchestrator (service-account rebinding) ──
        string script;
        IReadOnlyList<string> requiredSecrets;
        IReadOnlyList<ConfigurationExportService.ContentManifestItem> manifest;
        using (var scope = dev.Services.CreateScope())
        {
            var export = await scope.ServiceProvider
                .GetRequiredService<ConfigurationExportService>()
                .GenerateAsync("prod_orchestrator");
            script = export.Script;
            requiredSecrets = export.RequiredSecrets;
            manifest = export.ContentManifest;
        }

        // Dev secrets are never in the export.
        Assert.NotEmpty(requiredSecrets);
        Assert.DoesNotContain($"{DevSmtpCipherPrefix}{suffix}", script);
        Assert.DoesNotContain($"{DevUserHashPrefix}{suffix}", script);

        // Supply prod-specific secret values for every placeholder.
        foreach (var placeholder in requiredSecrets)
            script = script.Replace($"${{{placeholder}}}", ProdSecret);

        // ── Promote into a fresh, separate prod portal ──────────────────────────────
        using var prod = new PortalWebFactory();
        _ = prod.CreateClient();

        // Root rebinding: the report script travels separately; copy it under the PROD script root
        // and repoint the publication at the prod path.
        var prodScriptRoot = Path.Combine(prod.TempDir, "scripts");
        foreach (var item in manifest.Where(m => m.Kind == "ReportScript"))
        {
            var prodPath = Path.Combine(prodScriptRoot, Path.GetFileName(item.Source!));
            File.Copy(item.Source!, prodPath, overwrite: true);
            script = script.Replace(item.Source!, prodPath, StringComparison.Ordinal);
        }

        var statements = ExtractAdminStatements(script);
        Assert.NotEmpty(statements);
        var connector = await ConnectAsAdminAsync(prod);
        var ctx = new LiteralEvalContext();
        // Replay twice — promotion must be idempotent.
        foreach (var _ in Enumerable.Range(0, 2))
            foreach (var stmt in statements)
                await connector.ExecuteAdminStatementAsync(stmt, ctx);

        // ── Verify the promoted prod state ──────────────────────────────────────────
        using var verify = prod.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<PortalDbContext>();

        // Logical identity preserved (environment-independent).
        Assert.Equal(1, await db.Groups.CountAsync(g => g.Name == $"finance_{suffix}"));
        Assert.True(await db.Folders.AnyAsync(f => f.Path == $"/root_{suffix}"));
        Assert.True(await (from a in db.FolderAcls
                           join f in db.Folders on a.FolderId equals f.Id
                           join g in db.Groups on a.GroupId equals g.Id
                           where f.Path == $"/root_{suffix}" && g.Name == $"finance_{suffix}"
                           select a).AnyAsync(a => a.Permission == FolderPermission.Manage));
        var report = await db.Reports.Include(r => r.Folder).SingleAsync(r => r.Name == $"report_{suffix}");
        Assert.Equal($"/root_{suffix}", report.Folder.Path);
        Assert.True(await db.Subscriptions.AnyAsync(s => s.Name == $"sub_{suffix}" && s.Schedule == "Daily"));

        // Secret rebinding: the promoted connection carries the environment-neutral SECRET:
        // reference, and no dev credential material travelled with it. Rebinding is now structural
        // rather than a re-encryption step — prod resolves the same reference from its own store.
        var smtp = await db.PortalSharedConnections.SingleAsync(c => c.Alias == $"corp_{suffix}");
        Assert.Equal("SMTP", smtp.ConnectorType, ignoreCase: true);
        Assert.Contains("SECRET:corp_smtp_password", smtp.OptionsJson);
        Assert.DoesNotContain(DevSmtpCipherPrefix, smtp.OptionsJson);
        Assert.DoesNotContain(ProdSecret, smtp.OptionsJson);
        var alice = await db.Users.SingleAsync(u => u.UserName == $"alice_{suffix}");
        Assert.NotEqual($"{DevUserHashPrefix}{suffix}", alice.PasswordHash);
        Assert.False(string.IsNullOrEmpty(alice.PasswordHash));

        // Root rebinding: the publication points under the prod script root, not dev's.
        Assert.StartsWith(prodScriptRoot, report.ScriptPath);
        Assert.True(File.Exists(report.ScriptPath));
        Assert.NotEqual(devScriptPath, report.ScriptPath);

        // Idempotent: the second promotion pass created no duplicates.
        Assert.Equal(1, await db.Reports.CountAsync(r => r.Name == $"report_{suffix}"));
        Assert.Equal(1, await db.Users.CountAsync(u => u.UserName == $"alice_{suffix}"));
        Assert.Equal(1, await db.PortalSharedConnections.CountAsync(c => c.Alias == $"corp_{suffix}"));
        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.Name == $"sub_{suffix}"));
    }

    private static async Task<string> SeedDevAsync(PortalWebFactory factory, string suffix)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();

        var alice = new PortalUser
        {
            UserName = $"alice_{suffix}",
            Email = $"alice_{suffix}@dev.local",
            IsActive = true,
            NormalizedUserName = $"ALICE_{suffix}".ToUpperInvariant(),
            PasswordHash = $"{DevUserHashPrefix}{suffix}" // dev-only secret; must never reach prod
        };
        db.Users.Add(alice);
        var finance = new Group { Name = $"finance_{suffix}", Description = "Finance" };
        db.Groups.Add(finance);
        await db.SaveChangesAsync();

        var publisherRoleId = await db.Roles.Where(r => r.Name == "Publisher").Select(r => r.Id).FirstAsync();
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = alice.Id, RoleId = publisherRoleId });

        var root = new Folder { Name = $"root_{suffix}", Path = $"/root_{suffix}", OwnerId = alice.Id };
        db.Folders.Add(root);
        await db.SaveChangesAsync();
        db.FolderAcls.Add(new FolderAcl { FolderId = root.Id, GroupId = finance.Id, Permission = FolderPermission.Manage });

        // The reference name is environment-neutral by design — that is what makes promotion work:
        // the same SECRET:name resolves to a different value from each environment's secret store.
        // The dev *value* (DevSmtpCipherPrefix) therefore never enters the catalog at all, which is
        // a stronger guarantee than the export merely omitting it.
        SmtpCatalogSeed.Add(db, $"corp_{suffix}", host: "smtp.dev.test", port: 587,
            username: "mailer", defaultFrom: "reports@dev.test", useSsl: true,
            passwordSecretRef: "SECRET:corp_smtp_password");

        var scriptPath = Path.Combine(config.ScriptRootPath, $"promote_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SELECT 1 AS Value INTO #data;");
        var report = new Report
        {
            FolderId = root.Id,
            Name = $"report_{suffix}",
            Description = "Promotion report",
            ScriptPath = scriptPath,
            CreatedBy = alice.Id
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        db.Subscriptions.Add(new Subscription
        {
            ReportId = report.Id,
            UserId = alice.Id,
            Name = $"sub_{suffix}",
            Schedule = "Daily",
            Format = SubscriptionFormat.PDF,
            SmtpAlias = $"corp_{suffix}",
            Recipients = $"team_{suffix}@dev.local",
            IsActive = true
        });
        await db.SaveChangesAsync();
        return scriptPath;
    }

    private static List<Statement> ExtractAdminStatements(string script)
    {
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        var result = new List<Statement>();
        foreach (var top in parsed.Statements)
        {
            switch (top)
            {
                case ExecutePushdownStatement push when !string.IsNullOrWhiteSpace(push.SqlText):
                    result.AddRange(new Parser(new Lexer(push.SqlText).Tokenize(), push.SqlText).Parse().Statements);
                    break;
                case ExecuteRemoteBlockStatement block:
                    result.AddRange(block.Body.Statements);
                    break;
            }
        }
        return result;
    }

    private static async Task<PortalDataSource> ConnectAsAdminAsync(PortalWebFactory factory)
    {
        using var setup = factory.CreateClient();
        var login = await setup.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["token"]!
            .GetValue<string>();
        var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })
        };
        change.Headers.Authorization = new("Bearer", token);
        (await setup.SendAsync(change)).EnsureSuccessStatusCode();

        return new PortalDataSource(factory.CreateClient(), "admin", "Admin@Tests99!",
            SystemExecutionContext.Instance.Logger);
    }
}
