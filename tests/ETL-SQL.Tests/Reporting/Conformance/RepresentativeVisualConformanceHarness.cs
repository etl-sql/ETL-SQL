using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.ReportBuilder;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Spectre.Console.Rendering;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Reporting.Conformance;

/// <summary>
/// Reusable expected semantic projection for a visual fixture.
/// Serves as the golden semantic reference model for PlotPlan conformance and renderer cross-validation.
/// </summary>
public record RepresentativeSemanticProjection(
    string FixtureFileName,
    VisualType ExpectedVisualType,
    IReadOnlyList<string> ExpectedCategories,
    IReadOnlyList<string> ExpectedSeriesNames,
    bool HasExplicitDomain,
    bool HasNullGaps,
    bool HasDualAxes,
    IReadOnlyList<OverlayType> ExpectedOverlays,
    IReadOnlyDictionary<string, string> ExpectedPalette,
    string AccessibleSummary,
    IReadOnlyDictionary<string, string> BackendExpectations);

/// <summary>
/// Harness for discovering, evaluating, and cross-validating representative visual fixtures
/// across AST, ReportManifest, ECharts, SvgChartRenderer, and TerminalRenderer surfaces.
/// </summary>
public static class RepresentativeVisualConformanceHarness
{
    private static readonly Dictionary<string, RepresentativeSemanticProjection> _registry = BuildRegistry();

    public static IReadOnlyDictionary<string, RepresentativeSemanticProjection> Registry => _registry;

