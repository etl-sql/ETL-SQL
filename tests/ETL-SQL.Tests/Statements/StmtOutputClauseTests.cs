using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;

using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;

namespace ETL_SQL.Tests.Statements
{
    public class OutputClauseTests
    {
        private readonly IServiceProvider _serviceProvider;

        public OutputClauseTests()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
        }

        [Fact]
        public async Task Insert_Output_Into_Temp_Table()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #Source (Id INT, Name VARCHAR);
                INSERT INTO #Source VALUES (1, 'A'), (2, 'B');

                CREATE TABLE #Target (Id INT, Name VARCHAR);
                CREATE TABLE #Audit (NewId INT, NewName VARCHAR);

                INSERT INTO #Target (Id, Name)
                OUTPUT INSERTED.Id AS NewId, INSERTED.Name AS NewName INTO #Audit
                SELECT Id, Name FROM #Source;

                SELECT * FROM #Audit;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            
            var auditTable = evaluator.LastResult;
            Assert.NotNull(auditTable);
            Assert.Equal(2, auditTable.Rows.Count);
            Assert.Equal(1, Convert.ToDecimal(auditTable.Rows[0]["NewId"]));
            Assert.Equal("A", auditTable.Rows[0]["NewName"]);
        }

        [Fact]
        public async Task Update_Output_Deleted_And_Inserted()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #Data (Id INT, Val VARCHAR);
                INSERT INTO #Data VALUES (1, 'Old');

                CREATE TABLE #History (OldVal VARCHAR, NewVal VARCHAR);

                UPDATE #Data 
                SET Val = 'New'
                OUTPUT DELETED.Val AS OldVal, INSERTED.Val AS NewVal INTO #History
                WHERE Id = 1;

                SELECT * FROM #History;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());

            var historyTable = evaluator.LastResult;
            Assert.NotNull(historyTable);
            Assert.Single(historyTable.Rows);
            Assert.Equal("Old", historyTable.Rows[0]["OldVal"]);
            Assert.Equal("New", historyTable.Rows[0]["NewVal"]);
        }

        [Fact]
        public async Task Delete_Output_Deleted()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #Data (Id INT, Name VARCHAR);
                INSERT INTO #Data VALUES (1, 'John'), (2, 'Jane');

                DELETE FROM #Data
                OUTPUT DELETED.Id AS Id, DELETED.Name AS Name
                WHERE Id = 1;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var output = evaluator.LastResult;

            Assert.NotNull(output);
            Assert.Single(output.Rows);
            Assert.Equal(1, Convert.ToDecimal(output.Rows[0]["Id"]));
            Assert.Equal("John", output.Rows[0]["Name"]);
        }
    }
}
