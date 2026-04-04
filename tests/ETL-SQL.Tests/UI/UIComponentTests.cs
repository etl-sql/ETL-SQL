using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.UI;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests
{
    public class UIComponentTests
    {
        [Fact]
        public async Task AutocompleteController_ShowsSuggestions()
        {
            var buffer = new EditorBuffer();
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var renderer = new EditorRenderer(buffer, evaluator);
            var connections = new Dictionary<string, IDataSource>();
            var metadata = new MetadataManager(connections);
            var controller = new AutocompleteController(buffer, renderer, metadata, connections);
            
            buffer.Load(new[] { "SEL" });
            buffer.CursorLine = 0;
            buffer.CursorColumn = 3;
            
            await controller.UpdateAsync();
            
            Assert.True(renderer.AutocompleteVisible);
            Assert.Contains(renderer.AutocompleteOptions, o => o.Text == "SELECT");
        }

        [Fact]
        public async Task AutocompleteController_HandlesNavigation()
        {
            var buffer = new EditorBuffer();
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var renderer = new EditorRenderer(buffer, evaluator);
            var connections = new Dictionary<string, IDataSource>();
            var metadata = new MetadataManager(connections);
            var controller = new AutocompleteController(buffer, renderer, metadata, connections);
            
            buffer.Load(new[] { "S" });
            buffer.CursorLine = 0;
            buffer.CursorColumn = 1;
            await controller.UpdateAsync();
            
            Assert.True(renderer.AutocompleteOptions.Count > 1);
            
            int initialIndex = renderer.AutocompleteIndex;
            controller.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            Assert.Equal(initialIndex + 1, renderer.AutocompleteIndex);
            
            controller.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.Equal(initialIndex, renderer.AutocompleteIndex);
        }

        [Fact]
        public async Task InputHandler_MapsBasicKeys()
        {
            var buffer = new EditorBuffer();
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var renderer = new EditorRenderer(buffer, evaluator);
            var connections = new Dictionary<string, IDataSource>();
            var metadata = new MetadataManager(connections);
            var controller = new AutocompleteController(buffer, renderer, metadata, connections);
            ETL_SQL.Program.ServiceProvider = DependencyInjectionSetup.BuildServiceProvider();
            var editor = new ConsoleEditor("test.etlsql", connections);
            var handler = new InputHandler(editor, buffer, renderer, controller);
            
            // Test normal character
            var keyInfo = new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false);
            await handler.HandleKey(keyInfo);
            Assert.Equal("A", buffer.GetText());
            
            // Test backspace
            var backspace = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false);
            await handler.HandleKey(backspace);
            Assert.Equal("", buffer.GetText());
        }

        [Fact]
        public void MetadataManager_RefreshesConnections()
        {
            var connections = new Dictionary<string, IDataSource>();
            var manager = new MetadataManager(connections);
            string script = "CREATE CONNECTION C ON MOCKDB('dummy');";
            
            manager.RefreshConnections(script, force: true);
            
            Assert.True(connections.ContainsKey("C"));
        }
    }
}
