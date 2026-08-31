using System.Linq;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting.Authoring;
using Xunit;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// The Portal's <see cref="DesignerAnalysisService"/> and the host-neutral
/// <see cref="DesignerScriptParsingService"/> must read a script the same way. They were separate
/// implementations of the same ~300 lines, and a one-line fix to the shared copy did not reach the
/// Portal at all, so these tests pin the behaviours that actually differed, not just the happy path.
/// </summary>
public class DesignerParsingParityTests
{
    private readonly DesignerAnalysisService _portal = new();
    private readonly DesignerScriptParsingService _shared = new();

    private const string Script = """
        CREATE DATASET &sales AS (
          SELECT region, amount FROM pg.sales
        );

        CREATE VISUAL v_rev AS BAR (
            TITLE = 'Revenue',
            SOURCE = &sales,
            MAPPINGS (X = region, Y = amount)
        );

        CREATE PAGE [Summary] AS DASHBOARD (
            LAYOUT (
                STRUCTURE = 'A B
                             C D',
                MAP ('A' = v_rev, 'B' = v_rev, 'C' = v_rev, 'D' = v_rev)
            )
        );
        """;

    [Fact]
    public void BothServicesReadTheSameGridPositions()
    {
        var portal = _portal.Parse(Script, 100).DesignState;
        var shared = _shared.Parse(Script);

        var portalCells = portal.Pages.SelectMany(p => p.Visuals).Select(v => (v.GridCol, v.GridRow)).ToList();
        var sharedCells = shared.Pages.SelectMany(p => p.Visuals).Select(v => (v.GridCol, v.GridRow)).ToList();

        Assert.Equal(sharedCells, portalCells);
        Assert.Equal(4, portalCells.Count);
        Assert.Equal(2, portalCells.Select(c => c.GridRow).Distinct().Count());
    }

    /// <summary>
    /// The name the parser reads a dataset back under has to be the name generation would write, or a
    /// visual's SOURCE stops matching the dataset it came from. The two copies carried different rules:
    /// generation replaces every non-ASCII character and prefixes anything not starting with a letter,
    /// while the parser's copy kept Unicode letters and prefixed only a leading digit. The generator is
    /// canonical here, because it is the side that writes the name into the script.
    /// </summary>
    [Theory]
    [InlineData("_private")]
    [InlineData("sales2024")]
    [InlineData("Uniced")]
    public void DatasetNamesNormalizeTheSameWayGenerationWritesThem(string authored)
    {
        var script = $"""
            CREATE DATASET &{authored} AS (
              SELECT region, amount FROM pg.sales
            );
            """;

        var portal = _portal.Parse(script, 100).DesignState;
        var shared = _shared.Parse(script);

        var portalName = Assert.Single(portal.Datasets).Name;
        var sharedName = Assert.Single(shared.Datasets).Name;

        Assert.Equal(sharedName, portalName);

        // And it must match what generation emits for that dataset, or the round trip renames it.
        var generated = new ETL_SQL.Portal.Services.DesignerScriptGenerationService().Generate(portal);
        Assert.Contains(portalName, generated);
    }

    /// <summary>
    /// A dataset whose name does not start with a letter is the case the two rules disagreed on:
    /// generation prefixes it, the parser's copy did not. Parsing then reported a dataset the generator
    /// would write under a different name.
    /// </summary>
    [Fact]
    public void ADatasetNameThatDoesNotStartWithALetterIsReadUnderTheNameGenerationWouldWrite()
    {
        const string script = """
            CREATE DATASET &_private AS (
              SELECT region FROM pg.sales
            );
            """;

        var portalName = Assert.Single(_portal.Parse(script, 100).DesignState.Datasets).Name;
        var sharedName = Assert.Single(_shared.Parse(script).Datasets).Name;

        Assert.Equal(portalName, sharedName);
        Assert.Equal("&v__private", portalName);
    }

    /// <summary>
    /// A script with nothing to draw still gives the canvas one page to draw on. The client already
    /// synthesised exactly this page when the server sent none; both services now agree on it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SELECT region, amount INTO #stage FROM pg.sales;")]
    public void AScriptWithNoPagesStillYieldsOnePage(string script)
    {
        var portal = _portal.Parse(script, 100).DesignState;
        var shared = _shared.Parse(script);

        Assert.Equal(shared.Pages.Count, portal.Pages.Count);
        var page = Assert.Single(portal.Pages);
        Assert.Empty(page.Visuals);
    }

    [Fact]
    public void BothServicesReadTheSameDatasetsVisualsAndBookmarks()
    {
        var script = Script + """


            CREATE BOOKMARK WestQ4 AS (
                TITLE = 'West, Q4',
                PARAMETERS (@Region = 'West'),
                PAGE = Summary
            );
            """;

        var portal = _portal.Parse(script, 100).DesignState;
        var shared = _shared.Parse(script);

        Assert.Equal(
            shared.Datasets.Select(d => (d.Name, d.Query)),
            portal.Datasets.Select(d => (d.Name, d.Query)));
        Assert.Equal(
            shared.Pages.SelectMany(p => p.Visuals).Select(v => (v.Name, v.Type, v.Title, v.Dataset)),
            portal.Pages.SelectMany(p => p.Visuals).Select(v => (v.Name, v.Type, v.Title, v.Dataset)));
        Assert.Equal(
            shared.Bookmarks!.Select(b => (b.Name, b.Title, b.Page)),
            portal.Bookmarks!.Select(b => (b.Name, b.Title, b.Page)));
    }
}
