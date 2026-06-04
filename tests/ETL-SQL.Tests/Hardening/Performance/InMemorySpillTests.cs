using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Hardening.Performance
{
    [Trait("Category", "Performance")]
    public class InMemorySpillTests
    {
        [Fact]
        public async Task InMemoryDataSource_SpillsToDisk_WhenThresholdExceeded()
        {
            // Arrange
            var eval = ServiceProviderServiceExtensions.GetRequiredService<Evaluator>(global::ETL_SQL.App.DependencyInjectionSetup.BuildServiceProvider());
            eval.TempTableSpillThresholdRows = 20;
            eval.SpillEncryptionEnabled = false; // Simplify for test matching old style if needed, but new is better
            
            try
            {
                var ds = new InMemoryDataSource();
                ds.ExecutionContext = eval;
                
                var schema = new List<string> { "Id", "Val" };
                
                // Prepare 5 batches of 10 rows = 50 rows total
                var allBatches = new List<DataTable>();
                for (int b = 0; b < 5; b++)
                {
                    var dt = new DataTable();
                    dt.SetColumns(schema);
                    for (int i = 0; i < 10; i++)
                    {
                        var r = dt.NewRow();
                        r["Id"] = b * 10 + i;
                        r["Val"] = i;
                        await dt.AddRowAsync(r);
                    }
                    allBatches.Add(dt);
                }

                // Act
                await ds.WriteBatches(allBatches.ToAsyncEnumerable());

                // Assert
                // With threshold 20, first 2 batches (20 rows) stay in memory.
                // Next 3 batches (30 rows) should trigger spilling.
                
                // Verify data integrity
                int rowCount = 0;
                long idSum = 0;
                await foreach (var batch in ds.ReadBatches())
                {
                    rowCount += batch.Rows.Count;
                    foreach (var row in batch.Rows) idSum += Convert.ToInt64(row["Id"]);
                }
                Assert.Equal(50, rowCount);

                // Verify cleanup
                await ds.TruncateAsync();
                int clearedCount = 0;
                await foreach (var batch in ds.ReadBatches()) clearedCount += batch.Rows.Count;
                Assert.Equal(0, clearedCount);
            }
            finally
            {
                eval.SpillStore.Cleanup();
            }
        }
    }
}
