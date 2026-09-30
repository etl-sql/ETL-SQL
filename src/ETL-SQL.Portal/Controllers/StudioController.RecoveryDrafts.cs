using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting.Authoring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Recovery drafts: an author's unsaved edits, kept on the Portal so a crash, a dropped connection,
/// or an expired sign-in loses nothing. Each draft is private to its author and scoped to the
/// tenant through its report; another user's draft answers exactly as no draft does.
/// </summary>
public sealed partial class StudioController
{
    [HttpGet("drafts/{reportId:int}")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioRecoveryDraftDto>> GetRecoveryDraft(int reportId, CancellationToken ct)
    {
        if (!portalConfig.Studio.DraftRecovery) return DraftRecoveryOff();
        if (await AuthoredReportAsync(reportId, ct) is null) return NotFound();

        var draft = await CatalogScope.StudioRecoveryDrafts
            .FirstOrDefaultAsync(item => item.ReportId == reportId && item.UserId == CurrentUserId, ct);
        if (draft is null) return NotFound();
        if (draft.UpdatedAt < DraftExpiry())
        {
            db.StudioRecoveryDrafts.Remove(draft);
            await db.SaveChangesAsync(ct);
            return NotFound();
        }
        return Ok(new StudioRecoveryDraftDto(draft.Content, draft.BaseVersion, draft.BaseSourceRevision, draft.UpdatedAt));
    }

    [HttpPut("drafts/{reportId:int}")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<IActionResult> PutRecoveryDraft(int reportId, [FromBody] SaveStudioRecoveryDraftRequest request, CancellationToken ct)
    {
        if (!portalConfig.Studio.DraftRecovery) return DraftRecoveryOff();
        if (await AuthoredReportAsync(reportId, ct) is null) return NotFound();

        var content = request.Content ?? "";
        if (content.Length > Math.Max(1, portalConfig.DesignerLimits.MaxScriptCharacters))
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "The draft is larger than a script may be." });
        if (StudioDraftPolicy.ContainsPlaintextSecret(content))
            return UnprocessableEntity(new
            {
                error = "The draft holds a plaintext credential, so it was not kept. Encrypt the credential or use a SECRET: reference.",
                code = "PLAINTEXT_SECRET"
            });

        var draft = await CatalogScope.StudioRecoveryDrafts
            .FirstOrDefaultAsync(item => item.ReportId == reportId && item.UserId == CurrentUserId, ct);
        if (draft is null)
        {
            draft = new StudioRecoveryDraft { ReportId = reportId, UserId = CurrentUserId };
            db.StudioRecoveryDrafts.Add(draft);
        }
        draft.Content = content;
        draft.BaseVersion = request.BaseVersion;
        draft.BaseSourceRevision = request.BaseSourceRevision;
        draft.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("drafts/{reportId:int}")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<IActionResult> DeleteRecoveryDraft(int reportId, CancellationToken ct)
    {
        // Deleting is allowed with recovery off, so turning it off can be followed by a clean-up.
        var draft = await CatalogScope.StudioRecoveryDrafts
            .FirstOrDefaultAsync(item => item.ReportId == reportId && item.UserId == CurrentUserId, ct);
        if (draft is not null)
        {
            db.StudioRecoveryDrafts.Remove(draft);
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    /// <summary>The report, when it exists in this tenant and the caller may author it.</summary>
    private async Task<Report?> AuthoredReportAsync(int reportId, CancellationToken ct)
    {
        var report = await CatalogScope.Documents.FirstOrDefaultAsync(item => item.Id == reportId && !item.IsDeleted, ct);
        if (report is null) return null;
        var permission = await folderPermissions.GetEffectiveReportPermissionAsync(report, User);
        return permission.AtLeast(FolderPermission.Author) ? report : null;
    }

    private DateTime DraftExpiry() => DateTime.UtcNow.AddDays(-Math.Max(1, portalConfig.Studio.DraftRetentionDays));

    private ObjectResult DraftRecoveryOff() => StatusCode(StatusCodes.Status409Conflict, new
    {
        error = "Draft recovery is turned off on this Portal (Portal:Studio:DraftRecovery).",
        code = "DRAFT_RECOVERY_OFF"
    });
}
