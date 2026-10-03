using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Semantics;

public static class ChartContractSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ChartSpec value) => SerializeContract(value);
    public static string Serialize(ChartDataSet value) => SerializeContract(value);
    public static string Serialize(PlotPlan value) => SerializeContract(value);

    public static ChartSpec DeserializeChartSpec(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var value = Deserialize<ChartSpec>(MigrateLegacy(json,
            ChartContractVersions.LegacyChartSpecSchema, ChartContractVersions.ChartSpecSchema, ChartContractVersions.ChartSpecCurrent, out var legacy));
        if (!legacy || !Enabled(value.Theme.Tokens, "STACKED")) return value;
        value = value with
        {
            Layers = value.Layers.Select(layer => layer with
            {
                Bindings = layer.Bindings.Select(binding => binding.Channel is FieldChannel.Y or FieldChannel.Y2
                    ? binding with { Stack = StackMode.Zero }
                    : binding).ToImmutableArray()
            }).ToImmutableArray(),
            Theme = value.Theme with
            {
                Tokens = value.Theme.Tokens.Where(token => !token.Name.Equals("STACKED", StringComparison.OrdinalIgnoreCase)).ToImmutableArray()
            }
        };
        value.Validate();
        return value;
    }
    public static ChartDataSet DeserializeChartData(string json) => Deserialize<ChartDataSet>(json);
    public static PlotPlan DeserializePlotPlan(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var value = Deserialize<PlotPlan>(MigrateLegacy(json,
            ChartContractVersions.LegacyPlotPlanSchema, ChartContractVersions.PlotPlanSchema, ChartContractVersions.PlotPlanCurrent, out var legacy));
        if (legacy && Enabled(value.Style, "STACKED"))
            throw new InvalidDataException("A version-one PlotPlan using global STACKED geometry must be regenerated from its ChartSpec; it cannot be migrated without silently changing resolved geometry.");
        return value;
    }

    private static bool Enabled(ImmutableArray<StyleToken> tokens, string name)
    {
        if (tokens.IsDefaultOrEmpty) return false;
        var value = tokens.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        return value is not null && !value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0";
    }

    private static string MigrateLegacy(string json, string legacySchema, string currentSchema, int currentVersion, out bool legacy)
    {
        legacy = false;
        if (string.IsNullOrWhiteSpace(json)) return json;
        // COMPAT_BREAK: 0.20.0 — migrate only an exact version-one envelope, preserving strings in the payload.
        if (JsonNode.Parse(json) is not JsonObject envelope ||
            envelope["schema"] is not JsonValue schema || !schema.TryGetValue<string>(out var schemaText) || schemaText != legacySchema ||
            envelope["version"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number != 1) return json;
        legacy = true;
        envelope["schema"] = currentSchema;
        envelope["version"] = currentVersion;
        return envelope.ToJsonString();
    }

    private static string SerializeContract<T>(T value) where T : IVersionedChartContract
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        return JsonSerializer.Serialize(value, Options);
    }

    private static T Deserialize<T>(string json) where T : IVersionedChartContract
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Contract JSON is required.", nameof(json));
        var value = JsonSerializer.Deserialize<T>(json, Options)
                    ?? throw new JsonException($"The {typeof(T).Name} payload was null.");
        value.Validate();
        return value;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

internal static class ChartContractValidation
{
    internal static void RequireVersion(string schema, int version, string expectedSchema, int expectedVersion, string contract)
    {
        if (!string.Equals(schema, expectedSchema, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported {contract} schema '{schema}'. Expected '{expectedSchema}'.");
        if (version != expectedVersion)
            throw new InvalidDataException($"Unsupported {contract} version {version}. Expected {expectedVersion}.");
    }

    internal static void RequireName(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{field} is required.");
    }

    internal static void RequireUnique(IEnumerable<string> values, string field, IEqualityComparer<string>? comparer = null)
    {
        var duplicate = values.GroupBy(value => value, comparer ?? StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Duplicate {field} '{duplicate.Key}'.");
    }
}
