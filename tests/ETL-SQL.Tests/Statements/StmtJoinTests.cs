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
        [Trait("Category", "Smoke.Core")]
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
        public async Task TestCommaJoin_TwoTables()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_CommaJoin", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            try
            {
                var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

                string pathU = Path.Combine(testDir, "cj_u.csv").Replace("\\", "/");
                string pathO = Path.Combine(testDir, "cj_o.csv").Replace("\\", "/");
                await File.WriteAllTextAsync(pathU, "id,name\n1,Alice\n2,Bob");
                await File.WriteAllTextAsync(pathO, "uid,amount\n1,100\n1,50\n2,200");

                await ev.Evaluate(Parse($"CREATE CONNECTION cj_u ON FLATFILE('{pathU}');"));
                await ev.Evaluate(Parse($"CREATE CONNECTION cj_o ON FLATFILE('{pathO}');"));

                var res = await ev.ExecuteQuery(
                    Parse("SELECT cj_u.name, cj_o.amount FROM cj_u, cj_o WHERE cj_u.id = cj_o.uid ORDER BY cj_u.name, cj_o.amount;").Statements[0]
                ).ToListAsync();
                var rows = res.SelectMany(b => b.Rows).ToList();

                Assert.Equal(3, rows.Count);
                Assert.Equal("Alice", rows[0]["name"]?.ToString());
                Assert.Equal("50", rows[0]["amount"]?.ToString());
                Assert.Equal("Alice", rows[1]["name"]?.ToString());
                Assert.Equal("100", rows[1]["amount"]?.ToString());
                Assert.Equal("Bob", rows[2]["name"]?.ToString());
                Assert.Equal("200", rows[2]["amount"]?.ToString());
            }
            finally
            {
                if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
            }
        }

        [Fact]
        public async Task TestCommaJoin_ThreeTables()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_CommaJoin3", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            try
            {
                var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

                string pathU = Path.Combine(testDir, "t1.csv").Replace("\\", "/");
                string pathO = Path.Combine(testDir, "t2.csv").Replace("\\", "/");
                string pathC = Path.Combine(testDir, "t3.csv").Replace("\\", "/");
                await File.WriteAllTextAsync(pathU, "id,val\n1,A\n2,B");
                await File.WriteAllTextAsync(pathO, "id,fk\n10,1\n11,2");
                await File.WriteAllTextAsync(pathC, "fk2,label\n10,X\n11,Y");

                await ev.Evaluate(Parse($"CREATE CONNECTION t1 ON FLATFILE('{pathU}');"));
                await ev.Evaluate(Parse($"CREATE CONNECTION t2 ON FLATFILE('{pathO}');"));
                await ev.Evaluate(Parse($"CREATE CONNECTION t3 ON FLATFILE('{pathC}');"));

                var res = await ev.ExecuteQuery(
                    Parse("SELECT t1.val, t3.label FROM t1, t2, t3 WHERE t1.id = t2.fk AND t2.id = t3.fk2 ORDER BY t1.val;").Statements[0]
                ).ToListAsync();
                var rows = res.SelectMany(b => b.Rows).ToList();

                Assert.Equal(2, rows.Count);
                Assert.Equal("A", rows[0]["val"]?.ToString());
                Assert.Equal("X", rows[0]["label"]?.ToString());
                Assert.Equal("B", rows[1]["val"]?.ToString());
                Assert.Equal("Y", rows[1]["label"]?.ToString());
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
