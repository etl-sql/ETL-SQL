using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using MySqlConn = ETL_SQL.Connectors.MySql.MySqlConnector;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// The section 6 review reported that the MSSQL, Postgres and MySQL auth probes skip the egress
/// check. The probe has one caller, <see cref="ConnectionDiagnosticEngine"/>, which authorizes the
/// host before any network I/O. What was real: a probe given options without the host key dropped
/// the authorized target host, and a malformed target threw an unwrapped ArgumentException.
/// </summary>
[Trait("Category", "Connectors")]
public sealed class DatabaseDiagnosticAuthProbeTests
{
    public static TheoryData<string, string> BlockedTargets => new()
    {
        { "MSSQL", "SERVER" },
        { "POSTGRES", "HOST" },
        { "MYSQL", "HOST" },
    };

    [Theory]
    [MemberData(nameof(BlockedTargets))]
    public async Task BlockedHost_IsDeniedBeforeTheAuthProbeRuns(string connectorType, string hostKey)
    {
        var security = new SecurityService(NullLogger.Instance) { IsTestMode = false };
        security.AllowedHosts.Clear();
        security.AllowedHosts.Add("allowed.example.com");
        var engine = new ConnectionDiagnosticEngine(new Registry());
        var snapshot = ExecutionPolicySnapshot.Capture(
            EnterprisePolicyRuntime.Current, "unit", ScriptExecutionMode.Batch, "unit-test");

        var report = await engine.DiagnoseAsync(
            "probe", connectorType, string.Empty,
            new Dictionary<string, string>
            {
                [hostKey] = "blocked.example.net",
                ["DATABASE"] = "d",
                ["USER"] = "u",
                ["PASSWORD"] = "p",
            },
            security, snapshot, 1);

        var step = Assert.Single(report.Steps);
        Assert.Equal("POLICY", step.Layer);
        Assert.Equal(DiagnosticStatus.Denied, step.Status);
    }

    [Fact]
    public void SqlServerProbe_OptionsWithoutServer_KeepTheAuthorizedTarget()
    {
        const string target = "Server=allowed.example.com;Database=d";
        var options = new Dictionary<string, string> { ["DATABASE"] = "d", ["USER"] = "u", ["PASSWORD"] = "p" };

        var connector = new SqlServerConnector();
        Assert.Equal("allowed.example.com", connector.GetHost(target, options));
        Assert.Equal(target, connector.BuildDiagnosticConnectionString(target, options));
    }

    [Fact]
    public void PostgresProbe_OptionsWithoutHost_KeepTheAuthorizedTarget()
    {
        const string target = "Host=allowed.example.com;Database=d";
        var options = new Dictionary<string, string> { ["DATABASE"] = "d", ["USER"] = "u", ["PASSWORD"] = "p" };

        var connector = new PostgresConnector();
        Assert.Equal("allowed.example.com", connector.GetHost(target, options));
        Assert.Equal(target, connector.BuildDiagnosticConnectionString(target, options));
    }

    [Fact]
    public void MySqlProbe_OptionsWithoutHost_KeepTheAuthorizedTarget()
    {
        const string target = "Server=allowed.example.com;Database=d";
        var options = new Dictionary<string, string> { ["DATABASE"] = "d", ["USER"] = "u", ["PASSWORD"] = "p" };

        var connector = new MySqlConn();
        Assert.Equal("allowed.example.com", connector.GetHost(target, options));
        Assert.Equal(target, connector.BuildDiagnosticConnectionString(target, options));
    }

    [Fact]
    public void Probes_OptionsWithTheHostKey_DialTheOptionHost()
    {
        var sql = new SqlServerConnector().BuildDiagnosticConnectionString(
            "Server=ignored", new Dictionary<string, string> { ["SERVER"] = "allowed.example.com", ["DATABASE"] = "d" });
        var pg = new PostgresConnector().BuildDiagnosticConnectionString(
            "Host=ignored", new Dictionary<string, string> { ["HOST"] = "allowed.example.com", ["DATABASE"] = "d" });
        var my = new MySqlConn().BuildDiagnosticConnectionString(
            "Server=ignored", new Dictionary<string, string> { ["SERVER"] = "allowed.example.com", ["DATABASE"] = "d" });

        Assert.Contains("allowed.example.com", sql);
        Assert.Contains("allowed.example.com", pg);
        Assert.Contains("allowed.example.com", my);
    }

    public static TheoryData<IConnectionDiagnosticAuthProbe, string> Probes => new()
    {
        { new SqlServerConnector(), "MSSQL" },
        { new PostgresConnector(), "POSTGRES" },
        { new MySqlConn(), "MYSQL" },
    };

    [Theory]
    [MemberData(nameof(Probes))]
    public async Task MalformedTarget_ReportsAuthFailedInsteadOfThrowing(IConnectionDiagnosticAuthProbe probe, string connectorType)
    {
        var steps = await probe.DiagnoseAuthenticationAsync(
            new ConnectionDiagnosticAuthContext("probe", connectorType, "this is not a connection string", null, 1));

        var step = Assert.Single(steps);
        Assert.Equal("AUTH", step.Layer);
        Assert.Equal(DiagnosticStatus.Failed, step.Status);
    }

    private sealed class Registry : IConnectorRegistry
    {
        private readonly IConnector[] _connectors = [new SqlServerConnector(), new PostgresConnector(), new MySqlConn()];
        public void Register(IConnector connector) { }
        public IConnector? GetConnector(string name) =>
            _connectors.FirstOrDefault(c => string.Equals(c.Name, name, System.StringComparison.OrdinalIgnoreCase));
        public IEnumerable<string> GetRegisteredNames() => _connectors.Select(c => c.Name);
        public HashSet<string> GetAllConnectorKeywords() => [];
        public HashSet<string> GetAllConnectorFunctions() => [];
        public Dictionary<string, string[]> GetAllConnectorOptionValues() => [];
        public IEnumerable<ConnectorSchemaDescriptor> GetAllConnectorSchemas() => _connectors.Select(c => c.GetSchemaDescriptor());
        public ConnectorSchemaDescriptor? GetConnectorSchema(string connectorType) =>
            GetConnector(connectorType)?.GetSchemaDescriptor();
    }
}
