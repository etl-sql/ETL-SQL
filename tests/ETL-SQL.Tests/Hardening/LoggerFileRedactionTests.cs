using ETL_SQL.Common;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

public class LoggerFileRedactionTests
{
    [Theory]
    [Trait("CompatBreak", "0.20.0")]
    [InlineData("Authentication {Password}")]
    [InlineData("Authentication {@Credential}")]
    [InlineData("Authentication {$ApiKey}")]
    [InlineData("Authentication {AccessToken}")]
    [InlineData("Authentication {Password,20}")]
    [InlineData("Authentication {{literal}} {Password}")]
    public void SensitiveTemplatePropertyMasksBareValueInFileAndUi(string template)
    {
        string? uiMessage = null;
        var text = Capture(logger =>
        {
            logger.OnMessage += (message, _, _) => uiMessage = message;
            logger.Info(template, "synthetic-bare-secret");
        });
        Assert.DoesNotContain("synthetic-bare-secret", text);
        Assert.DoesNotContain("synthetic-bare-secret", Assert.IsType<string>(uiMessage));
        Assert.Contains("Authentication", text);
    }

    [Fact]
    [Trait("CompatBreak", "0.20.0")]
    public void SensitivePropertyMasksOnlyItsMatchingArguments()
    {
        var text = Capture(logger => logger.Info("User {User}, password {Password}, status {Status}, repeated {Password}",
            "visible-user", "synthetic-first-secret", "visible-status", "synthetic-second-secret"));
        Assert.DoesNotContain("synthetic", text);
        Assert.Contains("visible-user", text);
        Assert.Contains("visible-status", text);
    }

    [Theory]
    [InlineData("PASSWORD='synthetic first second'; safe=visible")]
    [InlineData("{\"password\":\"synthetic first second\",\"safe\":\"visible\"}")]
    [InlineData("PASSWORD='synthetic \\'first second'; safe=visible")]
    [InlineData("PASSWORD='synthetic ''first second'; safe=visible")]
    [InlineData("{\"password\":\"synthetic \\\"first second\",\"safe\":\"visible\"}")]
    public void FileOutputRedactsCompleteQuotedValue(string value)
    {
        var text = Capture(logger => logger.Info("Input {Value}", value));
        Assert.DoesNotContain("synthetic", text);
        Assert.DoesNotContain("first", text);
        Assert.DoesNotContain("second", text);
        Assert.Contains("visible", text);
    }

    [Theory]
    [InlineData("Credentials {Value}")]
    [InlineData("Credentials {@Value}")]
    public void FileOutputRedactsNestedObjects(string template)
    {
        var text = Capture(logger => logger.Info(template, new
        {
            User = "visible",
            Password = "synthetic-object-secret",
            Nested = new[] { new { ApiKey = "synthetic-nested-secret", Detail = "PWD='synthetic quoted secret'" } }
        }));
        Assert.DoesNotContain("synthetic", text);
        Assert.Contains("visible", text);
    }

    private static string Capture(Action<LoggerService> write)
    {
        var directory = Path.Combine(Path.GetTempPath(), "etlsql-redaction-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var logger = new LoggerService { SuppressConsole = true })
            {
                logger.InitializeAppLogger(directory);
                write(logger);
            }
            return string.Join('\n', Directory.GetFiles(directory, "*.log").Select(File.ReadAllText));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
