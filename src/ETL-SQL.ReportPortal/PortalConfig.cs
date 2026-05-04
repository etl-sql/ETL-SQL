namespace ETL_SQL.ReportPortal;

public class PortalConfig
{
    public string DatabasePath    { get; set; } = "./portal.db";
    public string ScriptRootPath  { get; set; } = "./Reports";
    public string SnapshotDirectory { get; set; } = "./Snapshots";
    public ResourcesConfig Resources { get; set; } = new();
    public JwtConfig       Jwt       { get; set; } = new();
    public FirstRunConfig  FirstRun  { get; set; } = new();
    public OrchestratorConfig Orchestrator { get; set; } = new();
}

public class OrchestratorConfig
{
    public string? ApiUrl { get; set; }
}

public class ResourcesConfig
{
    public int MaxConcurrentReportExecutions { get; set; } = 4;
    public int ExecutionTimeoutSeconds       { get; set; } = 300;
    public int SessionCacheMaxSize           { get; set; } = 50;
    public int SessionCacheTtlMinutes        { get; set; } = 30;
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
