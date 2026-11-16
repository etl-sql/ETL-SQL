using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using FluentFTP;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// The FTP page told users to set <c>USE_SSL=TRUE</c>; the connector knew only EXPLICIT, ON and
/// IMPLICIT and read every other value, including a quoted 'TRUE', as no encryption, so following
/// the page sent the password in clear text. Every value is now either understood or an error
/// before any connection opens (ROADMAP RF01).
/// </summary>
[Trait("CompatBreak", "0.22.0")]
[Trait("Connector", "FTP")]
public sealed class FtpSecurityOptionTests
{
    [Theory]
    [InlineData(null, FtpEncryptionMode.None)]
    [InlineData("", FtpEncryptionMode.None)]
    [InlineData("OFF", FtpEncryptionMode.None)]
    [InlineData("false", FtpEncryptionMode.None)]
    [InlineData("NO", FtpEncryptionMode.None)]
    [InlineData("0", FtpEncryptionMode.None)]
    [InlineData("EXPLICIT", FtpEncryptionMode.Explicit)]
    [InlineData(" explicit ", FtpEncryptionMode.Explicit)]
    [InlineData("ON", FtpEncryptionMode.Explicit)]
    [InlineData("TRUE", FtpEncryptionMode.Explicit)]
    [InlineData("YES", FtpEncryptionMode.Explicit)]
    [InlineData("1", FtpEncryptionMode.Explicit)]
    [InlineData("IMPLICIT", FtpEncryptionMode.Implicit)]
    public void UseSsl_KnownValues(string? value, FtpEncryptionMode expected) =>
        Assert.Equal(expected, FtpConnector.ParseEncryptionMode(value));

    [Theory]
    [InlineData("TLS")]
    [InlineData("SSL")]
    [InlineData("REQUIRED")]
    public void UseSsl_UnknownValueIsAnError(string value)
    {
        var error = Assert.Throws<ExecutionException>(() => FtpConnector.ParseEncryptionMode(value));
        Assert.Contains("USE_SSL", error.Message);
        Assert.Contains("EXPLICIT", error.Message);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("ON", true)]
    [InlineData("TRUE", true)]
    [InlineData("OFF", false)]
    [InlineData("FALSE", false)]
    [InlineData("0", false)]
    public void Passive_KnownValues(string? value, bool expected) =>
        Assert.Equal(expected, FtpConnector.ParsePassive(value));

    [Fact]
    public void Passive_UnknownValueIsAnError() =>
        Assert.Contains("PASSIVE", Assert.Throws<ExecutionException>(() => FtpConnector.ParsePassive("ACTIVE")).Message);

    [Fact]
    public void CreateDataSource_RejectsAnUnknownUseSslBeforeConnecting()
    {
        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.Logger).Returns(NullLogger.Instance);
        context.SetupGet(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));
        context.SetupGet(c => c.ServiceProvider).Returns(new Mock<IServiceProvider>().Object);

        var error = Assert.Throws<ExecutionException>(() => new FtpConnector().CreateDataSource(context.Object, "",
            new Dictionary<string, string> { ["HOST"] = "ftp.example.com", ["USE_SSL"] = "TLS" }));
        Assert.Contains("USE_SSL", error.Message);
    }
}
