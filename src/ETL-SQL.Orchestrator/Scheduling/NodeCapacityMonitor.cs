using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ETL_SQL.Orchestrator.Scheduling;

public sealed record NodeCapacitySnapshot(
    long WorkingSetBytes,
    long GcHeapBytes,
    long TotalAvailableMemoryBytes,
    double MemoryLoadPercent,
    double ProcessCpuPercent,
    int ProcessorCount,
    bool IsOverloaded,
    DateTime CapturedAtUtc)
{
    /// <summary>Free bytes on the volume hosting application/state files; 0 if unknown.</summary>
    public long StateDiskFreeBytes { get; init; }

    /// <summary>Free bytes on the volume used for engine spill/temp; 0 if unknown.</summary>
    public long SpillDiskFreeBytes { get; init; }

    /// <summary>
    /// Whole-host CPU utilization % (all processes, all cores), distinct from this process's
    /// <see cref="ProcessCpuPercent"/>. Null until a second sample exists or on an unsupported platform.
    /// </summary>
    public double? HostCpuPercent { get; init; }
}

public interface INodeCapacityMonitor
{
    NodeCapacitySnapshot Capture();
}

public sealed class NodeCapacityMonitor : INodeCapacityMonitor
{
    private readonly object _lock = new();
    private TimeSpan _lastCpu = Process.GetCurrentProcess().TotalProcessorTime;
    private DateTime _lastUtc = DateTime.UtcNow;
    private NodeCapacitySnapshot? _cachedSnapshot;

    // Whole-host CPU baseline (cumulative idle/total counters from the last sample). Needs two
    // samples to yield a rate, so the first Capture returns null for host CPU.
    private ulong _lastHostIdle;
    private ulong _lastHostTotal;
    private bool _hasHostCpuBaseline;

    public NodeCapacitySnapshot Capture()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (_cachedSnapshot != null && (now - _cachedSnapshot.CapturedAtUtc).TotalSeconds < 1.0)
            {
                return _cachedSnapshot;
            }

            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var elapsed = Math.Max(0.001, (now - _lastUtc).TotalSeconds);
            var cpuDelta = Math.Max(0, (cpu - _lastCpu).TotalSeconds);
            var cpuPercent = Math.Clamp(
                cpuDelta / (elapsed * Math.Max(1, Environment.ProcessorCount)) * 100.0,
                0,
                100);

            _lastCpu = cpu;
            _lastUtc = now;

            var hostCpuPercent = TryGetHostCpuPercent();

            var gc = GC.GetGCMemoryInfo();
            var totalMemory = gc.TotalAvailableMemoryBytes > 0
                ? gc.TotalAvailableMemoryBytes
                : 0;
            var memoryLoad = totalMemory > 0
                ? Math.Clamp(gc.MemoryLoadBytes * 100.0 / totalMemory, 0, 100)
                : 0;

            _cachedSnapshot = new NodeCapacitySnapshot(
                process.WorkingSet64,
                GC.GetTotalMemory(forceFullCollection: false),
                totalMemory,
                memoryLoad,
                cpuPercent,
                Environment.ProcessorCount,
                memoryLoad >= 95 || cpuPercent >= 95,
                now)
            {
                // Free disk is the most outage-critical host signal. Sample the state volume (app/DB
                // base directory) and the spill volume (temp). Both use DriveInfo — cross-platform.
                StateDiskFreeBytes = TryGetFreeBytes(AppContext.BaseDirectory),
                SpillDiskFreeBytes = TryGetFreeBytes(System.IO.Path.GetTempPath()),
                HostCpuPercent = hostCpuPercent,
            };

            return _cachedSnapshot;
        }
    }

    private static long TryGetFreeBytes(string? path)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(
                string.IsNullOrWhiteSpace(path) ? AppContext.BaseDirectory : path));
            if (string.IsNullOrEmpty(root)) return 0;
            var drive = new System.IO.DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : 0;
        }
        catch
        {
            return 0; // Disk stats are best-effort; never fail a capacity capture over them.
        }
    }

    /// <summary>
    /// Whole-host CPU % across the interval since the last call, from OS-level cumulative counters
    /// (Windows GetSystemTimes / Linux /proc/stat). Returns null on the first sample (no interval yet)
    /// or on an unsupported platform. Best-effort: any failure yields null, never an exception.
    /// Caller holds <see cref="_lock"/>, so the baseline fields need no further synchronization.
    /// </summary>
    private double? TryGetHostCpuPercent()
    {
        if (!TryReadHostCpuCounters(out var idle, out var total))
            return null;

        double? result = null;
        if (_hasHostCpuBaseline && total > _lastHostTotal)
        {
            var totalDelta = total - _lastHostTotal;
            var idleDelta = idle >= _lastHostIdle ? idle - _lastHostIdle : 0;
            var busyDelta = totalDelta >= idleDelta ? totalDelta - idleDelta : 0;
            result = Math.Clamp(busyDelta * 100.0 / totalDelta, 0, 100);
        }

        _lastHostIdle = idle;
        _lastHostTotal = total;
        _hasHostCpuBaseline = true;
        return result;
    }

    /// <summary>Reads cumulative host idle and total CPU ticks. False if the platform is unsupported.</summary>
    private static bool TryReadHostCpuCounters(out ulong idle, out ulong total)
    {
        idle = 0;
        total = 0;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                    return false;
                // On Windows, kernel time already includes idle, so total = kernel + user.
                idle = idleFt.ToUInt64();
                total = kernelFt.ToUInt64() + userFt.ToUInt64();
                return total > 0;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // First line of /proc/stat: "cpu  user nice system idle iowait irq softirq steal ...".
                var firstLine = File.ReadLines("/proc/stat").FirstOrDefault();
                if (firstLine is null || !firstLine.StartsWith("cpu ", StringComparison.Ordinal))
                    return false;
                var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                ulong sum = 0;
                ulong idleTicks = 0;
                for (int i = 1; i < parts.Length; i++)
                {
                    if (!ulong.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                        continue;
                    sum += v;
                    if (i == 4 || i == 5) idleTicks += v; // idle + iowait
                }
                idle = idleTicks;
                total = sum;
                return total > 0;
            }
        }
        catch
        {
            // Best-effort: an unreadable /proc or a P/Invoke failure just means "no host CPU sample".
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTimeTicks
    {
        public uint LowDateTime;
        public uint HighDateTime;
        public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTimeTicks lpIdleTime, out FileTimeTicks lpKernelTime, out FileTimeTicks lpUserTime);
}
