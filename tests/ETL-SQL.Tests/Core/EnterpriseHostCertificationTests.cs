using System.Runtime.CompilerServices;

namespace ETL_SQL.Tests.Core;

public sealed class EnterpriseHostCertificationTests
{
    public static TheoryData<string, string, string> ExecutableHosts => new()
    {
        { "Portal", "src/ETL-SQL.Portal/Program.cs", "WebApplication.CreateBuilder" },
        { "Orchestrator", "src/ETL-SQL.Orchestrator.Service/Program.cs", "WebApplication.CreateBuilder" },
        { "CLI", "src/ETL-SQL.App/Program.cs", "DependencyInjectionSetup.BuildServiceProvider" },
        { "TUI", "src/ETL-SQL.TUI/Program.cs", "TuiDependencyInjectionSetup.BuildServiceProvider" },
        { "Report Player", "src/ETL-SQL.ReportPlayer/Program.cs", "WebApplication.CreateBuilder" },
        { "Report Builder", "src/ETL-SQL.ReportBuilder.CLI/Program.cs", "args[0].ToLowerInvariant() switch" },
        { "Language Server", "src/ETL-SQL.LanguageServer/Program.cs", "LanguageServer.From" }
    };

    public static TheoryData<string, string, string, string> ConfigurationHosts => new()
    {
        { "Portal", "src/ETL-SQL.Portal/Program.cs", "builder.Configuration.AddEnterprisePolicy();", "builder.Services.AddEtlSqlEngine" },
        { "Orchestrator", "src/ETL-SQL.Orchestrator.Service/Program.cs", ".AddEnterprisePolicy();", "builder.Services.AddEtlSqlEngine" },
        { "CLI and Report Builder", "src/ETL-SQL.App/App/DependencyInjectionSetup.cs", "builder.AddEnterprisePolicy();", "var configuration = builder.Build();" },
        { "TUI", "src/ETL-SQL.TUI/App/TuiDependencyInjectionSetup.cs", ".AddEnterprisePolicy()", ".Build();" },
        { "Report Player", "src/ETL-SQL.ReportPlayer/Program.cs", "builder.Configuration.AddEnterprisePolicy();", "builder.Services.AddEtlSqlEngine" },
        { "Language Server", "src/ETL-SQL.LanguageServer/Program.cs", ".AddEnterprisePolicy()", ".Build();" }
    };

    [Theory]
    [MemberData(nameof(ExecutableHosts))]
    public void ExecutableHost_InitializesEnterprisePolicyBeforeComposition(
        string host, string relativePath, string compositionMarker)
    {
        var source = ReadRepoFile(relativePath);
        var initialization = source.IndexOf(
            "EnterprisePolicyRuntime.InitializeFromMachineAsync", StringComparison.Ordinal);
        var composition = source.IndexOf(compositionMarker, StringComparison.Ordinal);

        Assert.True(initialization >= 0, $"{host} does not initialize enterprise policy.");
        Assert.True(composition >= 0, $"{host} composition marker '{compositionMarker}' was not found.");
        Assert.True(initialization < composition,
            $"{host} must initialize enterprise policy before composing host services.");
    }

    [Theory]
    [MemberData(nameof(ConfigurationHosts))]
    public void ConfigurationHost_AppliesEnterprisePolicyBeforeConfigurationIsConsumed(
        string host, string relativePath, string policyMarker, string consumerMarker)
    {
        var source = ReadRepoFile(relativePath);
        var policy = source.IndexOf(policyMarker, StringComparison.Ordinal);
        var consumer = source.IndexOf(consumerMarker, StringComparison.Ordinal);

        Assert.True(policy >= 0, $"{host} does not add enterprise policy configuration.");
        Assert.True(consumer >= 0, $"{host} configuration consumer '{consumerMarker}' was not found.");
        Assert.True(policy < consumer,
            $"{host} must add enterprise policy before configuration-bound services are created.");
    }

    [Fact]
    public void SpawnedJobRunners_EnterCliAfterEnterpriseInitialization()
    {
        var program = ReadRepoFile("src/ETL-SQL.App/Program.cs");
        var initialization = program.IndexOf(
            "EnterprisePolicyRuntime.InitializeFromMachineAsync", StringComparison.Ordinal);
        var runnerDispatch = program.IndexOf("WarmJobRunner.RunAsync", StringComparison.Ordinal);

        Assert.True(initialization >= 0 && runnerDispatch >= 0 && initialization < runnerDispatch,
            "Warm runners must initialize machine enterprise policy before accepting jobs.");

        var executor = ReadRepoFile("src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs");
        Assert.Contains("psi.ArgumentList.Add(\"runner\")", executor, StringComparison.Ordinal);
        Assert.Contains("new List<string> { \"run\", scriptFile, \"--json\" }", executor,
            StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[]
                 {
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                     Path.GetDirectoryName(sourceFilePath) ?? string.Empty
                 })
        {
            var current = new DirectoryInfo(start);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "ETL-SQL.slnx")))
                    return current.FullName;

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the ETL-SQL repository root.");
    }
}
