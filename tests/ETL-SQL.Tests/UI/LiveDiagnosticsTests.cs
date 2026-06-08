using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>The shared static-analysis pass behind F5 and the live (debounced) diagnostics.</summary>
    public class LiveDiagnosticsTests
    {
        static LiveDiagnosticsTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        private static ConsoleEditor NewEditor()
        {
            var e = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            e._renderer.Headless = true;
            return e;
        }

        [Fact]
        public async Task AnalyzeAsync_ProducesDiagnostics_ForProblemScript()
        {
            var (_, diags) = await NewEditor().AnalyzeAsync("SELECT @missing;", logToMessages: false);
            Assert.NotEmpty(diags);
        }

        [Fact]
        public async Task AnalyzeAsync_DoesNotExecuteTheScript()
        {
            var e = NewEditor();
            await e.AnalyzeAsync("DECLARE @x INT = 5;", logToMessages: false);

            // Pure analysis must not evaluate — no variable should have been declared.
            Assert.Empty(e._evaluator.VarContext.GetVariablesWithMetadata());
        }

        [Fact]
        public async Task AnalyzeAsync_FeedsTheGutterMap()
        {
            var (_, diags) = await NewEditor().AnalyzeAsync("SELECT @missing;", logToMessages: false);
            Assert.NotEmpty(DiagnosticGutter.BuildLineMap(diags));
        }

        [Fact]
        public async Task AnalyzeAsync_CancelledToken_Throws()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<System.OperationCanceledException>(
                () => NewEditor().AnalyzeAsync("SELECT 1;", logToMessages: false, cts.Token));
        }

        [Fact]
        public async Task Diagnostic_IsFindableByLine_ForStatusBar()
        {
            // The status bar shows the diagnostic on the cursor's line by matching d.Line.
            var (_, diags) = await NewEditor().AnalyzeAsync("SELECT 1;\nSELECT @missing;", logToMessages: false);
            var onLine2 = diags.FirstOrDefault(d => d.Line == 2);
            Assert.NotNull(onLine2);
            Assert.False(string.IsNullOrEmpty(onLine2!.Message));
        }

        [Fact]
        public void ScheduleLiveAnalysis_Headless_IsNoOp()
        {
            // Tests run headless; the debounced background pass must not be scheduled.
            var e = NewEditor();
            e.ScheduleLiveAnalysis();
            Assert.False(e._renderer.LiveAnalysisPending);
        }
    }
}