    public static string GetRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ETL-SQL.slnx")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        return Directory.GetCurrentDirectory();
    }

    public static async Task<(Script Ast, ReportManifest Manifest, Evaluator Evaluator)> CompileFixtureAsync(string fixtureFileName)
    {
        var repoRoot = GetRepoRoot();
        var fixturePath = Path.Combine(repoRoot, "tests", "fixtures", "reporting", "conformance", fixtureFileName);

        if (!File.Exists(fixturePath))
            throw new FileNotFoundException($"Conformance fixture not found: {fixturePath}");

        var script = await File.ReadAllTextAsync(fixturePath);
        return await CompileScriptAsync(script, fixturePath, $"Fixture '{fixtureFileName}'");
    }

    /// <summary>
    /// Compiles Report-SQL that is not a fixture on disk — script text that lives somewhere else and
    /// still has to prove it produces a real report, such as the sample Studio seeds for a first run.
    /// </summary>
    /// <param name="script">The Report-SQL to run.</param>
    /// <param name="sourcePath">Path recorded on the manifest; it need not exist.</param>
    /// <param name="description">What to name in a parse failure.</param>
    public static async Task<(Script Ast, ReportManifest Manifest, Evaluator Evaluator)> CompileScriptAsync(
        string script, string sourcePath, string description)
    {
        var tokens = new Lexer(script).Tokenize();
        var ast = new CoreParser(tokens, script).Parse();

        if (ast.Diagnostics.Any(d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error))
        {
            var errors = string.Join("; ", ast.Diagnostics.Select(d => d.Message));
            throw new InvalidOperationException($"{description} failed to parse: {errors}");
        }

        var evaluator = CreateConformanceEvaluator();
        await evaluator.Evaluate(ast);

        var manifestBuilder = new ManifestBuilder(evaluator);
        var manifest = await manifestBuilder.BuildAsync(sourcePath);

        return (ast, manifest, evaluator);
    }

    public static string? RenderEChartsJson(ReportManifest manifest, string visualName)
    {
        var visual = manifest.Visuals.FirstOrDefault(v => v.Name.Equals(visualName, StringComparison.OrdinalIgnoreCase));
        if (visual == null) return null;
        return new SvgChartRenderer().Render(visual);
    }

    public static string? RenderSvg(ReportManifest manifest, string visualName)
    {
        var visual = manifest.Visuals.FirstOrDefault(v => v.Name.Equals(visualName, StringComparison.OrdinalIgnoreCase));
        if (visual == null) return null;
        return new SvgChartRenderer().Render(visual);
    }

    public static IRenderable? RenderTerminal(ReportManifest manifest)
    {
        var page = manifest.Pages.FirstOrDefault();
        if (page == null) return null;
        return TerminalRenderer.RenderPage(page, manifest);
    }

    private static Evaluator CreateConformanceEvaluator()
    {
        var services = new ServiceCollection();
        var logger = NullLogger.Instance;
        var sec = new ETL_SQL.Services.SecurityService(logger) { IsTestMode = true };
        var connRegistry = new ConnectorRegistry();
        connRegistry.Register(new ETL_SQL.Connectors.MockDb.MockDbConnector());
        connRegistry.Register(new ETL_SQL.Connectors.FlatFile.FlatFileConnector());

        services.AddSingleton<Common.ILogger>(logger);
        services.AddSingleton(sec);
        services.AddSingleton<IConnectorRegistry>(connRegistry);
        services.AddSingleton<IFunctionRegistry, FunctionRegistry>();
        services.AddSingleton<ILineageTracker, LineageTracker>();
        services.AddSingleton<IDockerManager>(new Mock<IDockerManager>().Object);
        services.AddSingleton<ISessionStateManager>(new SessionStateManager(logger, sec, new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object, new SqliteSessionMetadataStoreFactory(), null));
        services.AddSingleton<ILanguageHelpRegistry, LanguageHelpRegistry>();
        services.AddSingleton<EvaluatorComponentRegistry>();
        services.AddSingleton<IReportContext, ReportRegistry>();
        services.AddTransient<Evaluator>();

        var handlers = new[]
        {
            typeof(DeclareStatementHandler),
            typeof(SetVariableStatementHandler),
            typeof(SelectStatementHandler),
            typeof(InsertStatementHandler),
            typeof(ExecutePushdownStatementHandler),
            typeof(CreateTableStatementHandler),
            typeof(CreateConnectionStatementHandler),
            typeof(CreateVisualStatementHandler),
            typeof(CreatePageStatementHandler),
            typeof(CreateDatasetStatementHandler),
            typeof(CreateContainerStatementHandler),
            typeof(CreateNavigationStatementHandler),
            typeof(CreateButtonStatementHandler),
            typeof(CreateStyleStatementHandler),
            typeof(CreateThemeStatementHandler),
            typeof(SetReportMetadataStatementHandler),
            typeof(ExportReportStatementHandler)
        };

        foreach (var h in handlers)
        {
            services.AddTransient(typeof(IStatementHandler), h);
            services.AddTransient(h);
        }

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<Evaluator>();
    }

    private static Dictionary<string, RepresentativeSemanticProjection> BuildRegistry()
    {
        return new Dictionary<string, RepresentativeSemanticProjection>(StringComparer.OrdinalIgnoreCase)
        {
            ["bar_stable_ordering.rptsql"] = new(
                FixtureFileName: "bar_stable_ordering.rptsql",
                ExpectedVisualType: VisualType.Bar,
                ExpectedCategories: ["Alpha", "Beta", "Gamma", "Delta"],
                ExpectedSeriesNames: ["Actual", "Forecast"],
                HasExplicitDomain: false,
                HasNullGaps: false,
                HasDualAxes: false,
                ExpectedOverlays: [],
                ExpectedPalette: new Dictionary<string, string>(),
                AccessibleSummary: "Bar chart with deterministic Alpha, Beta, Gamma, Delta order across Actual and Forecast series",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Renders multi-series grouped bar with legend and categorical x-axis",
                    ["SVG"] = "Renders all resolved grouped series from the shared PlotPlan",
                    ["Terminal"] = "Renders the same ordered series and categories as a semantic table"
                }),

            ["bar_explicit_domain.rptsql"] = new(
                FixtureFileName: "bar_explicit_domain.rptsql",
                ExpectedVisualType: VisualType.Bar,
                ExpectedCategories: ["Dept A", "Dept B", "Dept C", "Dept D"],
                ExpectedSeriesNames: [],
                HasExplicitDomain: true,
                HasNullGaps: false,
                HasDualAxes: false,
                ExpectedOverlays: [],
                ExpectedPalette: new Dictionary<string, string>(),
                AccessibleSummary: "Headcount by department with explicit 0 to 500 Y-axis domain",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Applies min=0 and max=500 to yAxis option",
                    ["SVG"] = "Uses the resolved 0 to 500 PlotPlan domain",
                    ["Terminal"] = "Reports values from the plan whose scale retains the explicit domain"
                }),

            ["bar_multi_series_stacked.rptsql"] = new(
                FixtureFileName: "bar_multi_series_stacked.rptsql",
                ExpectedVisualType: VisualType.Bar,
                ExpectedCategories: ["2026-Q1", "2026-Q2"],
                ExpectedSeriesNames: ["Enterprise", "Mid-Market", "SMB"],
                HasExplicitDomain: false,
                HasNullGaps: false,
                HasDualAxes: false,
                ExpectedOverlays: [],
                ExpectedPalette: new Dictionary<string, string>
                {
                    ["Enterprise"] = "#1E3A8A",
                    ["Mid-Market"] = "#3B82F6",
                    ["SMB"] = "#93C5FD"
                },
                AccessibleSummary: "Quarterly revenue stacked bar partitioned by tier with custom blue color palette",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Applies stack='total' and explicit color map to series list",
                    ["SVG"] = "Renders stacked geometry with the resolved custom palette and stacked domain",
                    ["Terminal"] = "Renders every partitioned series from the same stacked plan"
                }),

            ["line_temporal_decimals.rptsql"] = new(
                FixtureFileName: "line_temporal_decimals.rptsql",
                ExpectedVisualType: VisualType.Line,
                ExpectedCategories: ["2026-01-01", "2026-01-02", "2026-01-03", "2026-01-04", "2026-01-05"],
                ExpectedSeriesNames: [],
                HasExplicitDomain: false,
                HasNullGaps: false,
                HasDualAxes: false,
                ExpectedOverlays: [],
                ExpectedPalette: new Dictionary<string, string>(),
                AccessibleSummary: "Continuous daily time series line tracking high-precision floating point telemetry",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Renders smooth line series with temporal category labels",
                    ["SVG"] = "SvgChartRenderer renders connected polyline vector paths",
                    ["Terminal"] = "TerminalRenderer uses BrailleCanvas 2x4 dot matrix for continuous curve"
                }),

            ["line_null_gaps.rptsql"] = new(
                FixtureFileName: "line_null_gaps.rptsql",
                ExpectedVisualType: VisualType.Line,
                ExpectedCategories: ["Day 1", "Day 2", "Day 3", "Day 4", "Day 5", "Day 6"],
                ExpectedSeriesNames: [],
                HasExplicitDomain: false,
                HasNullGaps: true,
                HasDualAxes: false,
                ExpectedOverlays: [],
                ExpectedPalette: new Dictionary<string, string>(),
                AccessibleSummary: "Discontinuous line chart with null values at Day 3 and Day 5",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Emits null entries in series data array, creating line gap when connectNulls is false",
                    ["SVG"] = "Breaks native line paths at the plan's explicit gap rows",
                    ["Terminal"] = "Labels the same null rows as semantic gaps"
                }),

            ["scatter_multi_series_inferred.rptsql"] = new(
                FixtureFileName: "scatter_multi_series_inferred.rptsql",
                ExpectedVisualType: VisualType.Scatter,
                ExpectedCategories: [],
                ExpectedSeriesNames: ["Cohort A", "Cohort B"],
                HasExplicitDomain: false,
                HasNullGaps: false,
                HasDualAxes: false,
                ExpectedOverlays: [],
                ExpectedPalette: new Dictionary<string, string>(),
                AccessibleSummary: "2D scatter plot correlating Velocity and Efficiency across Cohort A and Cohort B",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Renders scatter series with [x, y, size] coordinate tuples",
                    ["SVG"] = "Renders native points from resolved quantitative X/Y scales",
                    ["Terminal"] = "Reports both inferred cohorts and their resolved coordinates"
                }),

            ["pie_donut_proportions.rptsql"] = new(
                FixtureFileName: "pie_donut_proportions.rptsql",
                ExpectedVisualType: VisualType.Pie,
                ExpectedCategories: ["Search", "Direct", "Referral", "Organic Social"],
                ExpectedSeriesNames: [],
                HasExplicitDomain: false,
                HasNullGaps: false,
                HasDualAxes: false,
                ExpectedOverlays: [],
                ExpectedPalette: new Dictionary<string, string>(),
                AccessibleSummary: "Proportional lead distribution comparing standard PIE and 50% inner-radius DONUT",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Pie uses radius=[0, '70%']; Donut uses radius=['50%', '70%']",
                    ["SVG"] = "SvgChartRenderer renders SVG wedge arcs and center hole for donut",
                    ["Terminal"] = "TerminalRenderer renders percentage breakdown text table"
                }),

            ["combo_dual_axes.rptsql"] = new(
                FixtureFileName: "combo_dual_axes.rptsql",
                ExpectedVisualType: VisualType.Combo,
                ExpectedCategories: ["Week 1", "Week 2", "Week 3", "Week 4"],
                ExpectedSeriesNames: ["QualityPassRate", "UnitsProduced"],
                HasExplicitDomain: false,
                HasNullGaps: false,
                HasDualAxes: true,
                ExpectedOverlays: [],
                ExpectedPalette: new Dictionary<string, string>
                {
                    ["UnitsProduced"] = "#2563EB",
                    ["QualityPassRate"] = "#E11D48"
                },
                AccessibleSummary: "Dual-axis combo visual with Units Produced bar and Quality Pass Rate line",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Renders dual yAxis array (left value, right value) with distinct series bindings",
                    ["SVG"] = "Renders native bar and line layers against their resolved axes",
                    ["Terminal"] = "Reports both layers and their Y/Y2 values from the shared plan"
                }),

            ["rule_statistical_overlays.rptsql"] = new(
                FixtureFileName: "rule_statistical_overlays.rptsql",
                ExpectedVisualType: VisualType.Bar,
                ExpectedCategories: ["Sprint 1", "Sprint 2", "Sprint 3", "Sprint 4", "Sprint 5", "Sprint 6"],
                ExpectedSeriesNames: [],
                HasExplicitDomain: false,
                HasNullGaps: false,
                HasDualAxes: false,
                ExpectedOverlays: [OverlayType.Goal, OverlayType.Average, OverlayType.MovingAvg],
                ExpectedPalette: new Dictionary<string, string>(),
                AccessibleSummary: "Engineering velocity bar chart with GOAL(50), AVERAGE, and MOVING_AVG(2) statistical overlay lines",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Lowers overlays into markLine data items and moving average line series",
                    ["SVG"] = "SvgChartRenderer computes horizontal overlay benchmark lines",
                    ["Terminal"] = "TerminalRenderer draws horizontal threshold marker lines across terminal width"
                }),

            ["accessible_semantic_fallbacks.rptsql"] = new(
                FixtureFileName: "accessible_semantic_fallbacks.rptsql",
                ExpectedVisualType: VisualType.Bar,
                ExpectedCategories: ["Americas", "EMEA", "APAC"],
                ExpectedSeriesNames: [],
                HasExplicitDomain: false,
                HasNullGaps: false,
                HasDualAxes: false,
                ExpectedOverlays: [],
                ExpectedPalette: new Dictionary<string, string>(),
                AccessibleSummary: "Accessible sales dashboard with summary KPI card and tabular region breakdown",
                BackendExpectations: new Dictionary<string, string>
                {
                    ["ECharts"] = "Renders interactive visual with formatted data labels",
                    ["SVG"] = "SvgChartRenderer embeds vector chart into Markdown with accessible caption",
                    ["Terminal"] = "TerminalRenderer renders summary KPI card in Spectre Panel above chart"
                })
        };
    }
}
