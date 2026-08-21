using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Tests.Reporting.CascadingSlicers;

public record TestInventoryEntry(
    string Category,
    string TestFile,
    string TestName,
    string Description);

public record CascadingSlicerBaselineReport(
    DateTime GeneratedAtUtc,
    string GitBranch,
    IReadOnlyList<TestInventoryEntry> ExistingTestInventory,
    IReadOnlyList<StateTransitionScenario> Scenarios,
    IReadOnlyDictionary<string, DependencyGraphAnalysis> DependencyGraphs,
    IReadOnlyDictionary<string, string> RunnableVsPendingStatus);

public static class CascadingSlicerBaselineModel
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IReadOnlyList<TestInventoryEntry> GetExistingTestInventory()
    {
        return new List<TestInventoryEntry>
        {
            new("Parameter Declaration & AST", "tests/ETL-SQL.Tests/Core/Parser/ReportSqlParserTests.cs", "Parse_VisualWithActions_SetsParameter", "Parses SET_PARAMETER actions on BAR and SLICER visuals"),
            new("Action Manifest Binding", "tests/ETL-SQL.Tests/Reporting/ReportingEndToEndTests.cs", "VisualActions_SetParameter_BindsToManifest", "Verifies action manifest emission for @Region, @Search, @Limit, @Active"),
            new("Designer Roundtrip AST", "tests/ETL-SQL.Tests/Portal/Fixtures/ReportDesignerRoundTripFixtures.cs", "Roundtrip_PreservesSetParameterAction", "Validates that Visual Report Builder script synchronization preserves SET_PARAMETER bindings"),
            new("MultiSelect Visual Syntax", "tests/ETL-SQL.Tests/Core/Parser/ReportSqlParserTests.cs", "Parse_MultiSelectVisual_ExtractsOptionsSource", "Parses MULTISELECT visual type with mandatory SOURCE clause"),
            new("Offline Snapshot Packaging", "src/ETL-SQL.Reporting/SnapshotPackageService.cs", "ReadManifestFromPackageAsync", "Serializes and deserializes ReportManifest and table data into standalone .etlsnap packages"),
            new("Interactive Execution Engine", "src/ETL-SQL.ReportHosting/InteractiveSessionManager.cs", "ExecuteInteractionQueryAsync", "Executes parameterized visual SQL queries during live interactive portal sessions"),
            new("Terminal Slicer Rendering", "tests/ETL-SQL.Tests/Reporting/TerminalRendererTests.cs", "RenderSlicer_EmitsInteractiveControl", "Renders interactive slicers in terminal ANSI / Spectre output")
        };
    }

    public static IReadOnlyList<StateTransitionScenario> GetRepresentativeScenarios()
    {
        return new List<StateTransitionScenario>
        {
            new(
                ScenarioId: "SCENARIO_1_PARENT_CHILD",
                Title: "One Parent and One Child Cascade (@country -> @state)",
                FixtureFile: "parent_child_cascade.rptsql",
                InitialState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@country"] = "USA", ["@state"] = "CA" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@country"] = new[] { "USA", "Canada" },
                        ["@state"] = new[] { "CA", "NY", "TX" }
                    }),
                TriggerAction: new SetParameterAction("@country", "Canada"),
                ExpectedInvalidatedParameters: new[] { "@state" },
                ExpectedFinalState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@country"] = "Canada", ["@state"] = "ON" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@country"] = new[] { "USA", "Canada" },
                        ["@state"] = new[] { "ON", "BC" }
                    }),
                ExpectedQueryRefreshCount: 2, // 1 for StateSlicer options, 1 for RegionalSalesChart
                ResetPolicy: DescendantResetBehavior.RetainIfEligibleElseResetToFirst,
                IsSupportedToday: true,
                StatusExplanation: "Runnable today via sequential query execution; automated reactive client propagation pending Phase 6"
            ),

            new(
                ScenarioId: "SCENARIO_2_TWO_PARENTS_ONE_CHILD",
                Title: "Two Parents Feeding One Child (@region + @year -> @category)",
                FixtureFile: "two_parents_one_child_cascade.rptsql",
                InitialState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@region"] = "North America", ["@year"] = "2026", ["@category"] = "Hardware" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@region"] = new[] { "North America", "EMEA" },
                        ["@year"] = new[] { "2025", "2026" },
                        ["@category"] = new[] { "Hardware", "Cloud Services" }
                    }),
                TriggerAction: new SetParameterAction("@region", "EMEA"),
                ExpectedInvalidatedParameters: new[] { "@category" },
                ExpectedFinalState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@region"] = "EMEA", ["@year"] = "2026", ["@category"] = "Hardware" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@region"] = new[] { "North America", "EMEA" },
                        ["@year"] = new[] { "2025", "2026" },
                        ["@category"] = new[] { "Hardware", "Security" }
                    }),
                ExpectedQueryRefreshCount: 2, // 1 for CategorySlicer, 1 for SkuCountCard
                ResetPolicy: DescendantResetBehavior.RetainIfEligibleElseResetToFirst,
                IsSupportedToday: true,
                StatusExplanation: "Runnable today; @category remains 'Hardware' as it is eligible in EMEA 2026"
            ),

            new(
                ScenarioId: "SCENARIO_3_THREE_LEVEL_CASCADE",
                Title: "Three-Level Hierarchy Cascade (@division -> @department -> @team)",
                FixtureFile: "three_level_cascade.rptsql",
                InitialState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@division"] = "Engineering", ["@department"] = "Core Platform", ["@team"] = "Storage Engine" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@division"] = new[] { "Engineering", "Sales" },
                        ["@department"] = new[] { "Core Platform", "Security" },
                        ["@team"] = new[] { "Storage Engine", "Query Optimizer" }
                    }),
                TriggerAction: new SetParameterAction("@division", "Sales"),
                ExpectedInvalidatedParameters: new[] { "@department", "@team" },
                ExpectedFinalState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@division"] = "Sales", ["@department"] = "Enterprise", ["@team"] = "Strategic Accounts" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@division"] = new[] { "Engineering", "Sales" },
                        ["@department"] = new[] { "Enterprise", "SMB" },
                        ["@team"] = new[] { "Strategic Accounts" }
                    }),
                ExpectedQueryRefreshCount: 3, // DepartmentSlicer, TeamSlicer, HeadcountCard
                ResetPolicy: DescendantResetBehavior.AlwaysResetToFirst,
                IsSupportedToday: true,
                StatusExplanation: "Runnable today via pipeline cascade; atomic multi-level client batching pending Phase 6"
            ),

            new(
                ScenarioId: "SCENARIO_4_NULL_AND_ALL",
                Title: "Null and All Option Selections (__ALL__ / NULL Wildcards)",
                FixtureFile: "null_and_all_selections.rptsql",
                InitialState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@selected_channel"] = "__ALL__", ["@selected_campaign"] = "__ALL__" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@selected_channel"] = new[] { "__ALL__", "Digital", "Direct", "Partner" },
                        ["@selected_campaign"] = new[] { "__ALL__", "Search_SEM", "Social_Paid", "Direct_Mail", "Affiliate_Network" }
                    }),
                TriggerAction: new SetParameterAction("@selected_channel", "Digital"),
                ExpectedInvalidatedParameters: new[] { "@selected_campaign" },
                ExpectedFinalState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@selected_channel"] = "Digital", ["@selected_campaign"] = "__ALL__" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@selected_channel"] = new[] { "__ALL__", "Digital", "Direct", "Partner" },
                        ["@selected_campaign"] = new[] { "__ALL__", "Search_SEM", "Social_Paid" }
                    }),
                ExpectedQueryRefreshCount: 2, // CampaignSlicer options, ConversionsTable
                ResetPolicy: DescendantResetBehavior.RetainIfEligibleElseResetToFirst,
                IsSupportedToday: true,
                StatusExplanation: "Runnable today; wildcard pattern matching supported via SQL COALESCE / OR clauses"
            ),

            new(
                ScenarioId: "SCENARIO_5_MULTISELECT_PARENT",
                Title: "Multi-Select Parent Values (@regions -> @territory)",
                FixtureFile: "multiselect_parent_cascade.rptsql",
                InitialState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@regions"] = "North,East", ["@territory"] = "Boston" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@regions"] = new[] { "North", "East", "West" },
                        ["@territory"] = new[] { "Boston", "Manchester", "New York City", "Philadelphia" }
                    }),
                TriggerAction: new SetParameterAction("@regions", "West"),
                ExpectedInvalidatedParameters: new[] { "@territory" },
                ExpectedFinalState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@regions"] = "West", ["@territory"] = "Seattle" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@regions"] = new[] { "North", "East", "West" },
                        ["@territory"] = new[] { "Seattle", "San Francisco" }
                    }),
                ExpectedQueryRefreshCount: 2, // TerritorySlicer options, AccountCountCard
                ResetPolicy: DescendantResetBehavior.AlwaysResetToFirst,
                IsSupportedToday: true,
                StatusExplanation: "Runnable today via CSV string matching; native array-typed parameter binding pending Phase 6"
            ),

            new(
                ScenarioId: "SCENARIO_6_INVALID_DESCENDANT_RESET",
                Title: "Invalid Descendant Selection & Auto-Reset",
                FixtureFile: "invalid_descendant_selection.rptsql",
                InitialState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@country"] = "USA", ["@state"] = "TX" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@country"] = new[] { "USA", "Germany" },
                        ["@state"] = new[] { "TX", "CA" }
                    }),
                TriggerAction: new SetParameterAction("@country", "Germany"),
                ExpectedInvalidatedParameters: new[] { "@state" },
                ExpectedFinalState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@country"] = "Germany", ["@state"] = "BY" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@country"] = new[] { "USA", "Germany" },
                        ["@state"] = new[] { "BY", "BE" }
                    }),
                ExpectedQueryRefreshCount: 2,
                ResetPolicy: DescendantResetBehavior.RetainIfEligibleElseResetToFirst,
                IsSupportedToday: false,
                StatusExplanation: "Pending Phase 6; current client keeps stale '@state=TX' value until manual interaction"
            ),

            new(
                ScenarioId: "SCENARIO_7_RAPID_TRANSITIONS",
                Title: "Rapid Consecutive Parent Changes (Debounce & Convergence)",
                FixtureFile: "rapid_parent_transitions.rptsql",
                InitialState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@dept"] = "D1", ["@role"] = "R1_Lead" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@dept"] = new[] { "D1", "D2", "D3" },
                        ["@role"] = new[] { "R1_Lead", "R1_Senior" }
                    }),
                TriggerAction: new SetParameterAction("@dept", "D3"), // Fast transition through D2 -> D3
                ExpectedInvalidatedParameters: new[] { "@role" },
                ExpectedFinalState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@dept"] = "D3", ["@role"] = "R3_Architect" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@dept"] = new[] { "D1", "D2", "D3" },
                        ["@role"] = new[] { "R3_Architect", "R3_Consultant" }
                    }),
                ExpectedQueryRefreshCount: 2, // Coalesced: only final D3 query fires
                ResetPolicy: DescendantResetBehavior.AlwaysResetToFirst,
                IsSupportedToday: false,
                StatusExplanation: "Pending Phase 6; debounce and cancellation tokens will coalesce rapid burst queries"
            ),

            new(
                ScenarioId: "SCENARIO_8_CYCLIC_DEPENDENCY",
                Title: "Cyclic Dependency Cascade (Diagnostic Target)",
                FixtureFile: "cyclic_dependency_cascade.rptsql",
                InitialState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@paramA"] = "A1", ["@paramB"] = "B1" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@paramA"] = new[] { "A1", "A2" },
                        ["@paramB"] = new[] { "B1", "B2" }
                    }),
                TriggerAction: new SetParameterAction("@paramA", "A2"),
                ExpectedInvalidatedParameters: new[] { "@paramB", "@paramA" },
                ExpectedFinalState: new CascadingStateSnapshot(
                    ParameterValues: new Dictionary<string, string?> { ["@paramA"] = "A2", ["@paramB"] = "B1" },
                    EligibleOptionSets: new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["@paramA"] = new[] { "A1", "A2" },
                        ["@paramB"] = new[] { "B1", "B2" }
                    }),
                ExpectedQueryRefreshCount: 0, // Should be rejected at lint/compile time
                ResetPolicy: DescendantResetBehavior.RetainValueEvenIfInvalid,
                IsSupportedToday: false,
                StatusExplanation: "Pending Phase 6; future compiler linter will emit diagnostic error on cyclic parameter graph"
            )
        };
    }

    public static Dictionary<string, DependencyGraphAnalysis> BuildAllDependencyGraphs()
    {
        var result = new Dictionary<string, DependencyGraphAnalysis>();

        // Graph 1: Parent Child
        var g1 = new CascadingSlicerDependencyGraph();
        g1.AddNode(new SlicerDependencyNode("CountrySlicer", "@country", Array.Empty<string>()));
        g1.AddNode(new SlicerDependencyNode("StateSlicer", "@state", new[] { "@country" }));
        result["parent_child_cascade.rptsql"] = g1.Analyze();

        // Graph 2: Two Parents One Child
        var g2 = new CascadingSlicerDependencyGraph();
        g2.AddNode(new SlicerDependencyNode("RegionSlicer", "@region", Array.Empty<string>()));
        g2.AddNode(new SlicerDependencyNode("YearSlicer", "@year", Array.Empty<string>()));
        g2.AddNode(new SlicerDependencyNode("CategorySlicer", "@category", new[] { "@region", "@year" }));
        result["two_parents_one_child_cascade.rptsql"] = g2.Analyze();

        // Graph 3: Three Level
        var g3 = new CascadingSlicerDependencyGraph();
        g3.AddNode(new SlicerDependencyNode("DivisionSlicer", "@division", Array.Empty<string>()));
        g3.AddNode(new SlicerDependencyNode("DepartmentSlicer", "@department", new[] { "@division" }));
        g3.AddNode(new SlicerDependencyNode("TeamSlicer", "@team", new[] { "@division", "@department" }));
        result["three_level_cascade.rptsql"] = g3.Analyze();

        // Graph 8: Cyclic
        var g8 = new CascadingSlicerDependencyGraph();
        g8.AddNode(new SlicerDependencyNode("SlicerNodeA", "@paramA", new[] { "@paramB" }));
        g8.AddNode(new SlicerDependencyNode("SlicerNodeB", "@paramB", new[] { "@paramA" }));
        result["cyclic_dependency_cascade.rptsql"] = g8.Analyze();

        return result;
    }

    public static CascadingSlicerBaselineReport GenerateBaselineReport()
    {
        var inventory = GetExistingTestInventory();
        var scenarios = GetRepresentativeScenarios();
        var graphs = BuildAllDependencyGraphs();

        var statusMap = new Dictionary<string, string>
        {
            ["AST Parsing & Diagnostics"] = "Runnable Today: All standard Report-SQL slicers parse without error diagnostics.",
            ["Action Manifest Metadata"] = "Runnable Today: ACTIONS (ON_CHANGE = SET_PARAMETER(@var, val)) correctly recorded on VisualManifest.",
            ["Manual Parameter Ingestion"] = "Runnable Today: Evaluator correctly accepts and injects external @parameters during live re-execution.",
            ["Reactive Client Graph Propagation"] = "Pending Phase 6 Design: Client runtime does not automatically cascade parameter changes down the DAG.",
            ["Automatic Descendant State Invalidation"] = "Pending Phase 6 Design: Invalidation of stale child selections upon parent mutation is not yet automated.",
            ["Compile-Time Cycle Detection"] = "Pending Phase 6 Design: Compiler currently allows cyclic parameter source queries without diagnostics."
        };

        return new CascadingSlicerBaselineReport(
            GeneratedAtUtc: DateTime.UtcNow,
            GitBranch: "test/reporting-phase6-cascading-slicer-baselines",
            ExistingTestInventory: inventory,
            Scenarios: scenarios,
            DependencyGraphs: graphs,
            RunnableVsPendingStatus: statusMap);
    }

    public static string FormatMarkdownReport(CascadingSlicerBaselineReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase 6 Cascading Slicer & Parameter Dependency Baseline Report");
        sb.AppendLine();
        sb.AppendLine($"> **Timestamp (UTC):** {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} | **Branch:** `{report.GitBranch}`");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 1. Existing Test & Feature Inventory");
        sb.AppendLine();
        sb.AppendLine("| Category | Test / Source Location | Target | Description |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");

        foreach (var item in report.ExistingTestInventory)
        {
            sb.AppendLine($"| **{item.Category}** | `{item.TestFile}` | `{item.TestName}` | {item.Description} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 2. Capabilities: Runnable Today vs Pending Phase 6 Accepted Design");
        sb.AppendLine();
        sb.AppendLine("| Capability Area | Status | Operational Details |");
        sb.AppendLine("| :--- | :---: | :--- |");

        foreach (var (cap, desc) in report.RunnableVsPendingStatus)
        {
            var statusBadge = desc.StartsWith("Runnable Today") ? "✅ **Runnable Today**" : "⏳ **Pending Design**";
            sb.AppendLine($"| **{cap}** | {statusBadge} | {desc} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 3. Representative Scenarios & State Transition Baselines");
        sb.AppendLine();

        foreach (var s in report.Scenarios)
        {
            sb.AppendLine($"### `{s.ScenarioId}` — {s.Title}");
            sb.AppendLine();
            sb.AppendLine($"- **Fixture File:** `tests/fixtures/reporting/cascading-slicers/{s.FixtureFile}`");
            sb.AppendLine($"- **Status:** {(s.IsSupportedToday ? "✅ Supported Today" : "⏳ Pending Phase 6")}");
            sb.AppendLine($"- **Trigger Action:** `SET_PARAMETER({s.TriggerAction.ParameterName}, '{s.TriggerAction.NewValue}')`");
            sb.AppendLine($"- **Invalidated Descendants:** `{string.Join(", ", s.ExpectedInvalidatedParameters)}`");
            sb.AppendLine($"- **Expected Query Refreshes:** `{s.ExpectedQueryRefreshCount}`");
            sb.AppendLine($"- **Reset Policy:** `{s.ResetPolicy}`");
            sb.AppendLine($"- **Notes:** {s.StatusExplanation}");
            sb.AppendLine();
            sb.AppendLine("**Initial State:**");
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(s.InitialState, JsonOpts));
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("**Expected Final State:**");
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(s.ExpectedFinalState, JsonOpts));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 4. Dependency Graph Topological Ordering & Cycles");
        sb.AppendLine();

        foreach (var (fixture, graph) in report.DependencyGraphs)
        {
            sb.AppendLine($"### Fixture: `{fixture}`");
            sb.AppendLine($"- **Root Parameters:** `{string.Join(", ", graph.RootParameters)}`");
            sb.AppendLine($"- **Topological Execution Order:** `{string.Join(" -> ", graph.TopologicalOrder)}`");
            sb.AppendLine($"- **Has Cycles:** `{(graph.HasCycles ? "YES (Cycle Detected)" : "NO")}`");
            if (graph.HasCycles)
            {
                foreach (var cycle in graph.Cycles)
                {
                    sb.AppendLine($"  - Detected Cycle: `{string.Join(" -> ", cycle)}`");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string FormatJsonReport(CascadingSlicerBaselineReport report)
    {
        return JsonSerializer.Serialize(report, JsonOpts);
    }
}
