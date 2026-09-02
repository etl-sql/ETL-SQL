using ETL_SQL.Portal.Services;

namespace ETL_SQL.Tests.Portal;

public sealed class CascadeDesignerRoundTripTests
{
    [Fact]
    public void CascadeClause_SurvivesDesignerParseAndPatch()
    {
        const string script = """
            SELECT CityCode, RegionCode INTO #options FROM source.cities;
            CREATE VISUAL City AS SLICER (
                TITLE = 'City',
                SOURCE = #options,
                MAPPINGS (VALUE = CityCode),
                ACTIONS (ON_CHANGE = SET_PARAMETER(@City, CityCode)),
                CASCADE (MODE = LOCAL, PARENTS (@Region = RegionCode), INVALID = CLEAR, NULL = ALL, ALL_VALUE = '*', MULTISELECT = ANY)
            );
            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = City)));
            """;
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        var visual = Assert.Single(Assert.Single(parsed.DesignState.Pages).Visuals);
        Assert.Contains("MODE = LOCAL", visual.Options["cascade"]);

        var patched = new DesignerScriptPatcher().Patch(script, parsed.DesignState);
        var reparsed = analysis.Parse(patched, 100);
        Assert.Null(reparsed.Error);
        Assert.Equal(visual.Options["cascade"],
            Assert.Single(Assert.Single(reparsed.DesignState.Pages).Visuals).Options["cascade"]);
    }

    /// <summary>A slicer with no cascade, so the designer has to write the clause rather than keep one.</summary>
    private const string PlainSlicerScript = """
        SELECT CityCode, RegionCode INTO #options FROM source.cities;
        DECLARE @Region VARCHAR = 'North';
        CREATE VISUAL City AS SLICER (
            TITLE = 'City',
            SOURCE = #options,
            MAPPINGS (VALUE = CityCode),
            ACTIONS (ON_CHANGE = SET_PARAMETER(@City, CityCode))
        );
        CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = City)));
        """;

    /// <summary>
    /// The exact text Studio's cascade editor writes (designer.js, <c>writeCascade</c>).
    ///
    /// <para>It is pinned here because the inspector rewrites the whole clause on every edit, in the
    /// serializer's own shape, so that a parse of what Studio wrote returns the string Studio would
    /// write again. If the two drift, every cascade edit rewrites the clause a second time and the
    /// script churns on open.</para>
    /// </summary>
    private const string StudioWrittenCascade =
        "CASCADE ( MODE = LOCAL, PARENTS (@Region = RegionCode), INVALID = CLEAR, NULL = ALL, ALL_VALUE = '*', MULTISELECT = ANY )";

    [Fact]
    public void CascadeTheDesignerAdds_IsWrittenIntoTheStatementAndParsesBack()
    {
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(PlainSlicerScript, 100);
        Assert.Null(parsed.Error);

        var visual = Assert.Single(Assert.Single(parsed.DesignState.Pages).Visuals);
        Assert.False(visual.Options.ContainsKey("cascade"));
        visual.Options["cascade"] = StudioWrittenCascade;

        var patched = new DesignerScriptPatcher().Patch(PlainSlicerScript, parsed.DesignState);
        Assert.Contains("CASCADE", patched, StringComparison.Ordinal);

        var reparsed = analysis.Parse(patched, 100);
        Assert.Null(reparsed.Error);

        // Byte-identical, not merely equivalent: what Studio writes is what the serializer produces,
        // so reopening the report does not rewrite the clause the author just saved.
        Assert.Equal(StudioWrittenCascade,
            Assert.Single(Assert.Single(reparsed.DesignState.Pages).Visuals).Options["cascade"]);
    }

    [Fact]
    public void CascadeTheDesignerClears_IsRemovedFromTheStatement()
    {
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(PlainSlicerScript, 100);
        var withCascade = new DesignerScriptPatcher().Patch(PlainSlicerScript, parsed.DesignState);

        var seeded = analysis.Parse(PlainSlicerScript, 100);
        Assert.Single(Assert.Single(seeded.DesignState.Pages).Visuals).Options["cascade"] = StudioWrittenCascade;
        withCascade = new DesignerScriptPatcher().Patch(PlainSlicerScript, seeded.DesignState);
        Assert.Contains("CASCADE", withCascade, StringComparison.Ordinal);

        var reopened = analysis.Parse(withCascade, 100);
        Assert.Single(Assert.Single(reopened.DesignState.Pages).Visuals).Options.Remove("cascade");
        var cleared = new DesignerScriptPatcher().Patch(withCascade, reopened.DesignState);

        Assert.DoesNotContain("CASCADE", cleared, StringComparison.Ordinal);
        var reparsed = analysis.Parse(cleared, 100);
        Assert.Null(reparsed.Error);
        // The rest of the statement is untouched: clearing a cascade is not a rewrite of the visual.
        Assert.Contains("ACTIONS (ON_CHANGE = SET_PARAMETER(@City, CityCode))", cleared, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossVisualInteractionTheDesignerSets_IsWrittenAndParsesBack()
    {
        const string script = """
            SELECT Region, Revenue INTO #sales FROM source.orders;
            CREATE VISUAL ByRegion AS BAR (
                TITLE = 'Revenue by region',
                SOURCE = #sales,
                MAPPINGS (X = Region, Y = Revenue)
            );
            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = ByRegion)));
            """;

        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);

        var visual = Assert.Single(Assert.Single(parsed.DesignState.Pages).Visuals);
        visual.Options["interaction:ON_SELECT"] = "FILTER";
        visual.Options["interaction:MATCHING"] = "Region";

        var patched = new DesignerScriptPatcher().Patch(script, parsed.DesignState);
        Assert.Contains("INTERACTIONS (ON_SELECT = FILTER, MATCHING = Region)", patched, StringComparison.Ordinal);

        var reparsed = analysis.Parse(patched, 100);
        Assert.Null(reparsed.Error);
        var reloaded = Assert.Single(Assert.Single(reparsed.DesignState.Pages).Visuals);
        Assert.Equal("FILTER", reloaded.Options["interaction:ON_SELECT"]);
        Assert.Equal("REGION", reloaded.Options["interaction:MATCHING"].ToUpperInvariant());
    }
}
