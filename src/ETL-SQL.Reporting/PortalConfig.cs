using System;
using System.Collections.Generic;

namespace ETL_SQL.ReportPortal;

public class PortalConfig
{
    public string DatabasePath { get; set; } = "./portal.db";
    public PortalDatabaseConfig Database { get; set; } = new();
    public string ScriptRootPath { get; set; } = "./Reports";
    public string SnapshotDirectory { get; set; } = "./Snapshots";
    public string MapRootPath { get; set; } = "./data/maps";
    public string DatasetRootPath { get; set; } = "./data/datasets";
    public bool AllowServiceControl { get; set; } = false;
    public int MaxPreviewRows { get; set; } = 50000;
    public ResourcesConfig Resources { get; set; } = new();
    public JwtConfig Jwt { get; set; } = new();
    public IdentityConfig Identity { get; set; } = new();
    public FirstRunConfig FirstRun { get; set; } = new();
    public OrchestratorConfig Orchestrator { get; set; } = new();
    public DatasetConfig Dataset { get; set; } = new();
    public PortalSecurityConfig Security { get; set; } = new();
    public PortalRateLimitConfig RateLimit { get; set; } = new();
    public AuditConfig Audit { get; set; } = new();
    public PortalStorageConfig Storage { get; set; } = new();
    public PortalModuleConfig Modules { get; set; } = new();
    public PortalLoadBalancerConfig LoadBalancer { get; set; } = new();
    public PortalTopologyConfig Topology { get; set; } = new();
    public OperationalDigestConfig OperationalDigest { get; set; } = new();
    public AdminServicesConfig AdminServices { get; set; } = new();
}

/// <summary>
/// Feature flags for functional layers inside the Report Portal binary. Defaults preserve the
/// existing all-in-one Portal behavior; route and worker fences consume these values as they land.
/// </summary>
public class PortalModuleConfig
{
    public bool Reporting { get; set; } = true;
    public bool Designer { get; set; } = true;
    public bool ConnectionCatalog { get; set; } = true;
    public bool SecretStore { get; set; } = true;
    public bool Scheduling { get; set; } = true;
    public bool Operations { get; set; } = true;
    public bool Documentation { get; set; } = true;
}

/// <summary>
/// Native replacements for the samples/admin_operations scheduler scripts: managed background
/// services with per-service enablement, schedule, HA singleton lease, retry, run history, and
/// SMTP notification targets. All disabled by default — opt in per deployment.
/// </summary>
public class AdminServicesConfig
{
    public FailureDigestServiceConfig FailureDigest { get; set; } = new();
    public BackupReportServiceConfig BackupReport { get; set; } = new();
    public CapacityReportServiceConfig CapacityReport { get; set; } = new();

    /// <summary>Days of AdminServiceRun history retained; pruned by the services themselves.</summary>
    public int RunHistoryRetentionDays { get; set; } = 90;
}

/// <summary>Schedule and delivery controls shared by every native admin service.</summary>
public class AdminServiceScheduleConfig
{
    /// <summary>Enables the service. Default false; requires <see cref="Recipients"/> and <see cref="SmtpAlias"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hours between runs. Minimum effective value is 1; default 24.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>Semicolon/comma-separated administrator recipients.</summary>
    public string Recipients { get; set; } = string.Empty;

    /// <summary>Alias of a configured SMTP connection used to send the report.</summary>
    public string SmtpAlias { get; set; } = string.Empty;

    /// <summary>From address; when empty the SMTP connection's own from/username is used.</summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>Delivery attempts per run before the run is recorded as Failed. Default 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay between delivery attempts within one run. Default 300 seconds.</summary>
    public int RetryDelaySeconds { get; set; } = 300;
}

/// <summary>Native replacement for samples/admin_operations/daily_failure_digest.etlsql.</summary>
public class FailureDigestServiceConfig : AdminServiceScheduleConfig
{
    /// <summary>Hours of history examined; keep slightly above the interval so windows overlap. Default 25.</summary>
    public int LookbackHours { get; set; } = 25;

    /// <summary>When true (default), send only when failures were found.</summary>
    public bool AlertOnly { get; set; } = true;
}

/// <summary>Native replacement for samples/admin_operations/backup_and_report.etlsql.</summary>
public class BackupReportServiceConfig : AdminServiceScheduleConfig
{
    /// <summary>Alert when the last recorded backup (job-state 'admin-backup') is older than this. Default 26 hours.</summary>
    public int MaxBackupAgeHours { get; set; } = 26;

