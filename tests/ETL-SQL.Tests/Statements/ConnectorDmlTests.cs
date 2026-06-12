using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class ConnectorDmlTests
    {
        [Fact]
        public async Task TestInsertIntoRemoteFromLocal()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["remote"] = mockDb;

            string script = @"
                CREATE TABLE #Local (Val INT);
                INSERT INTO #Local VALUES (100);
                INSERT INTO remote.TargetTable SELECT * FROM #Local;
            ";

            await evaluator.Evaluate(TestHelpers.Parse(script));

            // Verify that an INSERT statement was sent to the mock DB
            Assert.Contains(mockDb.ExecutedSql, s => s.Contains("INSERT INTO [TargetTable]") || s.Contains("INSERT INTO TargetTable"));
        }

        [Fact]
        public async Task TestUpdateRemote()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["remote"] = mockDb;

            string script = "UPDATE remote.TargetTable SET Val = 200 WHERE ID = 1;";

            await evaluator.Evaluate(TestHelpers.Parse(script));

            Assert.Contains(mockDb.ExecutedSql, s => (s.Contains("UPDATE [TargetTable]") || s.Contains("UPDATE TargetTable")) && s.Contains("Val = @") && s.Contains("ID = @"));
        }

        [Fact]
        public async Task TestDeleteRemote()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["remote"] = mockDb;

            string script = "DELETE FROM remote.TargetTable WHERE ID = 1;";

            await evaluator.Evaluate(TestHelpers.Parse(script));

            Assert.Contains(mockDb.ExecutedSql, s => (s.Contains("DELETE FROM [TargetTable]") || s.Contains("DELETE FROM TargetTable")) && s.Contains("ID = @"));
        }

        [Fact]
        public async Task TestDropTableRemote()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["remote"] = mockDb;

            string script = "DROP TABLE remote.TargetTable;";

            await evaluator.Evaluate(TestHelpers.Parse(script));

            Assert.Contains(mockDb.ExecutedSql, s => s.Contains("DROP TABLE [TargetTable]") || s.Contains("DROP TABLE TargetTable"));
        }

        [Fact]
        public async Task TestInsertValuesParameterRemapCollision()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["remote"] = mockDb;

            string script = @"
                DECLARE @id INT;
                SET @id = 1;
                INSERT INTO remote.TargetTable (Col0, Col1, Col2) VALUES (100, 10 + @id, @id + 2);
            ";

            await evaluator.Evaluate(TestHelpers.Parse(script));

            Assert.Single(mockDb.ExecutedSql);
            var sql = mockDb.ExecutedSql[0];
            Assert.Contains("VALUES (@p0, (@p1 + @p2), (@p3 + @p4))", sql);
        }
    }
}
