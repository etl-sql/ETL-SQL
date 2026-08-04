using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public enum DraftWorkflowStatus
{
    Ok,
    NotFound,
    Forbidden,
    Conflict,
    InvalidState,
    SelfApproval,
    StaleBase,
    MissingVersion,
}

public sealed record DraftWorkflowResult(
    DraftWorkflowStatus Status,
    ReportScriptDraft? Draft = null,
    string? Message = null,
    long? ReportVersion = null);

/// <summary>
/// Draft → review → publish for report scripts.
///
/// <para>The workflow exists to put a gap between authoring a change and it taking effect, and the
/// only thing that makes that gap worth anything is that <b>somebody else</b> closes it. So the
/// separation-of-duties rule here is not a policy toggle: an author can never approve their own
/// draft, whatever capabilities or roles they hold, including Admin. A four-eyes control that the
/// most privileged account can bypass is a control that fails exactly when it is needed, since the
/// account that gets compromised or coerced is the privileged one.</para>
///
/// <para>The whole workflow is opt-in (<c>Portal:Studio:RequireApprovalToPublish</c>, default off).
/// Turning it on is a decision about how an organization works, not something an upgrade should
/// impose — and an organization that has not yet decided who reviews would otherwise find every
/// change stuck behind nobody.</para>
/// </summary>
public sealed class ReportDraftWorkflowService(
    PortalDbContext db,
    FolderPermissionService folderPermissions,
    StudioAuthorizationService studioAuthorization,
    AuditService audit)
{
    public static string HashScript(string scriptText) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scriptText)))
            .ToLowerInvariant();

    /// <summary>
    /// Creates or updates this report's open draft. Authoring, so it takes <c>Author</c> on the
    /// report and the <c>ScriptSave</c> capability.
    /// </summary>
    public async Task<DraftWorkflowResult> SaveDraftAsync(
        int reportId,
        string scriptText,
        ClaimsPrincipal user,
        int userId,
        string? userName,
        string? baseScriptHash,
        CancellationToken ct = default)
    {
        var report = await db.Reports.Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == reportId && !r.IsDeleted, ct);
        if (report is null) return new(DraftWorkflowStatus.NotFound);

        var permission = await folderPermissions.GetEffectiveReportPermissionAsync(report, user);
        if (!permission.AtLeast(FolderPermission.Author))
            return new(DraftWorkflowStatus.Forbidden);
        if (!studioAuthorization.HasCapability(user, StudioCapabilities.ScriptSave))
            return new(DraftWorkflowStatus.Forbidden);

        var draft = await OpenDraftAsync(reportId, ct);

        // An approval or a review-in-progress is about specific content, so any edit invalidates it
        // and the draft goes back to the author's desk. Without this an author could get a trivial
        // change approved and then swap the body, or edit under a reviewer who is midway through
        // reading — either way the recorded decision would describe something that never shipped.
        if (draft is not null
            && draft.Status is ReportScriptDraft.ApprovedStatus or ReportScriptDraft.PendingStatus)
        {
            draft.ApprovedByUserId = null;
            draft.ApprovedByUserName = null;
            draft.DecidedAtUtc = null;
            draft.SubmittedAtUtc = null;
        }

        if (draft is null)
        {
            draft = new ReportScriptDraft
            {
                ReportId = reportId,
                AuthorUserId = userId,
                AuthorUserName = userName,
                BaseScriptHash = baseScriptHash ?? report.PublishedScriptHash,
            };
            db.ReportScriptDrafts.Add(draft);
        }

        draft.ScriptText = scriptText;
        draft.ScriptHash = HashScript(scriptText);
        draft.UpdatedAtUtc = DateTime.UtcNow;
        draft.Status = ReportScriptDraft.DraftStatus;
        draft.Version++;

        audit.Stage(userId, "DRAFT_SAVE", "ReportScriptDraft", reportId.ToString(),
            $"hash={draft.ScriptHash}");
        await db.SaveChangesAsync(ct);
        return new(DraftWorkflowStatus.Ok, draft);
    }

    /// <summary>Submits the open draft for review.</summary>
    public async Task<DraftWorkflowResult> SubmitAsync(
        int reportId, ClaimsPrincipal user, int userId, string? userName,
        long? expectedVersion, CancellationToken ct = default)
    {
        var draft = await OpenDraftAsync(reportId, ct);
        if (draft is null) return new(DraftWorkflowStatus.NotFound);

        var report = await db.Reports.Include(r => r.Folder)
            .FirstAsync(r => r.Id == reportId, ct);
        var permission = await folderPermissions.GetEffectiveReportPermissionAsync(report, user);
        if (!permission.AtLeast(FolderPermission.Author))
            return new(DraftWorkflowStatus.Forbidden);

        if (expectedVersion is null) return new(DraftWorkflowStatus.MissingVersion);
        if (expectedVersion != draft.Version)
            return new(DraftWorkflowStatus.Conflict, draft, "The draft changed since you loaded it.");

        if (draft.Status is not (ReportScriptDraft.DraftStatus or ReportScriptDraft.RejectedStatus))
            return new(DraftWorkflowStatus.InvalidState, draft,
                $"A draft in '{draft.Status}' cannot be submitted.");

        draft.Status = ReportScriptDraft.PendingStatus;
        draft.SubmittedAtUtc = DateTime.UtcNow;
        draft.Version++;
        RecordDecision(draft, "submit", null, userId, userName);

        audit.Stage(userId, "DRAFT_SUBMIT", "ReportScriptDraft", draft.Id.ToString(),
            $"report={reportId}; hash={draft.ScriptHash}");
        await db.SaveChangesAsync(ct);
        return new(DraftWorkflowStatus.Ok, draft);
    }

    /// <summary>
    /// Approves or rejects a pending draft.
    /// </summary>
    /// <remarks>
    /// Requires the <c>ReportApprove</c> capability and <c>Author</c> on the report — a reviewer has
    /// to be able to read what they are approving. The author check is separate and absolute.
    /// </remarks>
    public async Task<DraftWorkflowResult> DecideAsync(
        int reportId, bool approve, string? reason, ClaimsPrincipal user, int userId, string? userName,
        long? expectedVersion, CancellationToken ct = default)
    {
        var draft = await OpenDraftAsync(reportId, ct);
        if (draft is null) return new(DraftWorkflowStatus.NotFound);

        var report = await db.Reports.Include(r => r.Folder)
            .FirstAsync(r => r.Id == reportId, ct);
        var permission = await folderPermissions.GetEffectiveReportPermissionAsync(report, user);
        if (!permission.AtLeast(FolderPermission.Author))
            return new(DraftWorkflowStatus.Forbidden);
        if (!studioAuthorization.HasCapability(user, StudioCapabilities.ReportApprove))
            return new(DraftWorkflowStatus.Forbidden);

        // Checked before capabilities are allowed to matter and with no exception for Admin. A
        // four-eyes control the most privileged account can bypass fails exactly when it is needed.
        if (draft.AuthorUserId == userId)
        {
            return new(DraftWorkflowStatus.SelfApproval, draft,
                "You cannot approve your own draft. Review is only worth having when someone else does it.");
        }

        if (expectedVersion is null) return new(DraftWorkflowStatus.MissingVersion);
        if (expectedVersion != draft.Version)
            return new(DraftWorkflowStatus.Conflict, draft, "The draft changed since you loaded it.");

        if (draft.Status != ReportScriptDraft.PendingStatus)
            return new(DraftWorkflowStatus.InvalidState, draft,
                $"Only a pending draft can be decided; this one is '{draft.Status}'.");

        draft.Status = approve ? ReportScriptDraft.ApprovedStatus : ReportScriptDraft.RejectedStatus;
        draft.DecidedAtUtc = DateTime.UtcNow;
        draft.Version++;
        if (approve)
        {
            draft.ApprovedByUserId = userId;
            draft.ApprovedByUserName = userName;
        }
        RecordDecision(draft, approve ? "approve" : "reject", reason, userId, userName);

        // The hash is in the audit detail because an approval is of specific content, not of a
        // draft id — that is what makes "was this reviewed?" answerable later.
        audit.Stage(userId, approve ? "DRAFT_APPROVE" : "DRAFT_REJECT",
            "ReportScriptDraft", draft.Id.ToString(),
            $"report={reportId}; hash={draft.ScriptHash}; author={draft.AuthorUserName}; reason={reason}");
        await db.SaveChangesAsync(ct);
        return new(DraftWorkflowStatus.Ok, draft);
    }

    /// <summary>
    /// Confirms an approved draft may be published, and marks it published.
    /// </summary>
    /// <remarks>
    /// Writing the script file itself stays with <c>ReportScriptSaveService</c>, which already owns
    /// artifact writes, backup, and rollback. This decides <em>whether</em>, that does the <em>how</em>.
    /// </remarks>
    public async Task<DraftWorkflowResult> BeginPublishAsync(
        int reportId, ClaimsPrincipal user, int userId, CancellationToken ct = default)
    {
        var draft = await OpenDraftAsync(reportId, ct);
        if (draft is null) return new(DraftWorkflowStatus.NotFound);

        var report = await db.Reports.Include(r => r.Folder)
            .FirstAsync(r => r.Id == reportId, ct);
        var permission = await folderPermissions.GetEffectiveReportPermissionAsync(report, user);
        if (!permission.AtLeast(FolderPermission.Manage))
            return new(DraftWorkflowStatus.Forbidden);
        if (!studioAuthorization.HasCapability(user, StudioCapabilities.ReportPublish))
            return new(DraftWorkflowStatus.Forbidden);

        if (draft.Status != ReportScriptDraft.ApprovedStatus)
            return new(DraftWorkflowStatus.InvalidState, draft,
                $"Only an approved draft can be published; this one is '{draft.Status}'.");

        // The live script may have moved since the draft was based on it — someone else published,
        // or it was changed outside the Portal. Publishing anyway would silently discard that work,
        // and the reviewer approved a diff against a base that no longer exists.
        if (draft.BaseScriptHash is not null
            && report.PublishedScriptHash is not null
            && draft.BaseScriptHash != report.PublishedScriptHash)
        {
            return new(DraftWorkflowStatus.StaleBase, draft,
                "The live script changed after this draft was written. Rebase it and have it "
                + "reviewed again — the approval was for a change against a version that is no "
                + "longer there.");
        }

        // The report's own version travels back so the caller can pass it to the script write.
        // Making the client supply it would be ceremony: the draft's If-Match already says which
        // draft is being published, and the stale-base check above is what actually stops this
        // overwriting someone else's work.
        return new(DraftWorkflowStatus.Ok, draft, null, report.Version);
    }

    /// <summary>Marks the draft published once the script write has succeeded.</summary>
    public async Task CompletePublishAsync(
        ReportScriptDraft draft, int userId, CancellationToken ct = default)
    {
        draft.Status = ReportScriptDraft.PublishedStatus;
        draft.PublishedAtUtc = DateTime.UtcNow;
        draft.Version++;

        audit.Stage(userId, "DRAFT_PUBLISH", "ReportScriptDraft", draft.Id.ToString(),
            $"report={draft.ReportId}; hash={draft.ScriptHash}; approvedBy={draft.ApprovedByUserName}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The report's draft that is still in play, if any.</summary>
    public Task<ReportScriptDraft?> OpenDraftAsync(int reportId, CancellationToken ct = default) =>
        db.ReportScriptDrafts
            .Include(d => d.Decisions)
            .Where(d => d.ReportId == reportId
                && (d.Status == ReportScriptDraft.DraftStatus
                    || d.Status == ReportScriptDraft.PendingStatus
                    || d.Status == ReportScriptDraft.ApprovedStatus
                    || d.Status == ReportScriptDraft.RejectedStatus))
            .OrderByDescending(d => d.Id)
            .FirstOrDefaultAsync(ct);

    private static void RecordDecision(
        ReportScriptDraft draft, string decision, string? reason, int userId, string? userName) =>
        draft.Decisions.Add(new ReportScriptDraftDecision
        {
            DraftId = draft.Id,
            Decision = decision,
            Reason = reason,
            ScriptHash = draft.ScriptHash,
            DecidedByUserId = userId,
            DecidedByUserName = userName,
        });
}
