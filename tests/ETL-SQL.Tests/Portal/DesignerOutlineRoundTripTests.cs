using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Xunit;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// The two writes Studio's document outline makes, against the real parse → patch pair rather than
/// the sandbox's approximation of it.
///
/// <para>Both are worth pinning here because both are indirect. "Move up" never edits an ordering:
/// it swaps two tiles' grid coordinates and lets the patcher regenerate <c>STRUCTURE</c> from them,
/// so the claim being tested is that a coordinate swap really does come back out as a rearranged
/// layout. "Hide" writes <c>VISIBLE</c>, which the parser accepts both as a visual property and as
/// an <c>OPTIONS</c> entry — the outline writes the second, and the thing that matters is that a
/// reparse still reports it, because the panel decides which icon to draw from what it reads back.
/// </para>
///
/// <para>The patcher returns the <em>original</em> script when the patched one does not parse, so a
/// broken edit here presents as "nothing happened" rather than as an error. Every assertion below
/// therefore checks the script actually changed, not merely that the call returned something.</para>
/// </summary>
public class DesignerOutlineRoundTripTests
{
    private readonly DesignerAnalysisService _analysis = new();
    private readonly DesignerScriptPatcher _patcher = new();

    private const string TwoColumnDashboard = """
        CREATE CONNECTION corp AS MOCKDB();

        SELECT region, SUM(total) AS revenue INTO #by_region FROM corp.orders GROUP BY region;

        CREATE VISUAL RevenueCard AS CARD (
            SOURCE = #by_region,
            TITLE = 'Total Revenue',
            MAPPINGS (VALUE = revenue)
        );

        CREATE VISUAL SalesByRegion AS BAR (
            SOURCE = #by_region,
            TITLE = 'Sales by Region',
            MAPPINGS (X = region, Y = revenue)
        );

        CREATE PAGE [Executive Overview] AS DASHBOARD (
            LAYOUT (STRUCTURE = 'A B', MAP ('A' = RevenueCard, 'B' = SalesByRegion))
        );
        """;

    private DesignerStateDto ParseState(string script)
    {
        var parsed = _analysis.Parse(script, 500);
        Assert.Null(parsed.Error);
        return parsed.DesignState;
    }

    private static DesignerVisualDto Visual(DesignerStateDto state, string name) =>
        state.Pages.SelectMany(page => page.Visuals)
            .Single(visual => visual.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Replaces one page's visuals, which is all either outline write ever touches.</summary>
    private static DesignerStateDto WithVisuals(DesignerStateDto state, List<DesignerVisualDto> visuals) =>
        state with { Pages = [state.Pages[0] with { Visuals = visuals }] };

    [Fact]
    public void SwappingTwoVisualsGridPlacement_RearrangesTheLayoutAndSurvivesAReparse()
    {
        var state = ParseState(TwoColumnDashboard);
        var card = Visual(state, "RevenueCard");
        var bar = Visual(state, "SalesByRegion");

        // Side by side, so the outline shows them in one row band and "move later" is a real move.
        Assert.Equal(card.GridRow, bar.GridRow);
        Assert.True(card.GridCol < bar.GridCol);

        // Exactly what the panel does: trade placement, spans included, and change nothing else.
        var swappedCard = card with
        {
            GridCol = bar.GridCol,
            GridRow = bar.GridRow,
            GridColSpan = bar.GridColSpan,
            GridRowSpan = bar.GridRowSpan,
        };
        var swappedBar = bar with
        {
            GridCol = card.GridCol,
            GridRow = card.GridRow,
            GridColSpan = card.GridColSpan,
            GridRowSpan = card.GridRowSpan,
        };

        var patched = _patcher.Patch(TwoColumnDashboard, WithVisuals(state, [swappedCard, swappedBar]));

        Assert.NotEqual(TwoColumnDashboard, patched);
        var reparsed = ParseState(patched);
        var movedCard = Visual(reparsed, "RevenueCard");
        var movedBar = Visual(reparsed, "SalesByRegion");
        Assert.True(movedBar.GridCol < movedCard.GridCol);
        Assert.Equal(movedCard.GridRow, movedBar.GridRow);

        // The statements themselves are untouched: a reorder is a layout edit, not a rewrite of the
        // visuals, and an author's hand-written CREATE VISUAL body has to come through byte for byte.
        Assert.Contains("TITLE = 'Total Revenue'", patched, StringComparison.Ordinal);
        Assert.Contains("CREATE CONNECTION corp AS MOCKDB();", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void HidingAVisual_WritesVisibleOffAndReadsBackAsHidden()
    {
        var state = ParseState(TwoColumnDashboard);
        var card = Visual(state, "RevenueCard");
        Assert.False(card.Options.ContainsKey("VISIBLE"));

        var hidden = card with
        {
            Options = new Dictionary<string, string>(card.Options, StringComparer.OrdinalIgnoreCase)
            {
                ["VISIBLE"] = "OFF",
            },
        };
        var patched = _patcher.Patch(TwoColumnDashboard, WithVisuals(state, [hidden, Visual(state, "SalesByRegion")]));

        Assert.NotEqual(TwoColumnDashboard, patched);
        var reparsed = ParseState(patched);
        Assert.Equal("OFF", Visual(reparsed, "RevenueCard").Options["VISIBLE"]);
        Assert.False(Visual(reparsed, "SalesByRegion").Options.ContainsKey("VISIBLE"));

        // And showing it again removes the hiding rather than leaving VISIBLE = OFF behind a second
        // ON, which is the shape a naive append would produce.
        var shownState = ParseState(patched);
        var shown = Visual(shownState, "RevenueCard");
        var restored = shown with
        {
            Options = new Dictionary<string, string>(shown.Options, StringComparer.OrdinalIgnoreCase)
            {
                ["VISIBLE"] = "ON",
            },
        };
        var reshown = _patcher.Patch(patched, WithVisuals(shownState, [restored, Visual(shownState, "SalesByRegion")]));
        Assert.Equal("ON", Visual(ParseState(reshown), "RevenueCard").Options["VISIBLE"]);
    }
}
