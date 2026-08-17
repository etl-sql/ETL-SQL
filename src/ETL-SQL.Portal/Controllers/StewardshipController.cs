using System.Security.Claims;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Durable governance workflow API.
///
/// <para>Authorization is split three ways, because these are three different authorities and
/// collapsing them is how a viewer ends up able to change the threshold that decides whether the
/// estate is compliant:</para>
///
/// <list type="bullet">
/// <item><b>Read</b> — <c>StewardshipViewer</c> and above. Stewards must see other stewards' work;
/// a queue you cannot see past is a queue you cannot cover for.</item>
/// <item><b>Decide</b> — <c>DataSteward</c> and above. Ignoring a finding, accepting a risk, marking
/// an asset reviewed, and assigning a badge are all steward judgements, and all audited.</item>
/// <item><b>Configure</b> — <c>StewardshipManager</c> or <c>Admin</c>. Thresholds, enabled checks,
/// glossary content, and suppression categories change what "governed" means estate-wide. Whoever
/// can lower the bar is not the same person as whoever works against it.</item>
/// </list>
///
/// <para>Every mutation writes an audit row. A governance surface whose own changes are unauditable
/// cannot be evidence of anything.</para>
/// </summary>
[ApiController]
[Route("api/governance")]
[Authorize(Policy = "GovernanceRead")]
[RequirePortalModule("Reporting")]
public sealed class GovernanceController(
    PortalDbContext db,
    GovernanceService governance,
    AuditService audit,
    DatasetTenantScope tenantScope) : ControllerBase
{
    private string TenantId => tenantScope.TenantId;
    private IQueryable<StewardshipFinding> Findings =>
        db.StewardshipFindings.Where(value => value.TenantId == TenantId);
    private IQueryable<StewardshipScan> Scans =>
        db.StewardshipScans.Where(value => value.TenantId == TenantId);
    private IQueryable<StewardshipResolutionCategory> Categories =>
        db.StewardshipResolutionCategories.Where(value => value.TenantId == TenantId);
    private IQueryable<StewardshipGlossaryTerm> Glossary =>
        db.StewardshipGlossaryTerms.Where(value => value.TenantId == TenantId);
    private IQueryable<StewardshipAssetReview> Reviews =>
        db.StewardshipAssetReviews.Where(value => value.TenantId == TenantId);
    private IQueryable<StewardshipAssetBadge> Badges =>
        db.StewardshipAssetBadges.Where(value => value.TenantId == TenantId);
    private int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private string? CurrentUserName => User.Identity?.Name;

    // ── Dashboard ───────────────────────────────────────────────────────────────────────────────

    /// <param name="scope">
    /// <c>mine</c> narrows to the caller's own steward queue. It is a filter, never a boundary —
    /// see the authorization note on the class.
    /// </param>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] string scope = "all", CancellationToken cancellationToken = default)
    {
        var steward = string.Equals(scope, "mine", StringComparison.OrdinalIgnoreCase)
            ? CurrentUserName
            : null;
        return Ok(await governance.GetDashboardAsync(steward, cancellationToken));
    }

    [HttpGet("findings")]
    public async Task<IActionResult> GetFindings(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        var query = Findings.AsNoTracking().Include(f => f.Decisions).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(f => f.Status == status);

        var findings = await query
            .OrderByDescending(f => f.LastSeenUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return Ok(findings.Select(GovernanceService.ToFindingDto));
    }

    [HttpGet("scans")]
    public async Task<IActionResult> GetScans(
        [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var scans = await Scans.AsNoTracking()
            .OrderByDescending(s => s.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return Ok(scans.Select(GovernanceService.ToScanDto));
    }

    // ── Steward decisions ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Disposes of a finding: <c>ignore</c>, <c>accept-risk</c>, or <c>reopen</c>.
    /// </summary>
    /// <remarks>
    /// A reason is mandatory and the asset version is mandatory. Without the reason the decision
    /// cannot be reviewed; without the version it cannot be revisited when the asset changes, and a
    /// suppression that never revisits is an exemption granted in perpetuity by accident.
    /// </remarks>
    [HttpPost("findings/{id:int}/decide")]
    [Authorize(Policy = "GovernanceDecide")]
    public async Task<IActionResult> DecideFinding(
        int id, [FromBody] DecideFindingRequest req, CancellationToken cancellationToken = default)
    {
        var decision = req.Decision?.Trim().ToLowerInvariant();
        if (decision is not ("ignore" or "accept-risk" or "reopen"))
            return BadRequest(new { error = "Decision must be 'ignore', 'accept-risk', or 'reopen'." });
        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { error = "A reason is required so the decision can be reviewed later." });
        if (decision != "reopen" && string.IsNullOrWhiteSpace(req.AssetVersion))
            return BadRequest(new { error = "An asset version is required so the suppression can be revisited when the asset changes." });

        var finding = await Findings
            .Include(f => f.Decisions)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (finding is null) return NotFound(new { error = "Finding not found." });

        DateTime? suppressedUntil = null;
        if (decision != "reopen")
        {
            var category = string.IsNullOrWhiteSpace(req.CategoryValue)
                ? null
                : await Categories
                    .FirstOrDefaultAsync(c => c.Value == req.CategoryValue && !c.Disabled, cancellationToken);
            if (!string.IsNullOrWhiteSpace(req.CategoryValue) && category is null)
                return BadRequest(new { error = $"Resolution category '{req.CategoryValue}' is not defined or is disabled." });

            if (category?.ExpiryDays is { } days)
                suppressedUntil = DateTime.UtcNow.AddDays(days);

            finding.Status = decision == "ignore"
                ? StewardshipFinding.IgnoredStatus
                : StewardshipFinding.AcceptedRiskStatus;
            finding.AssetVersion = req.AssetVersion;
            finding.SuppressedUntilUtc = suppressedUntil;
        }
        else
        {
            finding.Status = StewardshipFinding.ReopenedStatus;
            finding.SuppressedUntilUtc = null;
        }

        db.StewardshipFindingDecisions.Add(new StewardshipFindingDecision
        {
            TenantId = TenantId,
            FindingId = finding.Id,
            Decision = decision,
            CategoryValue = req.CategoryValue,
            Reason = req.Reason.Trim(),
            AssetVersion = req.AssetVersion,
            DecidedByUserId = CurrentUserId,
            DecidedByUserName = CurrentUserName
        });

        await audit.LogAsync(CurrentUserId, $"GOVERNANCE_{decision.ToUpperInvariant().Replace('-', '_')}",
            "StewardshipFinding", finding.Id.ToString(),
            $"asset={finding.AssetKey}; rule={finding.RuleKey}; version={req.AssetVersion}; reason={req.Reason.Trim()}");

        await db.SaveChangesAsync(cancellationToken);
        var saved = await Findings.AsNoTracking()
            .Include(f => f.Decisions)
            .FirstAsync(f => f.Id == finding.Id, cancellationToken);
        return Ok(GovernanceService.ToFindingDto(saved));
    }

    [HttpPost("assets/review")]
    [Authorize(Policy = "GovernanceDecide")]
    public async Task<IActionResult> ReviewAsset(
        [FromBody] ReviewAssetRequest req, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(req.AssetKey) || string.IsNullOrWhiteSpace(req.AssetVersion))
            return BadRequest(new { error = "Asset key and version are required." });

        var review = await Reviews
            .FirstOrDefaultAsync(r => r.AssetKey == req.AssetKey, cancellationToken);
        if (review is null)
        {
            review = new StewardshipAssetReview { TenantId = TenantId, AssetKey = req.AssetKey };
            db.StewardshipAssetReviews.Add(review);
        }

        review.ReviewedVersion = req.AssetVersion;
        review.Note = req.Note;
        review.ReviewedAtUtc = DateTime.UtcNow;
        review.ReviewedByUserId = CurrentUserId;

        await audit.LogAsync(CurrentUserId, "GOVERNANCE_REVIEW_ASSET", "GovernanceAsset", req.AssetKey,
            $"version={req.AssetVersion}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { req.AssetKey, req.AssetVersion, review.ReviewedAtUtc });
    }

    [HttpPost("assets/badges")]
    [Authorize(Policy = "GovernanceDecide")]
    public async Task<IActionResult> AssignBadge(
        [FromBody] AssignBadgeRequest req, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(req.AssetKey) || string.IsNullOrWhiteSpace(req.Badge))
            return BadRequest(new { error = "Asset key and badge are required." });

        var existing = await Badges
            .FirstOrDefaultAsync(b => b.AssetKey == req.AssetKey && b.Badge == req.Badge, cancellationToken);
        if (existing is not null)
            return Ok(new { req.AssetKey, req.Badge, existing.AssignedAtUtc });

        db.StewardshipAssetBadges.Add(new StewardshipAssetBadge
        {
            TenantId = TenantId,
            AssetKey = req.AssetKey,
            Badge = req.Badge,
            AssetVersion = req.AssetVersion,
            Reason = req.Reason,
            AssignedByUserId = CurrentUserId
        });

        await audit.LogAsync(CurrentUserId, "GOVERNANCE_ASSIGN_BADGE", "GovernanceAsset", req.AssetKey,
            $"badge={req.Badge}; version={req.AssetVersion}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { req.AssetKey, req.Badge });
    }

    [HttpDelete("assets/badges")]
    [Authorize(Policy = "GovernanceDecide")]
    public async Task<IActionResult> RemoveBadge(
        [FromQuery] string assetKey, [FromQuery] string badge, CancellationToken cancellationToken = default)
    {
        var existing = await Badges
            .FirstOrDefaultAsync(b => b.AssetKey == assetKey && b.Badge == badge, cancellationToken);
        if (existing is null) return NotFound(new { error = "Badge assignment not found." });

        db.StewardshipAssetBadges.Remove(existing);
        await audit.LogAsync(CurrentUserId, "GOVERNANCE_REMOVE_BADGE", "GovernanceAsset", assetKey,
            $"badge={badge}");
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ── Governance manager: scans, settings, categories, glossary ───────────────────────────────

    /// <summary>
    /// Recomputes findings across the estate. Restricted to governance managers because a scan
    /// rewrites the queue every steward is working from.
    /// </summary>
    [HttpPost("scan")]
    [Authorize(Policy = "GovernanceConfigure")]
    public async Task<IActionResult> RunScan(CancellationToken cancellationToken = default)
    {
        var scan = await governance.ScanAsync("manual", CurrentUserId, cancellationToken);
        await audit.LogAsync(CurrentUserId, "GOVERNANCE_SCAN", "StewardshipScan", scan.Id.ToString(),
            $"assets={scan.AssetsScanned}; opened={scan.FindingsOpened}; resolved={scan.FindingsResolved}; reopened={scan.FindingsReopened}");
        return Ok(GovernanceService.ToScanDto(scan));
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken = default) =>
        Ok(ToDto(await governance.GetSettingsAsync(cancellationToken)));

    [HttpPut("settings")]
    [Authorize(Policy = "GovernanceConfigure")]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateStewardshipSettingsRequest req, CancellationToken cancellationToken = default)
    {
        if (req.TargetScore is < 0 or > 100)
            return BadRequest(new { error = "Target score must be between 0 and 100." });
        if (req.StaleAfterDays < 1)
            return BadRequest(new { error = "Stale-after days must be at least 1." });

        var level = string.IsNullOrWhiteSpace(req.PolicyLevel) ? "scored" : req.PolicyLevel.Trim().ToLowerInvariant();
        if (level is not ("visible" or "suggestion" or "scored" or "certification-gate"))
            return BadRequest(new { error = "Policy level must be visible, suggestion, scored, or certification-gate." });

        var settings = await governance.GetSettingsAsync(cancellationToken);
        var before = $"target={settings.TargetScore}; level={settings.PolicyLevel}; glossary={settings.EnableGlossaryCheck}";

        settings.TargetScore = req.TargetScore;
        settings.EnableMetadataCheck = req.EnableMetadataCheck;
        settings.EnableProtectedDataCheck = req.EnableProtectedDataCheck;
        settings.EnableGlossaryCheck = req.EnableGlossaryCheck;
        settings.EnableStalenessCheck = req.EnableStalenessCheck;
        settings.DeductMetadata = Math.Clamp(req.DeductMetadata, 0, 100);
        settings.DeductProtectedData = Math.Clamp(req.DeductProtectedData, 0, 100);
        settings.DeductGlossary = Math.Clamp(req.DeductGlossary, 0, 100);
        settings.DeductStaleness = Math.Clamp(req.DeductStaleness, 0, 100);
        settings.StaleAfterDays = req.StaleAfterDays;
        settings.PolicyLevel = level;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = CurrentUserId;
        settings.Version++;

        // Before and after both, because "who lowered the threshold" is the question this row exists
        // to answer, and the new value alone does not answer it.
        await audit.LogAsync(CurrentUserId, "GOVERNANCE_UPDATE_SETTINGS", "StewardshipSettings",
            settings.Id.ToString(),
            $"before[{before}] after[target={settings.TargetScore}; level={settings.PolicyLevel}; glossary={settings.EnableGlossaryCheck}]");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(settings));
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken = default)
    {
        var categories = await Categories.AsNoTracking()
            .OrderBy(c => c.Label)
            .ToListAsync(cancellationToken);
        return Ok(categories.Select(c =>
            new GovernanceCategoryDto(c.Id, c.Value, c.Label, c.Color, c.ExpiryDays, c.Disabled)));
    }

    [HttpPost("categories")]
    [Authorize(Policy = "GovernanceConfigure")]
    public async Task<IActionResult> SaveCategory(
        [FromBody] SaveGovernanceCategoryRequest req, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(req.Value) || string.IsNullOrWhiteSpace(req.Label))
            return BadRequest(new { error = "Category value and label are required." });

        var value = req.Value.Trim();
        var category = await Categories
            .FirstOrDefaultAsync(c => c.Value == value, cancellationToken);
        var isNew = category is null;
        if (category is null)
        {
            category = new StewardshipResolutionCategory
            {
                TenantId = TenantId,
                Value = value,
                CreatedByUserId = CurrentUserId
            };
            db.StewardshipResolutionCategories.Add(category);
        }

        category.Label = req.Label.Trim();
        category.Color = string.IsNullOrWhiteSpace(req.Color) ? "noise" : req.Color.Trim();
        category.ExpiryDays = req.ExpiryDays is > 0 ? req.ExpiryDays : null;
        category.Disabled = req.Disabled;
        category.Version++;

        await audit.LogAsync(CurrentUserId, isNew ? "GOVERNANCE_CREATE_CATEGORY" : "GOVERNANCE_UPDATE_CATEGORY",
            "GovernanceCategory", value, $"label={category.Label}; expiryDays={category.ExpiryDays?.ToString() ?? "none"}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new GovernanceCategoryDto(
            category.Id, category.Value, category.Label, category.Color, category.ExpiryDays, category.Disabled));
    }

    /// <summary>
    /// Disables a category rather than deleting it. Decisions cite the category by value, and
    /// removing the row would leave historical suppressions citing a reason nobody can look up.
    /// </summary>
    [HttpDelete("categories/{value}")]
    [Authorize(Policy = "GovernanceConfigure")]
    public async Task<IActionResult> DisableCategory(string value, CancellationToken cancellationToken = default)
    {
        var category = await Categories
            .FirstOrDefaultAsync(c => c.Value == value, cancellationToken);
        if (category is null) return NotFound(new { error = "Category not found." });

        category.Disabled = true;
        category.Version++;
        await audit.LogAsync(CurrentUserId, "GOVERNANCE_DISABLE_CATEGORY", "GovernanceCategory", value, null);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("glossary")]
    public async Task<IActionResult> GetGlossary(CancellationToken cancellationToken = default)
    {
        var terms = await Glossary.AsNoTracking()
            .OrderBy(t => t.Term)
            .ToListAsync(cancellationToken);
        return Ok(terms.Select(ToDto));
    }

    [HttpPost("glossary")]
    [Authorize(Policy = "GovernanceConfigure")]
    public async Task<IActionResult> SaveGlossaryTerm(
        [FromBody] SaveGlossaryTermRequest req, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(req.Term) || string.IsNullOrWhiteSpace(req.DataType)
            || string.IsNullOrWhiteSpace(req.Aliases) || string.IsNullOrWhiteSpace(req.Description))
            return BadRequest(new { error = "Term, data type, aliases, and description are required." });

        var name = req.Term.Trim();
        var term = await Glossary.FirstOrDefaultAsync(t => t.Term == name, cancellationToken);
        var isNew = term is null;
        if (term is null)
        {
            term = new StewardshipGlossaryTerm
            {
                TenantId = TenantId,
                Term = name,
                CreatedByUserId = CurrentUserId
            };
            db.StewardshipGlossaryTerms.Add(term);
        }

        term.DataType = req.DataType.Trim();
        term.Aliases = req.Aliases.Trim();
        term.Description = req.Description.Trim();
        term.Formula = string.IsNullOrWhiteSpace(req.Formula) ? null : req.Formula.Trim();
        term.Steward = req.Steward?.Trim();
        term.Disabled = req.Disabled;
        term.UpdatedAtUtc = DateTime.UtcNow;
        term.UpdatedByUserId = CurrentUserId;
        term.Version++;

        await audit.LogAsync(CurrentUserId, isNew ? "GOVERNANCE_CREATE_TERM" : "GOVERNANCE_UPDATE_TERM",
            "StewardshipGlossaryTerm", name, $"aliases={term.Aliases}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(term));
    }

    [HttpDelete("glossary/{term}")]
    [Authorize(Policy = "GovernanceConfigure")]
    public async Task<IActionResult> DeleteGlossaryTerm(string term, CancellationToken cancellationToken = default)
    {
        var existing = await Glossary.FirstOrDefaultAsync(t => t.Term == term, cancellationToken);
        if (existing is null) return NotFound(new { error = "Glossary term not found." });

        db.StewardshipGlossaryTerms.Remove(existing);
        await audit.LogAsync(CurrentUserId, "GOVERNANCE_DELETE_TERM", "StewardshipGlossaryTerm", term, null);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static StewardshipSettingsDto ToDto(StewardshipSettings s) => new(
        s.TargetScore, s.EnableMetadataCheck, s.EnableProtectedDataCheck, s.EnableGlossaryCheck,
        s.EnableStalenessCheck, s.DeductMetadata, s.DeductProtectedData, s.DeductGlossary,
        s.DeductStaleness, s.StaleAfterDays, s.PolicyLevel, s.UpdatedAtUtc, s.Version);

    private static StewardshipGlossaryTermDto ToDto(StewardshipGlossaryTerm t) => new(
        t.Id, t.Term, t.DataType, t.Aliases, t.Description, t.Formula, t.Steward, t.Disabled, t.UpdatedAtUtc);
}
