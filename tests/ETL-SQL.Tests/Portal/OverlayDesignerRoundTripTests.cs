using ETL_SQL.Portal.Services;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// Studio's inspector edits a visual's <c>OVERLAYS</c>. The designer carries the clause in the
/// formatter's own shape, so every overlay the dialect has reads back as written and survives an
/// unrelated edit.
/// </summary>
public sealed class OverlayDesignerRoundTripTests
{
    private static string Script(string overlays) => $"""
        SELECT Month, Revenue, Forecast, Low, High, RunningRevenue INTO #sales FROM source.sales;
        CREATE VISUAL Trend AS LINE (
            SOURCE = #sales,
            MAPPINGS (X = Month, Y = Revenue),
            {overlays}
        );
        CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Trend)));
        """;

    private static (DesignerAnalysisService Analysis, ETL_SQL.Portal.Models.DesignerStateDto State) Parse(string script)
    {
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        return (analysis, parsed.DesignState);
    }

    private static ETL_SQL.Portal.Models.DesignerVisualDto Trend(ETL_SQL.Portal.Models.DesignerStateDto state) =>
        Assert.Single(state.Pages.SelectMany(page => page.Visuals), visual => visual.Name == "Trend");

    public static TheoryData<string> Forms => new()
    {
        "OVERLAYS (GOAL(100) AS DASHED)",
        "OVERLAYS (GOAL(100) AS SOLID WITH (COLOR = '#dc2626', LABEL = 'Target'))",
        "OVERLAYS (AVERAGE AS DOTTED WITH (LABEL = 'Mean'))",
        "OVERLAYS (MOVING_AVG(3) AS SOLID)",
        "OVERLAYS (LINEAR AS DASHED, POLYNOMIAL(2) AS DOTTED)",
        "OVERLAYS (REFERENCE_LINE (VALUE = 50, LABEL = 'Floor', STYLE = SOLID, COLOR = '#16a34a'))",
        "OVERLAYS (REFERENCE_BAND (LOW = 10, HIGH = 20, COLOR = '#fde68a', LABEL = 'Normal'))",
        "OVERLAYS (FORECAST(Forecast) AS DASHED WITH (CONFIDENCE_LOW = Low, CONFIDENCE_HIGH = High))",
        "OVERLAYS (RUNNING_TOTAL(RunningRevenue) AS SOLID)",
        "OVERLAYS (ANNOTATIONS (POINT (TYPE = MAX, LABEL = 'Peak')))",
    };

    [Theory]
    [MemberData(nameof(Forms))]
    public void TheDesignerReadsTheClauseAsWritten(string overlays)
    {
        var (_, state) = Parse(Script(overlays));

        Assert.Equal(overlays, Trend(state).Options["overlays"]);
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void AnUnrelatedEditLeavesTheClauseAlone(string overlays)
    {
        var script = Script(overlays);
        var (_, state) = Parse(script);
        Trend(state).Options["LEGEND"] = "OFF";

        var patched = new DesignerScriptPatcher().Patch(script, state);

        Assert.Contains("LEGEND", patched, StringComparison.Ordinal);
        Assert.Contains(overlays, patched, StringComparison.Ordinal);
        Parse(patched);
    }
}
