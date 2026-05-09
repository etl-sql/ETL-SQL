using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.ReportPlayer;
using ETL_SQL.Core;
using ETL_SQL.Data;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Engine;

namespace ETL_SQL.Tests.Reporting.Reporting
{
    public class ReportingEndToEndTests
    {
        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task TestComplexReport_BuildsCompleteManifest()
        {
            // 1. Setup multi-dataset report script
            string scriptPath = Path.Combine(Path.GetTempPath(), $"complex_report_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
DECLARE @Threshold INT = 50;

-- Dataset A: Sales
SELECT 'Region1' AS Region, 100 AS Amount INTO #Sales;
INSERT INTO #Sales (Region, Amount) VALUES ('Region2', 20);

-- Dataset B: Inventory
SELECT 'Item1' AS Item, 200 AS Stock INTO #Inventory;

CREATE VISUAL HighSales AS BAR (
    TITLE 'High Sales',
    SOURCE = (SELECT * FROM #Sales WHERE Amount > @Threshold),
    MAPPINGS (X = Region, Y = Amount)
);

CREATE VISUAL LowSales AS TABLE (
    TITLE 'Low Sales',
    SOURCE = (SELECT * FROM #Sales WHERE Amount <= @Threshold)
);

CREATE VISUAL InventoryStatus AS CARD (
    TITLE 'Total Stock',
    SOURCE = (SELECT SUM(Stock) AS StockVal FROM #Inventory),
    MAPPINGS (VALUE = StockVal)
);

CREATE PAGE Dashboard AS LAYOUT (
    STRUCTURE = 'A B / C .',
    MAP('A' = HighSales, 'B' = LowSales, 'C' = InventoryStatus)
);
");

            try
            {
                var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();

                // 2. Verify Manifest Structure
                Assert.Equal(3, manifest.Visuals.Count);
                
                var highSales = manifest.Visuals.First(v => v.Name == "HighSales");
                Assert.Single(highSales.Rows); // Region1 only
                Assert.Equal("Region1", highSales.Rows[0][0]);

                var lowSales = manifest.Visuals.First(v => v.Name == "LowSales");
                Assert.Single(lowSales.Rows); // Region2 only
                Assert.Equal("Region2", lowSales.Rows[0][0]);

                var invCard = manifest.Visuals.First(v => v.Name == "InventoryStatus");
                Assert.Equal("200", invCard.Rows[0][0]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task TestDashboardService_SelectiveRefresh_UpdatesCorrectVisuals()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"selective_refresh_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
DECLARE @RegionFilter STRING INPUT = 'All';

SELECT 'North' AS Region, 100 AS Sales INTO #Data;
INSERT INTO #Data (Region, Sales) VALUES ('South', 200);

CREATE VISUAL RegionSales AS TABLE (
    SOURCE = (SELECT * FROM #Data WHERE @RegionFilter = 'All' OR Region = @RegionFilter)
);

CREATE VISUAL StaticTotal AS CARD (
    SOURCE = (SELECT SUM(Sales) AS TotalVal FROM #Data),
    MAPPINGS (VALUE = TotalVal)
);

CREATE PAGE Main AS LAYOUT (
    STRUCTURE = 'A B',
    MAP('A' = RegionSales, 'B' = StaticTotal)
);
");

            try
            {
                var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                
                // Initial build (All regions)
                var manifest1 = await service.GetManifestAsync();
                Assert.Equal(2, manifest1.Visuals.First(v => v.Name == "RegionSales").Rows.Count);

                // Update parameter to 'North'
                // This should refresh RegionSales but technically StaticTotal persists
                var manifest2 = await service.SetParameterAsync("RegionFilter", "North");
                
                var regionSales = manifest2.Visuals.First(v => v.Name == "RegionSales");
                Assert.Single(regionSales.Rows);
                Assert.Equal("North", regionSales.Rows[0][0]);

                var totalSales = manifest2.Visuals.First(v => v.Name == "StaticTotal");
                Assert.Equal("300", totalSales.Rows[0][0]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}
