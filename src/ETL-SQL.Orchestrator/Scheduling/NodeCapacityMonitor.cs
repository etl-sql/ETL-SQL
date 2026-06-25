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
    DateTime CapturedAtUtc);

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
                now);

            return _cachedSnapshot;
        }
    }
}
