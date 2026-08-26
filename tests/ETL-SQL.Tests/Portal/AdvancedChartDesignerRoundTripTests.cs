using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Xunit;

namespace ETL_SQL.Tests.Portal;

public sealed class AdvancedChartDesignerRoundTripTests
{
    [Fact]
    public void UnrelatedDesignerEdit_PreservesNestedChartClauseByteForByte()
    {
        const string script = """
            CREATE VISUAL Native AS CUSTOM (
              TITLE = 'Original',
              SOURCE = #prepared,
              CHART (
                /* author-owned scale note */
                COORDINATE (TYPE = CARTESIAN),
                SCALES (amount = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)),
                LAYERS (
                  bars = RECT (
                    ENCODINGS (Y = Revenue (TYPE = QUANTITATIVE, SCALE = amount)),
                    CONDITIONS (COLOR WHEN Revenue < 0 THEN '#b91c1c' ELSE '#2563eb')
                  )
                )
              )
            );
            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Native)));
            """;
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        var page = Assert.Single(parsed.DesignState.Pages);
        var visual = Assert.Single(page.Visuals);
        var state = parsed.DesignState with
        {
            Pages = [page with { Visuals = [visual with { Title = "Updated" }] }]
        };

        var patched = new DesignerScriptPatcher().Patch(script, state);

        Assert.Equal(Clause(script, "CHART"), Clause(patched, "CHART"));
        Assert.Contains("TITLE = 'Updated'", patched);
    }

    [Fact]
    public void ChartClauseEdit_SurgicallyPatchesChartClause_PreservingSurroundingSqlAndComments()
    {
        const string script = """
            -- Step 1: Data prep
            /* Vital SQL rationale */
            SELECT 'Cat' AS category, 100 AS value INTO #prepared;

            CREATE VISUAL Native AS CUSTOM (
              TITLE = 'Original',
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  bars = RECT (
                    ENCODINGS (Y = value (TYPE = QUANTITATIVE))
                  )
                )
              )
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Native)));
            -- Trailing comment
            """;

        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        var visual = Assert.Single(parsed.DesignState.Pages[0].Visuals);

        const string newChartBlock = """
            CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN),
                SCALES (valScale = LINEAR (CHANNEL = X, INCLUDE_ZERO = ON)),
                LAYERS (
                  lines = LINE (
                    ENCODINGS (X = value (TYPE = QUANTITATIVE, SCALE = valScale))
                  )
                )
              )
            """;

        var updatedOptions = new Dictionary<string, string>(visual.Options)
        {
            ["advanced_chart"] = newChartBlock
        };

        var state = parsed.DesignState with
        {
            Pages = [parsed.DesignState.Pages[0] with
            {
                Visuals = [visual with { Options = updatedOptions }]
            }]
        };

        var patched = new DesignerScriptPatcher().Patch(script, state);

        Assert.Contains("COORDINATE (TYPE = TRANSPOSED_CARTESIAN)", patched);
        Assert.Contains("SCALES (valScale = LINEAR (CHANNEL = X, INCLUDE_ZERO = ON))", patched);
        Assert.Contains("lines = LINE", patched);
        Assert.Contains("-- Step 1: Data prep", patched);
        Assert.Contains("/* Vital SQL rationale */", patched);
        Assert.Contains("-- Trailing comment", patched);

        // Reparsing patched script must be clean
        var reparsed = analysis.Parse(patched, 100);
        Assert.Null(reparsed.Error);
    }

