using Xunit;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Core.Parser;
using ETL_SQL.Services;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace ETL_SQL.Tests
{
    public class SecurityOverrideStatementTests
    {
        [Fact]
        public async Task TestSetSecurityOverrideParsesAndEvaluates()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();

            // 1. Test ALLOW_FILE_TYPE_ACCESS
            Assert.False(evaluator.AllowUnknownFileTypes);
            await Execute(evaluator, "SET ALLOW_FILE_TYPE_ACCESS ON;");
            Assert.True(evaluator.AllowUnknownFileTypes);
            await Execute(evaluator, "SET ALLOW_FILE_TYPE_ACCESS OFF;");
            Assert.False(evaluator.AllowUnknownFileTypes);

            // 2. Test ALLOW_GREATER_THAN_n_FILE
            Assert.False(evaluator.AllowLargeFileOperationCount);
            // n=100 is default, but we should match it
            await Execute(evaluator, "SET ALLOW_GREATER_THAN_100_FILE ON;");
            Assert.True(evaluator.AllowLargeFileOperationCount);
            await Execute(evaluator, "SET ALLOW_GREATER_THAN_100_FILE OFF;");
            Assert.False(evaluator.AllowLargeFileOperationCount);

            // 3. Test ALLOW_RECURSIVE_GREATER_THAN_n_LAYERS
            Assert.False(evaluator.AllowDeepRecursion);
            await Execute(evaluator, "SET ALLOW_RECURSIVE_GREATER_THAN_5_LAYERS ON;");
            Assert.True(evaluator.AllowDeepRecursion);
            await Execute(evaluator, "SET ALLOW_RECURSIVE_GREATER_THAN_5_LAYERS OFF;");
            Assert.False(evaluator.AllowDeepRecursion);
        }

        private async Task Execute(Evaluator evaluator, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();
            await evaluator.Evaluate(script);
        }
    }
}
