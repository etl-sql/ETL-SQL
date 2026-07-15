using System.Diagnostics;
using System.Security.Claims;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Services;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests;

public class PortalScriptSourceControlServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "etlsql-portal-git-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CommitScript_CommitsChangedScript()
    {
        Directory.CreateDirectory(_root);
        var reports = Path.Combine(_root, "Reports");
        Directory.CreateDirectory(reports);
        var script = Path.Combine(reports, "sales.rptsql");
        await File.WriteAllTextAsync(script, "SELECT 1 AS Value;");

        await GitAsync("init");
        await GitAsync("config", "user.name", "Test User");
        await GitAsync("config", "user.email", "test@example.local");
        await GitAsync("add", ".");
        await GitAsync("commit", "-m", "initial");

        var service = NewService();
        var before = await service.GetCurrentRevisionAsync();

        await File.WriteAllTextAsync(script, "SELECT 2 AS Value;");
        var result = await service.CommitScriptAsync("sales.rptsql", Principal());

        Assert.True(result.Committed);
        Assert.False(string.IsNullOrWhiteSpace(result.Revision));
        Assert.NotEqual(before, result.Revision);
    }

    [Fact]
    public void ValidateScriptTextForCommit_RejectsPlaintextCredentialOptions()
    {
        var service = NewService();

        Assert.Throws<InvalidOperationException>(() =>
            service.ValidateScriptTextForCommit("CREATE CONNECTION c AS MSSQL(PASSWORD = 'plain');"));
        service.ValidateScriptTextForCommit("CREATE CONNECTION c AS MSSQL(PASSWORD = 'SECRET:db-password');");
        service.ValidateScriptTextForCommit("CREATE CONNECTION c AS MSSQL(PASSWORD = 'ENC:abc');");
    }

    private PortalScriptSourceControlService NewService() => new(new PortalConfig
    {
        ScriptRootPath = Path.Combine(_root, "Reports"),
        SourceControl = new PortalSourceControlConfig
        {
            Enabled = true,
            Provider = "Git",
            RepositoryRoot = _root,
            CommitterName = "Portal Bot",
            CommitterEmail = "portal@example.local"
        }
    });

    private async Task GitAsync(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {stdout}{stderr}");
    }

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Name, "portal-user")
        ],
        "Test"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            ClearAttributes(_root);
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void ClearAttributes(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(directory, FileAttributes.Normal);
        File.SetAttributes(path, FileAttributes.Normal);
    }
}
