using ETL_SQL.App.Portability;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Portability;

namespace ETL_SQL.Tests.Portability;

/// <summary>
/// The <c>admin tenant</c> verbs. These are the customer-side surface: someone handed a bundle must
/// be able to check it with the shipped binary and a published key, so the exit code and the printed
/// reason are the product, not decoration.
/// </summary>
public sealed class TenantPortabilityAdminServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tenant-cli-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<string> _messages = [];
        public string Text => string.Join(Environment.NewLine, _messages);
        public string? SessionId { get; set; }
        public bool IsDebugEnabled => true;
        public bool IsVerboseEnabled => true;
        public bool IsVerbose { get; set; }
        public bool SuppressConsole { get; set; }
        public bool IsJsonMode { get; set; }
        public event Action<string, string?, ConsoleColor>? OnMessage;

        public void Log(LogLevel level, string message, Exception? ex = null)
        {
            _messages.Add(message);
            OnMessage?.Invoke(message, null, ConsoleColor.White);
        }

        public void WriteLine(string message, ConsoleColor color = ConsoleColor.White) => _messages.Add(message);
    }

    private static TenantBundleRequest Request() => new(
        "bundle-1",
        DateTimeOffset.Parse("2026-08-10T09:00:00Z"),
        "0.18.0", "Enterprise", "tenant-acme",
        TenantBundleExportMode.ConfigurationAndArtifacts,
        "consistency-1",
        [new TenantBundlePayload("script:daily_load", "artifact", "text/plain",
            "artifacts/daily_load.etlsql", "SELECT 1;")],
        [new TenantBundleRequiredBinding("SECRET:sales", "secret", "Provision it at the target.")],
        [new TenantBundleExclusion("dataset:snapshot", "dataset", "Content does not travel.",
            "Transfer it separately.")]);

    private static async Task<(int Code, string Output)> RunAsync(string command, CliContext ctx)
    {
        var logger = new CapturingLogger();
        ctx.Command = command;
        var code = await InvokeAsync(ctx, logger);
        return (code, logger.Text);
    }

    // Internal to ETL-SQL.App, reachable here through the project's existing InternalsVisibleTo.
    private static Task<int> InvokeAsync(CliContext ctx, ILogger logger) =>
        ETL_SQL.App.Portability.TenantPortabilityAdminService.RunAsync(ctx, logger);

    [Fact]
    public async Task ValidateReportsAValidBundleAndSaysWhatItDidNotCheck()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());

        var (code, output) = await RunAsync("admin-tenant-validate",
            new CliContext { TenantBundleRoot = _root });

        Assert.Equal((int)TenantPortabilityExitCode.Ok, code);
        Assert.Contains("Bundle valid.", output, StringComparison.Ordinal);
        // Integrity was checked, authenticity was not, and the operator is told so rather than left
        // to assume a green result covered both.
        Assert.Contains("authenticity was not", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateSeparatesATamperedBundleFromAnUnverifiableSignature()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        await File.WriteAllTextAsync(Path.Combine(_root, "artifacts", "daily_load.etlsql"), "DROP TABLE x;");

        var (code, output) = await RunAsync("admin-tenant-validate",
            new CliContext { TenantBundleRoot = _root });

        Assert.Equal((int)TenantPortabilityExitCode.BundleInvalid, code);
        Assert.Contains("do not import it", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreflightListsOutstandingBindingsAndExitsDistinctly()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());

        var (code, output) = await RunAsync("admin-tenant-preflight",
            new CliContext { TenantBundleRoot = _root });

        Assert.Equal((int)TenantPortabilityExitCode.BindingsRequired, code);
        Assert.Contains("SECRET:sales", output, StringComparison.Ordinal);
        // What will not travel is printed too, not just what is missing.
        Assert.Contains("dataset:snapshot", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreflightIsCleanOnceTheTargetDeclaresItsBindings()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());

        var (code, output) = await RunAsync("admin-tenant-preflight",
            new CliContext { TenantBundleRoot = _root, TenantBindings = ["SECRET:sales"] });

        Assert.Equal((int)TenantPortabilityExitCode.Ok, code);
        Assert.Contains("Preflight clean", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreflightAcceptsImportStyleSourceTargetBindings()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());

        var (code, output) = await RunAsync("admin-tenant-preflight",
            new CliContext { TenantBundleRoot = _root, TenantBindings = ["SECRET:sales=SECRET:prod-sales"] });

        Assert.Equal((int)TenantPortabilityExitCode.Ok, code);
        Assert.Contains("Preflight clean", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportBindingMapRequiresMappingsAndRejectsDuplicates()
    {
        var map = TenantPortabilityAdminService.ParseBindingMap(
            ["SECRET:source=SECRET:target", "SHARED:dev=SHARED:prod"]);

        Assert.Equal("SECRET:target", map["SECRET:source"]);
        Assert.Throws<ArgumentException>(() =>
            TenantPortabilityAdminService.ParseBindingMap(["SECRET:source"]));
        Assert.Throws<ArgumentException>(() =>
            TenantPortabilityAdminService.ParseBindingMap(
                ["SECRET:source=SECRET:a", "SECRET:source=SECRET:b"]));
    }

    [Fact]
    public async Task AMissingBundleArgumentFailsWithoutATraceback()
    {
        var (code, output) = await RunAsync("admin-tenant-validate", new CliContext());

        Assert.Equal((int)TenantPortabilityExitCode.BundleInvalid, code);
        Assert.Contains("--bundle is required", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingBundleDirectoryIsNotFoundRatherThanInvalid()
    {
        var (code, _) = await RunAsync("admin-tenant-preflight",
            new CliContext { TenantBundleRoot = Path.Combine(_root, "nowhere") });

        Assert.Equal((int)TenantPortabilityExitCode.NotFound, code);
    }
}
