using System.Text.Json;
using ETL_SQL.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class OrchestratorFileLoggingTests
{
    [Fact]
    public void HostAndEngineShareConfiguredRollingJsonSink()
    {
        using var factory = new LoggingFactory();
        Directory.CreateDirectory(factory.LogDirectory);
        var expired = Path.Combine(factory.LogDirectory, "etlsql-expired.log");
        File.WriteAllText(expired, "expired");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-5));
        using var client = factory.CreateClient();
        var engine = factory.Services.GetRequiredService<LoggerService>();
        engine.SuppressConsole = true;
        var host = factory.Services.GetRequiredService<ILogger<OrchestratorFileLoggingTests>>();
        engine.Info("filtered-engine-marker");
        host.LogInformation("filtered-host-marker");
        engine.Warning("engine-kept-marker");
        using (host.BeginScope(new Dictionary<string, object?> { ["RunId"] = "run-check" }))
            for (var i = 0; i < 48; i++) host.LogWarning("roll-marker {Index} {Payload}", i, new string('x', 32768));
        var paths = Directory.GetFiles(factory.LogDirectory, "*.log");
        Assert.False(File.Exists(expired));
        Assert.True(paths.Length >= 2);
        var messages = new List<string>();
        foreach (var path in paths)
        {
            Assert.True(new FileInfo(path).Length <= 1024 * 1024 + 40000);
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            while (reader.ReadLine() is { } line)
            {
                using var json = JsonDocument.Parse(line);
                messages.Add(json.RootElement.GetProperty("RenderedMessage").GetString()!);
                if (line.Contains("roll-marker")) Assert.Equal("run-check", json.RootElement.GetProperty("Properties").GetProperty("RunId").GetString());
            }
        }
        Assert.Single(messages, m => m.Contains("engine-kept-marker"));
        Assert.Equal(48, messages.Count(m => m.Contains("roll-marker")));
        Assert.DoesNotContain(messages, m => m.Contains("filtered-"));
    }

    private sealed class LoggingFactory : OrchestratorWebFactory
    {
        protected override bool EnableApplicationFileLogging => true;
        public string LogDirectory => Path.Combine(TempDir, "configured-logs");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:AppLog:Directory"] = LogDirectory,
                ["Logging:AppLog:Format"] = "Json",
                ["Logging:AppLog:FileSizeLimitMb"] = "1",
                ["Logging:AppLog:RetentionDays"] = "1",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
        }
    }
}
