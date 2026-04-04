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
    public class ExplainTests
    {
        private static Script Parse(string sql) => new Lexer(sql).TokenizeToScript();
        



        [Fact]
        public async Task TestExplainGlobalAggregate()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = Parse("EXPLAIN SELECT COUNT(*) FROM DUAL;");
            await ev.Evaluate(script);
            var plan = ev.LastResult as DataTable;
            Assert.True(plan != null, "Plan should be a DataTable");
            Assert.True(plan!.Rows.Any(r => r["Operation"]?.ToString() == "Aggregate" && r["Details"]?.ToString() == "Global Aggregate"), "Should show Global Aggregate");
        }

        [Fact]
        public async Task TestExplainGroupBy()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = Parse("EXPLAIN SELECT A, COUNT(*) FROM DUAL GROUP BY A;");
            await ev.Evaluate(script);
            var plan = ev.LastResult as DataTable;
            Assert.True(plan != null, "Plan should be a DataTable");
            Assert.True(plan!.Rows.Any(r => r["Operation"]?.ToString() == "Aggregate" && r["Details"]?.ToString()?.Contains("Group By: A") == true), "Should show Group By A");
        }

        [Fact]
        public async Task TestExplainOrderBy()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = Parse("EXPLAIN SELECT * FROM DUAL ORDER BY A DESC;");
            await ev.Evaluate(script);
            var plan = ev.LastResult as DataTable;
            Assert.True(plan != null, "Plan should be a DataTable");
            Assert.True(plan!.Rows.Any(r => r["Operation"]?.ToString() == "Sort" && r["Details"]?.ToString()?.Contains("A DESC") == true), "Should show Sort by A DESC");
        }

        [Fact]
        public async Task TestExplainLimit()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = Parse("EXPLAIN SELECT * FROM DUAL LIMIT 10;");
            await ev.Evaluate(script);
            var plan = ev.LastResult as DataTable;
            Assert.True(plan != null, "Plan should be a DataTable");
            Assert.True(plan!.Rows.Any(r => r["Operation"]?.ToString() == "Top/Limit"), "Should show Top/Limit");
        }

        [Fact]
        public async Task TestExplainUnion()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = Parse("EXPLAIN SELECT 1 FROM DUAL UNION ALL SELECT 2 FROM DUAL;");
            await ev.Evaluate(script);
            var plan = ev.LastResult as DataTable;
            Assert.True(plan != null, "Plan should be a DataTable");
            Assert.True(plan!.Rows.Any(r => r["Operation"]?.ToString() == "Set Operation (UNION ALL)"), "Should show Set Operation (UNION ALL)");
        }

        [Fact]
        public async Task TestExplainSubqueryIndicator()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = Parse("EXPLAIN SELECT * FROM DUAL WHERE A = (SELECT 1 FROM DUAL);");
            await ev.Evaluate(script);
            var plan = ev.LastResult as DataTable;
            Assert.True(plan != null, "Plan should be a DataTable");
            Assert.True(plan!.Rows.Any(r => r["Operation"]?.ToString() == "Filter" && r["Details"]?.ToString()?.Contains("[Subquery]") == true), "Should show Filter with [Subquery] indicator");
        }

        [Fact]
        public async Task TestExplainJoin()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = Parse("EXPLAIN SELECT * FROM DUAL A JOIN DUAL B ON A.X = B.X;");
            await ev.Evaluate(script);
            var plan = ev.LastResult as DataTable;
            Assert.True(plan != null, "Plan should be a DataTable");
            Assert.True(plan!.Rows.Any(r => r["Operation"]?.ToString()?.Contains("Join") == true), "Should show Join operation");
        }

        [Fact]
        public async Task TestExplainDistinct()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = Parse("EXPLAIN SELECT DISTINCT * FROM DUAL;");
            await ev.Evaluate(script);
            var plan = ev.LastResult as DataTable;
            Assert.True(plan != null, "Plan should be a DataTable");
            Assert.True(plan!.Rows.Any(r => r["Operation"]?.ToString() == "Distinct"), "Should show Distinct operation");
        }

        [Fact]
        public async Task TestExplainIndexSeek()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            // Create table and index
            await ev.Evaluate(Parse("CREATE TABLE #IdxTest (Id INT, Name STRING);"));
            await ev.Evaluate(Parse("CREATE INDEX IX_Id ON #IdxTest (Id);"));
            
            // Explain a query that should use the index
            var script = Parse("EXPLAIN SELECT * FROM #IdxTest WHERE Id = 1;");
            await ev.Evaluate(script);
            
            var plan = ev.LastResult as DataTable;
            Assert.NotNull(plan);
            
            // Check for Index Seek operation
            Assert.True(plan!.Rows.Any(r => r["Operation"]?.ToString() == "Index Seek" && r["Details"]?.ToString()?.Contains("Index: Id") == true), 
                "Should show Index Seek for column Id");
        }
    }
}
