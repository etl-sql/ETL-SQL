using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Connectors.Portal;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// ST08: a pipeline travels in the configuration export as the catalog document it is, and arrives
/// as a pipeline. What belongs to the source host's Orchestrator does not travel, and the export
/// says so. The statement is <c>PUBLISH REPORT … WITH (KIND = 'PIPELINE')</c>: one document, with a kind.
/// </summary>
[Trait("Category", "Portal")]
public sealed class PipelineConfigurationRoundTripTests
{
    private const string PipelineScript =
        "CREATE CONNECTION warehouse AS POSTGRES(HOST = 'db.internal', DATABASE = 'dw', USER = 'loader', PASSWORD = 'SECRET:warehouse_password');\n"
        + "SELECT 1 AS Loaded INTO #staged;\nASSERT (SELECT COUNT(*) FROM #staged) = 1;\n";

    private sealed class LiteralEvalContext : SystemExecutionContext
    {
        public override ValueTask<object?> EvaluateValue(Expression? expr, Row context, bool decryptSensitive = false) =>
            new(expr is LiteralExpression lit ? lit.Value : null);
    }

    [Fact]
    public async Task APipelineRoundTripsAsAPipelineAndLeavesHostOwnedBindingsBehind()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Nightly load {suffix}";
        var folderPath = $"/Etl-{suffix}";

