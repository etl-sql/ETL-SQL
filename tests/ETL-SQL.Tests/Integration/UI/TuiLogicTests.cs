using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.TUI.UI;
using ETL_SQL.Data;
using ETL_SQL.TUI;

namespace ETL_SQL.Tests.Integration.UI
{
    public class TuiLogicTests
    {
        [Fact]
        public async Task KeywordProvider_DoesNotAppendTrailingSpace()
        {
            var engine = new SuggestionEngine();
            var ctx = new SuggestionContext { Prefix = "SEL" };

            var results = (await engine.GetSuggestionsAsync(ctx)).ToList();
            var selectSuggestion = results.FirstOrDefault(s => s.Text.Trim() == "SELECT");

            Assert.NotNull(selectSuggestion);
            Assert.False(selectSuggestion.Text.EndsWith(" "), "Keyword suggestion must NOT end with a space to prevent double-spacing bugs.");
        }

        [Fact]
        public async Task RunScript_ResolvesRelativeSubScriptsFromOpenFileDirectory()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "etl-sql-tui-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var mainPath = Path.Combine(tempDir, "main.etlsql");
                var subPath = Path.Combine(tempDir, "sub_script.etlsql");

                await File.WriteAllTextAsync(subPath, @"
DECLARE @child int OUTPUT;
SET @child = 42;
");
                await File.WriteAllTextAsync(mainPath, "RUN SCRIPT 'sub_script.etlsql';");

                ETL_SQL.TUI.Program.ServiceProvider = TuiDependencyInjectionSetup.BuildServiceProvider();
                var editor = new ConsoleEditor(mainPath, new Dictionary<string, IDataSource>());
                editor._renderer.Headless = true;

                await editor.InitializeAsync();
                await editor.RunScript();

                Assert.Equal(42m, Convert.ToDecimal(editor._evaluator.GetVariable("@child")));
                Assert.DoesNotContain(editor._evaluator.Messages, message => message.Message.Contains("Script file not found"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
