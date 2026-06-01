using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Engine;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Tests for Phase 6: Database Catalog Metadata Import.
    /// Uses an in-memory mock provider — no real database required.
    /// </summary>
    public class CatalogMetadataImportTests
    {
        // ── ICatalogMetadataProvider contract ───────────────────────────────

        [Fact]
        public async Task MockProvider_ReturnsColumns()
        {
            var provider = new MockCatalogProvider();
            var cols = await provider.GetColumnMetadataAsync("dbo", "Customers");
            Assert.Equal(3, cols.Count);
            Assert.Contains(cols, c => c.ColumnName == "CustomerID" && c.IsPrimaryKey);
            Assert.Contains(cols, c => c.ColumnName == "Email" && !c.IsNullable);
            Assert.Contains(cols, c => c.ColumnName == "PhoneNumber" && c.IsNullable);
        }

        [Fact]
        public async Task MockProvider_ReturnsRelationships()
        {
            var provider = new MockCatalogProvider();
            var rels = await provider.GetRelationshipsAsync("dbo", "Orders");
            Assert.Single(rels);
            Assert.Equal("CustomerID", rels[0].ForeignKeyColumn);
            Assert.Equal("dbo.Customers", rels[0].ReferencedTable);
            Assert.Equal("CustomerID", rels[0].ReferencedColumn);
        }

        // ── CatalogColumn record ───────────────────────────────────────────

        [Fact]
        public void CatalogColumn_Equality_WorksByValue()
        {
            var c1 = new CatalogColumn("id", "INT", false, true, "Primary key", new Dictionary<string, string>());
            var c2 = new CatalogColumn("id", "INT", false, true, "Primary key", new Dictionary<string, string>());
            Assert.Equal(c1.ColumnName, c2.ColumnName);
            Assert.Equal(c1.DataType, c2.DataType);
            Assert.Equal(c1.IsNullable, c2.IsNullable);
            Assert.Equal(c1.IsPrimaryKey, c2.IsPrimaryKey);
            Assert.Equal(c1.Description, c2.Description);
        }

        [Fact]
        public void CatalogColumn_WithNullDescription_IsValid()
        {
            var col = new CatalogColumn("name", "VARCHAR", true, false, null, new Dictionary<string, string>());
            Assert.Null(col.Description);
        }

        // ── IConnector.GetCatalogProvider default ──────────────────────────

        [Fact]
        public void IConnector_GetCatalogProvider_DefaultReturnsNull()
        {
            // MockDbConnector doesn't override GetCatalogProvider → null via DIM
            IConnector connector = new ETL_SQL.Connectors.MockDb.MockDbConnector();
            var provider = connector.GetCatalogProvider("mockdb://");
            Assert.Null(provider);
        }

        // ── IDataSource.GetCatalogProvider default ─────────────────────────

        [Fact]
        public void IDataSource_GetCatalogProvider_DefaultReturnsNull()
        {
            // A data source that doesn't override GetCatalogProvider returns null
            IDataSource ds = new NoCatalogDataSource();
            Assert.Null(ds.GetCatalogProvider());
        }

        // ── Tag naming conventions ─────────────────────────────────────────

        [Fact]
        public void DbTagPrefix_IsDbUnderscore()
        {
            // Structural catalog tags carry the db_ prefix; the column comment is
            // recorded as the lineage description ("d") so it inherits downstream.
            var col = new CatalogColumn("email", "VARCHAR", false, false, "Contact email", new Dictionary<string, string>());
            var tags = BuildDbTags(col);
            Assert.All(tags.Keys.Where(k => k != "d"), k => Assert.StartsWith("db_", k));
        }

        [Fact]
        public void DbColumnComment_MappedToLineageDescription()
        {
            var col = new CatalogColumn("email", "VARCHAR", false, false, "Contact email", new Dictionary<string, string>());
            var tags = BuildDbTags(col);
            Assert.Equal("Contact email", tags["d"]);
        }

        [Fact]
        public void DbTagPrefix_NullableAndPk_MappedCorrectly()
        {
            var col = new CatalogColumn("id", "INT", false, true, null, new Dictionary<string, string>());
            var tags = BuildDbTags(col);
            Assert.Equal("false", tags["db_nullable"]);
            Assert.Equal("true", tags["db_is_pk"]);
        }

        [Fact]
        public async Task Evaluator_LazilyImportsCatalogMetadataBeforeSelectLineage()
        {
            var evaluator = CreateEvaluator();
            evaluator.LineageImportCatalog = true;

            var provider = new RecordingCatalogProvider();
            evaluator.Connections["mock"] = new CatalogBackedDataSource(provider);

            await evaluator.Evaluate(Parse(
                "SELECT SUM(Amount) AS TotalAmount INTO #Summary FROM mock.dbo.Sales;"));

            Assert.Equal(("dbo", "Sales"), Assert.Single(provider.ColumnRequests));
            Assert.Equal(("dbo", "Sales"), Assert.Single(provider.RelationshipRequests));

            var entries = evaluator.LineageTracker.GetFullLineage().ToList();
            var imported = Assert.Single(entries, e =>
                e.Operation == "DB_CATALOG" &&
                e.TargetTable == "mock.dbo.Sales" &&
                e.TargetColumn == "Amount");
            Assert.Equal("DECIMAL(18,2)", imported.Metadata["db_type"]);
            Assert.Equal("false", imported.Metadata["db_nullable"]);
            Assert.Equal("Sales amount from catalog", imported.Metadata["d"]);
            Assert.Equal("finance", imported.Metadata["db_domain"]);

            var derived = Assert.Single(entries, e =>
                e.TargetTable == "#Summary" &&
                e.Operation == "SELECT INTO" &&
                e.TargetColumn == "TotalAmount");
            Assert.Contains("Amount: Sales amount from catalog", derived.DerivedFromDescriptions);
            Assert.Equal("finance", derived.Metadata["db_domain"]);
        }

        [Fact]
        public async Task Evaluator_DoesNotImportCatalogMetadataWhenDisabled()
        {
            var evaluator = CreateEvaluator();
            evaluator.LineageImportCatalog = false;

            var provider = new RecordingCatalogProvider();
            evaluator.Connections["mock"] = new CatalogBackedDataSource(provider);

            await evaluator.Evaluate(Parse(
                "SELECT SUM(Amount) AS TotalAmount INTO #Summary FROM mock.dbo.Sales;"));

            Assert.Empty(provider.ColumnRequests);
            Assert.DoesNotContain(evaluator.LineageTracker.GetFullLineage(),
                e => e.Operation == "DB_CATALOG");
        }

        private static Dictionary<string, string> BuildDbTags(CatalogColumn col)
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["db_type"]     = col.DataType,
                ["db_nullable"] = col.IsNullable ? "true" : "false",
                ["db_is_pk"]    = col.IsPrimaryKey ? "true" : "false",
            };
            if (!string.IsNullOrEmpty(col.Description))
                meta["d"] = col.Description;   // column comment → lineage description
            return meta;
        }

        private static Evaluator CreateEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize(), sql).Parse();

        // ── Helper types ───────────────────────────────────────────────────

        private sealed class MockCatalogProvider : ICatalogMetadataProvider
        {
            public Task<IReadOnlyList<CatalogColumn>> GetColumnMetadataAsync(string schema, string tableName, CancellationToken ct = default)
            {
                IReadOnlyList<CatalogColumn> cols = new List<CatalogColumn>
                {
                    new("CustomerID", "INT",     false, true,  "Surrogate PK",   new Dictionary<string, string>()),
                    new("Email",      "VARCHAR", false, false, "Contact email",  new Dictionary<string, string>()),
                    new("PhoneNumber","VARCHAR", true,  false, null,             new Dictionary<string, string>()),
                };
                return Task.FromResult(cols);
            }

            public Task<IReadOnlyList<CatalogRelationship>> GetRelationshipsAsync(string schema, string tableName, CancellationToken ct = default)
            {
                IReadOnlyList<CatalogRelationship> rels = new List<CatalogRelationship>
                {
                    new("CustomerID", "dbo.Customers", "CustomerID"),
                };
                return Task.FromResult(rels);
            }
        }

        private sealed class NoCatalogDataSource : IDataSource
        {
            public string Path => "MOCK";
            public string ConnectorType => "MOCK";
            public Dictionary<string, string>? Options => null;
            public IAsyncEnumerable<ETL_SQL.Data.DataTable> ReadBatches(int batchSize = 10000)
                => throw new NotSupportedException();
            public Task WriteBatches(IAsyncEnumerable<ETL_SQL.Data.DataTable> batches, bool append = false)
                => throw new NotSupportedException();
            public Task<IEnumerable<string>> GetColumnsAsync()
                => Task.FromResult(Enumerable.Empty<string>());
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class RecordingCatalogProvider : ICatalogMetadataProvider
        {
            public List<(string Schema, string Table)> ColumnRequests { get; } = new();
            public List<(string Schema, string Table)> RelationshipRequests { get; } = new();

            public Task<IReadOnlyList<CatalogColumn>> GetColumnMetadataAsync(string schema, string tableName, CancellationToken ct = default)
            {
                ColumnRequests.Add((schema, tableName));
                IReadOnlyList<CatalogColumn> cols = new List<CatalogColumn>
                {
                    new(
                        "Amount",
                        "DECIMAL(18,2)",
                        false,
                        false,
                        "Sales amount from catalog",
                        new Dictionary<string, string> { ["domain"] = "finance" })
                };
                return Task.FromResult(cols);
            }

            public Task<IReadOnlyList<CatalogRelationship>> GetRelationshipsAsync(string schema, string tableName, CancellationToken ct = default)
            {
                RelationshipRequests.Add((schema, tableName));
                return Task.FromResult<IReadOnlyList<CatalogRelationship>>(Array.Empty<CatalogRelationship>());
            }
        }

        private sealed class CatalogBackedDataSource : IDataSource
        {
            private readonly RecordingCatalogProvider _provider;

            public CatalogBackedDataSource(RecordingCatalogProvider provider)
            {
                _provider = provider;
            }

            public string Path => "MOCK";
            public string ConnectorType => "MOCK";
            public Dictionary<string, string>? Options => null;

            public async IAsyncEnumerable<ETL_SQL.Data.DataTable> ReadBatches(int batchSize = 10000)
            {
                var table = new ETL_SQL.Data.DataTable();
                table.SetColumns(new[] { "Amount" });
                await table.AddRowAsync(new Row { ["Amount"] = 10m });
                await table.AddRowAsync(new Row { ["Amount"] = 15m });
                yield return table;
            }

            public Task WriteBatches(IAsyncEnumerable<ETL_SQL.Data.DataTable> batches, bool append = false)
                => throw new NotSupportedException();

            public Task<IEnumerable<string>> GetColumnsAsync()
                => Task.FromResult<IEnumerable<string>>(new[] { "Amount" });

            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public ICatalogMetadataProvider? GetCatalogProvider() => _provider;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
