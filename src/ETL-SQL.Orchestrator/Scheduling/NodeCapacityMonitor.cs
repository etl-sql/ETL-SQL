using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>Total bytes currently reserved/held across in-process and tracked child workers.</summary>
    public long AggregateWorkerReservedBytes { get; init; }

    /// <summary>Configured host-wide memory budget in bytes (0 when unbounded/unknown).</summary>
    public long EffectiveMemoryBudgetBytes { get; init; }

    /// <summary>Percentage of <see cref="EffectiveMemoryBudgetBytes"/> consumed by active worker reservations.</summary>
    public double WorkerMemoryReservationPercent { get; init; }

    /// <summary>Minimum free bytes reserved on the configured spill volume (<c>Engine:SpillMinimumFreeDiskMB</c>).</summary>
    public long SpillMinimumFreeBytes { get; init; }

    /// <summary>Resolved path whose volume is monitored for spill free space.</summary>
    public string? SpillVolumePath { get; init; }
}

public interface INodeCapacityMonitor
{
    NodeCapacitySnapshot Capture();
}

public sealed class NodeCapacityMonitor : INodeCapacityMonitor
{
    private const double CpuHighWatermarkPercent = 95.0;
    private const double CpuRecoveryWatermarkPercent = 85.0;
    private const double MemoryLoadHighWatermarkPercent = 95.0;
    private const double MemoryLoadRecoveryWatermarkPercent = 85.0;
    private const double ReservationHighWatermarkPercent = 90.0;
    private const double ReservationDefaultRecoveryPercent = 80.0;
    private const double MinimumCpuWindowSeconds = 0.5;

    private readonly object _lock = new();
    private readonly IConfiguration? _configuration;
    private readonly IMemoryGrantArbiter? _memoryArbiter;
    private readonly ChildProcessTracker? _childProcessTracker;
    private readonly Func<double>? _memoryLoadPercentProvider;
    private readonly Func<double>? _processCpuPercentProvider;
    private readonly Func<double?>? _hostCpuPercentProvider;
    private readonly Func<string, long?>? _freeBytesProvider;
    private readonly TimeSpan _cacheWindow;

    private TimeSpan _lastCpu = Process.GetCurrentProcess().TotalProcessorTime;
    private DateTime _lastUtc = DateTime.UtcNow;
    private double _lastCpuPercent;
    private NodeCapacitySnapshot? _cachedSnapshot;
    private bool _cachedArbiterExhausted;
    private bool _isOverloaded;

    // Whole-host CPU baseline (cumulative idle/total counters from the last sample). Needs two
    // samples to yield a rate, so the first Capture returns null for host CPU.
    private ulong _lastHostIdle;
    private ulong _lastHostTotal;
    private bool _hasHostCpuBaseline;

    public NodeCapacityMonitor()
        : this(configuration: null, memoryArbiter: null, childProcessTracker: null)
    {
    }

    [ActivatorUtilitiesConstructor]
    public NodeCapacityMonitor(
        IConfiguration? configuration,
        IMemoryGrantArbiter? memoryArbiter = null,
        ChildProcessTracker? childProcessTracker = null)
        : this(configuration, memoryArbiter, childProcessTracker, null, null, null, null, null)
    {
    }

    internal NodeCapacityMonitor(
        IConfiguration? configuration,
        IMemoryGrantArbiter? memoryArbiter,
        ChildProcessTracker? childProcessTracker,
        Func<double>? memoryLoadPercentProvider,
        Func<double>? processCpuPercentProvider,
        Func<double?>? hostCpuPercentProvider,
        Func<string, long?>? freeBytesProvider,
        TimeSpan? cacheWindow = null)
    {
        _configuration = configuration;
        _memoryArbiter = memoryArbiter;
        _childProcessTracker = childProcessTracker;
        _memoryLoadPercentProvider = memoryLoadPercentProvider;
        _processCpuPercentProvider = processCpuPercentProvider;
        _hostCpuPercentProvider = hostCpuPercentProvider;
        _freeBytesProvider = freeBytesProvider;
        _cacheWindow = cacheWindow ?? TimeSpan.FromSeconds(1);
    }

    public NodeCapacitySnapshot Capture()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var liveReservedBytes = ComputeAggregateReservedBytes();
            var liveArbiterExhausted = IsArbiterExhausted();

