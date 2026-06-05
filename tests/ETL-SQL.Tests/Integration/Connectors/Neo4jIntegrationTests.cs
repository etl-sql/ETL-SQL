using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Neo4j;
using ETL_SQL.Services;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Collection("Neo4j collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "NEO4J")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class Neo4jIntegrationTests
    {
        private readonly Neo4jFixture _fixture;

        public Neo4jIntegrationTests(Neo4jFixture fixture)
        {
            _fixture = fixture;
        }

        private IExecutionContext MakeContext(bool isWhatIf = false)
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            ctx.Setup(c => c.IsWhatIf).Returns(isWhatIf);
            return ctx.Object;
        }

        private Dictionary<string, string> MakeOptions(params (string Key, string Value)[] extras)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "USER", _fixture.Username },
                { "PASSWORD", _fixture.Password },
                { "DATABASE", "neo4j" }
            };
            foreach (var (key, value) in extras)
            {
                options[key] = value;
            }
            return options;
        }

        private static async IAsyncEnumerable<DataTable> Batches(params DataTable[] tables)
        {
            foreach (var table in tables)
            {
                yield return table;
            }
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Neo4jConnector_GetVersionAsync_Success()
        {
            var ctx = MakeContext();
            var connector = new Neo4jConnector();

            string connStr = $"bolt://{Uri.EscapeDataString(_fixture.Username)}:{Uri.EscapeDataString(_fixture.Password)}@127.0.0.1:{_fixture.Port}";

            var version = await connector.GetVersionAsync(ctx, connStr);
            Assert.Contains("Connected", version);
            Assert.Contains("Neo4j Connector", version);
        }

        [Fact]
        public void Neo4jConnector_BuildConnectionString_DoesNotEmbedCredentials()
        {
            var connector = new Neo4jConnector();
            var options = new Dictionary<string, string>
            {
                { "HOST", "graph.local" },
                { "PORT", "7687" },
                { "USER", "neo4j" },
                { "PASSWORD", "super-secret" }
            };

            string connStr = connector.BuildConnectionString(options);

            Assert.Equal("bolt://graph.local:7687", connStr);
            Assert.DoesNotContain("neo4j", connStr);
            Assert.DoesNotContain("super-secret", connStr);
        }

        [Fact]
        public async Task Neo4jConnector_GetVersionAsync_InvalidPassword_ThrowsException()
        {
            var ctx = MakeContext();
            var connector = new Neo4jConnector();

            string invalidConnStr = $"bolt://{Uri.EscapeDataString(_fixture.Username)}:{Uri.EscapeDataString("wrong_password")}@127.0.0.1:{_fixture.Port}";

            await Assert.ThrowsAsync<ExecutionException>(() =>
                connector.GetVersionAsync(ctx, invalidConnStr));
        }

        [Fact]
        public async Task Neo4jDataSource_Lifecycle_WriteAndRead_NodesAndEdges_Success()
        {
            var ctx = MakeContext();
            var options = MakeOptions();

            // 1. Create a Neo4jDataSource
            var dsNode = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_CUSTOMER", options);

            // 2. Truncate the DB first to ensure clean state
            await dsNode.TruncateAsync();

            // 3. Write Nodes (Customer)
            var nodeTable = new DataTable();
            nodeTable.SetColumns(new[] { "name", "city", "status" });
            await nodeTable.AddRowAsync(new Row { ["name"] = "Alice", ["city"] = "New York", ["status"] = "Active" });
            await nodeTable.AddRowAsync(new Row { ["name"] = "Bob", ["city"] = "San Francisco", ["status"] = "Inactive" });

            async IAsyncEnumerable<DataTable> GetNodeBatches()
            {
                yield return nodeTable;
                await Task.CompletedTask;
            }

            await dsNode.WriteBatches(GetNodeBatches(), append: true);

            // 4. Schema Discovery (GetColumnsAsync)
            var columns = (await dsNode.GetColumnsAsync()).ToList();
            Assert.Contains("_id", columns);
            Assert.Contains("_labels", columns);
            Assert.Contains("name", columns);
            Assert.Contains("city", columns);
            Assert.Contains("status", columns);

            // 5. Read Nodes
            var nodeBatches = await dsNode.ReadBatches().ToListAsync();
            Assert.Single(nodeBatches);
            var resultNodeTable = nodeBatches[0];
            Assert.Equal(2, resultNodeTable.Rows.Count);

            var rowAlice = resultNodeTable.Rows.FirstOrDefault(r => r["name"]?.ToString() == "Alice");
            Assert.NotNull(rowAlice);
            Assert.Equal("New York", rowAlice["city"]?.ToString());
            Assert.Equal("Active", rowAlice["status"]?.ToString());
            string aliceId = rowAlice["_id"]?.ToString() ?? "";
            Assert.NotEmpty(aliceId);

            var rowBob = resultNodeTable.Rows.FirstOrDefault(r => r["name"]?.ToString() == "Bob");
            Assert.NotNull(rowBob);
            Assert.Equal("San Francisco", rowBob["city"]?.ToString());
            Assert.Equal("Inactive", rowBob["status"]?.ToString());
            string bobId = rowBob["_id"]?.ToString() ?? "";
            Assert.NotEmpty(bobId);

            // 6. Write Relationships (EDGE_FRIEND_OF)
            var dsEdge = new Neo4jDataSource(ctx, _fixture.ConnectionString, "EDGE_FRIEND_OF", options);
            var edgeTable = new DataTable();
            edgeTable.SetColumns(new[] { "_from_id", "_to_id", "since", "closeness" });
            await edgeTable.AddRowAsync(new Row
            {
                ["_from_id"] = aliceId,
                ["_to_id"] = bobId,
                ["since"] = "2020",
                ["closeness"] = "best friends"
            });

            async IAsyncEnumerable<DataTable> GetEdgeBatches()
            {
                yield return edgeTable;
                await Task.CompletedTask;
            }

            await dsEdge.WriteBatches(GetEdgeBatches(), append: true);

            // 7. Read Relationships
            var edgeBatches = await dsEdge.ReadBatches().ToListAsync();
            Assert.Single(edgeBatches);
            var resultEdgeTable = edgeBatches[0];
            Assert.Single(resultEdgeTable.Rows);

            var relRow = resultEdgeTable.Rows[0];
            Assert.Equal(aliceId, relRow["_from_id"]?.ToString());
            Assert.Equal(bobId, relRow["_to_id"]?.ToString());
            Assert.Equal("2020", relRow["since"]?.ToString());
            Assert.Equal("best friends", relRow["closeness"]?.ToString());

            // 8. Raw SQL/Cypher pushdown
            var dsRaw = (IDatabaseSource)dsNode;
            var rawBatches = await dsRaw.ExecuteRawSql(
                "MATCH (a:CUSTOMER)-[r:FRIEND_OF]->(b:CUSTOMER) WHERE a.name = ?1 RETURN a.name AS fromName, b.name AS toName, r.closeness AS closeness",
                new object[] { "Alice" }).ToListAsync();

            Assert.Single(rawBatches);
            var rawTable = rawBatches[0];
            Assert.Single(rawTable.Rows);
            Assert.Equal("Alice", rawTable.Rows[0]["fromName"]?.ToString());
            Assert.Equal("Bob", rawTable.Rows[0]["toName"]?.ToString());
            Assert.Equal("best friends", rawTable.Rows[0]["closeness"]?.ToString());
        }

        [Fact]
        public async Task Neo4jDataSource_ConnectionString_DoesNotExposePassword()
        {
            var ctx = MakeContext();
            var options = MakeOptions();
            var ds = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_SECRET_CHECK", options);

            Assert.DoesNotContain(_fixture.Password, ds.ConnectionString);
            Assert.DoesNotContain(_fixture.Username + ":", ds.ConnectionString);

            var version = await ds.GetVersionAsync();
            Assert.Contains("Connected", version);
        }

        [Fact]
        public async Task Neo4jDataSource_WithTable_DisposeScopedSourceDoesNotCloseSharedDriver()
        {
            var ctx = MakeContext();
            var options = MakeOptions();
            var root = new Neo4jDataSource(ctx, _fixture.ConnectionString, null, options);
            await root.GetTablesAsync();

            var scoped = root.WithTable("NODE_DISPOSE_SCOPE");
            await scoped.DisposeAsync();

            var version = await root.GetVersionAsync();
            Assert.Contains("Connected", version);

            await root.DisposeAsync();
        }

        [Fact]
        public async Task Neo4jDataSource_ExecuteRawSql_WhenWhatIfSkipsMutatingCypher()
        {
            var normalCtx = MakeContext();
            var dryRunCtx = MakeContext(isWhatIf: true);
            var options = MakeOptions();
            var normal = new Neo4jDataSource(normalCtx, _fixture.ConnectionString, "NODE_WHATIF", options);
            var dryRun = new Neo4jDataSource(dryRunCtx, _fixture.ConnectionString, "NODE_WHATIF", options);

            await normal.TruncateAsync();
            await dryRun.ExecuteRawSql("CREATE (:WHATIF {name: 'should_not_exist'})").ToListAsync();

            var result = await normal.ExecuteRawSql("MATCH (n:WHATIF) RETURN count(n) AS count").ToListAsync();
            Assert.Single(result);
            Assert.Equal(0L, result[0].Rows[0]["count"]);
        }

        [Fact]
        public async Task Neo4jDataSource_WriteBatches_WithKeyColumnsMergesNodes()
        {
            var ctx = MakeContext();
            var options = MakeOptions(("KEY_COLUMNS", "customer_id"));
            var ds = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_KEYED_CUSTOMER", options);
            await ds.TruncateAsync();

            var first = new DataTable();
            first.SetColumns(new[] { "customer_id", "name" });
            await first.AddRowAsync(new Row { ["customer_id"] = "C001", ["name"] = "Alice" });

            var second = new DataTable();
            second.SetColumns(new[] { "customer_id", "name" });
            await second.AddRowAsync(new Row { ["customer_id"] = "C001", ["name"] = "Alice Updated" });

            await ds.WriteBatches(Batches(first), append: true);
            await ds.WriteBatches(Batches(second), append: true);

            var result = await ds.ExecuteRawSql("MATCH (n:KEYED_CUSTOMER {customer_id: 'C001'}) RETURN count(n) AS count, max(n.name) AS name").ToListAsync();
            Assert.Single(result);
            Assert.Equal(1L, result[0].Rows[0]["count"]);
            Assert.Equal("Alice Updated", result[0].Rows[0]["name"]);
        }

        [Fact]
        public async Task Neo4jDataSource_WriteBatches_WithEndpointKeysCreatesRelationship()
        {
            var ctx = MakeContext();
            var nodeOptions = MakeOptions(("KEY_COLUMNS", "customer_id"));
            var edgeOptions = MakeOptions(
                ("FROM_LABEL", "KEYED_EDGE_CUSTOMER"),
                ("TO_LABEL", "KEYED_EDGE_CUSTOMER"),
                ("FROM_KEY_COLUMN", "customer_id"),
                ("TO_KEY_COLUMN", "customer_id"),
                ("KEY_COLUMNS", "friendship_id"));
            var nodeDs = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_KEYED_EDGE_CUSTOMER", nodeOptions);
            var edgeDs = new Neo4jDataSource(ctx, _fixture.ConnectionString, "EDGE_KEYED_FRIEND_OF", edgeOptions);
            await nodeDs.TruncateAsync();

            var nodes = new DataTable();
            nodes.SetColumns(new[] { "customer_id", "name" });
            await nodes.AddRowAsync(new Row { ["customer_id"] = "C001", ["name"] = "Alice" });
            await nodes.AddRowAsync(new Row { ["customer_id"] = "C002", ["name"] = "Bob" });
            await nodeDs.WriteBatches(Batches(nodes), append: true);

            var edges = new DataTable();
            edges.SetColumns(new[] { "_from_key", "_to_key", "friendship_id", "since" });
            await edges.AddRowAsync(new Row { ["_from_key"] = "C001", ["_to_key"] = "C002", ["friendship_id"] = "F001", ["since"] = "2026" });
            await edgeDs.WriteBatches(Batches(edges), append: true);
            await edgeDs.WriteBatches(Batches(edges), append: true);

            var result = await nodeDs.ExecuteRawSql(
                "MATCH (:KEYED_EDGE_CUSTOMER {customer_id: 'C001'})-[r:KEYED_FRIEND_OF]->(:KEYED_EDGE_CUSTOMER {customer_id: 'C002'}) RETURN count(r) AS count, max(r.since) AS since")
                .ToListAsync();
            Assert.Single(result);
            Assert.Equal(1L, result[0].Rows[0]["count"]);
            Assert.Equal("2026", result[0].Rows[0]["since"]);
        }

        [Fact]
        public async Task Neo4jDataSource_WriteBatches_WithBacktickInLabelEscapesIdentifier()
        {
            var ctx = MakeContext();
            var options = MakeOptions();
            var ds = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_WE`IRD", options);
            await ds.TruncateAsync();

            var table = new DataTable();
            table.SetColumns(new[] { "name" });
            await table.AddRowAsync(new Row { ["name"] = "Escaped" });

            await ds.WriteBatches(Batches(table), append: true);

            var rows = await ds.ReadBatches().ToListAsync();
            Assert.Single(rows);
            Assert.Single(rows[0].Rows);
            Assert.Equal("Escaped", rows[0].Rows[0]["name"]);
        }

        [Fact]
        public async Task Neo4jDataSource_WriteBatches_ReplaceRollbackPreservesExistingDataOnFailure()
        {
            var ctx = MakeContext();
            var options = MakeOptions(("KEY_COLUMNS", "customer_id"));
            var ds = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_ATOMIC_CUSTOMER", options);
            await ds.TruncateAsync();

            var existing = new DataTable();
            existing.SetColumns(new[] { "customer_id", "name" });
            await existing.AddRowAsync(new Row { ["customer_id"] = "C001", ["name"] = "Existing" });
            await ds.WriteBatches(Batches(existing), append: true);

            var replacement = new DataTable();
            replacement.SetColumns(new[] { "customer_id", "name" });
            await replacement.AddRowAsync(new Row { ["customer_id"] = "C002", ["name"] = "Replacement" });

            var invalid = new DataTable();
            invalid.SetColumns(new[] { "customer_id", "name" });
            await invalid.AddRowAsync(new Row { ["customer_id"] = null, ["name"] = "Invalid" });

            await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(Batches(replacement, invalid), append: false));

            var result = await ds.ExecuteRawSql("MATCH (n:ATOMIC_CUSTOMER) RETURN count(n) AS count, max(n.name) AS name").ToListAsync();
            Assert.Single(result);
            Assert.Equal(1L, result[0].Rows[0]["count"]);
            Assert.Equal("Existing", result[0].Rows[0]["name"]);
        }
    }
}
