using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace ETL_SQL.Core.Execution;

/// <summary>
/// Reports current host and container memory headroom rather than static total capacity.
/// Combines OS free/available memory (<c>GlobalMemoryStatusEx</c> on Windows,
/// <c>/proc/meminfo</c> <c>MemAvailable</c> on Linux) with active container cgroup limits
/// and any process GC heap limit (<c>GCHeapHardLimit</c>).
/// </summary>
public class DefaultSystemResources : ISystemResources
{
    private const long UnlimitedSentinelBytes = 1L << 50; // 1 PB+ sentinel used by unlimited cgroups.

    public long GetAvailableMemoryBytes()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        long? hostAvailable = TryGetHostAvailableBytes();
        long? containerHeadroom = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? TryGetLinuxCgroupHeadroomBytes()
            : null;

        long? effective = MinNullable(hostAvailable, containerHeadroom);

        if (gcInfo.TotalAvailableMemoryBytes > 0)
        {
            if (effective.HasValue)
            {
                // TotalAvailableMemoryBytes may reflect a per-process GCHeapHardLimit smaller than
                // physical host RAM; bound headroom by remaining process capacity using WorkingSet
                // rather than subtracting whole-host MemoryLoadBytes from a process-local GC limit.
                long processRemaining = Math.Max(0L, gcInfo.TotalAvailableMemoryBytes - Environment.WorkingSet);
                effective = Math.Min(effective.Value, processRemaining);
            }
            else
            {
                // Fallback when OS/cgroup counters are unavailable: use GC system load or WorkingSet.
                long used = Math.Max(gcInfo.MemoryLoadBytes, Environment.WorkingSet);
                effective = Math.Max(0L, gcInfo.TotalAvailableMemoryBytes - used);
            }
        }

