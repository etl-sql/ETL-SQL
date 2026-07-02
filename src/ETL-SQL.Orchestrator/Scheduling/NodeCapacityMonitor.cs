using System;
using System.Diagnostics;

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
}
