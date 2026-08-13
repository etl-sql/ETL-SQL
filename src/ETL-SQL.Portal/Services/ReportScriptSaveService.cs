using System.Security.Claims;
using System.Security.Cryptography;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public enum ReportScriptSaveStatus
{
    Saved,
    NotFound,
    Forbidden,
    MissingVersion,
    Conflict
}

public sealed record ReportScriptSaveResult(
    ReportScriptSaveStatus Status,
    long? Version = null,
    string? SourceRevision = null,
    Report? Current = null,
    string? Error = null);

public sealed class ReportScriptSaveService(
    PortalDbContext db,
    FolderPermissionService folderPermissions,
    IArtifactStorage artifacts,
    PortalConfig portalConfig,
    PortalScriptSourceControlService sourceControl,
    AuditService audit,
    PortalTenantCatalogScope? catalogScope = null)
{
    private IQueryable<Report> Reports => catalogScope?.Reports ?? db.Reports;

    public async Task<ReportScriptSaveResult> SaveAsync(
        int id,
        string scriptText,
        long? expectedVersion,
        ClaimsPrincipal user,
        int currentUserId,
        string? baseRevision = null,
        CancellationToken ct = default)
    {
        var report = await Reports.Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct);
        if (report is null)
            return new ReportScriptSaveResult(ReportScriptSaveStatus.NotFound);

        var perm = await folderPermissions.GetEffectiveReportPermissionAsync(report, user);
        if (!perm.AtLeast(FolderPermission.Author))
            return new ReportScriptSaveResult(ReportScriptSaveStatus.Forbidden);

        if (expectedVersion is null)
            return new ReportScriptSaveResult(ReportScriptSaveStatus.MissingVersion);
        if (!OptimisticConcurrency.Prepare(db, report, expectedVersion.Value))
            return new ReportScriptSaveResult(ReportScriptSaveStatus.Conflict, Current: report);

        var scriptKey = PortalPathGuard.ToScriptKey(portalConfig, report.ScriptPath);
        if (scriptKey is null)
            return new ReportScriptSaveResult(ReportScriptSaveStatus.Forbidden);

        sourceControl.ValidateScriptTextForCommit(scriptText);
        var currentRevision = await sourceControl.GetCurrentRevisionAsync(ct);

        var hash = "sha256:" + Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scriptText))).ToLowerInvariant();

        var hadOriginal = await artifacts.ExistsAsync(ArtifactArea.Scripts, scriptKey, ct);
        var backup = hadOriginal
            ? await artifacts.ReadAllBytesAsync(ArtifactArea.Scripts, scriptKey, ct)
            : null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var wroteScript = false;
        try
        {
            await db.SaveChangesAsync(ct);

            await artifacts.WriteAllTextAsync(ArtifactArea.Scripts, scriptKey, scriptText, ct: ct);
            wroteScript = true;

            report.PublishedScriptHash = hash;
            report.ScriptLastModified = DateTime.UtcNow;
            report.UpdatedAt = DateTime.UtcNow;
            var detail = currentRevision is null
                ? report.Name
                : $"{report.Name}; sourceRevision={currentRevision}; sourceControlPending=true";
            audit.Stage(currentUserId, "DESIGNER_SAVE", "Report", id.ToString(), detail);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            if (wroteScript) await RestoreScriptAsync(scriptKey, backup, hadOriginal, ct);
            await db.Entry(report).ReloadAsync(ct);
            return new ReportScriptSaveResult(ReportScriptSaveStatus.Conflict, Current: report);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            if (wroteScript) await RestoreScriptAsync(scriptKey, backup, hadOriginal, ct);
            throw;
        }

        return new ReportScriptSaveResult(ReportScriptSaveStatus.Saved, report.Version, currentRevision);
    }

    private async Task RestoreScriptAsync(string scriptKey, byte[]? backup, bool hadOriginal, CancellationToken ct)
    {
        if (backup is not null)
            await artifacts.WriteAllBytesAsync(ArtifactArea.Scripts, scriptKey, backup, ct: ct);
        else if (!hadOriginal)
            await artifacts.DeleteAsync(ArtifactArea.Scripts, scriptKey, ct);
    }
}
