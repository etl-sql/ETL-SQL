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
    public PortalLoadBalancerConfig LoadBalancer { get; set; } = new();
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
}

public class DatasetConfig
{
    /// <summary>
    /// Portal-managed at-rest key (base64) used to encrypt cached dataset parquet. Portable: back it
    /// up with the portal config and move it with the portal — losing it makes every cached dataset
    /// unreadable (they must be re-materialised). When unset, datasets fall back to host-bound
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
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? TenantId { get; set; }
    public string[] GroupClaimTypes { get; set; } = ["groups", "roles"];
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
    /// Initial password for the seeded admin account. When unset, a random password is generated
    /// at first run and written once to the startup log — there is no well-known default.
    /// </summary>
    public string? AdminPassword { get; set; }
}
