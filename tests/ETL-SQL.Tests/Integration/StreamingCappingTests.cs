using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Integration
{
    public class StreamingCappingTests
    {
        [Fact]
        public async Task SelectShouldBeCappedInteractive()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();
            
            // Set cap to 100
            ev.MaxLastResultRows = 100;
            ev.RedirectOutput = false; // "Interactive" mode

            // Create a source with 1000 rows
            await ev.Evaluate(Parse("CREATE TABLE #LotsOfRows (ID INT);"));
            await ev.Evaluate(Parse("DECLARE @i INT = 0; WHILE @i < 1000 BEGIN INSERT INTO #LotsOfRows VALUES (@i); SET @i = @i + 1; END"));

            // Execute SELECT
            await ev.Evaluate(Parse("SELECT * FROM #LotsOfRows;"));

            Assert.NotNull(ev.LastResult);
            Assert.Equal(100, ev.LastResult.Rows.Count);
            Assert.True(ev.LastResult.IsCapped);
            
            // For interactive mode, we STOPPED reading once we hit the cap.
            // So TotalRowsMatched (and @@ROWCOUNT delta) should be 100.
            Assert.Equal(100, ev.LastResult.TotalRowsMatched);
        }

        [Fact]
        public async Task SelectShouldNotBeCappedWhenRedirected()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();
            
            ev.MaxLastResultRows = 100;
            ev.RedirectOutput = true; // "Redirected" mode

            await ev.Evaluate(Parse("CREATE TABLE #LotsOfRows (ID INT);"));
            await ev.Evaluate(Parse("DECLARE @i INT = 0; WHILE @i < 1000 BEGIN INSERT INTO #LotsOfRows VALUES (@i); SET @i = @i + 1; END"));

            await ev.Evaluate(Parse("SELECT * FROM #LotsOfRows;"));

            Assert.NotNull(ev.LastResult);
            
            // In redirected mode:
            // 1. result.Rows.Count should still be capped at 100 for engine memory safety.
            Assert.Equal(100, ev.LastResult.Rows.Count);
            Assert.True(ev.LastResult.IsCapped);
            
            // 2. But we should NOT have stopped consumption. 
            // So TotalRowsMatched should be 1000.
            Assert.Equal(1000, ev.LastResult.TotalRowsMatched);
        }

        [Fact]
        public async Task SetOverrideShouldWork()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();
            
            ev.MaxLastResultRows = 100;
            ev.RedirectOutput = false;

            await ev.Evaluate(Parse("CREATE TABLE #Limited (ID INT);"));
            await ev.Evaluate(Parse("DECLARE @i INT = 0; WHILE @i < 200 BEGIN INSERT INTO #Limited VALUES (@i); SET @i = @i + 1; END"));

            // Override via SET
            await ev.Evaluate(Parse("SET MAX_LAST_RESULT_ROWS = 50; SELECT * FROM #Limited;"));

            Assert.NotNull(ev.LastResult);
            Assert.Equal(50, ev.LastResult.Rows.Count);
            Assert.Equal(50, ev.LastResult.TotalRowsMatched);
            
            // Check session state persisted correctly
            Assert.Equal(50, ev.MaxLastResultRows);
        }

        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }
    }
}
