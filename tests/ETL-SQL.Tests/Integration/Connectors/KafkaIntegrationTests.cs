using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Confluent.Kafka;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Kafka;
using ETL_SQL.Services;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Collection("Kafka collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "KAFKA")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class KafkaIntegrationTests
    {
        private readonly KafkaFixture _fixture;

        public KafkaIntegrationTests(KafkaFixture fixture)
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
        public async Task KafkaConnector_GetVersionAsync_Success()
        {
            var ctx = MakeContext();
            var connector = new KafkaConnector();

            var version = await connector.GetVersionAsync(ctx, _fixture.BootstrapServers);
            Assert.Contains("Connected", version);
            Assert.Contains("Kafka Connector", version);
        }

        [Fact]
        public async Task KafkaConnector_GetVersionAsync_InvalidServer_ThrowsException()
        {
            var ctx = MakeContext();
            var connector = new KafkaConnector();

            // Pointing to a dummy non-existent local port with short timeout to fail fast
            string invalidServers = "127.0.0.1:9099";
            var options = new Dictionary<string, string>
            {
                { "BOOTSTRAP_SERVERS", invalidServers }
            };

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                connector.GetVersionAsync(ctx, invalidServers));
            Assert.Contains("Kafka", ex.Message);
        }

        [Fact]
        public async Task KafkaDataSource_ProduceAndConsume_RoundTrip_Success()
        {
            var ctx = MakeContext();
            var topic = $"topic-{Guid.NewGuid():N}";
            
            var options = new Dictionary<string, string>
            {
                { "BOOTSTRAP_SERVERS", _fixture.BootstrapServers },
                { "TOPIC", topic },
                { "GROUP_ID", $"test-group-{Guid.NewGuid():N}" },
                { "AUTO_OFFSET_RESET", "Earliest" },
                { "TIMEOUT_MS", "3000" } // Poll up to 3 seconds for message
            };

            var ds = new KafkaDataSource(ctx, _fixture.BootstrapServers, topic, options);

            // 1. Produce (WriteBatches)
            var table = new DataTable();
            table.SetColumns(new[] { "Key", "Value" });
            await table.AddRowAsync(new Row { ["Key"] = "alert-101", ["Value"] = "Host down event" });
            await table.AddRowAsync(new Row { ["Key"] = "alert-102", ["Value"] = "High memory event" });

            async IAsyncEnumerable<DataTable> GetBatches()
            {
                yield return table;
                await Task.CompletedTask;
            }

            await ds.WriteBatches(GetBatches(), append: true);

            // 2. Consume (ReadBatches)
            var batches = await ds.ReadBatches().ToListAsync();
            
            Assert.NotEmpty(batches);
            var resultTable = batches[0];
            Assert.Equal(2, resultTable.Rows.Count);

            var row1 = resultTable.Rows.FirstOrDefault(r => r["Key"]?.ToString() == "alert-101");
            Assert.NotNull(row1);
            Assert.Equal("Host down event", row1["Value"]?.ToString());

            var row2 = resultTable.Rows.FirstOrDefault(r => r["Key"]?.ToString() == "alert-102");
            Assert.NotNull(row2);
            Assert.Equal("High memory event", row2["Value"]?.ToString());
        }
    }
}
