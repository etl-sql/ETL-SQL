using System.Text.RegularExpressions;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Generates and validates departmental deployment plans, and reports this environment's own
/// isolation evidence.
///
/// The naming, path and port conventions are the ones in Departmental_Isolation.md §3–§4: every
/// isolated resource is a deterministic function of the environment id, which is what makes a plan
/// checkable rather than a document someone has to follow carefully.
///
/// Two boundaries this service holds:
/// <list type="bullet">
///   <item><description>It <b>never provisions</b>. Creating databases, accounts, key rings and
///     endpoints is authority this process must not hold — an environment that could provision
///     another is not isolated from it.</description></item>
///   <item><description>It <b>never generates or reports a secret</b>. Keys appear as requirements
///     at named configuration keys, so a plan is safe to review, store, and hand to whoever does
///     the provisioning.</description></item>
/// </list>
/// </summary>
public sealed partial class EnvironmentPlanService(PortalDbContext db, PortalConfig config)
{
    /// <summary>Environment ids are DNS-safe tokens: they become hostnames, accounts, and paths.</summary>
    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,30}[a-z0-9])?$")]
    private static partial Regex EnvironmentIdPattern();

    /// <summary>Ports are allocated in blocks; §3 requires at least 10 between environments.</summary>
    public const int PortBlockSize = 10;

    public static bool IsValidEnvironmentId(string environmentId) =>
        !string.IsNullOrWhiteSpace(environmentId) && EnvironmentIdPattern().IsMatch(environmentId);

    public EnvironmentPlanDto GeneratePlan(string environmentId, int portBase)
    {
        var env = environmentId.Trim().ToLowerInvariant();

        return new EnvironmentPlanDto(
            env,
            portBase,
            Resources:
            [
                new("PortalDatabase", $"portal_{env}",
                    $"/srv/etl-sql/{env}/data/portal.db",
                    $"PostgreSQL database 'portal_{env}' with its own login",
                    "A distinct database and a distinct login. Sharing either lets one environment read another's catalog."),
                new("OrchestratorDatabase", $"orch_{env}",
                    $"/srv/etl-sql/{env}/data/etlsql.db",
                    $"PostgreSQL database 'orch_{env}' with its own login",
                    "As above: job history and schedules are as sensitive as the catalog."),
                new("ArtifactRoot", $"{env}-artifacts",
                    $"/srv/etl-sql/{env}/{{Reports,Snapshots,datasets,maps}}",
                    $"Dedicated share or prefix per environment (Smb/UNC)",
                    "A distinct root reachable only by this environment's service identity."),
                new("KeyRing", $"{env}-portal-keys",
                    $"/srv/etl-sql/{env}/data/.portal-keys",
                    "Dedicated shared path per environment",
                    "A shared key ring lets one environment decrypt another's protected state."),
                // The security-event outbox defaults to a MACHINE-WIDE path under
                // LocalApplicationData, shared by every ETL-SQL process on the host. Two
                // environments on one machine would therefore write their security events into one
                // another's queue — a cross-environment leak of exactly the records isolation
                // exists to keep apart, and one nothing else in this plan would catch.
                new("SecurityEventOutbox", $"{env}-security-events",
                    $"/srv/etl-sql/{env}/data/security-events.db "
                    + "(set ETLSQL_SECURITY_EVENT_OUTBOX_PATH; the default is machine-wide)",
                    "Dedicated path per environment",
                    "The default location is shared by every process on the host, so leaving it "
                    + "unset puts two environments' security events in one queue."),
                new("ServiceIdentity", $"etlsql-{env}",
                    $"OS account 'etlsql-{env}'",
                    $"Dedicated account or gMSA 'etlsql-{env}'",
                    "Granted access only to this environment's paths, databases, and keys."),
                new("WindowsServices", $"ETL-SQL-Portal-{env}",
                    $"ETL-SQL-Portal-{env}, ETL-SQL-Orchestrator-{env}",
                    "Same names, one set per node",
                    "Distinct service names so one environment's restart cannot stop another's."),
                new("SystemdUnits", $"etl-sql-portal@{env}",
                    $"etl-sql-portal@{env}, etl-sql-orchestrator@{env}",
                    "Same units, one set per node",
                    "Distinct instances under the environment's own account."),
                new("ComposeProject", $"etlsql-{env}",
                    $"etlsql-{env}",
                    $"etlsql-{env}",
                    "Distinct project name so volumes and networks never merge.")
            ],
            Ports:
            [
                new("Portal HTTP", portBase + 0, 0),
                new("Orchestrator HTTP", portBase + 1, 1),
                new("Portal HTTPS", portBase + 2, 2),
                new("Orchestrator HTTPS", portBase + 3, 3),
                new("PostgreSQL (HA, optional published)", portBase + 32, 32)
            ],
            SecretRequirements:
            [
                new("Portal:Jwt:Secret",
                    "A unique secret of 32 or more characters.",
                    "This environment's HA nodes only — a shared secret lets a token minted for one environment authenticate to another."),
                new("Portal:Dataset:AtRestKey",
                    "A unique base64 key of 32 or more bytes.",
                    "This environment's HA nodes only."),
                new("Orchestrator:ApiKey / Portal:Orchestrator:ApiKey",
                    "A unique key; the two must match within the environment.",
                    "This environment's nodes only — it gates the Orchestrator job API.")
            ],
            Notes:
            [
                "This plan is a description. The Portal does not provision environments: creating "
                    + "databases, accounts, key rings and endpoints belongs to a separately authorized "
                    + "deployment plane, because an environment able to provision another is not "
                    + "isolated from it.",
                "No secret values appear here and none were generated. Supply each requirement above "
                    + "at the named configuration key during provisioning.",
                $"Allocate the next environment a port base at least {PortBlockSize} away.",
                "Within one environment, HA nodes deliberately share its database, artifact root, key "
                    + "ring and the three keys above. The isolation boundary is between environments, "
                    + "never within one."
            ]);
    }

    /// <summary>
    /// Checks a proposed plan against what this Portal already knows: its own environment, the
    /// environments named for fleet visibility, and the machine registry. Any shared resource is a
    /// collision, not a warning — sharing one is enough to break isolation.
    /// </summary>
    public async Task<EnvironmentPlanValidationDto> ValidateAsync(
        EnvironmentPlanDto plan, CancellationToken ct = default)
    {
        var collisions = new List<EnvironmentCollisionDto>();
        var warnings = new List<string>();

        var current = CurrentEnvironmentId();
        if (string.Equals(plan.EnvironmentId, current, StringComparison.OrdinalIgnoreCase))
        {
            collisions.Add(new EnvironmentCollisionDto("EnvironmentId",
                $"'{plan.EnvironmentId}' is the environment this Portal is already running as.",
                $"this Portal ({current})"));
        }

        foreach (var known in await KnownEnvironmentIdsAsync(ct))
        {
            if (string.Equals(known, plan.EnvironmentId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(known, current, StringComparison.OrdinalIgnoreCase))
            {
                collisions.Add(new EnvironmentCollisionDto("EnvironmentId",
                    $"'{plan.EnvironmentId}' is already in use.", known));
            }
        }

        // The Portal can only see its own ports, so this catches the common single-host mistake
        // rather than claiming fleet-wide port knowledge it does not have.
        var localPorts = LocalPorts();
        foreach (var port in plan.Ports.Where(port => localPorts.Contains(port.Port)))
        {
            collisions.Add(new EnvironmentCollisionDto("Port",
                $"Port {port.Port} ({port.Endpoint}) is already bound by this Portal.",
                $"this Portal ({current})"));
        }

        foreach (var resource in plan.Resources)
        {
            if (SharesPathWithCurrent(resource.SingleNodeValue))
            {
                collisions.Add(new EnvironmentCollisionDto(resource.Kind,
                    $"'{resource.SingleNodeValue}' overlaps a path this Portal already uses.",
                    $"this Portal ({current})"));
            }
        }

        if (plan.PortBase <= 0)
            warnings.Add("No port base was supplied, so the port block could not be checked.");
        if (string.Equals(current, "default", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "This Portal has no ETLSQL_ENV set, so it is running as 'default'. Environment id "
                + "collisions can only be checked against environments it has been told about.");
        }

        return new EnvironmentPlanValidationDto(
            collisions.Count == 0,
            plan.EnvironmentId,
            collisions,
            warnings,
            ProvisioningNote:
                "Validation says the plan does not collide with what this Portal can see. It does not "
                + "provision anything: export the plan and apply it through the deployment plane that "
                + "holds authority over databases, accounts, keys and endpoints.");
    }

    /// <summary>
    /// This environment measured against the isolation contract. Some resources are simply not
    /// observable from inside the process — a shared database <em>login</em>, or two environments
    /// running under the same OS account, are facts the deployment plane holds. Those are reported as
    /// unknown rather than assumed isolated, because a verification that quietly assumes the answer
    /// is worse than one that admits the gap.
    /// </summary>
    public EnvironmentIsolationEvidenceDto DescribeCurrent()
    {
        var env = CurrentEnvironmentId();
        var evidence = new List<EnvironmentEvidenceItemDto>
        {
            new("EnvironmentId", env, env != "default",
                env == "default"
                    ? "ETLSQL_ENV is unset, so this deployment is unlabelled. Every derived name and "
                      + "path is therefore the default one, which is what collides first."
                    : "Set from ETLSQL_ENV; every isolated resource derives from it."),
            new("PortalDatabase", DescribePath(config.DatabasePath),
                PathMentionsEnvironment(config.DatabasePath, env),
                "The Portal can see its own database path, not whether another environment shares its login."),
            new("ArtifactRoot", DescribePath(config.ScriptRootPath),
                PathMentionsEnvironment(config.ScriptRootPath, env),
                "Scripts, snapshots, datasets and maps must all sit under this environment's root."),
            new("KeyRing", DescribePath(config.Storage.KeyRingPath ?? "(default: beside the portal database)"),
                config.Storage.KeyRingPath is null ? null : PathMentionsEnvironment(config.Storage.KeyRingPath, env),
                "A shared key ring lets one environment decrypt another's protected state."),
            new("SecurityEventOutbox",
                Environment.GetEnvironmentVariable(
                    ETL_SQL.Core.Governance.SecurityEventOutboxPaths.StandaloneOverrideEnvironmentVariable)
                    is { Length: > 0 } configured
                    ? DescribePath(configured)
                    : "(default: machine-wide under LocalApplicationData)",
                Environment.GetEnvironmentVariable(
                    ETL_SQL.Core.Governance.SecurityEventOutboxPaths.StandaloneOverrideEnvironmentVariable)
                    is { Length: > 0 } path
                    ? PathMentionsEnvironment(path, env)
                    : false,
                "Left unset the outbox is shared by every ETL-SQL process on this host, so two "
                + "environments would write security events into one queue."),
            new("JwtSecret", config.Jwt.Secret is { Length: > 0 } ? "configured" : "not configured",
                null,
                "Uniqueness cannot be checked from inside one environment; compare across environments "
                    + "during provisioning. A shared secret lets a token from one authenticate to another."),
            new("DatasetAtRestKey", string.IsNullOrWhiteSpace(config.Dataset.AtRestKey) ? "not configured" : "configured",
                null,
                "As above — uniqueness is a provisioning-time property."),
            new("ServiceIdentity", Environment.UserName, null,
                "The account this process runs as. Whether another environment shares it is visible to "
                    + "the deployment plane, not from here.")
        };

        var findings = new List<string>();
        if (env == "default")
            findings.Add("ETLSQL_ENV is unset: this deployment is unlabelled and cannot be distinguished in fleet views.");
        foreach (var item in evidence.Where(item => item.Isolated == false))
            findings.Add($"{item.Resource} does not appear to be scoped to environment '{env}'.");
        if (string.IsNullOrWhiteSpace(config.Dataset.AtRestKey))
            findings.Add("No dataset at-rest key is configured, so caches fall back to host-bound encryption.");

        return new EnvironmentIsolationEvidenceDto(env, evidence, findings, "/api/fleet/workspace");
    }

    private static string CurrentEnvironmentId() =>
        Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default";

    private async Task<IReadOnlyList<string>> KnownEnvironmentIdsAsync(CancellationToken ct)
    {
        var fromFleet = config.Fleet.Environments
            .Select(environment => environment.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name));
        var fromMachines = await db.Set<PolicyMachineEntity>()
            .AsNoTracking()
            .Select(machine => machine.Environment)
            .Distinct()
            .ToListAsync(ct);

        return [.. fromFleet.Concat(fromMachines)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)];
    }

    private HashSet<int> LocalPorts()
    {
        var ports = new HashSet<int>();
        foreach (var url in (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed) && !parsed.IsDefaultPort)
                ports.Add(parsed.Port);
        }

        if (Uri.TryCreate(config.Orchestrator.ApiUrl, UriKind.Absolute, out var orchestrator))
            ports.Add(orchestrator.Port);

        return ports;
    }

    private bool SharesPathWithCurrent(string candidate)
    {
        if (!candidate.Contains('/') && !candidate.Contains('\\')) return false;

        foreach (var path in new[]
        {
            config.DatabasePath, config.ScriptRootPath, config.SnapshotDirectory,
            config.DatasetRootPath, config.MapRootPath, config.Storage.KeyRingPath
        })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(normalized, TryFullPath(candidate), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? TryFullPath(string candidate)
    {
        try { return Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Paths are reported by shape, not in full: an absolute host path is deployment detail.</summary>
    private static string DescribePath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "(unset)" : Path.GetFileName(path.TrimEnd('/', '\\')) is { Length: > 0 } leaf
            ? $".../{leaf}"
            : path;

    private static bool PathMentionsEnvironment(string path, string environmentId) =>
        environmentId != "default"
        && !string.IsNullOrWhiteSpace(path)
        && path.Contains(environmentId, StringComparison.OrdinalIgnoreCase);
}
