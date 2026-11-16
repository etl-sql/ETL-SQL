using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class LiveHeapTrackerTests
{
    [Fact]
    public void ReportsNothingUntilAFullCollectionRuns()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        using var tracker = new LiveHeapTracker();

        Assert.Null(tracker.Snapshot());
    }

    [Fact]
    public void CountsDataThatSurvivesAFullCollectionButNotGarbage()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        using var tracker = new LiveHeapTracker();

        var retained = new byte[48 * 1024 * 1024];
        for (var i = 0; i < 8; i++) GC.KeepAlive(new byte[32 * 1024 * 1024]);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var live = tracker.Snapshot();

        Assert.NotNull(live);
        // The retained array survives; the 256 MB of garbage does not.
        Assert.InRange(live!.Value, retained.Length, before + retained.Length + 64L * 1024 * 1024);
        GC.KeepAlive(retained);
    }

    [Fact]
    public void KeepsThePeakAcrossCollections()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        using var tracker = new LiveHeapTracker();

        HoldThroughACollection(tracker);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.True(tracker.Snapshot() >= 64L * 1024 * 1024);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void HoldThroughACollection(LiveHeapTracker tracker)
    {
        var large = new byte[64 * 1024 * 1024];
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.True(tracker.Snapshot() >= large.Length);
        GC.KeepAlive(large);
    }
}
