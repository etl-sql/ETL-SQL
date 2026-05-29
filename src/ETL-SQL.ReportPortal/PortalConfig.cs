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
}
