using ETL_SQL.ReportPortal.Controllers;
using ETL_SQL.ReportPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

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
}
