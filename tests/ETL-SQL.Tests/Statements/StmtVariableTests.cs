using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

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
                SELECT * FROM eng.variables;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            Assert.NotNull(results);
            Assert.True(results.Rows.Count >= 2);

            var countRow = results.Rows.FirstOrDefault(r => r["variable_name"].ToString() == "@count");
            Assert.NotNull(countRow);
            Assert.Equal(42m, Convert.ToDecimal(countRow["value"]));
            Assert.Equal("Global", countRow["scope"]);

            var nameRow = results.Rows.FirstOrDefault(r => r["variable_name"].ToString() == "@name");
            Assert.NotNull(nameRow);
            Assert.Equal("Chuck", nameRow["value"]);
        }

        [Fact]
        public async Task TestShowVariables_MasksSensitiveVariables()
        {
            var eval = await GetEvaluator();
            eval.ShowPassword = false; // Ensure masking is on

            var sql = @"
                DECLARE @secret STRING = 'my-password' PASSWORD;
                SELECT * FROM eng.variables;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            var secretRow = results.Rows.FirstOrDefault(r => r["variable_name"].ToString() == "@secret");
            Assert.NotNull(secretRow);
            Assert.Equal("*******", secretRow["value"]);
            Assert.Equal(true, secretRow["is_sensitive"]);
        }

        [Fact]
        public async Task TestShowVariables_ShowsSensitiveVariables_WhenExplicitlyEnabled()
        {
            var eval = await GetEvaluator();
            eval.ShowPassword = true;

            var sql = @"
                DECLARE @secret STRING = 'my-password' PASSWORD;
                SELECT * FROM eng.variables;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            var secretRow = results.Rows.FirstOrDefault(r => r["variable_name"].ToString() == "@secret");
            Assert.NotNull(secretRow);
            Assert.Equal("my-password", secretRow["value"]);
        }

        [Fact]
        public async Task TestShowVariablesInto_WritesToTempTable()
        {
            var eval = await GetEvaluator();
            var sql = @"
                DECLARE @v INT = 1;
                SELECT * INTO #myVars FROM eng.variables;
                SELECT * FROM #myVars WHERE variable_name = '@v';
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            Assert.NotNull(results);
            Assert.Single(results.Rows);
            Assert.Equal(1m, Convert.ToDecimal(results.Rows[0]["value"]));
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
                    SELECT * FROM eng.variables WHERE scope = 'Local';
                END;

                EXECUTE TestScope;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            Assert.NotNull(results);
            // Should only contain @inner because Procedures push a new scope
            Assert.Contains(results.Rows, r => r["variable_name"].ToString() == "@inner");
            Assert.DoesNotContain(results.Rows, r => r["variable_name"].ToString() == "@outer");
        }

        [Fact]
        public async Task TestVariableInterpolation_InStringLiteral()
        {
            var eval = await GetEvaluator();
            var sql = @"
                DECLARE @date_str STRING = '2026-07-22';
                DECLARE @filename STRING = 'data_out_${@date_str}.csv';
                DECLARE @path STRING = 'C:\tmp\sent\${date_str}_export.csv';
                SELECT @filename AS Filename, @path AS Path;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            Assert.NotNull(results);
            Assert.Single(results.Rows);
            Assert.Equal("data_out_2026-07-22.csv", results.Rows[0]["Filename"]?.ToString());
            Assert.Equal(@"C:\tmp\sent\2026-07-22_export.csv", results.Rows[0]["Path"]?.ToString());
        }
    }
}
