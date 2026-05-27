using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Connectors.Avro;
using Spectre.Console;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Tests.Integration
{
    [Trait("Category", "Integration")]
    [Trait("Connector", "AVRO")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class AvroTests
    {

        private static async IAsyncEnumerable<DataTable> ArrayToAsyncEnumerable(DataTable[] data)
        {
            foreach(var d in data)
            {
                yield return d;
            }
            await Task.CompletedTask;
        }

        [Fact]
        public async Task TestWriteReadAvro()
        {
            string path = "test_data.avro";
            if (File.Exists(path)) File.Delete(path);

            var ds = new AvroDataSource(SystemExecutionContext.Instance, path);
            var batch = new DataTable();
            batch.SetColumns(new[] { "ID", "Name", "Active" });
            
            var r1 = new Row(); r1["ID"] = 101; r1["Name"] = "X"; r1["Active"] = true;
            var r2 = new Row(); r2["ID"] = 102; r2["Name"] = "Y"; r2["Active"] = false;
            await batch.AddRowAsync(r1);
            await batch.AddRowAsync(r2);

            await ds.WriteBatches(ArrayToAsyncEnumerable(new[] { batch }));

            Assert.True(File.Exists(path), "Avro file should be created");

            var dsRead = new AvroDataSource(SystemExecutionContext.Instance, path);
            var batches = await dsRead.ReadBatches().ToListAsync();
            
            Assert.True(batches.Count == 1, "Should read 1 batch");
            Assert.True(batches[0].Rows.Count == 2, "Should read 2 rows");
            Assert.True(batches[0].Rows[0]["Name"]?.ToString() == "X", "First row Name should be X");
            Assert.True(Convert.ToBoolean(batches[0].Rows[1]["Active"]) == false, "Second row Active should be false");

            if (File.Exists(path)) File.Delete(path);
        }

        [Fact]
        public async Task CorruptFile_ReadBatches_WrapsAsExecutionException()
        {
            string path = $"corrupt_{Guid.NewGuid():N}.avro";
            File.WriteAllText(path, "not an avro file");

            try
            {
                var ds = new AvroDataSource(SystemExecutionContext.Instance, path);

                var ex = await Assert.ThrowsAsync<ExecutionException>(
                    async () => await ds.ReadBatches().ToListAsync());

                Assert.Contains("Avro", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task TestBooleanRegression()
        {
            string path = "regress_bool.avro";
            if (File.Exists(path)) File.Delete(path);

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = @"
                CREATE CONNECTION test_avro AS AVRO('regress_bool.avro');
                DROP TABLE IF EXISTS #src;
                CREATE TABLE #src (id INT, active BOOLEAN);
                INSERT INTO #src VALUES (1, TRUE), (2, FALSE), (3, TRUE);
                INSERT INTO test_avro SELECT * FROM #src;
                CREATE CONNECTION check_avro AS AVRO('regress_bool.avro');
            ";
            await evaluator.Evaluate(Parse(script));

            var checkConn = evaluator.Connections["check_avro"];
            var results = await checkConn.ReadBatches().ToListAsync();
            
            Assert.True(results.Count > 0, "Should have results");
            var rows = results[0].Rows;
            Assert.True(rows.Count == 3, "Should have 3 rows");
            Assert.True(Convert.ToBoolean(rows[0]["active"]) == true, "Row 1 should be true");
            Assert.True(Convert.ToBoolean(rows[1]["active"]) == false, "Row 2 should be false");
            Assert.True(Convert.ToBoolean(rows[2]["active"]) == true, "Row 3 should be true");

            if (File.Exists(path)) File.Delete(path);
        }

        private static Script Parse(string source)
        {
            return new Parser(new Lexer(source).Tokenize()).Parse();
        }
    }
}
