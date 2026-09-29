using ETL_SQL.Portal.Services;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// Studio's inspector edits a visual's <c>TOOLTIP</c>. The designer carries the whole clause, so each
/// form reads back as the clause the author wrote, can be changed and removed, and survives an
/// unrelated edit byte for byte.
/// </summary>
public sealed class TooltipDesignerRoundTripTests
{
    private const string Header = """
        SELECT Month, Region, Revenue, Share INTO #sales FROM source.sales;
        CREATE VISUAL Detail AS TABLE (SOURCE = #sales);
        CREATE CONTAINER Box AS BOX (LAYOUT (STRUCTURE = 'A', MAP ('A' = Detail)));

        """;

    private static string Script(string tooltip) => Header + $"""
        CREATE VISUAL ByMonth AS BAR (
            SOURCE = #sales,
            MAPPINGS (X = Month, Y = Revenue, TOOLTIP = Region){(tooltip.Length == 0 ? "" : ",\n    " + tooltip)}
        );
        CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A B', MAP ('A' = ByMonth, 'B' = Detail)));
        """;

    private static (DesignerAnalysisService Analysis, ETL_SQL.Portal.Models.DesignerStateDto State) Parse(string script)
    {
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        return (analysis, parsed.DesignState);
    }

    private static ETL_SQL.Portal.Models.DesignerVisualDto ByMonth(ETL_SQL.Portal.Models.DesignerStateDto state) =>
        Assert.Single(state.Pages.SelectMany(page => page.Visuals), visual => visual.Name == "ByMonth");

    public static TheoryData<string> Forms => new()
    {
        "TOOLTIP = 'Revenue for the month'",
        "TOOLTIP = Box",
        "TOOLTIP ('**Month detail**', VISUALS (Detail))",
        "TOOLTIP (FIELDS (Region, Revenue FORMAT 'C0', Share FORMAT 'P1'))",
        "TOOLTIP ('**Mix**', FIELDS (Region))",
    };

    [Theory]
    [MemberData(nameof(Forms))]
    public void TheDesignerReadsTheClauseAsWritten(string tooltip)
    {
        var (_, state) = Parse(Script(tooltip));

        Assert.Equal(tooltip, ByMonth(state).Options["tooltip"]);
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void AnUnrelatedEditLeavesTheClauseAlone(string tooltip)
    {
        var script = Script(tooltip);
        var (_, state) = Parse(script);
        ByMonth(state).Options["LEGEND"] = "OFF";

        var patched = new DesignerScriptPatcher().Patch(script, state);

        Assert.Contains("LEGEND", patched, StringComparison.Ordinal);
        Assert.Contains(tooltip, patched, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDesignerAddsChangesAndRemovesATooltip()
    {
        var script = Script("");
        var (_, state) = Parse(script);
        Assert.False(ByMonth(state).Options.ContainsKey("tooltip"));

        ByMonth(state).Options["tooltip"] = "TOOLTIP (FIELDS (Region, Revenue FORMAT 'C0'))";
        var added = new DesignerScriptPatcher().Patch(script, state);
        Assert.Contains("TOOLTIP (FIELDS (Region, Revenue FORMAT 'C0'))", added, StringComparison.Ordinal);

        var (_, again) = Parse(added);
        ByMonth(again).Options["tooltip"] = "TOOLTIP = 'Hover text'";
        var changed = new DesignerScriptPatcher().Patch(added, again);
        Assert.Contains("TOOLTIP = 'Hover text'", changed, StringComparison.Ordinal);
        Assert.DoesNotContain("FIELDS", changed, StringComparison.Ordinal);

        var (_, third) = Parse(changed);
        ByMonth(third).Options.Remove("tooltip");
        var removed = new DesignerScriptPatcher().Patch(changed, third);
        Assert.DoesNotContain("Hover text", removed, StringComparison.Ordinal);
        // The TOOLTIP mapping role is a different thing and stays.
        Assert.Contains("TOOLTIP = Region", removed, StringComparison.Ordinal);
        Parse(removed);
    }
}
