using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Semantics;

public static class ChartContractSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ChartSpec value) => SerializeContract(value);
    public static string Serialize(ChartDataSet value) => SerializeContract(value);
    public static string Serialize(PlotPlan value) => SerializeContract(value);

    public static ChartSpec DeserializeChartSpec(string json) => Deserialize<ChartSpec>(json);
    public static ChartDataSet DeserializeChartData(string json) => Deserialize<ChartDataSet>(json);
    public static PlotPlan DeserializePlotPlan(string json) => Deserialize<PlotPlan>(json);

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
    internal static void RequireVersion(string schema, int version, string expectedSchema, string contract)
    {
        if (!string.Equals(schema, expectedSchema, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported {contract} schema '{schema}'. Expected '{expectedSchema}'.");
        if (version != ChartContractVersions.Current)
            throw new InvalidDataException($"Unsupported {contract} version {version}. Expected {ChartContractVersions.Current}.");
    }

    internal static void RequireName(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{field} is required.");
    }

    internal static void RequireUnique(IEnumerable<string> values, string field)
    {
        var duplicate = values.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Duplicate {field} '{duplicate.Key}'.");
    }
}
