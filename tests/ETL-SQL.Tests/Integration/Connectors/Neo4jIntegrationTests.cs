using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Neo4j;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

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

        private static async IAsyncEnumerable<DataTable> GenerateNodeBatches(int rowCount, int batchSize)
        {
            for (int offset = 0; offset < rowCount; offset += batchSize)
            {
                var table = new DataTable();
                table.SetColumns(new[] { "customer_id", "name", "cohort", "score" });

                var end = Math.Min(rowCount, offset + batchSize);
                for (int i = offset; i < end; i++)
                {
                    await table.AddRowAsync(new Row
                    {
                        ["customer_id"] = $"C{i:D8}",
                        ["name"] = $"Customer {i}",
                        ["cohort"] = i % 10,
                        ["score"] = i % 1000
                    });
                }

                yield return table;
            }
        }

        private static async IAsyncEnumerable<DataTable> GenerateEdgeBatches(int rowCount, int batchSize)
        {
            for (int offset = 0; offset < rowCount; offset += batchSize)
            {
                var table = new DataTable();
                table.SetColumns(new[] { "_from_key", "_to_key", "edge_id", "weight" });

                var end = Math.Min(rowCount, offset + batchSize);
                for (int i = offset; i < end; i++)
                {
                    await table.AddRowAsync(new Row
                    {
                        ["_from_key"] = $"C{i:D8}",
                        ["_to_key"] = $"C{((i + 1) % rowCount):D8}",
                        ["edge_id"] = $"E{i:D8}",
                        ["weight"] = i % 100
                    });
                }

                yield return table;
            }
        }

        private static int Neo4jScaleRows()
        {
            var raw = Environment.GetEnvironmentVariable("NEO4J_SCALE_ROWS");
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows) && rows > 0
                ? rows
                : 0;
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
        public async Task Neo4jEngine_CreateConnectionWithOptions_LoadsKeyedNodes()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = $@"
CREATE CONNECTION graph AS NEO4J(
    HOST = '127.0.0.1',
    PORT = {_fixture.Port},
    USER = '{_fixture.Username}',
    PASSWORD = '{_fixture.Password}',
    DATABASE = 'neo4j',
    KEY_COLUMNS = 'customer_id'
);

EXECUTE graph
BEGIN
    MATCH (n:ENGINE_CUSTOMER) DETACH DELETE n
END;

CREATE TABLE #stage (customer_id VARCHAR, name VARCHAR);
INSERT INTO #stage VALUES ('E001', 'Engine Alice');
INSERT INTO graph.NODE_ENGINE_CUSTOMER (customer_id, name)
SELECT customer_id, name FROM #stage;

CREATE TABLE #stage2 (customer_id VARCHAR, name VARCHAR);
INSERT INTO #stage2 VALUES ('E001', 'Engine Alice Updated');
INSERT INTO graph.NODE_ENGINE_CUSTOMER (customer_id, name)
SELECT customer_id, name FROM #stage2;

SELECT customer_id, name
FROM graph.NODE_ENGINE_CUSTOMER
WHERE customer_id = 'E001';
";

            await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            Assert.NotNull(eval.LastResult);
            Assert.Single(eval.LastResult.Rows);
            Assert.Equal("E001", eval.LastResult.Rows[0]["customer_id"]?.ToString());
            Assert.Equal("Engine Alice Updated", eval.LastResult.Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task Neo4jEngine_LoadSessionState_RehydratesConnectionWithoutExposingPassword()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var state = new SessionState
            {
                SessionId = "neo4j-session-rehydrate",
                Connections = new List<ETL_SQL.Core.Data.ConnectionInfo>
                {
                    new()
                    {
                        Name = "graph",
                        Type = "NEO4J",
                        ConnectionString = _fixture.ConnectionString,
                        Options = MakeOptions()
                    }
                }
            };

            await eval.LoadSessionState(state);

            var ds = Assert.IsType<Neo4jDataSource>(eval.Connections["graph"]);
            Assert.DoesNotContain(_fixture.Password, ds.ConnectionString);
            Assert.Equal("********", ((IDataSource)ds).GetConfig()["PASSWORD"]);

            var batches = await ds.ExecuteRawSql("RETURN 1 AS ok").ToListAsync();
            Assert.Single(batches);
            Assert.Equal(1L, batches[0].Rows[0]["ok"]);
        }

        [Fact]
        public async Task Neo4jEngine_TransactionRollback_RollsBackGraphWrites()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = $@"
