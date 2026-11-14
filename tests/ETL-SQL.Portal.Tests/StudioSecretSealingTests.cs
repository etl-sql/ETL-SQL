using System.Security.Claims;
using ETL_SQL.Common;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using ETL_SQL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ETL_SQL.Portal.Tests;

public sealed class StudioSecretSealingTests
{
    [Fact]
    public void SealEndpoint_SealsLiteralUsePasswordAndReturnsPrompt()
    {
        var controller = new DesignerController(securityService: new SecurityService(NullLogger.Instance));
        const string raw = "USE PASSWORD = 'AuthorPassword1!';\nCREATE CONNECTION db AS MSSQL(SERVER='s', DATABASE='d', UID='u', PASSWORD='DbPassword1!');";

        var ok = Assert.IsType<OkObjectResult>(controller.Seal(new SealDesignerRequest(raw)));
        var body = Assert.IsType<SealDesignerResponse>(ok.Value);

        Assert.True(body.Changed);
        Assert.False(body.RequiresPassword);
        Assert.False(body.AllowsPlaintextSecrets);
        Assert.Contains("USE PASSWORD PROMPT;", body.SealedScript, StringComparison.Ordinal);
        Assert.Contains("ENC:", body.SealedScript, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthorPassword1!", body.SealedScript, StringComparison.Ordinal);
        Assert.DoesNotContain("DbPassword1!", body.SealedScript, StringComparison.Ordinal);
    }

    [Fact]
    public void SealEndpoint_RequiresPasswordWhenPlaintextPresentWithoutUsePassword()
    {
        var controller = new DesignerController(securityService: new SecurityService(NullLogger.Instance));
        const string raw = "CREATE CONNECTION db AS MSSQL(SERVER='s', DATABASE='d', UID='u', PASSWORD='DbPassword1!');";

        var missingPasswordOk = Assert.IsType<OkObjectResult>(controller.Seal(new SealDesignerRequest(raw)));
        var missingPasswordBody = Assert.IsType<SealDesignerResponse>(missingPasswordOk.Value);
        Assert.True(missingPasswordBody.RequiresPassword);
        Assert.False(missingPasswordBody.Changed);

        var sealedOk = Assert.IsType<OkObjectResult>(controller.Seal(new SealDesignerRequest(raw, "MasterPass1!")));
        var sealedBody = Assert.IsType<SealDesignerResponse>(sealedOk.Value);
        Assert.False(sealedBody.RequiresPassword);
        Assert.True(sealedBody.Changed);
        Assert.Contains("ENC:", sealedBody.SealedScript, StringComparison.Ordinal);
        Assert.DoesNotContain("DbPassword1!", sealedBody.SealedScript, StringComparison.Ordinal);
    }

    [Fact]
    public void SealEndpoint_LeavesScriptUntouchedWhenAllowPlaintextSecretsIsOn()
    {
        var controller = new DesignerController(securityService: new SecurityService(NullLogger.Instance));
        const string raw = "SET ALLOW_PLAINTEXT_SECRETS = ON;\nCREATE CONNECTION db AS MSSQL(SERVER='s', DATABASE='d', UID='u', PASSWORD='DbPassword1!');";

        var ok = Assert.IsType<OkObjectResult>(controller.Seal(new SealDesignerRequest(raw)));
        var body = Assert.IsType<SealDesignerResponse>(ok.Value);

        Assert.True(body.AllowsPlaintextSecrets);
        Assert.False(body.RequiresPassword);
        Assert.False(body.Changed);
        Assert.Equal(raw, body.SealedScript);
    }

    [Fact]
    public async Task ReportScriptSaveService_SealsLiteralUsePasswordAndRejectsMissingSavePassword()
    {
        var config = new PortalConfig
        {
            ScriptRootPath = Path.Combine(Path.GetTempPath(), "etlsql-portal-seal-tests"),
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        var tenant = TenantContext.FromVerifiedCredential("tenant-seal");
        var tenantScope = new DatasetTenantScope(config, tenant);
        await using var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var user = new PortalUser { Id = 1, UserName = "author", TenantId = tenant.Tenant.Value };
        var folder = new Folder { Id = 1, TenantId = tenant.Tenant.Value, Name = "Root", Path = "/", OwnerId = 1 };
        var report = new Report
        {
            Id = 1,
            TenantId = tenant.Tenant.Value,
            FolderId = 1,
            Folder = folder,
            Name = "Pipeline",
            CreatedBy = 1,
            Kind = CatalogDocumentKind.Pipeline,
            ScriptPath = Path.Combine(config.ScriptRootPath, tenant.Tenant.Value, "studio", "item.etlsql")
        };
        db.Users.Add(user);
        db.Folders.Add(folder);
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        var storage = new TenantArtifactStorageFactory(new InMemoryArtifactStorage()).ForTenant(tenant);
        await storage.WriteAllTextAsync(ArtifactArea.Scripts, "studio/item.etlsql", "SELECT 1;");

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var catalog = new PortalTenantCatalogScope(db, tenantScope);
        var security = new SecurityService(NullLogger.Instance);
        var save = new ReportScriptSaveService(db, new FolderPermissionService(db, catalog), storage,
            config, new PortalScriptSourceControlService(config), new AuditService(db, accessor, tenantScope), catalog, security);

        const string needsPassword = "CREATE CONNECTION db AS MSSQL(SERVER='s', DATABASE='d', UID='u', PASSWORD='DbPassword1!');";
        var rejected = await save.SaveAsync(report.Id, needsPassword, report.Version, principal, 1);
        Assert.Equal(ReportScriptSaveStatus.SealPasswordRequired, rejected.Status);
        Assert.Equal("SELECT 1;", await storage.ReadAllTextAsync(ArtifactArea.Scripts, "studio/item.etlsql"));

        var sealedWithPromptPass = await save.SaveAsync(report.Id, needsPassword, report.Version, principal, 1, sealPassword: "MasterPass1!");
        Assert.Equal(ReportScriptSaveStatus.Saved, sealedWithPromptPass.Status);
        Assert.NotNull(sealedWithPromptPass.ScriptText);
        Assert.Contains("ENC:", sealedWithPromptPass.ScriptText!, StringComparison.Ordinal);
        Assert.DoesNotContain("DbPassword1!", sealedWithPromptPass.ScriptText!, StringComparison.Ordinal);
        Assert.Equal(sealedWithPromptPass.ScriptText, await storage.ReadAllTextAsync(ArtifactArea.Scripts, "studio/item.etlsql"));

        const string withLiteral = "USE PASSWORD = 'AuthorPassword1!';\nCREATE CONNECTION db AS MSSQL(SERVER='s', DATABASE='d', UID='u', PASSWORD='DbPassword1!');";
        var sealedFromLiteral = await save.SaveAsync(report.Id, withLiteral, sealedWithPromptPass.Version, principal, 1);
        Assert.Equal(ReportScriptSaveStatus.Saved, sealedFromLiteral.Status);
        Assert.NotNull(sealedFromLiteral.ScriptText);
        Assert.Contains("USE PASSWORD PROMPT;", sealedFromLiteral.ScriptText!, StringComparison.Ordinal);
        Assert.Contains("ENC:", sealedFromLiteral.ScriptText!, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthorPassword1!", sealedFromLiteral.ScriptText!, StringComparison.Ordinal);
        Assert.DoesNotContain("DbPassword1!", sealedFromLiteral.ScriptText!, StringComparison.Ordinal);
        Assert.Equal(sealedFromLiteral.ScriptText, await storage.ReadAllTextAsync(ArtifactArea.Scripts, "studio/item.etlsql"));
    }
}
