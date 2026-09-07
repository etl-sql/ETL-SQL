using System.Diagnostics;

namespace ETL_SQL.Engine.Handlers;

public interface IToolProcess : IDisposable
{
    void Start();
    StreamWriter StandardInput { get; }
    StreamReader StandardOutput { get; }
    StreamReader StandardError { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken token);
    void Kill();
}

public interface IToolProcessFactory
{
    IToolProcess Create(ProcessStartInfo startInfo);
}

internal sealed class ToolProcessFactory : IToolProcessFactory
{
    public IToolProcess Create(ProcessStartInfo startInfo) => new ToolProcess(startInfo);

    private sealed class ToolProcess(ProcessStartInfo startInfo) : IToolProcess
    {
        private readonly Process _process = new() { StartInfo = startInfo };
        public void Start()
        {
            if (!_process.Start()) throw new InvalidOperationException("Tool process did not start.");
        }
        public StreamWriter StandardInput => _process.StandardInput;
        public StreamReader StandardOutput => _process.StandardOutput;
        public StreamReader StandardError => _process.StandardError;
        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;
        public Task WaitForExitAsync(CancellationToken token) => _process.WaitForExitAsync(token);
        public void Kill() => _process.Kill(entireProcessTree: true);
        public void Dispose() => _process.Dispose();
    }
}
