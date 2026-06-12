using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Kafka;
using ETL_SQL.Connectors.Mongodb;
using ETL_SQL.Data;
using ETL_SQL.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Category", "Connectors")]
    public class MongodbAndKafkaConnectorTests
    {
        private Mock<IExecutionContext> CreateMockContext()
        {
            var mockLogger = new Mock<ILogger>();
            var securityService = new SecurityService(mockLogger.Object);
            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(c => c.SecurityService).Returns(securityService);
            mockContext.Setup(c => c.Logger).Returns(mockLogger.Object);
            mockContext.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
            return mockContext;
        }

        // ── MongoDB Connector Tests ──────────────────────────────────────────

        [Fact]
        public void MongodbConnector_Metadata_IsCorrect()
        {
            var connector = new MongodbConnector();
            Assert.Equal("MONGODB", connector.Name);
            Assert.Contains("MONGO", connector.Aliases);
            Assert.NotEmpty(connector.GetHelp());
            Assert.NotEmpty(connector.GetSupportedOptions());
            Assert.DoesNotContain("URI", connector.GetSupportedOptions().Keys);
            Assert.DoesNotContain("DB", connector.GetSupportedOptions().Keys);
            Assert.DoesNotContain("SERVER", connector.GetSupportedOptions().Keys);
            Assert.DoesNotContain("UID", connector.GetSupportedOptions().Keys);
        }

        [Fact]
        public void MongodbConnector_CreateDataSource_Success()
        {
            var mockContext = CreateMockContext();
            var connector = new MongodbConnector();
            var options = new Dictionary<string, string>
            {
                { "DATABASE", "my_db" },
                { "COLLECTION", "my_collection" }
            };

            var dataSource = (MongodbDataSource)connector.CreateDataSource(mockContext.Object, "mongodb://localhost:27017/my_db", options);
            Assert.Equal("my_collection", dataSource.Path);
        }

        [Fact]
        public async Task MongodbDataSource_ReadBatches_Success()
        {
            var mockContext = CreateMockContext();
            var mockClient = new Mock<IMongoClient>();
            var mockDatabase = new Mock<IMongoDatabase>();
            var mockCollection = new Mock<IMongoCollection<BsonDocument>>();
            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();

            var doc1 = new BsonDocument
            {
                { "id", 1 },
                { "name", "Alice" },
                { "address", new BsonDocument { { "city", "New York" } } },
                { "hobbies", BsonNull.Value }
            };
            var doc2 = new BsonDocument
            {
                { "id", 2 },
                { "name", "Bob" },
                { "hobbies", new BsonArray { "reading", "gaming" } }
            };

            mockCollection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var cursor = new Mock<IAsyncCursor<BsonDocument>>();
                    var count = 0;
                    cursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(() =>
                        {
                            count++;
                            return count == 1;
                        });
                    cursor.Setup(c => c.Current).Returns(() => new[] { doc1, doc2 });
                    return cursor.Object;
                });

            mockDatabase.Setup(d => d.GetCollection<BsonDocument>("users", It.IsAny<MongoCollectionSettings>()))
                .Returns(mockCollection.Object);
            mockClient.Setup(c => c.GetDatabase("my_db", It.IsAny<MongoDatabaseSettings>()))
                .Returns(mockDatabase.Object);

            var dataSource = new MongodbDataSource(mockContext.Object, "mongodb://localhost:27017", "my_db", "users", null, mockClient.Object);

            // Retrieve columns (triggering schema discovery Setup)
            var columns = (await dataSource.GetColumnsAsync()).ToList();
            Assert.Contains("id", columns);
            Assert.Contains("name", columns);
            Assert.Contains("address", columns);
            Assert.Contains("hobbies", columns);

            var batches = new List<DataTable>();
            await foreach (var batch in dataSource.ReadBatches())
            {
                batches.Add(batch);
            }

            Assert.Single(batches);
            Assert.Equal(2, batches[0].Rows.Count);
            Assert.Equal(1, Convert.ToInt32(batches[0].Rows[0]["id"]));
            Assert.Equal("Alice", batches[0].Rows[0]["name"]);

            // Nested document serialized to JSON string
            var addressJson = batches[0].Rows[0]["address"]?.ToString();
            Assert.Contains("\"city\" : \"New York\"", addressJson);

            // Nested array serialized to JSON string
            var hobbiesJson = batches[0].Rows[1]["hobbies"]?.ToString();
            Assert.Contains("\"reading\"", hobbiesJson);
        }

        [Fact]
        public async Task MongodbDataSource_WriteBatches_Success()
        {
            var mockContext = CreateMockContext();
            var mockClient = new Mock<IMongoClient>();
            var mockDatabase = new Mock<IMongoDatabase>();
            var mockCollection = new Mock<IMongoCollection<BsonDocument>>();

            mockDatabase.Setup(d => d.GetCollection<BsonDocument>("users", It.IsAny<MongoCollectionSettings>()))
                .Returns(mockCollection.Object);
            mockClient.Setup(c => c.GetDatabase("my_db", It.IsAny<MongoDatabaseSettings>()))
                .Returns(mockDatabase.Object);

            var table = new DataTable();
            table.SetColumns(new[] { "id", "name", "address" });
            await table.AddRowAsync(new Row
            {
                ["id"] = 1,
                ["name"] = "Alice",
                ["address"] = "{\"city\":\"New York\"}"
            });

            async IAsyncEnumerable<DataTable> GetBatches()
            {
                yield return table;
                await Task.CompletedTask;
            }

            var dataSource = new MongodbDataSource(mockContext.Object, "mongodb://localhost:27017", "my_db", "users", null, mockClient.Object);
            await dataSource.WriteBatches(GetBatches(), append: true);

            mockCollection.Verify(c => c.InsertManyAsync(
                It.Is<IEnumerable<BsonDocument>>(docs =>
                    docs.Count() == 1 &&
                    docs.First()["id"].AsInt32 == 1 &&
                    docs.First()["name"].AsString == "Alice" &&
                    docs.First()["address"].AsBsonDocument["city"].AsString == "New York"
                ),
                It.IsAny<InsertManyOptions>(),
                It.IsAny<CancellationToken>()
            ), Times.Once);
        }

        // ── Kafka Connector Tests ────────────────────────────────────────────

        [Fact]
        public void KafkaConnector_Metadata_IsCorrect()
        {
            var connector = new KafkaConnector();
            Assert.Equal("KAFKA", connector.Name);
            Assert.Empty(connector.Aliases);
            Assert.NotEmpty(connector.GetHelp());
            Assert.NotEmpty(connector.GetSupportedOptions());
            Assert.DoesNotContain("SERVERS", connector.GetSupportedOptions().Keys);
        }

        [Fact]
        public void KafkaConnector_EgressSecurity_ValidatesHost()
        {
            var mockLogger = new Mock<ILogger>();
            var securityService = new SecurityService(mockLogger.Object)
            {
                IsTestMode = false
            };
            securityService.AllowedHosts.Clear();

            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(c => c.SecurityService).Returns(securityService);
            mockContext.Setup(c => c.Logger).Returns(mockLogger.Object);

            var options = new Dictionary<string, string>
            {
                { "BOOTSTRAP_SERVERS", "kafka-broker-1.prod:9092,kafka-broker-2.prod:9092" },
                { "TOPIC", "alerts" }
            };

            var connector = new KafkaConnector();

            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                connector.CreateDataSource(mockContext.Object, "kafka-broker-1.prod:9092,kafka-broker-2.prod:9092", options)
            );
        }

        [Fact]
        public async Task KafkaDataSource_ReadBatches_Success()
        {
            var mockContext = CreateMockContext();
            var mockConsumer = new Mock<IConsumer<string, string>>();

            var message = new Message<string, string>
            {
                Key = "key-1",
                Value = "value-1",
                Timestamp = new Timestamp(new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc))
            };

            var consumeResult = new ConsumeResult<string, string>
            {
                Topic = "alerts",
                Partition = new Partition(1),
                Offset = new Offset(555L),
                Message = message
            };

            var consumeCount = 0;
            mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
                .Returns(() =>
                {
                    consumeCount++;
                    if (consumeCount == 1) return consumeResult;
                    return null;
                });

            var options = new Dictionary<string, string>
            {
                { "TIMEOUT_MS", "300" }
            };

            var dataSource = new KafkaDataSource(mockContext.Object, "localhost:9092", "alerts", options, mockConsumer.Object);

            var batches = new List<DataTable>();
            await foreach (var batch in dataSource.ReadBatches())
            {
                batches.Add(batch);
            }

            Assert.Single(batches);
            Assert.Single(batches[0].Rows);
            Assert.Equal(1, Convert.ToInt32(batches[0].Rows[0]["Partition"]));
            Assert.Equal(555L, Convert.ToInt64(batches[0].Rows[0]["Offset"]));
            Assert.Equal("key-1", batches[0].Rows[0]["Key"]);
            Assert.Equal("value-1", batches[0].Rows[0]["Value"]);
            Assert.Equal(new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc), batches[0].Rows[0]["Timestamp"]);
        }

        [Fact]
        public async Task KafkaDataSource_WriteBatches_Success()
        {
            var mockContext = CreateMockContext();
            var mockProducer = new Mock<IProducer<string, string>>();

            mockProducer.Setup(p => p.ProduceAsync("alerts", It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeliveryResult<string, string>());

            var table = new DataTable();
            table.SetColumns(new[] { "Key", "Value" });
            await table.AddRowAsync(new Row
            {
                ["Key"] = "alert-id-99",
                ["Value"] = "High CPU load alert"
            });

            async IAsyncEnumerable<DataTable> GetBatches()
            {
                yield return table;
                await Task.CompletedTask;
            }

            var dataSource = new KafkaDataSource(mockContext.Object, "localhost:9092", "alerts", null, null, mockProducer.Object);
            await dataSource.WriteBatches(GetBatches(), append: true);

            mockProducer.Verify(p => p.ProduceAsync(
                "alerts",
                It.Is<Message<string, string>>(m =>
                    m.Key == "alert-id-99" &&
                    m.Value == "High CPU load alert"
                ),
                It.IsAny<CancellationToken>()
            ), Times.Once);
        }
    }
}
