using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class ScenarioResourceSamplerTests
{
    [Fact]
    public void ScenarioStartExcludesBetweenScenarioWorkAndRetainsStartingMemory()
    {
        using var sampler = new ScenarioResourceSampler();
        var previousScenario = new byte[32 * 1024 * 1024];
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        sampler.SnapshotAndReset();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        sampler.StartScenario();
        sampler.StartOperation();
        var operation = new byte[2 * 1024 * 1024];
        sampler.FinishOperation();
        var verification = new byte[1024 * 1024];
        var lifecycle = sampler.SnapshotAndReset();

        Assert.InRange(lifecycle.AllocatedBytes, 1024 * 1024, 8 * 1024 * 1024);
        Assert.Equal(0, lifecycle.Gen2Collections);
        Assert.True(lifecycle.PeakManagedHeapBytes >= previousScenario.Length);
        Assert.True(lifecycle.PeakWorkingSetBytes >= lifecycle.StartWorkingSetBytes);
        GC.KeepAlive(previousScenario);
        GC.KeepAlive(operation);
        GC.KeepAlive(verification);
    }

    [Fact]
    public void TimedResourcesExcludeSetupAndVerificationButLifecycleRetainsThem()
    {
        using var sampler = new ScenarioResourceSampler();
        var setup = new byte[32 * 1024 * 1024];
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        sampler.StartOperation();
        var operation = new byte[1024 * 1024];
        var timed = sampler.FinishOperation();

        var verification = new byte[32 * 1024 * 1024];
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var lifecycle = sampler.SnapshotAndReset();

        Assert.InRange(timed.AllocatedBytes, 512 * 1024, 8 * 1024 * 1024);
        Assert.Equal(0, timed.Gen2Collections);
        Assert.True(lifecycle.AllocatedBytes >= 64 * 1024 * 1024);
        Assert.True(lifecycle.Gen2Collections >= 2);
        Assert.True(lifecycle.PeakManagedHeapBytes >= 64 * 1024 * 1024);
        Assert.True(lifecycle.GcPauseTime >= timed.GcPauseTime);
        GC.KeepAlive(setup);
        GC.KeepAlive(operation);
        GC.KeepAlive(verification);
    }

    [Fact]
    public void CombiningOperationsExcludesWorkBetweenIntervals()
    {
        using var sampler = new ScenarioResourceSampler();
        sampler.StartOperation();
        var firstBuffer = new byte[1024 * 1024];
        var first = sampler.FinishOperation();

        var between = new byte[32 * 1024 * 1024];
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        sampler.StartOperation();
        var secondBuffer = new byte[1024 * 1024];
        var second = sampler.FinishOperation();
        var combined = first.AddOperation(second);
        var lifecycle = sampler.SnapshotAndReset();

        Assert.InRange(combined.AllocatedBytes, 1024 * 1024, 8 * 1024 * 1024);
        Assert.Equal(0, combined.Gen2Collections);
        Assert.True(lifecycle.AllocatedBytes > combined.AllocatedBytes + 24 * 1024 * 1024);
        Assert.True(lifecycle.Gen2Collections >= 1);
        GC.KeepAlive(firstBuffer);
        GC.KeepAlive(between);
        GC.KeepAlive(secondBuffer);
    }

    [Fact]
    public void OverlappingOperationsAndLifecycleResetDuringOperationAreRejected()
    {
        using var sampler = new ScenarioResourceSampler();
        Assert.Throws<InvalidOperationException>(() => sampler.FinishOperation());
        sampler.StartOperation();
        Assert.Throws<InvalidOperationException>(() => sampler.StartOperation());
        Assert.Throws<InvalidOperationException>(() => sampler.SnapshotAndReset());
        sampler.FinishOperation();
        sampler.SnapshotAndReset();
        Assert.Throws<InvalidOperationException>(() => sampler.FinishOperation());
    }
}
