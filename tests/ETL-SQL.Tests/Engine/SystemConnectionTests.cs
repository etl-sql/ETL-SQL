using Xunit;
using ETL_SQL.Engine;
using ETL_SQL.Data;

using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;

namespace ETL_SQL.Tests.Engine
{
    public class SystemConnectionTests
    {
        private Evaluator CreateEvaluator()
        {
            return DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        }

        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }

        [Fact]
        public async Task TestMockDbConnectionAndQuery()
        {
            var evaluator = CreateEvaluator();
            
            // 1. Create connection
            await evaluator.Evaluate(Parse("CREATE CONNECTION m AS MOCKDB();"));
            
            Assert.True(evaluator.Connections.ContainsKey("m"), "Connection 'm' should be registered");
            
            // 2. Select * from mock table
            var results = await evaluator.ExecuteQuery(Parse("SELECT * FROM m.Users;").Statements[0]).ToListAsync();
            var result = results.FirstOrDefault();
            
            Assert.NotNull(result);
            Assert.Contains("UserID", result.ColumnNames);
            Assert.Contains("UserName", result.ColumnNames);
            Assert.NotEmpty(result.Rows);

            // 3. Select specific columns with alias (Verifies MockSqlDataSource filtering fix)
            var results2 = await evaluator.ExecuteQuery(Parse("SELECT u.UserID FROM m.Users u;").Statements[0]).ToListAsync();
            var result2 = results2.FirstOrDefault();
            Assert.NotNull(result2);
            Assert.Single(result2.ColumnNames);
            Assert.Equal("UserID", result2.ColumnNames[0]);
            Assert.NotEmpty(result2.Rows);
        }

        [Fact]
        public async Task TestFlatFileConnectionAndQuery()
        {
            var evaluator = CreateEvaluator();
            var testCsv = Path.Combine(Directory.GetCurrentDirectory(), "test_categories_sys2.csv");
            File.WriteAllLines(testCsv, new[] { "id,category", "1,Electronics", "2,Books" });

            try
            {
                // 1. Create connection
                await evaluator.Evaluate(Parse($"CREATE CONNECTION c AS FLATFILE('{testCsv.Replace("\\", "/")}');"));
                
                Assert.True(evaluator.Connections.ContainsKey("c"), "Connection 'c' should be registered");
                
                // 2. Select columns
                var results = await evaluator.ExecuteQuery(Parse("SELECT c.id, c.category FROM c;").Statements[0]).ToListAsync();
                var result = results.FirstOrDefault();
                
                Assert.NotNull(result);
                Assert.Equal(2, result.ColumnNames.Count);
                Assert.Contains("id", result.ColumnNames);
                Assert.Contains("category", result.ColumnNames);
                Assert.Equal(2, result.Rows.Count);
            }
            finally
            {
                if (File.Exists(testCsv)) File.Delete(testCsv);
            }
        }

        [Fact]
        public async Task TestAliasColumnSuggestionsScoping()
        {
             var script = Parse("SELECT u.UserID FROM m.Users u;");
             var select = script.Statements.OfType<SelectStatement>().First();
             
             Assert.Equal("Users", select.FromTable.TableName);
             Assert.Equal("u", select.FromTable.Alias);
             
             var col = select.Columns.First();
             Assert.IsType<IdentifierExpression>(col.Expression);
             Assert.Equal("u.UserID", ((IdentifierExpression)col.Expression).Name);
        }

        [Fact]
        public async Task TestMultipleConnectionsInOneScript()
        {
            var sql = @"
CREATE CONNECTION c AS FLATFILE('categories.csv');
SELECT c.id FROM c;

CREATE CONNECTION m AS MOCKDB();
SELECT * FROM m.Users;
";
            var script = Parse(sql);
            var connections = script.Statements.OfType<CreateConnectionStatement>().ToList();
            
            Assert.Equal(2, connections.Count);
            Assert.Equal("c", connections[0].ConnectionName);
            Assert.Equal("m", connections[1].ConnectionName);
            
            var selects = script.Statements.OfType<SelectStatement>().ToList();
            Assert.Equal(2, selects.Count);
        }
    }
}
