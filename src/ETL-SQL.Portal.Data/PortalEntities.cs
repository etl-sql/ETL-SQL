using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using Microsoft.AspNetCore.Identity;

namespace ETL_SQL.Portal.Data;

public interface IVersionedEntity
{
    long Version { get; set; }
}

// ── Identity ──────────────────────────────────────────────────────────────────

public class PortalUser : IdentityUser<int>, IVersionedEntity
{
    public string TenantId { get; set; } = "portal-host";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MiddleInitial { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = false;
    public string Provider { get; set; } = "Local";

    /// <summary>Immutable subject identifier from the external identity provider (OIDC <c>sub</c>),
    /// set only on federated accounts. Federated logins are keyed on this rather than the mutable
    /// username, so an account cannot be hijacked by a reused or attacker-chosen <c>preferred_username</c>.</summary>
    public string? ExternalSubject { get; set; }
    /// <summary>Normalized HTTPS issuer that assigned <see cref="ExternalSubject"/>.</summary>
    public string? ExternalIssuer { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;

    public ICollection<UserGroup> UserGroups { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<ReportFavorite> ReportFavorites { get; set; } = [];
    public ICollection<SavedReportView> SavedViews { get; set; } = [];
    public ICollection<ReportAlert> ReportAlerts { get; set; } = [];
}

public class PortalRole : IdentityRole<int>
{
    public PortalRole() { }
    public PortalRole(string roleName) : base(roleName) { }
}

/// <summary>
/// Server-owned routing from a shared Portal host/login domain to one tenant's OIDC authority.
/// Anonymous discovery resolves by the exact normalized Portal host; neither tenant nor issuer is
/// accepted from the request. Client credentials remain SECRET: references.
/// </summary>
public class SharedIdentityAuthority : IVersionedEntity
{
    public int Id { get; set; }
    public string AuthorityId { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = "";
    public string PortalHost { get; set; } = "";
    public string LoginDomain { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string? ClientSecretReference { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;
}

// ── Groups ────────────────────────────────────────────────────────────────────

public class Group : IVersionedEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Provider { get; set; } = "Local";
    public string? AdGroup { get; set; }
    public long Version { get; set; } = 1;

    public ICollection<UserGroup> UserGroups { get; set; } = [];
    public ICollection<FolderAcl> FolderAcls { get; set; } = [];
    public ICollection<DatasetAcl> DatasetAcls { get; set; } = [];
    public ICollection<GroupStudioCapability> StudioCapabilities { get; set; } = [];
}

/// <summary>
/// A Studio capability granted to a group.
///
/// Capabilities were previously resolvable only from <c>Portal:Studio:RoleCapabilities</c>
/// configuration, which means changing who may publish or push required a config change and a
/// restart, and could not be expressed for anything narrower than a whole role. Granting them to a
/// group puts Studio authority on the same footing as every other grant: assignable, auditable, and
/// revocable while the deployment is running.
///
/// The grant is resolved at sign-in and carried as a <c>studio_capability</c> claim, so the
/// per-request check stays a claim lookup rather than a database read. Changing a group's
/// capabilities therefore invalidates its members' sessions, the same way changing an ACL does.
/// </summary>
public class GroupStudioCapability
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public string Capability { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UserGroup
{
    public string TenantId { get; set; } = "portal-host";
    public int UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
}

// ── Folders ───────────────────────────────────────────────────────────────────

public class Folder : IVersionedEntity
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public Folder? Parent { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public int OwnerId { get; set; }
    public long Version { get; set; } = 1;

    public ICollection<Folder> Children { get; set; } = [];
    public ICollection<FolderAcl> Acls { get; set; } = [];
    public ICollection<Report> Reports { get; set; } = [];
}

/// <summary>
/// A grant on a folder or a report.
///
/// <para><b>The numeric values are storage, not authority order.</b> These are persisted as integers
/// in every ACL row, so <c>Author</c> had to be appended rather than inserted in its rightful place
/// between <c>Execute</c> and <c>Manage</c> — inserting it would have renumbered <c>Manage</c> and
/// silently reinterpreted every grant already in the database.</para>
///
/// <para>That makes the declaration order a lie about authority, and comparing these values with
/// <c>&gt;=</c> gives <c>Author</c> everything <c>Manage</c> has. Use
/// <see cref="FolderPermissions.AtLeast(FolderPermission, FolderPermission)"/>, which ranks them
/// correctly. The ordinal comparison is the trap; the extension method is the way through it.</para>
/// </summary>
public enum FolderPermission
{
    Read = 0,
    Execute = 1,
    Manage = 2,

    /// <summary>
    /// May change a report's content and metadata, and run it. May <b>not</b> move or delete it,
    /// alter any ACL, or administer the folder — authoring is not administration.
    /// </summary>
    Author = 3,
}

/// <summary>
/// Authority comparison for <see cref="FolderPermission"/>.
/// </summary>
public static class FolderPermissions
{
    /// <summary>
    /// Where a grant sits in the authority ladder, independent of the integer it is stored as.
    /// Read &lt; Execute &lt; Author &lt; Manage.
    /// </summary>
    public static int Rank(this FolderPermission permission) => permission switch
    {
        FolderPermission.Read => 0,
        FolderPermission.Execute => 1,
        FolderPermission.Author => 2,
        FolderPermission.Manage => 3,
        _ => 0,
    };

    /// <summary>True when <paramref name="held"/> confers at least <paramref name="required"/>.</summary>
    public static bool AtLeast(this FolderPermission held, FolderPermission required) =>
        held.Rank() >= required.Rank();

    /// <summary>Null is no grant, which is never enough.</summary>
    public static bool AtLeast(this FolderPermission? held, FolderPermission required) =>
        held is { } value && value.AtLeast(required);

    /// <summary>The stronger of two grants, ranked rather than compared ordinally.</summary>
    public static FolderPermission Max(FolderPermission left, FolderPermission right) =>
        left.Rank() >= right.Rank() ? left : right;
}

public class FolderAcl
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public Folder Folder { get; set; } = null!;
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public FolderPermission Permission { get; set; }
}

// ── Reports ───────────────────────────────────────────────────────────────────

public class Report : IVersionedEntity
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public Folder Folder { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Owner { get; set; }
    public string? Contact { get; set; }
    public string? Tags { get; set; }
    public string? Category { get; set; }
    public string? Domain { get; set; }
    public string? Steward { get; set; }
    public string? Certification { get; set; }
    public string? MetadataJson { get; set; }
    public string ScriptPath { get; set; } = "";
    public DateTime ScriptLastModified { get; set; }
    public string? PublishedScriptHash { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastViewedAt { get; set; }
    public DateTime? LastRefreshStartedAt { get; set; }
    public DateTime? LastRefreshCompletedAt { get; set; }
    public string? LastRefreshStatus { get; set; }
    public string? LastRefreshError { get; set; }
    public long? LastRefreshDurationMs { get; set; }
    public bool IsDeleted { get; set; } = false;
    public long Version { get; set; } = 1;

    public ICollection<ReportSnapshot> Snapshots { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
    public ICollection<ReportJobLink> ReportJobLinks { get; set; } = [];
    public ICollection<ReportFavorite> Favorites { get; set; } = [];
    public ICollection<ReportShareLink> ShareLinks { get; set; } = [];
    public ICollection<ReportEmbedToken> EmbedTokens { get; set; } = [];
    public ICollection<SavedReportView> SavedViews { get; set; } = [];
    public ICollection<ReportAlert> Alerts { get; set; } = [];
    public ICollection<ReportAcl> Acls { get; set; } = [];
}

public class ReportFavorite
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReportAccessRequest
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public int RequesterUserId { get; set; }
    public PortalUser Requester { get; set; } = null!;
    public string Status { get; set; } = "Pending";
    public string? Reason { get; set; }
    public string? DecisionReason { get; set; }
    public int? DecidedByUserId { get; set; }
    public PortalUser? DecidedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}

public class ReportShareLink
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public int CreatedBy { get; set; }
    public PortalUser Creator { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class ReportEmbedToken
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public int CreatedBy { get; set; }
    public PortalUser Creator { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class SavedReportView
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public int UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? ParametersJson { get; set; }
    public string? FiltersJson { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ReportAlert
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public int OwnerId { get; set; }
    public PortalUser Owner { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? OptionsJson { get; set; }
    public string VisualName { get; set; } = "";
    public string Operator { get; set; } = ">=";
    public decimal Threshold { get; set; }
    // Legacy inline delivery fields. New scripts use AlertNotifications instead.
    public string? Recipient { get; set; }
    public string? SmtpAlias { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? LastState { get; set; }
    public DateTime? LastEvaluatedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime? LastNotifiedAt { get; set; }

    public ICollection<AlertNotification> Notifications { get; set; } = [];
}

public class AlertNotification
{
    public int Id { get; set; }
    public int AlertId { get; set; }
    public ReportAlert Alert { get; set; } = null!;
    public string OrchestratorAlias { get; set; } = "";
    public string NotificationName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReportSnapshot
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public string ManifestPath { get; set; } = "";
    public DateTime BuiltAt { get; set; } = DateTime.UtcNow;
    public int BuiltBy { get; set; }
    public string? ParametersJson { get; set; }
    public string? ScriptHashAtRunTime { get; set; }
    public bool? HashMatched { get; set; }
}

// ── Portal execution jobs ─────────────────────────────────────────────────────

public class PortalExecutionJob
{
    public string Id { get; set; } = "";
    public int ReportId { get; set; }
    public int UserId { get; set; }
    public string ActorType { get; set; } = "User";
    public string? ActorId { get; set; }
    public string? EffectiveScopes { get; set; }
    public string? CorrelationId { get; set; }
    public string Kind { get; set; } = "Execution";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ManifestPath { get; set; }
    public string? Error { get; set; }
    public long RowsProcessed { get; set; }
    public long PeakMemoryBytes { get; set; }
    public double CpuTimeSeconds { get; set; }
}

public class ServiceAccount : IVersionedEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = "portal-host";
    public string ClientId { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string? Description { get; set; }
    public int OwnerUserId { get; set; }
    public PortalUser OwnerUser { get; set; } = null!;
    public string SecretHash { get; set; } = "";
    public string Scopes { get; set; } = "";
    public string RoleNames { get; set; } = "";
    /// <summary>
    /// Space-separated Studio capabilities, following <see cref="RoleNames"/> and
    /// <see cref="Scopes"/>. Capped by the owner's own capabilities at token issue time, so a
    /// service account can never outlive or exceed the authority of the person who created it.
    /// </summary>
    public string StudioCapabilities { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public long Version { get; set; } = 1;
}

// ── Subscriptions ─────────────────────────────────────────────────────────────

public enum SubscriptionFormat { PDF, CSV, Markdown, Link }

public class Subscription : IVersionedEntity
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public int UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public string? Schedule { get; set; }
    /// <summary>Optional wall-clock delivery time (hh:mm). Persisted so startup reconciliation
    /// can recreate a lost Orchestrator job without losing the configured delivery time.</summary>
    public string? AtTime { get; set; }
    public bool DeliverOnRefresh { get; set; } = false;
    public SubscriptionFormat Format { get; set; } = SubscriptionFormat.PDF;
    public string SmtpAlias { get; set; } = "";
    public string Recipients { get; set; } = "";
    public string? ScriptPath { get; set; }
    public string? Name { get; set; }
    public string? ParametersJson { get; set; }
    public DateTime? LastSentAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    /// <summary>EndTime of the last Orchestrator trigger completion handled by the portal's
    /// delivery executor — durable dedupe so a re-observed completion is not delivered twice.</summary>
    public DateTime? LastTriggeredAt { get; set; }
    public int FailCount { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}

// ── SMTP Connections ──────────────────────────────────────────────────────────

// SmtpConnection was removed: SMTP is a normal connector stored in PortalSharedConnection, which
// holds SECRET: references rather than an encrypted credential value and carries ACLs, audit and a
// usage ledger. The table is dropped by the DropSmtpConnections migration.

// ── Subscription delivery ledger ────────────────────────────────────────────────

/// <summary>
/// Durable record of one recipient delivery attempt. The unique
/// <c>(SubscriptionId, TriggerKey, RecipientKey)</c> index gives
/// <b>at-most-once-per-recipient-trigger</b> delivery. Every attempt and terminal outcome is
/// observable here, and <see cref="DeliveryId"/> equals the audit correlation id so delivery rows
/// and audit events join.
/// </summary>
public class SubscriptionDelivery
{
    public int Id { get; set; }

    /// <summary>The <c>delivery-&lt;guid&gt;</c> id, shared with this attempt's audit correlation id.</summary>
    public string DeliveryId { get; set; } = "";

    public int SubscriptionId { get; set; }

    /// <summary>Identity of the triggering scheduler completion (its <c>EndTime</c>, round-trip
    /// "o" format), or <c>manual:&lt;guid&gt;</c> for an ad-hoc delivery. Unique per subscription.</summary>
    public string TriggerKey { get; set; } = "";

    /// <summary>Stable SHA-256 key of the normalized recipient. Used for per-recipient trigger
    /// deduplication without putting an email address in indexes or operational messages.</summary>
    public string RecipientKey { get; set; } = "";

    /// <summary>InProgress | Delivered | Failed | Denied | Skipped.</summary>
    public string Outcome { get; set; } = "InProgress";

    public string? Detail { get; set; }
    public string Recipients { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

// ── Portal-managed encrypted secret store ─────────────────────────────────────

/// <summary>
/// A named secret stored encrypted at rest with the portal's cluster-wide Data Protection keys.
/// Administrators write values through the admin API; script execution resolves them as
/// SECRET:name. The plaintext is never returned by any API after write.
/// </summary>
public class PortalSecret : IVersionedEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public string Name { get; set; } = "";
    public string EncryptedValue { get; set; } = "";
    public bool Disabled { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public int? UpdatedByUserId { get; set; }
    public long Version { get; set; } = 1;
}

/// <summary>
/// A governed shared connection (SHARED:alias) in the Portal catalog. Credential fields hold
/// SECRET: references, never values (enforced on write); Target and OptionsJson are additionally
/// encrypted at rest via the PII converter because they can carry sensitive endpoints.
/// </summary>
public class PortalSharedConnection : IVersionedEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public string Alias { get; set; } = "";
    public string ConnectorType { get; set; } = "";
    public string? Target { get; set; }
    public string OptionsJson { get; set; } = "{}";
    public bool Disabled { get; set; }
    public string? EnvironmentScope { get; set; }
    public int? OwnerUserId { get; set; }
    /// <summary>Comma-separated fields this entry classifies as sensitive (masked + SECRET:-resolvable).</summary>
    public string? SensitiveFieldsCsv { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public int? UpdatedByUserId { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }
    public long Version { get; set; } = 1;

    public ICollection<SharedConnectionAcl> Acls { get; set; } = [];
}

public enum SharedConnectionPermission { Use = 0 }

/// <summary>
/// A per-connection use grant (group-scoped, like <see cref="DatasetAcl"/>). An entry with no
/// grants is usable by any caller; an entry with grants requires an admin, its owner, or a
/// member of a granted group — and a caller without an injected identity is denied.
/// </summary>
public class SharedConnectionAcl
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public int SharedConnectionId { get; set; }
    public PortalSharedConnection SharedConnection { get; set; } = null!;
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public SharedConnectionPermission Permission { get; set; } = SharedConnectionPermission.Use;
}

/// <summary>
/// Per-consumer usage of a shared connection: who resolved SHARED:alias and when, so
/// administrators see impact per consumer before disabling or deleting an entry (rather than a
/// single last-used timestamp). Written best-effort at resolution time.
/// </summary>
public class SharedConnectionUsage
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public int SharedConnectionId { get; set; }
    public PortalSharedConnection SharedConnection { get; set; } = null!;
    /// <summary>Effective user of the resolving execution, or "(none)" for identity-less runs.</summary>
    public string ConsumerUser { get; set; } = "";
    public DateTime LastUsedAtUtc { get; set; }
    public long UseCount { get; set; }
}

/// <summary>
/// One run of a native admin background service (failure digest, backup report, capacity report):
/// the durable per-run ledger behind retention, the admin status API, and operational review.
/// </summary>
public class AdminServiceRun
{
    public int Id { get; set; }
    public string ServiceName { get; set; } = "";
    /// <summary>Sent | Skipped | Failed.</summary>
    public string Outcome { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? NodeName { get; set; }
    public int Attempts { get; set; }
}

// ── Audit Log ─────────────────────────────────────────────────────────────────

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string ActorType { get; set; } = "User";
    public string? ActorId { get; set; }
    public string? EffectiveScopes { get; set; }
    public string Action { get; set; } = "";
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Detail { get; set; }

    /// <summary>Request trace identifier (HTTP) or operation id (background work) so every
    /// audit row can be tied back to the operation that produced it.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// The Studio capability that authorized this mutation, when one did. Roles and capabilities are
    /// separate authorities — a Publisher may hold <c>ReportPublish</c> but not <c>SourcePush</c> —
    /// so reviewing a Studio mutation means knowing which capability let it through, not just who
    /// the actor was. Null for anything not gated on a Studio capability.
    /// </summary>
    public string? StudioCapability { get; set; }

    public ICollection<AuditOutboxMessage> OutboxMessages { get; set; } = [];
}

public class AuditOutboxMessage
{
    public long Id { get; set; }
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public int? AuditLogId { get; set; }
    public AuditLog? AuditLog { get; set; }
    public int? UserId { get; set; }
    public string ActorType { get; set; } = "User";
    public string? ActorId { get; set; }
    public string? EffectiveScopes { get; set; }
    public string Action { get; set; } = "";
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? CorrelationId { get; set; }
    /// <summary>Mirrors <see cref="AuditLog.StudioCapability"/> so a remote collector sees it too.</summary>
    public string? StudioCapability { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── Enterprise Policy Authority ───────────────────────────────────────────────

/// <summary>
/// An immutable published organization-policy version served by the policy authority. Rows are
/// append-only; supersession flips <see cref="RolloutState"/> but never deletes or rewrites payload.
/// </summary>
public class PolicyVersionEntity
{
    public long Id { get; set; }
    public string Tenant { get; set; } = "";
    public string Environment { get; set; } = "";
    public string PolicyVersion { get; set; } = "";
    public string PolicyHash { get; set; } = "";
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Author { get; set; } = "";
    public string? Reviewer { get; set; }
    public string? SupersededVersion { get; set; }
    public string RolloutState { get; set; } = "Active";
    public string SignedEnvelopeJson { get; set; } = "{}";
    public DateTimeOffset PublishedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set only for a Canary version: the named machine group it targets. Mutually exclusive
    /// with <see cref="CanaryPercentage"/>.</summary>
    public string? CanaryGroup { get; set; }
    /// <summary>Set only for a Canary version: the percentage (1–100) of the fleet it targets by stable
    /// machine-identity hash. Mutually exclusive with <see cref="CanaryGroup"/>.</summary>
    public int? CanaryPercentage { get; set; }
}

/// <summary>
/// An enrolled machine known to the policy authority. Retrieval responses are bound to the
/// tenant/environment recorded here — never to what the caller claims — and requests are refused
/// when the identity is unknown, revoked, or presents enrollment/tenant details that no longer
/// match this registration (a reassigned or copied identity).
/// </summary>
public class PolicyMachineEntity
{
    public long Id { get; set; }
    public string MachineId { get; set; } = "";
    public string EnrollmentId { get; set; } = "";
    public string Tenant { get; set; } = "";
    public string Environment { get; set; } = "";
    /// <summary>SHA-1 (40 hex) or SHA-256 (64 hex) thumbprint. When set, the machine must present
    /// a TLS client certificate with this thumbprint.</summary>
    public string? ClientCertificateThumbprint { get; set; }
    public bool Revoked { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    /// <summary>Optional canary group label. A canary version targeting this group name is served to
    /// this machine while the rest of the fleet stays on the active version.</summary>
    public string? CanaryGroup { get; set; }
}

// ── Report Refresh Job Links ──────────────────────────────────────────────────

public class ReportJobLink
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public string OrchestratorAlias { get; set; } = "";
    public string JobName { get; set; } = "";
    public DateTime? LastRefreshedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── Refresh Tokens ────────────────────────────────────────────────────────────

public class RefreshToken
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public int UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

// ── Datasets ──────────────────────────────────────────────────────────────────

public class Dataset : IVersionedEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string FolderPath { get; set; } = "";   // display metadata (logical or filesystem path)
    public int? FolderId { get; set; }         // portal folder for PUBLIC access checks; resolved from OwningReport.FolderId
    public int? CreatedBy { get; set; }         // publisher/owner when there is no OwningReport (e.g. PUBLISH DATASET)
    public string ParquetFilePath { get; set; } = "";
    public string? AtRestKeyVersion { get; set; }
    public int? OwningReportId { get; set; }
    public Report? OwningReport { get; set; }
    public string SourceQuery { get; set; } = "";
    public DatasetAccessLevel AccessLevel { get; set; } = DatasetAccessLevel.Private;
    public DatasetEncryptionMode EncryptionMode { get; set; } = DatasetEncryptionMode.MachineBound;
    public DateTime? LastRefresh { get; set; }
    public string? Ttl { get; set; }
    public string? RefreshInterval { get; set; }
    public long RowCount { get; set; }
    public string? ColumnSchema { get; set; } // JSON
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;

    public ICollection<DatasetAcl> Acls { get; set; } = [];
    public ICollection<DatasetUserAcl> UserAcls { get; set; } = [];
}

/// <summary>
/// A dataset grant made directly to one user, rather than to a group.
///
/// This is a sibling table instead of a nullable <c>UserId</c> on <see cref="DatasetAcl"/> (the
/// shape <see cref="ReportAcl"/> uses) for one reason: relaxing <c>DatasetAcl.GroupId</c> to
/// nullable is an <c>AlterColumn</c>, which the rolling-expand migration contract rejects, and which
/// SQLite implements as a full table rebuild. Adding a table is additive and safe to deploy under a
/// rolling upgrade.
///
/// It exists so dataset authorship does not have to act as standing permission. A dataset's creator
/// is granted <see cref="DatasetPermission.Owner"/> here at creation time, which means removing
/// their access is a matter of deleting a row — where a <c>CreatedBy == userId</c> short-circuit
/// left every dataset a departing user had ever created permanently reachable by them.
/// </summary>
public class DatasetUserAcl
{
    public int Id { get; set; }
    public int DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public int UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public DatasetPermission Permission { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DatasetAcl
{
    public int Id { get; set; }
    public int DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public DatasetPermission Permission { get; set; }
}

public class ReportAcl
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;
    public int? UserId { get; set; }
    public PortalUser? User { get; set; }
    public int? GroupId { get; set; }
    public Group? Group { get; set; }
    public FolderPermission Permission { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
