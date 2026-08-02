using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Governance;

public sealed record WorkspacePolicyDocument
{
    public const string CurrentSchemaVersion = "1.0";
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public IReadOnlyList<WorkspaceRequiredTagRule> RequiredTags { get; init; } = [];
    public IReadOnlyList<WorkspaceProtectedDataPattern> ProtectedDataPatterns { get; init; } = [];
    public WorkspaceQualityThresholds QualityThresholds { get; init; } = new();
    public WorkspaceStewardshipWeights StewardshipWeights { get; init; } = new();
}

public sealed record WorkspaceRequiredTagRule
{
    public string Tag { get; init; } = string.Empty;
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public IReadOnlyList<string> Exclude { get; init; } = [];
}

public sealed record WorkspaceProtectedDataPattern
{
    public string Name { get; init; } = string.Empty;
    public string Regex { get; init; } = string.Empty;
    public string Classification { get; init; } = string.Empty;
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public IReadOnlyList<string> Exclude { get; init; } = [];
}

public sealed record WorkspaceQualityThresholds
{
    public WorkspaceQualityThreshold Warning { get; init; } = new();
    public WorkspaceQualityThreshold Failure { get; init; } = new();
}

public sealed record WorkspaceQualityThreshold
{
    public decimal? WarnPercent { get; init; }
    public decimal? QuarantinePercent { get; init; }
    public decimal? NullPercent { get; init; }
    public int? FreshnessMinutes { get; init; }
}

public sealed record WorkspaceStewardshipWeights
{
    public decimal RequiredTagCompleteness { get; init; } = 1m;
    public decimal ProtectedDataCoverage { get; init; } = 1m;
    public decimal QualityRuleCoverage { get; init; } = 1m;
}

public sealed record WorkspacePolicyDiagnostic(string Path, int Line, int Column, string Message);

public sealed record WorkspacePolicyLoadResult(
    string Path,
    WorkspacePolicyDocument? Policy,
    IReadOnlyList<WorkspacePolicyDiagnostic> Diagnostics)
{
    public bool IsValid => Policy != null && Diagnostics.Count == 0;
}

public static class WorkspacePolicyLoader
{
    public const string FileName = "etlsql-policy.json";
    private static readonly HashSet<string> AllowedScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SCRIPT", "JOB", "TABLE", "COLUMN", "DATASET", "REPORT"
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string? Find(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return null;
    }

    public static WorkspacePolicyLoadResult Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var json = File.ReadAllText(fullPath);
        WorkspacePolicyDocument? policy;
        try
        {
            policy = JsonSerializer.Deserialize<WorkspacePolicyDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new(fullPath, null,
            [
                new(fullPath, checked((int)(ex.LineNumber ?? 0) + 1),
                    checked((int)(ex.BytePositionInLine ?? 0) + 1), ex.Message)
            ]);
        }

        if (policy == null)
            return new(fullPath, null, [new(fullPath, 1, 1, "Policy document is empty.")]);