            if (_cachedSnapshot != null
                && _cacheWindow > TimeSpan.Zero
                && (now - _cachedSnapshot.CapturedAtUtc) < _cacheWindow
                && liveReservedBytes == _cachedSnapshot.AggregateWorkerReservedBytes
                && liveArbiterExhausted == _cachedArbiterExhausted)
            {
                return _cachedSnapshot;
            }

            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            double cpuPercent;
            if (_processCpuPercentProvider != null)
            {
                cpuPercent = Math.Clamp(_processCpuPercentProvider(), 0, 100);
            }
            else
            {
                // Processor time is accounted in clock ticks of about 15 ms per thread. Over a window
                // shorter than that, two busy threads read as every core saturated: the first sample
                // after start-up reported the node overloaded. A window that short is not a rate, so
                // the previous reading stands and the baseline keeps accumulating.
                var elapsed = (now - _lastUtc).TotalSeconds;
                cpuPercent = _lastCpuPercent;
                if (elapsed >= MinimumCpuWindowSeconds)
                {
                    var cpuDelta = Math.Max(0, (cpu - _lastCpu).TotalSeconds);
                    cpuPercent = Math.Clamp(
                        cpuDelta / (elapsed * Math.Max(1, Environment.ProcessorCount)) * 100.0,
                        0,
                        100);
                    _lastCpu = cpu;
                    _lastUtc = now;
                    _lastCpuPercent = cpuPercent;
                }
            }

            var hostCpuPercent = _hostCpuPercentProvider != null
                ? _hostCpuPercentProvider()
                : TryGetHostCpuPercent();

            var gc = GC.GetGCMemoryInfo();
            var totalMemory = gc.TotalAvailableMemoryBytes > 0
                ? gc.TotalAvailableMemoryBytes
                : 0;
            var memoryLoad = _memoryLoadPercentProvider != null
                ? Math.Clamp(_memoryLoadPercentProvider(), 0, 100)
                : DefaultSystemResources.GetMemoryLoadPercent(process.WorkingSet64);

            var spillPath = ResolveSpillVolumePath(_configuration);
            long? sampledStateFree = SampleFreeBytes(AppContext.BaseDirectory);
            long? sampledSpillFree = SampleFreeBytes(spillPath);

            long budgetBytes = ResolveEffectiveMemoryBudgetBytes();
            double workerReservationPercent = budgetBytes > 0
                ? Math.Clamp(liveReservedBytes * 100.0 / budgetBytes, 0.0, 100.0)
                : 0.0;
            long spillMinimumFreeBytes = DefaultThresholds.SpillMinimumFreeDiskMB(_configuration) * 1024L * 1024L;

            _isOverloaded = EvaluateOverloadedWithHysteresis(
                currentlyOverloaded: _isOverloaded,
                memoryLoadPercent: memoryLoad,
                processCpuPercent: cpuPercent,
                hostCpuPercent: hostCpuPercent,
                workerReservationPercent: workerReservationPercent,
                budgetBytes: budgetBytes,
                arbiterExhausted: liveArbiterExhausted,
                spillMinimumFreeBytes: spillMinimumFreeBytes,
                sampledSpillFreeBytes: sampledSpillFree,
                configuredHysteresisMB: _configuration?.GetValue<int?>("Orchestration:ResourceManagement:HysteresisMemoryMB"));

            _cachedArbiterExhausted = liveArbiterExhausted;
            _cachedSnapshot = new NodeCapacitySnapshot(
                process.WorkingSet64,
                GC.GetTotalMemory(forceFullCollection: false),
                totalMemory,
                memoryLoad,
                cpuPercent,
                Environment.ProcessorCount,
                _isOverloaded,
                now)
            {
                // Free disk is the most outage-critical host signal. Sample the state volume (app/DB
                // base directory) and the configured spill volume. Both use DriveInfo — cross-platform.
                StateDiskFreeBytes = sampledStateFree ?? 0L,
                SpillDiskFreeBytes = sampledSpillFree ?? 0L,
                HostCpuPercent = hostCpuPercent,
                AggregateWorkerReservedBytes = liveReservedBytes,
                EffectiveMemoryBudgetBytes = budgetBytes,
                WorkerMemoryReservationPercent = workerReservationPercent,
                SpillMinimumFreeBytes = spillMinimumFreeBytes,
                SpillVolumePath = spillPath,
            };

