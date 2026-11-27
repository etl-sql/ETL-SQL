using System;
using System.Collections.Generic;
using System.Threading;
using ETL_SQL.TestSupport;
using Xunit;

namespace ETL_SQL.Tests.Infrastructure;

/// <summary>
/// The load probe measures thread-pool starvation, so on a starved runner its work items run
/// after the probe has given up. A late item must be harmless: an unhandled exception on a pool
/// thread crashes the whole test host (CI run 45406034624, engine shard 8).
/// </summary>
public sealed class LoadAwareWaitCalibrationTests
{
    [Fact]
    public void ProbeItemsThatRunAfterTheProbeTimedOut_DoNotThrow()
    {
        var held = new List<WaitCallback>();

        var calibration = LoadAwareWait.MeasureLoad(
            callback => held.Add(callback),
            TimeSpan.FromMilliseconds(10));

        Assert.Equal(4d, calibration.Scale);
        Assert.NotEmpty(held);
        foreach (var callback in held)
            callback(null);
    }

    [Fact]
    public void ProbeItemsThatRunPromptly_MeasureAnUnloadedPool()
    {
        var calibration = LoadAwareWait.MeasureLoad(
            callback => callback(null),
            TimeSpan.FromSeconds(5));

        Assert.Equal(1d, calibration.Scale);
    }
}
