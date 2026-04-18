using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Engine;

namespace ETL_SQL.Tests.Statements
{
    public class ExecuteParameterTests
    {
        private static Script Parse(string source)
        {
            var tokens = new Lexer(source).Tokenize();
            return new Parser(tokens, source).Parse();
        }

        [Fact]
        public async Task TestIndexedParameters_Pushdown()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE CONNECTION MyDb ON MOCKDB('mock://localhost') WITH (dialect='MSSQL');
                DECLARE @id = 123;
                EXECUTE MyDb WITH (@id) BEGIN 
                    SELECT * FROM Users WHERE UserID = ?1 OR AltID = ?1
                END;
            ";
            await ev.Evaluate(Parse(script));
            
            // The mock datasource returns the processed SQL in the ResultSets when parameters are used.
            var lastResult = ev.LastResult;
            Assert.NotNull(lastResult);
            
            string processedSql = lastResult.Rows[0]["ProcessedSql"]?.ToString() ?? "";
            
            // Verify ?1 was replaced by @p0 in both places
            Assert.Contains("@p0", processedSql);
            Assert.DoesNotContain("?1", processedSql);
            
            // Count occurrences of @p0
            int count = (processedSql.Length - processedSql.Replace("@p0", "").Length) / "@p0".Length;
            Assert.Equal(2, count);
        }

        [Fact]
        public async Task TestMixedParameters_Pushdown()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE CONNECTION MyDb ON MOCKDB('mock://localhost') WITH (dialect='MSSQL');
                DECLARE @c1 ANY = 'A';
                DECLARE @c2 ANY = 'B';
                EXECUTE MyDb WITH (@c1, @c2) BEGIN 
                    SELECT * FROM Users WHERE Col1 = ? AND Col2 = ?2 AND Col3 = ?1
                END;
            ";
            await ev.Evaluate(Parse(script));
            
            var lastResult = ev.LastResult;
            string processedSql = lastResult?.Rows[0]["ProcessedSql"]?.ToString() ?? "";
            
            // ? maps to @p0
            // ?2 maps to @p1
            // ?1 maps to @p0
            Assert.Contains("Col1 = @p0", processedSql);
            Assert.Contains("Col2 = @p1", processedSql);
            Assert.Contains("Col3 = @p0", processedSql);
        }

        [Fact]
        public async Task TestExecuteStringLiteral_WithIntoAndWith()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE CONNECTION MyDb ON MOCKDB('mock://localhost') WITH (dialect='MSSQL');
                DECLARE @cat_id ANY = 5;
                EXECUTE (
                  'SELECT id, name FROM remote_table WHERE category_id = ?1'
                ) AT MyDb INTO #target WITH (@cat_id);
            ";
            await ev.Evaluate(Parse(script));
            
            // Verify #target exists and was loaded
            Assert.True(ev.Connections.ContainsKey("#target"));
            var ds = ev.Connections["#target"];
            
            // Check the value loaded
            var batches = ds.ReadBatches();
            await foreach (var batch in batches)
            {
                Assert.Equal(5, Convert.ToInt32(batch.Rows[0]["ParameterValue"]));
                string sql = batch.Rows[0]["ProcessedSql"].ToString();
                Assert.Contains("category_id = @p0", sql);
            }
        }
    }
}
