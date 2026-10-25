using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Reporting;

/// <summary>
/// The versioned, serializable resolved-state envelope shared by author bookmarks and Portal saved
/// views. It captures typed parameter values, the active page, and named-object VISIBLE/COLLAPSED
/// state, together with a schema version and (for saved views) the report script hash used to detect
/// revision drift.
/// </summary>
public sealed class ResolvedReportState
{
    /// <summary>Current envelope schema version. Bump when the shape changes incompatibly.</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>SHA-256 of the report script at capture time (saved views only; null for author bookmarks).</summary>
    [JsonPropertyName("scriptHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScriptHash { get; set; }

    /// <summary>Optional report revision identifier (reserved for future revisioned catalogs).</summary>
    [JsonPropertyName("reportRevision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReportRevision { get; set; }

    /// <summary>The page that should be active when the state is applied.</summary>
    [JsonPropertyName("activePage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActivePage { get; set; }

    /// <summary>Typed parameter assignments, keyed by <c>@name</c>.</summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, ReportStateValue> Parameters { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Named-object visibility state (object name → visible).</summary>
    [JsonPropertyName("visible")]
    public Dictionary<string, bool> Visible { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Named-object collapsed state (object name → collapsed).</summary>
    [JsonPropertyName("collapsed")]
    public Dictionary<string, bool> Collapsed { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// Strictly validates a client-supplied envelope. Persisted envelopes are read through
    /// <see cref="FromJson"/>, but new writes must be an object using the current schema and scalar
    /// parameter values so corrupt or forward-incompatible state is never stored silently.
    /// </summary>
    public static bool TryFromJson(string? json, out ResolvedReportState state, out string? error)
    {
        state = new ResolvedReportState();
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "State is required.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "State must be a JSON object.";
                return false;
            }

            var parsed = JsonSerializer.Deserialize<ResolvedReportState>(json, SerializerOptions);
            if (parsed is null)
            {
                error = "State could not be read.";
                return false;
            }
            if (parsed.SchemaVersion != CurrentSchemaVersion)
            {
                error = $"Unsupported report-state schema version '{parsed.SchemaVersion}'.";
                return false;
            }

            Normalize(parsed);
            state = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Parses an envelope from JSON. Unknown/older schema versions are read best-effort. Returns an
    /// empty envelope for null/blank input rather than throwing, so a malformed persisted view can
    /// never prevent a report from opening.
    /// </summary>
    public static ResolvedReportState FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ResolvedReportState();
        try
        {
            var state = JsonSerializer.Deserialize<ResolvedReportState>(json, SerializerOptions);
            if (state == null) return new ResolvedReportState();
            Normalize(state, allowCaseVariants: true);
            return state;
        }
        catch (JsonException)
        {
            return new ResolvedReportState();
        }
    }

    private static void Normalize(ResolvedReportState state, bool allowCaseVariants = false)
    {
        state.Parameters = NormalizeNames(state.Parameters, allowCaseVariants);
        state.Visible = NormalizeNames(state.Visible, allowCaseVariants);
        state.Collapsed = NormalizeNames(state.Collapsed, allowCaseVariants);
    }

    private static Dictionary<string, T> NormalizeNames<T>(Dictionary<string, T>? values, bool allowCaseVariants)
    {
        var normalized = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (values == null) return normalized;
        foreach (var (name, value) in values)
        {
            // COMPAT_BREAK: 0.22 — new envelopes reject ambiguous names; migrated maps retain
            // their original assignment order and resolve with the engine's ordinal comparison.
            if (allowCaseVariants) normalized[name] = value;
            else if (!normalized.TryAdd(name, value))
                throw new JsonException("Report-state names must be unique ignoring case.");
        }
        return normalized;
    }

    /// <summary>Projects the typed parameters to the string dictionary the runtime/engine consumes.</summary>
    public Dictionary<string, string> ToParameterStrings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Parameters)
            result[key] = value.ToCanonicalString();
        return result;
    }

    /// <summary>Computes the canonical SHA-256 hash of a report script, used for saved-view drift detection.</summary>
    public static string ComputeScriptHash(string? scriptSource)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(scriptSource ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public ResolvedReportState Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        ScriptHash = ScriptHash,
        ReportRevision = ReportRevision,
        ActivePage = ActivePage,
        Parameters = new(Parameters, StringComparer.OrdinalIgnoreCase),
        Visible = new(Visible, StringComparer.OrdinalIgnoreCase),
        Collapsed = new(Collapsed, StringComparer.OrdinalIgnoreCase)
    };
}

/// <summary>
/// The outcome of reconciling a persisted envelope against the current report manifest. Unknown or
/// deleted references are dropped with a warning rather than blocking the report from opening.
/// </summary>
public sealed class ReportStateReconciliation
{
    public ResolvedReportState State { get; init; } = new();
    public List<string> Warnings { get; } = new();
    public bool HasDrift { get; set; }

    public bool HasWarnings => Warnings.Count > 0;
}
