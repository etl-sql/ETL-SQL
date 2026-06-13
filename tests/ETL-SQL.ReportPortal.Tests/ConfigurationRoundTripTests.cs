using System.Net.Http.Json;
using ETL_SQL.Connectors.ReportPortal;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// P1.11: prove clean-server round-trip reconstruction. A source portal is seeded with the
/// identity / permission / SMTP graph (overlapping ACLs and a disabled user included); its
/// configuration is exported, the <c>${...}</c> secrets are supplied, and the bootstrap is replayed
/// <b>twice</b> into a fresh empty portal through the connector. The reconstructed effective state
/// must equal the source's, the second pass must be a no-op (idempotent), and no source secret may
/// appear in the export.
/// </summary>
[Trait("Category", "Portal")]
public sealed class ConfigurationRoundTripTests
{
    private sealed class LiteralEvalContext : SystemExecutionContext
    {
        public override ValueTask<object?> EvaluateValue(Expression? expr, Row context, bool decryptSensitive = false) =>
            new(expr is LiteralExpression lit ? lit.Value : null);
    }

    private sealed record NormalizedState(
        HashSet<string> Users, HashSet<string> Groups, HashSet<string> Memberships,
        HashSet<string> Folders, HashSet<string> Acls, HashSet<string> Smtp);

    [Fact]
    public async Task CleanServer_RoundTrip_ReconstructsEffectiveState_Idempotently()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var source = new PortalWebFactory();
        _ = source.CreateClient();
        await SeedSourceAsync(source, suffix);

        // ── Export from the source ──────────────────────────────────────────────
        string script;
        IReadOnlyList<string> requiredSecrets;
        NormalizedState sourceState;
        using (var scope = source.Services.CreateScope())
        {
            var exporter = scope.ServiceProvider.GetRequiredService<ConfigurationExportService>();
            var export = await exporter.GenerateAsync();
            script = export.Script;
            requiredSecrets = export.RequiredSecrets;

            // No seeded secret or capability token may appear in the export.
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            foreach (var marker in await SeededSecretsAsync(db, suffix))
                Assert.DoesNotContain(marker, script);

            sourceState = await ReadStateAsync(db, suffix);
        }

        // ── Supply every required secret ────────────────────────────────────────
        foreach (var placeholder in requiredSecrets)
            script = script.Replace($"${{{placeholder}}}", "Imported@Round1!");

        var statements = ExtractAdminStatements(script);
        Assert.NotEmpty(statements);

        // ── Replay twice into a fresh empty portal ──────────────────────────────
        using var target = new PortalWebFactory();
        var connector = await ConnectAsAdminAsync(target);
        var ctx = new LiteralEvalContext();
        foreach (var _ in Enumerable.Range(0, 2))
            foreach (var stmt in statements)
                await connector.ExecuteAdminStatementAsync(stmt, ctx);

