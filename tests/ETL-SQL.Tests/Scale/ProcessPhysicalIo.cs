using System.Runtime.InteropServices;

namespace ETL_SQL.Tests.Scale;

/// <summary>
/// Process-level I/O transfer counters: Windows <c>GetProcessIoCounters</c> and Linux
/// <c>/proc/self/io</c>. These count bytes the process transferred through the I/O subsystem
/// (Linux <c>read_bytes</c>/<c>write_bytes</c> are storage-layer, Windows transfer counts include
/// cache hits) — the standard per-process proxy for physical I/O attribution in the profiling
/// report. Unsupported platforms report <see cref="Supported"/> = false rather than zeros.
/// </summary>
internal readonly record struct ProcessIoCounters(
    bool Supported,
    long ReadBytes,
    long WriteBytes,
    long ReadOperations,
    long WriteOperations)
{
    public static ProcessIoCounters Capture()
    {
        if (OperatingSystem.IsWindows()) return CaptureWindows();
        if (OperatingSystem.IsLinux()) return CaptureLinux();
        return new ProcessIoCounters(false, 0, 0, 0, 0);
    }

    /// <summary>Counters accumulated between <paramref name="start"/> and <paramref name="end"/>.</summary>
    public static ProcessIoCounters Delta(in ProcessIoCounters start, in ProcessIoCounters end) =>
        start.Supported && end.Supported
            ? new ProcessIoCounters(true,
                Math.Max(0, end.ReadBytes - start.ReadBytes),
                Math.Max(0, end.WriteBytes - start.WriteBytes),
                Math.Max(0, end.ReadOperations - start.ReadOperations),
                Math.Max(0, end.WriteOperations - start.WriteOperations))
            : new ProcessIoCounters(false, 0, 0, 0, 0);

    private static ProcessIoCounters CaptureWindows()
    {
        return GetProcessIoCounters(GetCurrentProcess(), out var counters)
            ? new ProcessIoCounters(true,
                unchecked((long)counters.ReadTransferCount),
                unchecked((long)counters.WriteTransferCount),
                unchecked((long)counters.ReadOperationCount),
                unchecked((long)counters.WriteOperationCount))
            : new ProcessIoCounters(false, 0, 0, 0, 0);
    }

    private static ProcessIoCounters CaptureLinux()
    {
        try
        {
            long readBytes = 0, writeBytes = 0, readOps = 0, writeOps = 0;
            foreach (var line in File.ReadLines("/proc/self/io"))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0) continue;
                var key = line[..separator];
                if (!long.TryParse(line[(separator + 1)..].Trim(), out var value)) continue;
                switch (key)
                {
                    case "read_bytes": readBytes = value; break;
                    case "write_bytes": writeBytes = value; break;
                    case "syscr": readOps = value; break;
                    case "syscw": writeOps = value; break;
                }
            }
            return new ProcessIoCounters(true, readBytes, writeBytes, readOps, writeOps);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ProcessIoCounters(false, 0, 0, 0, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr processHandle, out IO_COUNTERS counters);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
