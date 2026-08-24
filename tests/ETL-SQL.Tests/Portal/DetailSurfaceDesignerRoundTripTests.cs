using ETL_SQL.Portal.Services;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// The Report Builder does not author detail surfaces, so the requirement on it is the
/// opposite one: an unrelated edit must not destroy a `TOOLTIP` clause the author wrote by
/// hand. The patcher rewrites only the clauses it owns, so a tooltip survives byte-for-byte —
/// these tests pin that, because the formatter had exactly this defect and dropped the clause
/// silently.
/// </summary>
public sealed class DetailSurfaceDesignerRoundTripTests
{
    private const string Script = """
        SELECT Month, Region, Revenue INTO #sales FROM source.sales;
        CREATE VISUAL MonthDetail AS BAR (
            TITLE = 'Regional Detail',
            SOURCE = (SELECT Region, Revenue FROM #sales WHERE Month = @hover_value),
            MAPPINGS (X = Region, Y = Revenue)
        );
        CREATE CONTAINER TooltipBox AS BOX (
            LAYOUT (STRUCTURE = 'A', MAP ('A' = MonthDetail))
        );
        CREATE VISUAL BarWithTooltip AS BAR (
            TITLE = 'Revenue by Month',
            SOURCE = (SELECT Month, SUM(Revenue) AS Revenue FROM #sales GROUP BY Month),
            MAPPINGS (X = Month, Y = Revenue),
            TOOLTIP = TooltipBox
        );
        CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = BarWithTooltip)));
        """;

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void ReferencedContainerTooltip_SurvivesDesignerParseAndPatch()
    {
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(Script, 100);
        Assert.Null(parsed.Error);

        var patched = new DesignerScriptPatcher().Patch(Script, parsed.DesignState);

        Assert.Contains("TOOLTIP = TooltipBox", patched);
        Assert.Null(analysis.Parse(patched, 100).Error);
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void InlineVisualsTooltip_SurvivesDesignerParseAndPatch()
    {
        const string inline = """
            SELECT Month, Revenue INTO #sales FROM source.sales;
            CREATE VISUAL MonthDetail AS BAR (
                SOURCE = #sales,
                MAPPINGS (X = Month, Y = Revenue)
            );
            CREATE VISUAL InlineBar AS BAR (
                SOURCE = #sales,
                MAPPINGS (X = Month, Y = Revenue),
                TOOLTIP ('**Detail**', VISUALS (MonthDetail))
            );
            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = InlineBar)));
            """;

        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(inline, 100);
        Assert.Null(parsed.Error);

        var patched = new DesignerScriptPatcher().Patch(inline, parsed.DesignState);

        // The VISUALS list is what makes this a popover; losing it would silently downgrade
        // the surface to a text tooltip.
        Assert.Contains("VISUALS (MonthDetail)", patched);
        Assert.Null(analysis.Parse(patched, 100).Error);
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void EditingAnUnrelatedClause_LeavesTheTooltipIntact()
    {
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(Script, 100);
        Assert.Null(parsed.Error);

        // Retitle the trigger visual — an edit the designer genuinely owns. The DTO is
        // init-only, so the edit is expressed as a replacement in the page's visual list.
        var page = Assert.Single(parsed.DesignState.Pages);
        var index = page.Visuals.FindIndex(v => v.Name == "BarWithTooltip");
        page.Visuals[index] = page.Visuals[index] with { Title = "Revenue by Month (revised)" };

        var patched = new DesignerScriptPatcher().Patch(Script, parsed.DesignState);

        Assert.Contains("Revenue by Month (revised)", patched);
        Assert.Contains("TOOLTIP = TooltipBox", patched);
        Assert.Null(analysis.Parse(patched, 100).Error);
    }
}
