using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class DynamicSqlTests
    {

        [Fact]
        public async Task TestBasicExec()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = "DECLARE @sql VARCHAR(100) = 'PRINT ''Hello from Dynamic SQL'''; EXEC (@sql);";
            await ev.Evaluate(Parse(script));
        }

        [Fact]
        public async Task TestExecWithVariables()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @count INT = 0;
                DECLARE @sql VARCHAR(100) = 'SET @count = 100;';
                EXEC (@sql);
                PRINT @count;
            ";
            await ev.Evaluate(Parse(script));
            Assert.Equal(100, Convert.ToInt32(ev.Variables["@count"]));
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }


    }
}
