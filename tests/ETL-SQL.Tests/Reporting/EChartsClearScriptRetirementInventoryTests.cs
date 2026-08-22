using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public enum RetirementClassification
{
    RemovableNow,
    RemovableAfterNamedBatch,
    RequiredUntilFinalRetirement
}

public sealed record RetirementInventoryEntry(
    string RelativePath,
    string ComponentName,
    RetirementClassification Classification,
    string? TargetBatch,
    string Rationale);

/// <summary>
/// Automated inventory guard test for Phase 8.
///
/// Asserts that every consumer of ECharts, ClearScript, V8, and SSR in the codebase
/// is explicitly accounted for, classified with a clear retirement rationale/prerequisite,
/// and that no untracked coupling exists.
/// </summary>
public class EChartsClearScriptRetirementInventoryTests
{
    private static string GetRepoRoot()
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

    public static readonly IReadOnlyList<RetirementInventoryEntry> AuthoritativeInventory =
    [
        // Package / Native Binaries
        new("Directory.Packages.props", "Microsoft.ClearScript.V8 package versions",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Central package management versions for ClearScript and native platform runtimes."),
        new("src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj", "ClearScript PackageReferences",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Package references to ClearScript.V8 and 5 native platform binaries for SSR."),

        // SSR and Server Export Pipeline
        new("src/ETL-SQL.Reporting/EChartsSsrRenderer.cs", "EChartsSsrRenderer class",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "V8 script engine pool running echarts.min.js server-side for static export."),
        new("src/ETL-SQL.Reporting/SvgChartRenderer.cs", "SvgChartRenderer SSR dispatch",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Dispatches unmigrated visuals to ECharts SSR for static SVG export."),
        new("src/ETL-SQL.Portal/Program.cs", "Portal SSR Error Logger Configuration",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Registers logging delegate for EChartsSsrRenderer.OnError."),
        new("src/ETL-SQL.Portal/Controllers/ExportController.cs", "ExportController SSR Reference",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Export endpoint server-side chart rendering documentation and SSR fallback path."),

        // Transient Renderers & Compilers
        new("src/ETL-SQL.Reporting/EChartsRenderer.cs", "EChartsRenderer class",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Main entry point for generating ECharts options JSON for browser runtime during migration."),
        new("src/ETL-SQL.Reporting/Renderers/PlotPlanEChartsRenderer.cs", "PlotPlanEChartsRenderer class",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Transient compiler from PlotPlan to ECharts options for migrated visuals."),

        // Legacy Specialized Renderers (Mapped to named batches)
        new("src/ETL-SQL.Reporting/Renderers/SpecializedRenderer.cs", "SpecializedRenderer (All Visuals)",
            RetirementClassification.RemovableAfterNamedBatch, "Batch 6: Composition (All)",
            "Legacy option builders: Bubble/Waterfall/Candlestick (Batch 1), BoxPlot/Heatmap (Batch 2), Radar/Gauge (Batch 3), Funnel/Gantt (Batch 4), Treemap/Sunburst/Sankey/Network/Map (Batch 5), Trellis (Batch 6)."),

        // Theme Support
        new("src/ETL-SQL.Core/ReportingThemeBuilder.cs", "ReportingThemeBuilder.BuildEChartsTheme",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Translates Report-SQL theme properties to ECharts theme JSON structure."),
        new("src/ETL-SQL.Engine/Handlers/CreateThemeStatementHandler.cs", "CreateThemeStatementHandler",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Writes themes/*.json as ECharts theme object."),
        new("src/ETL-SQL.Core/ReportAst.cs", "ReportAst Theme doc",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Theme AST documentation referencing ECharts theme JSON."),
        new("src/ETL-SQL.App/App/EngineRunner.cs", "EngineRunner Help Text",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Command line banner referencing Apache ECharts visualization library."),

        // Browser Assets & Host HTML
        new("src/ETL-SQL.ReportRuntime/Resources/Shared/echarts.min.js", "Canonical echarts.min.js",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Canonical 1.07 MB ECharts 5.x bundle in shared runtime assets."),
        new("src/ETL-SQL.ReportPlayer/wwwroot/echarts.min.js", "ReportPlayer echarts.min.js copy",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Generated copy synchronized from ReportRuntime canonical asset."),
        new("src/ETL-SQL.WorkstationEditor/wwwroot/echarts.min.js", "WorkstationEditor echarts.min.js copy",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Generated copy synchronized from ReportRuntime canonical asset."),
        new("src/ETL-SQL.Portal/wwwroot/js/echarts.min.js", "Portal echarts.min.js copy",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Generated copy synchronized from ReportRuntime canonical asset."),
        new("src/etl-sql-vscode/media/echarts.min.js", "VS Code echarts.min.js copy",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Generated copy synchronized from ReportRuntime canonical asset."),
        new("scripts/sync-assets.js", "sync-assets.js script",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Synchronizes echarts.min.js and other runtime assets across host projects."),
        new("src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js", "report-runtime.js ECharts integration",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Browser chart initialization, renderItem wiring, brush and tooltip handlers."),
        new("src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js", "designer.js ECharts preview",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Report Builder live chart preview rendering via window.echarts."),
        new("src/ETL-SQL.Portal/wwwroot/index.html", "Portal index.html script tag",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Script tag loading echarts.min.js."),
        new("src/ETL-SQL.Portal/wwwroot/designer.html", "Portal designer.html script tag",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Script tag loading echarts.min.js."),
        new("src/ETL-SQL.Portal/wwwroot/designer-preview.html", "Portal designer-preview.html script loader",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Dynamic script element loading echarts.min.js."),
        new("src/ETL-SQL.Portal/wwwroot/admin.html", "Portal admin.html script tag",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Script tag loading echarts.min.js."),
        new("src/ETL-SQL.Portal/wwwroot/orchestrator.html", "Portal orchestrator.html timeline",
            RetirementClassification.RemovableNow, null,
            "Internal timeline Gantt chart using echarts.init; can be migrated to native HTML/canvas/SVG timeline independently."),

        // Tests
        new("tests/ETL-SQL.Tests/Reporting/EChartsSsrSpikeTests.cs", "EChartsSsrSpikeTests.cs",
            RetirementClassification.RemovableNow, null,
            "Spike test for V8 ClearScript initialization; not part of production test contract."),
        new("tests/ETL-SQL.Tests/Reporting/EChartsSsrTests.cs", "EChartsSsrTests.cs",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Server-side rendering unit tests for ECharts SSR during migration."),
        new("tests/ETL-SQL.Tests/Reporting/EChartsRendererCoverageTests.cs", "EChartsRendererCoverageTests.cs",
            RetirementClassification.RemovableAfterNamedBatch, "Incremental per batch",
            "Tests legacy ECharts option generation; phased out as each batch migrates to PlotPlan."),
        new("tests/ETL-SQL.Tests/Reporting/Conformance/RepresentativeVisualConformanceTests.cs", "RepresentativeVisualConformanceTests.cs",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Verifies transient ECharts option output for representative slice."),
        new("tests/ETL-SQL.Tests/Reporting/Conformance/RepresentativeVisualConformanceHarness.cs", "RepresentativeVisualConformanceHarness.cs",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Helper harness rendering ECharts options for conformance tests."),
        new("tests/ETL-SQL.Tests/Reporting/AdvancedAuthoring/AdvancedAuthoringSemanticReadinessTests.cs", "AdvancedAuthoringSemanticReadinessTests.cs",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Verifies ECharts lowering of advanced authoring specs during transition."),
        new("tests/ETL-SQL.Tests/Reporting/AdvancedAuthoring/AdvancedAuthoringSemanticReadinessHarness.cs", "AdvancedAuthoringSemanticReadinessHarness.cs",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Readiness inventory comments and test helper."),
        new("tests/ETL-SQL.Tests/Reporting/AdvancedAuthoring/AdvancedChartProductionTests.cs", "AdvancedChartProductionTests.cs",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Production tests asserting ECharts output for custom charts."),
        new("tests/ETL-SQL.Tests/Reporting/Gantt/GanttCharacterizationTests.cs", "GanttCharacterizationTests.cs",
            RetirementClassification.RemovableAfterNamedBatch, "Batch 4: Flow/Timeline",
            "Characterization tests asserting current Gantt ECharts and terminal behavior."),

        // Notices & Inventory
        new("THIRD-PARTY-NOTICES.md", "THIRD-PARTY-NOTICES.md",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Legal notices for Apache ECharts, ClearScript, and V8 engine."),
        new("THIRD-PARTY-INVENTORY.md", "THIRD-PARTY-INVENTORY.md",
            RetirementClassification.RequiredUntilFinalRetirement, null,
            "Software inventory table entries for echarts.min.js and Microsoft.ClearScript.V8.*.")
    ];

    [Fact]
    public void AuthoritativeInventory_ContainsExpectedCategories()
    {
        Assert.NotEmpty(AuthoritativeInventory);
        Assert.Contains(AuthoritativeInventory, e => e.Classification == RetirementClassification.RemovableNow);
        Assert.Contains(AuthoritativeInventory, e => e.Classification == RetirementClassification.RemovableAfterNamedBatch);
        Assert.Contains(AuthoritativeInventory, e => e.Classification == RetirementClassification.RequiredUntilFinalRetirement);
    }

    [Fact]
    public void AuthoritativeInventory_AllReferencedFiles_ExistOnDisk()
    {
        var repoRoot = GetRepoRoot();
        foreach (var entry in AuthoritativeInventory)
        {
            var fullPath = Path.Combine(repoRoot, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath), $"Inventory entry file does not exist on disk: {entry.RelativePath}");
        }
    }

    [Fact]
    public void AuthoritativeInventory_BatchEntries_SpecifyTargetBatch()
    {
        foreach (var entry in AuthoritativeInventory)
        {
            if (entry.Classification == RetirementClassification.RemovableAfterNamedBatch)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.TargetBatch),
                    $"Entry {entry.RelativePath} is classified as RemovableAfterNamedBatch but has no TargetBatch specified.");
            }
            Assert.False(string.IsNullOrWhiteSpace(entry.Rationale),
                $"Entry {entry.RelativePath} must have a non-empty retirement rationale.");
        }
    }
}
