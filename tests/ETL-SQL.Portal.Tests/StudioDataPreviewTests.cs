using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class StudioDataPreviewTests
{
    [Fact]
    public void SourcePreview_QuotesOnlyTheServerValidatedIdentity()
    {
        var sql = PortalDesignerDataPreviewService.BuildSourcePreviewScript(
            "sales]alias",
            "dbo.Order]Lines");

        Assert.Equal("SELECT * FROM [sales]]alias].[dbo].[Order]]Lines];", sql);
    }

    [Fact]
    public void TempPreview_ReplaysOnlyReadOnlyPrefixThroughMaterializer()
    {
        const string script = """
            SELECT Id, Amount INTO #stage FROM sales.Orders;
            SELECT SUM(Amount) AS Total INTO #later FROM #stage;
            DELETE FROM sales.Orders WHERE Id = 1;
            """;

        var preview = PortalDesignerDataPreviewService.BuildTempPreviewScript(script, "#stage");

        Assert.Contains("INTO #stage", preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT * FROM [#stage]", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#later", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", preview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TempPreview_RejectsMutationBeforeMaterializer()
    {
        const string script = """
            DELETE FROM sales.Orders WHERE Id = 1;
            SELECT Id INTO #stage FROM sales.Orders;
            """;

        var error = Assert.Throws<ArgumentException>(() =>
            PortalDesignerDataPreviewService.BuildTempPreviewScript(script, "#stage"));

        Assert.Contains("statement 1", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not allowed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TempPreview_RejectsUnmaterializedTarget()
    {
        var error = Assert.Throws<KeyNotFoundException>(() =>
            PortalDesignerDataPreviewService.BuildTempPreviewScript(
                "SELECT Id INTO #other FROM sales.Orders;",
                "#stage"));

        Assert.Contains("not materialized", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatasetPreview_ExtractsTheCurrentReadOnlyQueryAndConnection()
    {
        const string script = """
            CREATE CONNECTION sales AS MSSQL('SHARED:sales');
            CREATE DATASET &orders AS (
                SELECT Region, SUM(Total) AS Revenue
                FROM sales.Orders
                GROUP BY Region
            );
            """;

        var (query, connection) = PortalDesignerDataPreviewService.BuildDatasetPreviewScript(script, "&orders");

        Assert.Equal("sales", connection);
        Assert.Contains("SUM(Total)", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE CONNECTION", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatasetPreview_RejectsCrossConnectionQueries()
    {
        const string script = """
            CREATE DATASET &combined AS (
                SELECT a.Id FROM sales.Orders a JOIN crm.Customers c ON a.Id = c.Id
            );
            """;

        var error = Assert.Throws<ArgumentException>(() =>
            PortalDesignerDataPreviewService.BuildDatasetPreviewScript(script, "combined"));

        Assert.Contains("one governed catalog connection", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Response_RedactsSecretsAndEnforcesRowCap()
    {
        var table = new DataTable();
        table.SetColumns(["Id", "Password", "Reference"]);
        for (var id = 1; id <= 3; id++)
        {
            await table.AddRowAsync(new Row
            {
                ["Id"] = id,
                ["Password"] = "plain-secret",
                ["Reference"] = "ENC:YWJjZA=="
            });
        }

        var response = PortalDesignerRunService.ToResponse(table, 12, null, rowCap: 2, resultByteCap: 16_384);

        Assert.Equal(2, response.Rows.Count);
        Assert.True(response.Capped);
        Assert.All(response.Rows, row =>
        {
            Assert.Equal("********", row["Password"]);
            Assert.DoesNotContain("YWJjZA", Assert.IsType<string>(row["Reference"]));
        });
    }

    [Fact]
    public async Task Response_StopsBeforeSerializedByteLimit()
    {
        var table = new DataTable();
        table.SetColumns(["Payload"]);
        await table.AddRowAsync(new Row { ["Payload"] = new string('a', 800) });
        await table.AddRowAsync(new Row { ["Payload"] = new string('b', 800) });

        var response = PortalDesignerRunService.ToResponse(table, 8, null, rowCap: 100, resultByteCap: 1_024);

        Assert.Single(response.Rows);
        Assert.True(response.Capped);
        Assert.True(response.ByteCapped);
        Assert.InRange(response.BytesReturned, 1, 1_024);
        Assert.Contains("byte preview limit", response.Message, StringComparison.OrdinalIgnoreCase);
    }
}
