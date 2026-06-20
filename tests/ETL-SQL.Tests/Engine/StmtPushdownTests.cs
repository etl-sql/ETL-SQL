using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class PushdownTests
    {
        private static Script Parse(string source)
        {
            var tokens = new Lexer(source).Tokenize();
            return new Parser(tokens, source).Parse();
        }

        [Fact]
        public async Task TestStandalonePushdown()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mock = new MockDatabaseSource();
            ev.Connections["MyDb"] = mock;

            string script = @"
                EXECUTE MyDb BEGIN SELECT UserID, UserName FROM Users END;
            ";
            await ev.Evaluate(Parse(script));

            // Verification: Verify the SQL actually hit the mock
            Assert.Single(mock.ExecutedSql);
            Assert.Contains("SELECT UserID, UserName FROM Users", mock.ExecutedSql[0]);
        }

        [Fact]
        public async Task TestPushdownIntoTempTable()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE CONNECTION MyDb AS MOCKDB();
                EXECUTE MyDb INTO #RemoteUsers BEGIN SELECT UserID, UserName FROM Users END;
            ";
            await ev.Evaluate(Parse(script));

            // Verify table exists and has data
            Assert.True(ev.Connections.ContainsKey("#RemoteUsers"));
            var ds = ev.Connections["#RemoteUsers"];
            var columns = (await ds.GetColumnsAsync()).ToList();
            Assert.Contains("UserID", columns);
            Assert.Contains("UserName", columns);
        }

        [Fact]
        public async Task TestInsertIntoExecutePushdown()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            var mock = new MockDatabaseSource();
            ev.Connections["MyDb"] = mock;

            var dt = new DataTable();
            dt.SetColumns(new[] { "UserID", "UserName" });

            var row1 = dt.NewRow();
            row1["UserID"] = 1;
            row1["UserName"] = "Alice";
            dt.Rows.Add(row1);

            var row2 = dt.NewRow();
            row2["UserID"] = 2;
            row2["UserName"] = "Bob";
            dt.Rows.Add(row2);
            mock.SeededResults.Add(dt);

            await ev.Evaluate(TestHelpers.Parse(@"
                CREATE TABLE #TargetUsers (UID INT, UName ANY);
                INSERT INTO #TargetUsers (UID, UName)
                EXECUTE MyDb BEGIN SELECT UserID, UserName FROM Users END;
            "));

            // Verify #TargetUsers has data
            var ds = ev.Connections["#TargetUsers"];
            var batches = ds.ReadBatches();
            int rowCount = 0;
            await foreach (var batch in batches)
            {
                rowCount += batch.Rows.Count;
            }
            Assert.Equal(2, rowCount);
        }

        [Fact]
        public async Task TestComplexNestedPushdown()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            // User's provided complex block
            string script = @"
                CREATE CONNECTION m AS MOCKDB();
                EXECUTE m INTO #temp
                BEGIN
                  IF 1=0
                   BEGIN
                      SELECT 1
                    END
                   ELSE
                   BEGIN
                     SELECT 2
                    END

                    DECLARE @id = 1
                    WHILE (@id < 3)
                BEGIN
                   SELECT 
                @id; SET @id = @id + 1;
                END
                END
            ";

            var parsed = Parse(script);
            var pushdownStmt = parsed.Statements.OfType<ExecutePushdownStatement>().FirstOrDefault();

            Assert.NotNull(pushdownStmt);

            // Verify the captured SQL block contains the nested BEGIN/ENDs
            string capturedSql = pushdownStmt.SqlText.Trim();
            Assert.Contains("IF 1=0", capturedSql);
            Assert.Contains("WHILE (@id < 3)", capturedSql);
            Assert.Contains("BEGIN", capturedSql); // Nested BEGINs
            Assert.Contains("END", capturedSql);   // Nested ENDs

            // Execute it (mock will just return dummy results but verify no crash)
            await ev.Evaluate(parsed);
            Assert.True(ev.Connections.ContainsKey("#temp"));
        }

        [Fact]
        public async Task TestSelectInto_WithAggregation_PushedDown()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mock = new MockDatabaseSource();
            ev.Connections["MyDb"] = mock;

            // Seed mock database with aggregated results
            var seedDt = new DataTable();
            seedDt.SetColumns(new[] { "Region", "TotalRevenue" });

            var row1 = seedDt.NewRow();
            row1["Region"] = "North";
            row1["TotalRevenue"] = 5000m;
            seedDt.Rows.Add(row1);

            var row2 = seedDt.NewRow();
            row2["Region"] = "South";
            row2["TotalRevenue"] = 7500m;
            seedDt.Rows.Add(row2);

            mock.SeededResults.Add(seedDt);

            string script = @"
                CREATE TABLE #Target (Region STRING, TotalRevenue DECIMAL);
                SELECT Region, SUM(Revenue) AS TotalRevenue
                INTO #Target
                FROM MyDb.Sales
                GROUP BY Region;
            ";

            await ev.Evaluate(Parse(script));

            // Verify aggregated query was pushed down to the mock db
            Assert.Contains(mock.ExecutedSql, sql => sql.Contains("GROUP BY") && sql.Contains("SUM("));

            // Verify Target table has been populated with the aggregated rows
            Assert.True(ev.Connections.ContainsKey("#Target"));
            var targetDs = ev.Connections["#Target"];
            var batches = targetDs.ReadBatches();
            var resultRows = new List<Row>();
            await foreach (var batch in batches)
            {
                resultRows.AddRange(batch.Rows);
            }

            Assert.Equal(2, resultRows.Count);
            var north = resultRows.FirstOrDefault(r => r["Region"]?.ToString() == "North");
            Assert.NotNull(north);
            Assert.Equal(5000m, Convert.ToDecimal(north["TotalRevenue"]));
        }

        [Fact]
        public async Task TestSelectInto_WithLocalFunctions_NotPushedDown()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mock = new MockDatabaseSource();
            ev.Connections["MyDb"] = mock;

            // Even if we seed the mock, it should not execute a GROUP BY query on MyDb
            string script = @"
                CREATE TABLE #TargetLocal (Region STRING, TotalRevenue DECIMAL);
                SELECT Region, SUM(Revenue) AS TotalRevenue, GET_JOB_STATE('test') AS StateVal
                INTO #TargetLocal
                FROM MyDb.Sales
                GROUP BY Region;
            ";

            await ev.Evaluate(Parse(script));

            // Verify the executed SQL on MyDb did NOT contain GROUP BY
            Assert.DoesNotContain(mock.ExecutedSql, sql => sql.Contains("GROUP BY"));
        }

        [Fact]
        public async Task TestSemiJoinPushdown_OptimizesCrossConnectionJoin()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mock = new MockDatabaseSource();
            ev.Connections["MyDb"] = mock;

            // Seed mock database with query results
            var seedDt = new DataTable();
            seedDt.SetColumns(new[] { "Id", "Name" });

            var row1 = seedDt.NewRow();
            row1["Id"] = 101;
            row1["Name"] = "Alice";
            seedDt.Rows.Add(row1);

            var row2 = seedDt.NewRow();
            row2["Id"] = 102;
            row2["Name"] = "Bob";
            seedDt.Rows.Add(row2);

            mock.SeededResults.Add(seedDt);

            // Create local temp table and populate it
            string initScript = @"
                CREATE TABLE #Local (ID INT);
                INSERT INTO #Local (ID) VALUES (101), (102), (103);
            ";
            await ev.Evaluate(Parse(initScript));

            // Run optimized join query
            string selectScript = @"
                SELECT #Local.ID, MyDb.Customer.Name 
                FROM #Local 
                JOIN MyDb.Customer ON #Local.ID = MyDb.Customer.Id;
            ";

            var parsed = Parse(selectScript);
            var selectStmt = parsed.Statements.OfType<SelectStatement>().First();

            // 1. Manually optimize
            var optimized = await ETL_SQL.Core.Planning.SemiJoinPushdownOptimizer.OptimizeAsync(selectStmt, ev);

            // Assert that the join target was indeed rewritten with a subquery
            Assert.Single(optimized.Joins);
            var joinTable = optimized.Joins[0].Table;
            Assert.NotNull(joinTable.Subquery);

            // 2. Check if pushdown is possible on the rewritten subquery
            var pushdownEngine = new ETL_SQL.Engine.Services.PushdownEngine(ev.Logger);
            bool isPushdownPossible = pushdownEngine.IsPushdownPossible((SelectStatement)joinTable.Subquery, ev, out var connName);
            Assert.True(isPushdownPossible, "Rewritten subquery should support SQL pushdown to MyDb connection");
            Assert.Equal("MyDb", connName);

            // 3. Evaluate the script
            await ev.Evaluate(parsed);

            // Verify a pushdown query with IN clause was sent to MyDb
            Assert.NotEmpty(mock.ExecutedSql);
            // Since literals are now parameterized, it should be like "SELECT * FROM [Customer] WHERE Id IN (@p0, @p1, @p2)"
            Assert.Contains(mock.ExecutedSql, sql => sql.Contains("IN (@p0, @p1, @p2)"));

            // Verify that the query returned 2 rows matching the seeded results (101 -> Alice, 102 -> Bob)
            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.Equal(2, result.Rows.Count);

            // Run EXPLAIN query and verify the output contains the SEMI-JOIN metadata
            string explainScript = @"
                EXPLAIN SELECT #Local.ID, MyDb.Customer.Name 
                FROM #Local 
                JOIN MyDb.Customer ON #Local.ID = MyDb.Customer.Id;
            ";
            await ev.Evaluate(Parse(explainScript));
            var planTable = ev.LastResult;
            Assert.NotNull(planTable);

            bool foundSemiJoinExplain = false;
            foreach (var r in planTable.Rows)
            {
                var details = r["Details"]?.ToString() ?? "";
                if (details.Contains("SEMI-JOIN PUSHDOWN ON #Local.ID (3 keys)"))
                {
                    foundSemiJoinExplain = true;
                    break;
                }
            }
            Assert.True(foundSemiJoinExplain, "Explain plan should show the semi-join pushdown details");
        }
    }
}

