using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Core.Governance;

public enum ScriptExecutionMode
{
    Interactive,
    Batch,
    Scheduled,
    Remote
}

public enum RemoteExecutionMode
{
    Disabled,
    TrustedOrchestrator,
    AllowedHosts
}

public sealed record OrganizationPolicyDocument
{
    public const string CurrentSchemaVersion = "1.0";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public ConnectorPolicySection Connectors { get; init; } = new();
    public FilesystemPolicySection Filesystem { get; init; } = new();
    public NetworkPolicySection Network { get; init; } = new();
    public ExecutionPolicySection Execution { get; init; } = new();
    public ProcessPolicySection Process { get; init; } = new();
    public RemoteExecutionPolicySection RemoteExecution { get; init; } = new();
    public MutationGuardrailPolicySection MutationGuardrails { get; init; } = new();
    public SecurityEventPolicySection SecurityEvents { get; init; } = new();
    public MetadataGovernancePolicySection Metadata { get; init; } = new();
    public SaasOnboardingAuthorizationPolicySection SaasOnboarding { get; init; } = new();

    public IReadOnlyDictionary<string, object> ToPolicyValues()
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (Connectors.AllowedTypes.Count > 0)
            values["Connectors:AllowedTypes"] = Connectors.AllowedTypes;
        if (Filesystem.ApprovedRoots.Count > 0)
            values["Security:ApprovedSafeZones"] = Filesystem.ApprovedRoots;
        if (Filesystem.AllowedWriteExtensions.Count > 0)
            values["Security:AllowedWriteExtensions"] = Filesystem.AllowedWriteExtensions;
        if (Network.AllowedSchemes.Count > 0)
            values["Security:AllowedSchemes"] = Network.AllowedSchemes;
        if (Network.AllowedPorts.Count > 0)
            values["Security:AllowedPorts"] = Network.AllowedPorts.Select(port => port.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (Execution.MaxParallelDegree.HasValue)
            values["Security:MaxParallelDegree"] = Execution.MaxParallelDegree.Value;
        if (Execution.MaxFileOperationsPerScript.HasValue)
            values["Security:MaxFileOperationsPerScript"] = Execution.MaxFileOperationsPerScript.Value;
        if (Execution.MaxRecursiveNestingDepth.HasValue)
            values["Security:MaxRecursiveNestingDepth"] = Execution.MaxRecursiveNestingDepth.Value;
        if (Execution.MaxSpillBytesPerScript.HasValue)
            values["Security:MaxSpillBytesPerScript"] = Execution.MaxSpillBytesPerScript.Value;
        if (Execution.MaxSmtpEmailsPerScript.HasValue)
            values["Security:MaxSmtpEmailsPerScript"] = Execution.MaxSmtpEmailsPerScript.Value;
        if (Execution.MaxStringResultSize.HasValue)
            values["Security:MaxStringResultSize"] = Execution.MaxStringResultSize.Value;
        if (Process.AllowedDockerImages.Count > 0)
            values["Security:AllowedDockerImages"] = Process.AllowedDockerImages;
        values["Security:AllowedExecutionModes"] = Execution.AllowedModes.Select(value => value.ToString()).ToArray();
        values["Security:RemoteExecutionMode"] = RemoteExecution.Mode.ToString();
        if (RemoteExecution.AllowedHosts.Count > 0)
            values["Security:AllowedHosts"] = RemoteExecution.AllowedHosts;
        values["Security:RequireWhatIfForDestructiveStatements"] = MutationGuardrails.RequireWhatIfForDestructiveStatements;
        values["Security:RequireTransactionForMutations"] = MutationGuardrails.RequireTransactionForMutations;
        values["Audit:RemoteDeliveryRequired"] = MutationGuardrails.RequireRemoteAuditForMutations;
        if (!string.IsNullOrWhiteSpace(SecurityEvents.CollectorEndpoint))
            values["SecurityEvents:CollectorEndpoint"] = SecurityEvents.CollectorEndpoint;
        values["SecurityEvents:BatchSize"] = SecurityEvents.BatchSize;
        values["SecurityEvents:IntervalSeconds"] = SecurityEvents.IntervalSeconds;
        values["SecurityEvents:LeaseSeconds"] = SecurityEvents.LeaseSeconds;
        values["SecurityEvents:MinimumForwardedSeverity"] = SecurityEvents.MinimumForwardedSeverity.ToString();
        if (SecurityEvents.FailClosedMaxTerminalFailures.HasValue)
            values["SecurityEvents:FailClosedMaxTerminalFailures"] = SecurityEvents.FailClosedMaxTerminalFailures.Value;
        if (SecurityEvents.FailClosedMaxOldestEventSeconds.HasValue)
            values["SecurityEvents:FailClosedMaxOldestEventSeconds"] = SecurityEvents.FailClosedMaxOldestEventSeconds.Value;
        if (SecurityEvents.FailClosedMaxPendingEvents.HasValue)
            values["SecurityEvents:FailClosedMaxPendingEvents"] = SecurityEvents.FailClosedMaxPendingEvents.Value;
        if (SecurityEvents.FailClosedMaxOutboxBytes.HasValue)
            values["SecurityEvents:FailClosedMaxOutboxBytes"] = SecurityEvents.FailClosedMaxOutboxBytes.Value;
        if (SaasOnboarding.Enabled)
        {
            values["SaaS:Onboarding:TenantId"] = SaasOnboarding.TenantId ?? string.Empty;
            values["SaaS:Onboarding:OperatorPrincipal"] = SaasOnboarding.OperatorPrincipal ?? string.Empty;
            values["SaaS:Onboarding:AuthorizationReference"] = SaasOnboarding.AuthorizationReference ?? string.Empty;
            values["SaaS:Onboarding:Reason"] = SaasOnboarding.Reason ?? string.Empty;
            values["SaaS:Onboarding:ExpiresUtc"] = SaasOnboarding.ExpiresUtc?.ToString("O") ?? string.Empty;
        }

        return values;
    }
}