        var diagnostics = Validate(fullPath, json, policy);
        return new(fullPath, diagnostics.Count == 0 ? policy : null, diagnostics);
    }

    private static List<WorkspacePolicyDiagnostic> Validate(
        string path, string json, WorkspacePolicyDocument policy)
    {
        var diagnostics = new List<WorkspacePolicyDiagnostic>();
        AddIf(policy.SchemaVersion != WorkspacePolicyDocument.CurrentSchemaVersion, "schemaVersion",
            $"Unsupported workspace policy schema version '{policy.SchemaVersion}'. Expected '{WorkspacePolicyDocument.CurrentSchemaVersion}'.");

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in policy.RequiredTags)
        {
            AddIf(string.IsNullOrWhiteSpace(rule.Tag) || !rule.Tag.StartsWith('@'), "tag",
                "Required tag names must start with '@'.");
            AddIf(!string.IsNullOrWhiteSpace(rule.Tag) && !tags.Add(rule.Tag), "tag",
                $"Required tag '{rule.Tag}' is duplicated.");
            ValidateScopes(rule.Scopes, "scopes", diagnostics, path, json);
            AddIf(rule.Exclude.Any(string.IsNullOrWhiteSpace), "exclude", "Exclusion patterns cannot be empty.");
        }

        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in policy.ProtectedDataPatterns)
        {
            AddIf(string.IsNullOrWhiteSpace(pattern.Name), "name", "Protected-data pattern name is required.");
            AddIf(!string.IsNullOrWhiteSpace(pattern.Name) && !patterns.Add(pattern.Name), "name",
                $"Protected-data pattern '{pattern.Name}' is duplicated.");
            AddIf(string.IsNullOrWhiteSpace(pattern.Classification), "classification",
                "Protected-data classification is required.");
            ValidateScopes(pattern.Scopes, "scopes", diagnostics, path, json);
            try
            {
                _ = new Regex(pattern.Regex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                Add("regex", $"Invalid protected-data regex '{pattern.Name}': {ex.Message}");
            }
        }

        ValidateThreshold("warnPercent", policy.QualityThresholds.Warning.WarnPercent, policy.QualityThresholds.Failure.WarnPercent);
        ValidateThreshold("quarantinePercent", policy.QualityThresholds.Warning.QuarantinePercent, policy.QualityThresholds.Failure.QuarantinePercent);
        ValidateThreshold("nullPercent", policy.QualityThresholds.Warning.NullPercent, policy.QualityThresholds.Failure.NullPercent);
        ValidateMinutes("freshnessMinutes", policy.QualityThresholds.Warning.FreshnessMinutes, policy.QualityThresholds.Failure.FreshnessMinutes);
        AddIf(policy.StewardshipWeights.RequiredTagCompleteness < 0, "requiredTagCompleteness", "Stewardship weights cannot be negative.");
        AddIf(policy.StewardshipWeights.ProtectedDataCoverage < 0, "protectedDataCoverage", "Stewardship weights cannot be negative.");
        AddIf(policy.StewardshipWeights.QualityRuleCoverage < 0, "qualityRuleCoverage", "Stewardship weights cannot be negative.");
        return diagnostics;

        void ValidateThreshold(string property, decimal? warning, decimal? failure)
        {
            AddIf(warning is < 0 or > 1 || failure is < 0 or > 1, property,
                $"{property} must be between 0 and 1.");
            AddIf(warning.HasValue && failure.HasValue && warning.Value > failure.Value, property,
                $"Warning {property} cannot exceed failure {property}.");
        }

        void ValidateMinutes(string property, int? warning, int? failure)
        {
            AddIf(warning < 0 || failure < 0, property, $"{property} cannot be negative.");
            // Smaller freshness age is stricter, so the warning boundary must not be later than failure.
            AddIf(warning.HasValue && failure.HasValue && warning.Value > failure.Value, property,
                $"Warning {property} cannot exceed failure {property}.");
        }

        void AddIf(bool condition, string property, string message)
        {
            if (condition) Add(property, message);
        }

        void Add(string property, string message)
        {
            var (line, column) = Locate(json, property);
            diagnostics.Add(new(path, line, column, message));
        }
    }

    private static void ValidateScopes(
        IReadOnlyList<string> scopes,
        string property,
        List<WorkspacePolicyDiagnostic> diagnostics,
        string path,
        string json)
    {
        if (scopes.Count == 0)
        {
            var location = Locate(json, property);
            diagnostics.Add(new(path, location.Line, location.Column, "At least one scope is required."));
            return;
        }
        foreach (var scope in scopes.Where(scope => !AllowedScopes.Contains(scope)))
        {
            var location = Locate(json, property);
            diagnostics.Add(new(path, location.Line, location.Column,
                $"Unsupported scope '{scope}'. Allowed scopes: {string.Join(", ", AllowedScopes.Order())}."));
        }
    }

    private static (int Line, int Column) Locate(string json, string property)
    {
        var needle = $"\"{property}\"";
        var index = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return (1, 1);
        var before = json[..index];
        var line = before.Count(c => c == '\n') + 1;
        var lastNewline = before.LastIndexOf('\n');
        return (line, index - lastNewline);
    }
}
