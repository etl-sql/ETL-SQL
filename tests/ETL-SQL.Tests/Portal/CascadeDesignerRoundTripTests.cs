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
}
