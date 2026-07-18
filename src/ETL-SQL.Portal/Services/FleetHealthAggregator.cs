using System.Net.Http.Json;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Portal.Services;

/// <summary>The read-only health summary one environment exposes to the fleet aggregator (P2.2):
/// status plus the operational signals an operator needs to triage a fleet, and nothing else — no
/// report data, scripts, secrets, or identities.</summary>
public sealed record FleetEnvironmentStatus(
    string Environment,
    string Status,
    int QueueDepth,
    int ActiveExecutions,
    int FailedRefreshes,
    int AuditOutboxPending,
    int AuditOutboxFailed,
    string Storage,
    DateTime CapturedAtUtc,
    SecurityEventDiagnostics? SecurityEvents = null,
    FleetNodeInventory? Inventory = null);

public sealed record FleetNodeInventory(
    string Environment,
    string NodeId,
    string InstalledVersion,
    FleetSchemaVersions SchemaVersions,
    FleetPolicyInventory Policy,
    FleetRuntimeProviders Providers,
    string ConfigurationFingerprint,
    FleetUpgradeReadiness UpgradeReadiness,
    FleetCompatibilityMetadata? Compatibility = null,
    FleetMigrationState? Migration = null);

public sealed record FleetCompatibilityMetadata(
    string MetadataVersion,
    string CompatibilityWindow,
    IReadOnlyList<string> RollingUpgradeSequence,
    IReadOnlyList<FleetComponentCompatibility> Components);

public sealed record FleetComponentCompatibility(
    string Component,
    string Version,
    string Contract,
    string MinimumCompatibleVersion,
    bool RequiredForUpgrade,
    string Status);

public sealed record FleetMigrationState(
    string State,
    string? OwnerNodeId,
    string? Provider,
    string? LockKind,
    long? LockKey,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? AcquiredAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int? PendingMigrations,
    string? Error);

public sealed record FleetSchemaVersions(
    string Enrollment,
    string PolicyEnvelope,
    string PolicyPayload,
    int SecurityEvent,
    string? LastAppliedPortalMigration,
    int AppliedPortalMigrations,
    int PendingPortalMigrations);

public sealed record FleetPolicyInventory(
    bool IsEnrolled,
    bool IsAvailable,
    string Status,
    string? PolicyVersion,
    string? PolicyHash,
    DateTimeOffset? IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LastLoadedAtUtc,
    bool PolicySigningKeyConfigured,
    bool ClientCertificateConfigured,
    DateTimeOffset? ClientCertificateExpiresAtUtc,
    int GovernedKeyCount);

public sealed record FleetRuntimeProviders(
    string PortalDatabase,
    string ArtifactStorage);

public sealed record FleetUpgradeReadiness(
    bool Ready,
    IReadOnlyList<string> Findings);

/// <summary>One environment the aggregator polls: its label, base URL, and a scoped FleetReader
/// bearer token. The token authorizes only <c>GET /api/fleet/status</c> in that environment.</summary>
public sealed record FleetEnvironmentDescriptor(string Name, Uri BaseUrl, string? BearerToken);

/// <summary>Per-environment poll outcome; <see cref="Status"/> is null when the environment is
/// unreachable or returned a non-success response (the aggregator never fails the whole fleet view
/// because one environment is down).</summary>
public sealed record FleetEnvironmentResult(
    string Name, bool Reachable, FleetEnvironmentStatus? Status, string? Error);

public sealed record FleetHealthGroup(string Key, int Total, int Unreachable, int Unhealthy);

public sealed record FleetFinding(
    string Severity,
    string Scope,
    string Code,
    string Message);

public enum FleetGroupBy
{
    None,
    Status,
    Environment,
    DatabaseProvider,
    StorageProvider,
    PolicyVersion,
    UpgradeReadiness
}

public sealed record FleetViewOptions(
    string? Search = null,
    string? Status = null,
    bool? Reachable = null,
    bool? UpgradeReady = null,
    string? DatabaseProvider = null,
    string? StorageProvider = null,
    string? PolicyVersion = null,
    FleetGroupBy GroupBy = FleetGroupBy.None);

