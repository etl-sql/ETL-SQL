using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.SqlLogicTests
{
    /// <summary>
    /// Reproduces specific correctness regressions found during SLT corpus runs,
    /// without loading the full corpus (which OOMs on low-memory machines).
    /// Each test is self-contained: it sets up t1 fresh and runs a single query.
    /// </summary>
    [Trait("Category", "SLT")]
    public class CorpusRegressionTests
    {
        // The 30 INSERT statements from select2.test (with NULLs in various columns)
        private static readonly string[] T1InsertsSelect2 =
        [
            "INSERT INTO t1(e,c,b,d,a) VALUES(NULL,102,NULL,101,104)",
            "INSERT INTO t1(a,c,d,e,b) VALUES(107,106,108,109,105)",
            "INSERT INTO t1(e,d,b,a,c) VALUES(110,114,112,NULL,113)",
            "INSERT INTO t1(d,c,e,a,b) VALUES(116,119,117,115,NULL)",
            "INSERT INTO t1(c,d,b,e,a) VALUES(123,122,124,NULL,121)",
            "INSERT INTO t1(a,d,b,e,c) VALUES(127,128,129,126,125)",
            "INSERT INTO t1(e,c,a,d,b) VALUES(132,134,131,133,130)",
            "INSERT INTO t1(a,d,b,e,c) VALUES(138,136,139,135,137)",
            "INSERT INTO t1(e,c,d,a,b) VALUES(144,141,140,142,143)",
            "INSERT INTO t1(b,a,e,d,c) VALUES(145,149,146,NULL,147)",
            "INSERT INTO t1(b,c,a,d,e) VALUES(151,150,153,NULL,NULL)",
            "INSERT INTO t1(c,e,a,d,b) VALUES(155,157,159,NULL,158)",
            "INSERT INTO t1(c,b,a,d,e) VALUES(161,160,163,164,162)",
            "INSERT INTO t1(b,d,a,e,c) VALUES(167,NULL,168,165,166)",
            "INSERT INTO t1(d,b,c,e,a) VALUES(171,170,172,173,174)",
            "INSERT INTO t1(e,c,a,d,b) VALUES(177,176,179,NULL,175)",
            "INSERT INTO t1(b,e,a,d,c) VALUES(181,180,182,183,184)",
            "INSERT INTO t1(c,a,b,e,d) VALUES(187,188,186,189,185)",
            "INSERT INTO t1(d,b,c,e,a) VALUES(190,194,193,192,191)",
            "INSERT INTO t1(a,e,b,d,c) VALUES(199,197,198,196,195)",
            "INSERT INTO t1(b,c,d,a,e) VALUES(NULL,202,203,201,204)",
            "INSERT INTO t1(c,e,a,b,d) VALUES(208,NULL,NULL,206,207)",
            "INSERT INTO t1(c,e,a,d,b) VALUES(214,210,213,212,211)",
            "INSERT INTO t1(b,c,a,d,e) VALUES(218,215,216,217,219)",
            "INSERT INTO t1(b,e,d,a,c) VALUES(223,221,222,220,224)",
            "INSERT INTO t1(d,e,b,a,c) VALUES(226,227,228,229,225)",
            "INSERT INTO t1(a,c,b,e,d) VALUES(234,231,232,230,233)",
            "INSERT INTO t1(e,b,a,c,d) VALUES(237,236,239,NULL,238)",
            "INSERT INTO t1(e,c,b,a,d) VALUES(NULL,244,240,243,NULL)",
            "INSERT INTO t1(e,d,c,b,a) VALUES(246,248,247,249,245)",
        ];

        // The 30 INSERT statements from select1.test lines 4-93 (named-column form)
        private static readonly string[] T1Inserts =
        [
            "INSERT INTO t1(e,c,b,d,a) VALUES(103,102,100,101,104)",
            "INSERT INTO t1(a,c,d,e,b) VALUES(107,106,108,109,105)",
            "INSERT INTO t1(e,d,b,a,c) VALUES(110,114,112,111,113)",
            "INSERT INTO t1(d,c,e,a,b) VALUES(116,119,117,115,118)",
            "INSERT INTO t1(c,d,b,e,a) VALUES(123,122,124,120,121)",
            "INSERT INTO t1(a,d,b,e,c) VALUES(127,128,129,126,125)",
            "INSERT INTO t1(e,c,a,d,b) VALUES(132,134,131,133,130)",
            "INSERT INTO t1(a,d,b,e,c) VALUES(138,136,139,135,137)",
            "INSERT INTO t1(e,c,d,a,b) VALUES(144,141,140,142,143)",
            "INSERT INTO t1(b,a,e,d,c) VALUES(145,149,146,148,147)",
            "INSERT INTO t1(b,c,a,d,e) VALUES(151,150,153,154,152)",
            "INSERT INTO t1(c,e,a,d,b) VALUES(155,157,159,156,158)",
            "INSERT INTO t1(c,b,a,d,e) VALUES(161,160,163,164,162)",
            "INSERT INTO t1(b,d,a,e,c) VALUES(167,169,168,165,166)",
            "INSERT INTO t1(d,b,c,e,a) VALUES(171,170,172,173,174)",
            "INSERT INTO t1(e,c,a,d,b) VALUES(177,176,179,178,175)",
            "INSERT INTO t1(b,e,a,d,c) VALUES(181,180,182,183,184)",
            "INSERT INTO t1(c,a,b,e,d) VALUES(187,188,186,189,185)",
            "INSERT INTO t1(d,b,c,e,a) VALUES(190,194,193,192,191)",
            "INSERT INTO t1(a,e,b,d,c) VALUES(199,197,198,196,195)",
            "INSERT INTO t1(b,c,d,a,e) VALUES(200,202,203,201,204)",
            "INSERT INTO t1(c,e,a,b,d) VALUES(208,209,205,206,207)",
            "INSERT INTO t1(c,e,a,d,b) VALUES(214,210,213,212,211)",
            "INSERT INTO t1(b,c,a,d,e) VALUES(218,215,216,217,219)",
            "INSERT INTO t1(b,e,d,a,c) VALUES(223,221,222,220,224)",
            "INSERT INTO t1(d,e,b,a,c) VALUES(226,227,228,229,225)",
            "INSERT INTO t1(a,c,b,e,d) VALUES(234,231,232,230,233)",
            "INSERT INTO t1(e,b,a,c,d) VALUES(237,236,239,235,238)",
            "INSERT INTO t1(e,c,b,a,d) VALUES(242,244,240,243,241)",
            "INSERT INTO t1(e,d,c,b,a) VALUES(246,248,247,249,245)",
        ];

        private static async Task<SltRunner> CreateT1RunnerAsync(
            long? spillThreshold = null,
            bool persistentSession = false,
            string? sessionRoot = null)
        {
            var runner = new SltRunner();

            if (spillThreshold.HasValue)
                runner.TempTableSpillThresholdRows = spillThreshold.Value;

            if (persistentSession)
            {
                runner.IsPersistentSession = true;
                runner.SessionId = "parity-test";
                runner.SessionRoot = sessionRoot ?? Path.GetTempPath();
            }

            await RunStatement(runner, "CREATE TABLE t1(a INTEGER, b INTEGER, c INTEGER, d INTEGER, e INTEGER)");
            foreach (var insert in T1Inserts)
                await RunStatement(runner, insert);

            return runner;
        }

        private static Task RunStatement(SltRunner runner, string sql) =>
            runner.RunTestAsync(new SltRecord
            {
                Type = SltRecordType.Statement,
                Sql = sql,
                ExpectSuccess = true,
                LineNumber = 0
            });

        private static async Task<SltRunner> CreateT1RunnerSelect2Async()
        {
            var runner = new SltRunner();
            await RunStatement(runner, "CREATE TABLE t1(a INTEGER, b INTEGER, c INTEGER, d INTEGER, e INTEGER)");
            foreach (var insert in T1InsertsSelect2)
                await RunStatement(runner, insert);
            return runner;
        }

        // select2.test line 96 — same query as select1 but with NULLs in various columns
        // NULLs change avg(c): only 29 of 30 rows have non-null c; c=NULL excluded from AVG
        // Expected avg(c) = 4996/29 = 172.276, so c=172 (row 15) goes to ELSE (b*10=1700)
        [SltFact]
        public async Task Select2_Line96_CaseWithScalarSubqueryAndNulls()
        {
            using var runner = await CreateT1RunnerSelect2Async();
            var record = new SltRecord
            {
                Type = SltRecordType.Query,
                Sql = "SELECT CASE WHEN c>(SELECT avg(c) FROM t1) THEN a*2 ELSE b*10 END FROM t1",
                SortMode = SltSortMode.RowSort,
                ExpectedResult = "30 values hashing to efdbaa4d180e7867bec1c4d897bd25b9",
                LineNumber = 96
            };
            await runner.RunTestAsync(record);
        }

        // select1.test line 94
        [SltFact]
        public async Task Line94_CaseWithScalarSubquery_CountAndHash()
        {
            using var runner = await CreateT1RunnerAsync();
            var record = new SltRecord
            {
                Type = SltRecordType.Query,
                Sql = """
                    SELECT CASE WHEN c>(SELECT avg(c) FROM t1) THEN a*2 ELSE b*10 END
                      FROM t1
                     ORDER BY 1
                    """,
                SortMode = SltSortMode.NoSort,
                ExpectedResult = "30 values hashing to 3c13dee48d9356ae19af2515e05e6b54",
                LineNumber = 94
            };
            await runner.RunTestAsync(record);
        }

        // select1.test line 2270
        [SltFact]
        public async Task Line2270_NotBetweenAndBetweenWithArithmetic()
        {
            using var runner = await CreateT1RunnerAsync();
            var record = new SltRecord
            {
                Type = SltRecordType.Query,
                Sql = """
                    SELECT (a+b+c+d+e)/5,
                           a+b*2+c*3
                      FROM t1
                     WHERE b>c
                        OR d NOT BETWEEN 110 AND 150
                        OR c BETWEEN b-2 AND d+2
                     ORDER BY 2,1
                    """,
                SortMode = SltSortMode.NoSort,
                ExpectedResult = "58 values hashing to 689847f49b3867b87e7c46dfeb0da7c1",
                LineNumber = 2270
            };
            await runner.RunTestAsync(record);
        }

        // select1.test line 3221 (the 90-value triple-column query)
        [SltFact]
        public async Task Line3221_TripleColumnCaseAndArithmetic()
        {
            using var runner = await CreateT1RunnerAsync();
            var record = new SltRecord
            {
                Type = SltRecordType.Query,
                Sql = """
                    SELECT CASE WHEN c>(SELECT avg(c) FROM t1) THEN a*2 ELSE b*10 END,
                           CASE WHEN a<b-3 THEN 111 WHEN a<=b THEN 222
                            WHEN a<b+3 THEN 333 ELSE 444 END,
                           a+b*2+c*3+d*4
                      FROM t1
                     ORDER BY 2,3,1
                    """,
                SortMode = SltSortMode.NoSort,
                ExpectedResult = "90 values hashing to 95dc79fe00aff04819a8779833b65771",
                LineNumber = 3221
            };
            await runner.RunTestAsync(record);
        }

        // select3.test line 2273 — correlated count(*) subquery + simple CASE with NULLs
        [SltFact]
        public async Task Select3_Line2273_CorrelatedCountAndSimpleCaseWithNulls()
        {
            using var runner = await CreateT1RunnerSelect2Async();
            var record = new SltRecord
            {
                Type = SltRecordType.Query,
                Sql = """
                    SELECT (SELECT count(*) FROM t1 AS x WHERE x.b<t1.b),
                           CASE a+1 WHEN b THEN 111 WHEN c THEN 222
                            WHEN d THEN 333  WHEN e THEN 444 ELSE 555 END,
                           a+b*2+c*3+d*4+e*5,
                           a-b,
                           abs(a),
                           d
                      FROM t1
                     WHERE (c<=d-2 OR c>=d+2)
                        OR c BETWEEN b-2 AND d+2
                    """,
                SortMode = SltSortMode.RowSort,
                ExpectedResult = "114 values hashing to 0446a9accb80cccec6bc4d954353b3d5",
                LineNumber = 2273
            };
            await runner.RunTestAsync(record);
        }

        // Persistent vs transient parity: same hash in both modes when spilling is forced.
        // Catches divergence in spill file cleanup (IsPersistentSession=false cleans up eagerly).
        [SltFact]
        public async Task SpillParity_TransientVsPersistentGivesSameHash()
        {
            var record = new SltRecord
            {
                Type = SltRecordType.Query,
                Sql = """
                    SELECT CASE WHEN c>(SELECT avg(c) FROM t1) THEN a*2 ELSE b*10 END
                      FROM t1
                     ORDER BY 1
                    """,
                SortMode = SltSortMode.NoSort,
                ExpectedResult = "30 values hashing to 3c13dee48d9356ae19af2515e05e6b54",
                LineNumber = 94
            };

            // Transient mode with forced spilling (threshold below 30 rows)
            using var transientRunner = await CreateT1RunnerAsync(spillThreshold: 5);
            await transientRunner.RunTestAsync(record);

            // Persistent mode with forced spilling — spill files go to a real session dir
            string sessionDir = Path.Combine(Path.GetTempPath(), "ETL_SLT_Parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionDir);
            try
            {
                using var persistentRunner = await CreateT1RunnerAsync(
                    spillThreshold: 5, persistentSession: true, sessionRoot: sessionDir);
                await persistentRunner.RunTestAsync(record);
            }
            finally
            {
                if (Directory.Exists(sessionDir))
                    Directory.Delete(sessionDir, true);
            }
        }
    }
}
