using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Observability;
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

        [Fact]
        public async Task ConnectorRegistry_EmitsConnectorOperationMetricsAndSpans()
        {
            var registry = new ConnectorRegistry();
            registry.Register(new PreviewFailureConnector());
            var connector = registry.GetConnector("TELEMETRY_TEST")!;
            var context = new SystemExecutionContext();

            var stoppedActivities = new List<Activity>();
            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == ConnectorObservability.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => stoppedActivities.Add(activity)
            };
            ActivitySource.AddActivityListener(activityListener);

            var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
            using var meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == ConnectorObservability.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.Start();

            Assert.Equal("1.0", await connector.GetVersionAsync(context, "secret-host"));
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                connector.GetColumnsAsync(context, "secret-host", "Customers"));

            Assert.Contains(stoppedActivities, activity =>
                activity.OperationName == "connector.operation"
                && Tag(activity, ObservabilityConventions.Tags.ConnectorType) == "TELEMETRY_TEST"
                && Tag(activity, ConnectorObservability.OperationTag) == "version"
                && Tag(activity, ObservabilityConventions.Tags.Status) == "success");
            Assert.Contains(stoppedActivities, activity =>
                Tag(activity, ConnectorObservability.OperationTag) == "columns"
                && Tag(activity, ObservabilityConventions.Tags.Status) == "failure");
            Assert.Contains(measurements, m => m.Name == "etlsql.connector.operation.completed"
                && HasTag(m.Tags, ObservabilityConventions.Tags.ConnectorType, "TELEMETRY_TEST")
                && HasTag(m.Tags, ConnectorObservability.OperationTag, "version")
                && HasTag(m.Tags, ObservabilityConventions.Tags.Status, "success"));
            Assert.DoesNotContain(measurements, m => m.Tags.Any(tag =>
                tag.Value is string value && value.Contains("secret-host", StringComparison.OrdinalIgnoreCase)));
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
            public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("columns unavailable");
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

        private static string? Tag(Activity activity, string key)
        {
            var value = activity.TagObjects.FirstOrDefault(t => t.Key == key).Value;
            return value?.ToString();
        }

        private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var result = new Dictionary<string, object?>();
            foreach (var tag in tags)
                result[tag.Key] = tag.Value;
            return result;
        }

        private static bool HasTag(Dictionary<string, object?> tags, string key, object value) =>
            tags.TryGetValue(key, out var actual) && Equals(actual, value);
    }
}