        using var source = new PortalWebFactory();
        _ = source.CreateClient();
        var sourceScript = Path.Combine(source.TempDir, "scripts", $"nightly_load_{suffix}.etlsql");
        await File.WriteAllTextAsync(sourceScript, PipelineScript);
        int sourceId;
        using (var scope = source.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(user => user.UserName == "admin");
            var folder = new Folder { Name = $"Etl-{suffix}", Path = folderPath, OwnerId = admin.Id };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            var pipeline = new Report
            {
                TenantId = admin.TenantId,
                FolderId = folder.Id,
                Kind = CatalogDocumentKind.Pipeline,
                Name = name,
                Description = "Loads the warehouse nightly",
                ScriptPath = sourceScript,
                PublishedScriptHash = "sha256:sourcehostonlyhash",
                CreatedBy = admin.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Reports.Add(pipeline);
            await db.SaveChangesAsync();
            sourceId = pipeline.Id;
        }

        // ── Export ──────────────────────────────────────────────────────────────
        ConfigurationExportService.ExportResult export;
        using (var scope = source.Services.CreateScope())
            export = await scope.ServiceProvider.GetRequiredService<ConfigurationExportService>().GenerateAsync();
        var script = export.Script;

        var statement = script.Split('\n').Single(line => line.Contains($"PUBLISH REPORT '{name}'", StringComparison.Ordinal));
        Assert.Contains("KIND = 'PIPELINE'", statement);
        Assert.Contains("DESCRIPTION = 'Loads the warehouse nightly'", statement);
        Assert.Contains("CATALOG_OWNER = 'admin'", statement);
        Assert.Contains($"IN FOLDER '{folderPath}'", statement);
        var manifestItem = Assert.Single(export.ContentManifest, item => item.Kind == "PipelineScript");
        Assert.Equal(sourceScript, manifestItem.Source);
        // Not a report: the report manifest must not claim an .etlsql file.
        Assert.DoesNotContain(export.ContentManifest, item => item.Kind == "ReportScript" && item.Source == sourceScript);

        // Host-owned state stays behind, and the export names what it left.
        Assert.Contains(export.Skipped, item => item.Contains("Orchestrator bindings of 1 pipeline(s)", StringComparison.Ordinal));
        Assert.DoesNotContain($"PIPELINE:{sourceId}", script);
        Assert.DoesNotContain("sourcehostonlyhash", script);
        // The script file travels by copy, never inline, so neither its text nor its secret reference is in the export.
        Assert.DoesNotContain("warehouse_password", script);
        Assert.DoesNotContain("db.internal", script);

        // ── Replay twice into an empty portal ───────────────────────────────────
        using var target = new PortalWebFactory();
        _ = target.CreateClient();
        var targetScript = Path.Combine(target.TempDir, "scripts", Path.GetFileName(sourceScript));
        File.Copy(sourceScript, targetScript, overwrite: true);
        script = script.Replace(sourceScript, targetScript, StringComparison.Ordinal);
        foreach (var placeholder in export.RequiredSecrets)
            script = script.Replace($"${{{placeholder}}}", "Imported@Round1!");

        var statements = ExtractAdminStatements(script);
        var connector = await ConnectAsAdminAsync(target);
        var context = new LiteralEvalContext();
        foreach (var _ in Enumerable.Range(0, 2))
            foreach (var replayed in statements)
                await connector.ExecuteAdminStatementAsync(replayed, context);

        int targetId;
        using (var scope = target.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            // Replayed twice, published once.
            var imported = await db.Reports.Include(report => report.Folder).SingleAsync(report => report.Name == name);
            Assert.Equal(CatalogDocumentKind.Pipeline, imported.Kind);
            Assert.Equal(folderPath, imported.Folder.Path);
            Assert.Equal("Loads the warehouse nightly", imported.Description);
            Assert.EndsWith(".etlsql", imported.ScriptPath, StringComparison.Ordinal);
            Assert.Equal(PipelineScript, await File.ReadAllTextAsync(imported.ScriptPath));
            Assert.Equal("admin", (await db.Users.SingleAsync(user => user.Id == imported.CreatedBy)).UserName);
            // The target computed its own hash; the source's did not travel.
            Assert.NotEqual("sha256:sourcehostonlyhash", imported.PublishedScriptHash);
            targetId = imported.Id;
        }

        // ── It is still a pipeline at the target: reader surfaces refuse it ─────
        using var client = target.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@Tests99!" });
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, $"/api/reports/{targetId}"),
            (HttpMethod.Post, $"/api/reports/{targetId}/execute"),
            (HttpMethod.Get, $"/api/reports/{targetId}/manifest")
        })
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new("Bearer", token);
            if (method == HttpMethod.Post) request.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode);
        }
        // And Studio lists it as the pipeline it is.
        using (var list = new HttpRequestMessage(HttpMethod.Get, "/api/studio/reports"))
        {
            list.Headers.Authorization = new("Bearer", token);
            var documents = await (await client.SendAsync(list)).Content.ReadFromJsonAsync<JsonArray>();
            Assert.Contains(documents!, item => item!["id"]!.GetValue<int>() == targetId && item["kind"]!.GetValue<string>() == "Pipeline");
        }
    }

    [Fact]
    public async Task NeitherKindCanBePublishedFromTheOthersFile()
    {
        using var portal = new PortalWebFactory();
        _ = portal.CreateClient();
        var pipelineFile = Path.Combine(portal.TempDir, "scripts", "load.etlsql");
        var reportFile = Path.Combine(portal.TempDir, "scripts", "sales.rptsql");
        await File.WriteAllTextAsync(pipelineFile, "SELECT 1 AS Loaded INTO #staged;\n");
        await File.WriteAllTextAsync(reportFile, "SELECT 1 AS Value;\n");
        var connector = await ConnectAsAdminAsync(portal);
        var context = new LiteralEvalContext();
        await connector.ExecuteAdminStatementAsync(Parse("CREATE FOLDER '/Mixed';"), context);

        // An .etlsql file is not a report, with or without the option.
        var asReport = await Assert.ThrowsAsync<ExecutionException>(() => connector.ExecuteAdminStatementAsync(
            Parse($"PUBLISH REPORT 'Load' FROM '{pipelineFile}' IN FOLDER '/Mixed';"), context));
        Assert.Contains("Only .rptsql files may be published as reports", asReport.Message);
        // And a report file is not a pipeline.
        var asPipeline = await Assert.ThrowsAsync<ExecutionException>(() => connector.ExecuteAdminStatementAsync(
            Parse($"PUBLISH REPORT 'Sales' FROM '{reportFile}' IN FOLDER '/Mixed' WITH (KIND = 'PIPELINE');"), context));
        Assert.Contains("Only .etlsql files may be published as pipelines", asPipeline.Message);

        using var scope = portal.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Reports
            .AnyAsync(report => report.Name == "Load" || report.Name == "Sales"));
    }

    /// <summary>
    /// ST09: promotion is the same export replayed into another environment. The pipeline keeps its
    /// logical owner and folder, is bound to the target's own script root, and a document already
    /// holding its name with different content stops it without being touched.
    /// </summary>
    [Fact]
    public async Task PromotingAPipelineKeepsItsOwnerRebindsItsScriptAndRejectsACollisionUntouched()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Nightly load {suffix}";
        var folderPath = $"/Etl-{suffix}";
        var ownerName = $"etl_owner_{suffix}";

        using var dev = new PortalWebFactory();
        _ = dev.CreateClient();
        var devScript = Path.Combine(dev.TempDir, "scripts", $"nightly_load_{suffix}.etlsql");
        await File.WriteAllTextAsync(devScript, PipelineScript);
        using (var scope = dev.Services.CreateScope())
        {
            var owner = await CreateUserAsync(scope, ownerName, "Publisher");
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var folder = new Folder { Name = $"Etl-{suffix}", Path = folderPath, OwnerId = owner.Id };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            db.Reports.Add(new Report
            {
                TenantId = owner.TenantId,
                FolderId = folder.Id,
                Kind = CatalogDocumentKind.Pipeline,
                Name = name,
                ScriptPath = devScript,
                CreatedBy = owner.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        ConfigurationExportService.ExportResult export;
        using (var scope = dev.Services.CreateScope())
            export = await scope.ServiceProvider.GetRequiredService<ConfigurationExportService>().GenerateAsync();

        // ── Promote into prod ───────────────────────────────────────────────────
        using var prod = new PortalWebFactory();
        _ = prod.CreateClient();
        var prodScript = Path.Combine(prod.TempDir, "scripts", Path.GetFileName(devScript));
        File.Copy(devScript, prodScript);
        var prodStatements = ExtractAdminStatements(Rebind(export, devScript, prodScript));
        var prodAdmin = await ConnectAsAdminAsync(prod);
        var context = new LiteralEvalContext();
        foreach (var statement in prodStatements)
            await prodAdmin.ExecuteAdminStatementAsync(statement, context);

        using (var scope = prod.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var promoted = await db.Reports.Include(report => report.Folder).SingleAsync(report => report.Name == name);
            Assert.Equal(CatalogDocumentKind.Pipeline, promoted.Kind);
            // Ownership is the logical owner, recreated in prod by the same export — not whoever ran the promotion.
            var promotedOwner = await db.Users.SingleAsync(user => user.Id == promoted.CreatedBy);
            Assert.Equal(ownerName, promotedOwner.UserName);
            Assert.Equal(promotedOwner.Id, promoted.Folder.OwnerId);
            Assert.Equal(folderPath, promoted.Folder.Path);
            // Bound to prod's script root, never dev's.
            Assert.StartsWith(Path.Combine(prod.TempDir, "scripts"), promoted.ScriptPath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(dev.TempDir, promoted.ScriptPath, StringComparison.OrdinalIgnoreCase);
        }

        // A target the promoter has no rights on is not a permitted target.
        var outsiderScript = Path.Combine(prod.TempDir, "scripts", $"outsider_{suffix}.etlsql");
        await File.WriteAllTextAsync(outsiderScript, PipelineScript);
        using (var scope = prod.Services.CreateScope())
            await CreateUserAsync(scope, $"outsider_{suffix}", "Publisher");
        var outsider = new PortalDataSource(prod.CreateClient(), $"outsider_{suffix}", UserPassword, SystemExecutionContext.Instance.Logger);
        var refused = await Assert.ThrowsAsync<ExecutionException>(() => outsider.ExecuteAdminStatementAsync(
            Parse($"PUBLISH REPORT 'Outsider load' FROM '{outsiderScript}' IN FOLDER '{folderPath}' WITH (KIND = 'PIPELINE');"), context));
        // Refused before anything is asked of the catalog: to this account the folder does not exist.
        Assert.Contains("not found", refused.Message);
        using (var scope = prod.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Reports.AnyAsync(report => report.Name == "Outsider load"));

        // ── A target where the name is already taken by different content ───────
        using var taken = new PortalWebFactory();
        _ = taken.CreateClient();
        const string existingText = "SELECT 2 AS SomethingElse INTO #other;\n";
        var existingScript = Path.Combine(taken.TempDir, "scripts", $"already_here_{suffix}.etlsql");
        await File.WriteAllTextAsync(existingScript, existingText);
        long existingVersion;
        using (var scope = taken.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(user => user.UserName == "admin");
            var folder = new Folder { Name = $"Etl-{suffix}", Path = folderPath, OwnerId = admin.Id };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            var existing = new Report
            {
                TenantId = admin.TenantId,
                FolderId = folder.Id,
                Kind = CatalogDocumentKind.Pipeline,
                Name = name,
                ScriptPath = existingScript,
                CreatedBy = admin.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Reports.Add(existing);
            await db.SaveChangesAsync();
            existingVersion = existing.Version;
        }
        var takenScript = Path.Combine(taken.TempDir, "scripts", Path.GetFileName(devScript));
        File.Copy(devScript, takenScript);
        var takenAdmin = await ConnectAsAdminAsync(taken);
        ExecutionException? collision = null;
        foreach (var statement in ExtractAdminStatements(Rebind(export, devScript, takenScript)))
        {
            if (statement is PublishPortalReportStatement publish && publish.ReportName == name)
                collision = await Assert.ThrowsAsync<ExecutionException>(() => takenAdmin.ExecuteAdminStatementAsync(statement, context));
            else
                await takenAdmin.ExecuteAdminStatementAsync(statement, context);
        }
        Assert.NotNull(collision);
        Assert.Contains("409", collision.Message);
        Assert.Contains("different content", collision.Message);

        using (var scope = taken.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            // Still one document under that name, still the one that was there, still its own text.
            var survivor = await db.Reports.SingleAsync(report => report.Name == name);
            Assert.Equal(existingScript, survivor.ScriptPath);
            Assert.Equal(existingVersion, survivor.Version);
            Assert.Equal("admin", (await db.Users.SingleAsync(user => user.Id == survivor.CreatedBy)).UserName);
            Assert.Equal(existingText, await File.ReadAllTextAsync(existingScript));
        }
    }

    private const string UserPassword = "Promoted@User99!";

    private static string Rebind(ConfigurationExportService.ExportResult export, string sourceScript, string targetScript)
    {
        var script = export.Script.Replace(sourceScript, targetScript, StringComparison.Ordinal);
        foreach (var placeholder in export.RequiredSecrets)
            script = script.Replace($"${{{placeholder}}}", UserPassword);
        return script;
    }

    private static async Task<PortalUser> CreateUserAsync(IServiceScope scope, string userName, string role)
    {
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PortalUser>>();
        var user = new PortalUser
        {
            UserName = userName,
            Email = $"{userName}@test.local",
            IsActive = true,
            MustChangePassword = false,
            Provider = "Local"
        };
        var created = await users.CreateAsync(user, UserPassword);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Users.SingleAsync(item => item.Id == user.Id);
    }
    private static Statement Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse().Statements.Single();

    private static List<Statement> ExtractAdminStatements(string script)
    {
        var result = new List<Statement>();
        foreach (var top in new Parser(new Lexer(script).Tokenize()).Parse().Statements)
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
        var login = await setup.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })
        };
        change.Headers.Authorization = new("Bearer", token);
        (await setup.SendAsync(change)).EnsureSuccessStatusCode();
        return new PortalDataSource(factory.CreateClient(), "admin", "Admin@Tests99!", SystemExecutionContext.Instance.Logger);
    }
}
