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
using System.IO;

namespace ETL_SQL.Tests.Statements
{
    public class TruncateTests
    {
        private readonly IServiceProvider _serviceProvider;

        public TruncateTests()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
        }

        [Fact]
        public async Task Truncate_InMemory_Table()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #Temp (Id INT, Name VARCHAR);
                INSERT INTO #Temp VALUES (1, 'Test'), (2, 'Other');
                SELECT COUNT(*) AS Cnt FROM #Temp;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var res1 = evaluator.LastResult;
            Assert.NotNull(res1.Rows[0]["Cnt"]);
            Assert.Equal(2, Convert.ToInt32(res1.Rows[0]["Cnt"]));

            await evaluator.Evaluate(new Lexer("TRUNCATE TABLE #Temp; SELECT COUNT(*) AS Cnt FROM #Temp;").TokenizeToScript());
            var res2 = evaluator.LastResult;
            Assert.NotNull(res2.Rows[0]["Cnt"]);
            Assert.Equal(0, Convert.ToInt32(res2.Rows[0]["Cnt"]));
        }

        [Fact]
        public async Task Truncate_Json_File()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var jsonFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            try
            {
                var script = $@"
                    CREATE CONNECTION conn AS JSON('{jsonFile.Replace("\\", "\\\\")}');
                    CREATE TABLE #Src (Id INT, Val VARCHAR);
                    INSERT INTO #Src VALUES (1, 'A'), (2, 'B');
                    INSERT INTO conn.Data SELECT * FROM #Src;
                    
                    SELECT COUNT(*) AS Cnt FROM conn.Data;
                ";

                await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
                Assert.NotNull(evaluator.LastResult.Rows[0]["Cnt"]);
                Assert.Equal(2, Convert.ToInt32(evaluator.LastResult.Rows[0]["Cnt"]));

                await evaluator.Evaluate(new Lexer("TRUNCATE TABLE conn.Data; SELECT COUNT(*) AS Cnt FROM conn.Data;").TokenizeToScript());
                Assert.NotNull(evaluator.LastResult.Rows[0]["Cnt"]);
                Assert.Equal(0, Convert.ToInt32(evaluator.LastResult.Rows[0]["Cnt"]));
            }
            finally
            {
                if (File.Exists(jsonFile)) File.Delete(jsonFile);
            }
        }

        [Fact]
        public async Task Truncate_FlatFile()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var csvFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".csv");
            try
            {
                var script = $@"
                    CREATE CONNECTION conn AS FLATFILE('{csvFile.Replace("\\", "\\\\")}', HEADER='ON');
                    CREATE TABLE #Src (Id INT, Val VARCHAR);
                    INSERT INTO #Src VALUES (1, 'X'), (2, 'Y'), (3, 'Z');
                    INSERT INTO conn.Data SELECT * FROM #Src;
                    
                    SELECT COUNT(*) AS Cnt FROM conn.Data;
                ";

                await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
                Assert.NotNull(evaluator.LastResult.Rows[0]["Cnt"]);
                Assert.Equal(3, Convert.ToInt32(evaluator.LastResult.Rows[0]["Cnt"]));

                await evaluator.Evaluate(new Lexer("TRUNCATE TABLE conn.Data; SELECT COUNT(*) AS Cnt FROM conn.Data;").TokenizeToScript());
                Assert.NotNull(evaluator.LastResult.Rows[0]["Cnt"]);
                Assert.Equal(0, Convert.ToInt32(evaluator.LastResult.Rows[0]["Cnt"]));
            }
            finally
            {
                if (File.Exists(csvFile)) File.Delete(csvFile);
            }
        }
    }
}
