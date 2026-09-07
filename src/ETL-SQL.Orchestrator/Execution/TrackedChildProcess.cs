using System.Diagnostics;

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
            _process.MainModule?.FileName ?? throw new InvalidOperationException("Cannot identify child executable."));
        public void Kill() => _process.Kill(entireProcessTree: true);
        public void Dispose() => _process.Dispose();
    }
}