    /// <summary>When true (default), send only when the last backup failed, is missing, or is stale.</summary>
    public bool AlertOnly { get; set; } = true;
}

/// <summary>Native replacement for samples/admin_operations/capacity_report.etlsql. Always sends a summary.</summary>
public class CapacityReportServiceConfig : AdminServiceScheduleConfig
{
    /// <summary>Hours of host metric samples aggregated per report. Default 24.</summary>
    public int LookbackHours { get; set; } = 24;
}

/// <summary>
/// A scheduled email digest of the portal's operational metrics (active/queued executions, 24h failure
/// rates, storage usage, migration status) for administrators. Disabled by default — opt in per
/// deployment. In a multi-node HA cluster a leader lock ensures exactly one node sends each interval.
/// </summary>
public class OperationalDigestConfig
{
    /// <summary>Enables the digest. Default false; requires <see cref="Recipients"/> and <see cref="SmtpAlias"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hours between digests. Minimum effective value is 1; default 24.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>Semicolon/comma-separated administrator recipients.</summary>
    public string Recipients { get; set; } = string.Empty;

    /// <summary>Alias of a configured SMTP connection used to send the digest.</summary>
    public string SmtpAlias { get; set; } = string.Empty;

    /// <summary>From address; when empty the SMTP connection's own from/username is used.</summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>
    /// When true, send only when at least one alert condition is met (failure rate, pending migrations,
    /// queue backlog/outbox/storage pressure) — a quieter "alert me when something is wrong" mode.
    /// Default false (always send).
    /// </summary>
    public bool AlertOnly { get; set; }

    /// <summary>Execution-failure rate (percent, over the 24h window) at or above which an alert is raised.</summary>
    public int FailureRatePercentThreshold { get; set; } = 25;

    /// <summary>Queued-execution count at or above which a backlog alert is raised.</summary>
    public int QueueDepthAlertThreshold { get; set; } = 20;

    /// <summary>Average queued-execution age in seconds at or above which an alert is raised. 0 disables.</summary>
    public int QueueAgeSecondsAlertThreshold { get; set; } = 300;

    /// <summary>Subscription delivery failure rate in percent at or above which an alert is raised. 0 disables.</summary>
    public int DeliveryFailureRatePercentThreshold { get; set; } = 25;

    /// <summary>Pending audit outbox rows at or above which an alert is raised. 0 disables.</summary>
    public int AuditOutboxPendingAlertThreshold { get; set; } = 1000;

    /// <summary>Oldest pending audit outbox age in seconds at or above which an alert is raised. 0 disables.</summary>
    public int AuditOutboxAgeSecondsAlertThreshold { get; set; } = 900;

    /// <summary>Pending security-event rows at or above which an alert is raised. 0 disables.</summary>
    public int SecurityEventPendingAlertThreshold { get; set; } = 1000;

    /// <summary>Oldest pending security-event age in seconds at or above which an alert is raised. 0 disables.</summary>
    public int SecurityEventAgeSecondsAlertThreshold { get; set; } = 900;

    /// <summary>Dataset storage bytes at or above which an alert is raised. 0 disables.</summary>
    public long DatasetStorageBytesAlertThreshold { get; set; }

    /// <summary>Snapshot storage bytes at or above which an alert is raised. 0 disables.</summary>
    public long SnapshotStorageBytesAlertThreshold { get; set; }

    /// <summary>Report snapshots older than this many hours are counted as stale. 0 disables.</summary>
    public int SnapshotFreshnessHours { get; set; }

    /// <summary>Datasets older than this many hours are counted as stale. 0 disables.</summary>
    public int DatasetFreshnessHours { get; set; }

    /// <summary>Active policy versions expiring within this many hours raise an alert. 0 disables.</summary>
    public int PolicyVersionExpiryWarningHours { get; set; } = 72;

    /// <summary>Client certificates expiring within this many hours raise an alert. 0 disables.</summary>
    public int CertificateExpiryWarningHours { get; set; } = 168;

    /// <summary>Raise an alert when the policy-authority signing surface is degraded or unavailable.</summary>
    public bool AlertOnPolicyAuthorityUnavailable { get; set; } = true;