/// <summary>
/// One short-lived, signed authorization for the deployment-plane tenant onboarding operation.
/// The CLI tenant value is checked against this policy; it never creates tenant authority.
/// </summary>
public sealed record SaasOnboardingAuthorizationPolicySection
{
    public bool Enabled { get; init; }
    public string? TenantId { get; init; }
    public string? OperatorPrincipal { get; init; }
    public string? AuthorizationReference { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? ExpiresUtc { get; init; }
}

public sealed record MetadataGovernancePolicySection
{
    public IReadOnlyList<OrganizationRequiredTagRule> RequiredTags { get; init; } = [];
}

public sealed record OrganizationRequiredTagRule
{
    public string Tag { get; init; } = string.Empty;
    public IReadOnlyList<string> Scopes { get; init; } = [];
}

public sealed record ConnectorPolicySection
{
    public IReadOnlyList<string> AllowedTypes { get; init; } = Array.Empty<string>();
}

public sealed record FilesystemPolicySection
{
    public IReadOnlyList<string> ApprovedRoots { get; init; } = Array.Empty<string>();

    /// <summary>
    /// File extensions (with or without a leading dot) that script-driven writes may target.
    /// Empty means the authoritative organization policy does not constrain write extensions
    /// beyond the local <see cref="Services.SecurityService"/> file-type rules.
    /// </summary>
    public IReadOnlyList<string> AllowedWriteExtensions { get; init; } = Array.Empty<string>();
}

public sealed record NetworkPolicySection
{
    /// <summary>
    /// URL schemes a connector destination may use (case-insensitive, e.g. <c>https</c>, <c>sftp</c>).
    /// Empty means the authoritative organization policy does not constrain schemes beyond the local
    /// egress guardrails. Enforced only for destinations that carry a scheme (URL-shaped targets and
    /// per-request REST URLs); it does not apply to ADO connection strings that name no scheme.
    /// </summary>
    public IReadOnlyList<string> AllowedSchemes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Destination ports a connector may reach. Empty means the authoritative organization policy
    /// does not constrain ports. Enforced only when the destination carries an explicit or
    /// scheme-implied port; a target with no discernible port is not blocked by this rule.
    /// </summary>
    public IReadOnlyList<int> AllowedPorts { get; init; } = Array.Empty<int>();
}

public sealed record ExecutionPolicySection
{
    public IReadOnlyList<ScriptExecutionMode> AllowedModes { get; init; } =
        new[]
        {
            ScriptExecutionMode.Interactive,
            ScriptExecutionMode.Batch,
            ScriptExecutionMode.Scheduled
        };

    public int? MaxParallelDegree { get; init; }
    public int? MaxFileOperationsPerScript { get; init; }
    public int? MaxRecursiveNestingDepth { get; init; }

