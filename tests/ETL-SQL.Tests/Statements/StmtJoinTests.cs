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

namespace ETL_SQL.Tests.Statements
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
            string testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            
            try
            {
                var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                var data1 = "ID\n1\n2";
                var data2 = "ID\n2\n3";
                string path1 = Path.Combine(testDir, "rj1.csv").Replace("\\", "/");
                string path2 = Path.Combine(testDir, "rj2.csv").Replace("\\", "/");
                
                await File.WriteAllTextAsync(path1, data1);
                await File.WriteAllTextAsync(path2, data2);
                
                await ev.Evaluate(Parse($"CREATE CONNECTION rj1 ON FLATFILE('{path1}');"));
                await ev.Evaluate(Parse($"CREATE CONNECTION rj2 ON FLATFILE('{path2}');"));
                
                var res = await ev.ExecuteQuery(Parse("SELECT rj1.ID as L, rj2.ID as R FROM rj1 RIGHT JOIN rj2 ON rj1.ID = rj2.ID;").Statements[0]).ToListAsync();
                var rows = res.SelectMany(b => b.Rows).ToList();
                
                Assert.Equal(2, rows.Count);
                Assert.Contains(rows, r => r["L"] == null && r["R"]?.ToString() == "3");
                Assert.Contains(rows, r => r["L"]?.ToString() == "2" && r["R"]?.ToString() == "2");
            }
            finally
            {
                if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
            }
        }

        [Fact]
        public async Task TestFullJoin()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                var data1 = "ID\n1\n2";
                var data2 = "ID\n2\n3";
                string path1 = Path.Combine(testDir, "fj1.csv").Replace("\\", "/");
                string path2 = Path.Combine(testDir, "fj2.csv").Replace("\\", "/");

                await File.WriteAllTextAsync(path1, data1);
                await File.WriteAllTextAsync(path2, data2);
                
                await ev.Evaluate(Parse($"CREATE CONNECTION fj1 ON FLATFILE('{path1}');"));
                await ev.Evaluate(Parse($"CREATE CONNECTION fj2 ON FLATFILE('{path2}');"));
                
                var sql = "SELECT fj1.ID as L, fj2.ID as R FROM fj1 FULL JOIN fj2 ON fj1.ID = fj2.ID;";
                var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
                var rows = res.SelectMany(b => b.Rows).ToList();
                
                Assert.Equal(3, rows.Count);
                Assert.Contains(rows, r => r["L"]?.ToString() == "1" && r["R"] == null);
                Assert.Contains(rows, r => r["L"]?.ToString() == "2" && r["R"]?.ToString() == "2");
                Assert.Contains(rows, r => r["L"] == null && r["R"]?.ToString() == "3");
            }
            finally
            {
                if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
            }
        }

        [Fact]
        public async Task TestCrossApply()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                var data1 = "ID\n1\n2";
                string path = Path.Combine(testDir, "ca.csv").Replace("\\", "/");
                await File.WriteAllTextAsync(path, data1);
                await ev.Evaluate(Parse($"CREATE CONNECTION c1 ON FLATFILE('{path}');"));
                
                var sql = "SELECT c1.ID, s.Val FROM c1 CROSS APPLY (SELECT 10 as Val UNION ALL SELECT 20) s;";
                var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
                var rows = res.SelectMany(b => b.Rows).ToList();
                
                Assert.Equal(4, rows.Count);
            }
            finally
            {
                if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
            }
        }

        [Fact]
        public async Task TestOuterApply()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                var data1 = "ID\n1";
                string path = Path.Combine(testDir, "oa.csv").Replace("\\", "/");
                await File.WriteAllTextAsync(path, data1);
                await ev.Evaluate(Parse($"CREATE CONNECTION c1 ON FLATFILE('{path}');"));
                
                var sql = "SELECT c1.ID, s.Val FROM c1 OUTER APPLY (SELECT 10 as Val WHERE 1=0) s;";
                var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
                var rows = res.SelectMany(b => b.Rows).ToList();
                
                Assert.Single(rows);
                Assert.Null(rows[0]["Val"]);
            }
            finally
            {
                if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
            }
        }

        [Fact]
        public async Task TestSubqueryInJoin()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                var data1 = "ID,Name\n1,Alice\n2,Bob";
                string path = Path.Combine(testDir, "sj.csv").Replace("\\", "/");
                await File.WriteAllTextAsync(path, data1);
                await ev.Evaluate(Parse($"CREATE CONNECTION c ON FLATFILE('{path}');"));
                
                var sql = "SELECT * FROM c JOIN (SELECT '1' as SubID) s ON c.ID = s.SubID;";
                var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
                var rows = res.SelectMany(b => b.Rows).ToList();
                
                Assert.Single(rows);
                Assert.Equal("Alice", rows[0]["Name"]?.ToString());
            }
            finally
            {
                if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
            }
        }
    }
}