    /// <summary>Raise an alert when the Portal database health check is unhealthy.</summary>
    public bool AlertOnDatabaseConnectivityFailure { get; set; } = true;

    /// <summary>Raise an alert when health diagnostics indicate database connection pool exhaustion.</summary>
    public bool AlertOnDatabasePoolExhaustion { get; set; } = true;

    /// <summary>Raise an alert when this node reports unhealthy fleet/readiness status.</summary>
    public bool AlertOnUnhealthyFleetNodes { get; set; } = true;

    /// <summary>Raise an alert when the catalog has unapplied EF migrations. Default true.</summary>
    public bool AlertOnPendingMigrations { get; set; } = true;

    /// <summary>Base path or URL used in emitted alert runbook links.</summary>
    public string RunbookBaseUri { get; set; } = "Docs/Operations/Alerting_Service_Objectives.md";
}

public class PortalLoadBalancerConfig
{
    /// <summary>
    /// Emits a stable per-process cookie that load balancers can use for sticky routing. Keep enabled for
    /// HA deployments because interactive report sessions are in-memory and intentionally node-local.
    /// </summary>
    public bool SessionAffinityEnabled { get; set; } = true;

    /// <summary>Name of the affinity cookie emitted by every Portal node.</summary>
    public string SessionAffinityCookieName { get; set; } = "ETLSQL_PORTAL_AFFINITY";

    /// <summary>Cookie lifetime in minutes. Minimum effective value is 1.</summary>
    public int SessionAffinityCookieMinutes { get; set; } = 480;
}

public class PortalTopologyConfig
{
    /// <summary>
    /// Expected deployment topology for readiness policy: Auto, Standalone, Departmental, or
    /// HighAvailability. Auto infers HA when PostgreSQL or a shared key ring is configured.
    /// </summary>
    public string ExpectedMode { get; set; } = "Auto";

    /// <summary>Minimum live Portal heartbeats required before an HA node is ready for traffic.</summary>
    public int MinLivePortalNodes { get; set; } = 1;

    /// <summary>Minimum live Orchestrator heartbeats required before an HA node is ready for traffic.</summary>
    public int MinLiveOrchestratorNodes { get; set; } = 0;

    /// <summary>Require PostgreSQL for Portal and Orchestrator state when the expected mode is HA.</summary>
    public bool RequirePostgresForHa { get; set; } = true;

    /// <summary>Require a configured shared Data Protection key-ring path when the expected mode is HA.</summary>
    public bool RequireSharedKeyRingForHa { get; set; } = true;
}

public class PortalStorageConfig
{
    /// <summary>
    /// Artifact-storage provider for scripts/snapshots/datasets/maps/keys: "Local" (default) or "Smb"
    /// (shared UNC share for multi-node Practical High Availability deployments). When "Smb", the area
    /// root paths (<see cref="PortalConfig.ScriptRootPath"/> etc.) must be UNC paths.
    /// </summary>
    public string Provider { get; set; } = "Local";

    /// <summary>
    /// Directory for the ASP.NET Data Protection key ring and the Keys artifact area. When unset, defaults
    /// to <c>.portal-keys</c> beside the portal database (node-local). For multi-node HA, point every node
    /// at the <b>same shared</b> location (e.g. a UNC path) so the key ring is shared — otherwise
    /// Data-Protection-encrypted secrets (SMTP/orchestrator credentials, auth cookies) written by one node
    /// cannot be read by another.
    /// </summary>
    public string? KeyRingPath { get; set; }
}

public class PortalDatabaseConfig
{
    /// <summary>EF Core provider for the portal state store: "Sqlite" (default) or "Postgres".
    /// Postgres is for shared multi-node (Practical High Availability) deployments.</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>Explicit connection string. When unset, SQLite derives one from
    /// <see cref="PortalConfig.DatabasePath"/>; Postgres requires this to be set.</summary>
    public string? ConnectionString { get; set; }
}

public class AuditConfig
{
    /// <summary>Days to retain audit rows; 0 (default) keeps them forever. Export rows you
    /// need to keep (CSV endpoint or external forwarding) before enabling retention.</summary>
    public int RetentionDays { get; set; }

    /// <summary>Seconds between retention sweeps. Minimum effective value is 1.</summary>
    public int PurgeIntervalSeconds { get; set; } = 86400;

    /// <summary>HTTPS collector endpoint for durable audit forwarding. Empty disables transport.</summary>
    public string? TransportEndpoint { get; set; }

