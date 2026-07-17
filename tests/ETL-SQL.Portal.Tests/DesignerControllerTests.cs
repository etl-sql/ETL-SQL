using System.Text.Json;
using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Tests;

public class DesignerControllerTests
{
    [Fact]
    public void Parse_RejectsScriptOverLimit()
    {
        var controller = new DesignerController(portalConfig: new PortalConfig
        {
            DesignerLimits = new PortalDesignerLimitsConfig { MaxScriptCharacters = 10 }
        });

        var result = Assert.IsType<ObjectResult>(
            controller.Parse(new ParseDesignerRequest("SELECT 1234567890 AS Value;")));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }

    [Fact]
    public void Generate_RejectsTooManyItems()
    {
        var controller = new DesignerController(portalConfig: new PortalConfig
        {
            DesignerLimits = new PortalDesignerLimitsConfig { MaxGeneratedItems = 2 }
        });
        var state = new DesignerStateDto(
            [
                new DesignerPageDto(
                    "p1",
                    "Main",
                    "Dashboard",
                    [
                        new DesignerVisualDto("v1", "A", "CARD", 1, 1, 12, 2, null, null, [], []),
                        new DesignerVisualDto("v2", "B", "CARD", 1, 3, 12, 2, null, null, [], [])
                    ])
            ],
            [new DesignerDatasetDto("ds1", "&data", "SELECT 1 AS Value")]);

        var result = Assert.IsType<ObjectResult>(controller.Generate(new GenerateDesignerRequest(state)));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }

    [Fact]
    public void Generate_RejectsWhenDesignerConcurrencyGateIsFull()
    {
        var config = new PortalConfig
        {
            DesignerLimits = new PortalDesignerLimitsConfig { MaxConcurrentRequests = 1 }
        };
        var controller = new DesignerController(portalConfig: config);
        Assert.True(DesignerController.TryAcquireDesignerGateForTest(config, out var lease));
        try
        {
            var state = new DesignerStateDto(
                [new DesignerPageDto("p1", "Main", "Dashboard", [])],
                []);

            var result = Assert.IsType<ObjectResult>(controller.Generate(new GenerateDesignerRequest(state)));

            Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        }
        finally
        {
            lease.Dispose();
        }
    }

    [Fact]
    public void Generate_UsesReportDatasetIdentifiers()
    {
        var controller = new DesignerController();
        var state = new DesignerStateDto(
            [
                new DesignerPageDto(
                    "p1",
                    "Sales",
                    "Dashboard",
                    [
                        new DesignerVisualDto(
                            "v1",
                            "SalesBar",
                            "BAR",
                            1,
                            1,
                            12,
                            4,
                            "Sales",
                            "&sales",
                            new Dictionary<string, string> { ["X"] = "region", ["Y"] = "amount" },
                            new Dictionary<string, string>())
                    ])
            ],
            [
                new DesignerDatasetDto("ds1", "&sales", "SELECT region, amount FROM #sales")
            ]);

        var result = Assert.IsType<OkObjectResult>(controller.Generate(new GenerateDesignerRequest(state)));
        var response = Assert.IsType<GenerateDesignerResponse>(result.Value);

        Assert.Contains("CREATE DATASET &sales AS", response.Script);
        Assert.Contains("SOURCE = &sales", response.Script);
        Assert.DoesNotContain("CREATE DATASET #", response.Script);
        Assert.DoesNotContain("SOURCE = #", response.Script);
    }

