using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Auto-reload of clean buffers on external change, and the Compare side-by-side.</summary>
    public class FileReloadTests
    {
        static FileReloadTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        private static ConsoleEditor NewEditor()
        {
            var e = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            e._renderer.Headless = true;
            return e;
        }

        private static string TempScript(string content)
        {
            var p = Path.Combine(Path.GetTempPath(), "reload_" + Path.GetRandomFileName() + ".etlsql");
            File.WriteAllText(p, content);
            return p;
        }

        [Fact]
        public async Task MaybeAutoReload_CleanBuffer_PicksUpExternalChange()
        {
            var path = TempScript("SELECT 1;");
            try
            {
                var e = NewEditor();
                await e.LoadFile(path);
                Assert.Equal("SELECT 1;", e._buffer.GetText());

                File.WriteAllText(path, "SELECT 2;");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(30)); // force a distinct mtime

                await e.MaybeAutoReloadAsync();
                Assert.Equal("SELECT 2;", e._buffer.GetText());
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public async Task MaybeAutoReload_DirtyBuffer_DoesNotDiscardEdits()
        {
            var path = TempScript("SELECT 1;");
            try
            {
                var e = NewEditor();
                await e.LoadFile(path);
                e.MarkDirty(); // unsaved edits present

                File.WriteAllText(path, "SELECT 2;");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(30));

                await e.MaybeAutoReloadAsync();
                Assert.Equal("SELECT 1;", e._buffer.GetText()); // not auto-reloaded — edits preserved
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void BuildSideBySide_MarksDifferingLines_AndHasHeaders()
        {
            var s = ConsoleEditor.BuildSideBySide(new[] { "a", "b" }, new[] { "a", "X" });
            Assert.Contains("EDITOR (unsaved)", s);
            Assert.Contains("ON DISK", s);
            Assert.Contains("≠", s); // line 2 differs
        }

        [Fact]
        public void BuildSideBySide_EscapesMarkupBrackets()
        {
            var s = ConsoleEditor.BuildSideBySide(new[] { "[x]" }, new[] { "" });
            Assert.Contains("[[x]]", s);
        }
    }
}