    /// <summary>Bearer token sent to the collector. Keep unset when the collector uses mTLS or network ACLs.</summary>
    public string? TransportBearerToken { get; set; }

    /// <summary>Maximum outbox rows sent in one collector request.</summary>
    public int TransportBatchSize { get; set; } = 100;

    /// <summary>Seconds between outbox transport sweeps. Minimum effective value is 1.</summary>
    public int TransportIntervalSeconds { get; set; } = 30;

    /// <summary>HTTP request timeout in seconds. Minimum effective value is 1.</summary>
    public int TransportTimeoutSeconds { get; set; } = 10;

    /// <summary>Maximum delivery attempts before a row is marked Failed.</summary>
    public int TransportMaxAttempts { get; set; } = 8;

    /// <summary>Warn when pending rows exceed this limit. Fail-closed mutation policy is handled separately.</summary>
    public int OutboxBackpressureLimit { get; set; } = 10000;

    /// <summary>Seconds a node owns a claimed batch before another sweep may retry it.</summary>
    public int TransportLockSeconds { get; set; } = 120;

    // ── Fail-closed mutation policy (governance Audit:RemoteDeliveryRequired) ──────

    /// <summary>
    /// When true, security-sensitive mutations are blocked (HTTP 503) once durable remote audit
    /// delivery is judged unavailable: any delivery has terminally failed, the pending backlog
    /// exceeds <see cref="FailClosedMaxPendingBacklog"/>, the oldest pending event is older than
    /// <see cref="FailClosedMaxBacklogSeconds"/>, or the queued payload exceeds
    /// <see cref="OutboxMaxBytes"/>.
    ///
    /// <para><b>Unset (null) is the safe default</b> and resolves per <see cref="ResolveRequireRemoteDelivery"/>:
    /// on for an <b>enrolled</b> deployment that has configured a collector (<see cref="TransportEndpoint"/>),
    /// off otherwise. Standalone/unenrolled deployments therefore stay local-only, and a deployment
    /// with no collector configured is never blocked. An explicit <c>true</c>/<c>false</c> always wins.</para>
    /// </summary>
    public bool? RequireRemoteDelivery { get; set; }

    /// <summary>
    /// Resolves the effective fail-closed policy. An explicit configured value is honoured; when unset,
    /// fail-closed is on only for an enrolled deployment that has a collector configured — so it is a
    /// no-op for standalone deployments and for enrolled deployments without remote audit set up.
    /// </summary>
    public bool ResolveRequireRemoteDelivery(bool isEnrolled) =>
        RequireRemoteDelivery ?? (isEnrolled && !string.IsNullOrWhiteSpace(TransportEndpoint));

    /// <summary>Fail-closed once this many undelivered (Pending) outbox rows accumulate. 0 disables this check.</summary>
    public int FailClosedMaxPendingBacklog { get; set; } = 1000;

    /// <summary>Fail-closed once the oldest undelivered event is older than this many seconds. 0 disables this check.</summary>
    public int FailClosedMaxBacklogSeconds { get; set; } = 900;

    // ── Local outbox disk-size safeguards and retention (governance Audit:OutboxMaxBytes) ──

    /// <summary>
    /// Approximate maximum size (in bytes, measured as the sum of queued payload lengths) the local
    /// outbox may reach before safeguards apply. When <see cref="RequireRemoteDelivery"/> is on,
    /// exceeding this fails new mutations closed; otherwise the transport sweep sheds the oldest
    /// rows to stay under the cap. 0 (default) disables the size safeguard.
    /// </summary>
    public long OutboxMaxBytes { get; set; }

    /// <summary>
    /// Minutes a Delivered outbox row is retained (for collector-side dedup reconciliation) before
    /// the transport sweep purges it. Minimum effective value is 1.
    /// </summary>
    public int OutboxDeliveredRetentionMinutes { get; set; } = 1440;
}

public class PortalRateLimitConfig
{
    public int AuthPermitLimit { get; set; } = 20;
    public int AuthWindowSeconds { get; set; } = 60;
    public int AnonymousTokenPermitLimit { get; set; } = 60;
    public int AnonymousTokenWindowSeconds { get; set; } = 60;
}

public class PortalSecurityConfig
{
    /// <summary>
    /// Exact HTTP(S) origins allowed to frame portal pages. Same-origin framing is always allowed.
    /// Wildcards are intentionally unsupported so embedding remains an explicit deployment decision.
    /// </summary>
    public string[] FrameAncestors { get; set; } = [];