public enum FleetUpgradeReportMode
{
    Preflight,
    Postflight
}

public sealed record FleetUpgradeCheck(
    string Code,
    string Status,
    string Message);

public sealed record FleetUpgradeReport(
    FleetUpgradeReportMode Mode,
    DateTime GeneratedAtUtc,
    bool Ready,
    IReadOnlyList<FleetUpgradeCheck> Checks,
    IReadOnlyList<FleetFinding> Findings);

public sealed record FleetHealthReport(
    DateTime GeneratedAtUtc,
    IReadOnlyList<FleetEnvironmentResult> Environments,
    IReadOnlyList<FleetHealthGroup>? Groups = null,
    IReadOnlyList<FleetFinding>? Findings = null)
{
    public int Total => Environments.Count;
    public int Unreachable => Environments.Count(e => !e.Reachable);
    public int Unhealthy =>
        Environments.Count(e => e.Reachable && e.Status is { } s
            && !string.Equals(s.Status, "Healthy", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Read-only fleet health aggregation (P2.2). Fans out to each environment's
/// <c>GET /api/fleet/status</c> with its scoped FleetReader token and merges the results. It only
/// ever issues that single read-only GET — it never writes, runs scripts, or reads report data — and
/// it tolerates unreachable environments rather than failing the whole view, so a fleet operator
/// gets a complete picture even during a partial outage. See the fleet trust boundary in
/// Departmental_Isolation.md.
/// </summary>
public sealed class FleetHealthAggregator(HttpClient http)
{
    private const string CurrentCompatibilityMetadataVersion = "1.0";

    public async Task<FleetHealthReport> AggregateAsync(
        IEnumerable<FleetEnvironmentDescriptor> environments,
        FleetViewOptions? options = null,
        CancellationToken ct = default)
    {
        var results = await Task.WhenAll(environments.Select(e => PollAsync(e, ct)));
        var filtered = ApplyView(results, options).ToArray();
        var groups = BuildGroups(filtered, options?.GroupBy ?? FleetGroupBy.None);
        var findings = BuildFindings(filtered);
        return new FleetHealthReport(DateTime.UtcNow, filtered, groups, findings);
    }

    public Task<FleetHealthReport> AggregateAsync(
        IEnumerable<FleetEnvironmentDescriptor> environments,
        CancellationToken ct) =>
        AggregateAsync(environments, null, ct);

    public static FleetUpgradeReport BuildUpgradeReport(
        FleetHealthReport report,
        FleetUpgradeReportMode mode)
    {
        var checks = new List<FleetUpgradeCheck>();
        AddCheck(checks, "all-environments-reachable", report.Unreachable == 0,
            report.Unreachable == 0
                ? "Every environment returned fleet status."
                : $"{report.Unreachable} environment(s) are unreachable.");
        AddCheck(checks, "all-environments-healthy", report.Unhealthy == 0,
            report.Unhealthy == 0
                ? "Every reachable environment reports Healthy."
                : $"{report.Unhealthy} reachable environment(s) are degraded or unhealthy.");

        var inventories = report.Environments
            .Where(result => result.Reachable)
            .Select(result => result.Status?.Inventory)
            .ToArray();
        AddCheck(checks, "inventory-present", inventories.All(inventory => inventory is not null),
            "Every reachable environment must return fleet inventory metadata.");
        AddCheck(checks, "compatibility-metadata-present",
            inventories.Where(inventory => inventory is not null).All(inventory => inventory!.Compatibility is not null),
            "Every reachable environment must return compatibility metadata.");
        AddCheck(checks, "supported-compatibility-window",
            report.Findings?.All(finding => finding.Code is not (
                "unsupported-compatibility-metadata" or
                "unsupported-compatibility-window")) != false,
            "Fleet versions must stay within the advertised N-1 rolling compatibility window.");
        AddCheck(checks, "upgrade-readiness",
            inventories.Where(inventory => inventory is not null).All(inventory => inventory!.UpgradeReadiness.Ready),
            "Every reachable environment must report upgrade readiness.");
        AddCheck(checks, "no-pending-portal-migrations",
            inventories.Where(inventory => inventory is not null)
                .All(inventory => inventory!.SchemaVersions.PendingPortalMigrations == 0),
            "Portal database schemas must have no pending migrations before traffic is restored.");

        if (mode == FleetUpgradeReportMode.Postflight)
        {
            AddCheck(checks, "postflight-no-divergence",
                report.Findings?.All(finding => finding.Code is not (
                    "policy-version-divergence" or
                    "policy-hash-divergence" or
                    "configuration-drift" or
                    "installed-version-divergence")) != false,
                "Postflight should not show policy, configuration, or installed-version divergence.");
        }

        return new FleetUpgradeReport(
            mode,
            DateTime.UtcNow,
            checks.All(check => check.Status == "Pass"),
            checks,
            report.Findings ?? Array.Empty<FleetFinding>());
    }

    private static void AddCheck(
        List<FleetUpgradeCheck> checks,
        string code,
        bool passed,
        string message) =>
        checks.Add(new FleetUpgradeCheck(code, passed ? "Pass" : "Fail", message));

    private async Task<FleetEnvironmentResult> PollAsync(FleetEnvironmentDescriptor env, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(env.BaseUrl, "api/fleet/status"));
            if (!string.IsNullOrWhiteSpace(env.BearerToken))
                request.Headers.Authorization = new("Bearer", env.BearerToken);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new FleetEnvironmentResult(env.Name, false, null, $"HTTP {(int)response.StatusCode}");

            var status = await response.Content.ReadFromJsonAsync<FleetEnvironmentStatus>(ct);
            return status is null
                ? new FleetEnvironmentResult(env.Name, false, null, "empty response")
                : new FleetEnvironmentResult(env.Name, true, status, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FleetEnvironmentResult(env.Name, false, null, ex.Message);
        }
    }

    private static IEnumerable<FleetEnvironmentResult> ApplyView(
        IEnumerable<FleetEnvironmentResult> results,
        FleetViewOptions? options)
    {
        if (options is null)
            return results;

        var query = results;
        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            var term = options.Search.Trim();
            query = query.Where(result => SearchableValues(result)
                .Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
        if (!string.IsNullOrWhiteSpace(options.Status))
            query = query.Where(result => string.Equals(result.Status?.Status, options.Status,
                StringComparison.OrdinalIgnoreCase));
        if (options.Reachable is { } reachable)
            query = query.Where(result => result.Reachable == reachable);
        if (options.UpgradeReady is { } ready)
            query = query.Where(result => result.Status?.Inventory?.UpgradeReadiness.Ready == ready);
        if (!string.IsNullOrWhiteSpace(options.DatabaseProvider))
            query = query.Where(result => string.Equals(
                result.Status?.Inventory?.Providers.PortalDatabase,
                options.DatabaseProvider,
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(options.StorageProvider))
            query = query.Where(result => string.Equals(
                result.Status?.Inventory?.Providers.ArtifactStorage,
                options.StorageProvider,
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(options.PolicyVersion))
            query = query.Where(result => string.Equals(
                result.Status?.Inventory?.Policy.PolicyVersion,
                options.PolicyVersion,
                StringComparison.OrdinalIgnoreCase));

        return query;
    }

    private static IReadOnlyList<FleetHealthGroup> BuildGroups(
        IReadOnlyList<FleetEnvironmentResult> results,
        FleetGroupBy groupBy)
    {
        if (groupBy == FleetGroupBy.None)
            return Array.Empty<FleetHealthGroup>();

        return results
            .GroupBy(result => GroupKey(result, groupBy), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new FleetHealthGroup(
                group.Key,
                group.Count(),
                group.Count(result => !result.Reachable),
                group.Count(result => result.Reachable
                    && result.Status is { } status
                    && !string.Equals(status.Status, "Healthy", StringComparison.OrdinalIgnoreCase))))
            .ToArray();
    }

    private static IReadOnlyList<FleetFinding> BuildFindings(IReadOnlyList<FleetEnvironmentResult> results)
    {
        var findings = new List<FleetFinding>();
        foreach (var result in results)
        {
            if (!result.Reachable)
            {
                findings.Add(new FleetFinding("Error", result.Name, "environment-unreachable",
                    result.Error ?? "Environment did not return fleet status."));
                continue;
            }

            if (result.Status is not { } status)
            {
                findings.Add(new FleetFinding("Error", result.Name, "status-missing",
                    "Environment returned no fleet status payload."));
                continue;
            }

            if (!string.Equals(status.Status, "Healthy", StringComparison.OrdinalIgnoreCase))
                findings.Add(new FleetFinding("Warning", result.Name, "environment-health",
                    $"Environment health is {status.Status}."));
            if (status.SecurityEvents is null)
                findings.Add(new FleetFinding("Warning", result.Name, "security-event-diagnostics-missing",
                    "Security-event diagnostics are not present in the fleet status payload."));
            if (status.Inventory is not { } inventory)
            {
                findings.Add(new FleetFinding("Warning", result.Name, "inventory-missing",
                    "Fleet inventory metadata is not present; upgrade readiness cannot be evaluated."));
                continue;
            }

            if (inventory.SchemaVersions.Enrollment != EnterpriseEnrollmentDocument.CurrentSchemaVersion)
                findings.Add(UnsupportedSchema(result.Name, "enrollment", inventory.SchemaVersions.Enrollment));
            if (inventory.SchemaVersions.PolicyEnvelope != SignedOrganizationPolicyEnvelope.CurrentSchemaVersion)
                findings.Add(UnsupportedSchema(result.Name, "policy-envelope", inventory.SchemaVersions.PolicyEnvelope));
            if (inventory.SchemaVersions.PolicyPayload != OrganizationPolicyDocument.CurrentSchemaVersion)
                findings.Add(UnsupportedSchema(result.Name, "policy-payload", inventory.SchemaVersions.PolicyPayload));
            if (inventory.SchemaVersions.SecurityEvent != SecurityEventContract.CurrentSchemaVersion)
                findings.Add(new FleetFinding("Error", result.Name, "unsupported-security-event-schema",
                    $"Security-event schema {inventory.SchemaVersions.SecurityEvent} is not supported by this aggregator."));

            if (!inventory.UpgradeReadiness.Ready)
            {
                foreach (var readinessFinding in inventory.UpgradeReadiness.Findings)
                    findings.Add(new FleetFinding("Warning", result.Name, "upgrade-readiness",
                        readinessFinding));
            }
            if (inventory.Compatibility is { } compatibility
                && !string.Equals(compatibility.MetadataVersion, CurrentCompatibilityMetadataVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new FleetFinding("Error", result.Name, "unsupported-compatibility-metadata",
                    $"Compatibility metadata version {compatibility.MetadataVersion} is not supported by this aggregator."));
            }
        }

        foreach (var environmentGroup in results
            .Where(result => result.Reachable && result.Status?.Inventory is not null)
            .GroupBy(result => result.Status!.Inventory!.Environment, StringComparer.OrdinalIgnoreCase))
        {
            AddDivergenceFinding(findings, environmentGroup, "policy-version-divergence",
                result => result.Status!.Inventory!.Policy.PolicyVersion ?? "Standalone",
                "Nodes in the same environment report different policy versions.");
            AddDivergenceFinding(findings, environmentGroup, "policy-hash-divergence",
                result => result.Status!.Inventory!.Policy.PolicyHash ?? "none",
                "Nodes in the same environment report different policy hashes.");
            AddDivergenceFinding(findings, environmentGroup, "configuration-drift",
                result => result.Status!.Inventory!.ConfigurationFingerprint,
                "Nodes in the same environment report different configuration fingerprints.");
            AddDivergenceFinding(findings, environmentGroup, "installed-version-divergence",
                result => result.Status!.Inventory!.InstalledVersion,
                "Nodes in the same environment report different installed versions.");
            AddCompatibilityWindowFinding(findings, environmentGroup);
        }

        return findings;
    }

    private static FleetFinding UnsupportedSchema(string scope, string schema, string version) =>
        new("Error", scope, $"unsupported-{schema}-schema",
            $"{schema} schema {version} is not supported by this aggregator.");

    private static void AddDivergenceFinding(
        List<FleetFinding> findings,
        IGrouping<string, FleetEnvironmentResult> environmentGroup,
        string code,
        Func<FleetEnvironmentResult, string> valueSelector,
        string message)
    {
        var values = environmentGroup
            .Select(valueSelector)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        if (values.Length > 1)
            findings.Add(new FleetFinding("Warning", environmentGroup.Key, code, message));
    }

    private static void AddCompatibilityWindowFinding(
        List<FleetFinding> findings,
        IGrouping<string, FleetEnvironmentResult> environmentGroup)
    {
        var versions = environmentGroup
            .Select(result => result.Status!.Inventory!.InstalledVersion)
            .Select(ParseMajorMinor)
            .ToArray();
        if (versions.Any(version => version is null))
        {
            findings.Add(new FleetFinding("Error", environmentGroup.Key, "unsupported-compatibility-window",
                "At least one installed version is not parseable as a semantic version; N-1 compatibility cannot be certified."));
            return;
        }

        var parsed = versions.Select(version => version!.Value).ToArray();
        var majorVersions = parsed.Select(version => version.Major).Distinct().Take(2).ToArray();
        if (majorVersions.Length > 1)
        {
            findings.Add(new FleetFinding("Error", environmentGroup.Key, "unsupported-compatibility-window",
                "Nodes in the same environment span more than one major installed version; N-1 rolling compatibility is not supported."));
            return;
        }

        var minMinor = parsed.Min(version => version.Minor);
        var maxMinor = parsed.Max(version => version.Minor);
        if (maxMinor - minMinor > 1)
            findings.Add(new FleetFinding("Error", environmentGroup.Key, "unsupported-compatibility-window",
                $"Nodes in the same environment span minor versions {minMinor} to {maxMinor}; only N-1 rolling compatibility is supported."));
    }

    private static (int Major, int Minor)? ParseMajorMinor(string version)
    {
        var core = version.Split('-', '+')[0];
        var parts = core.Split('.');
        if (parts.Length < 2)
            return null;
        return int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor)
            ? (major, minor)
            : null;
    }

    private static string GroupKey(FleetEnvironmentResult result, FleetGroupBy groupBy) =>
        groupBy switch
        {
            FleetGroupBy.Status => result.Reachable ? result.Status?.Status ?? "Unknown" : "Unreachable",
            FleetGroupBy.Environment => result.Status?.Environment ?? result.Name,
            FleetGroupBy.DatabaseProvider => result.Status?.Inventory?.Providers.PortalDatabase ?? "Unknown",
            FleetGroupBy.StorageProvider => result.Status?.Inventory?.Providers.ArtifactStorage ?? "Unknown",
            FleetGroupBy.PolicyVersion => result.Status?.Inventory?.Policy.PolicyVersion ?? "Standalone",
            FleetGroupBy.UpgradeReadiness => result.Status?.Inventory?.UpgradeReadiness.Ready == true
                ? "Ready"
                : "NotReady",
            _ => "All"
        };

    private static IEnumerable<string> SearchableValues(FleetEnvironmentResult result)
    {
        yield return result.Name;
        if (result.Error is not null) yield return result.Error;
        if (result.Status is not { } status) yield break;

        yield return status.Environment;
        yield return status.Status;
        yield return status.Storage;
        if (status.Inventory is not { } inventory) yield break;

        yield return inventory.NodeId;
        yield return inventory.InstalledVersion;
        yield return inventory.Providers.PortalDatabase;
        yield return inventory.Providers.ArtifactStorage;
        yield return inventory.Policy.Status;
        if (inventory.Policy.PolicyVersion is not null) yield return inventory.Policy.PolicyVersion;
        if (inventory.Policy.PolicyHash is not null) yield return inventory.Policy.PolicyHash;
        yield return inventory.ConfigurationFingerprint;
        foreach (var finding in inventory.UpgradeReadiness.Findings)
            yield return finding;
    }
}
