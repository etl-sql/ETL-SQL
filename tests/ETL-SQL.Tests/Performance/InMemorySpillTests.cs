using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Performance
{
    public class InMemorySpillTests
    {
        [Fact]
        public async Task InMemoryDataSource_SpillsToDisk_WhenThresholdExceeded()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "ETLSQL_Test_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            
            try
            {
                var ds = new InMemoryDataSource();
                ds.OverflowDirectory = tempDir;
                ds.OverflowEntropy = "TestEntropy";
                ds.MaxInMemoryBatches = 2; // Very low for testing
                
                var schema = new List<string> { "Id", "Val" };
                ds.SetSchema(schema.Select(s => new ColumnDefinition(s, "INT", false)));

                // Prepare 5 batches
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
                var files = Directory.GetFiles(tempDir, "*.spill");
                // 5 batches total. Threshold is 2. 
                // Batch 0 added. Count=1.
                // Batch 1 added. Count=2.
                // Batch 2 added: Count=3 > threshold. Spill Batch 0. Count=2.
                // Batch 3 added: Count=3 > threshold. Spill Batch 1. Count=2.
                // Batch 4 added: Count=3 > threshold. Spill Batch 2. Count=2.
                // Expected spilled: 3 files (Batch 0, 1, 2)
                Assert.Equal(3, files.Length);

                // Verify data integrity
                int rowCount = 0;
                await foreach (var batch in ds.ReadBatches())
                {
                    rowCount += batch.Rows.Count;
                }
                Assert.Equal(50, rowCount);

                // Verify cleanup
                await ds.TruncateAsync();
                Assert.Empty(Directory.GetFiles(tempDir, "*.spill"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