    /// <summary>
    /// Whether administrators bypass row-level security (HAS_GROUP/HAS_ROLE short-circuit to true).
    /// Default on. Turn off to filter admins by the same predicates as other users.
    /// See Docs/Design/RowLevelSecurity.md.
    /// </summary>
    public bool AdminBypassRowLevelSecurity { get; set; } = true;
}

public class DatasetConfig
{
    /// <summary>
    /// Portal-managed at-rest key (base64) used to encrypt cached dataset parquet <b>and report
    /// snapshot (<c>.etlsnap</c>) packages</b>. Portable: back it up with the portal config and move
    /// it with the portal — losing it makes every cached dataset and snapshot unreadable (they must be
    /// re-materialised). When unset, both datasets and snapshots fall back to host-bound
    /// ENCRYPT=MACHINE encryption (not portable across hosts) — see <see cref="AllowMachineFallback"/>.
    /// </summary>
    public string? AtRestKey { get; set; }

    /// <summary>Non-secret identifier stamped on datasets encrypted with <see cref="AtRestKey"/>.</summary>
    public string AtRestKeyVersion { get; set; } = "v1";

    /// <summary>
    /// Older version-to-key mappings retained only while datasets are being rotated. Remove an entry
    /// after no dataset references that version and backups made with it are no longer required.
    /// </summary>
    public Dictionary<string, string> PreviousAtRestKeys { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Version assigned to unversioned legacy rows during rotation. Leave unset when first adopting
    /// version metadata without changing the key; set to the old version before the first key rotation.
    /// </summary>
    public string? LegacyAtRestKeyVersion { get; set; }

    /// <summary>
    /// Allow the host-bound ENCRYPT=MACHINE fallback when <see cref="AtRestKey"/> is unset. Production
    /// must leave this false: the portal refuses to start without a key. Set true only for
    /// dev/standalone, where host-bound (non-portable) dataset caches are acceptable.
    /// </summary>
    public bool AllowMachineFallback { get; set; }
}

public class IdentityConfig
{
    public string Provider { get; set; } = "Local";
    public OidcIdentityConfig Oidc { get; set; } = new();
    public LdapIdentityConfig Ldap { get; set; } = new();
}

public class OidcIdentityConfig
{
    /// <summary>Master switch for federated OIDC login. When false the portal behaves exactly as
    /// before (Local/LDAP only) and OIDC endpoints report not-configured. Mirrors
    /// <see cref="LdapIdentityConfig.Enabled"/> so identity providers are gated consistently.</summary>
    public bool Enabled { get; set; }

    /// <summary>OIDC issuer / discovery authority (e.g. https://login.example.com/realms/etl).
    /// Must be an absolute HTTPS URI; the provider's <c>/.well-known/openid-configuration</c> and
    /// JWKS are resolved from it.</summary>
    public string? Authority { get; set; }

    /// <summary>Confidential client identifier registered with the identity provider.</summary>
    public string? ClientId { get; set; }

    /// <summary>Confidential client secret used for the authorization-code token exchange. Used
    /// verbatim (like Portal:Jwt:Secret); supply it via the Portal__Identity__Oidc__ClientSecret
    /// environment variable or a protected configuration source rather than committing it.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Optional tenant identifier for authority templating with multi-tenant providers.</summary>
    public string? TenantId { get; set; }

    /// <summary>Scopes requested at authorization time. <c>openid</c> is required and added if missing.</summary>
    public string[] Scopes { get; set; } = ["openid", "profile", "email"];

    /// <summary>Absolute path the identity provider redirects back to after authentication. Must be
    /// registered as a redirect URI with the provider and begin with '/'.</summary>
    public string CallbackPath { get; set; } = "/api/auth/oidc/callback";

    /// <summary>Relative path the app lands on after a successful federated login. The callback
    /// renders a hand-off page that stores the portal session (tokens are never put in the URL) and
    /// then forwards here. Must begin with '/'.</summary>
    public string PostLoginRedirectPath { get; set; } = "/index.html";

    /// <summary>Token claims, in priority order, that carry group/role membership for portal group sync.</summary>
    public string[] GroupClaimTypes { get; set; } = ["groups", "roles"];

