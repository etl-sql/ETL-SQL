using ETL_SQL.Portal.Services;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// The interaction clauses Studio's inspector edits — who a selection filters (<c>EMIT_FILTER</c>), and
/// a table's drill-through detail (<c>ROW_DETAIL</c>) — survive the designer's parse and patch, and
/// can be written, changed, and removed from the designer model.
/// </summary>
public sealed class InteractionDesignerRoundTripTests
{
    private const string Script = """
        SELECT Region, Amount INTO #sales FROM source.sales;
        DECLARE @region VARCHAR = 'All';
        CREATE VISUAL ByRegion AS BAR (
            SOURCE = #sales,
            MAPPINGS (X = Region, Y = Amount),
            EMIT_FILTER (TARGETS = (Detail))
        );
        CREATE VISUAL Detail AS TABLE (
            SOURCE = #sales
        );
        CREATE VISUAL Totals AS TABLE (
            SOURCE = #sales,
            ROW_DETAIL (TARGET = Detail, BINDINGS (@region = Region), LIMIT = 50)
        );
        CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A B C', MAP ('A' = ByRegion, 'B' = Detail, 'C' = Totals)));
        """;

    private static (DesignerAnalysisService Analysis, ETL_SQL.Portal.Models.DesignerStateDto State) Parse(string script)
    {
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        return (analysis, parsed.DesignState);
    }

    private static ETL_SQL.Portal.Models.DesignerVisualDto Visual(ETL_SQL.Portal.Models.DesignerStateDto state, string name) =>
        Assert.Single(state.Pages.SelectMany(page => page.Visuals), visual => visual.Name == name);

    [Fact]
    public void TheDesignerReadsWhoASelectionFilters()
    {
        var (_, state) = Parse(Script);

        Assert.Equal("Detail", Visual(state, "ByRegion").Options["emit_filter"]);
        Assert.False(Visual(state, "Detail").Options.ContainsKey("emit_filter"));
    }

    [Fact]
    public void TheDesignerReadsATablesRowDetail()
    {
        var (_, state) = Parse(Script);

        var clause = Visual(state, "Totals").Options["row_detail"];
        Assert.Contains("TARGET = Detail", clause, StringComparison.Ordinal);
        Assert.Contains("@region = Region", clause, StringComparison.Ordinal);
        Assert.Contains("LIMIT = 50", clause, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shape that must not come back: an unrelated edit to a visual carrying either clause is
    /// applied, and the clause is still there, unchanged.
    /// </summary>
    [Fact]
    public void AnUnrelatedEditKeepsBothClauses()
    {
        var (analysis, state) = Parse(Script);
        Visual(state, "ByRegion").Options["LEGEND"] = "OFF";
        Visual(state, "Totals").Options["PAGE_SIZE"] = "25";

        var patched = new DesignerScriptPatcher().Patch(Script, state);

        Assert.NotEqual(Script, patched);
        Assert.Contains("LEGEND", patched, StringComparison.Ordinal);
        Assert.Contains("EMIT_FILTER (TARGETS = (Detail))", patched, StringComparison.Ordinal);
        Assert.Contains("ROW_DETAIL (TARGET = Detail, BINDINGS (@region = Region), LIMIT = 50)", patched, StringComparison.Ordinal);
        var (_, reparsed) = Parse(patched);
        Assert.Equal("Detail", Visual(reparsed, "ByRegion").Options["emit_filter"]);
    }

    [Fact]
    public void TheDesignerChangesAndRemovesTargets()
    {
        var (_, state) = Parse(Script);
        Visual(state, "ByRegion").Options["emit_filter"] = "Detail, Totals";

        var changed = new DesignerScriptPatcher().Patch(Script, state);
        Assert.Contains("EMIT_FILTER (TARGETS = (Detail, Totals))", changed, StringComparison.Ordinal);

        var (_, again) = Parse(changed);
        Visual(again, "ByRegion").Options.Remove("emit_filter");
        var removed = new DesignerScriptPatcher().Patch(changed, again);
        Assert.DoesNotContain("EMIT_FILTER", removed, StringComparison.Ordinal);
        Parse(removed);
    }

    /// <summary>
    /// A trigger can run several actions. The designer model is keyed by trigger, and it used to
    /// keep only the last one, so any edit to the visual deleted the others.
    /// </summary>
    [Fact]
    public void AnEditKeepsEveryActionOnATrigger()
    {
        const string script = """
            SELECT Region, Amount INTO #sales FROM source.sales;
            DECLARE @region VARCHAR = 'All';
            CREATE VISUAL ByRegion AS BAR (
                SOURCE = #sales,
                MAPPINGS (X = Region, Y = Amount),
                ACTIONS (ON_CLICK = (SET_PARAMETER(@region, Region), NAVIGATE_PAGE(Detail)))
            );
            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = ByRegion)));
            CREATE PAGE [Detail] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = ByRegion)));
            """;
        var (_, state) = Parse(script);
        var visual = state.Pages.SelectMany(page => page.Visuals).First(v => v.Name == "ByRegion");
        visual.Options["LEGEND"] = "OFF";

        var patched = new DesignerScriptPatcher().Patch(script, state);

        Assert.Contains("LEGEND", patched, StringComparison.Ordinal);
        Assert.Contains("SET_PARAMETER(@region, Region)", patched, StringComparison.Ordinal);
        Assert.Contains("NAVIGATE_PAGE(Detail)", patched, StringComparison.Ordinal);
        Parse(patched);
    }

    [Fact]
    public void TheDesignerAddsAndRemovesARowDetail()
    {
        var (_, state) = Parse(Script);
        Visual(state, "Detail").Options["row_detail"] = "ROW_DETAIL (TARGET = Totals, BINDINGS (@region = Region))";

        var added = new DesignerScriptPatcher().Patch(Script, state);
        Assert.Contains("ROW_DETAIL (TARGET = Totals, BINDINGS (@region = Region))", added, StringComparison.Ordinal);

        var (_, again) = Parse(added);
        Visual(again, "Totals").Options.Remove("row_detail");
        var removed = new DesignerScriptPatcher().Patch(added, again);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(removed, "ROW_DETAIL"));
        Parse(removed);
    }
}
