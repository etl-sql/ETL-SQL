using System;
using System.Collections.Generic;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>Regression tests for the 2026-11-01 review of ETL-SQL.Connectors.Common.</summary>
public sealed class ConnectorCommonReviewTests
{
    private static IExecutionContext ContextWithAppSettingsWarehouseTimeout()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Connectors:DataWarehouse:DefaultCommandTimeoutSeconds"] = "1800"
            })
            .Build();
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.ServiceProvider).Returns(services.Object);
        return context.Object;
    }

    [Fact]
    public void OltpTimeout_IgnoresTheDataWarehouseDefault()
    {
        var seconds = ConnectorTimeouts.ResolveCommandTimeoutSeconds(
            ContextWithAppSettingsWarehouseTimeout(), new Dictionary<string, string>());

        Assert.Equal(30, seconds);
    }

    [Fact]
    public void OltpTimeout_StillHonoursTimeoutSecondsAndTheCallerFallback()
    {
        var context = ContextWithAppSettingsWarehouseTimeout();

        Assert.Equal(90, ConnectorTimeouts.ResolveCommandTimeoutSeconds(
            context, new Dictionary<string, string> { ["TIMEOUT_SECONDS"] = "90" }));
        Assert.Equal(45, ConnectorTimeouts.ResolveCommandTimeoutSeconds(context, null, 45));
    }

    [Fact]
    public void FtpConnector_BuildsItsConnectionStringFromHost()
    {
        var target = new FtpConnector().BuildConnectionString(
            new Dictionary<string, string> { ["HOST"] = "ftp.example.com" });

        Assert.Equal("ftp.example.com", target);
    }

    [Fact]
    public void Parser_QuoteInsideAnUnquotedPassword_DoesNotSwallowLaterPairs()
    {
        var result = ConnectionStringParser.Parse("Server=db;Password=p'ss;Database=Sales;User Id=u", "MSSQL");

        Assert.Equal("p'ss", result.ExtractedCredential);
        Assert.Equal("Sales", result.Options["DATABASE"]);
        Assert.Equal("u", result.Options["USER"]);
    }

    [Fact]
    public void Parser_BracedOdbcValue_IsUnquotedAndLaterPairsSurvive()
    {
        var result = ConnectionStringParser.Parse("DRIVER={ODBC Driver 18 for SQL Server};UID=sa;PWD=p");

        Assert.Equal("ODBC Driver 18 for SQL Server", result.Options["DRIVER"]);
        Assert.Equal("sa", result.Options["USER"]);
        Assert.Equal("p", result.ExtractedCredential);
    }

    [Fact]
    public void Parser_QuotedValue_KeepsSeparatorsAndDoubledQuotes()
    {
        var result = ConnectionStringParser.Parse("Server=db;Password='a;b''c';Database=Sales", "MSSQL");

        Assert.Equal("a;b'c", result.ExtractedCredential);
        Assert.Equal("Sales", result.Options["DATABASE"]);
    }

    [Fact]
    public void Parser_BracedValue_KeepsADoubledClosingBrace()
    {
        var result = ConnectionStringParser.Parse("DSN=x;PWD={a}}b;c};UID=u");

        Assert.Equal("a}b;c", result.ExtractedCredential);
        Assert.Equal("u", result.Options["USER"]);
    }
}
