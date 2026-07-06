using System;

namespace ETL_SQL.Core.Data;

public readonly record struct HashPartitionPlan(
    int PartitionCount,
    long EstimatedStateBytes,
    long TargetPartitionBytes,
    long EstimatedLargestPartitionBytes,
    bool HasUnsplittableHotKey,
    int EstimatedPartitionPasses);

/// <summary>Deterministic fan-out sizing for hash operators with known or sampled input evidence.</summary>
public static class HashPartitionSizing
{
    public static HashPartitionPlan Calculate(
        long inputBytes,
        long rowCount,
        int keyWidthBytes,
        long memoryBudgetBytes,
        long? estimatedDistinctKeys = null,
        double largestKeyFraction = 0,
        int minimumPartitions = 2,
        int maximumPartitions = 1024,
        double targetBudgetFraction = 0.70)
    {
        if (inputBytes < 0) throw new ArgumentOutOfRangeException(nameof(inputBytes));
        if (rowCount < 0) throw new ArgumentOutOfRangeException(nameof(rowCount));
        if (keyWidthBytes < 0) throw new ArgumentOutOfRangeException(nameof(keyWidthBytes));
        if (memoryBudgetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(memoryBudgetBytes));
        if (largestKeyFraction is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(largestKeyFraction));
        if (minimumPartitions <= 0 || maximumPartitions < minimumPartitions)
            throw new ArgumentOutOfRangeException(nameof(maximumPartitions));
        if (targetBudgetFraction is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(targetBudgetFraction));

        var keyStateBytes = checked(rowCount * checked(keyWidthBytes + 16L));
        var stateBytes = checked(inputBytes + keyStateBytes);
        var targetBytes = Math.Max(1L, (long)(memoryBudgetBytes * targetBudgetFraction));
        var hotBytes = (long)Math.Ceiling(stateBytes * largestKeyFraction);
        var distributableBytes = Math.Max(0, stateBytes - hotBytes);
        var required = Math.Max(1L, DivideRoundUp(distributableBytes, targetBytes));
        if (hotBytes > 0) required++;
        if (estimatedDistinctKeys.HasValue)
            required = Math.Min(required, Math.Max(1, estimatedDistinctKeys.Value));

        var partitionCount = NextPowerOfTwo(Math.Max(minimumPartitions, required));
        partitionCount = Math.Min(maximumPartitions, partitionCount);
        var distributedLargest = DivideRoundUp(distributableBytes, partitionCount);
        var largestPartition = Math.Max(hotBytes, distributedLargest);
        var unsplittable = hotBytes > targetBytes;
        var passes = largestPartition <= targetBytes
            ? 1
            : EstimatePasses(largestPartition, targetBytes, partitionCount, unsplittable);

        return new HashPartitionPlan(
            partitionCount,
            stateBytes,
            targetBytes,
            largestPartition,
            unsplittable,
            passes);
    }

    private static long DivideRoundUp(long value, long divisor)
        => value == 0 ? 0 : checked(1 + ((value - 1) / divisor));

    private static int NextPowerOfTwo(long value)
    {
        long result = 1;
        while (result < value && result < 1_073_741_824) result <<= 1;
        return (int)Math.Min(result, int.MaxValue);
    }

    private static int EstimatePasses(long largest, long target, int fanOut, bool unsplittable)
    {
        if (unsplittable || fanOut <= 1) return int.MaxValue;
        var passes = 1;
        while (largest > target && passes < 32)
        {
            largest = DivideRoundUp(largest, fanOut);
            passes++;
        }
        return passes;
    }
}
