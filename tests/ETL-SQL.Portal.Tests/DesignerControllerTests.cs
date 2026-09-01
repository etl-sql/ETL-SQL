using System.Text.Json;
using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Models;
using ETL_SQL.Reporting.Authoring;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Tests;

public class DesignerControllerTests
{
    [Fact]
    public void ApplyQueryFilters_ReturnsParserValidatedVisualSource()
    {
        var controller = new DesignerController();

        var result = Assert.IsType<OkObjectResult>(controller.ApplyQueryFilters(
            new ApplyDesignerQueryFiltersRequest(
                "#sales",
                [new DesignerQueryFilter("region", "Region", "categorical", ["North"])])));
        var response = Assert.IsType<ApplyDesignerQueryFiltersResponse>(result.Value);

        Assert.Contains("Region = 'North'", response.Source, StringComparison.Ordinal);
        Assert.StartsWith("(SELECT * FROM #sales", response.Source, StringComparison.Ordinal);
    }

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
    public async Task GenerateAndParse_RoundTripsSlicerParameterAndAction()
    {
        var controller = new DesignerController();
        var state = new DesignerStateDto(
            [
                new DesignerPageDto("p1", "Main", "Dashboard",
                [
                    new DesignerVisualDto(
                        "v1", "RegionSlicer", "SLICER", 1, 1, 3, 3, null, "&regions",
                        new Dictionary<string, string> { ["VALUE"] = "Region" },
                        new Dictionary<string, string>
                        {
                            ["TITLE"] = "Region",
                            ["action:ON_CHANGE"] = "SET_PARAMETER(@selected_region, value)"
                        })
                ])
            ],
            [new DesignerDatasetDto("ds1", "&regions", "SELECT DISTINCT Region FROM #sales")],
            Parameters:
            [
                new DesignerParameterDto("@selected_region", "VARCHAR", "'All'", IsInput: true)
            ]);

        var generatedResult = Assert.IsType<OkObjectResult>(controller.Generate(new GenerateDesignerRequest(state)));
        var generated = Assert.IsType<GenerateDesignerResponse>(generatedResult.Value);
        Assert.Contains("DECLARE @selected_region VARCHAR = 'All' INPUT;", generated.Script, StringComparison.Ordinal);
        Assert.Contains("ACTIONS (ON_CHANGE = SET_PARAMETER(@selected_region, value))", generated.Script, StringComparison.Ordinal);

        var parsedResult = Assert.IsType<OkObjectResult>(controller.Parse(new ParseDesignerRequest(generated.Script)));
        var parsed = Assert.IsType<ParseDesignerResponse>(parsedResult.Value);
        Assert.Null(parsed.Error);
        var parameter = Assert.Single(parsed.DesignState.Parameters!);
        Assert.Equal("@selected_region", parameter.Name);
        Assert.Equal("'All'", parameter.InitialValue);
        Assert.True(parameter.IsInput);
        var slicer = Assert.Single(parsed.DesignState.Pages[0].Visuals);
        Assert.Equal("Region", slicer.Mappings["VALUE"]);
        Assert.Equal("SET_PARAMETER(@selected_region, value)", slicer.Options["action:ON_CHANGE"]);

        var formatted = SqlFormatter.Format(generated.Script);
        var formattedAst = new CoreParser(new Lexer(formatted).Tokenize(), formatted).Parse();
        Assert.DoesNotContain(formattedAst.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var analyzedResult = Assert.IsType<OkObjectResult>(
            await controller.Analyze(new AnalyzeDesignerRequest(generated.Script, DocumentUri: "slicer.rptsql")));
        var analyzed = Assert.IsType<AnalyzeDesignerResponse>(analyzedResult.Value);
        Assert.DoesNotContain(analyzed.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generate_PreservesInlineVisualSource()
    {
        var controller = new DesignerController();
        var state = new DesignerStateDto(
            [
                new DesignerPageDto(
                    "p1",
                    "Inline",
                    "Dashboard",
                    [
                        new DesignerVisualDto(
                            "v1",
                            "InlineCard",
                            "CARD",
                            1,
                            1,
                            12,
                            2,
                            "Inline",
                            null,
                            new Dictionary<string, string> { ["VALUE"] = "Total" },
                            new Dictionary<string, string>
                            {
                                ["inline_source"] = "(SELECT SUM(Amount) AS Total FROM #sales)"
                            })
                    ])
            ],
            []);

        var result = Assert.IsType<OkObjectResult>(controller.Generate(new GenerateDesignerRequest(state)));
        var response = Assert.IsType<GenerateDesignerResponse>(result.Value);

        Assert.Contains("SOURCE = (SELECT SUM(Amount) AS Total FROM #sales)", response.Script);
        Assert.DoesNotContain("INLINE_SOURCE", response.Script, StringComparison.OrdinalIgnoreCase);
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
    public async Task Analyze_DoesNotReturnSelectStarWarningByDefault()
    {
        var controller = new DesignerController();

        var result = Assert.IsType<OkObjectResult>(
            await controller.Analyze(new AnalyzeDesignerRequest("SELECT * FROM #stage;")));
        var response = Assert.IsType<AnalyzeDesignerResponse>(result.Value);

        Assert.DoesNotContain(response.Diagnostics, d =>
            string.Equals(d.Code, "AvoidSelectStar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScriptDag_ReturnsDesignTimeFlow()
    {
        var controller = new DesignerController();

        var result = Assert.IsType<OkObjectResult>(
            controller.ScriptDag(new ScriptDagRequest(
                "CREATE CONNECTION m AS MOCKDB();\nSELECT UserID INTO #staging FROM m.Users;")));
        var response = Assert.IsType<ETL_SQL.Portal.Services.ScriptDagProjection>(result.Value);

        Assert.True(response.Parsed);
        Assert.Contains(response.Dag.Nodes, n => n.Label == "CONNECT m" && n.Type == "connection");
        Assert.Contains(response.Dag.Nodes, n => n.Label == "SELECT INTO #staging" && n.Type == "io");
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

    [Fact]
    public void Parse_ScalesStructureSlotsToGridColumns()
    {
        const string script = """
            CREATE DATASET &sales AS (SELECT 1 AS x);

            CREATE VISUAL BasicBar AS BAR (SOURCE = &sales, TITLE = 'Basic');
            CREATE VISUAL GroupedBar AS BAR (SOURCE = &sales, TITLE = 'Grouped');
            CREATE VISUAL FullBar AS BAR (SOURCE = &sales, TITLE = 'Full');

            CREATE PAGE BarKitchenSink AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A B / C C',
                    MAP (
                        'A' = BasicBar,
                        'B' = GroupedBar,
                        'C' = FullBar
                    )
                )
            );
            """;
        var controller = new DesignerController();
        var parseResult = Assert.IsType<OkObjectResult>(controller.Parse(new ParseDesignerRequest(script)));
        var parseResponse = Assert.IsType<ParseDesignerResponse>(parseResult.Value);

        Assert.Null(parseResponse.Error);
        Assert.NotNull(parseResponse.DesignState);
        Assert.Single(parseResponse.DesignState.Pages);
        var page = parseResponse.DesignState.Pages[0];
        Assert.Equal(3, page.Visuals.Count);

        var basic = page.Visuals.Single(v => v.Name == "BasicBar");
        Assert.Equal(1, basic.GridCol);
        Assert.Equal(6, basic.GridColSpan);
        Assert.Equal(1, basic.GridRow);
        Assert.Equal(4, basic.GridRowSpan);

        var grouped = page.Visuals.Single(v => v.Name == "GroupedBar");
        Assert.Equal(7, grouped.GridCol);
        Assert.Equal(6, grouped.GridColSpan);
        Assert.Equal(1, grouped.GridRow);
        Assert.Equal(4, grouped.GridRowSpan);

        var full = page.Visuals.Single(v => v.Name == "FullBar");
        Assert.Equal(1, full.GridCol);
        Assert.Equal(12, full.GridColSpan);
        Assert.Equal(5, full.GridRow);
        Assert.Equal(4, full.GridRowSpan);
    }

    [Fact]
    public void Parse_ReportsEveryTopLevelConnectionAsAuthored()
    {
        // The embedded query workbench builds its run preamble from these. It used to cut the
        // statement at the first semicolon with a regex, which is wrong for a body that spans lines
        // or carries a semicolon inside a quoted option, so an author's own connection came back
        // truncated and the run failed with "unknown connection" against a script that declares it.
        var controller = new DesignerController();
        const string script = """
            CREATE CONNECTION sales AS MOCKDB();

            -- A body that a first-semicolon scan cannot read correctly.
            CREATE CONNECTION [warehouse] AS SQLSERVER(
                SERVER = 'db01;failover=db02',
                DATABASE = 'ops'
            );

            SELECT Region INTO #r FROM sales.Orders;
            """;

        var result = Assert.IsType<OkObjectResult>(controller.Parse(new ParseDesignerRequest(script)));
        var parsed = Assert.IsType<ParseDesignerResponse>(result.Value);
        Assert.Null(parsed.Error);

        var connections = parsed.DesignState.Connections!;
        Assert.Equal(2, connections.Count);
        Assert.Equal("sales", connections[0].Name);
        Assert.Equal("CREATE CONNECTION sales AS MOCKDB()", connections[0].Text.TrimEnd(';'));

        Assert.Equal("warehouse", connections[1].Name);
        Assert.Contains("db01;failover=db02", connections[1].Text, StringComparison.Ordinal);
        Assert.Contains("DATABASE = 'ops'", connections[1].Text, StringComparison.Ordinal);

        // Whatever the preamble carries has to parse on its own, or the embedded run fails.
        foreach (var connection in connections)
        {
            var text = connection.Text.TrimEnd(';') + ";";
            var ast = new CoreParser(new Lexer(text).Tokenize(), text).Parse();
            Assert.DoesNotContain(ast.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Single(ast.Statements.OfType<ETL_SQL.Core.CreateConnectionStatement>());
        }
    }

    [Fact]
    public void PipelineTask_AddsThroughTheRouteAndReportsTheTasksBack()
    {
        var controller = new DesignerController();
        const string script = """
            CREATE CONNECTION staging_db AS MOCKDB();

            load_orders:
            EXECUTE staging_db BEGIN
                SELECT 1;
            END;
            """;

        var result = Assert.IsType<OkObjectResult>(controller.PipelineTask(
            new PipelineTaskRequest(script, "add", Id: "archive_orders", Connection: "staging_db",
                Body: "SELECT 2;", After: "load_orders")));
        var response = Assert.IsType<PipelineTaskResponse>(result.Value);

        Assert.True(response.Applied, response.Error);
        Assert.Null(response.Error);
        Assert.Equal(["load_orders", "archive_orders"], response.Tasks.Select(task => task.Id));

        // The script the route hands back has to be script the engine would accept.
        var ast = new CoreParser(new Lexer(response.Script).Tokenize(), response.Script).Parse();
        Assert.DoesNotContain(ast.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("CREATE CONNECTION staging_db AS MOCKDB();", response.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void PipelineTask_ReturnsARefusalRatherThanAnUnchangedScriptThatLooksLikeSuccess()
    {
        // The canvas repaints from whatever comes back. If a refused edit returned applied:true with
        // the original bytes, the author would see the map redraw and conclude the edit landed.
        var controller = new DesignerController();
        const string script = """
            load_orders:
            EXECUTE staging_db BEGIN
                SELECT 1;
            END;
            """;

        var duplicate = Assert.IsType<PipelineTaskResponse>(
            Assert.IsType<OkObjectResult>(controller.PipelineTask(
                new PipelineTaskRequest(script, "add", Id: "load_orders", Connection: "staging_db", Body: "SELECT 1;"))).Value);
        Assert.False(duplicate.Applied);
        Assert.Contains("already has a task", duplicate.Error!, StringComparison.Ordinal);
        Assert.Equal(script, duplicate.Script);

        var unknownOp = Assert.IsType<PipelineTaskResponse>(
            Assert.IsType<OkObjectResult>(controller.PipelineTask(
                new PipelineTaskRequest(script, "reticulate", Id: "load_orders"))).Value);
        Assert.False(unknownOp.Applied);
        Assert.Equal(script, unknownOp.Script);
    }

    private static IReadOnlyList<string> SplitLines(string script)
    {
        return script.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }
}
