using System.Text.Json;
using ETL_SQL.Core.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Coverage for the shared, versioned resolved-state envelope and its typed value serialization,
/// including backward-compatible reading of legacy ParametersJson/FiltersJson.
/// </summary>
public class ResolvedReportStateTests
{
    [Fact]
    public void ReportStateValue_SerializesTypedJsonTokens()
    {
        Assert.Equal("2026", JsonSerializer.Serialize(ReportStateValue.FromNumber(2026m)));
        Assert.Equal("true", JsonSerializer.Serialize(ReportStateValue.FromBoolean(true)));
        Assert.Equal("\"West\"", JsonSerializer.Serialize(ReportStateValue.FromString("West")));
        Assert.Equal("null", JsonSerializer.Serialize(ReportStateValue.Null));
    }

    [Fact]
    public void ReportStateValue_RoundTripsThroughJson()
    {
        foreach (var value in new[]
                 {
                     ReportStateValue.FromNumber(-3.5m),
                     ReportStateValue.FromBoolean(false),
                     ReportStateValue.FromString("hello"),
                     ReportStateValue.Null
                 })
        {
            var json = JsonSerializer.Serialize(value);
            var back = JsonSerializer.Deserialize<ReportStateValue>(json);
            Assert.Equal(value, back);
        }
    }

    [Fact]
    public void ReportStateValue_FromLegacyString_RecoversTypes()
    {
        Assert.Equal(ReportStateValueKind.Number, ReportStateValue.FromLegacyString("2026").Kind);
        Assert.Equal(ReportStateValueKind.Boolean, ReportStateValue.FromLegacyString("true").Kind);
        Assert.Equal(ReportStateValueKind.String, ReportStateValue.FromLegacyString("West").Kind);
        // Leading-zero codes must stay strings, not become numbers.
        Assert.Equal(ReportStateValueKind.String, ReportStateValue.FromLegacyString("007").Kind);
    }

    [Fact]
    public void Envelope_RoundTripsWithTypedValuesAndVersion()
    {
        var state = new ResolvedReportState
        {
            ActivePage = "Detail",
            ScriptHash = "abc123"
        };
        state.Parameters["@region"] = ReportStateValue.FromString("West");
        state.Parameters["@year"] = ReportStateValue.FromNumber(2026m);
        state.Collapsed["FilterPanel"] = true;
        state.Visible["DetailTable"] = false;

        var json = state.ToJson();
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"@year\":2026", json);   // typed number, not quoted

        var back = ResolvedReportState.FromJson(json);
        Assert.Equal(ResolvedReportState.CurrentSchemaVersion, back.SchemaVersion);
        Assert.Equal("Detail", back.ActivePage);
        Assert.Equal("abc123", back.ScriptHash);
        Assert.Equal(ReportStateValueKind.Number, back.Parameters["@year"].Kind);
        Assert.Equal(2026m, back.Parameters["@year"].NumberValue);
        Assert.True(back.Collapsed["FilterPanel"]);
        Assert.False(back.Visible["DetailTable"]);
    }

    [Fact]
    public void Envelope_FromJson_ToleratesMalformedInput()
    {
        // A malformed persisted view must never throw — the base report always opens.
        var state = ResolvedReportState.FromJson("{ not valid json ");
        Assert.NotNull(state);
        Assert.Empty(state.Parameters);

        Assert.NotNull(ResolvedReportState.FromJson(null));
        Assert.NotNull(ResolvedReportState.FromJson(""));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":99}")]
    [InlineData("{\"schemaVersion\":1,\"parameters\":{\"@x\":{\"bad\":true}}}")]
    public void Envelope_TryFromJson_RejectsInvalidClientState(string json)
    {
        Assert.False(ResolvedReportState.TryFromJson(json, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Envelope_FromLegacy_ReadsParametersAndFiltersJson()
    {
        var state = ResolvedReportState.FromLegacy(
            parametersJson: "{\"@region\":\"West\",\"@year\":2026}",
            filtersJson: "{\"category\":\"Hardware\"}",
            scriptHash: "hash1");

        Assert.Equal("hash1", state.ScriptHash);
        Assert.Equal(ReportStateValueKind.String, state.Parameters["@region"].Kind);
        Assert.Equal(ReportStateValueKind.Number, state.Parameters["@year"].Kind);
        // A filter key without a leading @ is normalized to a parameter name.
        Assert.True(state.Parameters.ContainsKey("@category"));
        Assert.Equal("Hardware", state.Parameters["@category"].ToCanonicalString());
    }

    [Fact]
    public void Envelope_ComputeScriptHash_IsStableAndDiffersOnChange()
    {
        var a = ResolvedReportState.ComputeScriptHash("CREATE BOOKMARK X AS (PAGE = Main);");
        var b = ResolvedReportState.ComputeScriptHash("CREATE BOOKMARK X AS (PAGE = Main);");
        var c = ResolvedReportState.ComputeScriptHash("CREATE BOOKMARK X AS (PAGE = Detail);");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Envelope_ToParameterStrings_ProjectsCanonicalForms()
    {
        var state = new ResolvedReportState();
        state.Parameters["@year"] = ReportStateValue.FromNumber(2026m);
        state.Parameters["@region"] = ReportStateValue.FromString("West");
        var strings = state.ToParameterStrings();
        Assert.Equal("2026", strings["@year"]);
        Assert.Equal("West", strings["@region"]);
    }
}
