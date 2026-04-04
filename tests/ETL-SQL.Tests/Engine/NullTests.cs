using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;
using ETL_SQL.Common;

namespace ETL_SQL.Tests
{
    public class NullTests
    {
        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }

        [Fact]
        public async Task TestIsNullWhere()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await evaluator.Evaluate(Parse("CREATE TABLE #t (id int, val varchar(50)); INSERT INTO #t (id, val) VALUES (1, 'a'), (2, NULL), (3, 'c');"));
            
            var batches = await evaluator.ExecuteQuery(Parse("SELECT * FROM #t WHERE val IS NULL;").Statements[0]).ToListAsync();
            Assert.NotEmpty(batches);
            var totalRows = batches.Sum(b => b.Rows.Count);
            Assert.Equal(1, totalRows);
            Assert.Equal("2", batches[0].Rows[0]["id"]?.ToString());
        }

        [Fact]
        public async Task TestIsNotNullWhere()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await evaluator.Evaluate(Parse("CREATE TABLE #t (id int, val varchar(50)); INSERT INTO #t (id, val) VALUES (1, 'a'), (2, NULL), (3, 'c');"));
            
            var batches = await evaluator.ExecuteQuery(Parse("SELECT * FROM #t WHERE val IS NOT NULL;").Statements[0]).ToListAsync();
            var totalRows = batches.Sum(b => b.Rows.Count);
            Assert.Equal(2, totalRows);
        }

        [Fact]
        public async Task TestCoalesce()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await evaluator.ExecuteQuery(Parse("SELECT COALESCE(NULL, 'fallback', 'ignored') AS result;").Statements[0]).ToListAsync();
            Assert.Equal("fallback", batches[0].Rows[0]["result"]?.ToString());
        }

        [Fact]
        public async Task TestNullJoin()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await evaluator.Evaluate(Parse(@"
                CREATE TABLE #t1 (id int, val varchar(50));
                INSERT INTO #t1 (id, val) VALUES (1, NULL);
                
                CREATE TABLE #t2 (id int, val varchar(50));
                INSERT INTO #t2 (id, val) VALUES (2, NULL);
            "));
            
            var batches = await evaluator.ExecuteQuery(Parse("SELECT * FROM #t1 t1 JOIN #t2 t2 ON t1.val = t2.val;").Statements[0]).ToListAsync();
            var totalRows = batches.Sum(b => b.Rows.Count);
            // In SQL, NULL = NULL is FALSE/UNKNOWN, so join should result in 0 rows
            Assert.Equal(0, totalRows);
        }
    }
}
