using Xunit;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Variables bottom-pane tab: strip entry, view switching, and the data contract.</summary>
    public class VariablesPanelTests
    {
        static VariablesPanelTests()
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
        public void BottomTabStrip_IncludesVariables()
        {
            Assert.Contains(BottomTabStrip.Tabs, t => t.Tab == BottomTab.Variables);
        }

        [Fact]
        public void ShowBottomTab_Variables_SetsOnlyVariablesVisible()
        {
            var r = NewEditor()._renderer;
            r.ShowBottomTab(BottomTab.Variables);

            Assert.True(r.VariablesVisible);
            Assert.False(r.OutputVisible);
            Assert.False(r.ResultsVisible);
            Assert.False(r.PerformanceVisible);
        }

        [Fact]
        public void ShowBottomTab_OtherView_ClearsVariables()
        {
            var r = NewEditor()._renderer;
            r.ShowBottomTab(BottomTab.Variables);
            r.ShowBottomTab(BottomTab.Results);

            Assert.False(r.VariablesVisible);
            Assert.True(r.ResultsVisible);
        }

        [Fact]
        public void Evaluator_ExposesVariablesWithMetadata_ForPanel()
        {
            // The panel reads evaluator.VarContext.GetVariablesWithMetadata() and deconstructs
            // each entry into (value, metadata) — lock that contract.
            var e = NewEditor();
            e._evaluator.VarContext.DeclareVariable("@x", 5m);

            var vars = e._evaluator.VarContext.GetVariablesWithMetadata();
            Assert.Contains(vars, kv => Equals(kv.Value.Value, 5m));
            var entry = vars.First(kv => Equals(kv.Value.Value, 5m));
            Assert.NotNull(entry.Value.Metadata);
        }
    }
}
