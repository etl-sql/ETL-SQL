using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>The sidebar's schema-explorer mode: toggling, building the tree, and insert-at-cursor.</summary>
    public class SidebarMetadataTests
    {
        static SidebarMetadataTests()
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
        public void FlatItems_AlwaysStartWithModeToggle()
        {
            var sb = NewEditor()._renderer._sidebarPanel;
            var items = sb.GetFlatVisibleItems();
            Assert.Equal(SidebarNodeKind.ModeToggle, items[0].Node.Kind);
        }

        [Fact]
        public async Task ToggleMode_SwitchesToMetadata_AndBuildsConnectionNodes()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "CREATE CONNECTION m AS MOCKDB();" });

            await e._renderer._sidebarPanel.ToggleModeAsync(e.CurrentScriptText);

            Assert.Equal(SidebarMode.Metadata, e._renderer._sidebarPanel.Mode);
            Assert.Contains(e._renderer._sidebarPanel.MetadataRoots, n => n.Kind == SidebarNodeKind.Connection && n.Name == "m");

            // The connection eagerly loaded its tables, and each table its columns.
            var conn = e._renderer._sidebarPanel.MetadataRoots.First(n => n.Name == "m");
            Assert.NotEmpty(conn.Children);
            Assert.All(conn.Children, t => Assert.Equal(SidebarNodeKind.Table, t.Kind));
            Assert.Contains(conn.Children, t => t.Children.Any(c => c.Kind == SidebarNodeKind.Column));
        }

        [Fact]
        public async Task ToggleMode_Twice_ReturnsToFiles()
        {
            var sb = NewEditor()._renderer._sidebarPanel;
            await sb.ToggleModeAsync("");
            await sb.ToggleModeAsync("");
            Assert.Equal(SidebarMode.Files, sb.Mode);
        }

        [Fact]
        public void InsertAtCursor_PutsTextInBuffer()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "" });
            e._buffer.CursorLine = 0; e._buffer.CursorColumn = 0;

            e.InsertAtCursor("m.Users");

            Assert.Contains("m.Users", e._buffer.GetText());
        }

        [Fact]
        public async Task MetadataMode_FlatItems_IncludeToggleAndRefresh()
        {
            var e = NewEditor();
            await e._renderer._sidebarPanel.ToggleModeAsync("");
            var items = e._renderer._sidebarPanel.GetFlatVisibleItems();

            Assert.Equal(SidebarNodeKind.ModeToggle, items[0].Node.Kind);
            Assert.Equal(SidebarNodeKind.Refresh, items[1].Node.Kind);
        }
    }
}
