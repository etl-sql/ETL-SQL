using System;
using System.IO;
using System.Linq;
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
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Reporting.CascadingSlicers;

public class CascadingSlicerBaselineTests
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

    [Theory]
    [InlineData("parent_child_cascade.rptsql")]
    [InlineData("two_parents_one_child_cascade.rptsql")]
    [InlineData("three_level_cascade.rptsql")]
    [InlineData("null_and_all_selections.rptsql")]
    [InlineData("multiselect_parent_cascade.rptsql")]
    [InlineData("invalid_descendant_selection.rptsql")]
    [InlineData("rapid_parent_transitions.rptsql")]
    [InlineData("cyclic_dependency_cascade.rptsql")]
    public async Task AllCascadingFixtures_ParseAndCompileWithoutDiagnostics(string fixtureFileName)
    {
        var repoRoot = GetRepoRoot();
        var fixturePath = Path.Combine(repoRoot, "tests", "fixtures", "reporting", "cascading-slicers", fixtureFileName);

        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        var script = await File.ReadAllTextAsync(fixturePath);
        var tokens = new Lexer(script).Tokenize();
        var ast = new CoreParser(tokens, script).Parse();

        Assert.NotNull(ast);
        Assert.DoesNotContain(ast.Diagnostics, d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);

        var evaluator = CreateTestEvaluator();
        await evaluator.Evaluate(ast);

        var manifestBuilder = new ManifestBuilder(evaluator);
        var manifest = await manifestBuilder.BuildAsync(fixturePath);

        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest.Visuals);
        Assert.NotEmpty(manifest.Pages);
    }

    [Fact]
    public void DependencyGraphs_CalculateTopologicalOrderAndRoots()
    {
        var graphs = CascadingSlicerBaselineModel.BuildAllDependencyGraphs();

        // 1. Parent Child
        var g1 = graphs["parent_child_cascade.rptsql"];
        Assert.False(g1.HasCycles);
        Assert.Equal(["@country"], g1.RootParameters);
        Assert.Equal(["@country", "@state"], g1.TopologicalOrder);
        Assert.Contains("@state", g1.DownstreamMap["@country"]);

        // 2. Two Parents One Child
        var g2 = graphs["two_parents_one_child_cascade.rptsql"];
        Assert.False(g2.HasCycles);
        Assert.Contains("@region", g2.RootParameters);
        Assert.Contains("@year", g2.RootParameters);
        Assert.Equal(3, g2.TopologicalOrder.Count);

        // 3. Three Level Hierarchy
        var g3 = graphs["three_level_cascade.rptsql"];
        Assert.False(g3.HasCycles);
        Assert.Equal(["@division"], g3.RootParameters);
        Assert.Equal(["@division", "@department", "@team"], g3.TopologicalOrder);
        Assert.Contains("@department", g3.DownstreamMap["@division"]);
        Assert.Contains("@team", g3.DownstreamMap["@division"]);
    }

    [Fact]
    public void CyclicDependency_IsDetectedByTestHarness()
    {
        var graphs = CascadingSlicerBaselineModel.BuildAllDependencyGraphs();
        var gCyclic = graphs["cyclic_dependency_cascade.rptsql"];

        Assert.True(gCyclic.HasCycles);
        Assert.NotEmpty(gCyclic.Cycles);
        Assert.Empty(gCyclic.RootParameters);
    }

    [Fact]
    public void StateTransitionScenarios_ValidateInvalidationAndQueryCounts()
    {
        var scenarios = CascadingSlicerBaselineModel.GetRepresentativeScenarios();

        Assert.Equal(8, scenarios.Count);

        var s1 = scenarios.Single(s => s.ScenarioId == "SCENARIO_1_PARENT_CHILD");
        Assert.Equal("@country", s1.TriggerAction.ParameterName);
        Assert.Equal("Canada", s1.TriggerAction.NewValue);
        Assert.Equal(["@state"], s1.ExpectedInvalidatedParameters);
        Assert.Equal(2, s1.ExpectedQueryRefreshCount);
        Assert.Equal("Canada", s1.ExpectedFinalState.ParameterValues["@country"]);
        Assert.Equal("ON", s1.ExpectedFinalState.ParameterValues["@state"]);

        var s6 = scenarios.Single(s => s.ScenarioId == "SCENARIO_6_INVALID_DESCENDANT_RESET");
        Assert.False(s6.IsSupportedToday);
        Assert.Equal(DescendantResetBehavior.RetainIfEligibleElseResetToFirst, s6.ResetPolicy);
    }

    [Fact]
    public void BaselineReport_GeneratesMarkdownAndJson()
    {
        var report = CascadingSlicerBaselineModel.GenerateBaselineReport();
        Assert.NotNull(report);
        Assert.NotEmpty(report.ExistingTestInventory);
        Assert.NotEmpty(report.Scenarios);
        Assert.NotEmpty(report.DependencyGraphs);

        var md = CascadingSlicerBaselineModel.FormatMarkdownReport(report);
        var json = CascadingSlicerBaselineModel.FormatJsonReport(report);

        Assert.Contains("Cascading Slicer & Parameter Dependency Baseline Report", md);
        Assert.Contains("SCENARIO_1_PARENT_CHILD", md);
        Assert.Contains("SCENARIO_8_CYCLIC_DEPENDENCY", md);

        Assert.Contains("\"SCENARIO_1_PARENT_CHILD\"", json);
        Assert.Contains("\"cyclic_dependency_cascade.rptsql\"", json);
    }

    private static Evaluator CreateTestEvaluator()
    {
        var services = new ServiceCollection();
        var logger = NullLogger.Instance;
        var sec = new ETL_SQL.Services.SecurityService(logger) { IsTestMode = true };
        var connRegistry = new ConnectorRegistry();
        connRegistry.Register(new ETL_SQL.Connectors.MockDb.MockDbConnector());
        connRegistry.Register(new ETL_SQL.Connectors.FlatFile.FlatFileConnector());

        services.AddSingleton<ILogger>(logger);
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
}
