using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Connectors.Json;
using Spectre.Console;
using ETL_SQL.Common;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests
{
    public class JsonTests
    {
        [Fact]
        public async Task TestBasicJsonRead()
        {
            string jsonFile = "JsonTest_Basic.json";
            await File.WriteAllTextAsync(jsonFile, @"[
                {""id"": 1, ""name"": ""Test1""},
                {""id"": 2, ""name"": ""Test2""}
            ]");

            try
            {
                var ds = new JsonDataSource(SystemExecutionContext.Instance, jsonFile);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Single(batches);
                Assert.Equal(2, batches[0].Rows.Count);
                Assert.Equal("Test1", batches[0].Rows[0]["name"]?.ToString());
            }
            finally { if (File.Exists(jsonFile)) File.Delete(jsonFile); }
        }

        [Fact]
        public async Task TestNestedJsonRead()
        {
            string jsonFile = "JsonTest_Nested.json";
            await File.WriteAllTextAsync(jsonFile, @"{
                ""status"": ""success"",
                ""data"": {
                    ""items"": [
                        {""val"": 10},
                        {""val"": 20}
                    ]
                }
            }");

            try
            {
                var options = new Dictionary<string, string> { { "ROOT_PATH", "data.items" } };
                var ds = new JsonDataSource(SystemExecutionContext.Instance, jsonFile, options);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Single(batches);
                Assert.Equal(2, batches[0].Rows.Count);
                Assert.Equal(20, Convert.ToInt32(batches[0].Rows[1]["val"]));
            }
            finally { if (File.Exists(jsonFile)) File.Delete(jsonFile); }
        }

        [Fact]
        public async Task TestJsonAggregation()
        {
            string jsonFile = "JsonTest_Agg.json";
            await File.WriteAllTextAsync(jsonFile, @"[
                {""grp"": ""A"", ""val"": 10},
                {""grp"": ""A"", ""val"": 20},
                {""grp"": ""B"", ""val"": 30}
            ]");

            try
            {
                var source = @"CREATE CONNECTION src ON JSON ('" + jsonFile + @"');
                               SELECT grp, SUM(val) as total FROM src GROUP BY grp ORDER BY grp;";
                
                var lexer = new Lexer(source);
                var parser = new Parser(lexer.Tokenize());
                var script = parser.Parse();
                
                var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                evaluator.IsVerbose = false;
                await evaluator.Evaluate(script);

                var results = evaluator.LastResult as DataTable;
                Assert.NotNull(results);
                Assert.Equal(2, results.Rows.Count);
                Assert.Equal("A", results.Rows[0]["grp"]?.ToString());
                Assert.Equal(30m, Convert.ToDecimal(results.Rows[0]["total"]));
            }
            finally { if (File.Exists(jsonFile)) File.Delete(jsonFile); }
        }
    }
}
