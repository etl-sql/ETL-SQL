using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// The governance dashboard's durable brain: settings, scoring, scans, findings, and decisions.
///
/// <para>Two rules shape everything here.</para>
///
/// <para><b>Scores are explainable, never opaque.</b> Every point an asset loses maps to a named
/// rule, and the rule travels with the score. A number a steward cannot argue with is a number they
/// will learn to ignore.</para>
///
/// <para><b>Decisions are version-scoped.</b> Ignoring a finding or accepting a risk records the
/// asset version it was decided against. When the asset changes, the suppression stops applying and
/// the finding reopens. A suppression that survives the change it was granted for is not governance;
/// it is a permanent exemption nobody remembers granting.</para>
/// </summary>
public sealed class GovernanceService(
    PortalDbContext db,
    PortalTenantLineageCatalog lineageCatalog,
    DatasetTenantScope tenantScope)
{
    private string TenantId => tenantScope.TenantId;
    private IQueryable<StewardshipSettings> Settings =>
        db.StewardshipSettings.Where(value => value.TenantId == TenantId);
    private IQueryable<StewardshipAssetReview> Reviews =>
        db.StewardshipAssetReviews.Where(value => value.TenantId == TenantId);
    private IQueryable<StewardshipAssetBadge> Badges =>
        db.StewardshipAssetBadges.Where(value => value.TenantId == TenantId);
    private IQueryable<StewardshipFinding> Findings =>
        db.StewardshipFindings.Where(value => value.TenantId == TenantId);
    private IQueryable<StewardshipScan> Scans =>
        db.StewardshipScans.Where(value => value.TenantId == TenantId);
    /// <summary>Rules the scan evaluates. The key is stable; the label is not.</summary>
    public static class Rules
    {
        public const string MissingMetadata = "missing-metadata";
        public const string ProtectedData = "untagged-protected-data";
        public const string Glossary = "glossary-review";
        public const string Stale = "changed-since-review";
        public const string BelowThreshold = "below-threshold";
    }

    // ── Settings ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the single settings row, creating it from defaults on first access. Defaults are the
    /// conservative end of the rollout ladder: glossary checks start off, because activating them
    /// against an estate that has never seen them would fail assets nobody has been asked about.
    /// </summary>
    public async Task<StewardshipSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var settings = await Settings
            .FirstOrDefaultAsync(s => s.Scope == Data.StewardshipSettings.DefaultScope, ct);
        if (settings is not null) return settings;

        settings = new StewardshipSettings
        {
            TenantId = TenantId,
            Scope = Data.StewardshipSettings.DefaultScope
        };
        db.StewardshipSettings.Add(settings);
        try
        {
            await db.SaveChangesAsync(ct);
            return settings;
        }
        catch (DbUpdateException)
        {
            // The dashboard issues five reads at once, each on its own scoped context, so on a
            // cold database several of them race to create this row. The unique index on Scope is
            // what makes that safe: exactly one insert wins and the losers read the winner's row.
            // Serialising through a process-local lock would be wrong instead of merely slower —
            // Portal runs multi-node, and the other node is not holding your lock.
            db.Entry(settings).State = EntityState.Detached;
            return await Settings
                .FirstAsync(s => s.Scope == Data.StewardshipSettings.DefaultScope, ct);
        }
    }

    // ── Scoring ─────────────────────────────────────────────────────────────────────────────────

    /// <param name="Deductions">
    /// Every lost point with the rule that took it. Returned alongside the score so the UI never has
    /// to reconstruct the reasoning — and cannot reconstruct it differently.
    /// </param>
    public sealed record ScoreResult(
        int Score,
        IReadOnlyList<GovernanceDeductionDto> Deductions,
        IReadOnlyList<string> AutomaticBadges);

    public static ScoreResult Score(StewardshipAssetDto asset, StewardshipSettings settings, bool reviewedCurrent)
    {
        var deductions = new List<GovernanceDeductionDto>();
        var badges = new List<string>();

        if (asset.MissingTags.Count > 0)
        {
            badges.Add("Needs Metadata");
            if (settings.EnableMetadataCheck)
            {
                deductions.Add(new GovernanceDeductionDto(
                    Rules.MissingMetadata, settings.DeductMetadata,
                    $"Missing required metadata: {string.Join(", ", asset.MissingTags)}"));
            }
        }

        if (asset.IsSensitive || asset.IsRestricted)
        {
            badges.Add(asset.IsRestricted ? "Restricted" : "Protected Data");
            // Carrying protected data is not itself a fault — failing to classify it is. An asset
            // tagged with its classification has done the thing the rule asks for.
            if (settings.EnableProtectedDataCheck && string.IsNullOrWhiteSpace(asset.Classification))
            {
                badges.Add("Untagged Protected Data");
                deductions.Add(new GovernanceDeductionDto(
                    Rules.ProtectedData, settings.DeductProtectedData,
                    "Protected data present with no @classification tag"));
            }
        }

        if (asset.IsStale)
        {
            badges.Add("Stale Lineage");
            if (settings.EnableStalenessCheck)
            {
                deductions.Add(new GovernanceDeductionDto(
                    Rules.Stale, settings.DeductStaleness, asset.StaleReason));
            }
        }

        if (!reviewedCurrent)
        {
            badges.Add("Changed Since Review");
            // Deliberately not scored on its own: it already shows as a badge and a finding, and
            // double-counting would make an unreviewed change look worse than untagged PII.
        }

        var score = Math.Clamp(100 - deductions.Sum(d => d.Points), 0, 100);
        return new ScoreResult(score, deductions, badges);
    }

    // ── Scan ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes findings for the whole estate and reconciles them with what is already recorded.
    ///
    /// <para>Reconciliation, not replacement: an existing finding keeps its identity and its
    /// decision history, a fixed one resolves, and a suppressed one whose asset version has moved on
    /// reopens. Deleting and re-creating would silently discard every steward decision on each
    /// scan — the dashboard would look clean and be empty of history.</para>
    /// </summary>
    public async Task<StewardshipScan> ScanAsync(string trigger, int? userId, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var scan = new StewardshipScan
        {
            TenantId = TenantId,
            Trigger = trigger,
            StartedByUserId = userId,
            Status = "running"
        };
        db.StewardshipScans.Add(scan);
        await db.SaveChangesAsync(ct);

        try
        {
            var assets = await LoadAssetsAsync(settings, ct);
            var reviews = await Reviews.ToDictionaryAsync(r => r.AssetKey, ct);
            var existing = await Findings.Include(f => f.Decisions).ToListAsync(ct);
            var byKey = existing.ToDictionary(f => (f.AssetKey, f.RuleKey));
            var now = DateTime.UtcNow;
            var seen = new HashSet<(string, string)>();

            foreach (var asset in assets)
            {
                var key = StewardshipProjection.AssetKey(asset);
                var version = StewardshipProjection.AssetVersion(asset);
                reviews.TryGetValue(key, out var review);
                var reviewedCurrent = review is not null && review.ReviewedVersion == version;

                var result = Score(asset, settings, reviewedCurrent);
                var failing = result.Deductions.Select(d => (d.RuleKey, d.Reason)).ToList();
                if (result.Score < settings.TargetScore)
                {
                    failing.Add((Rules.BelowThreshold,
                        $"Score {result.Score} is below the threshold of {settings.TargetScore}"));
                }
                if (review is not null && !reviewedCurrent)
                {
                    failing.Add((Rules.Stale,
                        $"Reviewed at version {review.ReviewedVersion}; current version is {version}"));
                }

                foreach (var (ruleKey, detail) in failing)
                {
                    seen.Add((key, ruleKey));
                    if (byKey.TryGetValue((key, ruleKey), out var finding))
                        UpdateExisting(finding, version, detail, now, scan);
                    else
                        OpenNew(key, ruleKey, version, detail, now, scan);
                }
            }

            // Anything not re-raised has been fixed by a newer version. Resolving is the automatic
            // reconciliation the workflow promises: a developer publishes a fix and the queue clears
            // itself, without a steward closing tickets by hand.
            foreach (var finding in existing)
            {
                if (seen.Contains((finding.AssetKey, finding.RuleKey))) continue;
                if (finding.Status == StewardshipFinding.ResolvedStatus) continue;
                finding.Status = StewardshipFinding.ResolvedStatus;
                finding.ResolvedAtUtc = now;
                scan.FindingsResolved++;
            }

            scan.AssetsScanned = assets.Count;
            scan.Status = "completed";
            scan.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return scan;
        }
        catch (Exception ex)
        {
            // A failed scan is recorded as failed, not left "running" forever. The dashboard has to
            // be able to say "the last scan failed" — silently missing findings reads as a clean
            // estate, which is the most dangerous wrong answer this surface can give.
            scan.Status = "failed";
            scan.CompletedAtUtc = DateTime.UtcNow;
            scan.Error = ETL_SQL.Core.Common.SecretRedactor.Redact(ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private void OpenNew(
        string assetKey, string ruleKey, string version, string detail, DateTime now, StewardshipScan scan)
    {
        db.StewardshipFindings.Add(new StewardshipFinding
        {
            TenantId = TenantId,
            AssetKey = assetKey,
            RuleKey = ruleKey,
            AssetVersion = version,
            Detail = detail,
            Status = StewardshipFinding.OpenStatus,
            FirstSeenUtc = now,
            LastSeenUtc = now
        });
        scan.FindingsOpened++;
    }

    private static void UpdateExisting(
        StewardshipFinding finding, string version, string detail, DateTime now, StewardshipScan scan)
    {
        finding.LastSeenUtc = now;
        finding.Detail = detail;

        var suppressed = finding.Status is StewardshipFinding.IgnoredStatus or StewardshipFinding.AcceptedRiskStatus;
        if (!suppressed)
        {
            if (finding.Status == StewardshipFinding.ResolvedStatus)
            {
                finding.Status = StewardshipFinding.ReopenedStatus;
                finding.ResolvedAtUtc = null;
                scan.FindingsReopened++;
            }
            finding.AssetVersion = version;
            return;
        }

        // A suppression covers the version it was granted against and no other. Either the asset
        // moved on, or the category's expiry ran out — both mean the decision no longer describes
        // what is in front of the steward.
        var versionMoved = finding.AssetVersion != version;
        var expired = finding.SuppressedUntilUtc is { } until && until <= now;
        if (versionMoved || expired)
        {
            finding.Status = StewardshipFinding.ReopenedStatus;
            finding.AssetVersion = version;
            finding.SuppressedUntilUtc = null;
            scan.FindingsReopened++;
        }
    }

    private async Task<List<StewardshipAssetDto>> LoadAssetsAsync(
        StewardshipSettings settings, CancellationToken ct)
    {
        _ = ct;
        var entries = await lineageCatalog.GetRecentLineageAsync(5000);
        return LineageAssetCollapse.LatestPerAsset(entries)
            .Select(e => StewardshipProjection.ToAsset(e, settings.StaleAfterDays))
            .ToList();
    }

    // ── Dashboard read model ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The dashboard's whole payload: assets with explained scores, findings, and the scan that
    /// produced them.
    /// </summary>
    /// <remarks>
    /// The last scan is included precisely so the UI can distinguish "no findings" from "never
    /// scanned". Those are opposite conclusions and a KPI tile showing zero cannot tell them apart.
    /// </remarks>
    public async Task<GovernanceDashboardDto> GetDashboardAsync(
        string? steward, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var assets = await LoadAssetsAsync(settings, ct);
        var reviews = await Reviews.AsNoTracking().ToDictionaryAsync(r => r.AssetKey, ct);
        var badges = (await Badges.AsNoTracking().ToListAsync(ct))
            .GroupBy(b => b.AssetKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Badge).ToList(), StringComparer.OrdinalIgnoreCase);

        var findings = await Findings.AsNoTracking()
            .Include(f => f.Decisions)
            .ToListAsync(ct);
        var findingsByAsset = findings
            .GroupBy(f => f.AssetKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<GovernanceAssetDto>();
        foreach (var asset in assets)
        {
            var key = StewardshipProjection.AssetKey(asset);
            var version = StewardshipProjection.AssetVersion(asset);
            reviews.TryGetValue(key, out var review);
            var reviewedCurrent = review is not null && review.ReviewedVersion == version;
            var result = Score(asset, settings, reviewedCurrent);

            rows.Add(new GovernanceAssetDto(
                key,
                version,
                asset.ScriptPath,
                asset.Owner,
                asset.Steward,
                asset.Domain,
                asset.Classification,
                result.Score,
                result.Score >= settings.TargetScore,
                result.Deductions,
                result.AutomaticBadges,
                badges.TryGetValue(key, out var assigned) ? assigned : [],
                review?.ReviewedAtUtc,
                review?.ReviewedVersion,
                findingsByAsset.TryGetValue(key, out var assetFindings)
                    ? [.. assetFindings.Select(ToFindingDto)]
                    : []));
        }

        if (!string.IsNullOrWhiteSpace(steward))
            rows = [.. rows.Where(r => string.Equals(r.Steward, steward, StringComparison.OrdinalIgnoreCase))];

        var lastScan = await Scans.AsNoTracking()
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        return new GovernanceDashboardDto(
            new GovernanceSummaryDto(
                rows.Count,
                rows.Count(r => r.Governed),
                rows.Count(r => !r.Governed),
                findings.Count(f => f.Status is StewardshipFinding.OpenStatus or StewardshipFinding.ReopenedStatus),
                findings.Count(f => f.Status == StewardshipFinding.IgnoredStatus),
                findings.Count(f => f.Status == StewardshipFinding.AcceptedRiskStatus),
                settings.TargetScore),
            rows,
            lastScan is null ? null : ToScanDto(lastScan));
    }

    public static StewardshipFindingDto ToFindingDto(StewardshipFinding f) => new(
        f.Id, f.AssetKey, f.RuleKey, f.AssetVersion, f.Detail, f.Status,
        f.FirstSeenUtc, f.LastSeenUtc, f.ResolvedAtUtc, f.SuppressedUntilUtc,
        [.. f.Decisions
            .OrderByDescending(d => d.DecidedAtUtc)
            .Select(d => new GovernanceDecisionDto(
                d.Id, d.Decision, d.CategoryValue, d.Reason, d.AssetVersion,
                d.DecidedAtUtc, d.DecidedByUserName))]);

    public static StewardshipScanDto ToScanDto(StewardshipScan s) => new(
        s.Id, s.Trigger, s.StartedAtUtc, s.CompletedAtUtc, s.Status, s.Error,
        s.AssetsScanned, s.FindingsOpened, s.FindingsResolved, s.FindingsReopened);
}
