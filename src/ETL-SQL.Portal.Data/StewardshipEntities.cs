namespace ETL_SQL.Portal.Data;

/// <summary>
/// Governance workflow state owned by the Portal.
///
/// <para>These tables hold <b>decisions and derived state</b> — what a steward concluded about an
/// asset — never the asset metadata itself. Ownership, stewardship, classification, and lineage stay
/// in the <c>.etlsql</c>/<c>.rptsql</c> sources and the lineage catalog, which are the durable
/// record. A governance dashboard that became the source of truth for metadata would be a second
/// place to change it, out of source control and out of review.</para>
///
/// <para><b>Every decision is version-scoped.</b> Accepting a risk or ignoring a finding records the
/// asset version it was made against, so a later version does not silently inherit it. A decision
/// that outlived the thing it was made about is indistinguishable from no governance at all.</para>
/// </summary>
internal static class StewardshipEntityNotes;

/// <summary>
/// Scoring and enablement configuration. A single logical row: thresholds and enabled checks are
/// deployment-wide, and a per-user copy would mean two stewards disagreeing about whether an asset
/// is governed.
/// </summary>
public class StewardshipSettings : IVersionedEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";

    /// <summary>Constant discriminator; the unique index on it is what keeps this table single-row.</summary>
    public string Scope { get; set; } = DefaultScope;

    public const string DefaultScope = "default";

    /// <summary>Score at or above which an asset counts as governed. Below it creates a finding.</summary>
    public int TargetScore { get; set; } = 80;

    public bool EnableMetadataCheck { get; set; } = true;
    public bool EnableProtectedDataCheck { get; set; } = true;
    public bool EnableGlossaryCheck { get; set; }
    public bool EnableStalenessCheck { get; set; } = true;

    public int DeductMetadata { get; set; } = 5;
    public int DeductProtectedData { get; set; } = 10;
    public int DeductGlossary { get; set; } = 5;
    public int DeductStaleness { get; set; } = 15;

    /// <summary>Days after which an unreviewed asset is stale.</summary>
    public int StaleAfterDays { get; set; } = 30;

    /// <summary>
    /// Enforcement level, per the rollout ladder: <c>visible</c>, <c>suggestion</c>, <c>scored</c>,
    /// <c>certification-gate</c>. Checks are visible before they are enforceable, so turning one on
    /// never retroactively fails an estate nobody has looked at yet.
    /// </summary>
    public string PolicyLevel { get; set; } = "scored";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }
    public long Version { get; set; } = 1;
}

/// <summary>
/// A reason a steward may cite when suppressing a finding — the organization's own vocabulary for
/// why something is not going to be fixed. Durable because an exception whose category no longer
/// exists cannot be reviewed.
/// </summary>
public class StewardshipResolutionCategory : IVersionedEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    /// <summary>Stable key stored on decisions; the label may be reworded, this may not.</summary>
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Severity styling key: <c>risk</c>, <c>false-positive</c>, or <c>noise</c>.</summary>
    public string Color { get; set; } = "noise";
    /// <summary>
    /// Days after which a decision in this category expires and its finding reopens; null means it
    /// never expires. An accepted risk with no review date is a permanent exemption by default.
    /// </summary>
    public int? ExpiryDays { get; set; }
    public bool Disabled { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public long Version { get; set; } = 1;
}

/// <summary>
/// An organization-owned business term. Manual and opt-in: glossary checks do not affect scores or
/// create findings until <see cref="StewardshipSettings.EnableGlossaryCheck"/> is on, so importing a
/// starter glossary cannot silently fail an estate.
/// </summary>
public class StewardshipGlossaryTerm : IVersionedEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public string Term { get; set; } = "";
    public string DataType { get; set; } = "";
    /// <summary>Comma-separated alternate names matched against lineage columns.</summary>
    public string Aliases { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Approved calculation, when the term has one. Empty means "stored attribute".</summary>
    public string? Formula { get; set; }
    public string? Steward { get; set; }
    public bool Disabled { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public int? UpdatedByUserId { get; set; }
    public long Version { get; set; } = 1;
}

/// <summary>
/// A steward-assigned badge — <c>Reviewed</c>, <c>Trusted</c>, <c>Certified</c>. Distinct from
/// automatic badges, which are computed from current evidence and never stored: storing a computed
/// badge would let it outlive the evidence that justified it.
/// </summary>
public class StewardshipAssetBadge : IVersionedEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    /// <summary>Normalized asset path, e.g. <c>sales.dbo.orders.customer_id</c>.</summary>
    public string AssetKey { get; set; } = "";
    public string Badge { get; set; } = "";
    /// <summary>Asset version this was granted against, when known. See the version-scoping note.</summary>
    public string? AssetVersion { get; set; }
    public string? Reason { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public int? AssignedByUserId { get; set; }
    public long Version { get; set; } = 1;
}

/// <summary>
/// The latest steward review of an asset, keyed by the version reviewed. "Changed since review" is
/// derived by comparing this to the asset's current version — which only works because the version
/// is recorded here rather than a bare timestamp.
/// </summary>
public class StewardshipAssetReview : IVersionedEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public string AssetKey { get; set; } = "";
    public string? ReviewedVersion { get; set; }
    public string? Note { get; set; }
    public DateTime ReviewedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ReviewedByUserId { get; set; }
    public long Version { get; set; } = 1;
}

