using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using Moq;

namespace ETL_SQL.Tests.Connectors
{
    public class ConnectionTelemetryTests
    {
        [Fact]
        public async Task CreateConnection_PreviewReadFailure_LogsWarningAndKeepsConnection()
        {
            var connector = new PreviewFailureConnector();
            var registry = new Mock<IConnectorRegistry>();
            var logger = new Mock<ILogger>();
            var context = new SystemExecutionContext();

            registry.Setup(r => r.GetConnector("TELEMETRY_TEST")).Returns(connector);

            var handler = new CreateConnectionStatementHandler(registry.Object, logger.Object);
            var statement = new CreateConnectionStatement("telemetry_conn", "TELEMETRY_TEST");

            await handler.Execute(statement, context);

            Assert.True(context.Connections.ContainsKey("telemetry_conn"));
            Assert.NotNull(context.LastResult);
            logger.Verify(
                l => l.Warning(
                    It.Is<string>(s => s.Contains("preview data not available", StringComparison.OrdinalIgnoreCase)),
                    It.Is<object?[]>(args => args.Any(a => a != null && string.Equals(a.ToString(), "telemetry_conn", StringComparison.OrdinalIgnoreCase)))),
                Times.Once);
        }

        private sealed class PreviewFailureConnector : IConnector
        {
            public string Name => "TELEMETRY_TEST";
            public IReadOnlyList<string> Aliases => Array.Empty<string>();
            public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("1.0");
            public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);
            public string GetHelp() => "Preview failure test connector.";
            public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null) => new PreviewFailureDataSource();
            public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult<IEnumerable<string>>(["id"]);
            public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        }

        private sealed class PreviewFailureDataSource : IDataSource
        {
            public string Path => "";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "TELEMETRY_TEST";

            public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
            {
                if (batchSize < 0)
                {
                    yield return new DataTable();
                }

                await Task.Yield();
                throw new InvalidOperationException("preview unavailable");
            }

            public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => Task.CompletedTask;
            public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>(["id"]);
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