    [Fact]
    public void AddCustomVisual_GeneratesValidScriptWithChartClause()
    {
        var newVisual = new DesignerVisualDto(
            Id: "v_custom_1",
            Name: "custom_chart",
            Type: "CUSTOM",
            GridCol: 1,
            GridRow: 1,
            GridColSpan: 12,
            GridRowSpan: 6,
            Title: "My Custom Chart",
            Dataset: "prepared",
            Mappings: new Dictionary<string, string>(),
            Options: new Dictionary<string, string>()
        );

        var state = new DesignerStateDto(
            Pages: [new DesignerPageDto("p1", "Overview", "Dashboard", [newVisual])],
            Datasets: [new DesignerDatasetDto("ds1", "prepared", "SELECT 'A' AS category, 50 AS value")]
        );

        var generated = new DesignerScriptGenerationService().Generate(state);

        Assert.Contains("CREATE VISUAL custom_chart AS CUSTOM", generated);
        Assert.Contains("CHART (", generated);
        Assert.Contains("COORDINATE (TYPE = CARTESIAN)", generated);
        Assert.Contains("LAYERS (", generated);

        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(generated, 100);
        Assert.Null(parsed.Error);
    }

    [Fact]
    public void ConvertStandardVisualToCustom_PatchesHeaderAndInsertsChartClause()
    {
        const string script = """
            CREATE VISUAL myChart AS BAR (
                TITLE = 'Sales Chart',
                SOURCE = #sales,
                MAPPINGS (Category = Region, Value = Amount)
            );
            CREATE PAGE [Main] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = myChart)));
            """;

        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        var visual = Assert.Single(parsed.DesignState.Pages[0].Visuals);

        const string customChart = """
            CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                    layer1 = RECT (
                        ENCODINGS (X = Region (TYPE = NOMINAL), Y = Amount (TYPE = QUANTITATIVE))
                    )
                )
            )
            """;

        var state = parsed.DesignState with
        {
            Pages = [parsed.DesignState.Pages[0] with
            {
                Visuals = [visual with
                {
                    Type = "CUSTOM",
                    Options = new Dictionary<string, string>(visual.Options)
                    {
                        ["advanced_chart"] = customChart
                    }
                }]
            }]
        };

        var patched = new DesignerScriptPatcher().Patch(script, state);

        Assert.Contains("CREATE VISUAL myChart AS CUSTOM", patched);
        Assert.Contains("CHART (", patched);
        Assert.Contains("layer1 = RECT", patched);

        var reparsed = analysis.Parse(patched, 100);
        Assert.Null(reparsed.Error);
    }

    [Fact]
    public void CorruptedChartClauseEdit_LosslessFallbackNeverThrows()
    {
        const string script = """
            CREATE VISUAL Native AS CUSTOM (
              TITLE = 'Original',
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  bars = RECT (
                    ENCODINGS (Y = Revenue (TYPE = QUANTITATIVE))
                  )
                )
              )
            );
            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Native)));
            """;

        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        var visual = Assert.Single(parsed.DesignState.Pages[0].Visuals);

        // Intentionally corrupted CHART block (unclosed parenthesis, missing mark)
        const string brokenChart = "CHART ( COORDINATE ( TYPE = UNKNOWN ), LAYERS ( unclosed = ";

        var state = parsed.DesignState with
        {
            Pages = [parsed.DesignState.Pages[0] with
            {
                Visuals = [visual with
                {
                    Options = new Dictionary<string, string>(visual.Options)
                    {
                        ["advanced_chart"] = brokenChart
                    }
                }]
            }]
        };

        // Patcher must not throw
        string patched = null!;
        var ex = Record.Exception(() => patched = new DesignerScriptPatcher().Patch(script, state));
        Assert.Null(ex);
        Assert.NotNull(patched);
    }

    private static string Clause(string text, string keyword)
    {
        var start = text.IndexOf(keyword, System.StringComparison.Ordinal);
        var open = text.IndexOf('(', start);
        var depth = 0; var inString = false;
        for (var index = open; index < text.Length; index++)
        {
            if (text[index] == '\'') inString = !inString;
            if (inString) continue;
            if (text[index] == '(') depth++;
            if (text[index] == ')' && --depth == 0) return text[start..(index + 1)];
        }
        throw new System.InvalidOperationException("Unterminated CHART clause.");
    }
}
