using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Integration
{
    [Trait("Category", "Integration")]
    [Trait("Connector", "PARQUET")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class ParquetTests
    {
        private static async IAsyncEnumerable<DataTable> ArrayToAsyncEnumerable(DataTable[] data)
        {
            foreach (var d in data)
            {
                yield return d;
            }
            await Task.CompletedTask;
        }

        [Fact]
        public async Task TestWriteReadParquet()
        {
            string path = "test_data.parquet";
            if (File.Exists(path)) File.Delete(path);

            var ds = new ParquetDataSource(SystemExecutionContext.Instance, path);
            var batch = new DataTable();
            batch.ColumnNames.AddRange(new[] { "ID", "Name", "Score" });

            var r1 = new Row(); r1["ID"] = 1L; r1["Name"] = "Alice"; r1["Score"] = 95.5;
            var r2 = new Row(); r2["ID"] = 2L; r2["Name"] = "Bob"; r2["Score"] = 88.0;
            await batch.AddRowAsync(r1);
            await batch.AddRowAsync(r2);

            await ds.WriteBatches(ArrayToAsyncEnumerable(new[] { batch }));

            Assert.True(File.Exists(path), "Parquet file should be created");

            var dsRead = new ParquetDataSource(SystemExecutionContext.Instance, path);
            var batches = await dsRead.ReadBatches().ToListAsync();

            Assert.Single(batches);
            Assert.Equal(2, batches[0].Rows.Count);
            Assert.Equal("Alice", batches[0].Rows[0]["Name"]?.ToString());
            Assert.Equal(88.0, Convert.ToDouble(batches[0].Rows[1]["Score"]));

            if (File.Exists(path)) File.Delete(path);
        }

        [Fact]
        public async Task CorruptFile_ReadBatches_WrapsAsExecutionException()
        {
            string path = $"corrupt_{Guid.NewGuid():N}.parquet";
            File.WriteAllText(path, "not a parquet file");

            try
            {
                var ds = new ParquetDataSource(SystemExecutionContext.Instance, path);

                var ex = await Assert.ThrowsAsync<ExecutionException>(
                    async () => await ds.ReadBatches().ToListAsync());

                Assert.Contains("Parquet", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