            return _cachedSnapshot;
        }
    }

    internal static string ResolveSpillVolumePath(IConfiguration? configuration)
    {
        if (configuration is not null)
        {
            var spillRoot = configuration["Engine:SpillRoot"];
            if (!string.IsNullOrWhiteSpace(spillRoot))
                return spillRoot;

            var sessionRoot = configuration["Session:Root"];
            if (!string.IsNullOrWhiteSpace(sessionRoot))
                return sessionRoot;

            if (configuration.GetValue<bool>("Orchestration:SandboxExecution:Enabled"))
            {
                var workspaceRoot = configuration["Orchestration:SandboxExecution:WorkspaceRoot"];
                if (!string.IsNullOrWhiteSpace(workspaceRoot))
                    return workspaceRoot;
            }
        }

        return Path.GetTempPath();
    }

    private long ComputeAggregateReservedBytes()
    {
        long arbiterReserved = Math.Max(0L, _memoryArbiter?.ReservedBytes ?? 0L);
        long unaccountedWorkers = Math.Max(0L, _childProcessTracker?.GetUnaccountedWorkerMemoryBytes() ?? 0L);
        return checked(arbiterReserved + unaccountedWorkers);
    }

    private bool IsArbiterExhausted() =>
        _memoryArbiter is BufferManager { IsMemoryExhausted: true }
        or BufferManager { IsSystemMemoryExhausted: true };

    private long ResolveEffectiveMemoryBudgetBytes()
    {
        if (_memoryArbiter is not null)
            return Math.Max(0L, _memoryArbiter.TotalBudgetBytes);

        if (_configuration is not null)
        {
            int effectiveMB = DefaultThresholds.ResolveGlobalMemoryGrantMB(_configuration);
            return effectiveMB > 0 ? (long)effectiveMB * 1024L * 1024L : 0L;
        }

        return 0L;
    }

    private static bool EvaluateOverloadedWithHysteresis(
        bool currentlyOverloaded,
        double memoryLoadPercent,
        double processCpuPercent,
        double? hostCpuPercent,
        double workerReservationPercent,
        long budgetBytes,
        bool arbiterExhausted,
        long spillMinimumFreeBytes,
        long? sampledSpillFreeBytes,
        int? configuredHysteresisMB)
    {
        double effectiveHostCpu = hostCpuPercent ?? 0d;

        if (!currentlyOverloaded)
        {
            bool memoryPressured = (budgetBytes > 0 && workerReservationPercent >= ReservationHighWatermarkPercent)
                || arbiterExhausted;
            bool spillLow = spillMinimumFreeBytes > 0
                && sampledSpillFreeBytes is long freeBytes
                && freeBytes < spillMinimumFreeBytes;

            return memoryLoadPercent >= MemoryLoadHighWatermarkPercent
                || processCpuPercent >= CpuHighWatermarkPercent
                || effectiveHostCpu >= CpuHighWatermarkPercent
                || memoryPressured
                || spillLow;
        }

        double reservationRecoveryPercent = ReservationDefaultRecoveryPercent;
        if (budgetBytes > 0 && configuredHysteresisMB is int hystMB && hystMB > 0)
        {
            long hystBytes = (long)hystMB * 1024L * 1024L;
            long safeBytes = Math.Max(budgetBytes / 2, budgetBytes - hystBytes);
            double safePercent = safeBytes * 100.0 / budgetBytes;
            reservationRecoveryPercent = Math.Min(ReservationDefaultRecoveryPercent, safePercent);
        }

        bool memoryRecovered = !arbiterExhausted
            && (budgetBytes <= 0 || workerReservationPercent <= reservationRecoveryPercent);

        long spillRecoveryBytes = spillMinimumFreeBytes > 0
            ? checked(spillMinimumFreeBytes + Math.Max(spillMinimumFreeBytes / 4, 32L * 1024 * 1024))
            : 0L;
        bool spillRecovered = spillMinimumFreeBytes <= 0
            || sampledSpillFreeBytes is not long availableFree
            || availableFree >= spillRecoveryBytes;

        bool canRecover = memoryLoadPercent <= MemoryLoadRecoveryWatermarkPercent
            && processCpuPercent <= CpuRecoveryWatermarkPercent
            && effectiveHostCpu <= CpuRecoveryWatermarkPercent
            && memoryRecovered
            && spillRecovered;

        return !canRecover;
    }

    private long? SampleFreeBytes(string? path)
    {
        var target = string.IsNullOrWhiteSpace(path) ? AppContext.BaseDirectory : path;
        if (_freeBytesProvider != null)
            return _freeBytesProvider(target);

        return TryGetFreeBytesNullable(target);
    }

    private static long? TryGetFreeBytesNullable(string? path)
    {
        try
        {
            var full = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? AppContext.BaseDirectory : path);
            if (OperatingSystem.IsWindows())
            {
                var root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return null;
                var drive = new DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace : null;
            }

            // On Unix DriveInfo reports free space for the mount point holding an existing path.
            var probe = full;
            while (!string.IsNullOrEmpty(probe) && !Directory.Exists(probe))
            {
                var parent = Path.GetDirectoryName(probe);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, probe, StringComparison.Ordinal))
                    break;
                probe = parent;
            }

            var target = Directory.Exists(probe) ? probe : Path.GetPathRoot(full) ?? "/";
            var unixDrive = new DriveInfo(target);
            return unixDrive.IsReady ? unixDrive.AvailableFreeSpace : null;
        }
        catch
        {
            return null; // Disk stats are best-effort; never fail a capacity capture over them.
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
