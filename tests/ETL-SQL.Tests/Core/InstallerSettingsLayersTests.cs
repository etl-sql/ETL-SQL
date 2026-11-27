using ETL_SQL.Common;
using Microsoft.Extensions.Configuration;
using ConfigurationExtensions = ETL_SQL.Common.ConfigurationExtensions;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// An MSI install layers the release defaults and the installer's choices under the operator's
/// appsettings.json, so an upgrade can replace the defaults without touching the operator's edits
/// (MI01). Elsewhere neither layer exists and appsettings.json is the full file, as before.
/// </summary>
public sealed class InstallerSettingsLayersTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("etlsql-layers-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void DefaultsSitUnderAppSettingsAndSecrets()
    {
        Write(ConfigurationExtensions.DefaultsFileName, """{ "A": "d", "B": "d", "C": "d" }""");
        Write("appsettings.json", """{ "B": "o", "C": "o" }""");
        Write(ConfigurationExtensions.InstalledSecretsFileName, """{ "C": "s" }""");

        var config = new ConfigurationBuilder()
            .SetBasePath(_dir)
            .AddJsonFile("appsettings.json")
            .AddInstallerLayers(_dir, new FakeRegistry(null))
            .AddInstalledSecrets(_dir)
            .Build();

        Assert.Equal("d", config["A"]);
        Assert.Equal("o", config["B"]);
        Assert.Equal("s", config["C"]);
    }

    [Fact]
    public void WebApplicationDefaultsGetLayersBeforeTheirAppSettings()
    {
        Write("appsettings.json", "{}");
        Write("appsettings.Development.json", "{}");
        Write(ConfigurationExtensions.DefaultsFileName, "{}");
        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .SetBasePath(_dir)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json");

        builder.AddInstallerLayers(_dir, Installed());

        var names = builder.Sources.Select(Describe).ToList();
        Assert.Equal(
            ["memory", ConfigurationExtensions.DefaultsFileName, "registry", "appsettings.json", "appsettings.Development.json"],
            names);
    }

    [Theory]
    [InlineData("1", "1", "1", "All")]
    [InlineData("1", "1", "0", "OrchestratorAndSteward")]
    [InlineData("1", "0", "1", "OrchestratorAndReports")]
    [InlineData("0", "1", "1", "StewardAndReports")]
    [InlineData("1", "0", "0", "OrchestratorOnly")]
    [InlineData("0", "1", "0", "DataStewardOnly")]
    [InlineData("0", "0", "1", "ReportOnly")]
    [InlineData("0", "0", "0", "All")]
    public void SurfaceTruthTable(string orch, string steward, string report, string expected)
    {
        var config = Build(Installed(("OrchSurface", orch), ("StewardSurface", steward), ("ReportPortal", report)));

        Assert.Equal(expected, config["Portal:Surface"]);
    }

    [Fact]
    public void NoSurfaceInputs_LeaveSurfaceUnset()
    {
        Assert.Null(Build(Installed())["Portal:Surface"]);
    }

    [Fact]
    public void ModulesAndDatabasePathMap()
    {
        var config = Build(Installed(
            ("Reporting", "1"), ("Designer", "0"), ("Scheduling", "1"), ("Operations", "0"),
            ("OrchestratorDatabasePath", @"C:\Windows\System32\config\systemprofile\AppData\Local\etlsql-data\etlsql.db")));

        Assert.Equal("true", config["Portal:Modules:Reporting"]);
        Assert.Equal("false", config["Portal:Modules:Designer"]);
        Assert.Equal("true", config["Portal:Modules:Scheduling"]);
        Assert.Equal("false", config["Portal:Modules:Operations"]);
        Assert.Equal(@"C:\Windows\System32\config\systemprofile\AppData\Local\etlsql-data\etlsql.db",
            config["Portal:Orchestrator:DatabasePath"]);
    }

    [Fact]
    public void InstallFolderJoinsOperatorSafeZones()
    {
        Write("appsettings.json", """{ "Security": { "ApprovedSafeZones": [ "C:\\a", "C:\\b" ] } }""");

        var zones = Build(Installed()).GetSection("Security:ApprovedSafeZones").Get<string[]>();

        Assert.Equal(["C:\\a", "C:\\b", _dir.TrimEnd(Path.DirectorySeparatorChar)], zones);
    }

    [Fact]
    public void OperatorValueBeatsRegistry()
    {
        Write("appsettings.json", """{ "Portal": { "Surface": "ReportOnly" } }""");

        var config = Build(Installed(("OrchSurface", "1"), ("StewardSurface", "1"), ("ReportPortal", "1")));

        Assert.Equal("ReportOnly", config["Portal:Surface"]);
    }

    [Fact]
    public void OtherFolderGetsNoRegistryValues()
    {
        var registry = new FakeRegistry(new Dictionary<string, string>
        {
            ["InstallFolder"] = Path.Combine(_dir, "elsewhere"),
            ["OrchSurface"] = "1",
        });

        var config = Build(registry);

        Assert.Null(config["Portal:Surface"]);
        Assert.Null(config.GetSection("Security:ApprovedSafeZones").Get<string[]>());
    }

    [Fact]
    public void InstallFolderMatchIgnoresCaseAndTrailingSeparator()
    {
        var values = new Dictionary<string, string> { ["InstallFolder"] = _dir.ToUpperInvariant() + Path.DirectorySeparatorChar };

        Assert.NotNull(InstallerRegistry.MatchingInstallFolder(values, _dir));
        Assert.Null(InstallerRegistry.MatchingInstallFolder(values, _dir + "x"));
        Assert.Null(InstallerRegistry.MatchingInstallFolder(null, _dir));
    }

    private IConfiguration Build(IInstallerRegistry registry)
    {
        if (!File.Exists(Path.Combine(_dir, "appsettings.json"))) Write("appsettings.json", "{}");
        return new ConfigurationBuilder()
            .SetBasePath(_dir)
            .AddJsonFile("appsettings.json")
            .AddInstallerLayers(_dir, registry)
            .Build();
    }

    private FakeRegistry Installed(params (string Name, string Value)[] values)
    {
        var all = values.ToDictionary(v => v.Name, v => v.Value);
        all["InstallFolder"] = _dir;
        return new FakeRegistry(all);
    }

    private static string Describe(IConfigurationSource source) => source switch
    {
        Microsoft.Extensions.Configuration.Json.JsonConfigurationSource json => Path.GetFileName(json.Path!),
        InstallerRegistryConfigurationSource => "registry",
        _ => "memory",
    };

    private void Write(string name, string json) => File.WriteAllText(Path.Combine(_dir, name), json);

    private sealed class FakeRegistry(IReadOnlyDictionary<string, string>? values) : IInstallerRegistry
    {
        public IReadOnlyDictionary<string, string>? ReadInstallValues() => values;
    }
}
