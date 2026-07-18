namespace ETL_SQL.Portal.Services;

/// <summary>Samples recursive Portal storage sizes off the HTTP request path.</summary>
public sealed class PortalStorageUsageSampler(PortalConfig config) : BackgroundService
{
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);

    private long datasetStorageBytes;
    private long snapshotStorageBytes;

    public long DatasetStorageBytes => Interlocked.Read(ref datasetStorageBytes);
    public long SnapshotStorageBytes => Interlocked.Read(ref snapshotStorageBytes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var datasetTask = Task.Run(
                () => MeasureDirectory(config.DatasetRootPath), stoppingToken);
            var snapshotTask = Task.Run(
                () => MeasureDirectory(config.SnapshotDirectory), stoppingToken);
            await Task.WhenAll(datasetTask, snapshotTask);
            Interlocked.Exchange(ref datasetStorageBytes, datasetTask.Result);
            Interlocked.Exchange(ref snapshotStorageBytes, snapshotTask.Result);
            await Task.Delay(SampleInterval, stoppingToken);
        }
    }

    internal static long MeasureDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return 0;
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* Files can disappear while a shared storage root is sampled. */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }
}
