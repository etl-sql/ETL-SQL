namespace ETL_SQL.ReportPortal;

public class PortalConfig
{
    public string DatabasePath    { get; set; } = "./portal.db";
    public string ScriptRootPath  { get; set; } = "./Reports";
    public string SnapshotDirectory { get; set; } = "./Snapshots";
    public string MapRootPath { get; set; } = "./data/maps";
    public string DatasetRootPath { get; set; } = "./data/datasets";
    public bool AllowServiceControl { get; set; } = false;
    public int  MaxPreviewRows    { get; set; } = 50000;
    public ResourcesConfig Resources { get; set; } = new();
    public JwtConfig       Jwt       { get; set; } = new();
    public IdentityConfig  Identity  { get; set; } = new();
    public FirstRunConfig  FirstRun  { get; set; } = new();
    public OrchestratorConfig Orchestrator { get; set; } = new();
    public DatasetConfig   Dataset   { get; set; } = new();
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
    public string? ApiUrl      { get; set; }
    public string? DatabasePath { get; set; }
    public string? ApiKey      { get; set; }
    public bool    SameHost    { get; set; } = false;
}

public class ResourcesConfig
{
    public int MaxConcurrentReportExecutions { get; set; } = 4;
    public int ExecutionTimeoutSeconds       { get; set; } = 300;
    public int SessionCacheMaxSize           { get; set; } = 50;
    public int SessionCacheTtlMinutes        { get; set; } = 30;
    public bool PersistAdHocInteractions     { get; set; } = false;
}

public class JwtConfig
{
    public string Secret          { get; set; } = "";
    public int    ExpiryMinutes   { get; set; } = 60;
    public int    RefreshExpiryDays { get; set; } = 7;
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
