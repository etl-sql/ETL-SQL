using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ETL_SQL.Orchestrator.Execution;

public sealed record ChildProcessIdentity(long StartedUtcTicks, string ExecutablePath);

/// <summary>A handle to one process instance, retained across identity verification and termination.</summary>
public interface ITrackedChildProcess : IDisposable
{
    bool HasExited { get; }
    ChildProcessIdentity Identity { get; }
    void Kill();
}

public interface ITrackedChildProcessFactory
{
    ITrackedChildProcess Open(int pid);
}

internal sealed class TrackedChildProcessFactory : ITrackedChildProcessFactory
{
    public ITrackedChildProcess Open(int pid) => new TrackedChildProcess(Process.GetProcessById(pid));

    private sealed class TrackedChildProcess : ITrackedChildProcess
    {
        private readonly Process _process;

        public TrackedChildProcess(Process process)
        {
            _process = process;
            // Keep the OS process handle open so a reused PID cannot replace this instance.
            try { _ = process.SafeHandle; }
            catch { process.Dispose(); throw; }
        }

        public bool HasExited => _process.HasExited;
        public ChildProcessIdentity Identity => new(_process.StartTime.ToUniversalTime().Ticks,
            GetExecutablePath(_process));
        public void Kill() => _process.Kill(entireProcessTree: true);
        public void Dispose() => _process.Dispose();

        private static string GetExecutablePath(Process process)
        {
            if (!OperatingSystem.IsWindows())
                return process.MainModule?.FileName
                    ?? throw new InvalidOperationException("Cannot identify child executable.");

            // A newly started Windows process may not have populated its module list yet.
            // Query the image attached to the retained process handle without waiting for its loader.
            var path = new StringBuilder(32_768);
            var length = path.Capacity;
            if (!QueryFullProcessImageName(process.SafeHandle, 0, path, ref length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return path.ToString();
        }

        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW",
            ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(
            SafeProcessHandle process, int flags, StringBuilder executablePath, ref int length);
    }
}
