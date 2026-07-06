using System.Text.Json;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Tests.Core;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Redact_MasksCommonSecretShapes()
    {
        var text = "PASSWORD='p@ss'; API_KEY=abc123; Authorization: Bearer token-123; " +
                   "{\"client_secret\":\"super-secret\",\"account_key\":\"acct\"}; " +
                   "value=ENC:abc123==; ref=SECRET:prod/db/password; " +
                   "raw=sas_Abcdefghijklmnopqrstuvwxyz0123456789_-ABCD";

        var redacted = SecretRedactor.Redact(text)!;

        Assert.DoesNotContain("p@ss", redacted);
        Assert.DoesNotContain("abc123", redacted);
        Assert.DoesNotContain("token-123", redacted);
        Assert.DoesNotContain("super-secret", redacted);
        Assert.DoesNotContain("acct", redacted);
        Assert.DoesNotContain("prod/db/password", redacted);
        Assert.DoesNotContain("sas_Abcdefghijklmnopqrstuvwxyz0123456789_-ABCD", redacted);
        Assert.Contains("PASSWORD='********'", redacted);
        Assert.Contains("AUTHORIZATION", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SECRET:********", redacted);
    }

    [Theory]
    [InlineData("bolt://neo4j:s3cret@db.example.com:7687", "bolt://neo4j:********@db.example.com:7687")]
    [InlineData("postgres://etl:p4ss.w0rd@10.0.0.5/analytics", "postgres://etl:********@10.0.0.5/analytics")]
    [InlineData("connect failed for mongodb://svc:top%40secret@cluster0/db retrying",
                "connect failed for mongodb://svc:********@cluster0/db retrying")]
    public void Redact_MasksUrlEmbeddedCredentials(string input, string expected)
    {
        Assert.Equal(expected, SecretRedactor.Redact(input));
    }

    [Theory]
    [InlineData("https://example.com:8080/user@domain/page")] // port + @ in path is not userinfo
    [InlineData("https://example.com/docs")]
    [InlineData("file://C:/data/output.csv")]
    [InlineData("mailto:someone@example.com")]
    public void Redact_LeavesCredentialFreeUrisIntact(string input)
    {
        Assert.Equal(input, SecretRedactor.Redact(input));
    }

    [Fact]
    public void DiagnosticsAndExecutionExceptions_RedactMessages()
    {
        var diagnostic = new Diagnostic("Connection failed PASSWORD=cleartext TOKEN=raw", 1, 1);
        var exception = new ExecutionException("Provider said CLIENT_SECRET=cleartext");

        Assert.DoesNotContain("cleartext", diagnostic.Message);
        Assert.DoesNotContain("raw", diagnostic.Message);
        Assert.DoesNotContain("cleartext", exception.Message);
    }

    [Fact]
    public void RedactValue_MasksSensitiveColumnsBeforeSerialization()
    {
        var data = new Dictionary<string, object?>
        {
            ["UserName"] = "worker",
            ["Password"] = "cleartext",
            ["Message"] = "failed with sas_token=raw"
        };

        var safe = data.ToDictionary(kv => kv.Key, kv => SecretRedactor.RedactValue(kv.Key, kv.Value));
        var json = JsonSerializer.Serialize(safe);

        Assert.Contains("worker", json);
        Assert.DoesNotContain("cleartext", json);
        Assert.DoesNotContain("raw", json);
        Assert.Contains(SecretRedactor.Mask, json);
    }
}
