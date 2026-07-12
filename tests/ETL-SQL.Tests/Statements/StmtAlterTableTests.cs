using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class AlterTableTests
    {
        private readonly IServiceProvider _serviceProvider;

        public AlterTableTests()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
        }

        [Fact]
        public async Task AlterTable_AddColumn()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #AddCol (Id INT);
                INSERT INTO #AddCol (Id) VALUES (1);
                ALTER TABLE #AddCol ADD Name VARCHAR(50);
                INSERT INTO #AddCol (Id, Name) VALUES (2, 'Bob');
                SELECT * FROM #AddCol ORDER BY Id;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;

            Assert.Equal(2, result.Rows.Count);
            Assert.Contains("Name", result.Rows[0].Columns.Keys);
            Assert.Null(result.Rows[0]["Name"]);
            Assert.Equal("Bob", result.Rows[1]["Name"]);
        }

        [Fact]
        public async Task AlterTable_DropColumn()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #DropCol (Id INT, Secret VARCHAR(10));
                INSERT INTO #DropCol (Id, Secret) VALUES (1, 'Keep');
                ALTER TABLE #DropCol DROP COLUMN Secret;
                SELECT * FROM #DropCol;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;

            Assert.Single(result.Rows);
            Assert.DoesNotContain("Secret", result.Rows[0].Columns.Keys);
            Assert.Equal(1, Convert.ToInt32(result.Rows[0]["Id"]));
        }

        [Fact]
        public async Task AlterTable_DropMiddleColumn_PreservesFollowingColumnValues()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            evaluator.UseColumnarTempTables = false;
            var script = @"
                CREATE TABLE #DropMiddle (Id INT, Name VARCHAR(10), Score INT);
                INSERT INTO #DropMiddle (Id, Name, Score) VALUES (1, 'Alice', 90), (2, 'Bob', 80);
                ALTER TABLE #DropMiddle DROP COLUMN Name;
                SELECT Id, Score FROM #DropMiddle ORDER BY Id;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;

            Assert.Equal(2, result.Rows.Count);
            Assert.Equal(1, Convert.ToInt32(result.Rows[0]["Id"]));
            Assert.Equal(90, Convert.ToInt32(result.Rows[0]["Score"]));
            Assert.Equal(2, Convert.ToInt32(result.Rows[1]["Id"]));
            Assert.Equal(80, Convert.ToInt32(result.Rows[1]["Score"]));
        }

        [Fact]
        public async Task AlterTable_RenameColumn()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #RenameCol (OldName INT);
                INSERT INTO #RenameCol (OldName) VALUES (42);
                ALTER TABLE #RenameCol RENAME COLUMN OldName TO NewName;
                SELECT * FROM #RenameCol;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;

            Assert.Single(result.Rows);
            Assert.DoesNotContain("OldName", result.Rows[0].Columns.Keys);
            Assert.Contains("NewName", result.Rows[0].Columns.Keys);
            Assert.Equal(42, Convert.ToInt32(result.Rows[0]["NewName"]));
        }
    }
}
