using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting.Authoring;
using Xunit;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// Every statement Studio's guided surfaces can emit must parse, and the patch must actually land.
///
/// <para>The patcher refuses any edit that would leave the document unparseable — the right call, and
/// the reason a wizard emitting retired syntax fails <em>silently</em>: the author fills in a field,
/// clicks confirm, sees a success toast, and the buffer is untouched. That is indistinguishable from
/// the dead-button defect the guided steps shipped with.</para>
///
/// <para>It happened with <c>CREATE DATASET ... REFRESH EVERY</c>, which is retired: the parser
/// rejects it and points at <c>CREATE SCHEDULE</c> plus <c>CREATE JOB ... FOR REPORT</c> instead. The
/// designer had no business offering it, and the browser lane missed it because that field is
/// optional and the test left it blank. These assertions cover the emission itself.</para>
/// </summary>
public class DesignerAuthoringEmissionTests
{
    private const string Host = """
        CREATE CONNECTION corp_db AS MSSQL(CONNECTION_STRING = 'SHARED:corp_sales_gw');

        CREATE PAGE [Main] AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );
        """;

    private static readonly DesignerScriptPatcher Patcher = new(new DesignerScriptGenerationService());
    private static readonly DesignerScriptParsingService Parsing = new();

    private static bool Parses(string script)
    {
        var ast = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
        return !ast.Diagnostics.Any(d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
    }

    private static string PatchWith(params DesignerAuthoringDataset[] datasets) =>
        Patcher.Patch(Host, Parsing.Parse(Host) with { Datasets = datasets.ToList() });

    [Theory]
    [InlineData(null)]
    [InlineData("2h")]
    [InlineData("30m")]
    [InlineData("1d")]
    public void ADatasetTheWizardCanBuild_ParsesAndIsActuallyWritten(string? ttl)
    {
        var patched = PatchWith(new DesignerAuthoringDataset("d1", "&sales", "SELECT 1 AS x", ttl));

        Assert.True(Parses(patched), $"A dataset with TTL '{ttl ?? "(none)"}' produced an unparseable script.");
        Assert.NotEqual(Host, patched);
        Assert.Contains("CREATE DATASET &sales", patched, System.StringComparison.Ordinal);
        if (ttl is not null) Assert.Contains($"TTL = '{ttl}'", patched, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ADatasetCarriesNoRefreshInterval_BecauseTheParserRetiredIt()
    {
        // Guards the regression directly: nothing in the designer may emit REFRESH EVERY again.
        var patched = PatchWith(new DesignerAuthoringDataset("d1", "&sales", "SELECT 1 AS x", "2h"));

        Assert.DoesNotContain("REFRESH EVERY", patched, System.StringComparison.OrdinalIgnoreCase);
        Assert.True(Parses(patched));
    }

    [Theory]
    [InlineData("VARCHAR", "'All'", true)]
    [InlineData("INT", "0", false)]
    [InlineData("DATE", null, true)]
    [InlineData("DECIMAL", "0.0", false)]
    [InlineData("BOOLEAN", null, false)]
    [InlineData("DATETIME", null, true)]
    public void AParameterTheWizardCanBuild_ParsesAndIsActuallyWritten(string dataType, string? initial, bool isInput)
    {
        var state = Parsing.Parse(Host);
        var patched = Patcher.Patch(Host, state with
        {
            Parameters = new List<DesignerAuthoringParameter>
            {
                new("@region", dataType, initial, IsInput: isInput)
            }
        });

        Assert.True(Parses(patched), $"A {dataType} parameter produced an unparseable script.");
        Assert.NotEqual(Host, patched);
        Assert.Contains("DECLARE @region", patched, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BAR")]
    [InlineData("LINE")]
    [InlineData("PIE")]
    [InlineData("CARD")]
    [InlineData("TABLE")]
    [InlineData("MATRIX")]
    [InlineData("GAUGE")]
    [InlineData("HEATMAP")]
    public void AVisualTheChartBuilderCanBuild_ParsesAndIsActuallyWritten(string type)
    {
        var state = Parsing.Parse(Host);
        var page = state.Pages[0];
        var visual = new DesignerAuthoringVisual(
            "v1", "built_visual", type, 1, 1, 6, 4, "Built visual", "&sales",
            new Dictionary<string, string> { ["X"] = "region", ["Y"] = "total", ["VALUE"] = "total", ["LABEL"] = "region", ["ROW"] = "region" },
            new Dictionary<string, string>());

        var patched = Patcher.Patch(Host, state with
        {
            Datasets = [new DesignerAuthoringDataset("d1", "&sales", "SELECT 1 AS region, 2 AS total")],
            Pages = [page with { Visuals = [visual] }]
        });

        Assert.True(Parses(patched), $"A {type} visual produced an unparseable script.");
        Assert.Contains($"CREATE VISUAL built_visual AS {type}", patched, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ATextBandCarriesItsContent_SoTheFurnitureStepPrintsSomething()
    {
        var state = Parsing.Parse(Host);
        var page = state.Pages[0];
        var band = new DesignerAuthoringVisual(
            "v1", "page_header", "TEXT", 1, 1, 12, 2, "Page header", null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["text_default"] = "'Quarterly report'",
                ["print_layout"] = "PRINT_LAYOUT (KEEP_TOGETHER = ON)"
            });

        var patched = Patcher.Patch(Host, state with { Pages = [page with { Visuals = [band] }] });

        Assert.True(Parses(patched));
        Assert.Contains("DEFAULT = 'Quarterly report'", patched, System.StringComparison.Ordinal);

        // And it must survive a round trip, or the canvas would delete the text on the next edit.
        var reparsed = Parsing.Parse(patched);
        var reparsedBand = reparsed.Pages.SelectMany(p => p.Visuals).Single(v => v.Name == "page_header");
        Assert.Equal("'Quarterly report'", reparsedBand.Options["text_default"]);
    }

    // ── The escape-hatch guarantee (contract rule 5) ──────────────────────────────────────────────

    private const string HandAuthored = """
        -- Hand-authored preparation the designer does not model at all.
        WITH regional AS (SELECT Region, SUM(Amount) AS Total FROM corp_db.sales GROUP BY Region)
        SELECT Region, Total INTO #regional FROM regional;

        CREATE DATASET &secured
            TTL = '2h'
            COMPRESS = ON
            ENCRYPT = MACHINE
            AS (SELECT Region, Total FROM #regional);

        CREATE PAGE [Main] AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );
        """;

    [Fact]
    public void AWizardWrite_LeavesHandAuthoredStatementsAlone()
    {
        // A wizard adds something unrelated. Everything the author wrote — the data-prep CTE, and the
        // dataset options the designer has no field for — must come back untouched.
        var state = Parsing.Parse(HandAuthored);
        var patched = Patcher.Patch(HandAuthored, state with
        {
            Parameters = new List<DesignerAuthoringParameter> { new("@region", "VARCHAR", "'All'", IsInput: true) }
        });

        Assert.True(Parses(patched));
        Assert.Contains("DECLARE @region", patched, System.StringComparison.Ordinal);

        // The dataset keeps clauses the authoring model cannot represent.
        Assert.Contains("COMPRESS = ON", patched, System.StringComparison.Ordinal);
        Assert.Contains("ENCRYPT = MACHINE", patched, System.StringComparison.Ordinal);

        // And the preparation statement survives, comment included.
        Assert.Contains("-- Hand-authored preparation", patched, System.StringComparison.Ordinal);
        Assert.Contains("WITH regional AS", patched, System.StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrippingAnUnchangedParseIsAByteForByteNoOp()
    {
        // This is what makes it safe for every wizard to send the whole design state back: reconciling
        // a state against the script it came from must change nothing. If it did not hold, opening a
        // wizard and cancelling would still rewrite the author's file.
        var parsed = Parsing.Parse(HandAuthored);

        Assert.Single(parsed.Datasets);
        Assert.Equal("&secured", parsed.Datasets[0].Name);
        Assert.Equal("2h", parsed.Datasets[0].Ttl);
        Assert.Equal(HandAuthored, Patcher.Patch(HandAuthored, parsed));
    }

    [Fact]
    public void RewritingADatasetDropsClausesTheDesignerCannotRepresent()
    {
        // A documented limitation, asserted so it is found by a test rather than by an author whose
        // encryption setting quietly disappeared. Nothing edits an existing dataset today — the wizard
        // only creates, and an unchanged dataset is skipped entirely by the test above. When dataset
        // editing is built (W4.1), it must either carry COMPRESS/ENCRYPT/KEYFILE through the authoring
        // model or refuse to rewrite a statement that uses them. This is what a rewrite produces now.
        var state = Parsing.Parse(HandAuthored);
        var edited = state with
        {
            Datasets = [state.Datasets[0] with { Query = "SELECT Region FROM #regional" }]
        };

        var patched = Patcher.Patch(HandAuthored, edited);

        Assert.Contains("SELECT Region FROM #regional", patched, System.StringComparison.Ordinal);
        Assert.Contains("TTL = '2h'", patched, System.StringComparison.Ordinal);
        Assert.DoesNotContain("COMPRESS = ON", patched, System.StringComparison.Ordinal);
        Assert.DoesNotContain("ENCRYPT = MACHINE", patched, System.StringComparison.Ordinal);
    }
    // ── Parameter editing (W1.2) ──────────────────────────────────────────────────────────────────

    private const string Declared = """
        DECLARE @country VARCHAR(50) = 'USA';
        DECLARE @limit INT = 10 REQUIRED;

        CREATE PAGE [Main] AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );
        """;

    [Fact]
    public void EditingAParameter_RewritesOnlyThatDeclaration()
    {
        var state = Parsing.Parse(Declared);
        var edited = state.Parameters!
            .Select(parameter => parameter.Name == "@country" ? parameter with { InitialValue = "'Canada'" } : parameter)
            .ToList();

        var patched = Patcher.Patch(Declared, state with { Parameters = edited });

        Assert.True(Parses(patched));
        Assert.Contains("DECLARE @country VARCHAR(50) = 'Canada';", patched, System.StringComparison.Ordinal);
        // A sized type is authored text, not a designer enum: editing a default must not truncate it.
        Assert.DoesNotContain("DECLARE @country VARCHAR =", patched, System.StringComparison.Ordinal);
        Assert.Contains("DECLARE @limit INT = 10 REQUIRED;", patched, System.StringComparison.Ordinal);
    }

    [Fact]
    public void RenamingAParameter_ReplacesTheDeclarationRatherThanAddingASecond()
    {
        var state = Parsing.Parse(Declared);
        var renamed = state.Parameters!
            .Select(parameter => parameter.Name == "@country" ? parameter with { Name = "@nation" } : parameter)
            .ToList();

        var patched = Patcher.Patch(Declared, state with { Parameters = renamed });

        Assert.True(Parses(patched));
        Assert.Contains("@nation", patched, System.StringComparison.Ordinal);
        Assert.DoesNotContain("@country", patched, System.StringComparison.Ordinal);
        Assert.Single(Regex.Matches(patched, @"DECLARE @nation\b"));
    }

    [Fact]
    public void DeletingAParameter_RemovesOnlyThatDeclaration()
    {
        var state = Parsing.Parse(Declared);
        var kept = state.Parameters!.Where(parameter => parameter.Name != "@limit").ToList();

        var patched = Patcher.Patch(Declared, state with { Parameters = kept });

        Assert.True(Parses(patched));
        Assert.DoesNotContain("@limit", patched, System.StringComparison.Ordinal);
        Assert.Contains("DECLARE @country VARCHAR(50) = 'USA';", patched, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockScopedDeclarationIsReportedAsSuchAndNeverRewritten()
    {
        // The manager lists these read-only. Without the flag they look identical to a top-level
        // parameter, and an Edit button on one would do nothing at all — the patcher refuses by design.
        const string withBlock = """
            DECLARE @country VARCHAR(50) = 'USA';

            BEGIN
                DECLARE @scratch INT = 1;
                PRINT 'working';
            END;

            CREATE PAGE [Main] AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );
            """;

        var state = Parsing.Parse(withBlock);
        var scratch = state.Parameters!.Single(parameter => parameter.Name == "@scratch");
        var country = state.Parameters!.Single(parameter => parameter.Name == "@country");

        Assert.True(scratch.IsBlockScoped);
        Assert.False(country.IsBlockScoped);

        // Even asked to change it, the patcher leaves a block-scoped declaration alone.
        var edited = state.Parameters!
            .Select(parameter => parameter.Name == "@scratch" ? parameter with { InitialValue = "999" } : parameter)
            .ToList();

        Assert.Contains("DECLARE @scratch INT = 1;", Patcher.Patch(withBlock, state with { Parameters = edited }),
            System.StringComparison.Ordinal);
    }
}
