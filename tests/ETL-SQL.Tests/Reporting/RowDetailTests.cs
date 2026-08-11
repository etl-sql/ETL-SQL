using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using ETL_SQL.ReportHosting;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    public class RowDetailTests
    {
        [Fact]
        public async Task DumpSampleManifest()
        {
            var scriptPath = @"C:\Users\chuck\scratch\ETL-SQL\samples\08_Reporting\master_detail_rows.rptsql";
            await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var json = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(@"C:\Users\chuck\scratch\ETL-SQL\manifest_dump.json", json);
        }
        [Fact]
        public async Task RowDetail_BuildsIndexAndRetainsMetadata()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"row_detail_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SELECT 1 AS ParentId, 'Row1' AS Label INTO #Parents;
INSERT INTO #Parents VALUES (2, 'Row2');

CREATE VISUAL parentTable AS TABLE (
    SOURCE = (SELECT * FROM #Parents),
    ROW_DETAIL (
        TARGET = 'childTable',
        MAP (@pid = ParentId),
        LIMIT = 50
    ),
    MAPPINGS (
        COLUMN Label AS 'DisplayLabel'
    )
);

SELECT 1 AS ChildId, 1 AS PId INTO #Children;
INSERT INTO #Children VALUES (2, 1);
INSERT INTO #Children VALUES (3, 2);

CREATE VISUAL childTable AS TABLE (
    SOURCE = (SELECT * FROM #Children),
    OPTIONS (VISIBLE = OFF)
);

CREATE PAGE dashboard AS DASHBOARD (
    STRUCTURE = 'A',
    MAP('A' = parentTable)
);
");

            try
            {
                await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();
                var json = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(@"C:\Users\chuck\scratch\ETL-SQL\manifest_dump.json", json);

                if (manifest.Visuals == null || manifest.Visuals.Count == 0)
                {
                    var msgs = manifest.Messages != null ? string.Join("\n", manifest.Messages.Select(m => m.Message)) : "No messages";
                    Assert.Fail($"No visuals found in manifest. Error: {manifest.Error}\nMessages: {msgs}");
                }
                var parent = manifest.Visuals.First(v => v.Name == "parentTable");
                Assert.NotNull(parent.RowDetail);
                Assert.Equal("childTable", parent.RowDetail.TargetName);
                Assert.Equal(50, parent.RowDetail.Limit);
                Assert.Single(parent.RowDetail.Bindings);
                Assert.Equal("ParentId", parent.RowDetail.Bindings[0].ParentColumn);
                Assert.Equal("pid", parent.RowDetail.Bindings[0].ChildParameter);

                Assert.NotNull(parent.RowDetailKeys);
                Assert.Equal(2, parent.RowDetailKeys.Count);
                Assert.Equal("1", parent.RowDetailKeys[0]["pid"]);
                Assert.Equal("2", parent.RowDetailKeys[1]["pid"]);

                // Mappings applied, parent shouldn't contain ParentId column
                Assert.DoesNotContain("ParentId", parent.Columns, StringComparer.OrdinalIgnoreCase);
                Assert.Contains("DisplayLabel", parent.Columns, StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}