        // ── Compare normalized effective state ──────────────────────────────────
        using (var scope = target.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var targetState = await ReadStateAsync(db, suffix);

            Assert.Equal(sourceState.Users, targetState.Users);
            Assert.Equal(sourceState.Groups, targetState.Groups);
            Assert.Equal(sourceState.Memberships, targetState.Memberships);
            Assert.Equal(sourceState.Folders, targetState.Folders);
            Assert.Equal(sourceState.Acls, targetState.Acls);
            Assert.Equal(sourceState.Smtp, targetState.Smtp);

            // Idempotent: no duplicate rows from the second pass.
            Assert.Equal(1, await db.Users.CountAsync(u => u.UserName == $"alice_{suffix}"));
            Assert.Equal(1, await db.Groups.CountAsync(g => g.Name == $"finance_{suffix}"));
        }
    }

    // ── Seeding ──────────────────────────────────────────────────────────────────

    private static async Task SeedSourceAsync(PortalWebFactory factory, string suffix)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var hasher = new PasswordHasher<PortalUser>();

        PortalUser User(string name, bool active)
        {
            var u = new PortalUser
            {
                UserName = $"{name}_{suffix}",
                Email = $"{name}_{suffix}@test.local",
                IsActive = active,
                NormalizedUserName = $"{name}_{suffix}".ToUpperInvariant(),
                PasswordHash = $"HASH-{name}-{suffix}"
            };
            return u;
        }

        var alice = User("alice", active: true);
        var bob = User("bob", active: true);
        var carol = User("carol", active: false); // disabled resource
        db.Users.AddRange(alice, bob, carol);

        var finance = new Group { Name = $"finance_{suffix}", Description = "Finance" };
        var ops = new Group { Name = $"ops_{suffix}", Description = "Operations" };
        db.Groups.AddRange(finance, ops);
        await db.SaveChangesAsync();

        // Roles for the users so the export emits them.
        await AddRoleAsync(db, alice.Id, "Publisher");
        await AddRoleAsync(db, bob.Id, "Viewer");
        await AddRoleAsync(db, carol.Id, "Viewer");

        db.UserGroups.AddRange(
            new UserGroup { UserId = alice.Id, GroupId = finance.Id },
            new UserGroup { UserId = bob.Id, GroupId = finance.Id },
            new UserGroup { UserId = bob.Id, GroupId = ops.Id });

        var root = new Folder { Name = $"root_{suffix}", Path = $"/root_{suffix}", OwnerId = alice.Id };
        db.Folders.Add(root);
        await db.SaveChangesAsync();
        var child = new Folder
        {
            Name = $"child_{suffix}",
            Path = $"/root_{suffix}/child_{suffix}",
            ParentId = root.Id,
            OwnerId = alice.Id
        };
        db.Folders.Add(child);
        await db.SaveChangesAsync();

        // Overlapping ACLs: both groups on the root, finance also on the child.
        db.FolderAcls.AddRange(
            new FolderAcl { FolderId = root.Id, GroupId = finance.Id, Permission = FolderPermission.Manage },
            new FolderAcl { FolderId = root.Id, GroupId = ops.Id, Permission = FolderPermission.Read },
            new FolderAcl { FolderId = child.Id, GroupId = finance.Id, Permission = FolderPermission.Execute });

        db.SmtpConnections.Add(new SmtpConnection
        {
            Alias = $"corp_{suffix}",
            Host = "smtp.corp.test",
            Port = 587,
            Username = "mailer",
            EncryptedPassword = $"CIPHER-{suffix}",
            FromAddress = "reports@corp.test",
            UseSsl = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddRoleAsync(PortalDbContext db, int userId, string role)
    {
        var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).FirstOrDefaultAsync();
        if (roleId != 0)
            db.UserRoles.Add(new IdentityUserRole<int> { UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    private static async Task<List<string>> SeededSecretsAsync(PortalDbContext db, string suffix)
    {
        var markers = new List<string> { $"CIPHER-{suffix}" };
        markers.AddRange(await db.Users
            .Where(u => u.UserName!.EndsWith($"_{suffix}") && u.PasswordHash != null)
            .Select(u => u.PasswordHash!)
            .ToListAsync());
        return markers;
    }

    // ── State comparison ─────────────────────────────────────────────────────────

    private static async Task<NormalizedState> ReadStateAsync(PortalDbContext db, string suffix)
    {
        var roleByUser = await (from ur in db.UserRoles
                                join r in db.Roles on ur.RoleId equals r.Id
                                select new { ur.UserId, r.Name }).ToListAsync();

        var users = new HashSet<string>();
        foreach (var u in await db.Users.Where(u => u.UserName!.EndsWith($"_{suffix}")).ToListAsync())
        {
            var role = roleByUser.FirstOrDefault(x => x.UserId == u.Id)?.Name ?? "Viewer";
            users.Add($"{u.UserName}|{role}|{u.IsActive}");
        }

        var groups = (await db.Groups.Where(g => g.Name.EndsWith($"_{suffix}")).Select(g => g.Name).ToListAsync())
            .ToHashSet();

        var memberships = (await (from ug in db.UserGroups
                                  join u in db.Users on ug.UserId equals u.Id
                                  join g in db.Groups on ug.GroupId equals g.Id
                                  where g.Name.EndsWith($"_{suffix}")
                                  select u.UserName + "@" + g.Name).ToListAsync()).ToHashSet();

        var folders = (await db.Folders.Where(f => f.Path.Contains($"_{suffix}")).Select(f => f.Path).ToListAsync())
            .ToHashSet();

        var acls = (await (from a in db.FolderAcls
                           join f in db.Folders on a.FolderId equals f.Id
                           join g in db.Groups on a.GroupId equals g.Id
                           where g.Name.EndsWith($"_{suffix}")
                           select f.Path + "|" + g.Name + "|" + a.Permission).ToListAsync()).ToHashSet();

        var smtp = (await db.SmtpConnections.Where(s => s.Alias.EndsWith($"_{suffix}"))
            .Select(s => s.Alias + "|" + s.Host + "|" + s.Port + "|" + s.UseSsl).ToListAsync()).ToHashSet();

        return new NormalizedState(users, groups, memberships, folders, acls, smtp);
    }

    // ── Replay plumbing ──────────────────────────────────────────────────────────

    private static List<Statement> ExtractAdminStatements(string script)
    {
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        var result = new List<Statement>();
        foreach (var top in parsed.Statements)
        {
            switch (top)
            {
                // EXECUTE <conn> BEGIN ... END captures its body as raw text, re-parsed here the
                // same way ExecutePushdownStatementHandler does at runtime.
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

    private static async Task<ReportPortalDataSource> ConnectAsAdminAsync(PortalWebFactory factory)
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

        return new ReportPortalDataSource(factory.CreateClient(), "admin", "Admin@Tests99!",
            SystemExecutionContext.Instance.Logger);
    }
}
