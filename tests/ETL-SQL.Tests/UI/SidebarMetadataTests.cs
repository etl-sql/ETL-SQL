using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Core.Common;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>The sidebar's schema-explorer mode: toggle, lazy load, views grouping, insert.</summary>
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

        // A minimal database source returning fixed tables/views/columns.
        private sealed class FakeDbSource : IDatabaseSource
        {
            private readonly string[] _tables, _views, _cols;
            public FakeDbSource(string[] tables, string[] views, string[] cols) { _tables = tables; _views = views; _cols = cols; }
            public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(_tables.AsEnumerable());
            public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(_views.AsEnumerable());
            public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => Task.FromResult(_cols.AsEnumerable());
            public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(_cols.AsEnumerable());
            public string ConnectionString => "";
            public string Dialect => "FAKE";
            public bool SupportsSqlPushdown => true;
            public Task<string> GetVersionAsync() => Task.FromResult("1");
            public HashSet<string> GetSupportedFunctions() => new();
            public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? p = null) { await Task.CompletedTask; yield break; }
            public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) { await Task.CompletedTask; yield break; }
            public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => Task.CompletedTask;
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public string Path => "";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "FAKE";
            public ValueTask DisposeAsync() => default;
        }

        private static SidebarPanel SidebarWithFakeDb(ConsoleEditor e, FakeDbSource db, string connName = "db")
        {
            var dict = new Dictionary<string, IDataSource> { [connName] = db };
            e._renderer._sidebarPanel.SetMetadata(new MetadataManager(SystemExecutionContext.Instance, dict));
            return e._renderer._sidebarPanel;
        }

        [Fact]
        public void FlatItems_AlwaysStartWithModeToggle()
        {
            var sb = NewEditor()._renderer._sidebarPanel;
            Assert.Equal(SidebarNodeKind.ModeToggle, sb.GetFlatVisibleItems()[0].Node.Kind);
        }

        [Fact]
        public async Task ToggleMode_BuildsConnectionRowsLazily()
        {
            var e = NewEditor();
            var sb = SidebarWithFakeDb(e, new FakeDbSource(new[] { "Orders" }, Array.Empty<string>(), new[] { "Id" }));

            await sb.ToggleModeAsync(""); // empty script → keep injected connections

            Assert.Equal(SidebarMode.Metadata, sb.Mode);
            var conn = Assert.Single(sb.MetadataRoots);
            Assert.Equal(SidebarNodeKind.Connection, conn.Kind);
            Assert.Empty(conn.Children);     // lazy: nothing loaded until expanded
            Assert.False(conn.IsLoaded);
        }

        [Fact]
        public async Task EnsureLoaded_Connection_LoadsTables_AndColumnsOnTableExpand()
        {
            var e = NewEditor();
            var sb = SidebarWithFakeDb(e, new FakeDbSource(new[] { "Orders" }, Array.Empty<string>(), new[] { "Id", "Total" }));
            await sb.ToggleModeAsync("");

            var conn = sb.MetadataRoots[0];
            await sb.EnsureLoadedAsync(conn);
            Assert.True(conn.IsLoaded);
            var tables = conn.Children.Where(c => c.Kind == SidebarNodeKind.Table).ToList();
            var table = Assert.Single(tables);
            Assert.Equal("Orders", table.Name);
            Assert.Equal("db.Orders", table.InsertText);

            await sb.EnsureLoadedAsync(table);
            Assert.Equal(new[] { "Id", "Total" }, table.Children.Select(c => c.Name));
            Assert.All(table.Children, c => Assert.Equal(SidebarNodeKind.Column, c.Kind));
        }

        [Fact]
        public async Task EnsureLoaded_Connection_GroupsViewsSeparately()
        {
            var e = NewEditor();
            var sb = SidebarWithFakeDb(e, new FakeDbSource(new[] { "Orders" }, new[] { "ActiveOrders" }, new[] { "Id" }));
            await sb.ToggleModeAsync("");

            var conn = sb.MetadataRoots[0];
            await sb.EnsureLoadedAsync(conn);

            var groups = conn.Children.Where(c => c.Kind == SidebarNodeKind.Group).ToList();
            var group = Assert.Single(groups);
            var view = Assert.Single(group.Children);
            Assert.Equal(SidebarNodeKind.View, view.Kind);
            Assert.Equal("ActiveOrders", view.Name);
        }

        [Fact]
        public async Task EnsureLoaded_NoViews_AddsNoGroup()
        {
            var e = NewEditor();
            var sb = SidebarWithFakeDb(e, new FakeDbSource(new[] { "Orders" }, Array.Empty<string>(), new[] { "Id" }));
            await sb.ToggleModeAsync("");

            var conn = sb.MetadataRoots[0];
            await sb.EnsureLoadedAsync(conn);
            Assert.DoesNotContain(conn.Children, c => c.Kind == SidebarNodeKind.Group);
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
            e.InsertAtCursor("m.Users");
            Assert.Contains("m.Users", e._buffer.GetText());
        }

        [Fact]
        public async Task MetadataMode_FlatItems_IncludeToggleAndRefresh()
        {
            var sb = NewEditor()._renderer._sidebarPanel;
            await sb.ToggleModeAsync("");
            var items = sb.GetFlatVisibleItems();
            Assert.Equal(SidebarNodeKind.ModeToggle, items[0].Node.Kind);
            Assert.Equal(SidebarNodeKind.Refresh, items[1].Node.Kind);
        }
    }
}
