using System;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class HashPartitionSizingTests
{
    [Fact]
    public void NormalDistributionTargetsOnePassWithinBudget()
    {
        var plan = HashPartitionSizing.Calculate(
            inputBytes: 100L * 1024 * 1024,
            rowCount: 1_000_000,
            keyWidthBytes: 8,
            memoryBudgetBytes: 64L * 1024 * 1024);

        Assert.Equal(4, plan.PartitionCount);
        Assert.Equal(1, plan.EstimatedPartitionPasses);
        Assert.False(plan.HasUnsplittableHotKey);
        Assert.True(plan.EstimatedLargestPartitionBytes <= plan.TargetPartitionBytes);
    }

    [Fact]
    public void HotKeyIsReportedInsteadOfHiddenByHigherFanOut()
    {
        var plan = HashPartitionSizing.Calculate(
            inputBytes: 100L * 1024 * 1024,
            rowCount: 1_000_000,
            keyWidthBytes: 8,
            memoryBudgetBytes: 64L * 1024 * 1024,
            largestKeyFraction: 0.80);

        Assert.True(plan.HasUnsplittableHotKey);
        Assert.Equal(int.MaxValue, plan.EstimatedPartitionPasses);
        Assert.True(plan.EstimatedLargestPartitionBytes > plan.TargetPartitionBytes);
    }

    [Fact]
    public void FanOutHonorsConfiguredAndCardinalityBounds()
    {
        var capped = HashPartitionSizing.Calculate(
            inputBytes: 10_000_000_000,
            rowCount: 100_000_000,
            keyWidthBytes: 16,
            memoryBudgetBytes: 64L * 1024 * 1024,
            maximumPartitions: 32);
        Assert.Equal(32, capped.PartitionCount);
        Assert.True(capped.EstimatedPartitionPasses > 1);

        var lowCardinality = HashPartitionSizing.Calculate(
            inputBytes: 1_000_000,
            rowCount: 10_000,
            keyWidthBytes: 4,
            memoryBudgetBytes: 1_000,
            estimatedDistinctKeys: 3,
            maximumPartitions: 64);
        Assert.Equal(4, lowCardinality.PartitionCount);
    }

    [Fact]
    public void InvalidEvidenceIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HashPartitionSizing.Calculate(1, 1, 4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HashPartitionSizing.Calculate(1, 1, 4, 10, largestKeyFraction: 1.1));
    }
}