CREATE CONNECTION graph AS NEO4J(
    HOST = '127.0.0.1',
    PORT = {_fixture.Port},
    USER = '{_fixture.Username}',
    PASSWORD = '{_fixture.Password}',
    DATABASE = 'neo4j',
    KEY_COLUMNS = 'customer_id'
);

EXECUTE graph
BEGIN
    MATCH (n:TX_ROLLBACK_CUSTOMER) DETACH DELETE n
END;

BEGIN TRANSACTION;
CREATE TABLE #tx_customer (customer_id VARCHAR, name VARCHAR);
INSERT INTO #tx_customer VALUES ('TX001', 'Rollback Alice');
INSERT INTO graph.NODE_TX_ROLLBACK_CUSTOMER (customer_id, name)
SELECT customer_id, name FROM #tx_customer;
ROLLBACK TRANSACTION;
";

            await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            var graph = Assert.IsType<Neo4jDataSource>(eval.Connections["graph"]);
            var result = await graph.ExecuteRawSql("MATCH (n:TX_ROLLBACK_CUSTOMER) RETURN count(n) AS count").ToListAsync();
            Assert.Single(result);
            Assert.Equal(0L, result[0].Rows[0]["count"]);
        }

        [Fact]
        public async Task Neo4jEngine_TransactionCommit_CommitsGraphWrites()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = $@"
CREATE CONNECTION graph AS NEO4J(
    HOST = '127.0.0.1',
    PORT = {_fixture.Port},
    USER = '{_fixture.Username}',
    PASSWORD = '{_fixture.Password}',
    DATABASE = 'neo4j',
    KEY_COLUMNS = 'customer_id'
);

EXECUTE graph
BEGIN
    MATCH (n:TX_COMMIT_CUSTOMER) DETACH DELETE n
END;