    /// <summary>
    /// Maximum total bytes a single script may spill to engine-owned temp/cache storage.
    /// Null means the authoritative organization policy does not bound spill volume.
    /// </summary>
    public long? MaxSpillBytesPerScript { get; init; }

    /// <summary>
    /// Maximum SMTP messages one script may send. Null means the authoritative organization policy
    /// does not bound outbound email.
    /// </summary>
    public int? MaxSmtpEmailsPerScript { get; init; }

    /// <summary>
    /// Maximum bytes a single string result may materialize. Null leaves the local safety limit in
    /// force without imposing an additional organization ceiling.
    /// </summary>
    public long? MaxStringResultSize { get; init; }
}

public sealed record ProcessPolicySection
{
    /// <summary>
    /// Docker image references a script may run via <c>USE DOCKER(...)</c>. An entry may be an exact
    /// <c>repo:tag</c>, a tagless <c>repo</c> (matches any tag), or a <c>prefix/*</c> registry/namespace
    /// wildcard. Empty means the authoritative organization policy does not constrain Docker images.
    /// </summary>
    public IReadOnlyList<string> AllowedDockerImages { get; init; } = Array.Empty<string>();
}

public sealed record RemoteExecutionPolicySection
{
    public RemoteExecutionMode Mode { get; init; } = RemoteExecutionMode.Disabled;
    public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();
}

public sealed record MutationGuardrailPolicySection
{
    public bool RequireWhatIfForDestructiveStatements { get; init; } = true;
    public bool RequireTransactionForMutations { get; init; } = true;
    public bool RequireRemoteAuditForMutations { get; init; }
}

public sealed record SecurityEventPolicySection
{
    public string? CollectorEndpoint { get; init; }
    public int BatchSize { get; init; } = 100;
    public int IntervalSeconds { get; init; } = 30;
    public int LeaseSeconds { get; init; } = 120;
    public SecurityEventSeverity MinimumForwardedSeverity { get; init; } = SecurityEventSeverity.Warning;
    public int? FailClosedMaxTerminalFailures { get; init; }
    public int? FailClosedMaxOldestEventSeconds { get; init; }
    public int? FailClosedMaxPendingEvents { get; init; }
    public long? FailClosedMaxOutboxBytes { get; init; }
}

public sealed record OrganizationPolicyValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors)
{
    public static OrganizationPolicyValidationResult Success { get; } =
        new(true, Array.Empty<string>());
}

