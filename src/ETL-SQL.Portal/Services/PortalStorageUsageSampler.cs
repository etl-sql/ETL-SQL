using Microsoft.Extensions.Logging;

namespace ETL_SQL.Portal.Services;

/// <summary>Samples recursive Portal storage sizes off the HTTP request path.</summary>
public sealed class PortalStorageUsageSampler(
    PortalConfig config,
    ILogger<PortalStorageUsageSampler>? logger = null) : BackgroundService
{
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);

    private long datasetStorageBytes;
    private long snapshotStorageBytes;
    private long lastSuccessfulSampleTicks;
    private long lastFailureTicks;
    private string? lastFailureMessage;

    public long DatasetStorageBytes => Interlocked.Read(ref datasetStorageBytes);
    public long SnapshotStorageBytes => Interlocked.Read(ref snapshotStorageBytes);
    public DateTimeOffset? LastSuccessfulSampleUtc => ReadTimestamp(ref lastSuccessfulSampleTicks);
    public DateTimeOffset? LastFailureUtc => ReadTimestamp(ref lastFailureTicks);
    public string? LastFailureMessage => Volatile.Read(ref lastFailureMessage);
    public bool IsStale =>
        LastSuccessfulSampleUtc is null
        || DateTimeOffset.UtcNow - LastSuccessfulSampleUtc.Value > EffectiveSampleInterval() * 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SampleOnceAsync(stoppingToken);
                await Task.Delay(EffectiveSampleInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task SampleOnceAsync(CancellationToken stoppingToken)
    {
        using var sampleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        sampleCts.CancelAfter(EffectiveSampleTimeout());
        var token = sampleCts.Token;
        var maxFiles = EffectiveMaxFiles();

        var datasetTask = Task.Run(
            () => MeasureDirectoryDetailed(config.DatasetRootPath, maxFiles, token),
            token);
        var snapshotTask = Task.Run(
            () => MeasureDirectoryDetailed(config.SnapshotDirectory, maxFiles, token),
            token);

        StorageMeasurement dataset;
        StorageMeasurement snapshot;
        try
        {
            await Task.WhenAll(datasetTask, snapshotTask);
            dataset = datasetTask.Result;
            snapshot = snapshotTask.Result;
        }
        catch (OperationCanceledException)
        {
            RecordFailure("Portal storage usage sampling timed out or was cancelled.");
            return;
        }

        if (dataset.IsComplete && snapshot.IsComplete)
        {
            Interlocked.Exchange(ref datasetStorageBytes, dataset.Bytes);
            Interlocked.Exchange(ref snapshotStorageBytes, snapshot.Bytes);
            Interlocked.Exchange(ref lastSuccessfulSampleTicks, DateTimeOffset.UtcNow.UtcTicks);
            Volatile.Write(ref lastFailureMessage, null);
            return;
        }

        RecordFailure(string.Join(
            "; ",
            new[] { dataset.Error, snapshot.Error }.Where(message => !string.IsNullOrWhiteSpace(message))));
    }

    internal static long MeasureDirectory(string? path)
        => MeasureDirectoryDetailed(path, int.MaxValue, CancellationToken.None).Bytes;

    internal static StorageMeasurement MeasureDirectoryDetailed(
        string? path,
        int maxFiles,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return new StorageMeasurement(0, 0, IsComplete: true, Error: null);

        maxFiles = Math.Max(1, maxFiles);
        try
        {
            long total = 0;
            var filesVisited = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                filesVisited++;
                if (filesVisited > maxFiles)
                {
                    return new StorageMeasurement(
                        total,
                        filesVisited - 1,
                        IsComplete: false,
                        Error: $"Storage usage sample for '{path}' exceeded the {maxFiles:N0} file limit.");
                }
                try { total += new FileInfo(file).Length; }
                catch { /* Files can disappear while a shared storage root is sampled. */ }
            }
            return new StorageMeasurement(total, filesVisited, IsComplete: true, Error: null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new StorageMeasurement(
                0,
                0,
                IsComplete: false,
                Error: $"Storage usage sample for '{path}' failed: {ex.Message}");
        }
    }

    private void RecordFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            message = "Portal storage usage sampling did not complete.";
        Volatile.Write(ref lastFailureMessage, message);
        Interlocked.Exchange(ref lastFailureTicks, DateTimeOffset.UtcNow.UtcTicks);
        logger?.LogWarning("Portal storage usage sample failed; retaining previous successful values. {Message}", message);
    }

    private TimeSpan EffectiveSampleInterval() =>
        TimeSpan.FromSeconds(Math.Max(1, config.Resources.StorageUsageSampleIntervalSeconds));

    private TimeSpan EffectiveSampleTimeout() =>
        TimeSpan.FromSeconds(Math.Max(1, config.Resources.StorageUsageSampleTimeoutSeconds));

    private int EffectiveMaxFiles() =>
        Math.Max(1, config.Resources.StorageUsageSampleMaxFiles);

    private static DateTimeOffset? ReadTimestamp(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }

    internal sealed record StorageMeasurement(long Bytes, int FilesVisited, bool IsComplete, string? Error);
}
