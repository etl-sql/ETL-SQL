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
    }
}