        return Math.Max(0L, effective ?? 0L);
    }

    public long GetTotalMemoryBytes() => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    /// <summary>
    /// Computes effective memory load % (0–100) using host OS memory, Linux cgroup limits, and the
    /// current process's working set against any configured GC heap cap so a per-process
    /// <c>GCHeapHardLimit</c> smaller than physical host RAM does not read whole-host usage as 100%.
    /// </summary>
    public static double GetMemoryLoadPercent(long processWorkingSetBytes = 0)
    {
        var gcInfo = GC.GetGCMemoryInfo();
        TryGetHostMemoryStats(out var hostTotal, out var hostAvailable);
        long? cgroupLimit = null;
        long? cgroupHeadroom = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            TryGetLinuxCgroupStats(out cgroupLimit, out cgroupHeadroom);

        long effectiveWorkingSet = processWorkingSetBytes > 0
            ? processWorkingSetBytes
            : Environment.WorkingSet;

        return ComputeMemoryLoadPercent(
            hostTotal,
            hostAvailable,
            cgroupLimit,
            cgroupHeadroom,
            gcInfo.TotalAvailableMemoryBytes,
            gcInfo.MemoryLoadBytes,
            effectiveWorkingSet);
    }

    internal static double ComputeMemoryLoadPercent(
        long? hostTotalBytes,
        long? hostAvailableBytes,
        long? cgroupLimitBytes,
        long? cgroupHeadroomBytes,
        long gcTotalAvailableBytes,
        long gcMemoryLoadBytes,
        long processWorkingSetBytes)
    {
        double load = 0.0;
        if (hostTotalBytes is > 0 && hostAvailableBytes is >= 0)
        {
            double hostLoad = (hostTotalBytes.Value - hostAvailableBytes.Value) * 100.0 / hostTotalBytes.Value;
            load = Math.Max(load, Math.Clamp(hostLoad, 0.0, 100.0));
        }

        if (cgroupLimitBytes is > 0 && cgroupHeadroomBytes is >= 0)
        {
            double cgroupLoad = (cgroupLimitBytes.Value - cgroupHeadroomBytes.Value) * 100.0 / cgroupLimitBytes.Value;
            load = Math.Max(load, Math.Clamp(cgroupLoad, 0.0, 100.0));
        }

        if (gcTotalAvailableBytes > 0)
        {
            long boundedWorkingSet = Math.Max(0L, processWorkingSetBytes);
            long processOrFallbackUsed = (hostTotalBytes is > 0 || cgroupLimitBytes is > 0 || gcMemoryLoadBytes > gcTotalAvailableBytes)
                ? boundedWorkingSet
                : Math.Max(gcMemoryLoadBytes, boundedWorkingSet);
            double processLoad = processOrFallbackUsed * 100.0 / gcTotalAvailableBytes;
            load = Math.Max(load, Math.Clamp(processLoad, 0.0, 100.0));
        }

        return Math.Clamp(load, 0.0, 100.0);
    }

    private static long? TryGetHostAvailableBytes()
    {
        TryGetHostMemoryStats(out _, out var available);
        return available;
    }

    private static void TryGetHostMemoryStats(out long? totalBytes, out long? availableBytes)
    {
        totalBytes = null;
        availableBytes = null;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var status = MEMORYSTATUSEX.Create();
                if (GlobalMemoryStatusEx(ref status))
                {
                    totalBytes = (long)Math.Min(status.ullTotalPhys, (ulong)long.MaxValue);
                    availableBytes = (long)Math.Min(status.ullAvailPhys, (ulong)long.MaxValue);
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                const string memInfoPath = "/proc/meminfo";
                if (File.Exists(memInfoPath))
                    ParseProcMemInfoStats(File.ReadAllText(memInfoPath), out totalBytes, out availableBytes);
            }
        }
        catch
        {
            // Fall through to GC-based headroom estimate.
        }
    }

    private static long? TryGetLinuxCgroupHeadroomBytes()
    {
        TryGetLinuxCgroupStats(out _, out var headroom);
        return headroom;
    }

    private static void TryGetLinuxCgroupStats(out long? limitBytes, out long? headroomBytes)
    {
        limitBytes = null;
        headroomBytes = null;
        try
        {
            const string v2MaxPath = "/sys/fs/cgroup/memory.max";
            const string v2CurPath = "/sys/fs/cgroup/memory.current";
            const string v2StatPath = "/sys/fs/cgroup/memory.stat";
            if (File.Exists(v2MaxPath) && File.Exists(v2CurPath))
            {
                var maxText = File.ReadAllText(v2MaxPath);
                var curText = File.ReadAllText(v2CurPath);
                var statText = File.Exists(v2StatPath) ? File.ReadAllText(v2StatPath) : null;
                var headroom = ComputeCgroupV2Headroom(maxText, curText, statText);
                if (headroom.HasValue && TryParseBoundedCgroupLimit(maxText, out var v2Limit))
                {
                    limitBytes = v2Limit;
                    headroomBytes = headroom.Value;
                    return;
                }
            }

            const string v1LimitPath = "/sys/fs/cgroup/memory/memory.limit_in_bytes";
            const string v1UsagePath = "/sys/fs/cgroup/memory/memory.usage_in_bytes";
            const string v1StatPath = "/sys/fs/cgroup/memory/memory.stat";
            if (File.Exists(v1LimitPath) && File.Exists(v1UsagePath))
            {
                var limitText = File.ReadAllText(v1LimitPath);
                var usageText = File.ReadAllText(v1UsagePath);
                var statText = File.Exists(v1StatPath) ? File.ReadAllText(v1StatPath) : null;
                var headroom = ComputeCgroupV1Headroom(limitText, usageText, statText);
                if (headroom.HasValue && TryParseBoundedCgroupLimit(limitText, out var v1Limit))
                {
                    limitBytes = v1Limit;
                    headroomBytes = headroom.Value;
                }
            }
        }
        catch
        {
            // Best effort; fallback uses /proc/meminfo or GC.
        }
    }

    private static bool TryParseBoundedCgroupLimit(string text, out long limitBytes)
    {
        limitBytes = 0;
        var trimmed = text.Trim();
        if (string.Equals(trimmed, "max", StringComparison.OrdinalIgnoreCase))
            return false;
        return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out limitBytes)
            && limitBytes > 0
            && limitBytes < UnlimitedSentinelBytes;
    }

    internal static long? ParseProcMemInfoAvailableBytes(string memInfoContent)
    {
        ParseProcMemInfoStats(memInfoContent, out _, out var available);
        return available;
    }

    internal static void ParseProcMemInfoStats(string memInfoContent, out long? totalBytes, out long? availableBytes)
    {
        totalBytes = null;
        availableBytes = null;
        if (string.IsNullOrWhiteSpace(memInfoContent))
            return;

        long? memTotalKb = null;
        long? memAvailableKb = null;
        long memFreeKb = 0;
        long buffersKb = 0;
        long cachedKb = 0;
        bool hasFallback = false;

        foreach (var rawLine in memInfoContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                if (TryParseMemInfoKb(line, out var kb))
                    memTotalKb = kb;
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                if (TryParseMemInfoKb(line, out var kb))
                    memAvailableKb = kb;
            }
            else if (line.StartsWith("MemFree:", StringComparison.Ordinal))
            {
                if (TryParseMemInfoKb(line, out var kb)) { memFreeKb = kb; hasFallback = true; }
            }
            else if (line.StartsWith("Buffers:", StringComparison.Ordinal))
            {
                if (TryParseMemInfoKb(line, out var kb)) { buffersKb = kb; hasFallback = true; }
            }
            else if (line.StartsWith("Cached:", StringComparison.Ordinal))
            {
                if (TryParseMemInfoKb(line, out var kb)) { cachedKb = kb; hasFallback = true; }
            }
        }

        if (memTotalKb.HasValue)
            totalBytes = Math.Max(0L, checked(memTotalKb.Value * 1024L));

        if (memAvailableKb.HasValue)
            availableBytes = Math.Max(0L, checked(memAvailableKb.Value * 1024L));
        else if (hasFallback)
            availableBytes = Math.Max(0L, checked((memFreeKb + buffersKb + cachedKb) * 1024L));
    }

    private static bool TryParseMemInfoKb(string line, out long kb)
    {
        kb = 0;
        int colon = line.IndexOf(':');
        if (colon < 0) return false;
        var rest = line[(colon + 1)..].TrimStart();
        int end = 0;
        while (end < rest.Length && char.IsDigit(rest[end])) end++;
        return end > 0 && long.TryParse(rest[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out kb);
    }

    internal static long? ComputeCgroupV2Headroom(string maxText, string currentText, string? memoryStatText)
    {
        var trimmedMax = maxText.Trim();
        if (string.Equals(trimmedMax, "max", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!long.TryParse(trimmedMax, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxBytes)
            || maxBytes <= 0 || maxBytes >= UnlimitedSentinelBytes)
            return null;

        if (!long.TryParse(currentText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentBytes)
            || currentBytes < 0)
            return null;

        long inactiveFile = ParseStatKeyValue(memoryStatText, "inactive_file") ?? 0L;
        long activeUsage = currentBytes > inactiveFile ? currentBytes - inactiveFile : 0L;
        return Math.Max(0L, maxBytes - activeUsage);
    }

    internal static long? ComputeCgroupV1Headroom(string limitText, string usageText, string? memoryStatText)
    {
        if (!long.TryParse(limitText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limitBytes)
            || limitBytes <= 0 || limitBytes >= UnlimitedSentinelBytes)
            return null;

        if (!long.TryParse(usageText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var usageBytes)
            || usageBytes < 0)
            return null;

        long inactiveFile = ParseStatKeyValue(memoryStatText, "total_inactive_file")
            ?? ParseStatKeyValue(memoryStatText, "inactive_file")
            ?? 0L;
        long activeUsage = usageBytes > inactiveFile ? usageBytes - inactiveFile : 0L;
        return Math.Max(0L, limitBytes - activeUsage);
    }

    internal static long? ParseStatKeyValue(string? statContent, string key)
    {
        if (string.IsNullOrWhiteSpace(statContent))
            return null;

        foreach (var rawLine in statContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(key, StringComparison.Ordinal))
                continue;
            if (line.Length <= key.Length || !char.IsWhiteSpace(line[key.Length]))
                continue;
            var valuePart = line[key.Length..].Trim();
            if (long.TryParse(valuePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return Math.Max(0L, value);
        }

        return null;
    }

    private static long? MinNullable(long? a, long? b) => (a, b) switch
    {
        (long x, long y) => Math.Min(x, y),
        (long x, null) => x,
        (null, long y) => y,
        _ => null
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public static MEMORYSTATUSEX Create() => new()
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