/// <summary>
/// A rule an asset currently fails, or failed and has since been dispositioned.
/// </summary>
/// <remarks>
/// Findings are <b>derived</b>: a scan recomputes them from lineage and settings. They are persisted
/// anyway because a decision needs something durable to attach to, and because "this was open, then
/// resolved" is history a recomputation cannot reconstruct.
/// </remarks>
public class StewardshipFinding : IVersionedEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public string AssetKey { get; set; } = "";
    /// <summary>Stable rule identifier, e.g. <c>missing-metadata</c>, <c>untagged-protected-data</c>.</summary>
    public string RuleKey { get; set; } = "";
    /// <summary>Asset version this finding was raised against.</summary>
    public string? AssetVersion { get; set; }
    public string? Detail { get; set; }

    /// <summary>
    /// <c>open</c>, <c>resolved</c>, <c>ignored</c>, <c>accepted-risk</c>, or <c>reopened</c>.
    /// </summary>
    public string Status { get; set; } = OpenStatus;

    public const string OpenStatus = "open";
    public const string ResolvedStatus = "resolved";
    public const string IgnoredStatus = "ignored";
    public const string AcceptedRiskStatus = "accepted-risk";
    public const string ReopenedStatus = "reopened";

    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    /// <summary>When a suppression stops applying, from its category's expiry.</summary>
    public DateTime? SuppressedUntilUtc { get; set; }
    public long Version { get; set; } = 1;

    public ICollection<StewardshipFindingDecision> Decisions { get; set; } = [];
}

/// <summary>
/// A steward's disposition of a finding, kept as an append-only trail. Superseding a decision adds a
/// row rather than editing one: the question a reviewer asks is "who accepted this, when, and on
/// what grounds", and an overwritten record cannot answer it.
/// </summary>
public class StewardshipFindingDecision
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public int FindingId { get; set; }
    public StewardshipFinding? Finding { get; set; }

    /// <summary><c>ignore</c>, <c>accept-risk</c>, <c>reopen</c>, or <c>review</c>.</summary>
    public string Decision { get; set; } = "";

    /// <summary>References <see cref="StewardshipResolutionCategory.Value"/> for suppressions.</summary>
    public string? CategoryValue { get; set; }
    public string Reason { get; set; } = "";

    /// <summary>
    /// Asset version the decision was made against. A later version does not inherit it — that is
    /// what stops an accepted risk from quietly covering code nobody has looked at.
    /// </summary>
    public string? AssetVersion { get; set; }

    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
    public int? DecidedByUserId { get; set; }
    public string? DecidedByUserName { get; set; }
}

/// <summary>
/// One governance scan: what recomputed the findings, when, and what it produced. Without it the
/// dashboard cannot distinguish "no findings" from "never scanned", which are opposite conclusions.
/// </summary>
public class StewardshipScan
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    /// <summary><c>manual</c>, <c>publish</c>, or <c>scheduled</c>.</summary>
    public string Trigger { get; set; } = "manual";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int AssetsScanned { get; set; }
    public int FindingsOpened { get; set; }
    public int FindingsResolved { get; set; }
    public int FindingsReopened { get; set; }
    /// <summary><c>running</c>, <c>completed</c>, or <c>failed</c>.</summary>
    public string Status { get; set; } = "running";
    public string? Error { get; set; }
    public int? StartedByUserId { get; set; }
}
