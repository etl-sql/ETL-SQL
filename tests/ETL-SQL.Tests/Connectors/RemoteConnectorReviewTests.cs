using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>Regression tests for the 2026-11-01 review of ETL-SQL.Connectors.Remote.</summary>
public sealed class RemoteConnectorReviewTests
{
    private static Mock<IExecutionContext> Context()
    {
        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.Logger).Returns(NullLogger.Instance);
        context.SetupGet(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));
        context.SetupGet(c => c.ServiceProvider).Returns(new Mock<IServiceProvider>().Object);
        context.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
        return context;
    }

    // ── SFTP / FTP ───────────────────────────────────────────────────────────

    [Fact]
    public void Sftp_CreateDataSource_TakesTheHostFromOptions()
    {
        var source = new SftpConnector().CreateDataSource(Context().Object, "", new Dictionary<string, string>
        {
            ["HOST"] = "sftp.example.com",
            ["USER"] = "u",
            ["PASSWORD"] = "p"
        });

        Assert.Equal("sftp://sftp.example.com", source.Path);
    }

    [Fact]
    public void Sftp_CreateDataSource_TakesAnInlinePortFromTheHostOption()
    {
        var source = new SftpConnector().CreateDataSource(Context().Object, "", new Dictionary<string, string>
        {
            ["HOST"] = "sftp.example.com:2222",
            ["USER"] = "u"
        });

        Assert.Equal("sftp://sftp.example.com:2222", source.Path);
    }

    [Fact]
    public void Ftp_CreateDataSource_TakesTheHostFromOptions()
    {
        var source = new FtpConnector().CreateDataSource(Context().Object, "", new Dictionary<string, string>
        {
            ["HOST"] = "ftp.example.com",
            ["USER"] = "u",
            ["PASSWORD"] = "p"
        });

        Assert.Equal("ftp://ftp.example.com", source.Path);
    }

    private sealed class SingleConnectorRegistry(IConnector connector) : IConnectorRegistry
    {
        public void Register(IConnector c) { }
        public IConnector? GetConnector(string name) =>
            string.Equals(name, connector.Name, StringComparison.OrdinalIgnoreCase) ? connector : null;
        public IEnumerable<string> GetRegisteredNames() => [connector.Name];
        public HashSet<string> GetAllConnectorKeywords() => [];
        public HashSet<string> GetAllConnectorFunctions() => [];
        public Dictionary<string, string[]> GetAllConnectorOptionValues() => [];
        public IEnumerable<ConnectorSchemaDescriptor> GetAllConnectorSchemas() => [connector.GetSchemaDescriptor()];
        public ConnectorSchemaDescriptor? GetConnectorSchema(string connectorType) => connector.GetSchemaDescriptor();
    }

    [Fact]
    public async Task Sftp_AuthProbe_IsNeverReachedForAFencedHost()
    {
        // DiagnoseAuthenticationAsync has no execution context of its own; its only caller, the
        // diagnostic engine, authorizes the destination before any probe runs.
        var engine = new ConnectionDiagnosticEngine(new SingleConnectorRegistry(new SftpConnector()));
        var snapshot = ETL_SQL.Core.Governance.ExecutionPolicySnapshot.Capture(
            ETL_SQL.Core.Governance.EnterprisePolicyRuntime.Current, "unit",
            ETL_SQL.Core.Governance.ScriptExecutionMode.Batch, "unit-test");

        var report = await engine.DiagnoseAsync("probe", "SFTP", "",
            new Dictionary<string, string> { ["HOST"] = "169.254.169.254", ["USER"] = "u", ["PASSWORD"] = "p" },
            new SecurityService(NullLogger.Instance), snapshot, 5, default);

        var policy = Assert.Single(report.Steps);
        Assert.Equal("POLICY", policy.Layer);
        Assert.Equal(DiagnosticStatus.Denied, policy.Status);
    }

    // ── Active Directory ─────────────────────────────────────────────────────

    [Fact]
    public async Task ActiveDirectory_GetVersionOnTheRegisteredConnector_ProbesTheGivenHost()
    {
        // The registered connector's own host is "localhost"; the probe must use the connection
        // string it is handed, so a fenced host is refused instead of localhost being bound.
        var ex = await Record.ExceptionAsync(() =>
            new ActiveDirectoryConnector().GetVersionAsync(Context().Object, "ldap://169.254.169.254:389/DC=x"));

        Assert.IsType<SecurityException>(ex);
    }

    [Theory]
    [InlineData("custom", "(objectClass=custom)")]
    [InlineData("x)(objectClass=*", "(objectClass=x\\29\\28objectClass=\\2a)")]
    [InlineData("a\\b", "(objectClass=a\\5cb)")]
    public void ActiveDirectory_TableNameIsEscapedInTheObjectClassFilter(string table, string expected)
    {
        var connector = new ActiveDirectoryConnector(Context().Object, "ldap://dc.example.com", null);

        var clone = (ActiveDirectoryConnector)connector.WithTable(table);

        Assert.Equal(expected, clone.ResolveLdapFilter());
    }

    [Fact]
    public void ActiveDirectory_WithTable_KeepsPortTlsAndBaseDnFromTheConnectionString()
    {
        var connector = new ActiveDirectoryConnector(Context().Object, "ldaps://dc.example.com:636/DC=corp,DC=example", null);

        var clone = (ActiveDirectoryConnector)connector.WithTable("groups");

        Assert.Equal(connector.Path, clone.Path);
        Assert.True(clone.UsesSsl);
    }

    [Fact]
    public async Task ActiveDirectory_ReadFailure_IsAnExecutionException()
    {
        var connector = new ActiveDirectoryConnector(Context().Object, "ldap://127.0.0.1:1", new Dictionary<string, string>
        {
            ["AUTH_MODE"] = "SIMPLE",
            ["USER"] = "u",
            ["PASSWORD"] = "p"
        });

        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in connector.ReadBatches()) { }
        });

        Assert.IsType<ExecutionException>(ex);
    }
}
