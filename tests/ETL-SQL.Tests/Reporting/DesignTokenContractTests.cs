using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Contracts;
using ETL_SQL.Reporting.HtmlVisual;
using ETL_SQL.Reporting.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class DesignTokenContractTests
{
    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).Parse();
    }

    [Fact]
    public void BuiltInTokens_LightAndDark_ContainAllStandardTokens()
    {
        var requiredTokens = new[]
        {
            DesignTokens.SurfaceCard,
            DesignTokens.TextPrimary,
            DesignTokens.TextMuted,
            DesignTokens.Border,
            DesignTokens.Accent,
            DesignTokens.Success,
            DesignTokens.Danger,
            DesignTokens.RadiusSm,
            DesignTokens.RadiusMd,
            DesignTokens.RadiusLg,
            DesignTokens.Bg,
            DesignTokens.Surface,
            DesignTokens.Text,
            DesignTokens.TextSecondary,
            DesignTokens.Shadow,
            DesignTokens.Warning,
            DesignTokens.Info,
            DesignTokens.FontFamily,
            DesignTokens.FontMono
        };

        foreach (var token in requiredTokens)
        {
            Assert.True(DesignTokens.LightTokens.ContainsKey(token), $"Light tokens missing: {token}");
            Assert.True(DesignTokens.DarkTokens.ContainsKey(token), $"Dark tokens missing: {token}");
            Assert.False(string.IsNullOrWhiteSpace(DesignTokens.LightTokens[token]));
            Assert.False(string.IsNullOrWhiteSpace(DesignTokens.DarkTokens[token]));
        }
    }

    [Theory]
    [InlineData("--etl-surface-card", true)]
    [InlineData("--etl-text-primary", true)]
    [InlineData("--etl-accent", true)]
    [InlineData("--etl-custom-token", false)]
    [InlineData("--portal-theme", false)]
    [InlineData("--portal-shell-bg", false)]
    [InlineData("--host-var", false)]
    [InlineData("color", false)]
    [InlineData("background", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedTokenName_FiltersCorrectly(string? tokenName, bool expectedAllowed)
    {
        Assert.Equal(expectedAllowed, DesignTokens.IsAllowedTokenName(tokenName));
    }

    [Theory]
    [InlineData("1px solid #94a3b8", "#94a3b8")]
    [InlineData("2px dashed rgb(100, 116, 139)", "rgb(100, 116, 139)")]
    [InlineData("#334155", "#334155")]
    [InlineData("none", "transparent")]
    [InlineData("0", "transparent")]
    [InlineData("hidden", "transparent")]
    [InlineData("1px solid red", "red")]
    public void ExtractBorderColor_ExtractsCleanColor(string? input, string? expected)
    {
        Assert.Equal(expected, DesignTokenResolver.ExtractBorderColor(input));
    }

    [Theory]
    [InlineData("#ffffff", true)]
    [InlineData("rgb(37, 99, 235)", true)]
    [InlineData("12px", true)]
    [InlineData("var(--etl-accent)", true)]
    [InlineData("0 4px 6px -1px rgba(0, 0, 0, 0.1)", true)]
    [InlineData("red; color: blue;", false)]
    [InlineData("red; } body { display: none; }", false)]
    [InlineData("url(https://evil.com/leak)", false)]
    [InlineData("expression(alert(1))", false)]
    [InlineData("@import 'evil.css'", false)]
    [InlineData("var(--portal-secret)", false)]
    [InlineData("value\nwith\nnewlines", false)]
    [InlineData("value\0null", false)]
    public void IsSafeCssValue_ValidatesCorrectly(string? cssValue, bool expectedSafe)
    {
        Assert.Equal(expectedSafe, DesignTokens.IsSafeCssValue(cssValue));
    }

    [Fact]
    public void ResolveScopedTokens_MapsStandardStyleProperties()
    {
        var styles = new Dictionary<string, string>
        {
            ["BACKGROUND"] = "#1e293b",
            ["COLOR"] = "#f8fafc",
            ["TEXT_MUTED"] = "#94a3b8",
            ["BORDER"] = "1px solid #334155",
            ["ACCENT"] = "#38bdf8",
            ["SUCCESS"] = "#22c55e",
            ["DANGER"] = "#ef4444",
            ["WARNING"] = "#f59e0b",
            ["INFO"] = "#0ea5e9",
            ["BORDER_RADIUS"] = "10px",
            ["SHADOW"] = "ON",
            ["FONT"] = "Inter, sans-serif",
            ["FONT_MONO"] = "Fira Code, monospace"
        };

        var tokens = DesignTokenResolver.ResolveScopedTokens(styles, isPageOrReportLevel: true);

        Assert.Equal("#1e293b", tokens[DesignTokens.SurfaceCard]);
        Assert.Equal("#1e293b", tokens[DesignTokens.Surface]);
        Assert.Equal("#1e293b", tokens[DesignTokens.Bg]);
        Assert.Equal("#f8fafc", tokens[DesignTokens.TextPrimary]);
        Assert.Equal("#f8fafc", tokens[DesignTokens.Text]);
        Assert.Equal("#94a3b8", tokens[DesignTokens.TextMuted]);
        Assert.Equal("#94a3b8", tokens[DesignTokens.TextSecondary]);
        Assert.Equal("#334155", tokens[DesignTokens.Border]);
        Assert.Equal("#38bdf8", tokens[DesignTokens.Accent]);
        Assert.Equal("#22c55e", tokens[DesignTokens.Success]);
        Assert.Equal("#ef4444", tokens[DesignTokens.Danger]);
        Assert.Equal("#f59e0b", tokens[DesignTokens.Warning]);
        Assert.Equal("#0ea5e9", tokens[DesignTokens.Info]);
        Assert.Equal("10px", tokens[DesignTokens.RadiusMd]);
        Assert.Equal("5px", tokens[DesignTokens.RadiusSm]);
        Assert.Equal("15px", tokens[DesignTokens.RadiusLg]);
        Assert.Equal("0 6px 18px rgba(15, 23, 42, 0.16)", tokens[DesignTokens.Shadow]);
        Assert.Equal("Inter, sans-serif", tokens[DesignTokens.FontFamily]);
        Assert.Equal("Fira Code, monospace", tokens[DesignTokens.FontMono]);
    }

    [Fact]
    public void ResolveScopedTokens_RejectsUnsafeValuesAndHostVariables()
    {
        var styles = new Dictionary<string, string>
        {
            ["--portal-private"] = "#ff0000",
            ["--etl-accent"] = "#6366f1",
            ["COLOR"] = "red; background: blue",
            ["BACKGROUND"] = "var(--portal-secret)",
            ["BORDER"] = "url(http://evil.com/test.png)"
        };

        var tokens = DesignTokenResolver.ResolveScopedTokens(styles);

        Assert.Single(tokens);
        Assert.True(tokens.ContainsKey(DesignTokens.Accent));
        Assert.Equal("#6366f1", tokens[DesignTokens.Accent]);
        Assert.False(tokens.ContainsKey("--portal-private"));
        Assert.False(tokens.ContainsKey(DesignTokens.TextPrimary));
        Assert.False(tokens.ContainsKey(DesignTokens.SurfaceCard));
        Assert.False(tokens.ContainsKey(DesignTokens.Border));
    }

    [Fact]
    public async Task ManifestBuilder_PopulatesDesignTokensAcrossHierarchy()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;

        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            SET REPORT THEME = 'dark';

            CREATE STYLE MyPageStyle AS (
                BORDER_RADIUS = '12px'
            );

            CREATE STYLE MyContainerStyle AS (
                BACKGROUND = '#f8fafc',
                BORDER = '1px solid #cbd5e1'
            );

            CREATE STYLE MyVisualOverride AS (
                ACCENT = '#ec4899'
            );

            CREATE VISUAL SalesChart AS BAR (
                SOURCE = #data,
                MAPPINGS (X = Cat, Y = Val),
                STYLE = MyVisualOverride
            );

            CREATE CONTAINER MainContainer AS BOX (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ('A' = SalesChart)
                ),
                STYLE = MyContainerStyle
            );

            CREATE PAGE Overview AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ('A' = MainContainer)
                ),
                STYLE = MyPageStyle
            );
            """;

        await evaluator.Evaluate(Parse(script));

        var manifestBuilder = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await manifestBuilder.BuildAsync("test.rptsql");

        Assert.NotNull(manifest);

        // Page tokens
        var page = manifest.Pages.Single(p => p.Name == "Overview");
        Assert.NotNull(page.DesignTokens);
        Assert.Equal("12px", page.DesignTokens[DesignTokens.RadiusMd]);

        // Container tokens
        var container = manifest.Containers.Single(c => c.Name == "MainContainer");
        Assert.NotNull(container.DesignTokens);
        Assert.Equal("#f8fafc", container.DesignTokens[DesignTokens.SurfaceCard]);
        Assert.Equal("#cbd5e1", container.DesignTokens[DesignTokens.Border]);

        // Visual tokens (scoped override only)
        var visual = manifest.Visuals.Single(v => v.Name == "SalesChart");
        Assert.NotNull(visual.DesignTokens);
        Assert.Single(visual.DesignTokens);
        Assert.Equal("#ec4899", visual.DesignTokens[DesignTokens.Accent]);
    }

    [Fact]
    public async Task ManifestBuilder_LayerContainer_And_CustomThemes_PopulateDesignTokens()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;

        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            CREATE THEME CorporateDark AS (
                BACKGROUND = '#0f172a',
                TEXT_COLOR = '#f8fafc',
                ACCENT_COLOR = '#38bdf8',
                BORDER_COLOR = '#1e293b'
            );

            CREATE STYLE LayerStyle AS (
                ACCENT = '#10b981',
                BORDER = '1px solid #059669'
            );

            CREATE STYLE PageStyle AS (
                THEME = 'CorporateDark'
            );

            CREATE VISUAL LayerChart AS BAR (
                SOURCE = #data,
                MAPPINGS (X = Cat, Y = Val)
            );

            CREATE CONTAINER LayerContainer AS LAYER (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ('A' = LayerChart)
                ),
                STYLE = LayerStyle
            );

            CREATE PAGE ThemedPage AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ('A' = LayerContainer)
                ),
                STYLE = PageStyle
            );
            """;

        await evaluator.Evaluate(Parse(script));

        var manifestBuilder = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await manifestBuilder.BuildAsync("test.rptsql");

        Assert.NotNull(manifest);

        // Custom theme in manifest
        Assert.NotNull(manifest.CustomThemes);
        var customTheme = manifest.CustomThemes.Single(t => t.Name == "CorporateDark");
        Assert.NotNull(customTheme.DesignTokens);
        Assert.Equal("#0f172a", customTheme.DesignTokens[DesignTokens.Bg]);
        Assert.Equal("#f8fafc", customTheme.DesignTokens[DesignTokens.TextPrimary]);
        Assert.Equal("#38bdf8", customTheme.DesignTokens[DesignTokens.Accent]);
        Assert.Equal("#1e293b", customTheme.DesignTokens[DesignTokens.Border]);

        // Page referencing custom theme
        var page = manifest.Pages.Single(p => p.Name == "ThemedPage");
        Assert.NotNull(page.DesignTokens);
        Assert.Equal("#0f172a", page.DesignTokens[DesignTokens.Bg]);
        Assert.Equal("#38bdf8", page.DesignTokens[DesignTokens.Accent]);

        // Layer container
        var container = manifest.Containers.Single(c => c.Name == "LayerContainer");
        Assert.NotNull(container.DesignTokens);
        Assert.Equal("#10b981", container.DesignTokens[DesignTokens.Accent]);
        Assert.Equal("#059669", container.DesignTokens[DesignTokens.Border]);
    }

    [Fact]
    public async Task ManifestBuilder_PageThemeOverridesReportThemeWithoutRestampingReportTokensOnContainer()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;

        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            CREATE THEME ReportTheme AS (
                BACKGROUND = '#111827',
                ACCENT_COLOR = '#ef4444'
            );

            SET REPORT THEME = 'ReportTheme';

            CREATE STYLE LightPage AS (
                THEME = 'light'
            );

            CREATE STYLE LocalContainer AS (
                BORDER = '1px solid #94a3b8'
            );

            CREATE VISUAL SalesChart AS BAR (
                SOURCE = #data,
                MAPPINGS (X = Cat, Y = Val)
            );

            CREATE CONTAINER MainContainer AS BOX (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ('A' = SalesChart)
                ),
                STYLE = LocalContainer
            );

            CREATE PAGE Overview AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ('A' = MainContainer)
                ),
                STYLE = LightPage
            );
            """;

        await evaluator.Evaluate(Parse(script));

        var manifest = await new ManifestBuilder(evaluator, maxVisualParallelism: 1).BuildAsync("test.rptsql");
        Assert.Equal("#ef4444", manifest.DesignTokens![DesignTokens.Accent]);

        var page = manifest.Pages.Single(p => p.Name == "Overview");
        Assert.Equal(DesignTokens.LightTokens[DesignTokens.Accent], page.DesignTokens![DesignTokens.Accent]);
        Assert.Equal(DesignTokens.LightTokens[DesignTokens.Bg], page.DesignTokens[DesignTokens.Bg]);

        var container = manifest.Containers.Single(c => c.Name == "MainContainer");
        Assert.Equal("#94a3b8", container.DesignTokens![DesignTokens.Border]);
        Assert.False(container.DesignTokens.ContainsKey(DesignTokens.Accent));
        Assert.False(container.DesignTokens.ContainsKey(DesignTokens.Bg));
    }

    [Fact]
    public async Task ManifestBuilder_RefreshVisual_CopiesDesignTokens()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;

        const string script = """
            SELECT 'A' AS Cat, 10 AS Val INTO #data;

            CREATE STYLE ChartStyle AS (
                ACCENT = '#ec4899'
            );

            CREATE VISUAL SalesChart AS BAR (
                SOURCE = #data,
                MAPPINGS (X = Cat, Y = Val),
                STYLE = ChartStyle
            );
            """;

        await evaluator.Evaluate(Parse(script));

        var manifestBuilder = new ManifestBuilder(evaluator, maxVisualParallelism: 1);
        var manifest = await manifestBuilder.BuildAsync("test.rptsql");
        var visual = manifest.Visuals.Single(v => v.Name == "SalesChart");
        Assert.NotNull(visual.DesignTokens);
        Assert.Equal("#ec4899", visual.DesignTokens[DesignTokens.Accent]);

        var vStmt = ((IExecutionContext)evaluator).ReportContext.VisualDefinitions["SalesChart"];
        await manifestBuilder.RefreshVisualAsync(vStmt, visual);

        Assert.NotNull(visual.DesignTokens);
        Assert.Equal("#ec4899", visual.DesignTokens[DesignTokens.Accent]);
    }

    [Fact]
    public void HtmlSanitizer_AllowsEtlDesignTokens_RejectsPortalTokens()
    {
        var sanitizer = new HtmlSanitizer();

        const string safeCss = """
            .card {
                background: var(--etl-surface-card);
                color: var(--etl-text-primary);
                border: 1px solid var(--etl-border);
                border-radius: var(--etl-radius-md);
            }
            """;

        var violationsSafe = sanitizer.ValidateCss(safeCss);
        Assert.Empty(violationsSafe);

        const string unsafeCss = """
            .card {
                background: var(--portal-theme-bg);
            }
            """;

        var violationsUnsafe = sanitizer.ValidateCss(unsafeCss);
        Assert.NotEmpty(violationsUnsafe);
        Assert.Contains(violationsUnsafe, v => v.Category == SanitizationCategory.Css);
    }
}
