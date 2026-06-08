using Xunit;
using System;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.TUI;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>The DI composition root builds a usable editor with dependencies injected.</summary>
    public class EditorFactoryTests
    {
        [Fact]
        public void CreateEditor_ResolvesDependencies_AndBuildsEditor()
        {
            var sp = TuiDependencyInjectionSetup.BuildServiceProvider();
            var editor = TuiDependencyInjectionSetup.CreateEditor(sp, "test.etlsql", new Dictionary<string, IDataSource>());

            Assert.NotNull(editor);
            Assert.False(editor.IsRunning);
        }

        [Fact]
        public void InjectingConnections_RegistersThemOnTheEvaluator()
        {
            var sp = TuiDependencyInjectionSetup.BuildServiceProvider();
            var conns = new Dictionary<string, IDataSource>
            {
                ["m"] = new ETL_SQL.Connectors.MockDb.MockSqlDataSource(
                    ETL_SQL.Core.Common.SystemExecutionContext.Instance, "dummy", "MSSQL")
            };
            var editor = TuiDependencyInjectionSetup.CreateEditor(sp, "test.etlsql", conns);

            Assert.True(editor._evaluator.Connections.ContainsKey("m"));
        }
    }
}
