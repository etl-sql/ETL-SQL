using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    public class ConsoleEditorExportTests
    {
        static ConsoleEditorExportTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        [Fact]
        public async Task ExportManifest_SourceIsTheScriptPathNotItsText()
        {
            var editor = new ConsoleEditor("sales.rptsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._buffer.Load(new[] { "SELECT 'the whole script text' AS Body;" });

            var manifest = await editor.BuildExportManifestAsync();

            Assert.Equal(Path.GetFullPath("sales.rptsql"), manifest.Source);
            Assert.DoesNotContain("whole script text", manifest.Source);
        }
    }
}
