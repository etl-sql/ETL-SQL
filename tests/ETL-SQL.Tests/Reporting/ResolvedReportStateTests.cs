using System.Text.Json;
using ETL_SQL.Core.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Coverage for the shared, versioned resolved-state envelope and its typed value serialization,
/// including scalar type fidelity.
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
    public void ReportStateValue_FromObject_PreservesStringsAndNativeKinds()
    {
        Assert.Equal(ReportStateValueKind.Number, ReportStateValue.FromObject(2026m).Kind);
        Assert.Equal(ReportStateValueKind.Boolean, ReportStateValue.FromObject(true).Kind);
        foreach (var text in new[] { "2026", "true", "West", "007" })
        {
            var value = ReportStateValue.FromObject(text);
            Assert.Equal(ReportStateValueKind.String, value.Kind);
            Assert.Equal(text, value.StringValue);
        }
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
    [InlineData("{\"parameters\":{\"@é\":\"first\",\"@É\":\"second\"}}")]
    [InlineData("{\"visible\":{\"Panel\":true,\"panel\":false}}")]
    [InlineData("{\"collapsed\":{\"Notes\":true,\"notes\":false}}")]
    [Trait("CompatBreak", "0.22")]
    public void Envelope_TryFromJson_RejectsInvalidClientState(string json)
    {
        Assert.False(ResolvedReportState.TryFromJson(json, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Envelope_ParameterProjectionRetainsTextAndNativeValues()
    {
        var state = ResolvedReportState.FromJson("""{"parameters":{"@code":"001","@year":2026,"@live":true}}""");
        var parameters = state.ToParameterStrings();
        Assert.Equal("001", parameters["@code"]);
        Assert.Equal("2026", parameters["@year"]);
        Assert.Equal("TRUE", parameters["@live"]);
    }

    [Fact]
    [Trait("CompatBreak", "0.22")]
    public void PersistedStateResolvesCaseVariantsInSourceOrder()
    {
        var state = ResolvedReportState.FromJson("""{"parameters":{"@Region":"West","@region":"East","@é":"001","@É":"002"}}""");
        Assert.Equal(2, state.Parameters.Count);
        Assert.Equal("East", state.Parameters["@REGION"].StringValue);
        Assert.Equal("002", state.Parameters["@é"].StringValue);
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
