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
/// revision drift. Reading tolerates older payloads and the legacy <c>ParametersJson</c>/<c>FiltersJson</c>
/// shape so existing saved views keep working.
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
    /// Strictly validates a client-supplied envelope. Persisted legacy data is still read through
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
            Normalize(state);
            return state;
        }
        catch (JsonException)
        {
            return new ResolvedReportState();
        }
    }

    private static void Normalize(ResolvedReportState state)
    {
        state.Parameters ??= new(StringComparer.OrdinalIgnoreCase);
        state.Visible ??= new(StringComparer.OrdinalIgnoreCase);
        state.Collapsed ??= new(StringComparer.OrdinalIgnoreCase);
        state.Parameters = new(state.Parameters, StringComparer.OrdinalIgnoreCase);
        state.Visible = new(state.Visible, StringComparer.OrdinalIgnoreCase);
        state.Collapsed = new(state.Collapsed, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds an envelope from the legacy saved-view columns. <paramref name="parametersJson"/> is a
    /// <c>{"@name":"value"}</c> string map; <paramref name="filtersJson"/>, when present, is merged as
    /// additional string parameters. Values are best-effort typed so a legacy view converges on the
    /// same typed shape as a freshly-saved one.
    /// </summary>
    public static ResolvedReportState FromLegacy(string? parametersJson, string? filtersJson, string? scriptHash = null)
    {
        var state = new ResolvedReportState { ScriptHash = scriptHash };
        MergeLegacyMap(state, parametersJson);
        MergeLegacyMap(state, filtersJson);
        return state;
    }

    private static void MergeLegacyMap(ResolvedReportState state, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (map == null) return;
            foreach (var (key, element) in map)
            {
                var name = key.StartsWith('@') ? key : "@" + key;
                state.Parameters[name] = element.ValueKind switch
                {
                    JsonValueKind.Number => ReportStateValue.FromNumber(element.GetDecimal()),
                    JsonValueKind.True => ReportStateValue.FromBoolean(true),
                    JsonValueKind.False => ReportStateValue.FromBoolean(false),
                    JsonValueKind.Null => ReportStateValue.Null,
                    JsonValueKind.String => ReportStateValue.FromLegacyString(element.GetString()),
                    _ => ReportStateValue.FromLegacyString(element.ToString())
                };
            }
        }
        catch (JsonException)
        {
            // Ignore an unreadable legacy blob; the base report still opens.
        }
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
