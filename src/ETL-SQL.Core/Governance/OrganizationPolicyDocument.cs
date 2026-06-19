using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public ExecutionPolicySection Execution { get; init; } = new();
    public RemoteExecutionPolicySection RemoteExecution { get; init; } = new();
    public MutationGuardrailPolicySection MutationGuardrails { get; init; } = new();

    public IReadOnlyDictionary<string, object> ToPolicyValues()
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (Connectors.AllowedTypes.Count > 0)
            values["Connectors:AllowedTypes"] = Connectors.AllowedTypes;
        if (Filesystem.ApprovedRoots.Count > 0)
            values["Security:ApprovedSafeZones"] = Filesystem.ApprovedRoots;
        if (Execution.MaxParallelDegree.HasValue)
            values["Security:MaxParallelDegree"] = Execution.MaxParallelDegree.Value;
        if (Execution.MaxFileOperationsPerScript.HasValue)
            values["Security:MaxFileOperationsPerScript"] = Execution.MaxFileOperationsPerScript.Value;
        if (RemoteExecution.AllowedHosts.Count > 0)
            values["Security:AllowedHosts"] = RemoteExecution.AllowedHosts;
        if (MutationGuardrails.RequireRemoteAuditForMutations)
            values["Audit:RemoteDeliveryRequired"] = true;

        return values;
    }
}

public sealed record ConnectorPolicySection
{
    public IReadOnlyList<string> AllowedTypes { get; init; } = Array.Empty<string>();
}

public sealed record FilesystemPolicySection
{
    public IReadOnlyList<string> ApprovedRoots { get; init; } = Array.Empty<string>();
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
        ValidateExecution(document.Execution, errors);
        ValidateRemoteExecution(document.RemoteExecution, errors);

        return errors.Count == 0
            ? OrganizationPolicyValidationResult.Success
            : new OrganizationPolicyValidationResult(false, errors);
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

    private static void ValidateExecution(ExecutionPolicySection execution, List<string> errors)
    {
        if (execution.AllowedModes.Count == 0)
            errors.Add("Execution allowed modes must include at least one mode.");
        if (execution.MaxParallelDegree.HasValue && execution.MaxParallelDegree.Value < 1)
            errors.Add("Execution max parallel degree must be at least 1.");
        if (execution.MaxFileOperationsPerScript.HasValue && execution.MaxFileOperationsPerScript.Value < 0)
            errors.Add("Execution max file operations per script must be zero or greater.");
    }

    private static void ValidateRemoteExecution(RemoteExecutionPolicySection remote, List<string> errors)
    {
        if (remote.Mode == RemoteExecutionMode.AllowedHosts && remote.AllowedHosts.Count == 0)
            errors.Add("Remote execution mode AllowedHosts requires at least one allowed host.");
        if (remote.Mode == RemoteExecutionMode.Disabled && remote.AllowedHosts.Count > 0)
            errors.Add("Remote execution allowed hosts must be empty when remote execution is Disabled.");
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
