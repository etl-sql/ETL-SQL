using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Sidebar deep-tree readability (extension-preserving truncation) and the ".." parent
    /// navigation that re-roots the tree one level up.
    /// </summary>
    public class SidebarNavTests
    {
        static SidebarNavTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        [Fact]
        public void TruncateName_KeepsExtensionForFiles()
        {
            var result = SidebarPanel.TruncateName("a_really_long_report_name.sql", 12, isDirectory: false);
            Assert.True(result.Length <= 12);
            Assert.EndsWith(".sql", result);
            Assert.Contains("…", result);
        }

        [Fact]
        public void TruncateName_ShortNameUnchanged()
        {
            Assert.Equal("short.sql", SidebarPanel.TruncateName("short.sql", 20, false));
        }

        [Fact]
        public void TruncateName_DirectoriesPlainEllipsis()
        {
            var result = SidebarPanel.TruncateName("VeryLongDirectoryName", 8, isDirectory: true);
            Assert.True(result.Length <= 8);
            Assert.EndsWith("…", result);
        }

        [Fact]
        public void FlatItems_IncludeParentEntry_WhenRootHasParent()
        {
            using var temp = new TempTree();
            var panel = NewPanel();
            panel.Initialize(Path.Combine(temp.Child, "x.etlsql")); // root = temp.Child (has parent temp.Root)

            var items = panel.GetFlatVisibleItems();
            // Row 0 is the mode toggle; the ".." parent entry follows in Files mode.
            Assert.Equal(SidebarNodeKind.ModeToggle, items[0].Node.Kind);
            Assert.Contains(items, i => i.Node.IsParentNav && i.Node.Name == "..");
        }

        [Fact]
        public void NavigateUp_ReRootsToParent()
        {
            using var temp = new TempTree();
            var panel = NewPanel();
            panel.Initialize(Path.Combine(temp.Child, "x.etlsql"));
            Assert.Equal(temp.Child, panel.RootNodes[0].Path);

            panel.NavigateUp();
            Assert.Equal(temp.Root, panel.RootNodes[0].Path);
            // The previous root now appears as a child folder of the new root.
            Assert.Contains(panel.RootNodes[0].Children, c => c.Path == temp.Child);
        }

        private static SidebarPanel NewPanel()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            return editor._renderer._sidebarPanel;
        }

        /// <summary>A throwaway root/child directory pair for re-rooting tests.</summary>
        private sealed class TempTree : System.IDisposable
        {
            public string Root { get; }
            public string Child { get; }

            public TempTree()
            {
                Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "etlsql_sb_" + Path.GetRandomFileName()));
                Child = Path.Combine(Root, "child");
                Directory.CreateDirectory(Child);
            }

            public void Dispose()
            {
                try { Directory.Delete(Root, recursive: true); } catch { }
            }
        }
    }
}
