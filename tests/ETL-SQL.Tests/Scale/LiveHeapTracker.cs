namespace ETL_SQL.Tests.Scale;

/// <summary>
/// Records the largest live managed heap a scenario held: the bytes that survived each full (gen2)
/// collection. <see cref="ScenarioResourceSampler"/> reports the heap size after the last
/// collection, which includes garbage a full collection has not reclaimed yet. That garbage grows
/// with run length when the GC has headroom, so it cannot show whether an operator's memory stays
/// flat as the input grows; the live size can. Kept apart from the sampler because the sampler is
/// part of the scale certification fixture hash.
/// </summary>
internal sealed class LiveHeapTracker : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pollingTask;
    private long _lastBlockingIndex;
    private long _lastBackgroundIndex;
    private long? _peakLiveBytes;

    public LiveHeapTracker()
    {
        // Only collections that start after construction count.
        _lastBlockingIndex = GC.GetGCMemoryInfo(GCKind.FullBlocking).Index;
        _lastBackgroundIndex = GC.GetGCMemoryInfo(GCKind.Background).Index;
        _pollingTask = PollAsync(_stop.Token);
    }

    /// <summary>The largest live heap seen so far, or null when no full collection has run.</summary>
    public long? Snapshot()
    {
        Poll();
        lock (_gate) return _peakLiveBytes;
    }

    /// <summary>Megabytes to one decimal place for a scenario report, or null when unmeasured.</summary>
    public static double? ToMegabytes(long? bytes)
        => bytes is { } value ? Math.Round(value / 1024d / 1024d, 1) : null;

    private void Poll()
    {
        lock (_gate)
        {
            Observe(GC.GetGCMemoryInfo(GCKind.FullBlocking), ref _lastBlockingIndex);
            Observe(GC.GetGCMemoryInfo(GCKind.Background), ref _lastBackgroundIndex);
        }
    }

    private void Observe(GCMemoryInfo info, ref long lastIndex)
    {
        if (info.Index <= lastIndex || info.Generation != 2) return;
        lastIndex = info.Index;
        _peakLiveBytes = Math.Max(_peakLiveBytes ?? 0, info.PromotedBytes);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) Poll();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _pollingTask.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
