using ETL_SQL.ReportPortal.Controllers;
using ETL_SQL.ReportPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.ReportPortal.Tests;

public class DesignerControllerTests
{
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
}
