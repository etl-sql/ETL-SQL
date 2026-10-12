using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting.Authoring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Private drafts: saved, unpublished Studio work that belongs to one user. A draft needs neither
/// publish rights nor a writable folder, and it is not a catalog document: nothing lists, serves or
/// executes it. Every query carries the tenant and the owner, so another principal's draft answers
/// exactly as a missing one does.
/// </summary>
public sealed partial class StudioController
{
    [HttpGet("private-drafts")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<IReadOnlyList<StudioPrivateDraftSummaryDto>>> GetPrivateDrafts(CancellationToken ct)
    {
        await ExpirePrivateDraftsAsync(ct);
        var drafts = await OwnPrivateDrafts.AsNoTracking()
            .OrderByDescending(draft => draft.UpdatedAt)
            .Select(draft => new { draft.Id, draft.Name, draft.Kind, draft.Version, draft.UpdatedAt })
            .ToListAsync(ct);
        return Ok(drafts.Select(draft => new StudioPrivateDraftSummaryDto(
            draft.Id, draft.Name, draft.Kind.ToString(), draft.Version, draft.UpdatedAt,
            PrivateDraftExpiresAt(draft.UpdatedAt))).ToList());
    }

    [HttpPost("private-drafts")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioPrivateDraftDto>> CreatePrivateDraft(
        [FromBody] CreateStudioPrivateDraftRequest request, CancellationToken ct)
    {
        if (!portalConfig.Studio.PrivateDrafts) return PrivateDraftsOff();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 160)
            return BadRequest(new StudioPrivateDraftRefusalDto("INVALID_NAME", "A draft name must be between 1 and 160 characters."));
        var kind = CatalogDocumentKind.Report;
        if (!string.IsNullOrWhiteSpace(request.Kind) && !Enum.TryParse(request.Kind, ignoreCase: true, out kind))
            return BadRequest(new StudioPrivateDraftRefusalDto("INVALID_KIND", "Kind must be Report or Pipeline."));
        var content = request.Content ?? "";
        if (RefusePrivateDraftText(content) is { } refusal) return refusal;

        await ExpirePrivateDraftsAsync(ct);
        var limit = Math.Max(1, portalConfig.Studio.PrivateDraftsPerUser);
        if (await OwnPrivateDrafts.CountAsync(ct) >= limit)
            return Conflict(new StudioPrivateDraftRefusalDto("DRAFT_LIMIT",
                $"You already hold {limit} private drafts, the most this Portal keeps for one user. Delete or publish one first."));
        if (await OwnPrivateDrafts.AnyAsync(draft => draft.Name == name, ct))
            return Conflict(new StudioPrivateDraftRefusalDto("DRAFT_NAME_TAKEN", $"You already have a private draft named '{name}'."));

        var now = DateTime.UtcNow;
        var draft = new StudioPrivateDraft
        {
            TenantId = _tenantScope.TenantId,
            OwnerUserId = CurrentUserId,
            Name = name,
            Kind = kind,
            Content = content,
            ContentHash = HashPrivateDraftText(content),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        draft.Revisions.Add(new StudioPrivateDraftRevision
        {
            Version = 1,
            Content = content,
            ContentHash = draft.ContentHash,
            SavedAt = now
        });
        db.StudioPrivateDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        // The audit trail records that a draft exists, never what it says.
        audit.Stage(CurrentUserId, "CREATE_PRIVATE_DRAFT", "StudioPrivateDraft", draft.Id.ToString(), $"kind={kind}");
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetPrivateDraft), new { id = draft.Id }, ToPrivateDraftDto(draft));
    }

