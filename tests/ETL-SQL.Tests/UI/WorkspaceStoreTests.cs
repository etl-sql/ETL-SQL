using Xunit;
using System.IO;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Workspace session persistence and clean/unclean-shutdown detection.</summary>
    public class WorkspaceStoreTests
    {
        private static string TempDir() =>
            Path.Combine(Path.GetTempPath(), "etlsql_ws_" + Path.GetRandomFileName());

        private static WorkspaceSession Sample(string wd) => new()
        {
            WorkingDirectory = wd,
            ActiveTab = 1,
            Tabs =
            {
                new WorkspaceTab { FilePath = "a.etlsql", CursorLine = 2, CursorColumn = 3 },
                new WorkspaceTab { FilePath = "b.etlsql", IsDirty = true, RecoveryText = "SELECT 1;", CursorLine = 5 },
            }
        };

        [Fact]
        public void Save_Load_RoundTrips()
        {
            var original = WorkspaceStore.BaseDir;
            WorkspaceStore.BaseDir = TempDir();
            try
            {
                var store = new WorkspaceStore();
                var wd = TempDir();
                store.Save(Sample(wd));

                var loaded = store.Load(wd);
                Assert.NotNull(loaded);
                Assert.Equal(1, loaded!.ActiveTab);
                Assert.Equal(2, loaded.Tabs.Count);
                Assert.Equal("b.etlsql", loaded.Tabs[1].FilePath);
                Assert.True(loaded.Tabs[1].IsDirty);
                Assert.Equal("SELECT 1;", loaded.Tabs[1].RecoveryText);
                Assert.Equal(2, loaded.Tabs[0].CursorLine);
            }
            finally { TryClean(WorkspaceStore.BaseDir); WorkspaceStore.BaseDir = original; }
        }

        [Fact]
        public void Load_Missing_ReturnsNull()
        {
            var original = WorkspaceStore.BaseDir;
            WorkspaceStore.BaseDir = TempDir();
            try { Assert.Null(new WorkspaceStore().Load(TempDir())); }
            finally { TryClean(WorkspaceStore.BaseDir); WorkspaceStore.BaseDir = original; }
        }

        [Fact]
        public void Sentinel_TracksCleanVsUncleanShutdown()
        {
            var original = WorkspaceStore.BaseDir;
            WorkspaceStore.BaseDir = TempDir();
            try
            {
                var store = new WorkspaceStore();
                var wd = TempDir();

                Assert.False(store.WasUncleanShutdown(wd));
                store.MarkRunning(wd);
                Assert.True(store.WasUncleanShutdown(wd)); // sentinel present = previous run didn't exit cleanly
                store.MarkCleanExit(wd);
                Assert.False(store.WasUncleanShutdown(wd));
            }
            finally { TryClean(WorkspaceStore.BaseDir); WorkspaceStore.BaseDir = original; }
        }

        [Fact]
        public void DifferentDirectories_DoNotCollide()
        {
            var original = WorkspaceStore.BaseDir;
            WorkspaceStore.BaseDir = TempDir();
            try
            {
                var store = new WorkspaceStore();
                string a = TempDir(), b = TempDir();
                var sa = Sample(a); sa.ActiveTab = 0;
                var sb = Sample(b); sb.ActiveTab = 1;
                store.Save(sa);
                store.Save(sb);

                Assert.Equal(0, store.Load(a)!.ActiveTab);
                Assert.Equal(1, store.Load(b)!.ActiveTab);
            }
            finally { TryClean(WorkspaceStore.BaseDir); WorkspaceStore.BaseDir = original; }
        }

        private static void TryClean(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
