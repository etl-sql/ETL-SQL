using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;

using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace ETL_SQL.Tests
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
