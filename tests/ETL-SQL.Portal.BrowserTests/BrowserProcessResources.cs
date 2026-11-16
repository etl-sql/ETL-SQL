using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ETL_SQL.Portal.BrowserTests;

internal sealed record BrowserProcessIdentity(int ProcessId, string Role);

internal sealed record BrowserProcessResourceSnapshot(
    int ProcessId,
    string Role,
    int? HandleCount,
    int? TcpV4Endpoints,
    int? TcpV6Endpoints,
    int? UdpV4Endpoints,
    int? UdpV6Endpoints,
    IReadOnlyList<string> Unavailable);

internal static class BrowserProcessResources
{
    private const uint InetV4 = 2;
    private const uint InetV6 = 23;
    private const uint TcpOwnerPidAll = 5;
    private const uint UdpOwnerPid = 1;

    internal static IReadOnlyList<BrowserProcessResourceSnapshot> Capture(IEnumerable<BrowserProcessIdentity> identities)
    {
        var selected = identities.Where(identity => identity.ProcessId > 0)
            .DistinctBy(identity => identity.ProcessId).Take(64).ToArray();
        var pids = selected.Select(identity => identity.ProcessId).ToHashSet();
        var tables = OperatingSystem.IsWindows()
            ? new[]
            {
                ReadTable(GetExtendedTcpTable, pids, InetV4, TcpOwnerPidAll, 24, 20, "tcp-v4"),
                ReadTable(GetExtendedTcpTable, pids, InetV6, TcpOwnerPidAll, 56, 52, "tcp-v6"),
                ReadTable(GetExtendedUdpTable, pids, InetV4, UdpOwnerPid, 12, 8, "udp-v4"),
                ReadTable(GetExtendedUdpTable, pids, InetV6, UdpOwnerPid, 28, 24, "udp-v6")
            }
            : Enumerable.Range(0, 4).Select(_ => new EndpointTable(null, "socket-counts: unsupported-platform")).ToArray();
        var snapshots = new List<BrowserProcessResourceSnapshot>();
        foreach (var identity in selected)
        {
            var unavailable = tables.Select(table => table.Error).OfType<string>().Distinct().ToList();
            int? handles = null;
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                handles = process.HandleCount;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                Win32Exception or NotSupportedException)
            {
                unavailable.Add($"handles: {exception.GetType().Name}");
            }
            int? Count(int index) => tables[index].Counts?.GetValueOrDefault(identity.ProcessId, 0);
            snapshots.Add(new BrowserProcessResourceSnapshot(identity.ProcessId, identity.Role, handles,
                Count(0), Count(1), Count(2), Count(3), unavailable));
        }
        return snapshots;
    }

    private sealed record EndpointTable(Dictionary<int, int>? Counts, string? Error);

    private delegate uint ReadNativeTable(IntPtr table, ref uint size, bool order, uint addressFamily,
        uint tableClass, uint reserved);

    // IP Helper OWNER_PID rows use DWORD alignment. Only the owning PID is read; addresses and
    // ports never leave the native buffer. TCP rows include listening and connected endpoints.
    private static EndpointTable ReadTable(ReadNativeTable read, HashSet<int> pids, uint addressFamily,
        uint tableClass, int rowBytes, int pidOffset, string label)
    {
        const uint insufficientBuffer = 122;
        const uint maximumBytes = 16 * 1024 * 1024;
        uint size = 0;
        try
        {
            var code = read(IntPtr.Zero, ref size, false, addressFamily, tableClass, 0);
            if (code is not (0 or insufficientBuffer)) return new(null, $"{label}: win32-{code}");
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (size < sizeof(uint) || size > maximumBytes) return new(null, $"{label}: invalid-buffer-size");
                var allocatedBytes = size;
                var buffer = Marshal.AllocHGlobal(checked((int)allocatedBytes));
                try
                {
                    code = read(buffer, ref size, false, addressFamily, tableClass, 0);
                    if (code == insufficientBuffer) continue;
                    if (code != 0) return new(null, $"{label}: win32-{code}");
                    var rowCount = unchecked((uint)Marshal.ReadInt32(buffer));
                    if (sizeof(uint) + (long)rowCount * rowBytes > allocatedBytes)
                        return new(null, $"{label}: invalid-row-count");
                    var counts = new Dictionary<int, int>();
                    for (var index = 0; index < rowCount; index++)
                    {
                        var pid = Marshal.ReadInt32(buffer, checked(sizeof(uint) + index * rowBytes + pidOffset));
                        if (pids.Contains(pid)) counts[pid] = counts.GetValueOrDefault(pid) + 1;
                    }
                    return new(counts, null);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            return new(null, $"{label}: table-changed-during-read");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or
            OverflowException or Win32Exception)
        {
            return new(null, $"{label}: {exception.GetType().Name}");
        }
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order,
        uint addressFamily, uint tableClass, uint reserved);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order,
        uint addressFamily, uint tableClass, uint reserved);
}