BEGIN TRANSACTION;
CREATE TABLE #tx_customer (customer_id VARCHAR, name VARCHAR);
INSERT INTO #tx_customer VALUES ('TX001', 'Commit Alice');
INSERT INTO graph.NODE_TX_COMMIT_CUSTOMER (customer_id, name)
SELECT customer_id, name FROM #tx_customer;
COMMIT TRANSACTION;
";

            await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            var graph = Assert.IsType<Neo4jDataSource>(eval.Connections["graph"]);
            var result = await graph.ExecuteRawSql("MATCH (n:TX_COMMIT_CUSTOMER) RETURN count(n) AS count, max(n.name) AS name").ToListAsync();
            Assert.Single(result);
            Assert.Equal(1L, result[0].Rows[0]["count"]);
            Assert.Equal("Commit Alice", result[0].Rows[0]["name"]);
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
        public async Task Neo4jDataSource_TruncateAsync_WithNodeTableOnlyDeletesThatLabel()
        {
            var ctx = MakeContext();
            var options = MakeOptions();
            var root = new Neo4jDataSource(ctx, _fixture.ConnectionString, null, options);
            var keep = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_TRUNCATE_KEEP", options);
            var drop = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_TRUNCATE_DROP", options);
            await root.TruncateAsync();

            var keepTable = new DataTable();
            keepTable.SetColumns(new[] { "name" });
            await keepTable.AddRowAsync(new Row { ["name"] = "Keep" });
            await keep.WriteBatches(Batches(keepTable), append: true);

            var dropTable = new DataTable();
            dropTable.SetColumns(new[] { "name" });
            await dropTable.AddRowAsync(new Row { ["name"] = "Drop" });
            await drop.WriteBatches(Batches(dropTable), append: true);

            await drop.TruncateAsync();

            var result = await root.ExecuteRawSql(@"
MATCH (n)
WHERE n:TRUNCATE_KEEP OR n:TRUNCATE_DROP
RETURN labels(n)[0] AS label, count(n) AS count
ORDER BY label").ToListAsync();

            Assert.Single(result);
            Assert.Single(result[0].Rows);
            Assert.Equal("TRUNCATE_KEEP", result[0].Rows[0]["label"]);
            Assert.Equal(1L, result[0].Rows[0]["count"]);
        }

        [Fact]
        public async Task Neo4jDataSource_GetColumnsAsync_UnionsSparsePropertiesWithinSample()
        {
            var ctx = MakeContext();
            var options = MakeOptions(("SCHEMA_SAMPLE_SIZE", "10"));
            var ds = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_SPARSE_SCHEMA", options);
            await ds.TruncateAsync();

            var table = new DataTable();
            table.SetColumns(new[] { "name", "late_property" });
            await table.AddRowAsync(new Row { ["name"] = "First", ["late_property"] = null });
            await table.AddRowAsync(new Row { ["name"] = "Second", ["late_property"] = "present" });
            await ds.WriteBatches(Batches(table), append: true);

            var columns = (await ds.GetColumnsAsync()).ToList();
            Assert.Contains("name", columns);
            Assert.Contains("late_property", columns);

            var rows = await ds.ReadBatches().ToListAsync();
            Assert.Single(rows);
            Assert.Contains(rows[0].Rows, r => r["late_property"]?.ToString() == "present");
        }

        [Fact]
        public async Task Neo4jDataSource_GetColumnsAsync_WithZeroSampleScansAllProperties()
        {
            var ctx = MakeContext();
            var options = MakeOptions(("SCHEMA_SAMPLE_SIZE", "0"));
            var ds = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_FULL_SCHEMA", options);
            await ds.TruncateAsync();

            var table = new DataTable();
            table.SetColumns(new[] { "name", "rare_property" });
            await table.AddRowAsync(new Row { ["name"] = "First", ["rare_property"] = null });
            await table.AddRowAsync(new Row { ["name"] = "Second", ["rare_property"] = "present" });
            await ds.WriteBatches(Batches(table), append: true);

            var columns = (await ds.GetColumnsAsync()).ToList();

            Assert.Contains("name", columns);
            Assert.Contains("rare_property", columns);
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
        public async Task Neo4jDataSource_ExecuteRawSql_WhenWhatIfSkipsArbitraryCall()
        {
            var dryRunCtx = MakeContext(isWhatIf: true);
            var options = MakeOptions();
            var dryRun = new Neo4jDataSource(dryRunCtx, _fixture.ConnectionString, "NODE_WHATIF_CALL", options);

            var skipped = await dryRun.ExecuteRawSql("CALL apoc.create.node(['WHATIF_CALL'], {name: 'skip'}) YIELD node RETURN node").ToListAsync();
            Assert.Empty(skipped);

            var allowed = await dryRun.ExecuteRawSql("CALL db.labels() YIELD label RETURN label").ToListAsync();
            Assert.NotNull(allowed);
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
        public async Task Neo4jDataSource_WriteBatches_WithMissingEndpointKeyThrowsByDefault()
        {
            var ctx = MakeContext();
            var options = MakeOptions(
                ("FROM_LABEL", "STRICT_EDGE_CUSTOMER"),
                ("TO_LABEL", "STRICT_EDGE_CUSTOMER"),
                ("FROM_KEY_COLUMN", "customer_id"),
                ("TO_KEY_COLUMN", "customer_id"));
            var ds = new Neo4jDataSource(ctx, _fixture.ConnectionString, "EDGE_STRICT_FRIEND_OF", options);
            await ds.TruncateAsync();

            var edges = new DataTable();
            edges.SetColumns(new[] { "_from_key", "_to_key", "since" });
            await edges.AddRowAsync(new Row { ["_from_key"] = "C001", ["_to_key"] = null, ["since"] = "2026" });

            await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(Batches(edges), append: true));
        }

        [Fact]
        public async Task Neo4jDataSource_WriteBatches_WithUnmatchedEndpointThrowsByDefault()
        {
            var ctx = MakeContext();
            var nodeOptions = MakeOptions(("KEY_COLUMNS", "customer_id"));
            var edgeOptions = MakeOptions(
                ("FROM_LABEL", "STRICT_MATCH_CUSTOMER"),
                ("TO_LABEL", "STRICT_MATCH_CUSTOMER"),
                ("FROM_KEY_COLUMN", "customer_id"),
                ("TO_KEY_COLUMN", "customer_id"));
            var nodeDs = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_STRICT_MATCH_CUSTOMER", nodeOptions);
            var edgeDs = new Neo4jDataSource(ctx, _fixture.ConnectionString, "EDGE_STRICT_MATCH_REL", edgeOptions);
            await nodeDs.TruncateAsync();
            await edgeDs.TruncateAsync();

            var nodes = new DataTable();
            nodes.SetColumns(new[] { "customer_id", "name" });
            await nodes.AddRowAsync(new Row { ["customer_id"] = "C001", ["name"] = "Alice" });
            await nodeDs.WriteBatches(Batches(nodes), append: true);

            var edges = new DataTable();
            edges.SetColumns(new[] { "_from_key", "_to_key", "since" });
            await edges.AddRowAsync(new Row { ["_from_key"] = "C001", ["_to_key"] = "MISSING", ["since"] = "2026" });

            await Assert.ThrowsAsync<ExecutionException>(() => edgeDs.WriteBatches(Batches(edges), append: true));
        }

        [Fact]
        public async Task Neo4jDataSource_WriteBatches_WithSkipMissingEndpointsAllowsDroppedEdgeRows()
        {
            var ctx = MakeContext();
            var options = MakeOptions(
                ("FROM_LABEL", "SKIP_EDGE_CUSTOMER"),
                ("TO_LABEL", "SKIP_EDGE_CUSTOMER"),
                ("FROM_KEY_COLUMN", "customer_id"),
                ("TO_KEY_COLUMN", "customer_id"),
                ("SKIP_MISSING_ENDPOINTS", "TRUE"));
            var ds = new Neo4jDataSource(ctx, _fixture.ConnectionString, "EDGE_SKIP_MISSING_REL", options);
            await ds.TruncateAsync();

            var edges = new DataTable();
            edges.SetColumns(new[] { "_from_key", "_to_key", "since" });
            await edges.AddRowAsync(new Row { ["_from_key"] = "C001", ["_to_key"] = null, ["since"] = "2026" });

            await ds.WriteBatches(Batches(edges), append: true);

            var result = await ds.ExecuteRawSql("MATCH ()-[r:SKIP_MISSING_REL]->() RETURN count(r) AS count").ToListAsync();
            Assert.Single(result);
            Assert.Equal(0L, result[0].Rows[0]["count"]);
        }

        [Fact]
        public async Task Neo4jDataSource_WriteBatches_NormalizesUnsupportedPropertyValues()
        {
            var ctx = MakeContext();
            var options = MakeOptions(("KEY_COLUMNS", "customer_id"));
            var ds = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_VALUE_NORMALIZATION", options);
            await ds.TruncateAsync();

            var createdAt = new DateTime(2026, 6, 5, 12, 30, 0, DateTimeKind.Utc);
            var table = new DataTable();
            table.SetColumns(new[] { "customer_id", "created_at", "metadata", "optional" });
            await table.AddRowAsync(new Row
            {
                ["customer_id"] = "C001",
                ["created_at"] = createdAt,
                ["metadata"] = new Dictionary<string, object?> { ["tier"] = "gold", ["score"] = 10 },
                ["optional"] = DBNull.Value
            });

            await ds.WriteBatches(Batches(table), append: true);

            var result = await ds.ExecuteRawSql(@"
MATCH (n:VALUE_NORMALIZATION {customer_id: 'C001'})
RETURN n.created_at AS created_at, n.metadata AS metadata, n.optional AS optional").ToListAsync();

            Assert.Single(result);
            Assert.Equal(createdAt.ToString("O"), result[0].Rows[0]["created_at"]);
            Assert.Contains("\"tier\":\"gold\"", result[0].Rows[0]["metadata"]?.ToString());
            Assert.Null(result[0].Rows[0]["optional"]);
        }

        [Fact]
        [Trait("Category", "ScaleCertification")]
        [Trait("Tier", "Provider")]
        public async Task Neo4jDataSource_Scale_BatchedKeyedNodeAndEdgeLoad()
        {
            var rows = Neo4jScaleRows();
            if (rows == 0)
            {
                Console.WriteLine("SKIP: Set NEO4J_SCALE_ROWS to run Neo4j graph scale certification.");
                return;
            }

            const int batchSize = 1000;
            var ctx = MakeContext();
            var nodeOptions = MakeOptions(("KEY_COLUMNS", "customer_id"));
            var edgeOptions = MakeOptions(
                ("FROM_LABEL", "SCALE_CUSTOMER"),
                ("TO_LABEL", "SCALE_CUSTOMER"),
                ("FROM_KEY_COLUMN", "customer_id"),
                ("TO_KEY_COLUMN", "customer_id"),
                ("KEY_COLUMNS", "edge_id"));
            var nodeDs = new Neo4jDataSource(ctx, _fixture.ConnectionString, "NODE_SCALE_CUSTOMER", nodeOptions);
            var edgeDs = new Neo4jDataSource(ctx, _fixture.ConnectionString, "EDGE_SCALE_LINK", edgeOptions);

            await edgeDs.TruncateAsync();
            await nodeDs.TruncateAsync();
            await nodeDs.ExecuteRawSql(
                "CREATE INDEX scale_customer_id IF NOT EXISTS FOR (n:SCALE_CUSTOMER) ON (n.customer_id)")
                .ToListAsync();
            await nodeDs.ExecuteRawSql("CALL db.awaitIndexes()").ToListAsync();

            var startMemory = GC.GetTotalMemory(forceFullCollection: true);
            var sw = Stopwatch.StartNew();

            await nodeDs.WriteBatches(GenerateNodeBatches(rows, batchSize), append: true);
            await edgeDs.WriteBatches(GenerateEdgeBatches(rows, batchSize), append: true);

            sw.Stop();
            var endMemory = GC.GetTotalMemory(forceFullCollection: true);

            var result = await nodeDs.ExecuteRawSql(@"
MATCH (n:SCALE_CUSTOMER)
WITH count(n) AS nodeCount, sum(n.score) AS scoreSum
MATCH ()-[r:SCALE_LINK]->()
RETURN nodeCount, scoreSum, count(r) AS edgeCount, sum(r.weight) AS weightSum").ToListAsync();

            Assert.Single(result);
            var row = result[0].Rows[0];
            var expectedScoreSum = Enumerable.Range(0, rows).Sum(i => (long)(i % 1000));
            var expectedWeightSum = Enumerable.Range(0, rows).Sum(i => (long)(i % 100));
            Assert.Equal((long)rows, Convert.ToInt64(row["nodeCount"], CultureInfo.InvariantCulture));
            Assert.Equal((long)rows, Convert.ToInt64(row["edgeCount"], CultureInfo.InvariantCulture));
            Assert.Equal(expectedScoreSum, Convert.ToInt64(row["scoreSum"], CultureInfo.InvariantCulture));
            Assert.Equal(expectedWeightSum, Convert.ToInt64(row["weightSum"], CultureInfo.InvariantCulture));

            var metric = new
            {
                scenario = "Neo4j Batched Keyed Node and Edge Load",
                rowCount = rows,
                elapsedMs = sw.ElapsedMilliseconds,
                spillBytes = 0,
                resultRows = rows * 2,
                peakManagedMemoryMB = Math.Round(Math.Max(0, endMemory - startMemory) / 1024d / 1024d, 2),
                memoryBoundMB = 512,
                passed = true
            };
            Console.WriteLine("CERT_METRIC:" + JsonSerializer.Serialize(metric));
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
