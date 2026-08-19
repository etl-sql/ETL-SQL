using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using Xunit;

namespace ETL_SQL.Tests.Governance;

public sealed class CapabilityReferenceTests
{
    [Fact]
    public void IsCapabilityReference_RecognizesValidPrefixes()
    {
        Assert.True(CapabilityReference.IsCapabilityReference("CAPABILITY:sftp_key"));
        Assert.True(CapabilityReference.IsCapabilityReference("  capability:my-secret.txt  "));
        Assert.True(CapabilityReference.IsCapabilityReference("CAPABILITY:db_password"));
        Assert.False(CapabilityReference.IsCapabilityReference("SECRET:db_password"));
        Assert.False(CapabilityReference.IsCapabilityReference("/run/secrets/capabilities/key"));
        Assert.False(CapabilityReference.IsCapabilityReference(null));
        Assert.False(CapabilityReference.IsCapabilityReference(""));
    }

    [Theory]
    [InlineData("CAPABILITY:sftp_key", "sftp_key")]
    [InlineData("capability:'sftp-key-2'", "sftp-key-2")]
    [InlineData("CAPABILITY:\"gcp.service_account\"", "gcp.service_account")]
    [InlineData("  CAPABILITY:s3_access_key  ", "s3_access_key")]
    public void GetCapabilityName_ExtractsCleanHandle(string reference, string expectedName)
    {
        Assert.Equal(expectedName, CapabilityReference.GetCapabilityName(reference));
    }

    [Theory]
    [InlineData("CAPABILITY:")]
    [InlineData("CAPABILITY:''")]
    [InlineData("CAPABILITY:../escaped")]
    [InlineData("CAPABILITY:foo/bar")]
    [InlineData("CAPABILITY:foo\\bar")]
    [InlineData("CAPABILITY:foo:bar")]
    [InlineData("CAPABILITY:.")]
    [InlineData("CAPABILITY:..")]
    public void GetCapabilityName_RejectsInvalidHandles(string invalidReference)
    {
        Assert.Throws<ArgumentException>(() => CapabilityReference.GetCapabilityName(invalidReference));
    }

    [Fact]
    public void ResolvePath_ThrowsDescriptiveError_WhenCapabilityMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cap-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() =>
                CapabilityReference.ResolvePath("CAPABILITY:non_existent_key", tempRoot));

            Assert.Contains("non_existent_key", ex.Message, StringComparison.Ordinal);
            Assert.Contains("is not mounted", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolvePath_And_ResolveContent_Succeed_WhenMounted()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cap-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var keyFile = Path.Combine(tempRoot, "sftp_key.pem");
        File.WriteAllText(keyFile, "-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY-----");

        try
        {
            var resolvedPath = CapabilityReference.ResolvePath("CAPABILITY:sftp_key.pem", tempRoot);
            Assert.Equal(Path.GetFullPath(keyFile), resolvedPath);

            var content = CapabilityReference.ResolveContent("CAPABILITY:sftp_key.pem", tempRoot);
            Assert.StartsWith("-----BEGIN PRIVATE KEY-----", content, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ConnectionSecretResolver_ResolvesCapabilityPathAndContent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cap-conn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var keyPath = Path.Combine(tempRoot, "sftp_rsa");
        File.WriteAllText(keyPath, "dummy-rsa-key-material");
        var passPath = Path.Combine(tempRoot, "db_pass");
        File.WriteAllText(passPath, "super-secret-password-123\n");

        var priorEnv = Environment.GetEnvironmentVariable(CapabilityReference.EnvironmentVariable);
        Environment.SetEnvironmentVariable(CapabilityReference.EnvironmentVariable, tempRoot);

        try
        {
            var resolver = new ConnectionSecretResolver(null);
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = "sftp.example.com",
                ["KEYFILE"] = "CAPABILITY:sftp_rsa",
                ["PASSWORD"] = "CAPABILITY:db_pass"
            };

            var resolved = await resolver.ResolveOptionsAsync(options, CancellationToken.None, "SFTP");

            Assert.Equal(Path.GetFullPath(keyPath), resolved["KEYFILE"]);
            Assert.Equal("super-secret-password-123", resolved["PASSWORD"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CapabilityReference.EnvironmentVariable, priorEnv);
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Evaluator_ResolvePath_ResolvesMountedCapability()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cap-eval-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "dataset.csv");
        File.WriteAllText(filePath, "id,name\n1,Alpha\n2,Beta\n");

        var priorEnv = Environment.GetEnvironmentVariable(CapabilityReference.EnvironmentVariable);
        Environment.SetEnvironmentVariable(CapabilityReference.EnvironmentVariable, tempRoot);

        try
        {
            var context = new SystemExecutionContext();
            var resolved = context.ResolvePath("CAPABILITY:dataset.csv");
            Assert.Equal(Path.GetFullPath(filePath), resolved);

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var evalResolved = evaluator.ResolvePath("CAPABILITY:dataset.csv");
            Assert.Equal(Path.GetFullPath(filePath), evalResolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CapabilityReference.EnvironmentVariable, priorEnv);
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SecretRedactor_MasksCapabilityReferences()
    {
        var input = "CREATE CONNECTION c AS SFTP(HOST='example.com', KEYFILE='CAPABILITY:sftp_key', PASSWORD='CAPABILITY:my_pass');";
        var redacted = SecretRedactor.Redact(input);

        Assert.NotNull(redacted);
        Assert.DoesNotContain("CAPABILITY:sftp_key", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("CAPABILITY:my_pass", redacted, StringComparison.Ordinal);
        Assert.Contains("CAPABILITY:********", redacted, StringComparison.Ordinal);
    }
}