    [HttpGet("private-drafts/{id:int}")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioPrivateDraftDto>> GetPrivateDraft(int id, CancellationToken ct)
    {
        var draft = await LivePrivateDraftAsync(id, tracking: false, ct);
        return draft is null ? PrivateDraftNotFound() : Ok(ToPrivateDraftDto(draft));
    }

    [HttpPut("private-drafts/{id:int}")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioPrivateDraftDto>> SavePrivateDraft(
        int id, [FromBody] SaveStudioPrivateDraftRequest request, CancellationToken ct)
    {
        if (!portalConfig.Studio.PrivateDrafts) return PrivateDraftsOff();
        var draft = await LivePrivateDraftAsync(id, tracking: true, ct);
        if (draft is null) return PrivateDraftNotFound();
        if (request.BaseVersion != draft.Version) return PrivateDraftConflict(draft.Version);

        var content = request.Content ?? "";
        if (RefusePrivateDraftText(content) is { } refusal) return refusal;
        var name = request.Name?.Trim();
        if (name is not null && name != draft.Name)
        {
            if (name.Length is < 1 or > 160)
                return BadRequest(new StudioPrivateDraftRefusalDto("INVALID_NAME", "A draft name must be between 1 and 160 characters."));
            if (await OwnPrivateDrafts.AnyAsync(other => other.Id != id && other.Name == name, ct))
                return Conflict(new StudioPrivateDraftRefusalDto("DRAFT_NAME_TAKEN", $"You already have a private draft named '{name}'."));
            draft.Name = name;
        }

        var now = DateTime.UtcNow;
        draft.Content = content;
        draft.ContentHash = HashPrivateDraftText(content);
        draft.Version++;
        draft.UpdatedAt = now;
        draft.UnsavedContent = null;
        draft.UnsavedBaseVersion = null;
        draft.UnsavedUpdatedAt = null;
        db.StudioPrivateDraftRevisions.Add(new StudioPrivateDraftRevision
        {
            DraftId = draft.Id,
            Version = draft.Version,
            Content = content,
            ContentHash = draft.ContentHash,
            SavedAt = now
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another tab saved between the read and the write: the version token or the unique
            // (draft, version) index refused this one.
            db.ChangeTracker.Clear();
            var current = await OwnPrivateDrafts.AsNoTracking().Where(item => item.Id == id)
                .Select(item => (long?)item.Version).FirstOrDefaultAsync(ct);
            return current is { } version ? PrivateDraftConflict(version) : PrivateDraftNotFound();
        }

        var oldest = draft.Version - Math.Max(1, portalConfig.Studio.PrivateDraftRevisionsKept);
        await db.StudioPrivateDraftRevisions
            .Where(revision => revision.DraftId == draft.Id && revision.Version <= oldest)
            .ExecuteDeleteAsync(ct);
        return Ok(ToPrivateDraftDto(draft));
    }

    [HttpDelete("private-drafts/{id:int}")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<IActionResult> DeletePrivateDraft(int id, CancellationToken ct)
    {
        // Deleting is allowed with private drafts off, so turning them off can be followed by a clean-up.
        var draft = await OwnPrivateDrafts.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (draft is null) return NoContent();
        await RemovePrivateDraftAsync(draft, ct);
        audit.Stage(CurrentUserId, "DELETE_PRIVATE_DRAFT", "StudioPrivateDraft", id.ToString(),
            $"kind={draft.Kind}; version={draft.Version}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("private-drafts/{id:int}/revisions")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<IReadOnlyList<StudioPrivateDraftRevisionDto>>> GetPrivateDraftRevisions(int id, CancellationToken ct)
    {
        if (await LivePrivateDraftAsync(id, tracking: false, ct) is null) return PrivateDraftNotFound();
        var revisions = await db.StudioPrivateDraftRevisions.AsNoTracking()
            .Where(revision => revision.DraftId == id)
            .OrderByDescending(revision => revision.Version)
            .Select(revision => new StudioPrivateDraftRevisionDto(revision.Version, revision.SavedAt, revision.ContentHash, null))
            .ToListAsync(ct);
        return Ok(revisions);
    }

    [HttpGet("private-drafts/{id:int}/revisions/{version:long}")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioPrivateDraftRevisionDto>> GetPrivateDraftRevision(int id, long version, CancellationToken ct)
    {
        if (await LivePrivateDraftAsync(id, tracking: false, ct) is null) return PrivateDraftNotFound();
        var revision = await db.StudioPrivateDraftRevisions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.DraftId == id && item.Version == version, ct);
        return revision is null
            ? PrivateDraftNotFound()
            : Ok(new StudioPrivateDraftRevisionDto(revision.Version, revision.SavedAt, revision.ContentHash, revision.Content));
    }

    /// <summary>
    /// Publishes a private draft as a catalog document through the catalog create action, with all
    /// of its checks. The draft is removed in the same transaction that creates the document, so a
    /// refusal or a failure leaves it exactly as it was.
    /// </summary>
    [HttpPost("private-drafts/{id:int}/publish")]
    [RequireStudioCapability(StudioCapabilities.ScriptSave,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    [RequireStudioCapability(StudioCapabilities.ReportPublish,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioReportDto>> PublishPrivateDraft(
        int id, [FromBody] PublishStudioPrivateDraftRequest request, CancellationToken ct)
    {
        var draft = await LivePrivateDraftAsync(id, tracking: true, ct);
        if (draft is null) return PrivateDraftNotFound();
        // What is published is the version the author is looking at, not one saved since.
        if (request.Version != draft.Version) return PrivateDraftConflict(draft.Version);
        if (draft.UnsavedContent is not null)
            return Conflict(new StudioPrivateDraftRefusalDto("DRAFT_HAS_UNSAVED_EDITS",
                "This draft has edits that were never saved. Save or discard them before publishing, so they are not lost with the draft.",
                draft.Version));

        // The history that leaves with the draft: which versions were kept, when, and what they
        // hashed to. The text itself goes to the catalog once, as the published document.
        var revisions = await db.StudioPrivateDraftRevisions.AsNoTracking()
            .Where(revision => revision.DraftId == id)
            .OrderBy(revision => revision.Version)
            .Select(revision => new { revision.Version, revision.SavedAt, revision.ContentHash })
            .ToListAsync(ct);
        var history = string.Join(", ", revisions.Select(revision =>
            $"v{revision.Version}@{revision.SavedAt:yyyy-MM-ddTHH:mm:ssZ}={revision.ContentHash}"));

        var (refusal, report) = await CreateCatalogDocumentAsync(
            string.IsNullOrWhiteSpace(request.Name) ? draft.Name : request.Name, draft.Kind, request.FolderId,
            request.Description, draft.Content,
            "PUBLISH_PRIVATE_DRAFT",
            $"draft={draft.Id}; draftCreated={draft.CreatedAt:yyyy-MM-ddTHH:mm:ssZ}; publishedVersion={draft.Version}; savedVersions=[{history}]",
            () => RemovePrivateDraftAsync(draft, ct), ct);
        return refusal ?? CreatedAtAction(nameof(GetReports), new { }, ToDto(report!));
    }

    // Unsaved edits to a private draft: what the author typed since the last save, kept so a crash
    // or an expired sign-in loses nothing. They are not a version, and follow the recovery-draft
    // policy (Portal:Studio:DraftRecovery and DraftRetentionDays), not the private-draft one.

    [HttpGet("private-drafts/{id:int}/unsaved")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioRecoveryDraftDto>> GetPrivateDraftUnsavedEdits(int id, CancellationToken ct)
    {
        if (!portalConfig.Studio.DraftRecovery) return DraftRecoveryOff();
        var draft = await LivePrivateDraftAsync(id, tracking: false, ct);
        if (draft is null) return PrivateDraftNotFound();
        if (draft.UnsavedContent is null || draft.UnsavedUpdatedAt is not { } updatedAt) return NotFound();
        if (updatedAt < DraftExpiry())
        {
            await ClearPrivateDraftUnsavedEditsAsync(id, ct);
            return NotFound();
        }
        return Ok(new StudioRecoveryDraftDto(draft.UnsavedContent, draft.UnsavedBaseVersion, null, updatedAt));
    }

    [HttpPut("private-drafts/{id:int}/unsaved")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<IActionResult> PutPrivateDraftUnsavedEdits(
        int id, [FromBody] SaveStudioRecoveryDraftRequest request, CancellationToken ct)
    {
        if (!portalConfig.Studio.DraftRecovery) return DraftRecoveryOff();
        if (!portalConfig.Studio.PrivateDrafts) return PrivateDraftsOff();
        if (await LivePrivateDraftAsync(id, tracking: false, ct) is null) return PrivateDraftNotFound();
        var content = request.Content ?? "";
        if (RefusePrivateDraftText(content) is { } refusal) return refusal;

        // Written without loading the row: the draft's version is a concurrency token, and unsaved
        // edits must neither move it nor lose a race against a save that does.
        var now = DateTime.UtcNow;
        var baseVersion = request.BaseVersion;
        var written = await OwnPrivateDrafts.Where(draft => draft.Id == id).ExecuteUpdateAsync(update => update
            .SetProperty(draft => draft.UnsavedContent, content)
            .SetProperty(draft => draft.UnsavedBaseVersion, baseVersion)
            .SetProperty(draft => draft.UnsavedUpdatedAt, now), ct);
        return written == 0 ? PrivateDraftNotFound() : NoContent();
    }

    [HttpDelete("private-drafts/{id:int}/unsaved")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<IActionResult> DeletePrivateDraftUnsavedEdits(int id, CancellationToken ct)
    {
        // Allowed with recovery off, as for a catalog document's recovery draft.
        await ClearPrivateDraftUnsavedEditsAsync(id, ct);
        return NoContent();
    }

    private Task<int> ClearPrivateDraftUnsavedEditsAsync(int id, CancellationToken ct) =>
        OwnPrivateDrafts.Where(draft => draft.Id == id).ExecuteUpdateAsync(update => update
            .SetProperty(draft => draft.UnsavedContent, (string?)null)
            .SetProperty(draft => draft.UnsavedBaseVersion, (long?)null)
            .SetProperty(draft => draft.UnsavedUpdatedAt, (DateTime?)null), ct);

    /// <summary>The caller's own drafts in the caller's tenant. Nothing else is ever queried.</summary>
    private IQueryable<StudioPrivateDraft> OwnPrivateDrafts =>
        CatalogScope.StudioPrivateDrafts.Where(draft => draft.OwnerUserId == CurrentUserId);

    /// <summary>The caller's draft, unless it has outlived its retention, in which case it is removed.</summary>
    private async Task<StudioPrivateDraft?> LivePrivateDraftAsync(int id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? OwnPrivateDrafts : OwnPrivateDrafts.AsNoTracking();
        var draft = await query.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (draft is null || draft.UpdatedAt >= PrivateDraftCutoff()) return draft;
        await ExpirePrivateDraftsAsync(ct);
        return null;
    }

    /// <summary>
    /// Removes every draft in this tenant that has outlived its retention, whoever owns it, so a
    /// user who never returns does not keep their text here for good.
    /// </summary>
    private async Task ExpirePrivateDraftsAsync(CancellationToken ct)
    {
        var cutoff = PrivateDraftCutoff();
        var expired = CatalogScope.StudioPrivateDrafts.Where(draft => draft.UpdatedAt < cutoff);
        if (!await expired.AnyAsync(ct)) return;
        await db.StudioPrivateDraftRevisions
            .Where(revision => expired.Select(draft => draft.Id).Contains(revision.DraftId))
            .ExecuteDeleteAsync(ct);
        await expired.ExecuteDeleteAsync(ct);
    }

    private async Task RemovePrivateDraftAsync(StudioPrivateDraft draft, CancellationToken ct)
    {
        await db.StudioPrivateDraftRevisions.Where(revision => revision.DraftId == draft.Id).ExecuteDeleteAsync(ct);
        db.StudioPrivateDrafts.Remove(draft);
        await db.SaveChangesAsync(ct);
    }

    private int PrivateDraftRetentionDays => Math.Max(1, portalConfig.Studio.PrivateDraftRetentionDays);

    private DateTime PrivateDraftCutoff() => DateTime.UtcNow.AddDays(-PrivateDraftRetentionDays);

    private DateTime PrivateDraftExpiresAt(DateTime updatedAt) => updatedAt.AddDays(PrivateDraftRetentionDays);

    private StudioPrivateDraftDto ToPrivateDraftDto(StudioPrivateDraft draft) => new(
        draft.Id, draft.Name, draft.Kind.ToString(), draft.Version, draft.Content, draft.UpdatedAt,
        PrivateDraftExpiresAt(draft.UpdatedAt));

    /// <summary>The checks a catalog save would run that also apply to text nobody else can read.</summary>
    private ObjectResult? RefusePrivateDraftText(string content)
    {
        if (content.Length > Math.Max(1, portalConfig.DesignerLimits.MaxScriptCharacters))
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new StudioPrivateDraftRefusalDto("DRAFT_TOO_LARGE", "The draft is larger than a script may be."));
        if (StudioDraftPolicy.ContainsPlaintextSecret(content))
            return UnprocessableEntity(new StudioPrivateDraftRefusalDto("PLAINTEXT_SECRET",
                "The draft holds a plaintext credential, so it was not kept. Encrypt the credential or use a SECRET: reference."));
        return null;
    }

    private static string HashPrivateDraftText(string content) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private ObjectResult PrivateDraftsOff() => StatusCode(StatusCodes.Status409Conflict,
        new StudioPrivateDraftRefusalDto("PRIVATE_DRAFTS_OFF",
            "Private drafts are turned off on this Portal (Portal:Studio:PrivateDrafts)."));

    private NotFoundObjectResult PrivateDraftNotFound() =>
        NotFound(new StudioPrivateDraftRefusalDto("DRAFT_NOT_FOUND", "No such private draft."));

    private ConflictObjectResult PrivateDraftConflict(long currentVersion) =>
        Conflict(new StudioPrivateDraftRefusalDto("DRAFT_CONFLICT",
            "This draft was saved somewhere else since you opened it. Reopen it to see the newer text.", currentVersion));
}
