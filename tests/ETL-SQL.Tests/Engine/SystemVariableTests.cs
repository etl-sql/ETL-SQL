using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


namespace ETL_SQL.Tests.Engine
{
    public class SystemVariableTests
    {
        [Fact]
        public async Task TestErrorVariable_SuccessReset()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var eval = provider.GetRequiredService<Evaluator>();

            // 1. Initial state should be 0
            var val1 = eval.GetVariable("@@ERROR");
            Assert.Equal(0, Convert.ToInt32(val1));

            // 2. Run a statement that fails
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await TestHelpers.Execute(eval, "SELECT * FROM NonExistentTable;")
            );

            // 3. @@ERROR should now be non-zero inside CATCH
            await TestHelpers.Execute(eval, @"
DECLARE @CapturedError = 0;
BEGIN TRY
    SELECT 1 / 0;
END TRY
BEGIN CATCH
    SET @CapturedError = @@ERROR;
END CATCH");

            var err = eval.GetVariable("@CapturedError");
            Assert.True(Convert.ToInt32(err) != 0, "@@ERROR should have been captured as non-zero inside CATCH.");


            // 4. Run a successful statement
            await TestHelpers.Execute(eval, "PRINT 'Success';");

            // 5. @@ERROR should be reset to 0
            var val2 = eval.GetVariable("@@ERROR");
            Assert.Equal(0, Convert.ToInt32(val2));
        }


        [Fact]
        public async Task TestRowCountVariable()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var eval = provider.GetRequiredService<Evaluator>();

            await TestHelpers.Execute(eval, @"
CREATE TABLE #T (id INT);
INSERT INTO #T VALUES (1), (2), (3);
");
            var count = eval.GetVariable("@@ROWCOUNT");
            Assert.Equal(3, Convert.ToInt32(count));
        }

    }
}
