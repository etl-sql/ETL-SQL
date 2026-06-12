using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>The Output bottom-pane tab: entries, the tab itself, F4 cycle, and clicking it.</summary>
    public class OutputTests
    {
        static OutputTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        private static ConsoleEditor NewEditor()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            return editor;
        }

        [Theory]
        [InlineData("http://localhost:5050/", true)]
        [InlineData("https://example.com", true)]
        [InlineData("C:\\reports\\sales.pdf", false)]
        public void OutputEntry_DetectsUrls(string location, bool isUrl)
        {
            Assert.Equal(isUrl, new OutputEntry(OutputKind.File, location, DateTime.Now).IsUrl);
        }

        [Theory]
        [InlineData(OutputKind.Server, "Serve")]
        [InlineData(OutputKind.Pdf, "PDF")]
        [InlineData(OutputKind.Markdown, "Markdown")]
        [InlineData(OutputKind.Csv, "CSV")]
        public void OutputEntry_KindLabels(OutputKind kind, string label)
        {
            Assert.Equal(label, new OutputEntry(kind, "x", DateTime.Now).KindLabel);
        }

        [Fact]
        public void BottomTabStrip_IncludesOutput()
        {
            Assert.Contains(BottomTabStrip.Tabs, t => t.Tab == BottomTab.Output);
        }

        [Fact]
        public void AddOutput_AppendsAndSelectsNewest()
        {
            var editor = NewEditor();
            editor._renderer.AddOutput(OutputKind.Server, "http://localhost:1/");
            editor._renderer.AddOutput(OutputKind.Pdf, "C:\\r\\sales.pdf");

            Assert.Equal(2, editor._renderer.OutputEntries.Count);
            Assert.Equal("C:\\r\\sales.pdf", editor._renderer.OutputEntries[1].Location);
            Assert.Equal(1, editor._renderer.OutputSelectedIndex);
        }

        [Fact]
        public async Task F4_CyclesThroughToOutput()
        {
            var editor = NewEditor();
            // Pipeline -> Results -> Performance -> Output
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F4, false, false, false));
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F4, false, false, false));
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F4, false, false, false));

            Assert.True(editor._renderer.OutputVisible);
            Assert.False(editor._renderer.ResultsVisible);
            Assert.False(editor._renderer.PerformanceVisible);
        }

        [Fact]
        public async Task ClickingOutputTab_ShowsOutputView()
        {
            var editor = NewEditor();
            editor._renderer.Render(editor, 80, 24); // lowerY = 10

            var seg = BottomTabStrip.Segments().First(s => s.Tab == BottomTab.Output);
            await editor._renderer.HandleMouseClick(0, seg.StartX, 10, false, editor);

            Assert.True(editor._renderer.OutputVisible);
        }
    }
}
