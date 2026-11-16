using System.Diagnostics;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Portability;

namespace ETL_SQL.Tests.Portability;

/// <summary>
/// The standalone <c>etl-sql-tenant-validator</c> as a customer runs it: a real process, judged by its
/// exit code. 0 valid, 3 invalid, 2 could not validate.
/// </summary>
public sealed class TenantValidatorCommandLineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tenant-validator-cli-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("--require-signatures")]                        // a typo of --require-signature
    [InlineData("--operator-key", "--require-signature")]       // an option that would swallow the next flag
    public async Task AnUnrecognisedOrIncompleteArgumentIsRefused_NotIgnored(params string[] arguments)
    {
        // The bundle is unsigned, so it passes whenever the signature requirement is lost.
        await TenantBundleWriter.WriteAsync(_root, Request());

        var (exitCode, _, stderr) = await RunAsync(["--bundle", _root, .. arguments]);

        Assert.True(exitCode == 2, $"Exit {exitCode}: {stderr}");
    }

    [Fact]
    public async Task ACorrectlySpeltSignatureRequirementStillFailsAnUnsignedBundle()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());

        var (exitCode, _, _) = await RunAsync("--bundle", _root, "--require-signature");

        Assert.Equal(3, exitCode);
    }

    [Fact]
    public async Task AManifestTheValidatorCannotInterpret_ExitsTwoInsteadOfCrashing()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        var manifestPath = Path.Combine(_root, TenantBundle.ManifestFileName);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        var components = manifest.Select(p => p.Key)
            .Single(key => key.Equals("components", StringComparison.OrdinalIgnoreCase));
        manifest[components] = null;
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());

        var (exitCode, _, stderr) = await RunAsync("--bundle", _root);

        Assert.True(exitCode == 2, $"Exit {exitCode}: {stderr}");
        Assert.DoesNotContain("   at ", stderr, StringComparison.Ordinal);
    }

    private static TenantBundleRequest Request() => new(
        "bundle-1",
        DateTimeOffset.Parse("2026-08-09T12:00:00Z"),
        "0.18.0",
        "Enterprise",
        "tenant-acme",
        TenantBundleExportMode.ConfigurationAndArtifacts,
        "consistency-2026-08-09T12:00:00Z",
        [
            new TenantBundlePayload("script:daily_load", "artifact", "text/plain",
                "artifacts/daily_load.etlsql", "SELECT 1 AS Value INTO #proof;")
        ],
        [],
        []);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        var validator = Path.Combine(AppContext.BaseDirectory, "etl-sql-tenant-validator.dll");
        Assert.True(File.Exists(validator), $"Expected the validator next to the tests at {validator}");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(validator);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout, await stderr);
    }
}
