using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;
using ETL_SQL.Common;

namespace ETL_SQL.Tests
{
    public class JoinTests
    {
        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();

        [Fact]
        public async Task TestCrossJoin()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("DECLARE @T1 LIST = ['A', 'B'];"));
            await ev.Evaluate(Parse("DECLARE @T2 LIST = [1, 2];"));
            
            var res = await ev.ExecuteQuery(Parse("SELECT * FROM @T1 AS t1 CROSS JOIN @T2 AS t2;").Statements[0]).ToListAsync();
            var allRows = res.SelectMany(b => b.Rows).ToList();
            
            Assert.Equal(4, allRows.Count);
        }

        [Fact]
        public async Task TestRightJoin()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var data1 = "ID\n1\n2";
            var data2 = "ID\n2\n3";
            await File.WriteAllTextAsync("rj1.csv", data1);
            await File.WriteAllTextAsync("rj2.csv", data2);
            
            await ev.Evaluate(Parse("CREATE CONNECTION rj1 ON FLATFILE('rj1.csv');"));
            await ev.Evaluate(Parse("CREATE CONNECTION rj2 ON FLATFILE('rj2.csv');"));
            
            var res = await ev.ExecuteQuery(Parse("SELECT rj1.ID as L, rj2.ID as R FROM rj1 RIGHT JOIN rj2 ON rj1.ID = rj2.ID;").Statements[0]).ToListAsync();
            var rows = res.SelectMany(b => b.Rows).ToList();
            
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r["L"] == null && r["R"]?.ToString() == "3");
            Assert.Contains(rows, r => r["L"]?.ToString() == "2" && r["R"]?.ToString() == "2");
            
            if (File.Exists("rj1.csv")) File.Delete("rj1.csv");
            if (File.Exists("rj2.csv")) File.Delete("rj2.csv");
        }

        [Fact]
        public async Task TestFullJoin()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var data1 = "ID\n1\n2";
            var data2 = "ID\n2\n3";
            await File.WriteAllTextAsync("fj1.csv", data1);
            await File.WriteAllTextAsync("fj2.csv", data2);
            
            await ev.Evaluate(Parse("CREATE CONNECTION fj1 ON FLATFILE('fj1.csv');"));
            await ev.Evaluate(Parse("CREATE CONNECTION fj2 ON FLATFILE('fj2.csv');"));
            
            var sql = "SELECT fj1.ID as L, fj2.ID as R FROM fj1 FULL JOIN fj2 ON fj1.ID = fj2.ID;";
            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            var rows = res.SelectMany(b => b.Rows).ToList();
            
            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, r => r["L"]?.ToString() == "1" && r["R"] == null);
            Assert.Contains(rows, r => r["L"]?.ToString() == "2" && r["R"]?.ToString() == "2");
            Assert.Contains(rows, r => r["L"] == null && r["R"]?.ToString() == "3");
            
            if (File.Exists("fj1.csv")) File.Delete("fj1.csv");
            if (File.Exists("fj2.csv")) File.Delete("fj2.csv");
        }

        [Fact]
        public async Task TestCrossApply()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var data1 = "ID\n1\n2";
            await File.WriteAllTextAsync("ca.csv", data1);
            await ev.Evaluate(Parse("CREATE CONNECTION c1 ON FLATFILE('ca.csv');"));
            
            var sql = "SELECT c1.ID, s.Val FROM c1 CROSS APPLY (SELECT 10 as Val UNION ALL SELECT 20) s;";
            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            var rows = res.SelectMany(b => b.Rows).ToList();
            
            Assert.Equal(4, rows.Count);
            
            if (File.Exists("ca.csv")) File.Delete("ca.csv");
        }

        [Fact]
        public async Task TestOuterApply()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var data1 = "ID\n1";
            await File.WriteAllTextAsync("oa.csv", data1);
            await ev.Evaluate(Parse("CREATE CONNECTION c1 ON FLATFILE('oa.csv');"));
            
            var sql = "SELECT c1.ID, s.Val FROM c1 OUTER APPLY (SELECT 10 as Val WHERE 1=0) s;";
            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            var rows = res.SelectMany(b => b.Rows).ToList();
            
            Assert.Single(rows);
            Assert.Null(rows[0]["Val"]);
            
            if (File.Exists("oa.csv")) File.Delete("oa.csv");
        }

        [Fact]
        public async Task TestSubqueryInJoin()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var data1 = "ID,Name\n1,Alice\n2,Bob";
            await File.WriteAllTextAsync("sj.csv", data1);
            await ev.Evaluate(Parse("CREATE CONNECTION c ON FLATFILE('sj.csv');"));
            
            var sql = "SELECT * FROM c JOIN (SELECT '1' as SubID) s ON c.ID = s.SubID;";
            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            var rows = res.SelectMany(b => b.Rows).ToList();
            
            Assert.Single(rows);
            Assert.Equal("Alice", rows[0]["Name"]?.ToString());
            
            if (File.Exists("sj.csv")) File.Delete("sj.csv");
        }
    }
}