    [Fact]
    public void GenerateAndParse_SupportsContainersAndButtons()
    {
        var controller = new DesignerController();
        var state = new DesignerStateDto(
            [
                new DesignerPageDto(
                    "p1",
                    "MainPage",
                    "Dashboard",
                    [
                        new DesignerVisualDto(
                            "v1",
                            "MyContainer",
                            "CONTAINER",
                            1,
                            1,
                            6,
                            4,
                            "Panel Title",
                            null,
                            new Dictionary<string, string>(),
                            new Dictionary<string, string> { ["CONTAINER_TYPE"] = "DRAWER" }),
                        new DesignerVisualDto(
                            "v2",
                            "MyButton",
                            "BUTTON",
                            7,
                            1,
                            6,
                            4,
                            "Click Me",
                            null,
                            new Dictionary<string, string>(),
                            new Dictionary<string, string> { ["BUTTON_TYPE"] = "REFRESH" })
                    ])
            ],
            new List<DesignerDatasetDto>());

        // 1. Generate script
        var genResult = Assert.IsType<OkObjectResult>(controller.Generate(new GenerateDesignerRequest(state)));
        var genResponse = Assert.IsType<GenerateDesignerResponse>(genResult.Value);

        // 2. Parse back
        var parseResult = Assert.IsType<OkObjectResult>(controller.Parse(new ParseDesignerRequest(genResponse.Script)));
        var parseResponse = Assert.IsType<ParseDesignerResponse>(parseResult.Value);

        Assert.Null(parseResponse.Error);
        Assert.NotNull(parseResponse.DesignState);
        Assert.Single(parseResponse.DesignState.Pages);
        var page = parseResponse.DesignState.Pages[0];
        Assert.Equal(2, page.Visuals.Count);

        var container = page.Visuals.FirstOrDefault(v => v.Type == "CONTAINER");
        Assert.NotNull(container);
        Assert.Equal("MyContainer", container.Name);
        Assert.Equal("Panel Title", container.Title);
        Assert.Equal("DRAWER", container.Options["CONTAINER_TYPE"]);

        var button = page.Visuals.FirstOrDefault(v => v.Type == "BUTTON");
        Assert.NotNull(button);
        Assert.Equal("MyButton", button.Name);
        Assert.Equal("Click Me", button.Title);
        Assert.Equal("REFRESH", button.Options["BUTTON_TYPE"]);
    }

    [Fact]
    public async Task Analyze_ReturnsParserDiagnostics()
    {
        var controller = new DesignerController();

        var result = Assert.IsType<OkObjectResult>(
            await controller.Analyze(new AnalyzeDesignerRequest("CREATE CONNECTION c AS;")));
        var response = Assert.IsType<AnalyzeDesignerResponse>(result.Value);

        Assert.Contains(response.Diagnostics, d =>
            d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Analyze_ReturnsLintDiagnostics()
    {
        var controller = new DesignerController();

        var result = Assert.IsType<OkObjectResult>(
            await controller.Analyze(new AnalyzeDesignerRequest("SELECT * FROM #stage;")));
        var response = Assert.IsType<AnalyzeDesignerResponse>(result.Value);

        Assert.Contains(response.Diagnostics, d =>
            string.Equals(d.Code, "AvoidSelectStar", StringComparison.OrdinalIgnoreCase) &&
            d.Source == "ETL-SQL Linter");
    }

    [Fact]
    public async Task Analyze_MatchesCliAnalysisDiagnostics()
    {
        const string script = """
            SELECT * FROM #stage;
            CREATE CONNECTION c AS;
            """;
        var controller = new DesignerController();

        var result = Assert.IsType<OkObjectResult>(
            await controller.Analyze(new AnalyzeDesignerRequest(script, DocumentUri: "golden.rptsql")));
        var response = Assert.IsType<AnalyzeDesignerResponse>(result.Value);

        var endpointJson = JsonSerializer.Serialize(response.Diagnostics, JsonSerializerOptions.Web);
        var cliJson = JsonSerializer.Serialize(await AnalyzeLikeCliAsync(script), JsonSerializerOptions.Web);
        Assert.Equal(cliJson, endpointJson);
    }

    private static async Task<IReadOnlyList<AnalysisDiagnostic>> AnalyzeLikeCliAsync(string script)
    {
        var lines = SplitLines(script);
        var tokens = new Lexer(script).Tokenize();
        var ast = new CoreParser(tokens, script).Parse();
        var diagnostics = new List<AnalysisDiagnostic>();
        diagnostics.AddRange(AnalysisDiagnosticBuilder.FromParserDiagnostics(ast.Diagnostics, lines));

        var linter = LinterFactory.CreateWithAllRules();
        var lintResults = await linter.AnalyzeAsync(ast, new DefaultLintContext { DocumentUri = "golden.rptsql" });
        diagnostics.AddRange(AnalysisDiagnosticBuilder.FromLintResults(lintResults, lines));

        return diagnostics
            .OrderByDescending(d => d.Severity == DiagnosticSeverity.Error)
            .ThenBy(d => d.StartLine)
            .ThenBy(d => d.StartColumn)
            .ToList();
    }

    private static IReadOnlyList<string> SplitLines(string script)
    {
        return script.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }
}
