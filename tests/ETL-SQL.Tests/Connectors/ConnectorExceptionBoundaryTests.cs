using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("CompatBreak", "0.20.0")]
public sealed class ConnectorExceptionBoundaryTests
{
    [Theory]
    [InlineData("Password=synthetic-secret")]
    [InlineData("Password='synthetic first second'")]
    public void RawProviderExceptionCannotEscapeBoundary(string message)
    {
        var provider = new IOException(message, new Exception("synthetic-inner-secret"));
        var exception = ConnectorExceptionWrapper.Wrap("Test", provider);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("synthetic", exception.ToString());
        Assert.Contains("Test connector error", exception.Message);
    }

    [Fact]
    public void ExecutionExceptionRetainsItsTypeAndRetryContract()
    {
        var original = new AmbiguousGatewayWriteException("test-operation");
        Assert.Same(original, ConnectorExceptionWrapper.Wrap("Test", original));
        Assert.Equal("test-operation", original.OperationId);
    }
}
