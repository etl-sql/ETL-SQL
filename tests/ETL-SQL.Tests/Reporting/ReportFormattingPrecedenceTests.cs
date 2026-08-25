using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Reporting;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Report formatting is resolved on the server and never inferred from the viewer's machine:
    /// <c>SET REPORT TIME_ZONE</c> then <c>Scheduler:DefaultTimeZone</c> then UTC;
    /// <c>SET REPORT LOCALE</c> then <c>Reporting:DefaultLocale</c> then invariant;
    /// visual <c>OPTIONS (NULL_LABEL)</c> then <c>SET REPORT NULL_LABEL</c> then
    /// <c>Reporting:DefaultNullLabel</c> then "-".
    /// </summary>
    public class ReportFormattingPrecedenceTests
    {
        private const string TemporalSource = @"
SELECT '2026-03-01T12:00:00+00:00' AS ObservedTime, 1234.5 AS Amount INTO #series
UNION ALL SELECT '2026-03-02T12:00:00+00:00', 2345.5;
";

        // Parsing ────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("TIME_ZONE", "America/New_York")]
        [InlineData("LOCALE", "de-DE")]
        [InlineData("NULL_LABEL", "n/a")]
        public void SetReport_FormattingKeys_Parse(string key, string value)
        {
            var script = $"SET REPORT {key} = '{value}';";
            var statement = (SetReportMetadataStatement)new Parser(new Lexer(script).Tokenize(), script).ParseStatement();

            Assert.Equal(key, statement.Key);
            Assert.Equal(value, statement.Value);
        }

        [Fact]
        public void SetReport_UnknownKey_IsRejectedAtParseTimeWithPosition()
        {
            const string script = "SET REPORT TIMEZONE = 'UTC';";

            var error = Assert.Throws<SyntaxException>(() => new Parser(new Lexer(script).Tokenize(), script).ParseStatement());

            Assert.Contains("TIMEZONE", error.Message);
            Assert.Contains("TIME_ZONE", error.Message);
            Assert.True(error.Line > 0);
        }

        [Fact]
        public void ReportMetadataKeys_AreOneClosedContract()
        {
            // The parser's closed set and the handler's switch are one contract; drift silently
            // re-creates the no-op this work removed.
            Assert.Equal(15, ReportMetadataKeys.All.Count);
            Assert.All(ReportMetadataKeys.All, key => Assert.True(ReportMetadataKeys.IsKnown(key)));
            Assert.False(ReportMetadataKeys.IsKnown("NOT_A_KEY"));
        }

        // Handler validation ─────────────────────────────────────────────────

        [Fact]
        public async Task SetReport_TimeZone_IsValidatedThroughTheSharedResolver()
        {
            var evaluator = Evaluator();
            // Abbreviations the rest of the language accepts must be accepted here too.
            await Run(evaluator, "SET REPORT TIME_ZONE = 'EST';");
            Assert.Equal("EST", evaluator.ReportContext.ReportTimeZone);

            var error = await Assert.ThrowsAsync<ExecutionException>(
                () => Run(Evaluator(), "SET REPORT TIME_ZONE = 'Mars/Olympus';"));
            Assert.Contains("Mars/Olympus", error.Message);
        }

        [Fact]
        public async Task SetReport_Locale_IsValidatedThroughCultureInfo()
        {
            var evaluator = Evaluator();
            await Run(evaluator, "SET REPORT LOCALE = 'de-DE';");
            Assert.Equal("de-DE", evaluator.ReportContext.ReportLocale);

            var error = await Assert.ThrowsAsync<ExecutionException>(
                () => Run(Evaluator(), "SET REPORT LOCALE = 'not-a-locale';"));
            Assert.Contains("not-a-locale", error.Message);
        }

        // Configuration tier ─────────────────────────────────────────────────

        [Fact]
        public void FromConfiguration_AbsentKeys_FallBackToInvariantUtcAndDash()
        {
            var settings = ReportFormattingSettings.FromConfiguration(Configuration());

            Assert.Equal(ReportFormattingSettings.InvariantLocale, settings.Locale);
            Assert.Equal("UTC", settings.TimeZone);
            Assert.Equal("-", settings.NullLabel);
        }

        [Fact]
        public void FromConfiguration_ShippedDefaults_MatchTheFallback()
        {
            var settings = ReportFormattingSettings.FromConfiguration(Configuration(
                ("Scheduler:DefaultTimeZone", "UTC"),
                ("Reporting:DefaultLocale", ""),
                ("Reporting:DefaultNullLabel", "-")));

            Assert.Equal(ReportFormattingSettings.Default, settings);
        }

        [Fact]
        public void FromConfiguration_InvalidValues_FailInsteadOfSilentlyDegrading()
        {
            Assert.Throws<ArgumentException>(() =>
                ReportFormattingSettings.FromConfiguration(Configuration(("Scheduler:DefaultTimeZone", "Mars/Olympus"))));
            Assert.Throws<ArgumentException>(() =>
                ReportFormattingSettings.FromConfiguration(Configuration(("Reporting:DefaultLocale", "not-a-locale"))));
        }

        [Fact]
        public void FromConfiguration_ExplicitlyEmptyNullLabel_IsAChoiceNotAnAbsence()
        {
            var settings = ReportFormattingSettings.FromConfiguration(Configuration(("Reporting:DefaultNullLabel", "")));
            Assert.Equal(string.Empty, settings.NullLabel);
        }

        // Precedence ─────────────────────────────────────────────────────────

        [Fact]
        public async Task ConfiguredDefaults_ApplyWhenTheScriptSetsNothing()
        {
            var manifest = await BuildReport(NamedChartScript(), new Dictionary<string, string?>
            {
                ["Scheduler:DefaultTimeZone"] = "Asia/Tokyo",
                ["Reporting:DefaultLocale"] = "de-DE",
                ["Reporting:DefaultNullLabel"] = "n/d"
            });

            Assert.Equal("Asia/Tokyo", manifest.Formatting.TimeZone);
            Assert.Equal("de-DE", manifest.Formatting.Locale);
            Assert.Equal("n/d", manifest.Formatting.NullLabel);
        }

        [Fact]
        public async Task SetReport_OutranksConfiguration()
        {
            var script = @"
SET REPORT TIME_ZONE = 'America/New_York';
SET REPORT LOCALE = 'fr-FR';
SET REPORT NULL_LABEL = 'none';
" + NamedChartScript();

            var manifest = await BuildReport(script, new Dictionary<string, string?>
            {
                ["Scheduler:DefaultTimeZone"] = "Asia/Tokyo",
                ["Reporting:DefaultLocale"] = "de-DE",
                ["Reporting:DefaultNullLabel"] = "n/d"
            });

            Assert.Equal("America/New_York", manifest.Formatting.TimeZone);
            Assert.Equal("fr-FR", manifest.Formatting.Locale);
            Assert.Equal("none", manifest.Formatting.NullLabel);
            Assert.Equal("none", Visual(manifest, "Chart").ChartSpec!.Formatting.NullLabel);
        }

        [Fact]
        public async Task VisualNullLabel_IsTheMostSpecificOverride()
        {
            var manifest = await BuildReport(@"
SET REPORT NULL_LABEL = 'report-level';
" + TemporalSource + @"
CREATE VISUAL Chart AS BAR (
    SOURCE = #series,
    MAPPINGS (X = ObservedTime, Y = Amount),
    OPTIONS (NULL_LABEL = 'visual-level')
);
");

            Assert.Equal("visual-level", Visual(manifest, "Chart").ChartSpec!.Formatting.NullLabel);
            // The report-level value still travels on the manifest for everything else on the page.
            Assert.Equal("report-level", manifest.Formatting.NullLabel);
        }

        [Fact]
        public async Task NullValues_RenderAsTheResolvedNullLabel()
        {
            var manifest = await BuildReport(@"
SET REPORT NULL_LABEL = '(missing)';

SELECT 'A' AS Category, 10.0 AS Amount INTO #bars
UNION ALL SELECT 'B', NULL;

CREATE VISUAL Chart AS BAR (SOURCE = #bars, MAPPINGS (X = Category, Y = Amount));
");

            var plan = Visual(manifest, "Chart").PlotPlan!;
            var rendered = string.Join("|", plan.Fallback.Items.Select(item => item.Value));

            Assert.Contains("(missing)", rendered);
        }

        // Time zone and locale actually change output ────────────────────────

        [Fact]
        public async Task TimeZone_MovesTemporalDisplayValues()
        {
            var utc = await TemporalDisplay("SET REPORT TIME_ZONE = 'UTC';");
            var tokyo = await TemporalDisplay("SET REPORT TIME_ZONE = 'Asia/Tokyo';");

            Assert.Equal("2026-03-01 12:00:00", utc.First());
            Assert.Equal("2026-03-01 21:00:00", tokyo.First());
        }

        [Fact]
        public async Task Locale_ChangesTemporalRendering()
        {
            var german = await TemporalDisplay("SET REPORT LOCALE = 'de-DE';");

            Assert.Equal("01.03.2026 12:00:00", german.First());
        }

        [Fact]
        public async Task NoScriptOrConfiguration_RendersInvariantUtcAndDash()
        {
            var manifest = await BuildReport(NamedChartScript());

            Assert.Equal(ReportFormattingSettings.Default.Locale, manifest.Formatting.Locale);
            Assert.Equal(ReportFormattingSettings.Default.TimeZone, manifest.Formatting.TimeZone);
            Assert.Equal(ReportFormattingSettings.Default.NullLabel, manifest.Formatting.NullLabel);
        }

        // The two lowerers agree ─────────────────────────────────────────────

        [Fact]
        public async Task NamedAndCustomCharts_ResolveTheSameFormattingAndTheme()
        {
            var manifest = await BuildReport(@"
SET REPORT TIME_ZONE = 'Europe/Paris';
SET REPORT LOCALE = 'fr-FR';
SET REPORT NULL_LABEL = 'rien';

SELECT 'A' AS Category, 10.0 AS Amount INTO #bars
UNION ALL SELECT 'B', 20.0;

CREATE STYLE BrandStyle AS (THEME = 'dark', COLOR = '#112233');

CREATE VISUAL Named AS BAR (SOURCE = #bars, MAPPINGS (X = Category, Y = Amount), STYLE = BrandStyle);

CREATE VISUAL Custom AS CUSTOM (
    SOURCE = #bars,
    STYLE = BrandStyle,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            cats    = BAND (CHANNEL = X, ORDER = SOURCE),
            amounts = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
        ),
        LAYERS (
            bars = RECT (
                ENCODINGS (
                    X = Category (TYPE = ORDINAL, SCALE = cats),
                    Y = Amount (TYPE = QUANTITATIVE, SCALE = amounts)
                )
            )
        )
    )
);
");

            var named = Visual(manifest, "Named").ChartSpec!;
            var custom = Visual(manifest, "Custom").ChartSpec!;

            Assert.Equal(named.Formatting.Locale, custom.Formatting.Locale);
            Assert.Equal(named.Formatting.TimeZone, custom.Formatting.TimeZone);
            Assert.Equal(named.Formatting.NullLabel, custom.Formatting.NullLabel);
            Assert.Equal("rien", custom.Formatting.NullLabel);

            // A CREATE STYLE theme used to reach BAR and stop at CUSTOM.
            Assert.Equal(named.Theme.Name, custom.Theme.Name);
            Assert.Equal("dark", custom.Theme.Name);
            Assert.Contains(custom.Theme.Tokens, token =>
                token.Name.Equals("COLOR", StringComparison.OrdinalIgnoreCase) && token.Value == "#112233");
            // Both paths now build tokens from the same resolved styles and options. The only names
            // named visuals still carry that CUSTOM does not are the mapping:* cross-filter hints, which
            // CUSTOM has no MAPPINGS clause to produce — tracked separately in TODO.md.
            var onlyOnNamed = named.Theme.Tokens.Select(token => token.Name)
                .Except(custom.Theme.Tokens.Select(token => token.Name), StringComparer.OrdinalIgnoreCase);
            Assert.All(onlyOnNamed, name => Assert.StartsWith("mapping:", name, StringComparison.OrdinalIgnoreCase));
            Assert.Empty(custom.Theme.Tokens.Select(token => token.Name)
                .Except(named.Theme.Tokens.Select(token => token.Name), StringComparer.OrdinalIgnoreCase));
        }

        // Survives context lifecycle ─────────────────────────────────────────

        [Fact]
        public void Clone_CarriesOverridesAndDefaultsIntoParallelBranches()
        {
            var registry = new ReportRegistry(Configuration(("Reporting:DefaultNullLabel", "cfg")))
            {
                ReportTimeZone = "Asia/Tokyo",
                ReportLocale = "de-DE"
            };

            var clone = registry.Clone();

            Assert.Equal("Asia/Tokyo", clone.ReportTimeZone);
            Assert.Equal("de-DE", clone.ReportLocale);
            Assert.Equal("cfg", clone.FormattingDefaults.NullLabel);
            Assert.Equal(registry.EffectiveFormatting, clone.EffectiveFormatting);
        }

        [Fact]
        public void Clear_DropsScriptOverridesButKeepsConfiguredDefaults()
        {
            var registry = new ReportRegistry(Configuration(
                ("Scheduler:DefaultTimeZone", "Asia/Tokyo"),
                ("Reporting:DefaultNullLabel", "cfg")))
            {
                ReportTimeZone = "America/New_York",
                ReportNullLabel = "script"
            };

            registry.Clear();

            Assert.Null(registry.ReportTimeZone);
            Assert.Null(registry.ReportNullLabel);
            Assert.Equal("Asia/Tokyo", registry.EffectiveFormatting.TimeZone);
            Assert.Equal("cfg", registry.EffectiveFormatting.NullLabel);
        }

        [Fact]
        public async Task Fork_PreservesFormattingForParallelVisualBuilds()
        {
            var evaluator = Evaluator();
            await Run(evaluator, "SET REPORT TIME_ZONE = 'Asia/Tokyo'; SET REPORT NULL_LABEL = 'none';");

            var fork = evaluator.Fork();

            Assert.Equal(evaluator.ReportContext.EffectiveFormatting, fork.ReportContext.EffectiveFormatting);
        }

        [Fact]
        public async Task InteractionRefresh_KeepsTheSameResolvedFormatting()
        {
            var evaluator = Evaluator();
            await Run(evaluator, "SET REPORT LOCALE = 'de-DE'; SET REPORT TIME_ZONE = 'Asia/Tokyo';" + NamedChartScript());

            var first = await new ManifestBuilder(evaluator).BuildAsync("report.rptsql");
            var refreshed = await new ManifestBuilder(evaluator).BuildAsync("report.rptsql",
                new Dictionary<string, string> { ["@region"] = "EMEA" });

            Assert.Equal(first.Formatting.Locale, refreshed.Formatting.Locale);
            Assert.Equal(first.Formatting.TimeZone, refreshed.Formatting.TimeZone);
            Assert.Equal(first.Formatting.NullLabel, refreshed.Formatting.NullLabel);
            var before = Visual(first, "Chart").ChartSpec!.Formatting;
            var after = Visual(refreshed, "Chart").ChartSpec!.Formatting;
            Assert.Equal(before.Locale, after.Locale);
            Assert.Equal(before.TimeZone, after.TimeZone);
            Assert.Equal(before.NullLabel, after.NullLabel);
        }

        // Every output surface reads the same resolved values ────────────────

        [Fact]
        public async Task EveryRenderedSurface_ReadsOneResolvedFormatting()
        {
            var manifest = await BuildReport(@"
SET REPORT TIME_ZONE = 'Asia/Tokyo';
SET REPORT LOCALE = 'de-DE';
SET REPORT NULL_LABEL = 'kein Wert';
" + NamedChartScript());

            var visual = Visual(manifest, "Chart");

            // Browser manifest and chart contract agree.
            Assert.Equal("Asia/Tokyo", manifest.Formatting.TimeZone);
            Assert.Equal("Asia/Tokyo", visual.ChartSpec!.Formatting.TimeZone);
            Assert.Equal("de-DE", visual.ChartSpec.Formatting.Locale);
            Assert.Equal("kein Wert", visual.ChartSpec.Formatting.NullLabel);

            // Browser SVG, PDF, email, and terminal all render from the resolved plan and the semantic
            // fallback, so pinning the resolved instant here pins every one of them.
            Assert.Contains("01.03.2026 21:00:00", string.Join("|", visual.ChartData!.Columns
                .Single(column => column.Name == "ObservedTime").DisplayValues));
            Assert.NotNull(visual.PlotPlan);
            Assert.NotNull(visual.SemanticFallback);
            Assert.Contains("01.03.2026 21:00:00", string.Join("|", visual.PlotPlan!.Fallback.Items.Select(item => item.Label)));
            Assert.Contains("01.03.2026", visual.NativeSvg);
        }

        // Helpers ────────────────────────────────────────────────────────────

        private static string NamedChartScript() => TemporalSource + @"
CREATE VISUAL Chart AS BAR (SOURCE = #series, MAPPINGS (X = ObservedTime, Y = Amount));
";

        private static VisualManifest Visual(ReportManifest manifest, string name) =>
            manifest.Visuals.Single(visual => visual.Name == name);

        private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
                .Build();

        private static Evaluator Evaluator(Dictionary<string, string?>? configOverrides = null)
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider(configOverrides).GetRequiredService<Evaluator>();
            evaluator.RedirectOutput = true;
            evaluator.DisplayExecuteTree = false;
            return evaluator;
        }

        private static Task Run(Evaluator evaluator, string script) =>
            evaluator.Evaluate(new Parser(new Lexer(script).Tokenize(), script).Parse());

        private static async Task<ReportManifest> BuildReport(string script, Dictionary<string, string?>? configOverrides = null)
        {
            var evaluator = Evaluator(configOverrides);
            await Run(evaluator, script);
            return await new ManifestBuilder(evaluator).BuildAsync("report.rptsql");
        }

        private static async Task<ImmutableArray<string?>> TemporalDisplay(string preamble)
        {
            var manifest = await BuildReport(preamble + NamedChartScript());
            return Visual(manifest, "Chart").ChartData!.Columns.Single(column => column.Name == "ObservedTime").DisplayValues;
        }
    }
}
