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

        private IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        [Fact]
        public async Task Neo4jConnector_GetVersionAsync_Success()
        {
            var ctx = MakeContext();
            var connector = new Neo4jConnector();

            // Build a connection string using credentials
            var options = new Dictionary<string, string>
            {
                { "USER", _fixture.Username },
                { "PASSWORD", _fixture.Password }
            };
            string connStr = connector.BuildConnectionString(options);
            // Replace port placeholder if any
            connStr = connStr.Replace("localhost:7687", $"127.0.0.1:{_fixture.Port}");

            var version = await connector.GetVersionAsync(ctx, connStr);
            Assert.Contains("Connected", version);
            Assert.Contains("Neo4j Connector", version);
        }

        [Fact]
        public async Task Neo4jConnector_GetVersionAsync_InvalidPassword_ThrowsException()
        {
            var ctx = MakeContext();
            var connector = new Neo4jConnector();

            string invalidConnStr = $"bolt://127.0.0.1:{_fixture.Port}";
            var options = new Dictionary<string, string>
            {
                { "USER", _fixture.Username },
                { "PASSWORD", "wrong_password" },
                { "TIMEOUT_SECONDS", "2" }
            };
            invalidConnStr = connector.BuildConnectionString(options).Replace("localhost:7687", $"127.0.0.1:{_fixture.Port}");

            await Assert.ThrowsAsync<ExecutionException>(() =>
                connector.GetVersionAsync(ctx, invalidConnStr));
        }

        [Fact]
        public async Task Neo4jDataSource_Lifecycle_WriteAndRead_NodesAndEdges_Success()
        {
            var ctx = MakeContext();
            var options = new Dictionary<string, string>
            {
                { "USER", _fixture.Username },
                { "PASSWORD", _fixture.Password },
                { "DATABASE", "neo4j" }
            };

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
    }
}
