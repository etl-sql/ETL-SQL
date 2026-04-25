using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.Tests.Integration
{
    public class DatabaseTests
    {

        private static async Task Execute(Evaluator eval, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();
            await eval.Evaluate(script);
        }

        private static async Task<object?> EvalFunc(Evaluator eval, string sql)
        {
            await Execute(eval, sql);
            var row = eval.LastResult?.Rows.FirstOrDefault();
            return row?["UserName"] ?? row?["ProductName"];
        }

        [Fact]
        public async Task TestMockDatabase()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            // 1. Create MOCKDB connection
            await Execute(eval, "CREATE CONNECTION MyMock ON MOCKDB('dummy_string');");
            
            // 2. Query mock table
            var sql = "SELECT UserName FROM MyMock.Users WHERE UserID = 1;";
            var res = await EvalFunc(eval, sql);
            Assert.Equal("User_1", res?.ToString());
            
            // 3. Query another table
            var res2 = await EvalFunc(eval, "SELECT ProductName FROM MyMock.Products WHERE ProductID = 101;");
            Assert.Equal("Product_101", res2?.ToString());
        }
    }
}
