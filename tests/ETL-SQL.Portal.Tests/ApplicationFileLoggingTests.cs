using ETL_SQL.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class ApplicationFileLoggingTests
{
    [Fact]
    public void HostEventReachesConfiguredFileWithScopesAndSanitizedException()
    {
        using var factory = new LoggingFactory();
        using var client = factory.CreateClient();
        var logger = factory.Services.GetRequiredService<ILogger<ApplicationFileLoggingTests>>();
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = "tenant-check",
            ["CorrelationId"] = "correlation-check",
            ["RunId"] = "run-check",
            ["AttemptId"] = "attempt-check",
            ["NodeId"] = "node-check",
            ["Password"] = "synthetic-scope-secret"
        }))
        {
            logger.LogError(new InvalidOperationException("PASSWORD='synthetic exception secret'"),
                "host-file-marker {Count} {Details}", 7, new { Password = "synthetic-object-secret", Safe = "detail-check" });
        }
        factory.Services.GetRequiredService<LoggerService>().Info("engine-single-marker {Count}", 7);
        var text = string.Join('\n', Directory.GetFiles(factory.LogDirectory, "*.log").Select(path =>
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            return reader.ReadToEnd();
        }));
        Assert.Single(text.Split('\n'), line => line.Contains("host-file-marker"));
        Assert.Single(text.Split('\n'), line => line.Contains("engine-single-marker"));
        foreach (var expected in new[] { "tenant-check", "correlation-check", "run-check", "attempt-check", "node-check", "detail-check", "InvalidOperationException" })
            Assert.Contains(expected, text);
        Assert.DoesNotContain("synthetic", text);
    }

    private sealed class LoggingFactory : PortalWebFactory
    {
        protected override bool EnableApplicationFileLogging => true;
        public string LogDirectory => Path.Combine(TempDir, "application-logs");
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
            => settings["Logging:AppLog:Directory"] = LogDirectory;
    }
}