public static class OrganizationPolicySchema
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly HashSet<string> SupportedVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        OrganizationPolicyDocument.CurrentSchemaVersion
    };

    public static IReadOnlyCollection<string> SupportedSchemaVersions =>
        new ReadOnlyCollection<string>(SupportedVersions.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray());

    /// <summary>Serializes a document with the same options the client parser uses, so a published
    /// envelope round-trips through <see cref="ParseAndValidateJson"/>.</summary>
    public static string Serialize(OrganizationPolicyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static OrganizationPolicyDocument ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Organization policy document JSON is required.", nameof(json));

        var document = JsonSerializer.Deserialize<OrganizationPolicyDocument>(json, JsonOptions);
        return document ?? throw new JsonException("Organization policy document could not be deserialized.");
    }

    public static OrganizationPolicyValidationResult Validate(OrganizationPolicyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<string>();
        if (!SupportedVersions.Contains(document.SchemaVersion))
            errors.Add($"Unsupported organization policy schema version '{document.SchemaVersion}'.");

        ValidateConnectorTypes(document.Connectors.AllowedTypes, errors);
        ValidateAbsoluteRoots(document.Filesystem.ApprovedRoots, errors);
        ValidateWriteExtensions(document.Filesystem.AllowedWriteExtensions, errors);
        ValidateNetwork(document.Network, errors);
        ValidateDockerImages(document.Process.AllowedDockerImages, errors);
        ValidateExecution(document.Execution, errors);
        ValidateRemoteExecution(document.RemoteExecution, errors);
        ValidateSecurityEvents(document.SecurityEvents, errors);
        ValidateMetadata(document.Metadata, errors);
        ValidateSaasOnboarding(document.SaasOnboarding, errors);

        return errors.Count == 0
            ? OrganizationPolicyValidationResult.Success
            : new OrganizationPolicyValidationResult(false, errors);
    }

    private static void ValidateSaasOnboarding(
        SaasOnboardingAuthorizationPolicySection authorization, List<string> errors)
    {
        var hasDetails = !string.IsNullOrWhiteSpace(authorization.TenantId)
                         || !string.IsNullOrWhiteSpace(authorization.OperatorPrincipal)
                         || !string.IsNullOrWhiteSpace(authorization.AuthorizationReference)
                         || !string.IsNullOrWhiteSpace(authorization.Reason)
                         || authorization.ExpiresUtc.HasValue;
        if (!authorization.Enabled)
        {
            if (hasDetails)
                errors.Add("SaaS onboarding authorization details must be empty when authorization is disabled.");
            return;
        }

        if (!TenantId.TryParse(authorization.TenantId, out _))
            errors.Add("SaaS onboarding tenant id must be a canonical tenant id.");
        if (string.IsNullOrWhiteSpace(authorization.OperatorPrincipal))
            errors.Add("SaaS onboarding authorization must name the platform operator.");
        if (string.IsNullOrWhiteSpace(authorization.AuthorizationReference))
            errors.Add("SaaS onboarding authorization must name its approval or change record.");
        if (string.IsNullOrWhiteSpace(authorization.Reason))
            errors.Add("SaaS onboarding authorization must state its reason.");
        if (!authorization.ExpiresUtc.HasValue || authorization.ExpiresUtc <= DateTimeOffset.UnixEpoch)
            errors.Add("SaaS onboarding authorization must carry a valid expiry.");
    }

    private static void ValidateMetadata(MetadataGovernancePolicySection metadata, List<string> errors)
    {
        var supportedScopes = new HashSet<string>(["REPORT", "DATASET", "COLUMN"], StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in metadata.RequiredTags)
        {
            if (string.IsNullOrWhiteSpace(rule.Tag) || !rule.Tag.StartsWith('@'))
                errors.Add("Metadata required tag names must start with '@'.");
            else if (!seen.Add(rule.Tag))
                errors.Add($"Metadata required tag '{rule.Tag}' is duplicated.");
            if (rule.Scopes.Count == 0)
                errors.Add($"Metadata required tag '{rule.Tag}' must declare at least one scope.");
            foreach (var scope in rule.Scopes.Where(scope => !supportedScopes.Contains(scope)))
                errors.Add($"Metadata required tag '{rule.Tag}' has unsupported scope '{scope}'. Allowed scopes: COLUMN, DATASET, REPORT.");
        }
    }

    public static OrganizationPolicyDocument ParseAndValidateJson(string json)
    {
        var document = ParseJson(json);
        var validation = Validate(document);
        if (!validation.IsValid)
            throw new InvalidOperationException("Invalid organization policy document: " + string.Join("; ", validation.Errors));

        return document;
    }

    private static void ValidateConnectorTypes(IReadOnlyList<string> allowedTypes, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in allowedTypes)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                errors.Add("Connector allowed types cannot contain blank entries.");
                continue;
            }

            if (!seen.Add(type.Trim()))
                errors.Add($"Connector allowed type '{type}' is duplicated.");
        }
    }

    private static void ValidateAbsoluteRoots(IReadOnlyList<string> approvedRoots, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in approvedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                errors.Add("Filesystem approved roots cannot contain blank entries.");
                continue;
            }

            var trimmed = root.Trim();
            if (!Path.IsPathRooted(trimmed))
                errors.Add($"Filesystem approved root '{root}' must be absolute.");
            if (!seen.Add(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                errors.Add($"Filesystem approved root '{root}' is duplicated.");
        }
    }

    private static void ValidateWriteExtensions(IReadOnlyList<string> extensions, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                errors.Add("Filesystem allowed write extensions cannot contain blank entries.");
                continue;
            }

            var normalized = extension.Trim().TrimStart('.');
            if (normalized.Length == 0 || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                errors.Add($"Filesystem allowed write extension '{extension}' is not a valid extension.");
            else if (!seen.Add(normalized))
                errors.Add($"Filesystem allowed write extension '{extension}' is duplicated.");
        }
    }

    private static void ValidateDockerImages(IReadOnlyList<string> images, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in images)
        {
            if (string.IsNullOrWhiteSpace(image))
                errors.Add("Process allowed Docker images cannot contain blank entries.");
            else if (!seen.Add(image.Trim()))
                errors.Add($"Process allowed Docker image '{image}' is duplicated.");
        }
    }

    private static void ValidateNetwork(NetworkPolicySection network, List<string> errors)
    {
        var seenSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scheme in network.AllowedSchemes)
        {
            if (string.IsNullOrWhiteSpace(scheme))
            {
                errors.Add("Network allowed schemes cannot contain blank entries.");
                continue;
            }

            var trimmed = scheme.Trim();
            // A URI scheme is letters/digits/+/-/. starting with a letter (RFC 3986).
            if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^[a-zA-Z][a-zA-Z0-9+.\\-]*$"))
                errors.Add($"Network allowed scheme '{scheme}' is not a valid URI scheme.");
            else if (!seenSchemes.Add(trimmed))
                errors.Add($"Network allowed scheme '{scheme}' is duplicated.");
        }

        var seenPorts = new HashSet<int>();
        foreach (var port in network.AllowedPorts)
        {
            if (port is < 1 or > 65535)
                errors.Add($"Network allowed port '{port}' must be between 1 and 65535.");
            else if (!seenPorts.Add(port))
                errors.Add($"Network allowed port '{port}' is duplicated.");
        }
    }

    private static void ValidateExecution(ExecutionPolicySection execution, List<string> errors)
    {
        if (execution.AllowedModes.Count == 0)
            errors.Add("Execution allowed modes must include at least one mode.");
        if (execution.MaxParallelDegree.HasValue && execution.MaxParallelDegree.Value < 1)
            errors.Add("Execution max parallel degree must be at least 1.");
        if (execution.MaxFileOperationsPerScript.HasValue && execution.MaxFileOperationsPerScript.Value < 0)
            errors.Add("Execution max file operations per script must be zero or greater.");
        if (execution.MaxRecursiveNestingDepth.HasValue && execution.MaxRecursiveNestingDepth.Value < 0)
            errors.Add("Execution max recursive nesting depth must be zero or greater.");
        if (execution.MaxSpillBytesPerScript.HasValue && execution.MaxSpillBytesPerScript.Value < 0)
            errors.Add("Execution max spill bytes per script must be zero or greater.");
        if (execution.MaxSmtpEmailsPerScript.HasValue && execution.MaxSmtpEmailsPerScript.Value < 0)
            errors.Add("Execution max SMTP emails per script must be zero or greater.");
        if (execution.MaxStringResultSize.HasValue && execution.MaxStringResultSize.Value < 0)
            errors.Add("Execution max string result size must be zero or greater.");
    }

    private static void ValidateRemoteExecution(RemoteExecutionPolicySection remote, List<string> errors)
    {
        if (remote.Mode == RemoteExecutionMode.AllowedHosts && remote.AllowedHosts.Count == 0)
            errors.Add("Remote execution mode AllowedHosts requires at least one allowed host.");
        if (remote.Mode == RemoteExecutionMode.Disabled && remote.AllowedHosts.Count > 0)
            errors.Add("Remote execution allowed hosts must be empty when remote execution is Disabled.");
    }

    private static void ValidateSecurityEvents(SecurityEventPolicySection securityEvents, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(securityEvents.CollectorEndpoint)
            && (!Uri.TryCreate(securityEvents.CollectorEndpoint, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(endpoint.UserInfo)))
            errors.Add("Security event collector endpoint must be an absolute HTTPS URI without embedded credentials.");
        if (securityEvents.BatchSize is < 1 or > 10_000)
            errors.Add("Security event batch size must be between 1 and 10000.");
        if (securityEvents.IntervalSeconds is < 1 or > 3600)
            errors.Add("Security event interval seconds must be between 1 and 3600.");
        if (securityEvents.LeaseSeconds is < 10 or > 3600)
            errors.Add("Security event lease seconds must be between 10 and 3600.");
        if (securityEvents.FailClosedMaxTerminalFailures is < 1)
            errors.Add("Security event fail-closed terminal failure limit must be at least 1.");
        if (securityEvents.FailClosedMaxOldestEventSeconds is < 1)
            errors.Add("Security event fail-closed oldest event seconds must be at least 1.");
        if (securityEvents.FailClosedMaxPendingEvents is < 1)
            errors.Add("Security event fail-closed pending event limit must be at least 1.");
        if (securityEvents.FailClosedMaxOutboxBytes is < 1)
            errors.Add("Security event fail-closed outbox byte limit must be at least 1.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
