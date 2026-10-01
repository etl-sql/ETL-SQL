using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using Xunit;

namespace ETL_SQL.Review;

public sealed class LoggingReproductions
{
    [Fact]
    public void ConnectorBoundaryMustNotRetainRawProviderCredentials()
    {
        const string syntheticPassword = "synthetic-review-password";
        var wrapped = ConnectorExceptionWrapper.Wrap("Review", new IOException("Password=" + syntheticPassword));
        Assert.DoesNotContain(syntheticPassword, wrapped.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredEngineLogMustMaskArgumentsNamedPassword()
    {
        using var logger = new LoggerService { SuppressConsole = true };
        string? message = null;
        logger.OnMessage += (text, _, _) => message = text;
        const string syntheticPassword = "synthetic-review-only-value";
        logger.Info("Authentication failed for {Password}", syntheticPassword);
        Assert.NotNull(message);
        Assert.DoesNotContain(syntheticPassword, message, StringComparison.Ordinal);
    }
}
