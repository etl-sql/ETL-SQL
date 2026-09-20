using System;
using System.Collections.Generic;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors.FlatFile;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

public sealed class RequiredPathContextTests
{
    [Fact]
    public void FlatFile_RejectsMissingContextBeforeOpeningAPath()
    {
        var error = Assert.Throws<ArgumentNullException>(() =>
            new FlatFileDataSource(null, "untrusted.csv"));
        Assert.Equal("context", error.ParamName);
    }

    [Fact]
    public void Smtp_RejectsMissingContextBeforeSendingOrOpeningAttachments()
    {
        var error = Assert.Throws<ArgumentNullException>(() =>
            new SmtpDataSource(null!, new Dictionary<string, string>()));
        Assert.Equal("context", error.ParamName);
    }
}
