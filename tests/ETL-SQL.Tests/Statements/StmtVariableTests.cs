using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core.Parser;
using ETL_SQL.App;
using System;

namespace ETL_SQL.Tests.Statements
{
    public class ShowVariablesTests
    {
        private async Task<Evaluator> GetEvaluator()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            return provider.GetRequiredService<Evaluator>();
        }

        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }

        [Fact]
        public async Task TestShowVariables_ListsGlobalVariables()
        {
            var eval = await GetEvaluator();
            var sql = @"
                DECLARE @count INT = 42;
                DECLARE @name STRING = 'Chuck';
                SHOW VARIABLES;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            Assert.NotNull(results);
            Assert.True(results.Rows.Count >= 2);
            
            var countRow = results.Rows.FirstOrDefault(r => r["Name"].ToString() == "@count");
            Assert.NotNull(countRow);
            Assert.Equal(42m, Convert.ToDecimal(countRow["Value"]));
            Assert.Equal("Global", countRow["Scope"]);

            var nameRow = results.Rows.FirstOrDefault(r => r["Name"].ToString() == "@name");
            Assert.NotNull(nameRow);
            Assert.Equal("Chuck", nameRow["Value"]);
        }

        [Fact]
        public async Task TestShowVariables_MasksSensitiveVariables()
        {
            var eval = await GetEvaluator();
            eval.ShowPassword = false; // Ensure masking is on

            var sql = @"
                DECLARE @secret STRING = 'my-password' PASSWORD;
                SHOW VARIABLES;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            var secretRow = results.Rows.FirstOrDefault(r => r["Name"].ToString() == "@secret");
            Assert.NotNull(secretRow);
            Assert.Equal("*******", secretRow["Value"]);
            Assert.Equal(true, secretRow["IsSensitive"]);
        }

        [Fact]
        public async Task TestShowVariables_ShowsSensitiveVariables_WhenExplicitlyEnabled()
        {
            var eval = await GetEvaluator();
            eval.ShowPassword = true; 

            var sql = @"
                DECLARE @secret STRING = 'my-password' PASSWORD;
                SHOW VARIABLES;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            var secretRow = results.Rows.FirstOrDefault(r => r["Name"].ToString() == "@secret");
            Assert.NotNull(secretRow);
            Assert.Equal("my-password", secretRow["Value"]);
        }

        [Fact]
        public async Task TestShowVariablesInto_WritesToTempTable()
        {
            var eval = await GetEvaluator();
            var sql = @"
                DECLARE @v INT = 1;
                SHOW VARIABLES INTO #myVars;
                SELECT * FROM #myVars WHERE Name = '@v';
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            Assert.NotNull(results);
            Assert.Single(results.Rows);
            Assert.Equal(1m, Convert.ToDecimal(results.Rows[0]["Value"]));
        }

        [Fact]
        public async Task TestShowLocalVariables_OnlyShowsLocalScope()
        {
            var eval = await GetEvaluator();
            var sql = @"
                DECLARE @outer STRING = 'outside';
                
                CREATE PROCEDURE TestScope()
                AS
                BEGIN
                    DECLARE @inner STRING = 'inside';
                    SHOW LOCAL VARIABLES;
                END;

                EXECUTE TestScope;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            Assert.NotNull(results);
            // Should only contain @inner because Procedures push a new scope
            Assert.Contains(results.Rows, r => r["Name"].ToString() == "@inner");
            Assert.DoesNotContain(results.Rows, r => r["Name"].ToString() == "@outer");
        }
    }
}
