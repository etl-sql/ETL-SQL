using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Engine
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

        [Fact]
        public async Task InteractiveOutputShouldOnlyRenderCappedRows()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();
            var sink = new CaptureOutputSink();

            var previousJsonMode = ResultFormatter.IsJsonMode;
            var previousSuppressOutput = ResultFormatter.SuppressOutput;
            var previousSink = ResultFormatter.OutputSink;

            try
            {
                ResultFormatter.IsJsonMode = true;
                ResultFormatter.SuppressOutput = false;
                ResultFormatter.OutputSink = sink;

                ev.MaxLastResultRows = 10;
                ev.RedirectOutput = false;

                await ev.Evaluate(Parse("CREATE TABLE #LotsOfRows (ID INT);"));
                await ev.Evaluate(Parse("DECLARE @i INT = 0; WHILE @i < 50 BEGIN INSERT INTO #LotsOfRows VALUES (@i); SET @i = @i + 1; END"));

                sink.Lines.Clear();
                await ev.Evaluate(Parse("SELECT * FROM #LotsOfRows;"));

                var resultLine = Assert.Single(sink.Lines);
                using var document = JsonDocument.Parse(resultLine);
                var renderedRows = document.RootElement.GetProperty("rows").GetArrayLength();

                Assert.Equal(10, renderedRows);
                Assert.Equal(10, ev.LastResult?.Rows.Count);
                Assert.True(ev.LastResult?.IsCapped);
            }
            finally
            {
                ResultFormatter.IsJsonMode = previousJsonMode;
                ResultFormatter.SuppressOutput = previousSuppressOutput;
                ResultFormatter.OutputSink = previousSink;
            }
        }

        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }

        private sealed class CaptureOutputSink : ResultFormatter.IResultOutputSink
        {
            public List<string> Lines { get; } = new();
            public void Write(Table table) { }
            public void WriteLine(string text) => Lines.Add(text);
            public void MarkupLine(string markup) => Lines.Add(markup);
            public ConsoleKeyInfo ReadKey(bool intercept) => default;
        }
    }
}
