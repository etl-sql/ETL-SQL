using System.Diagnostics;
using System.ServiceProcess;
using System.Text.Json;

namespace ETL_SQL.ToolArtifactServiceFixture;

internal sealed class ProbeService(ProbeSettings settings) : ServiceBase
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
            return 2;

        string path = Path.Combine(AppContext.BaseDirectory, "probe-settings.json");
        using var input = File.OpenRead(path);
        var settings = JsonSerializer.Deserialize<ProbeSettings>(input)
            ?? throw new InvalidDataException("Missing qualification settings.");
        if (args[0] == "--console")
            return RunProbeAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

        using var service = new ProbeService(settings) { ServiceName = args[0], AutoLog = false, CanStop = true };
        Run(service);
        return service.ExitCode;
    }

    private readonly CancellationTokenSource stopping = new();
    private Task<int>? probe;

    protected override void OnStart(string[] args) =>
        probe = Task.Run(() => RunProbeAsync(settings, stopping.Token));

    protected override void OnStop()
    {
        stopping.Cancel();
        probe?.Wait(TimeSpan.FromSeconds(10));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            stopping.Dispose();
        base.Dispose(disposing);
    }

    private static async Task<int> RunProbeAsync(ProbeSettings settings, CancellationToken stopToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var start = new ProcessStartInfo(settings.PowerShell)
        {
            WorkingDirectory = settings.WorkDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string arg in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", settings.VerificationScript,
            "-FixtureRoot", settings.FixtureRoot, "-AsJson"
        })
        {
            start.ArgumentList.Add(arg);
        }
        start.Environment["TEMP"] = settings.WorkDirectory;
        start.Environment["TMP"] = settings.WorkDirectory;

        ProbeResult result;
        try
        {
            using var worker = Process.Start(start) ?? throw new InvalidOperationException("Could not launch the probe.");
            try
            {
                var stdout = worker.StandardOutput.ReadToEndAsync(deadline.Token);
                var stderr = worker.StandardError.ReadToEndAsync(deadline.Token);
                await worker.WaitForExitAsync(deadline.Token);
                result = new ProbeResult(worker.ExitCode, await stdout, await stderr);
            }
            catch (OperationCanceledException)
            {
                if (!worker.HasExited)
                    worker.Kill(entireProcessTree: true);
                throw;
            }
        }
        catch (Exception e)
        {
            result = new ProbeResult(1, "", e.Message);
        }

        var temporaryReceipt = settings.ReceiptPath + ".tmp";
        await File.WriteAllTextAsync(temporaryReceipt, JsonSerializer.Serialize(result), CancellationToken.None);
        File.Move(temporaryReceipt, settings.ReceiptPath, overwrite: true);
        return result.ExitCode;
    }
}

internal sealed record ProbeSettings(
    string PowerShell, string VerificationScript, string FixtureRoot, string WorkDirectory, string ReceiptPath);

internal sealed record ProbeResult(int ExitCode, string Stdout, string Stderr);
