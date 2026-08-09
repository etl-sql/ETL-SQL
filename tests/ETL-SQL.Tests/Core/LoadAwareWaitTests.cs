using ETL_SQL.TestSupport;

namespace ETL_SQL.Tests.Core;

public sealed class LoadAwareWaitTests
{
    [Fact]
    public async Task ReturnsTheFirstSatisfiedObservedState()
    {
        var observations = 0;
        var result = await LoadAwareWait.UntilAsync(
            "counter to reach three",
            _ => Task.FromResult(Interlocked.Increment(ref observations)),
            value => value >= 3,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1),
            value => $"counter={value}");

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task TimeoutNamesConditionBudgetCalibrationAndLastState()
    {
        var error = await Assert.ThrowsAsync<TimeoutException>(() => LoadAwareWait.UntilAsync(
            "host to stop",
            _ => Task.FromResult(false),
            stopped => stopped,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(2),
            stopped => $"ApplicationStopping={stopped}"));

        Assert.Contains("host to stop", error.Message, StringComparison.Ordinal);
        Assert.Contains("Baseline budget=", error.Message, StringComparison.Ordinal);
        Assert.Contains("load scale=", error.Message, StringComparison.Ordinal);
        Assert.Contains("last observed state: ApplicationStopping=False", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CalibrationIsBoundedAndStableForTheProcess()
    {
        var first = LoadAwareWait.BudgetScale;
        var second = LoadAwareWait.BudgetScale;

        Assert.InRange(first, 1d, 4d);
        Assert.Equal(first, second);
    }
}
