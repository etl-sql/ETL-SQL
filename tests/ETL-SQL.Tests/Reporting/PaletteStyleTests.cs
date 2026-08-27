using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Contracts;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class PaletteStyleTests
{
    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    private static Statement ParseStatement(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).ParseStatement();

    private static Evaluator CreateEvaluator()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;
        return evaluator;
    }

    [Fact]
    public void TestParseAndFormat_ExplicitSeriesColor_PreservesKey()
    {
        var script = @"CREATE VISUAL V1 AS BAR (
    SOURCE = #data,
    MAPPINGS ( X = col1, Y = col2 ),
    STYLE ( COLOR:Domestic = '#1d4ed8' )
);";
        var stmt = ParseStatement(script);
        var vStmt = Assert.IsType<CreateVisualStatement>(stmt);
        Assert.True(vStmt.Styles.ContainsKey("COLOR:Domestic"));
        Assert.Equal("#1d4ed8", vStmt.Styles["COLOR:Domestic"]);

        var formatted = AstSerializer.Format(vStmt).Trim().Replace("\r\n", "\n");
        Assert.Contains("STYLE ( COLOR:Domestic = '#1d4ed8' )", formatted);
    }

    [Fact]
    public void TestParseAndFormat_DualStyleClause_PreservesBoth()
    {
        var script = @"CREATE VISUAL V1 AS BAR (
    SOURCE = #data,
    MAPPINGS ( X = col1, Y = col2 ),
    STYLE = NamedStyle,
    STYLE ( COLOR:Domestic = '#1d4ed8' )
);";
        var stmt = ParseStatement(script);
        var vStmt = Assert.IsType<CreateVisualStatement>(stmt);
        Assert.Equal("NamedStyle", vStmt.StyleName);
        Assert.True(vStmt.Styles.ContainsKey("COLOR:Domestic"));

        var formatted = AstSerializer.Format(vStmt).Trim().Replace("\r\n", "\n");
        Assert.Contains("STYLE = NamedStyle", formatted);
        Assert.Contains("STYLE ( COLOR:Domestic = '#1d4ed8' )", formatted);

        // Round-trip reparse
        var reparsed = ParseStatement(formatted);
        var reStmt = Assert.IsType<CreateVisualStatement>(reparsed);
        Assert.Equal("NamedStyle", reStmt.StyleName);
        Assert.True(reStmt.Styles.ContainsKey("COLOR:Domestic"));
    }

    [Fact]
    public void TestParseCreateStyle_WithPaletteSequence()
    {
        var script = @"CREATE STYLE BrandTheme AS (
    PALETTE = ('#2563eb', '#16a34a', '#f59e0b'),
    BACKGROUND = '#ffffff'
);";
        var stmt = ParseStatement(script);
        var sStmt = Assert.IsType<CreateStyleStatement>(stmt);

        Assert.Equal("BrandTheme", sStmt.Name);
        Assert.Equal(3, sStmt.Palette.Length);
        Assert.Equal("#2563eb", sStmt.Palette[0]);
        Assert.Equal("#16a34a", sStmt.Palette[1]);
        Assert.Equal("#f59e0b", sStmt.Palette[2]);

        var formatted = AstSerializer.Format(sStmt).Trim().Replace("\r\n", "\n");
        Assert.Contains("PALETTE = ('#2563eb', '#16a34a', '#f59e0b')", formatted);
    }

    [Fact]
    public async Task TestContainerManifest_ResolvesPaletteAndTokensEndToEnd()
    {
        var evaluator = CreateEvaluator();
        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            CREATE STYLE ContainerStyle AS (
                BACKGROUND = '#f1f5f9',
                PALETTE = ('#0284c7', '#0d9488', '#e11d48')
            );

            CREATE VISUAL V1 AS BAR (
                SOURCE = #data,
                MAPPINGS ( X = Cat, Y = Val )
            );

            CREATE CONTAINER C1 AS BOX (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = V1 )
                ),
                STYLE = ContainerStyle
            );

            CREATE PAGE P1 AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = C1 )
                )
            );
            """;

        await evaluator.Evaluate(Parse(script));

        var manifestBuilder = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await manifestBuilder.BuildAsync("test.rptsql");

        Assert.NotNull(manifest.Containers);
        var cm = Assert.Single(manifest.Containers);
        Assert.NotNull(cm.Palette);
        Assert.Equal(3, cm.Palette.Count);
        Assert.Equal("#0284c7", cm.Palette[0]);
        Assert.NotNull(cm.DesignTokens);
        Assert.Equal("#0284c7", cm.DesignTokens["--etl-palette-1"]);
        Assert.Equal("#0d9488", cm.DesignTokens["--etl-palette-2"]);
        Assert.Equal("#e11d48", cm.DesignTokens["--etl-palette-3"]);
    }

    [Fact]
    public async Task TestRefreshVisual_PageAndContainerInheritance()
    {
        var evaluator = CreateEvaluator();
        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            CREATE STYLE PageStyle AS (
                PALETTE = ('#111111', '#222222', '#333333')
            );

            CREATE STYLE ContainerStyle AS (
                PALETTE = ('#444444', '#555555')
            );

            CREATE VISUAL VPage AS BAR (
                SOURCE = #data,
                MAPPINGS ( X = Cat, Y = Val )
            );

            CREATE VISUAL VContainer AS BAR (
                SOURCE = #data,
                MAPPINGS ( X = Cat, Y = Val )
            );

            CREATE CONTAINER C1 AS BOX (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = VContainer )
                ),
                STYLE = ContainerStyle
            );

            CREATE PAGE P1 AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A B',
                    MAP ( 'A' = VPage, 'B' = C1 )
                ),
                STYLE = PageStyle
            );
            """;

        await evaluator.Evaluate(Parse(script));

        var manifestBuilder = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await manifestBuilder.BuildAsync("test.rptsql");

        var vmPage = manifest.Visuals.First(v => v.Name == "VPage");
        var vmCont = manifest.Visuals.First(v => v.Name == "VContainer");

        Assert.Equal("#111111", vmPage.Palette![0]);
        Assert.Equal("#444444", vmCont.Palette![0]);

        // Refresh and verify retention / recomputation of inherited palettes
        var vPageStmt = evaluator.ReportContext.VisualDefinitions["VPage"];
        var vContStmt = evaluator.ReportContext.VisualDefinitions["VContainer"];

        await manifestBuilder.RefreshVisualAsync(vPageStmt, vmPage);
        await manifestBuilder.RefreshVisualAsync(vContStmt, vmCont);

        Assert.NotNull(vmPage.Palette);
        Assert.Equal("#111111", vmPage.Palette[0]);
        Assert.NotNull(vmCont.Palette);
        Assert.Equal("#444444", vmCont.Palette[0]);
    }

    [Fact]
    public async Task TestContrastResolution_AgainstInheritedAncestorBackground_AndExclusion()
    {
        var evaluator = CreateEvaluator();
        // Page has dark background #111827.
        // Palette includes #1e293b (low contrast against #111827 ~1.3:1) and #f8fafc (high contrast ~16:1).
        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            CREATE STYLE DarkPageStyle AS (
                BACKGROUND = '#111827',
                PALETTE = ('#1e293b', '#f8fafc')
            );

            CREATE VISUAL V1 AS BAR (
                SOURCE = #data,
                MAPPINGS ( X = Cat, Y = Val )
            );

            CREATE PAGE P1 AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = V1 )
                ),
                STYLE = DarkPageStyle
            );
            """;

        await evaluator.Evaluate(Parse(script));

        var manifestBuilder = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await manifestBuilder.BuildAsync("test.rptsql");

        var vm = manifest.Visuals.First(v => v.Name == "V1");
        Assert.NotNull(vm.Diagnostics);
        Assert.Contains(vm.Diagnostics, d => d.Code == "PALETTE_CONTRAST_LOW" && d.Message.Contains("#1e293b"));

        // Low contrast color #1e293b MUST NOT be emitted in vm.Palette or tokens
        Assert.NotNull(vm.Palette);
        Assert.Single(vm.Palette);
        Assert.Equal("#f8fafc", vm.Palette[0]);
        Assert.Equal("#f8fafc", vm.DesignTokens!["--etl-palette-1"]);
        Assert.False(vm.DesignTokens.ContainsKey("--etl-palette-2"));
    }

    [Fact]
    public void TestContrastCalculation_WithAlphaCompositing()
    {
        // Semi-transparent blue over white
        var semiBlue = "#2563eb80"; // 50% alpha
        var bg = "#ffffff";
        var eval = ColorContrast.Evaluate(semiBlue, bg, minRatio: 3.0);
        Assert.True(eval.Ratio > 1.0);

        // Semi-transparent light gray over white should fail 3.0:1
        var lightGrayAlpha = "#cccccc80";
        var evalFail = ColorContrast.Evaluate(lightGrayAlpha, bg, minRatio: 3.0);
        Assert.False(evalFail.Passed);
    }

    [Fact]
    public async Task TestUnsafeOrInvalidPaintValues_ExcludedFromManifestAndTokens()
    {
        var evaluator = CreateEvaluator();
        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            CREATE STYLE UnsafeStyle AS (
                PALETTE = ('#2563eb', 'invalid-color', 'url(javascript:alert(1))', '#16a34a')
            );

            CREATE VISUAL V1 AS BAR (
                SOURCE = #data,
                MAPPINGS ( X = Cat, Y = Val ),
                STYLE = UnsafeStyle
            );

            CREATE PAGE P1 AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = V1 )
                )
            );
            """;

        await evaluator.Evaluate(Parse(script));

        var manifestBuilder = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await manifestBuilder.BuildAsync("test.rptsql");

        var vm = manifest.Visuals.First(v => v.Name == "V1");
        Assert.NotNull(vm.Palette);
        Assert.Equal(2, vm.Palette.Count);
        Assert.Equal("#2563eb", vm.Palette[0]);
        Assert.Equal("#16a34a", vm.Palette[1]);

        // Tokens only contain safe values
        Assert.True(vm.DesignTokens!.ContainsKey("--etl-palette-1"));
        Assert.True(vm.DesignTokens!.ContainsKey("--etl-palette-2"));
        Assert.False(vm.DesignTokens!.ContainsKey("--etl-palette-3"));
    }

    [Fact]
    public async Task TestStableSeriesIdentityAssignment_RowOrderInvariance()
    {
        var evaluator = CreateEvaluator();

        // Data 1: Domestic first, then International, then Online
        const string script1 = """
            SELECT 'Q1' AS Category, 'Domestic' AS Series, 100 AS Amount INTO #sales1;
            INSERT INTO #sales1 (Category, Series, Amount) VALUES ('Q1', 'International', 200);
            INSERT INTO #sales1 (Category, Series, Amount) VALUES ('Q1', 'Online', 300);

            CREATE STYLE MyPalette AS (
                PALETTE = ('#2563eb', '#16a34a', '#f59e0b')
            );

            CREATE VISUAL Chart1 AS BAR (
                SOURCE = #sales1,
                MAPPINGS ( X = Category, Y = Amount, COLOR = Series ),
                STYLE = MyPalette
            );

            CREATE PAGE P1 AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = Chart1 )
                )
            );
            """;

        await evaluator.Evaluate(Parse(script1));
        var mb1 = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var m1 = await mb1.BuildAsync("test1.rptsql");
        var vm1 = m1.Visuals.First(v => v.Name == "Chart1");
        Assert.Null(vm1.Error);
        var plan1 = vm1.PlotPlan!;

        // Data 2: Online first, then International, then Domestic (reversed)
        var evaluator2 = CreateEvaluator();
        const string script2 = """
            SELECT 'Q1' AS Category, 'Online' AS Series, 300 AS Amount INTO #sales2;
            INSERT INTO #sales2 (Category, Series, Amount) VALUES ('Q1', 'International', 200);
            INSERT INTO #sales2 (Category, Series, Amount) VALUES ('Q1', 'Domestic', 100);

            CREATE STYLE MyPalette AS (
                PALETTE = ('#2563eb', '#16a34a', '#f59e0b')
            );

            CREATE VISUAL Chart2 AS BAR (
                SOURCE = #sales2,
                MAPPINGS ( X = Category, Y = Amount, COLOR = Series ),
                STYLE = MyPalette
            );

            CREATE PAGE P2 AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = Chart2 )
                )
            );
            """;

        await evaluator2.Evaluate(Parse(script2));
        var mb2 = new ManifestBuilder(evaluator2, maxVisualParallelism: 1);
        var m2 = await mb2.BuildAsync("test2.rptsql");
        var vm2 = m2.Visuals.First(v => v.Name == "Chart2");
        Assert.Null(vm2.Error);
        var plan2 = vm2.PlotPlan!;

        var colorDomestic1 = plan1.Series.First(s => s.Key == "Domestic").Color;
        var colorDomestic2 = plan2.Series.First(s => s.Key == "Domestic").Color;

        var colorOnline1 = plan1.Series.First(s => s.Key == "Online").Color;
        var colorOnline2 = plan2.Series.First(s => s.Key == "Online").Color;

        Assert.Equal(colorDomestic1, colorDomestic2);
        Assert.Equal(colorOnline1, colorOnline2);
    }

    [Fact]
    public async Task TestCategoricalSeries_ReceivesPaletteDerivedSeriesTokens()
    {
        var evaluator = CreateEvaluator();
        const string script = """
            SELECT 'Q1' AS Category, 'Domestic' AS Series, 100 AS Amount INTO #sales;
            INSERT INTO #sales (Category, Series, Amount) VALUES ('Q1', 'International', 200);

            CREATE STYLE MyPalette AS (
                PALETTE = ('#2563eb', '#16a34a')
            );

            CREATE VISUAL Chart1 AS BAR (
                SOURCE = #sales,
                MAPPINGS ( X = Category, Y = Amount, COLOR = Series ),
                STYLE = MyPalette
            );

            CREATE PAGE P1 AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = Chart1 )
                )
            );
            """;

        await evaluator.Evaluate(Parse(script));
        var mb = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await mb.BuildAsync("test.rptsql");

        var vm = manifest.Visuals.First(v => v.Name == "Chart1");
        Assert.NotNull(vm.DesignTokens);
        Assert.True(vm.DesignTokens.ContainsKey("--etl-series-domestic"));
        Assert.True(vm.DesignTokens.ContainsKey("--etl-series-international"));
        Assert.Equal("#2563eb", vm.DesignTokens["--etl-series-domestic"]);
        Assert.Equal("#16a34a", vm.DesignTokens["--etl-series-international"]);
    }

    [Fact]
    public async Task TestLowContrastPaletteColors_ExcludedFromEmittedPaletteAndTokens()
    {
        var evaluator = CreateEvaluator();
        // Background is white (#ffffff). #ffffff and #f1f5f9 fail 3.0:1 contrast against #ffffff.
        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            CREATE STYLE LowContrastStyle AS (
                BACKGROUND = '#ffffff',
                PALETTE = ('#ffffff', '#2563eb', '#f1f5f9', '#16a34a')
            );

            CREATE VISUAL V1 AS BAR (
                SOURCE = #data,
                MAPPINGS ( X = Cat, Y = Val ),
                STYLE = LowContrastStyle
            );

            CREATE PAGE P1 AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = V1 )
                )
            );
            """;

        await evaluator.Evaluate(Parse(script));
        var mb = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await mb.BuildAsync("test.rptsql");

        var vm = manifest.Visuals.First(v => v.Name == "V1");
        Assert.NotNull(vm.Diagnostics);
        Assert.Contains(vm.Diagnostics, d => d.Code == "PALETTE_CONTRAST_LOW");

        // Only high-contrast colors (#2563eb and #16a34a) should be emitted
        Assert.NotNull(vm.Palette);
        Assert.Equal(2, vm.Palette.Count);
        Assert.Equal("#2563eb", vm.Palette[0]);
        Assert.Equal("#16a34a", vm.Palette[1]);
        Assert.DoesNotContain("#ffffff", vm.Palette);
        Assert.DoesNotContain("#f1f5f9", vm.Palette);

        // Design tokens should only contain the 2 valid palette entries
        Assert.Equal("#2563eb", vm.DesignTokens!["--etl-palette-1"]);
        Assert.Equal("#16a34a", vm.DesignTokens["--etl-palette-2"]);
        Assert.False(vm.DesignTokens.ContainsKey("--etl-palette-3"));
    }

    [Fact]
    public void TestResolvedSeriesTokens_AndCollisionHandling()
    {
        var assignments = new Dictionary<string, string>
        {
            ["Region North"] = "#2563eb",
            ["Region_North"] = "#16a34a",
            ["123Numeric"] = "#f59e0b"
        };

        var tokens = DesignTokenResolver.ResolveScopedTokens(null, false, null, assignments);

        // "Region North" -> --etl-series-region-north
        Assert.True(tokens.ContainsKey("--etl-series-region-north"));
        // "Region_North" collides with same base name -> --etl-series-region-north-2
        Assert.True(tokens.ContainsKey("--etl-series-region-north-2"));
        // "123Numeric" leading digit -> --etl-series-s-123numeric
        Assert.True(tokens.ContainsKey("--etl-series-s-123numeric"));
    }

    [Fact]
    public async Task TestConflictingPagePalettes_EmitsDiagnostic()
    {
        var evaluator = CreateEvaluator();
        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            CREATE STYLE StyleA AS ( PALETTE = ('#111111', '#222222') );
            CREATE STYLE StyleB AS ( PALETTE = ('#999999', '#888888') );

            CREATE VISUAL SharedVisual AS BAR (
                SOURCE = #data,
                MAPPINGS ( X = Cat, Y = Val )
            );

            CREATE PAGE PageA AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = SharedVisual )
                ),
                STYLE = StyleA
            );

            CREATE PAGE PageB AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = SharedVisual )
                ),
                STYLE = StyleB
            );
            """;

        await evaluator.Evaluate(Parse(script));

        var manifestBuilder = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await manifestBuilder.BuildAsync("test.rptsql");

        var vm = manifest.Visuals.First(v => v.Name == "SharedVisual");
        Assert.NotNull(vm.Diagnostics);
        Assert.Contains(vm.Diagnostics, d => d.Code == "PALETTE_INHERITANCE_CONFLICT");
    }
}