    /// <summary>Claim used as the portal username. Falls back to <c>preferred_username</c> then <c>sub</c>.</summary>
    public string UsernameClaimType { get; set; } = "preferred_username";

    /// <summary>Claim used as the user's email address.</summary>
    public string EmailClaimType { get; set; } = "email";

    /// <summary>Additional accepted token audiences beyond <see cref="ClientId"/>.</summary>
    public string[] AdditionalAudiences { get; set; } = [];

    /// <summary>Claim types that must be present in a validated id_token (beyond the always-required
    /// <c>sub</c>); authentication fails closed if any is missing. Empty by default.</summary>
    public string[] RequiredClaims { get; set; } = [];

    /// <summary>Allowed clock skew (seconds) when validating id_token lifetime. Minimum effective value is 0.</summary>
    public int ClockSkewSeconds { get; set; } = 60;
}

public class LdapIdentityConfig
{
    public bool Enabled { get; set; } = false;
    public string Server { get; set; } = "localhost";
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; } = false;
    public bool AllowSelfSignedCertificates { get; set; } = false;
    public string Domain { get; set; } = "";
    public string BaseDn { get; set; } = "";
    public string? ServiceUser { get; set; }
    public string? ServicePassword { get; set; }
    public Dictionary<string, string> RoleMappings { get; set; } = new();
}

public class OrchestratorConfig
{
    public string? ApiUrl { get; set; }
    public string? DatabasePath { get; set; }
    public string? ApiKey { get; set; }
    public bool SameHost { get; set; } = false;

    /// <summary>Seconds between Orchestrator job-history polls. Minimum effective value is 1.</summary>
    public int PollIntervalSeconds { get; set; } = 60;
}

public class ResourcesConfig
{
    public int MaxConcurrentReportExecutions { get; set; } = 4;

    /// <summary>Workload fairness (P2.6): the most of the shared execution slots a single
    /// non-administrator may hold at once, so one user cannot starve the rest. Effective only
    /// when below <see cref="MaxConcurrentReportExecutions"/>; administrators are exempt.
    /// Minimum effective value is 1.</summary>
    public int MaxConcurrentExecutionsPerUser { get; set; } = 2;

    /// <summary>Workload fairness (P2.6): when greater than zero, the most shared execution
    /// slots members of the same portal group may hold at once. Users in multiple groups must
    /// satisfy every group quota; users with no groups are governed by the user/global gates.
    /// Administrators are exempt.</summary>
    public int MaxConcurrentExecutionsPerGroup { get; set; }

    /// <summary>Weighted admission for queued interactive report executions. When global execution
    /// slots are saturated, queued work is admitted using this weight against
    /// <see cref="RefreshExecutionWeight"/>. Minimum effective value is 1.</summary>
    public int InteractiveExecutionWeight { get; set; } = 2;

    /// <summary>Weighted admission for queued refresh executions. Minimum effective value is 1.</summary>
    public int RefreshExecutionWeight { get; set; } = 1;

    public int ExecutionTimeoutSeconds { get; set; } = 300;
    public int SessionCacheMaxSize { get; set; } = 50;
    public int SessionCacheTtlMinutes { get; set; } = 30;
    public bool PersistAdHocInteractions { get; set; } = false;

    /// <summary>Newest snapshots kept per report; older rows and their manifest files are
    /// pruned after each successful execution. Minimum effective value is 1.</summary>
    public int SnapshotRetentionPerReport { get; set; } = 20;
}

public class JwtConfig
{
    public string Secret { get; set; } = "";
    public string[] PreviousSecrets { get; set; } = [];
    public int ExpiryMinutes { get; set; } = 60;
    public int RefreshExpiryDays { get; set; } = 7;

    /// <summary>Seconds between expired-refresh-token purges. Minimum effective value is 1.</summary>
    public int RefreshTokenPurgeIntervalSeconds { get; set; } = 3600;
}

public class FirstRunConfig
{
    public string AdminUsername { get; set; } = "admin";

    /// <summary>
    /// Whether the seeded first-run admin must change the initial password before using API routes.
    /// Keep true for production; disposable automation topologies may set false after generating a
    /// strong per-run password.
    /// </summary>
    public bool MustChangePassword { get; set; } = true;

    /// <summary>
    /// Initial password for the seeded admin account. When unset, a random password is generated
    /// at first run and written once to the startup log — there is no well-known default.
    /// </summary>
    public string? AdminPassword { get; set; }
}
