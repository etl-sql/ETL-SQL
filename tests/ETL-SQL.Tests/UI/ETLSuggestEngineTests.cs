using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.UI;
using ETL_SQL.Data;
using Moq;
using ETL_SQL.Tests;
using ETL_SQL.App;

namespace ETL_SQL.Tests.UI
{
    public class ETLSuggestEngineTests
    {
        [Fact]
        public void ParseAliases_ShouldIdentifySimpleAliases()
        {
            string script = "SELECT * FROM Users u JOIN Orders o ON u.Id = o.UserId";
            var aliases = ETLSuggestEngine.ParseAliases(script);

            Assert.True(aliases.ContainsKey("u"));
            Assert.Equal("Users", aliases["u"].TableName);
            Assert.True(aliases.ContainsKey("o"));
            Assert.Equal("Orders", aliases["o"].TableName);
        }

        [Fact]
        public void ParseAliases_ShouldHandleNoAlias()
        {
            string script = "SELECT * FROM Users";
            var aliases = ETLSuggestEngine.ParseAliases(script);

            Assert.True(aliases.ContainsKey("Users"));
            Assert.Equal("Users", aliases["Users"].TableName);
            Assert.Null(aliases["Users"].Alias);
        }

        [Fact]
        public async Task TrySuggestAsync_ShouldExpandStar()
        {
            var buffer = new EditorBuffer();
            // Don't use CREATE CONNECTION here to avoid clearing in my new logic
            buffer.Load(new[] { "SELECT * FROM myTable" });
            buffer.CursorLine = 0;
            buffer.CursorColumn = 8; // After '*'

            var mockDs = new Mock<IDataSource>();
            mockDs.Setup(d => d.GetColumnsAsync()).ReturnsAsync(new List<string> { "Id", "Name", "Email" });

            var connections = new Dictionary<string, IDataSource> { { "myTable", mockDs.Object } };
            var metadata = new MetadataManager(connections);
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var renderer = new EditorRenderer(buffer, evaluator);
            var controller = new AutocompleteController(buffer, renderer, metadata, connections);

            await controller.TrySuggestAsync();

            Assert.Contains("Id, Name, Email", buffer.Lines[0]);
        }

        [Fact]
        public async Task TrySuggestAsync_ShouldExpandAliasStar()
        {
            var buffer = new EditorBuffer();
            buffer.Load(new[] { "SELECT u.* FROM myTable u" });
            buffer.CursorLine = 0;
            buffer.CursorColumn = 10; // After 'u.*'

            var mockDs = new Mock<IDataSource>();
            mockDs.Setup(d => d.GetColumnsAsync()).ReturnsAsync(new List<string> { "Id", "Name" });

            var connections = new Dictionary<string, IDataSource> { { "myTable", mockDs.Object } };
            var metadata = new MetadataManager(connections);
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var renderer = new EditorRenderer(buffer, evaluator);
            var controller = new AutocompleteController(buffer, renderer, metadata, connections);

            await controller.TrySuggestAsync();

            Assert.Contains("u.Id, u.Name", buffer.Lines[0]);
        }

        [Fact]
        public async Task TrySuggestAsync_ShouldExpandConnectionTableWithAliasStar()
        {
            var buffer = new EditorBuffer();
            buffer.Load(new[] { "SELECT * FROM myConn.Users AS u" });
            buffer.CursorLine = 0;
            buffer.CursorColumn = 8; // After '*'

            var mockDb = new Mock<IDatabaseSource>();
            mockDb.Setup(d => d.GetColumnsAsync("Users")).ReturnsAsync(new List<string> { "UserID", "UserName" });

            var connections = new Dictionary<string, IDataSource> { { "myConn", mockDb.Object } };
            var metadata = new MetadataManager(connections);
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var renderer = new EditorRenderer(buffer, evaluator);
            var controller = new AutocompleteController(buffer, renderer, metadata, connections);

            await controller.TrySuggestAsync();

            Assert.Contains("u.UserID, u.UserName", buffer.Lines[0]);
        }
        [Fact]
        public async Task TrySuggestAsync_ShouldExpandConnectionTableWithAliasNoAsStar()
        {
            var buffer = new EditorBuffer();
            buffer.Load(new[] { "SELECT * FROM myConn.Users u" });
            buffer.CursorLine = 0;
            buffer.CursorColumn = 8; // After '*'

            var mockDb = new Mock<IDatabaseSource>();
            mockDb.Setup(d => d.GetColumnsAsync("Users")).ReturnsAsync(new List<string> { "UserID", "UserName" });

            var connections = new Dictionary<string, IDataSource> { { "myConn", mockDb.Object } };
            var metadata = new MetadataManager(connections);
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var renderer = new EditorRenderer(buffer, evaluator);
            var controller = new AutocompleteController(buffer, renderer, metadata, connections);

            await controller.TrySuggestAsync();

            Assert.Contains("u.UserID, u.UserName", buffer.Lines[0]);
        }
    }
}
