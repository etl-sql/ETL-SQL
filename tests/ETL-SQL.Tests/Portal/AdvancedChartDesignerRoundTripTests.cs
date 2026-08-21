using System.Linq;
using ETL_SQL.Portal.Services;

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
