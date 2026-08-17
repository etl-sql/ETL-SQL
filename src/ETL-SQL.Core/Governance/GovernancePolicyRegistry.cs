namespace ETL_SQL.Core.Governance;

/// <inheritdoc />
public sealed class GovernancePolicyRegistry : IGovernancePolicyRegistry
{
    private readonly Dictionary<string, GovernancePolicyDefinition> _definitions =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<GovernancePolicyDefinition> Definitions =>
        _definitions.Values
            .OrderBy(definition => definition.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool TryGet(string key, out GovernancePolicyDefinition definition) =>
        _definitions.TryGetValue(GovernancePolicyDefinition.NormalizeKey(key), out definition!);

    public GovernancePolicyDefinition GetRequired(string key) =>
        TryGet(key, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Governance policy '{key}' is not registered.");

    public void Register(GovernancePolicyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_definitions.TryAdd(definition.Key, definition))
            throw new InvalidOperationException($"Governance policy '{definition.Key}' is already registered.");
    }

    public void RegisterRange(IEnumerable<GovernancePolicyDefinition> definitions)
    {
        foreach (var definition in definitions)
            Register(definition);
    }

    public static GovernancePolicyRegistry CreateDefault()
    {
        var registry = new GovernancePolicyRegistry();
        registry.RegisterRange(DefaultDefinitions());
        return registry;
    }

    private static IEnumerable<GovernancePolicyDefinition> DefaultDefinitions()
    {
        yield return new(
            "Engine:AllowPlaintextSecrets",
            GovernancePolicyScope.Secret,
            GovernancePolicyClassification.Forbidden,
            GovernancePolicyValueKind.Boolean,
            "Controls whether scripts may persist plaintext connector or credential material.",
            defaultValue: false,
            allowedValues: ["false"]);

        yield return new(
            "Engine:NoSaveSensitive",
            GovernancePolicyScope.Secret,
            GovernancePolicyClassification.Locked,
            GovernancePolicyValueKind.Boolean,
            "Prevents sensitive values from being saved by lower-level hosts or scripts.",
            defaultValue: false);

        yield return new(
            "Engine:NoSaveConnection",
            GovernancePolicyScope.Secret,
            GovernancePolicyClassification.Locked,
            GovernancePolicyValueKind.Boolean,
            "Prevents full connection definitions from being saved by lower-level hosts or scripts.",
            defaultValue: false);

        yield return new(
            "Engine:ConnectionEncryption",
            GovernancePolicyScope.Secret,
            GovernancePolicyClassification.Locked,
            GovernancePolicyValueKind.Boolean,
            "Requires saved connection details to be encrypted when persistence is allowed.",
            defaultValue: false);

        yield return new(
            "Security:PathProtectionMode",
            GovernancePolicyScope.Filesystem,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Enum,
            "Restricts filesystem access posture for script-driven file operations.",
            defaultValue: "Restricted",
            allowedValues: ["Restricted", "Defined"]);

        yield return new(
            "Security:ApprovedSafeZones",
            GovernancePolicyScope.Filesystem,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.PathList,
            "Directories explicitly approved for script file operations and elevated file-operation limits.");

        yield return new(
            "Security:AllowedWriteExtensions",
            GovernancePolicyScope.Filesystem,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.StringList,
            "File extensions that script-driven writes may target under authoritative policy.",
            defaultValue: Array.Empty<string>());

        yield return new(
            "Security:AllowedHosts",
            GovernancePolicyScope.Network,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.HostPatternList,
            "Network host patterns that scripts and connectors may contact.",
            defaultValue: Array.Empty<string>());

        yield return new(
            "Security:EgressFenceExemptions",
            GovernancePolicyScope.Network,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.StringList,
            "Exact hosting-infrastructure destinations exempted from the non-bypassable egress fence " +
            "(cloud metadata, link-local node services, container runtime bridge, cluster service " +
            "discovery). Wildcards are rejected.",
            defaultValue: Array.Empty<string>());

        yield return new(
            "Security:AllowedEnvVars",
            GovernancePolicyScope.Security,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.StringList,
            "Environment variable names that scripts may read through ENV().",
            defaultValue: Array.Empty<string>());

        yield return new(
            "Security:MaxFileOperationsPerScript",
            GovernancePolicyScope.Filesystem,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Maximum file operations a script may perform without an approved override.",
            defaultValue: 100,
            minimumValue: 0);

        yield return new(
            "Security:MaxRecursiveNestingDepth",
            GovernancePolicyScope.Filesystem,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Maximum directory recursion depth permitted for script-driven file operations.",
            defaultValue: 5,
            minimumValue: 0);

        yield return new(
            "Security:MaxSpillBytesPerScript",
            GovernancePolicyScope.Filesystem,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Long,
            "Maximum total bytes a script may spill to engine-owned temp/cache storage.",
            minimumValue: 0L);

        yield return new(
            "Security:AllowedDockerImages",
            GovernancePolicyScope.Execution,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.StringList,
            "Docker image references a script may run via USE DOCKER(...).",
            defaultValue: Array.Empty<string>());

        yield return new(
            "Security:MaxParallelDegree",
            GovernancePolicyScope.Execution,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Maximum script-requested parallel degree.",
            defaultValue: 32,
            minimumValue: 1);

        yield return new(
            "Security:AllowedExecutionModes",
            GovernancePolicyScope.Execution,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.StringList,
            "Execution modes permitted by authoritative organization policy.");

        yield return new(
            "Security:RemoteExecutionMode",
            GovernancePolicyScope.Execution,
            GovernancePolicyClassification.Locked,
            GovernancePolicyValueKind.Enum,
            "Controls whether and how remote execution is permitted.",
            defaultValue: "Disabled",
            allowedValues: ["Disabled", "TrustedOrchestrator", "AllowedHosts"]);

        yield return new(
            "Security:RequireWhatIfForDestructiveStatements",
            GovernancePolicyScope.Security,
            GovernancePolicyClassification.Locked,
            GovernancePolicyValueKind.Boolean,
            "Requires a what-if guard before destructive statements execute.",
            defaultValue: true);

        yield return new(
            "Security:RequireTransactionForMutations",
            GovernancePolicyScope.Security,
            GovernancePolicyClassification.Locked,
            GovernancePolicyValueKind.Boolean,
            "Requires mutation statements to execute under transaction guardrails.",
            defaultValue: true);

        yield return new(
            "Security:MaxSmtpEmailsPerScript",
            GovernancePolicyScope.Connector,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Maximum SMTP messages one script may send.",
            defaultValue: 100,
            minimumValue: 0);

        yield return new(
            "Security:MaxStringResultSize",
            GovernancePolicyScope.Engine,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Long,
            "Maximum string result size materialized by script execution.",
            defaultValue: 104_857_600L,
            minimumValue: 0L);

        yield return new(
            "Connectors:AllowedTypes",
            GovernancePolicyScope.Connector,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.ConnectorTypeList,
            "Connector type tokens that organization policy permits.");

        yield return new(
            "Secrets:Provider",
            GovernancePolicyScope.Secret,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Enum,
            "Secret provider used to resolve named secret references.",
            defaultValue: "Environment",
            allowedValues: ["Environment", "OsSecretStore", "HttpsVault"]);

        yield return new(
            "Audit:RemoteDeliveryRequired",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Locked,
            GovernancePolicyValueKind.Boolean,
            "Requires mutation paths to honor remote audit delivery fail-closed policy.",
            defaultValue: false);

        yield return new(
            "Audit:OutboxMaxBytes",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Long,
            "Maximum local durable audit outbox size before policy-specific backpressure is applied.",
            minimumValue: 0L);

        yield return new(
            "SecurityEvents:CollectorEndpoint",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Locked,
            GovernancePolicyValueKind.String,
            "HTTPS collector endpoint for centrally forwarded security events.");

        yield return new(
            "SecurityEvents:BatchSize",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Maximum security events sent in one collector request.",
            defaultValue: 100,
            minimumValue: 1,
            maximumValue: 10_000);

        yield return new(
            "SecurityEvents:IntervalSeconds",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Seconds between security-event collector delivery sweeps.",
            defaultValue: 30,
            minimumValue: 1,
            maximumValue: 3600);

        yield return new(
            "SecurityEvents:LeaseSeconds",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Seconds a delivery worker owns a claimed security-event batch.",
            defaultValue: 120,
            minimumValue: 10,
            maximumValue: 3600);

        yield return new(
            "SecurityEvents:MinimumForwardedSeverity",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Locked,
            GovernancePolicyValueKind.Enum,
            "Minimum security-event severity forwarded to the central collector.",
            defaultValue: "Warning",
            allowedValues: ["Information", "Warning", "Error", "Critical"]);

        yield return new(
            "SecurityEvents:FailClosedMaxTerminalFailures",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Terminal security-event delivery failures allowed before execution is blocked.",
            minimumValue: 1);

        yield return new(
            "SecurityEvents:FailClosedMaxOldestEventSeconds",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Maximum age of an undelivered security event before execution is blocked.",
            minimumValue: 1);

        yield return new(
            "SecurityEvents:FailClosedMaxPendingEvents",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Integer,
            "Pending security-event backlog allowed before execution is blocked.",
            minimumValue: 1);

        yield return new(
            "SecurityEvents:FailClosedMaxOutboxBytes",
            GovernancePolicyScope.Audit,
            GovernancePolicyClassification.Constrained,
            GovernancePolicyValueKind.Long,
            "Maximum durable security-event outbox size before execution is blocked.",
            minimumValue: 1L);
    }
}

