using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;

namespace ETL_SQL.Tests.Reporting
{
    public class DatasetObservabilityTests
    {
        [Fact]
        public async Task InstrumentedDatasetRegistry_EmitsLowCardinalityMetricsAndSpans()
        {
            var registry = DatasetObservability.Instrument(new FakeDatasetRegistry());

            var stoppedActivities = new List<Activity>();
            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DatasetObservability.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => stoppedActivities.Add(activity)
            };
            ActivitySource.AddActivityListener(activityListener);

            var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
            using var meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == DatasetObservability.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.Start();

            var datasetId = await registry.RegisterOrUpdate(new DatasetMetadata { Name = "secret-dataset" });
            var lookedUp = await registry.Lookup("secret-dataset", "admin,password=do-not-leak");
            await Assert.ThrowsAsync<InvalidOperationException>(() => registry.Delete("secret-dataset"));

            Assert.Equal(42, datasetId);
            Assert.Equal(42, lookedUp?.Id);
            Assert.Contains(stoppedActivities, activity =>
                activity.OperationName == "dataset.operation"
                && Tag(activity, ObservabilityConventions.Tags.Node) == Environment.MachineName
                && Tag(activity, DatasetObservability.OperationTag) == "register_or_update"
                && Tag(activity, ObservabilityConventions.Tags.DatasetId) == "42"
                && Tag(activity, ObservabilityConventions.Tags.Status) == "success");
            Assert.Contains(stoppedActivities, activity =>
                Tag(activity, DatasetObservability.OperationTag) == "delete"
                && Tag(activity, ObservabilityConventions.Tags.Status) == "failure");
            Assert.Contains(measurements, measurement => measurement.Name == "etlsql.dataset.operation.completed"
                && HasTag(measurement.Tags, ObservabilityConventions.Tags.Node, Environment.MachineName)
                && HasTag(measurement.Tags, ObservabilityConventions.Tags.Component, "dataset")
                && HasTag(measurement.Tags, DatasetObservability.OperationTag, "register_or_update")
                && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "success"));
            Assert.DoesNotContain(measurements, measurement => measurement.Tags.ContainsKey(ObservabilityConventions.Tags.DatasetId));
            Assert.DoesNotContain(measurements, measurement => measurement.Tags.Any(tag =>
                tag.Value is string value
                && (value.Contains("secret-dataset", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("do-not-leak", StringComparison.OrdinalIgnoreCase))));
        }

        private sealed class FakeDatasetRegistry : IDatasetRegistry
        {
            public Task<int> RegisterOrUpdate(DatasetMetadata metadata) => Task.FromResult(42);

            public Task<DatasetMetadata?> Lookup(string name, string callerPermissions = "") =>
                Task.FromResult<DatasetMetadata?>(new DatasetMetadata { Id = 42, Name = name });

            public Task<bool> Exists(string name) => Task.FromResult(true);
            public Task<bool> CanEditAsync(string name, string callerPermissions) => Task.FromResult(true);
            public Task SetStale(string name) => Task.CompletedTask;
            public Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions) =>
                Task.FromResult<IEnumerable<DatasetMetadata>>([new DatasetMetadata { Id = 42, Name = "secret-dataset" }]);

            public Task Delete(string name) => throw new InvalidOperationException("delete failed");
            public string BuildDatasetFilePath(int datasetId, string name) => $"dataset-{datasetId}.parquet";
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
